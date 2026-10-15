"""Fixed upper-body camera regions with independent one-person pose detectors.

Regions are control positions, not biometric identities. A replacement at the
same position cannot be proved from pose alone: performers must reassign and
recalibrate after changing people. Long loss or a large position jump invalidates
neutral poses automatically without requiring a permanently latched manual reset.
"""
from concurrent.futures import ThreadPoolExecutor
import itertools
import math
import time
from types import SimpleNamespace

from interaction_support import landmark_xy_quality, head_yaw_feature

DEFAULT_BOUNDARIES = [0., 1. / 3., 2. / 3., 1.]


def validate_region_configuration(boundaries, padding):
    if not isinstance(boundaries, (list, tuple)) or len(boundaries) != 4:
        raise ValueError('Use four ordered region boundaries from 0 to 1')
    if any(type(value) not in (int, float) or not math.isfinite(value) for value in boundaries):
        raise ValueError('Region boundaries must be finite numbers')
    values = [float(value) for value in boundaries]
    if values[0] != 0. or values[-1] != 1. or any(values[i + 1] - values[i] < .15 for i in range(3)):
        raise ValueError('Regions must cover 0..1 with each width at least .15')
    if type(padding) not in (int, float) or not math.isfinite(padding) or not 0 <= padding <= .15:
        raise ValueError('Region padding must be within 0..0.15')
    return values, float(padding)


def parse_region_boundaries(value):
    try:
        boundaries = [float(part) for part in value.split(',')]
        return validate_region_configuration(boundaries, .1)[0]
    except (AttributeError, ValueError) as error:
        raise ValueError('Example regions: 0,0.3333,0.6667,1') from error


def map_landmarks(landmarks, x0, y0, width, height, frame_width, frame_height):
    """Map ROI coordinates back before body geometry or cup association."""
    mapped = []
    for point in landmarks:
        in_crop = (math.isfinite(point.x) and math.isfinite(point.y)
                   and 0 <= point.x <= 1 and 0 <= point.y <= 1)
        # A wrist outside this task's crop is extrapolated even if its mapped
        # coordinates happen to lie in the full camera frame. Keep it invalid.
        mapped.append(SimpleNamespace(x=(x0 + point.x * width) / frame_width,
                                      y=(y0 + point.y * height) / frame_height,
                                      z=getattr(point, 'z', 0.) * width / frame_width,
                                      visibility=getattr(point, 'visibility', 0.) if in_crop else 0.,
                                      presence=getattr(point, 'presence', 0.) if in_crop else 0.))
    return mapped


def body_center(landmarks):
    # Coordinates can establish the position of a low-confidence detection, but
    # confidence is never promoted to a valid movement signal.
    shoulders = [landmark_xy_quality(landmarks, i) for i in (11, 12)]
    if all(point is not None for point in shoulders):
        return tuple((shoulders[0][i] + shoulders[1][i]) / 2 for i in (0, 1))
    nose = landmark_xy_quality(landmarks, 0)
    return nose[:2] if nose is not None and nose[2] >= .5 else None


def plausible_single_body(landmarks):
    """Reject a pose stitched from two adjacent people.

    With performers shoulder to shoulder, one detection can take its face from
    one person and a shoulder from the neighbour. A real upper body keeps the
    nose roughly above the shoulder midpoint (head turns and shoulder tilt move
    it well under .75 shoulder widths), so a nose far outside that is two bodies.
    """
    shoulders = [landmark_xy_quality(landmarks, i) for i in (11, 12)]
    nose = landmark_xy_quality(landmarks, 0)
    if any(point is None or point[2] < .5 for point in shoulders) or nose is None or nose[2] < .5:
        return True
    width = math.dist(shoulders[0][:2], shoulders[1][:2])
    if width < .03:
        return True
    middle = (shoulders[0][0] + shoulders[1][0]) / 2
    return abs(nose[0] - middle) <= .75 * width


def owns_center(slot, center, boundaries):
    if center is None:
        return False
    return boundaries[slot - 1] <= center[0] < boundaries[slot] or slot == 3 and center[0] == 1.


def owns_overlap(slot, center, boundaries, padding):
    """Accept a candidate from an expanded crop for global role assignment.

    The old core ownership rule is intentionally kept for the compatibility
    detector. Flexible mode uses this only to gather candidates; the role is
    assigned later from the previous frame's position, so crossing a boundary
    cannot silently change P1/P2/P3.
    """
    if center is None:
        return False
    left = max(0., boundaries[slot - 1] - padding)
    right = min(1., boundaries[slot] + padding)
    return left <= center[0] < right or slot == 3 and center[0] == 1.


def region_diagnostic(slot, landmarks, boundaries, raw_count=0, rejected_reason=None):
    result = dict(slot=slot, xMin=boundaries[slot - 1], xMax=boundaries[slot], rawDetected=raw_count,
                  shoulderReady=False, leftHandReady=False, rightHandReady=False, headReady=False,
                  reason=rejected_reason or 'No person detected in this region')
    if landmarks is None:
        return result
    points = [landmark_xy_quality(landmarks, i) for i in (11, 12)]
    result['shoulderReady'] = (all(point is not None and point[2] >= .5 for point in points)
                               and math.dist(points[0][:2], points[1][:2]) >= .03)
    for key, index in [('leftHandReady', 15), ('rightHandReady', 16)]:
        wrist = landmark_xy_quality(landmarks, index)
        result[key] = result['shoulderReady'] and wrist is not None and wrist[2] >= .5
    result['headReady'] = head_yaw_feature(landmarks)['hasHeadYaw']
    required = {1: [('shoulderReady', 'shoulders'), ('leftHandReady', 'left wrist')],
                2: [('shoulderReady', 'shoulders / steering')],
                3: [('shoulderReady', 'shoulders'), ('rightHandReady', 'right wrist'),
                    ('leftHandReady', 'left wrist / ears'), ('headReady', 'nose / both ears')]}
    missing = [name for field, name in required[slot] if not result[field]]
    result['reason'] = 'Inputs ready' if not missing else 'Waiting for confident ' + ', '.join(missing)
    return result


class LandmarkHold:
    """Bridge short per-landmark dropouts for fixed seats.

    A wrist or shoulder that briefly drops below visibility reuses its last
    confident position for up to ``hold_seconds``; a seat whose shoulders have
    been unseen for longer is reported empty. This trades a short stale value
    for not stopping the elephant on every one-frame flicker.
    """
    def __init__(self, hold_seconds=.5, visible=.5, clock=None):
        self.hold_seconds = hold_seconds
        self.visible = visible
        self.clock = clock or time.monotonic
        self.reset()

    def reset(self):
        self._held = {}

    def apply(self, slot, landmarks):
        now = self.clock()
        held = self._held.setdefault(slot, {})
        def confident(point):
            return (point is not None and math.isfinite(point.x) and math.isfinite(point.y)
                    and min(getattr(point, 'visibility', 0.), getattr(point, 'presence', 0.)) >= self.visible)
        def fresh(index):
            return index in held and now - held[index][0] <= self.hold_seconds
        if landmarks is None:
            # Whole body missing this frame: keep the seat only while both shoulders are fresh.
            if not (fresh(11) and fresh(12)):
                return None
            return [SimpleNamespace(**vars(held[i][1])) if fresh(i)
                    else SimpleNamespace(x=0., y=0., z=0., visibility=0., presence=0.) for i in range(33)]
        merged = []
        for index, point in enumerate(landmarks):
            if confident(point):
                held[index] = (now, SimpleNamespace(**vars(point)))
                merged.append(point)
            elif fresh(index):
                merged.append(SimpleNamespace(**vars(held[index][1])))
            else:
                merged.append(point)
        return merged


class RegionalRoleTracker:
    """Position-stable role assignment for candidates gathered from overlap.

    Regions are only candidate sources in flexible mode. A close or ambiguous
    crossing freezes the assignment until an explicit reset, which is safer
    than allowing a single bad frame to swap P1/P2/P3.
    """
    def __init__(self, identity_timeout=2., clock=None, maximum_jump=.2):
        self.identity_timeout = identity_timeout
        self.maximum_jump = maximum_jump
        self.clock = clock or time.monotonic
        self.reset()

    def reset(self):
        self.last_seen = {slot: None for slot in (1, 2, 3)}
        # ``assign`` (the compatibility, fixed-region API) stores a dict here;
        # ``assign_candidates`` stores a left-to-right list.  ``None`` is the
        # uncalibrated state for both APIs and avoids treating an empty dict as
        # an already calibrated flexible tracker.
        self.positions = None
        self.expired = set()
        self.ambiguous = False
        self.status = 'Regional tracking: one upper body inside each P1 / P2 / P3 region'

    def assign(self, candidates):
        if self.positions is None or not isinstance(self.positions, dict):
            self.positions = {}
        now = self.clock()
        invalidate = False
        reasons = []
        for slot in (1, 2, 3):
            center = candidates.get(slot)
            seen = self.last_seen[slot]
            if seen is not None and now - seen > self.identity_timeout and slot not in self.expired:
                self.expired.add(slot)
                invalidate = True
                reasons.append(f'P{slot} lost for over {self.identity_timeout:g}s; recalibrate')
            if center is None:
                continue
            if slot in self.positions and slot not in self.expired and math.dist(center, self.positions[slot]) > self.maximum_jump:
                invalidate = True
                reasons.append(f'P{slot} position jumped; recalibrate')
            self.positions[slot] = center
            self.last_seen[slot] = now
            self.expired.discard(slot)
        self.status = '; '.join(reasons) if reasons else f'Regions present: {len(candidates)}/3; fixed P1 / P2 / P3'
        return dict(candidates), invalidate

    def assign_candidates(self, centers):
        """Assign a de-duplicated candidate list to stable P1/P2/P3 slots.

        Candidate indices are returned so the bridge can preserve the full
        landmark set. The first frame seeds left-to-right. Later frames use a
        minimum-distance assignment and reject near ties, missing identity
        evidence, or extra people without changing existing roles.
        """
        now = self.clock()
        centers = [tuple(center) for center in (centers or []) if center is not None]
        if self.ambiguous:
            self.status = 'Regional tracking paused: ambiguous crossing or extra person; press Reassign people'
            return {}, False
        if self.positions is None or not isinstance(self.positions, list):
            if len(centers) != 3:
                self.status = f'Waiting for exactly 3 upper bodies; found {len(centers)}'
                return {}, False
            order = sorted(range(3), key=lambda index: centers[index][0])
            self.positions = [centers[index] for index in order]
            self.last_seen = {slot + 1: now for slot in range(3)}
            self.status = '3/3 assigned from left to right; flexible boundaries active'
            return {slot + 1: order[slot] for slot in range(3)}, False
        stale = [slot for slot, seen in self.last_seen.items()
                 if seen is not None and now - seen > self.identity_timeout]
        if stale:
            newly_stale = [slot for slot in stale if slot not in self.expired]
            self.expired.update(stale)
            self.status = '; '.join(f'P{slot} lost for over {self.identity_timeout:g}s; recalibrate' for slot in stale)
            return {}, bool(newly_stale)
        if len(centers) > 3:
            self.ambiguous = True
            self.status = 'Regional tracking paused: extra person or duplicate candidate; press Reassign people'
            return {}, False
        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=3):
            used = [index for index in assignment if index >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[slot], centers[index])
                         for slot, index in enumerate(assignment) if index >= 0]
            if any(distance > self.maximum_jump for distance in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        if not candidates:
            self.status = 'Regional tracking paused: no candidate matches the calibrated positions'
            return {}, False
        candidates.sort()
        best = candidates[0]
        if (len(candidates) > 1 and candidates[1][0] == best[0]
                and candidates[1][1] - best[1] < .015):
            self.ambiguous = True
            self.status = 'Regional tracking paused: ambiguous crossing; press Reassign people'
            return {}, False
        result = {slot + 1: index for slot, index in enumerate(best[2]) if index >= 0}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
            self.last_seen[slot] = now
            self.expired.discard(slot)
        self.status = f'{len(result)}/3 assigned; flexible boundaries active'
        return result, False


class RegionalPoseDetector:
    """Three persistent VIDEO instances over padded upper-body regions.

    The compatibility ``detect`` API can remain one-person-per-region. Flexible
    candidate mode may request up to four poses per crop so two people close to
    a boundary are not silently collapsed into one detection. Duplicate
    detections from adjacent crops are removed before role assignment.
    """
    def __init__(self, model_path, vision, tasks_python, mediapipe,
                 boundaries=None, padding=.1, detector_factory=None,
                 num_poses_per_crop=1):
        self.boundaries, self.padding = validate_region_configuration(boundaries or DEFAULT_BOUNDARIES, padding)
        if type(num_poses_per_crop) is not int or not 1 <= num_poses_per_crop <= 4:
            raise ValueError('num_poses_per_crop must be an integer from 1 to 4')
        self.num_poses_per_crop = num_poses_per_crop
        self.mp = mediapipe
        self._detectors = []
        self._pool = None
        factory = detector_factory or vision.PoseLandmarker.create_from_options
        try:
            for _ in range(3):
                options = vision.PoseLandmarkerOptions(
                    base_options=tasks_python.BaseOptions(model_asset_path=str(model_path)),
                    running_mode=vision.RunningMode.VIDEO, num_poses=self.num_poses_per_crop,
                    # Slightly more permissive than the global detector: ownership and the
                    # stitched-pose check still decide which body a seat uses.
                    min_pose_detection_confidence=.4, min_pose_presence_confidence=.4,
                    min_tracking_confidence=.4)
                self._detectors.append(factory(options))
            self._pool = ThreadPoolExecutor(max_workers=3, thread_name_prefix='DigiPhant region')
        except Exception:
            self.close()
            raise

    def configure(self, boundaries, padding):
        self.boundaries, self.padding = validate_region_configuration(boundaries, padding)

    def _detect_one(self, slot, rgb, timestamp, flexible=False):
        import numpy as np
        height, width = rgb.shape[:2]
        left = max(0, int((self.boundaries[slot - 1] - self.padding) * width))
        right = min(width, math.ceil((self.boundaries[slot] + self.padding) * width))
        crop = np.ascontiguousarray(rgb[:, left:right])
        image = self.mp.Image(image_format=self.mp.ImageFormat.SRGB, data=crop)
        detected = self._detectors[slot - 1].detect_for_video(image, timestamp)
        mapped = [map_landmarks(points, left, 0, right - left, height, width, height)
                  for points in detected.pose_landmarks]
        accepted = []
        reason = None
        for points in mapped:
            center = body_center(points)
            if not plausible_single_body(points):
                reason = 'Pose mixed two neighbours; sit apart inside your own region'
                continue
            if owns_center(slot, center, self.boundaries) or (
                    flexible and owns_overlap(slot, center, self.boundaries, self.padding)):
                accepted.append(points)
            else:
                reason = ('Detected body belongs to another region; not assigned twice'
                          if center is not None else 'No valid body-center coordinates')
        if not flexible and len(accepted) > 1:
            # The padded crop keeps a performer's own shoulders inside the image; a
            # neighbour also visible in it is never used. Keep the body nearest the
            # region's middle.
            middle = (self.boundaries[slot - 1] + self.boundaries[slot]) / 2
            accepted.sort(key=lambda points: abs(body_center(points)[0] - middle))
            accepted = accepted[:1]
        diagnostic_landmarks = accepted[0] if accepted else None
        diagnostic = region_diagnostic(slot, diagnostic_landmarks, self.boundaries, len(mapped), reason)
        # The compatibility detector still returns one landmark list/None.
        # Flexible candidate mode returns all accepted people from this crop.
        result = accepted if flexible and self.num_poses_per_crop > 1 else (accepted[0] if accepted else None)
        # Fixed-region mode only draws the body it uses, so a neighbour partly visible
        # in this crop is not shown as a second, overlapping "Unassigned" skeleton.
        shown = mapped if flexible else accepted
        return slot, result, shown, diagnostic

    def detect_region(self, slot, rgb, timestamp):
        """Solo role practice: only the body centred in this region, neighbours ignored."""
        _, landmarks, raw, diagnostic = self._detect_one(slot, rgb, timestamp)
        return landmarks, raw, diagnostic

    def detect(self, rgb, timestamp):
        futures = [self._pool.submit(self._detect_one, slot, rgb, timestamp) for slot in (1, 2, 3)]
        outputs = [future.result() for future in futures]
        people = {slot: landmarks for slot, landmarks, _, _ in outputs if landmarks is not None}
        displayed = [landmarks for _, _, raw, _ in outputs for landmarks in raw]
        diagnostics = [item for _, _, _, item in outputs]
        return people, displayed, diagnostics

    def detect_candidates(self, rgb, timestamp):
        """Gather overlap candidates and remove duplicate detections of one body."""
        futures = [self._pool.submit(self._detect_one, slot, rgb, timestamp, True) for slot in (1, 2, 3)]
        outputs = [future.result() for future in futures]
        displayed = [landmarks for _, _, raw, _ in outputs for landmarks in raw]
        diagnostics = [item for _, _, _, item in outputs]
        candidates = []
        # A person visible in two overlapping crops normally has nearly the
        # same shoulder center.  Merge only very close cross-crop detections;
        # distinct people standing shoulder-to-shoulder remain separate and
        # are allowed to reach the role tracker, which can freeze on ambiguity.
        duplicate_distance = .04
        for source_slot, accepted, _, _ in outputs:
            accepted_points = accepted if isinstance(accepted, list) else ([accepted] if accepted is not None else [])
            for points in accepted_points:
                center = body_center(points)
                if center is None:
                    continue
                if any(math.dist(center, body_center(existing)) < duplicate_distance
                       for existing in candidates):
                    continue
                candidates.append(points)
        return candidates, displayed, diagnostics

    def close(self):
        if self._pool is not None:
            self._pool.shutdown(wait=True, cancel_futures=True)
            self._pool = None
        for detector in self._detectors:
            detector.close()
        self._detectors.clear()
