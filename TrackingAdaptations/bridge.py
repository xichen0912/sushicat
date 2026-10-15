"""DigiPhant camera -> local Unity UDP bridge. Run with --help for setup options."""
import argparse
import errno
import itertools
import json
import math
from pathlib import Path
import socket
import time
from contextlib import ExitStack

from segmentation_support import MODEL_NAME as SEGMENTATION_MODEL_NAME, SegmentationPreview
from identity_tracking import StablePerformerTracker, TrackingSession
from interaction_support import append_interaction_fields, landmark_xy_quality
from cup_detection import MODEL_NAME as CUP_MODEL_NAME, AsyncSemanticCupDetector
from fist_detection import MODEL_NAME as FIST_MODEL_NAME, FistGestureDetector, FistGestureWorker
from region_tracking import (LandmarkHold, RegionalPoseDetector, RegionalRoleTracker, body_center,
                             parse_region_boundaries, region_diagnostic)

FEATURES = ('LeftHandHeight', 'RightHandHeight', 'LeftFootLift', 'RightFootLift', 'Lean', 'ArmSpread')
MODEL_URL = 'https://storage.googleapis.com/mediapipe-models/pose_landmarker/pose_landmarker_full/float16/latest/pose_landmarker_full.task'


def append_frame_freshness(packet, captured_at, frame_sequence, now=None):
    """Send a duration, never a Python clock value to Unity's separate clock.

    This measures processing age since camera.read returned the image; it cannot
    measure any buffering inside the camera driver. The marker preserves old
    version-1 packet compatibility without treating a missing age as zero.
    """
    if now is None:
        now = time.monotonic()
    age = max(0., now - captured_at)
    packet.update(hasCaptureAge=True, captureAgeSeconds=age, frameSequence=frame_sequence)
    packet['diagnostics']['captureAgeSeconds'] = age


def extract_features(landmarks, upper_body_only=False):
    """Body-relative measurements, with invalid joints unavailable to controls.

    An off-screen wrist can still have high MediaPipe visibility/presence. Do not
    let an extrapolated or non-finite landmark become a walk/reverse command.
    Preserve the instructor's values and confidence for valid landmarks.
    """
    checked = [landmark_xy_quality(landmarks, i) for i in range(33)]
    def xy(i):
        point = checked[i]
        return (point[0], point[1]) if point is not None else (0., 0.)
    def quality(indices):
        return min(checked[i][2] if checked[i] is not None else 0. for i in indices)
    def result(center, raw, groups):
        confidence = [quality(group) for group in groups]
        values = [max(-3, min(3, value)) if confidence[i] > 0 and math.isfinite(value)
                  else 0. for i, value in enumerate(raw)]
        return center, values, confidence
    if upper_body_only:
        ls, rs = xy(11), xy(12)
        shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
        shoulder_width = math.dist(ls, rs)
        if quality([11, 12]) <= 0 or shoulder_width < .03:
            return (0., 0.), [0.] * 6, [0.] * 6
        scale = shoulder_width
        left = (shoulder[1] - xy(15)[1]) / scale
        right = (shoulder[1] - xy(16)[1]) / scale
        # Shoulder tilt replaces hip-relative lean; leg channels use hand heights.
        raw = [left, right, left, right, (ls[1] - rs[1]) / scale,
               abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
        groups = [[11, 12, 15], [11, 12, 16], [11, 12, 15],
                  [11, 12, 16], [11, 12], [11, 12, 15, 16]]
        return result(shoulder, raw, groups)
    ls, rs, lh, rh = (xy(i) for i in (11, 12, 23, 24))
    shoulder = ((ls[0] + rs[0]) / 2, (ls[1] + rs[1]) / 2)
    hip = ((lh[0] + rh[0]) / 2, (lh[1] + rh[1]) / 2)
    base = [11, 12, 23, 24]
    torso_height = math.dist(shoulder, hip)
    if quality(base) <= 0 or torso_height < .03:
        return (0., 0.), [0.] * 6, [0.] * 6
    scale = torso_height
    groups = [base + [15], base + [16], base + [27], base + [28], base, base + [15, 16]]
    raw = [(shoulder[1] - xy(15)[1]) / scale,
           (shoulder[1] - xy(16)[1]) / scale,
           (hip[1] - xy(27)[1]) / scale,
           (hip[1] - xy(28)[1]) / scale,
           (shoulder[0] - hip[0]) / scale,
           abs(xy(15)[0] - xy(16)[0]) / (2 * scale)]
    return result(hip, raw, groups)


class PerformerTracker:
    """Small-group nearest-position assignment with ambiguity rejection.

    This is not biometric identity. Crossings/long occlusions may need manual reset.
    """
    def __init__(self, count, max_distance=.20):
        if count not in (1, 2, 3, 4):
            raise ValueError("Choose 1, 2, 3, or 4 people")
        self.count = count
        self.max_distance = max_distance
        self.positions = None

    def reset(self):
        self.positions = None

    def assign(self, centers):
        if self.positions is None:
            if len(centers) < self.count:
                return {}
            order = sorted(range(len(centers)), key=lambda i: centers[i][0])[:self.count]
            self.positions = [centers[i] for i in order]
            return {slot + 1: index for slot, index in enumerate(order)}
        candidates = []
        for assignment in itertools.product(range(-1, len(centers)), repeat=self.count):
            used = [i for i in assignment if i >= 0]
            if len(set(used)) != len(used):
                continue
            distances = [math.dist(self.positions[s], centers[i]) for s, i in enumerate(assignment) if i >= 0]
            if any(d > self.max_distance for d in distances):
                continue
            candidates.append((-len(used), sum(distances), assignment))
        candidates.sort()
        best = candidates[0]
        # Avoid arbitrary role swaps when two assignments are almost equally plausible.
        if len(candidates) > 1 and candidates[1][0] == best[0] and candidates[1][1] - best[1] < .015:
            return {}
        result = {s + 1: i for s, i in enumerate(best[2]) if i >= 0}
        for slot, index in result.items():
            self.positions[slot - 1] = centers[index]
        return result


def requested_count(data):
    """Validate Unity's local count request without changing tracking on bad input."""
    try:
        value = json.loads(data)
        if isinstance(value, dict) and value.get('version') == 1:
            count = value.get('performerCount')
            if type(count) is int and 1 <= count <= 4:
                return count
    except (ValueError, UnicodeError):
        pass
    return None


def encode_preview(frame, cv2, byte_limit=8000):
    """Fit one low-latency JPEG into a local UDP datagram, preserving aspect ratio."""
    height, width = frame.shape[:2]
    # Keep the instructor's default for callers using a default UDP buffer.
    # The actual bridge requests the larger limit after enlarging its buffer.
    byte_limit = min(50000, max(1, int(byte_limit)))
    maximum_width, maximum_height = (640, 480) if byte_limit > 8000 else (320, 240)
    scale = min(maximum_width / width, maximum_height / height, 1.)
    preview = cv2.resize(frame, (max(1, round(width * scale)), max(1, round(height * scale))))
    # The sender explicitly raises its buffer above macOS's default 9216 bytes.
    # JPEG size depends on scene detail; bound one datagram below Unity's 60000.
    # A small-buffer platform can request the legacy 8000-byte fallback.
    for _ in range(4):
        for quality in (75, 60, 45, 30):
            ok, encoded = cv2.imencode('.jpg', preview, [cv2.IMWRITE_JPEG_QUALITY, quality])
            if ok and len(encoded) <= byte_limit:
                return encoded.tobytes()
        h, w = preview.shape[:2]
        preview = cv2.resize(preview, (max(1, w // 2), max(1, h // 2)))
    return None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--people', type=int, choices=(1, 2, 3, 4), default=3)
    parser.add_argument('--no-window', action='store_true', help='Preview is displayed inside Unity instead')
    parser.add_argument('--upper-body-only', action='store_true', help='Use shoulders and hands; feet and hips may be out of frame')
    parser.add_argument('--camera', type=int, default=0)
    parser.add_argument('--port', type=int, default=5055)
    parser.add_argument('--model', type=Path, default=Path(__file__).parent / 'pose_landmarker_full.task')
    parser.add_argument('--download-model', action='store_true', help='Download the official model if missing')
    parser.add_argument('--no-segmentation', action='store_true',
                        help='Disable the separate ImageSegmenter for actual A/B testing')
    parser.add_argument('--cup-model', type=Path, default=Path(__file__).parent / CUP_MODEL_NAME,
                        help='Official EfficientDet Lite0 semantic cup detector model')
    parser.add_argument('--no-cup-detection', action='store_true',
                        help='Disable cup recognition; pose and left-arm controls remain usable')
    parser.add_argument('--fist-model', type=Path, default=Path(__file__).parent / FIST_MODEL_NAME,
                        help='MediaPipe Gesture Recognizer model used for closed-fist interactions')
    parser.add_argument('--no-fist-detection', action='store_true',
                        help='Disable fist recognition; fist-gated interactions stay paused')
    parser.add_argument('--download-fist-model', action='store_true', default=True,
                        help='Download the official fist model when it is missing (default)')
    parser.add_argument('--no-download-fist-model', action='store_false', dest='download_fist_model',
                        help='Do not download a missing fist model')
    parser.add_argument('--fist-download-timeout', type=float, default=2.5,
                        help='Total seconds allowed for a missing fist model download (default: 2.5)')
    parser.add_argument('--identity-timeout', type=float, default=2.0,
                        help='Seconds before a lost role requires explicit reassignment (default: 2)')
    parser.add_argument('--regions', default='0,0.3333333333333333,0.6666666666666666,1',
                        help='Four left-to-right normalized upper-body team region boundaries')
    parser.add_argument('--region-padding', type=float, default=.1,
                        help='Extra full-frame width around each inference crop (0..0.15)')
    parser.add_argument('--global-team-tracking', action='store_true',
                        help='Use the original global multiperson detector for comparison')
    parser.add_argument('--flexible-regions', action='store_true',
                        help='Old mode: roles follow positions across overlapping crops. '
                             'Default: each fixed region uses only the body centred inside it')
    args = parser.parse_args()
    if not 1024 <= args.port <= 65533:
        parser.error('--port must be between 1024 and 65533')
    if not math.isfinite(args.identity_timeout) or args.identity_timeout <= 0:
        parser.error('--identity-timeout must be positive and finite')
    if not math.isfinite(args.fist_download_timeout) or args.fist_download_timeout <= 0:
        parser.error('--fist-download-timeout must be positive and finite')
    try:
        boundaries = parse_region_boundaries(args.regions)
        from region_tracking import validate_region_configuration
        boundaries, padding = validate_region_configuration(boundaries, args.region_padding)
    except ValueError as error:
        parser.error(str(error))
    if not args.model.exists():
        if not args.download_model:
            parser.error('Model missing. Run again with --download-model (internet required).')
        import urllib.request
        args.model.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.model.with_suffix('.download')
        print('Downloading the official MediaPipe pose model...')
        urllib.request.urlretrieve(MODEL_URL, temporary)
        temporary.replace(args.model)
    import cv2
    import mediapipe as mp
    from mediapipe.tasks import python
    from mediapipe.tasks.python import vision

    options = vision.PoseLandmarkerOptions(
        base_options=python.BaseOptions(model_asset_path=str(args.model)),
        running_mode=vision.RunningMode.VIDEO, num_poses=4,
        min_pose_detection_confidence=.5, min_pose_presence_confidence=.5,
        min_tracking_confidence=.5)
    session = TrackingSession(args.people, args.upper_body_only, identity_timeout=args.identity_timeout)
    session.region_boundaries, session.region_padding = boundaries, padding
    tracker = session.tracker
    regional_tracker = RegionalRoleTracker(args.identity_timeout)
    # Fixed seats bridge short landmark dropouts and never reset calibration on leave/return.
    landmark_hold = LandmarkHold()
    regional_detector = None
    camera = cv2.VideoCapture(args.camera)
    if not camera.isOpened():
        raise SystemExit('Camera could not open. Check permissions or try --camera 1.')
    # Best effort only: report the actual dimensions from each returned frame.
    camera.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
    camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    preview_byte_limit = 50000
    try:
        sender.setsockopt(socket.SOL_SOCKET, socket.SO_SNDBUF, 262144)
    except OSError:
        preview_byte_limit = 8000
        print('Large preview send buffer unavailable; using compact preview.')
    try:
        sender.bind(('127.0.0.1', args.port + 1))
        sender.setblocking(False)
    except OSError:
        camera.release()
        sender.close()
        raise SystemExit('Another camera bridge is already running. Close its preview with Q first.')
    segmentation = SegmentationPreview(Path(__file__).parent / SEGMENTATION_MODEL_NAME,
                                       not args.no_segmentation, vision, python)
    cups = AsyncSemanticCupDetector(args.cup_model, not args.no_cup_detection, vision, python)
    fists = FistGestureDetector(args.fist_model, not args.no_fist_detection, vision, python,
                                download_missing=args.download_fist_model,
                                download_timeout=args.fist_download_timeout,
                                defer_initialization=True)
    fist_worker = FistGestureWorker(fists)
    edges = [(11, 12), (11, 23), (12, 24), (23, 24), (11, 13), (13, 15),
             (12, 14), (14, 16), (23, 25), (25, 27), (24, 26), (26, 28),
             (27, 29), (29, 31), (27, 31), (28, 30), (30, 32), (28, 32),
             (0, 7), (0, 8), (7, 11), (8, 12)]
    colors = [(100, 230, 100), (255, 180, 80), (100, 160, 255), (220, 100, 230)]
    previous_timestamp = -1
    last_preview = 0.0
    last_capture = None
    camera_fps = 0.
    previous_loop_ms = 0.
    frame_sequence = 0
    print('Stand side by side, all visible. IDs start left-to-right in the unmirrored preview.')
    print('R: reset identities (then recalibrate in Unity). Q: quit.')
    try:
        with ExitStack() as contexts:
            contexts.callback(fist_worker.close)
            detector = None
            while True:
                loop_started = time.monotonic()
                for _ in range(32):
                    try:
                        data, address = sender.recvfrom(1024)
                    except BlockingIOError:
                        break
                    if address == ('127.0.0.1', args.port):
                        try:
                            command = json.loads(data)
                            if session.apply_command(command):
                                args.people, args.upper_body_only = session.count, session.upper_body_only
                                tracker = session.tracker
                                regional_tracker.reset()
                                landmark_hold.reset()
                                if regional_detector is not None:
                                    regional_detector.configure(session.region_boundaries, session.region_padding)
                                cups.reset()
                                fist_worker.invalidate()
                                print('Tracking assignments changed. Set neutral pose in Unity.')
                        except (ValueError, UnicodeError):
                            pass
                ok, frame = camera.read()
                if not ok:
                    raise RuntimeError('Camera stopped delivering frames.')
                captured_at = time.monotonic()
                if last_capture is not None:
                    instant_fps = 1. / max(.001, captured_at - last_capture)
                    camera_fps = instant_fps if camera_fps == 0 else .85 * camera_fps + .15 * instant_fps
                last_capture = captured_at
                frame_sequence += 1
                rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                timestamp = max(previous_timestamp + 1, time.monotonic_ns() // 1_000_000)
                previous_timestamp = timestamp
                image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
                observations = []
                regional_mode = args.people == 3 and args.upper_body_only and not args.global_team_tracking
                # Solo P1/P2/P3 practice watches only that role's region, so teammates
                # beside the performer can neither be chosen nor block the solo person.
                solo_region = (session.solo_region if args.people == 1 and args.upper_body_only
                               and not args.global_team_tracking else 0)
                inference_started = time.monotonic()
                region_diagnostics = []
                candidate_count = 0
                if regional_mode:
                    if regional_detector is None:
                        regional_detector = RegionalPoseDetector(args.model, vision, python, mp,
                            session.region_boundaries, session.region_padding,
                            num_poses_per_crop=2)
                    # Flexible mode gathers candidates from overlapping crops,
                    # then assigns the candidates by their previous positions.
                    # This lets a performer drift across a region boundary while
                    # preventing a crossing or extra person from silently
                    # changing P1/P2/P3.
                    if args.flexible_regions and hasattr(regional_detector, 'detect_candidates'):
                        regional_candidates, display_landmarks, region_diagnostics = regional_detector.detect_candidates(rgb, timestamp)
                        candidate_pairs = [(index, body_center(points))
                                           for index, points in enumerate(regional_candidates)]
                        candidate_pairs = [(index, center) for index, center in candidate_pairs
                                           if center is not None]
                        candidate_centers = [center for _, center in candidate_pairs]
                        role_to_candidate, identity_changed = regional_tracker.assign_candidates(candidate_centers)
                        regional_people = {
                            slot: regional_candidates[candidate_pairs[index][0]]
                            for slot, index in role_to_candidate.items()
                            if 0 <= index < len(candidate_pairs)
                        }
                        candidate_count = len(candidate_pairs)
                    else:
                        # Keep older test doubles and third-party integrations
                        # working while they migrate to the candidate API.
                        regional_people, display_landmarks, region_diagnostics = regional_detector.detect(rgb, timestamp)
                        held = {slot: landmark_hold.apply(slot, regional_people.get(slot)) for slot in (1, 2, 3)}
                        regional_people = {slot: points for slot, points in held.items() if points is not None}
                        display_landmarks = list(regional_people.values())
                        centers, _ = regional_tracker.assign({slot: body_center(points) for slot, points in regional_people.items()})
                        # Each seat is its own region: leaving pauses (Unity), returning continues
                        # with the same neutral pose, so seat loss never bumps the revision.
                        identity_changed = False
                        regional_tracker.status = (f'Seats present: {len(regional_people)}/3; fixed P1 / P2 / P3 '
                                                   '(leaving pauses, returning continues)')
                        regional_people = {slot: regional_people[slot] for slot in centers if slot in regional_people}
                        candidate_centers = [body_center(points) for points in regional_people.values()]
                        candidate_count = len(candidate_centers)
                    # The timestamp loss policy invalidates calibration once;
                    # Unity will stop safely when no assigned people are sent.
                    if identity_changed:
                        session.revision += 1
                        cups.reset()
                        fist_worker.invalidate()
                    assignments = {}
                    for slot, landmarks in regional_people.items():
                        center, values, confidence = extract_features(landmarks, True)
                        if confidence[4] < .5:
                            confidence[4] = 0. # preserve slot without legitimizing weak P2 steering
                        assignments[slot] = len(observations)
                        observations.append((center, values, confidence, landmarks))
                    raw_detected = sum(item['rawDetected'] for item in region_diagnostics)
                    valid_people = (candidate_count if args.flexible_regions and hasattr(regional_detector, 'detect_candidates')
                                    else sum(item['shoulderReady'] for item in region_diagnostics))
                    session.tracker.status = regional_tracker.status
                elif solo_region:
                    if regional_detector is None:
                        regional_detector = RegionalPoseDetector(args.model, vision, python, mp,
                            session.region_boundaries, session.region_padding,
                            num_poses_per_crop=2)
                    landmarks, display_landmarks, diagnostic = regional_detector.detect_region(solo_region, rgb, timestamp)
                    region_diagnostics = [diagnostic]
                    landmarks = landmark_hold.apply(solo_region, landmarks)
                    display_landmarks = [landmarks] if landmarks is not None else []
                    present = {1: body_center(landmarks)} if landmarks is not None else {}
                    centers, _ = regional_tracker.assign(present)  # a solo seat keeps its neutral on return
                    assignments = {}
                    if landmarks is not None and 1 in centers:
                        center, values, confidence = extract_features(landmarks, True)
                        assignments[1] = 0
                        observations.append((center, values, confidence, landmarks))
                    raw_detected, valid_people = len(display_landmarks), int(diagnostic['shoulderReady'])
                    candidate_count = len(assignments)
                    regional_tracker.status = (f'Solo P{solo_region}: only the P{solo_region} region is tracked'
                                               if assignments else f'Solo P{solo_region}: sit inside the P{solo_region} region')
                    session.tracker.status = regional_tracker.status
                else:
                    if detector is None:
                        detector = contexts.enter_context(vision.PoseLandmarker.create_from_options(options))
                    result = detector.detect_for_video(image, timestamp)
                    display_landmarks = list(result.pose_landmarks)
                    for landmarks in display_landmarks:
                        center, values, confidence = extract_features(landmarks, args.upper_body_only)
                        if confidence[4] >= .5:
                            observations.append((center, values, confidence, landmarks))
                    assignments = tracker.assign([o[0] for o in observations])
                    raw_detected, valid_people = len(display_landmarks), len(observations)
                inference_ms = (time.monotonic() - inference_started) * 1000.
                packet = {'version': 1, 'performerCount': args.people, 'upperBodyOnly': args.upper_body_only, 'people': [dict(slot=slot, values=observations[i][1], confidence=observations[i][2])
                                                 for slot, i in assignments.items()]}
                now = time.monotonic()
                assigned_landmarks = {slot: observations[i][3] for slot, i in assignments.items()}
                # Cup boxes are a short-lived inference cache from an earlier
                # original frame, associated conservatively with current wrists.
                # They are not a real-time identity or physical contact claim.
                current_slots = tuple(sorted(assigned_landmarks))
                # Poll only a completed background result.  The worker checks
                # assignment revision/slots and a short frame/clock age so a
                # crossed performer can never inherit another role's fist.
                fist_fields = fist_worker.poll(frame_sequence, session.revision,
                                               current_slots, now)
                append_interaction_fields(packet, assigned_landmarks,
                                          cups.for_people(assigned_landmarks, now), cups.status_for(now),
                                          fist_fields, fist_worker.status)
                session.append_protocol(packet)
                rejected = [f"P{item['slot']}: {item['reason']}" for item in region_diagnostics if item['reason'] != 'Inputs ready']
                if not regional_mode and not solo_region and valid_people < raw_detected:
                    rejected.append('Some detected bodies lack confident in-frame shoulders/torso')
                packet['diagnostics'] = dict(rawDetected=raw_detected, validPeople=valid_people,
                    assignedPeople=len(assignments), captureWidth=frame.shape[1], captureHeight=frame.shape[0],
                    cameraFps=camera_fps, poseInferenceMs=inference_ms, loopMs=previous_loop_ms,
                    cupInferenceMs=getattr(cups, 'inference_ms', 0.), fistInferenceMs=fist_worker.inference_ms,
                    frameSequence=frame_sequence,
                    regionalTracking=regional_mode or bool(solo_region), rejectionReasons='; '.join(rejected), regions=region_diagnostics,
                    candidatePeople=(candidate_count if regional_mode or solo_region else len(observations)),
                    trackerStatus=(regional_tracker.status if regional_mode or solo_region else tracker.status),
                    trackerAmbiguous=(regional_tracker.ambiguous if regional_mode else False))
                # Stamp age after pose / association work, immediately before delivery.
                append_frame_freshness(packet, captured_at, frame_sequence)
                sender.sendto(json.dumps(packet, allow_nan=False).encode(), ('127.0.0.1', args.port))
                # Queue optional hand inference only after the current pose
                # packet is on the wire.  This is non-blocking; a running
                # recognizer may finish later and poll() validates the result.
                if assigned_landmarks:
                    fist_worker.submit(image, assigned_landmarks, timestamp,
                                       frame_sequence, session.revision, current_slots)
                else:
                    fist_worker.invalidate()
                # Continue optional preview/segmentation work after pose delivery.
                segmentation.sample(image, timestamp, time.monotonic())
                # Freshness starts when this image was captured, rather than when
                # a slower optional inference eventually finishes.
                cups.sample(image, timestamp, timestamp / 1000.)
                segmentation.draw(frame, cv2)
                cups.draw(frame, cv2, time.monotonic())
                h, w = frame.shape[:2]
                # Draw every detection, even while waiting for the full group or
                # rejecting an ambiguous identity. Only assigned bodies drive Unity.
                slots = {id(observations[i][3]): slot for slot, i in assignments.items()}
                thickness = max(2, round(max(w / 320, h / 240) * 2))
                radius = max(3, round(thickness * 1.5))
                for landmarks in display_landmarks:
                    slot = slots.get(id(landmarks))
                    color = colors[slot - 1] if slot is not None else (180, 180, 180)
                    points = {}
                    for index, landmark in enumerate(landmarks):
                        if (min(landmark.visibility, landmark.presence) >= .5
                                and math.isfinite(landmark.x) and math.isfinite(landmark.y)
                                and 0 <= landmark.x <= 1 and 0 <= landmark.y <= 1):
                            points[index] = (min(w - 1, int(landmark.x * w)),
                                             min(h - 1, int(landmark.y * h)))
                    for a, b in edges:
                        if a in points and b in points:
                            cv2.line(frame, points[a], points[b], (20, 20, 20), thickness + 2, cv2.LINE_AA)
                            cv2.line(frame, points[a], points[b], color, thickness, cv2.LINE_AA)
                    for point in points.values():
                        cv2.circle(frame, point, radius + 1, (20, 20, 20), -1, cv2.LINE_AA)
                        cv2.circle(frame, point, radius, color, -1, cv2.LINE_AA)
                    anchor = points.get(11, next(iter(points.values()), None))
                    if anchor is not None:
                        label = f'P{solo_region or slot}' if slot is not None else 'Unassigned'
                        font_scale = max(.6, thickness * .3)
                        cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    (20, 20, 20), thickness + 2, cv2.LINE_AA)
                        cv2.putText(frame, label, anchor, cv2.FONT_HERSHEY_SIMPLEX, font_scale,
                                    color, thickness, cv2.LINE_AA)
                message = f'Raw {raw_detected} | valid shoulders {valid_people} | assigned {len(assignments)}/{args.people}'
                if regional_mode:
                    for slot in (1, 2, 3):
                        left = int(session.region_boundaries[slot - 1] * w)
                        right = min(w - 1, int(session.region_boundaries[slot] * w))
                        cv2.rectangle(frame, (left, 0), (right, h - 1), colors[slot - 1], 2)
                        cv2.putText(frame, f'P{slot} region', (left + 8, h - 40),
                                    cv2.FONT_HERSHEY_SIMPLEX, .6, colors[slot - 1], 2, cv2.LINE_AA)
                if regional_mode or solo_region:
                    for item in region_diagnostics:
                        if item['reason'] != 'Inputs ready':
                            x = int(item['xMin'] * w) + 8
                            cv2.putText(frame, item['reason'][:35], (x, h - 16), cv2.FONT_HERSHEY_SIMPLEX,
                                        .4, colors[item['slot'] - 1], 1, cv2.LINE_AA)
                if solo_region:
                    left = int(session.region_boundaries[solo_region - 1] * w)
                    right = min(w - 1, int(session.region_boundaries[solo_region] * w))
                    cv2.rectangle(frame, (left, 0), (right, h - 1), colors[solo_region - 1], 2)
                    cv2.putText(frame, f'Solo P{solo_region} region', (left + 8, h - 40),
                                cv2.FONT_HERSHEY_SIMPLEX, .6, colors[solo_region - 1], 2, cv2.LINE_AA)
                elif not regional_mode and tracker.reset_required:
                    message = tracker.status
                    reset_hint = ('Unity: Reassign people, then set neutral pose' if args.no_window
                                  else 'R: reassign, then set neutral pose in Unity | Q: quit')
                    cv2.putText(frame, reset_hint, (12, 50), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
                elif not regional_mode and tracker.positions is None:
                    message = tracker.status if args.no_window else tracker.status + ' | Stand side by side | Q: quit'
                cv2.putText(frame, message, (12, 26), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
                cv2.putText(frame, f'Capture {w}x{h} | {camera_fps:.1f} FPS | pose {inference_ms:.0f} ms',
                            (12, 102), cv2.FONT_HERSHEY_SIMPLEX, .45, (255, 255, 255), 1, cv2.LINE_AA)
                now = time.monotonic()
                if now - last_preview >= .1:
                    jpeg = encode_preview(frame, cv2, preview_byte_limit)
                    if jpeg is not None:
                        try:
                            sender.sendto(jpeg, ('127.0.0.1', args.port + 2))
                        except OSError as error:
                            # A dropped preview must not stop performer tracking.
                            if error.errno not in (errno.EMSGSIZE, errno.EAGAIN, errno.EWOULDBLOCK, errno.ENOBUFS):
                                raise
                            if error.errno == errno.EMSGSIZE:
                                preview_byte_limit = 8000
                    last_preview = now
                if not args.no_window:
                    cv2.imshow('DigiPhant tracking (unmirrored)', frame)
                key = (cv2.waitKey(1) & 0xff) if not args.no_window else -1
                if key == ord('q'):
                    break
                if key == ord('r'):
                    session.reset_assignments()
                    regional_tracker.reset()
                    landmark_hold.reset()
                    cups.reset()
                    fist_worker.invalidate()
                    print('Identities reset. Set neutral pose again in Unity before controlling.')
                previous_loop_ms = (time.monotonic() - loop_started) * 1000.
    finally:
        if regional_detector is not None:
            regional_detector.close()
        segmentation.close()
        cups.close()
        sender.sendto(b'{"version":1,"people":[]}', ('127.0.0.1', args.port))
        sender.close()
        camera.release()
        cv2.destroyAllWindows()


if __name__ == '__main__':
    main()
