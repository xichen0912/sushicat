"""Optional MediaPipe hand gesture recognition for closed-fist interactions.

The pose landmarker deliberately remains the source of body/role assignment.  This
module only recognizes hands and associates each detected wrist with the nearest
left/right pose wrist, so a hand crossing a region cannot silently change a role.
"""
import math
import threading
import time
import urllib.request
from pathlib import Path

from interaction_support import landmark_xy_quality

MODEL_NAME = 'gesture_recognizer.task'
MODEL_URL = ('https://storage.googleapis.com/mediapipe-models/gesture_recognizer/'
             'gesture_recognizer/float16/1/gesture_recognizer.task')
MODEL_DOWNLOAD_TIMEOUT_SECONDS = 2.5
MAX_MODEL_BYTES = 50 * 1024 * 1024
MAX_HANDS = 8  # four-person scenes can expose both hands for every performer
REBUILD_AFTER_ERRORS = 3  # consecutive failed frames before the recognizer is recreated


def _finite(value):
    try:
        return math.isfinite(float(value))
    except (TypeError, ValueError):
        return False


def _empty(reason='No fresh hand gesture detected'):
    return dict(leftFistClosed=False, leftFistConfidence=0.,
                rightFistClosed=False, rightFistConfidence=0., reason=str(reason)[:160])


def _point(point):
    try:
        x, y = float(point.x), float(point.y)
    except (AttributeError, TypeError, ValueError):
        return None
    if not (_finite(x) and _finite(y) and 0 <= x <= 1 and 0 <= y <= 1):
        return None
    return x, y


def _category(result):
    """Return (is_closed_fist, confidence) from one recognizer result list."""
    best = None
    for item in result or []:
        try:
            name = str(item.category_name or '').strip().lower().replace('-', '_').replace(' ', '_')
            score = float(item.score)
        except (AttributeError, TypeError, ValueError):
            continue
        if _finite(score) and 0 <= score <= 1 and (best is None or score > best[1]):
            best = (name, score)
    if best is None:
        return False, 0.
    return best[0] in ('closed_fist', 'fist', 'closedfist'), best[1]


def associate_gestures(people, hand_landmarks, gesture_lists):
    """Map recognizer hands to pose slots and return optional packet fields.

    ``people`` maps Unity slots to Pose Landmarker landmarks.  Matching by wrist
    position avoids relying on handedness labels, which differ for mirrored input.
    A hand farther than 1.25 shoulder widths from either wrist is discarded.
    """
    result = {slot: _empty() for slot in people}
    candidates = []
    for landmarks, gestures in zip(hand_landmarks or [], gesture_lists or []):
        if not landmarks:
            continue
        wrist = _point(landmarks[0])
        if wrist is None:
            continue
        closed, score = _category(gestures)
        candidates.append((wrist, closed, score))
    used = set()
    for wrist, closed, score in candidates:
        matches = []
        for slot, pose in people.items():
            shoulders = [landmark_xy_quality(pose, i) for i in (11, 12)]
            if any(point is None for point in shoulders):
                continue
            scale = math.dist(shoulders[0][:2], shoulders[1][:2])
            if not _finite(scale) or scale < .03:
                continue
            for side, index in (('left', 15), ('right', 16)):
                point = landmark_xy_quality(pose, index)
                if point is None:
                    continue
                distance = math.dist(wrist, point[:2]) / scale
                if distance <= 1.25 and (slot, side) not in used:
                    matches.append((distance, slot, side))
        if not matches:
            continue
        distance, slot, side = min(matches)
        # If two pose wrists are nearly equally close, do not guess which role
        # owns the hand.  The next frame can resolve it without a stale signal.
        ties = [item for item in matches if abs(item[0] - distance) < .12]
        if len(ties) > 1 and any(item[1:] != (slot, side) for item in ties):
            continue
        used.add((slot, side))
        key = side + 'Fist'
        result[slot][key + 'Closed'] = bool(closed)
        result[slot][key + 'Confidence'] = float(score)
        result[slot]['reason'] = 'Closed fist recognized' if closed else 'Hand recognized; open hand'
    return result


class FistGestureDetector:
    """Lazy GestureRecognizer wrapper; missing model disables safely."""
    def __init__(self, model_path, enabled, vision, tasks_python, download_missing=True,
                 download_timeout=MODEL_DOWNLOAD_TIMEOUT_SECONDS,
                 defer_initialization=False):
        self._recognizer = None
        self._initialized = False
        self._model_path = Path(model_path)
        self._enabled = bool(enabled)
        self._vision = vision
        self._tasks_python = tasks_python
        self._download_missing = bool(download_missing)
        self._download_timeout = download_timeout
        self.status = 'disabled by --no-fist-detection'
        self.inference_ms = 0.
        self._consecutive_errors = 0
        self._last_error = None
        if not enabled:
            self._initialized = True
            return
        if defer_initialization:
            self.status = 'gesture recognizer queued for background initialization'
            return
        self._initialize()

    def _initialize(self):
        if self._initialized:
            return
        self._initialized = True
        if not self._enabled:
            return
        try:
            download_timeout = float(self._download_timeout)
        except (TypeError, ValueError):
            download_timeout = MODEL_DOWNLOAD_TIMEOUT_SECONDS
        if not _finite(download_timeout) or download_timeout <= 0:
            download_timeout = MODEL_DOWNLOAD_TIMEOUT_SECONDS
        path = self._model_path
        downloaded = False
        if not path.is_file() and self._download_missing:
            self.status = 'downloading official gesture recognizer model'
            temporary = path.with_suffix(path.suffix + '.download')
            try:
                path.parent.mkdir(parents=True, exist_ok=True)
                deadline = time.monotonic() + download_timeout
                # urllib's timeout applies to each socket operation.  Bound the
                # total transfer as well, so an offline/slow launch fails fast.
                with urllib.request.urlopen(MODEL_URL, timeout=download_timeout) as source, temporary.open('wb') as target:
                    written = 0
                    while True:
                        if time.monotonic() >= deadline:
                            raise TimeoutError('gesture model download timed out')
                        chunk = source.read(min(1024 * 1024, MAX_MODEL_BYTES - written))
                        if not chunk:
                            break
                        written += len(chunk)
                        if written > MAX_MODEL_BYTES:
                            raise ValueError('gesture model download exceeds size limit')
                        target.write(chunk)
                if written <= 0:
                    raise ValueError('gesture model download was empty')
                temporary.replace(path)
                downloaded = True
            except Exception as error:
                try:
                    temporary.unlink()
                except OSError:
                    pass
                self.status = 'disabled: gesture recognizer model unavailable (' + str(error)[:120] + ')'
                print('Fist detection disabled: ' + self.status)
                return
        if not path.is_file():
            self.status = 'disabled: missing gesture_recognizer.task'
            print('Fist detection disabled: official model missing at ' + str(path))
            print('Official model: ' + MODEL_URL)
            return
        try:
            options = self._vision.GestureRecognizerOptions(
                base_options=self._tasks_python.BaseOptions(model_asset_path=str(path)),
                # IMAGE mode keeps no state between frames. In VIDEO mode one failed frame
                # left the graph in an error state, so every later frame failed as well.
                running_mode=self._vision.RunningMode.IMAGE,
                num_hands=MAX_HANDS,
                min_hand_detection_confidence=.5,
                min_hand_presence_confidence=.5,
                min_tracking_confidence=.5)
            self._recognizer = self._vision.GestureRecognizer.create_from_options(options)
            self.status = 'gesture recognizer ready'
        except Exception as error:
            # Do not leave a truncated/HTML response at the canonical path;
            # the next launch should retry rather than permanently reusing it.
            if downloaded:
                try:
                    path.unlink()
                except OSError:
                    pass
            self.status = 'disabled: gesture recognizer could not start (' + str(error)[:120] + ')'
            print('Fist detection disabled: ' + self.status)

    def detect(self, image, people, timestamp):
        # Do not spend a full-frame hand inference while role tracking is
        # paused or no performer was assigned.  The next assigned frame will
        # produce a fresh observation instead of carrying stale fist state.
        if not people:
            self.inference_ms = 0.
            return {}
        if not getattr(self, '_initialized', True):
            self._initialize()
        if self._recognizer is None:
            return {slot: _empty(self.status) for slot in people}
        import time
        started = time.monotonic()
        try:
            result = self._recognizer.recognize(image)
            self.inference_ms = (time.monotonic() - started) * 1000.
            self.status = 'gesture recognizer ready'
            self._consecutive_errors = 0
            return associate_gestures(people, result.hand_landmarks, result.gestures)
        except Exception as error:
            self.inference_ms = (time.monotonic() - started) * 1000.
            message = str(error)
            if message != self._last_error:
                print('Fist detection frame error: ' + message)  # full text for diagnosis
                self._last_error = message
            self._consecutive_errors += 1
            if self._consecutive_errors >= REBUILD_AFTER_ERRORS:
                # Self-heal: a recognizer that keeps failing is rebuilt on the next frame.
                self._rebuild()
                self.status = 'gesture recognizer restarting after frame errors (' + message[:100] + ')'
            else:
                self.status = 'gesture recognizer frame error (' + message[:120] + ')'
            return {slot: _empty(self.status) for slot in people}

    def _rebuild(self):
        recognizer, self._recognizer = self._recognizer, None
        self._initialized = False
        self._consecutive_errors = 0
        try:
            if recognizer is not None:
                recognizer.close()
        except Exception:
            pass

    def close(self):
        if self._recognizer is not None:
            self._recognizer.close()
            self._recognizer = None

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()


class FistGestureWorker:
    """Run fist inference on a single background thread with a bounded queue.

    The camera/pose loop must never wait for the hand recognizer.  ``submit``
    replaces a pending job when a newer pose frame arrives, so an overloaded
    recognizer cannot build an unbounded queue or consume stale camera frames.
    Results carry the pose frame sequence, assignment revision, and slot set
    used for inference.  ``poll`` only returns a result when all three still
    match the current pose packet and the result is recent enough to use.

    The detector is closed by the worker thread in ``close``.  This keeps the
    MediaPipe task's lifecycle on one thread and makes the bridge's shutdown
    path deterministic without leaking a native recognizer.
    """

    def __init__(self, detector, max_frame_lag=3, max_result_age=.30,
                 join_timeout=2.0):
        self.detector = detector
        try:
            self.max_frame_lag = max(1, int(max_frame_lag))
        except (TypeError, ValueError):
            self.max_frame_lag = 3
        try:
            self.max_result_age = max(.01, float(max_result_age))
        except (TypeError, ValueError):
            self.max_result_age = .30
        try:
            self.join_timeout = max(.05, float(join_timeout))
        except (TypeError, ValueError):
            self.join_timeout = 2.0
        self._condition = threading.Condition()
        self._pending = None
        self._result = None
        self._generation = 0
        self._closed = False
        self._thread = threading.Thread(target=self._run,
                                        name='DigiPhant-FistRecognizer',
                                        daemon=True)
        self._thread.start()

    @property
    def status(self):
        return str(getattr(self.detector, 'status', 'fist detector unavailable'))

    @property
    def inference_ms(self):
        try:
            return float(getattr(self.detector, 'inference_ms', 0.))
        except (TypeError, ValueError):
            return 0.

    @property
    def alive(self):
        return self._thread.is_alive()

    def submit(self, image, people, timestamp, frame_sequence, revision, slots=None):
        """Queue the latest assigned pose frame without waiting for inference."""
        if not people:
            self.invalidate()
            return False
        people = dict(people)
        if slots is None:
            slots = tuple(sorted(people))
        else:
            slots = tuple(sorted(slots))
        with self._condition:
            if self._closed:
                return False
            job = (image, people, int(timestamp), int(frame_sequence), int(revision), slots,
                   self._generation)
            # A single pending slot keeps latency bounded when inference is
            # slower than the camera.  A running job cannot be cancelled, but
            # its result remains revision/slot checked by poll().
            self._pending = job
            self._condition.notify()
        return True

    def invalidate(self):
        """Drop pending and completed observations after a role/reset change."""
        with self._condition:
            self._generation += 1
            self._pending = None
            self._result = None

    def poll(self, frame_sequence, revision, slots=None, now=None):
        """Return one fresh result safe for the current pose packet, or None."""
        if now is None:
            now = time.monotonic()
        expected_slots = tuple(sorted(slots or ()))
        with self._condition:
            result = self._result
            self._result = None
        if result is None:
            return None
        result_frame, result_revision, result_slots, completed_at, fields = result
        frame_delta = int(frame_sequence) - result_frame
        if (result_revision != int(revision)
                or result_slots != expected_slots
                or frame_delta < 1
                or frame_delta > self.max_frame_lag
                or now - completed_at > self.max_result_age):
            return None
        return fields

    def _run(self):
        try:
            while True:
                with self._condition:
                    while self._pending is None and not self._closed:
                        self._condition.wait()
                    if self._closed and self._pending is None:
                        break
                    job = self._pending
                    self._pending = None
                image, people, timestamp, frame_sequence, revision, slots, generation = job
                try:
                    fields = self.detector.detect(image, people, timestamp)
                except Exception as error:  # keep pose streaming after a native error
                    fields = {slot: _empty('background fist inference error: ' + str(error)[:120])
                              for slot in people}
                completed_at = time.monotonic()
                with self._condition:
                    if not self._closed and generation == self._generation:
                        self._result = (frame_sequence, revision, slots,
                                        completed_at, fields)
        finally:
            # Close native MediaPipe resources from the same thread that used
            # them.  A detector mock may omit close, so shutdown remains safe.
            try:
                close = getattr(self.detector, 'close', None)
                if close is not None:
                    close()
            except Exception:
                pass

    def close(self):
        """Stop the worker and join it; repeated calls are harmless."""
        with self._condition:
            if not self._closed:
                self._closed = True
                self._pending = None
                self._result = None
                self._condition.notify_all()
        if threading.current_thread() is not self._thread:
            self._thread.join(self.join_timeout)

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()
