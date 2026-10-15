"""Optional actual head motion, independent of hands, shoulder lean and camera."""
import copy
import json
import math
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest

ADAPTATIONS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADAPTATIONS))
from interaction_support import append_interaction_fields, head_yaw_feature
from cup_detection import no_cup


def pose(yaw=0.):
    points = [SimpleNamespace(x=.5, y=.5, visibility=.95, presence=.9)
              for _ in range(33)]
    for index, xy in {0: (.5 - yaw * .075, .22),
                      7: (.575, .2), 8: (.425, .2),
                      11: (.65, .4), 12: (.35, .4),
                      15: (.8, .6), 16: (.2, .6)}.items():
        points[index].x, points[index].y = xy
    return points


def rotate(points, indices, angle, center=(.5, .4)):
    cosine, sine = math.cos(angle), math.sin(angle)
    for index in indices:
        point = points[index]
        x, y = point.x - center[0], point.y - center[1]
        point.x = center[0] + x * cosine - y * sine
        point.y = center[1] + x * sine + y * cosine


class HeadYawTests(unittest.TestCase):
    def test_anatomical_right_is_positive_and_left_negative(self):
        for expected in [-1., -.5, 0., .5, 1.]:
            actual = head_yaw_feature(pose(expected))
            self.assertTrue(actual['hasHeadYaw'])
            self.assertAlmostEqual(actual['headYaw'], expected)
            self.assertEqual(actual['headYawConfidence'], .9)

    def test_independent_of_wrist_height_and_arm_spread(self):
        points = pose(.5)
        before = head_yaw_feature(points)
        points[15].x, points[15].y = .15, .1
        points[16].x, points[16].y = .9, .05
        self.assertEqual(head_yaw_feature(points), before)

    def test_translation_and_scale_do_not_change_yaw(self):
        points = pose(-.5)
        before = head_yaw_feature(points)
        for point in points:
            point.x = point.x * .55 + .1
            point.y = point.y * .55 + .1
        self.assertTrue(head_yaw_feature(points)['hasHeadYaw'])
        self.assertAlmostEqual(head_yaw_feature(points)['headYaw'], before['headYaw'])

    def test_whole_body_roll_does_not_change_yaw(self):
        points = pose(.65)
        before = head_yaw_feature(points)['headYaw']
        rotate(points, range(33), math.radians(25))
        self.assertTrue(head_yaw_feature(points)['hasHeadYaw'])
        self.assertAlmostEqual(head_yaw_feature(points)['headYaw'], before)

    def test_independent_shoulder_lean_does_not_drive_head(self):
        points = pose()
        rotate(points, (11, 12), math.radians(-30))
        self.assertTrue(head_yaw_feature(points)['hasHeadYaw'])
        self.assertAlmostEqual(head_yaw_feature(points)['headYaw'], 0.)

    def test_independent_head_roll_and_lateral_neck_shift_do_not_drive_yaw(self):
        points = pose(.5)
        before = head_yaw_feature(points)['headYaw']
        rotate(points, (0, 7, 8), math.radians(25), (.5, .2))
        for index in (0, 7, 8):
            points[index].x += .07
            points[index].y += .02
        self.assertTrue(head_yaw_feature(points)['hasHeadYaw'])
        self.assertAlmostEqual(head_yaw_feature(points)['headYaw'], before)

    def test_missing_and_low_confidence_face_or_shoulders_are_invalid(self):
        self.assertFalse(head_yaw_feature([])['hasHeadYaw'])
        for index in (0, 7, 8, 11, 12):
            for attribute in ('visibility', 'presence'):
                points = pose()
                setattr(points[index], attribute, .49)
                self.assertFalse(head_yaw_feature(points)['hasHeadYaw'])

    def test_nonfinite_and_out_of_frame_landmarks_are_invalid(self):
        for index in (0, 7, 8, 11, 12):
            for attribute, value in [('x', float('nan')), ('y', float('inf')),
                                     ('x', 1.01), ('y', -.01), ('presence', 1.1)]:
                points = pose()
                setattr(points[index], attribute, value)
                self.assertFalse(head_yaw_feature(points)['hasHeadYaw'])

    def test_degenerate_face_shoulder_and_reversed_ear_axes_are_invalid(self):
        for indices in [(7, 8), (11, 12)]:
            points = pose()
            points[indices[0]].x = points[indices[1]].x
            points[indices[0]].y = points[indices[1]].y
            self.assertFalse(head_yaw_feature(points)['hasHeadYaw'])
        points = pose()
        points[7].x, points[8].x = points[8].x, points[7].x
        self.assertFalse(head_yaw_feature(points)['hasHeadYaw'])
        points = pose()
        points[0].y = .6
        self.assertFalse(head_yaw_feature(points)['hasHeadYaw'])

    def test_packet_optional_fields_preserve_six_value_protocol_and_multiple_roles(self):
        base = {'version': 1, 'performerCount': 3, 'upperBodyOnly': True,
                'people': [dict(slot=slot, values=[.1] * 6, confidence=[.9] * 6)
                           for slot in (1, 2, 3)]}
        original = copy.deepcopy(base)
        append_interaction_fields(base, {1: pose(-.5), 2: pose(), 3: pose(.5)},
                                  {slot: no_cup() for slot in (1, 2, 3)}, 'ready')
        for before, after, expected in zip(original['people'], base['people'], (-.5, 0., .5)):
            self.assertEqual(after['values'], before['values'])
            self.assertEqual(after['confidence'], before['confidence'])
            self.assertTrue(after['hasHeadYaw'])
            self.assertAlmostEqual(after['headYaw'], expected)
        self.assertEqual(base['version'], 1)
        json.dumps(base, allow_nan=False)

    def test_optional_signal_can_be_absent_without_discarding_original_pose(self):
        packet = {'version': 1, 'people': [dict(slot=3, values=[0.] * 6, confidence=[.9] * 6)]}
        append_interaction_fields(packet, {}, {3: no_cup()}, 'disabled')
        self.assertFalse(packet['people'][0]['hasHeadYaw'])
        self.assertEqual(packet['people'][0]['headYaw'], 0.)
        self.assertEqual(packet['people'][0]['confidence'], [.9] * 6)
        json.dumps(packet, allow_nan=False)


if __name__ == '__main__':
    unittest.main()
