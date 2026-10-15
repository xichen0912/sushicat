"""Local semantic cup recognition, conservatively associated with right wrists.

Uses the official COCO-trained MediaPipe EfficientDet Lite0 ObjectDetector.
Cup color is not used. Frames are never saved or uploaded by this module.
"""
from dataclasses import dataclass
import math
from pathlib import Path
import threading
import time

from interaction_support import landmark_xy_quality, MIN_LANDMARK_CONFIDENCE

MODEL_NAME = 'efficientdet_lite0.tflite'
MODEL_URL = ('https://storage.googleapis.com/mediapipe-models/object_detector/'
             'efficientdet_lite0/int8/1/efficientdet_lite0.tflite')
SAMPLE_INTERVAL_SECONDS = 1. / 3.
MAX_AGE_SECONDS = .65
MIN_CUP_SCORE = .45


@dataclass(frozen=True)
class CupBox:
    x: float
    y: float
    width: float
    height: float
    score: float


def no_cup(reason='No semantic cup detected above the minimum confidence'):
    return dict(detected=False, label='cup', score=0., x=0., y=0.,
                width=0., height=0., ageSeconds=0., reason=str(reason)[:160])


def cup_boxes(detections, image_width, image_height):
    """Normalize valid semantic cup detections; reject malformed/huge boxes."""
    if (not isinstance(image_width, (int, float)) or not isinstance(image_height, (int, float))
            or not math.isfinite(image_width) or not math.isfinite(image_height)
            or image_width <= 0 or image_height <= 0):
        return []
    boxes = []
    for detection in detections or []:
        try:
            scores = [float(c.score) for c in detection.categories
                      if str(c.category_name).strip().lower() == 'cup']
            score = max(scores, default=0.)
            box = detection.bounding_box
            x0, y0 = float(box.origin_x), float(box.origin_y)
            width, height = float(box.width), float(box.height)
        except (AttributeError, TypeError, ValueError):
            continue
        if (not all(math.isfinite(v) for v in (score, x0, y0, width, height))
                or not MIN_CUP_SCORE <= score <= 1 or width <= 0 or height <= 0):
            continue
        x1, y1 = x0 + width, y0 + height
        # Detection may touch a frame edge. Require at least 70% of its area in-frame.
        left, top = max(0., x0), max(0., y0)
        right, bottom = min(image_width, x1), min(image_height, y1)
        if right <= left or bottom <= top:
            continue
        if ((right - left) * (bottom - top)) / (width * height) < .7:
            continue
        w, h = (right - left) / image_width, (bottom - top) / image_height
        if w < .008 or h < .008 or w * h < .0001 or w * h > .18:
            continue
        boxes.append(CupBox((left + right) / (2 * image_width),
                            (top + bottom) / (2 * image_height), w, h, score))
    return boxes


def _valid_box(box):
    try:
        values = (box.x, box.y, box.width, box.height, box.score)
        return (all(math.isfinite(v) for v in values)
                and 0 <= box.x <= 1 and 0 <= box.y <= 1
                and 0 < box.width <= 1 and 0 < box.height <= 1
                and MIN_CUP_SCORE <= box.score <= 1
                and box.width * box.height <= .18)
    except (AttributeError, TypeError, ValueError):
        return False


def _wrist_body(landmarks):
    points = [landmark_xy_quality(landmarks, i) for i in (11, 12, 16)]
    if any(p is None for p in points):
        return None
    if min(p[2] for p in points) < MIN_LANDMARK_CONFIDENCE:
        return None
    left, right, wrist = points
    scale = math.dist(left[:2], right[:2])
    if scale < .03:
        return None
    return wrist[0], wrist[1], scale


def _match_distance(body, box):
    """Require a suitably sized cup next to a confident right wrist."""
    x, y, scale = body
    if not (.05 <= box.width / scale <= 1.2 and .08 <= box.height / scale <= 1.8):
        return None
    edge_dx = max(abs(x - box.x) - box.width / 2, 0.)
    edge_dy = max(abs(y - box.y) - box.height / 2, 0.)
    if math.hypot(edge_dx, edge_dy) > max(.015, scale * .3):
        return None
    distance = math.hypot(x - box.x, y - box.y) / scale
    if distance > .9:
        return None
    return distance


def associate_cups(people, boxes, age_seconds):
    """Return one unambiguous fresh cup per role, never share one cup across people.

    people maps assigned Unity slot to its current MediaPipe landmarks. A cup
    equally close to two right wrists, or two similar cups near one wrist, is
    rejected rather than guessing. Missing/unconfident wrists cannot drink.
    """
    result = {slot: no_cup() for slot in people}
    if (not isinstance(age_seconds, (int, float)) or not math.isfinite(age_seconds)
            or not 0 <= age_seconds <= MAX_AGE_SECONDS):
        return {slot: no_cup('Cup detection unavailable or expired; waiting for a fresh sample') for slot in people}
    bodies = {slot: body for slot, landmarks in people.items()
              if (body := _wrist_body(landmarks)) is not None}
    owned = {slot: [] for slot in people}
    for slot in people:
        if slot not in bodies:
            result[slot] = no_cup('Right wrist or shoulders are outside the frame or below confidence')
        elif boxes:
            result[slot] = no_cup('Detected cup is too far from right wrist or has incompatible apparent size')
    for box in boxes:
        if not _valid_box(box):
            continue
        candidates = sorted((distance, slot) for slot, body in bodies.items()
                            if (distance := _match_distance(body, box)) is not None)
        if not candidates:
            continue
        if len(candidates) > 1 and candidates[1][0] - candidates[0][0] < .15:
            for _, slot in candidates:
                result[slot] = no_cup('Cup is near multiple right wrists; association is ambiguous')
            continue
        distance, slot = candidates[0]
        owned[slot].append((distance, box))
    for slot, candidates in owned.items():
        candidates.sort(key=lambda item: item[0])
        if not candidates:
            continue
        if len(candidates) > 1 and candidates[1][0] - candidates[0][0] < .15:
            result[slot] = no_cup('Multiple cups near right wrist; association is ambiguous')
            continue
        box = candidates[0][1]
        result[slot] = dict(detected=True, label='cup', score=box.score, x=box.x,
                            y=box.y, width=box.width, height=box.height,
                            ageSeconds=float(age_seconds), reason='Recognized cup associated with right wrist')
    return result


class SemanticCupDetector:
    """Independent 3 Hz object inference with a short, explicitly fresh cache."""
    def __init__(self, model_path, enabled, vision, tasks_python):
        self._detector = None
        self._boxes = []
        self._sample_time = None
        self._last_timestamp = -1
        self.inference_ms = 0.
        self.status = 'disabled by --no-cup-detection'
        path = Path(model_path)
        if not enabled:
            return
        if not path.is_file():
            self.status = 'disabled: missing efficientdet_lite0.tflite'
            print(f'Cup detector disabled: official model missing at {path}.')
            print(f'Official model: {MODEL_URL}')
            return
        try:
            options = vision.ObjectDetectorOptions(
                base_options=tasks_python.BaseOptions(model_asset_path=str(path)),
                running_mode=vision.RunningMode.VIDEO,
                max_results=8, score_threshold=MIN_CUP_SCORE,
                category_allowlist=['cup'])
            self._detector = vision.ObjectDetector.create_from_options(options)
            self.status = 'waiting for first sample'
            print('Semantic cup ObjectDetector enabled; target 3 Hz, local inference only.')
        except Exception as error:
            self._disable(error)

    @property
    def active(self):
        return self._detector is not None

    def reset(self):
        self._boxes = []
        self._sample_time = None
        if self.active:
            self.status = 'waiting for sample after reset'

    def _disable(self, error):
        self.close()
        self.status = f'disabled: {type(error).__name__}'
        print(f'Cup detector disabled after {type(error).__name__}: {error}')

    def sample(self, image, timestamp_ms, now_seconds):
        if not self.active:
            return False
        if self._sample_time is not None and now_seconds - self._sample_time < SAMPLE_INTERVAL_SECONDS:
            return False
        self._sample_time = now_seconds
        self._last_timestamp = max(self._last_timestamp + 1, timestamp_ms)
        inference_started = time.monotonic()
        try:
            detected = self._detector.detect_for_video(image, self._last_timestamp)
            self._boxes = cup_boxes(detected.detections, image.width, image.height)
            self.status = f'ready: {len(self._boxes)} cups | target 3 Hz'
            return True
        except Exception as error:
            self._disable(error)
            return False
        finally:
            self.inference_ms = (time.monotonic() - inference_started) * 1000.

    def for_people(self, people, now_seconds):
        if self._sample_time is None:
            return {slot: no_cup(self.status) for slot in people}
        age = float('inf') if self._sample_time is None else now_seconds - self._sample_time
        return associate_cups(people, self._boxes, age)

    def status_for(self, now_seconds):
        if self.active and self._sample_time is not None:
            age = now_seconds - self._sample_time
            if age > MAX_AGE_SECONDS:
                return f'stale: last cup inference {age:.1f}s ago'
        return self.status

    def draw(self, frame, cv2, now_seconds):
        age = float('inf') if self._sample_time is None else now_seconds - self._sample_time
        if 0 <= age <= MAX_AGE_SECONDS:
            h, w = frame.shape[:2]
            for box in self._boxes:
                p0 = (int((box.x - box.width / 2) * w), int((box.y - box.height / 2) * h))
                p1 = (int((box.x + box.width / 2) * w), int((box.y + box.height / 2) * h))
                cv2.rectangle(frame, p0, p1, (255, 200, 80), 2)
                cv2.putText(frame, f'cup {box.score:.2f}', p0, cv2.FONT_HERSHEY_SIMPLEX,
                            .45, (255, 200, 80), 1, cv2.LINE_AA)
        label = f'Cup detector: {self.status_for(now_seconds)}'
        cv2.putText(frame, label, (12, 76), cv2.FONT_HERSHEY_SIMPLEX, .45,
                    (20, 20, 20), 3, cv2.LINE_AA)
        cv2.putText(frame, label, (12, 76), cv2.FONT_HERSHEY_SIMPLEX, .45,
                    (255, 200, 80), 1, cv2.LINE_AA)

    def close(self):
        detector, self._detector = self._detector, None
        self._boxes = []
        self._sample_time = None
        if detector is not None:
            try:
                detector.close()
            except Exception as error:
                print(f'Cup detector cleanup warning: {error}')


class AsyncSemanticCupDetector:
    """One bounded background cup inference; pose delivery never waits for it.

    The worker owns the semantic detector. Only a completed immutable snapshot is
    exposed to the pose loop. There is no image queue: a busy worker drops optional
    samples. Reset invalidates both the snapshot and any in-flight old result.
    """
    def __init__(self, model_path, enabled, vision, tasks_python, *, detector_factory=None):
        factory = detector_factory or SemanticCupDetector
        self._engine = factory(model_path, enabled, vision, tasks_python)
        self._condition = threading.Condition()
        self._boxes = []
        self._sample_time = None
        self._status = self._engine.status
        self._generation = 0
        self._engine_generation = 0
        self._pending = None
        self._busy = False
        self._closed = False
        self._last_submitted = None
        self._inference_ms = 0.
        self._worker = None
        if self._engine.active:
            self._worker = threading.Thread(target=self._run, name='DigiPhant cup inference', daemon=True)
            self._worker.start()

    @property
    def active(self):
        with self._condition:
            return not self._closed and self._engine.active

    @property
    def inference_ms(self):
        with self._condition:
            return self._inference_ms

    def sample(self, image, timestamp_ms, now_seconds):
        with self._condition:
            if self._closed or not self._engine.active or self._busy or self._pending is not None:
                return False
            if self._last_submitted is not None and now_seconds - self._last_submitted < SAMPLE_INTERVAL_SECONDS:
                return False
            self._last_submitted = now_seconds
            self._pending = (self._generation, image, timestamp_ms, now_seconds)
            self._condition.notify()
            return True

    def _run(self):
        try:
            while True:
                with self._condition:
                    while not self._closed and self._pending is None:
                        self._condition.wait()
                    if self._closed:
                        return
                    generation, image, timestamp, captured_at = self._pending
                    self._pending = None
                    self._busy = True
                if generation != self._engine_generation:
                    self._engine.reset()
                    self._engine_generation = generation
                try:
                    self._engine.sample(image, timestamp, captured_at)
                    boxes = list(self._engine._boxes)
                    sample_time = self._engine._sample_time
                    status = self._engine.status
                    inference_ms = getattr(self._engine, 'inference_ms', 0.)
                except Exception as error:
                    self._engine.close()
                    boxes, sample_time = [], None
                    status = f'disabled: {type(error).__name__}'
                    inference_ms = 0.
                    print(f'Cup worker disabled after {type(error).__name__}: {error}')
                with self._condition:
                    self._busy = False
                    if generation == self._generation and not self._closed:
                        self._boxes, self._sample_time, self._status = boxes, sample_time, status
                        self._inference_ms = inference_ms
        finally:
            self._engine.close()

    def reset(self):
        with self._condition:
            self._generation += 1
            self._boxes = []
            self._sample_time = None
            self._last_submitted = None
            self._pending = None
            if self._engine.active:
                self._status = 'waiting for sample after reset'

    def _snapshot(self):
        with self._condition:
            return tuple(self._boxes), self._sample_time, self._status

    def for_people(self, people, now_seconds):
        boxes, sampled_at, status = self._snapshot()
        if sampled_at is None:
            return {slot: no_cup(status) for slot in people}
        age = float('inf') if sampled_at is None else now_seconds - sampled_at
        return associate_cups(people, boxes, age)

    def status_for(self, now_seconds):
        _, sampled_at, status = self._snapshot()
        if sampled_at is not None and now_seconds - sampled_at > MAX_AGE_SECONDS:
            return f'stale: last cup inference {now_seconds - sampled_at:.1f}s ago'
        return status

    def draw(self, frame, cv2, now_seconds):
        # Render a snapshot using the existing tested renderer, with no worker
        # lock held and no opportunity to invoke native inference on this thread.
        view = object.__new__(SemanticCupDetector)
        boxes, view._sample_time, view.status = self._snapshot()
        view._boxes = boxes
        view._detector = True if self.active else None
        view.draw(frame, cv2, now_seconds)

    def close(self):
        with self._condition:
            self._closed = True
            self._generation += 1
            self._pending = None
            self._boxes = []
            self._sample_time = None
            self._condition.notify_all()
        if self._worker is not None:
            self._worker.join(timeout=2.)
            if self._worker.is_alive():
                print('Cup inference is finishing; daemon worker will release its detector.')
        else:
            self._engine.close()
