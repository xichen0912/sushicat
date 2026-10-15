"""Optional upper-body interaction features; preserve the starter six channels."""
import math

MIN_LANDMARK_CONFIDENCE = .5


def landmark_xy_quality(landmarks, index):
    """Return a finite in-frame point and confidence, or None for missing joints."""
    try:
        p = landmarks[index]
        x, y = float(p.x), float(p.y)
        visibility, presence = float(p.visibility), float(p.presence)
        quality = min(visibility, presence)
    except (IndexError, AttributeError, TypeError, ValueError):
        return None
    if (not all(math.isfinite(v) for v in (x, y, visibility, presence))
            or not 0 <= x <= 1 or not 0 <= y <= 1
            or not 0 <= visibility <= 1 or not 0 <= presence <= 1):
        return None
    return x, y, quality


def left_arm_feature(landmarks):
    """Left wrist outward distance in the shoulder-line coordinate system.

    Positive means the performer's own left, independently of right-hand motion.
    Projecting on the shoulder line compensates for upper-body shoulder tilt.
    Unity subtracts its own neutral pose, as for the original six features.
    """
    empty = dict(hasLeftArm=False, leftArmExtension=0., leftArmConfidence=0.)
    points = [landmark_xy_quality(landmarks, i) for i in (11, 12, 15)]
    if any(p is None for p in points):
        return empty
    left, right, wrist = points
    quality = min(p[2] for p in points)
    if quality < MIN_LANDMARK_CONFIDENCE:
        return empty
    dx, dy = left[0] - right[0], left[1] - right[1]
    squared_width = dx * dx + dy * dy
    if squared_width < .03 ** 2:
        return empty
    extension = ((wrist[0] - left[0]) * dx + (wrist[1] - left[1]) * dy) / squared_width
    return dict(hasLeftArm=True, leftArmExtension=max(-3., min(3., extension)),
                leftArmConfidence=quality)


def head_yaw_feature(landmarks):
    """Approximate head yaw from the nose's lateral position between the ears.

    Positive means the performer's own right. Anatomical ear order is checked
    against the shoulder line, then the ear-line projection removes head/shoulder
    roll and lateral translation. This is a pose-landmark estimate, not an angle
    in degrees; near-profile or occluded faces should be treated as unavailable.
    Unity subtracts the calibrated neutral value, as for the six starter signals.
    """
    empty = dict(hasHeadYaw=False, headYaw=0., headYawConfidence=0.)
    points = [landmark_xy_quality(landmarks, i) for i in (0, 7, 8, 11, 12)]
    if any(point is None for point in points):
        return empty
    nose, left_ear, right_ear, left_shoulder, right_shoulder = points
    quality = min(point[2] for point in points)
    if quality < MIN_LANDMARK_CONFIDENCE:
        return empty

    # MediaPipe labels are anatomical: index 8 is the performer's right ear.
    ear_dx, ear_dy = right_ear[0] - left_ear[0], right_ear[1] - left_ear[1]
    shoulder_dx = right_shoulder[0] - left_shoulder[0]
    shoulder_dy = right_shoulder[1] - left_shoulder[1]
    ear_width_squared = ear_dx * ear_dx + ear_dy * ear_dy
    shoulder_width_squared = shoulder_dx * shoulder_dx + shoulder_dy * shoulder_dy
    if ear_width_squared < .018 ** 2 or shoulder_width_squared < .03 ** 2:
        return empty
    ear_width, shoulder_width = math.sqrt(ear_width_squared), math.sqrt(shoulder_width_squared)
    # Reject collapsed/implausible or reversed face axes instead of reversing yaw.
    alignment = ((ear_dx * shoulder_dx + ear_dy * shoulder_dy)
                 / (ear_width * shoulder_width))
    if alignment < .5 or not .1 <= ear_width / shoulder_width <= .9:
        return empty

    nose_dx = nose[0] - (left_ear[0] + right_ear[0]) / 2
    nose_dy = nose[1] - (left_ear[1] + right_ear[1]) / 2
    # A nose much farther than the ear span is a malformed or unreliable face.
    if math.hypot(nose_dx, nose_dy) > ear_width:
        return empty
    yaw = 2 * (nose_dx * ear_dx + nose_dy * ear_dy) / ear_width_squared
    return dict(hasHeadYaw=True, headYaw=max(-1.5, min(1.5, yaw)),
                headYawConfidence=quality)


def upper_body_geometry(landmarks):
    """Raw same-side shoulder/wrist heights for Unity's calibrated fixed scale.

    This avoids subtracting two ratios with different instantaneous shoulder
    widths. Joint coordinates also let Unity check scale changes and hand jumps
    against fresh frames. Invalid wrists are independent; a missing left wrist
    does not hide the right-hand geometry. No neutral pose is guessed here.
    """
    result = dict(present=False, leftHandVertical=0., rightHandVertical=0.,
                  shoulderWidth=0., shoulderConfidence=0.,
                  leftHandConfidence=0., rightHandConfidence=0.,
                  leftShoulderX=0., leftShoulderY=0.,
                  rightShoulderX=0., rightShoulderY=0.,
                  leftWristX=0., leftWristY=0., rightWristX=0., rightWristY=0.)
    left, right = (landmark_xy_quality(landmarks, i) for i in (11, 12))
    if left is None or right is None or min(left[2], right[2]) < MIN_LANDMARK_CONFIDENCE:
        return result
    width = math.dist(left[:2], right[:2])
    if width < .03:
        return result
    result.update(present=True, shoulderWidth=width,
                  shoulderConfidence=min(left[2], right[2]),
                  leftShoulderX=left[0], leftShoulderY=left[1],
                  rightShoulderX=right[0], rightShoulderY=right[1])
    for name, shoulder, index in [('left', left, 15), ('right', right, 16)]:
        wrist = landmark_xy_quality(landmarks, index)
        if wrist is not None and wrist[2] >= MIN_LANDMARK_CONFIDENCE:
            result[name + 'HandVertical'] = shoulder[1] - wrist[1]
            result[name + 'HandConfidence'] = min(left[2], right[2], wrist[2])
            result[name + 'WristX'] = wrist[0]
            result[name + 'WristY'] = wrist[1]
    return result


def append_interaction_fields(packet, landmarks_by_slot, cups_by_slot, cup_status,
                               fists_by_slot=None, fist_status=None):
    """Add optional interaction fields without changing the starter six-value schema.

    Fist fields are optional so an older bridge can still drive locomotion; Unity
    will keep fist-gated interactions paused until a fresh fist signal arrives.
    """
    empty_fist = dict(leftFistClosed=False, leftFistConfidence=0.,
                      rightFistClosed=False, rightFistConfidence=0.,
                      fistReason='Waiting for a fresh fist observation')
    for person in packet['people']:
        slot = person['slot']
        landmarks = landmarks_by_slot.get(slot, [])
        person.update(left_arm_feature(landmarks))
        person.update(head_yaw_feature(landmarks))
        # Fist-gated interactions also need the optional geometry in full-body
        # mode.  Movement channels remain the original six values; Unity simply
        # ignores this extra geometry for locomotion while TryReadFist can use it.
        if packet.get('upperBodyOnly') or fists_by_slot:
            person['upperBodyGeometry'] = upper_body_geometry(landmarks)
            person['upperBodyGeometry'].update((fists_by_slot or {}).get(slot, empty_fist))
        person['cup'] = cups_by_slot[slot]
    packet['cupDetectorStatus'] = str(cup_status)
    packet['fistDetectorStatus'] = str(fist_status or 'Fist detector unavailable')
