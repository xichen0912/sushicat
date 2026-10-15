"""Exercise preserved pose controls and compare them to the pinned original."""
import ast
import importlib.util
import math
import os
from pathlib import Path
import subprocess
import random
import sys
from types import SimpleNamespace
import unittest

ADAPTATIONS = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ADAPTATIONS))
import bridge


def original_bridge_source():
    """Use an explicit oracle, source checkout, or starter's pinned Git baseline."""
    override = os.environ.get('DIGIPHANT_ORIGINAL_BRIDGE')
    if override:
        return Path(override).read_text()
    source = ADAPTATIONS.parent.parent / 'DigiPhantSource' / 'Tracking' / 'bridge.py'
    if source.is_file():
        return source.read_text()
    starter = ADAPTATIONS.parent / 'DigiPhantStarter'
    # HEAD remains the upstream pinned checkout; the working bridge is adapted.
    # Read the tracked baseline, never the changed starter/Tracking/bridge.py.
    result = subprocess.run(['git', '-C', str(starter), 'show', 'HEAD:Tracking/bridge.py'],
                            check=True, capture_output=True, text=True, timeout=15)
    return result.stdout


def sample_landmarks():
    points = [SimpleNamespace(x=.5, y=.5, visibility=.9, presence=.8)
              for _ in range(33)]
    for index, xy in {11: (.3, .3), 12: (.7, .3), 23: (.4, .7), 24: (.6, .7),
                      15: (.1, .1), 16: (.9, .5), 27: (.4, .9), 28: (.6, 1.)}.items():
        points[index].x, points[index].y = xy
    return points


class PreservedControlsTests(unittest.TestCase):
    def test_full_body_and_upper_body_features(self):
        landmarks = sample_landmarks()
        center, values, confidence = bridge.extract_features(landmarks)
        self.assertEqual(center, (.5, .7))
        expected = [.5, -.5, -.5, -.75, 0., 1.]
        for actual, target in zip(values, expected):
            self.assertAlmostEqual(actual, target)
        self.assertEqual(confidence, [.8] * 6)
        center, values, _ = bridge.extract_features(landmarks, True)
        self.assertEqual(center, (.5, .3))
        for actual, target in zip(values, [.5, -.5, .5, -.5, 0., 1.]):
            self.assertAlmostEqual(actual, target)

    def test_identity_assignment_and_ambiguous_crossing(self):
        tracker = bridge.PerformerTracker(2)
        self.assertEqual(tracker.assign([(.8, .5), (.2, .5)]), {1: 1, 2: 0})
        self.assertEqual(tracker.assign([(.22, .5), (.78, .5)]), {1: 0, 2: 1})
        tracker.positions = [(.49, .5), (.51, .5)]
        self.assertEqual(tracker.assign([(.5, .5), (.5, .5)]), {})
        tracker.reset()
        self.assertIsNone(tracker.positions)

    def test_unity_count_protocol_validation(self):
        for count in range(1, 5):
            self.assertEqual(bridge.requested_count(
                ('{"version":1,"performerCount":%d}' % count).encode()), count)
        for data in [b'{}', b'not json', b'[]', b'\xff',
                     b'{"version":2,"performerCount":3}',
                     b'{"version":1,"performerCount":true}',
                     b'{"version":1,"performerCount":0}',
                     b'{"version":1,"performerCount":5}',
                     b'{"version":1,"performerCount":"3"}']:
            with self.subTest(data=data):
                self.assertIsNone(bridge.requested_count(data))

    def test_original_source_regression_from_pinned_baseline(self):
        original_tree = ast.parse(original_bridge_source())
        adapted_tree = ast.parse((ADAPTATIONS / 'bridge.py').read_text())
        # Unsafe off-screen landmark handling and the intentionally sharper
        # preview are student fixes. Preserve the remaining instructor helpers.
        names = {'PerformerTracker', 'requested_count'}
        originals = {n.name: ast.dump(n, include_attributes=False)
                     for n in original_tree.body if getattr(n, 'name', None) in names}
        adapted = {n.name: ast.dump(n, include_attributes=False)
                   for n in adapted_tree.body if getattr(n, 'name', None) in names}
        self.assertEqual(originals, adapted)
        # The original six-value local UDP packet schema remains unchanged;
        # session/reset diagnostics are optional fields appended after creation.
        def packets(tree):
            return [ast.dump(n.value, include_attributes=False) for n in ast.walk(tree)
                    if isinstance(n, ast.Assign) and any(isinstance(t, ast.Name) and
                    t.id == 'packet' for t in n.targets)]
        self.assertEqual(packets(original_tree), packets(adapted_tree))

    def test_valid_controls_match_pinned_teacher_formula(self):
        tree = ast.parse(original_bridge_source())
        function = next(node for node in tree.body if isinstance(node, ast.FunctionDef)
                        and node.name == 'extract_features')
        scope = {'math': math}
        exec(compile(ast.Module(body=[function], type_ignores=[]), '<pinned extract_features>', 'exec'), scope)
        random_values = random.Random(15)
        for upper in (False, True):
            for _ in range(50):
                landmarks = sample_landmarks()
                for index in (15, 16, 27, 28):
                    landmarks[index].x = random_values.uniform(.05, .95)
                    landmarks[index].y = random_values.uniform(.05, .95)
                self.assertEqual(bridge.extract_features(landmarks, upper),
                                 scope['extract_features'](landmarks, upper))

    def test_offscreen_or_nonfinite_wrist_cannot_command_reverse(self):
        for upper in (False, True):
            for attribute, value in [('x', 1.1), ('x', -.1), ('y', 1.2),
                                     ('y', float('nan')), ('x', float('inf')),
                                     ('visibility', float('nan')), ('presence', 1.1)]:
                landmarks = sample_landmarks()
                setattr(landmarks[15], attribute, value)
                center, values, confidence = bridge.extract_features(landmarks, upper)
                self.assertEqual((values[0], confidence[0]), (0., 0.))
                self.assertGreater(confidence[1], 0)
                self.assertTrue(all(math.isfinite(x) for x in (*center, *values, *confidence)))

    def test_missing_or_invalid_body_landmark_does_not_seed_identity(self):
        for upper in (False, True):
            landmarks = sample_landmarks()
            landmarks[11].x = float('nan')
            _, values, confidence = bridge.extract_features(landmarks, upper)
            self.assertEqual(values, [0.] * 6)
            self.assertEqual(confidence, [0.] * 6)
            self.assertEqual(bridge.extract_features([], upper), ((0., 0.), [0.] * 6, [0.] * 6))

    def test_each_foot_channel_rejects_out_of_frame_independently(self):
        for index, channel in ((27, 2), (28, 3)):
            landmarks = sample_landmarks()
            landmarks[index].y = 1.2
            _, values, confidence = bridge.extract_features(landmarks)
            self.assertEqual((values[channel], confidence[channel]), (0., 0.))
            self.assertEqual(confidence[0], .8)

    def test_preview_sharper_bounded_and_does_not_modify_tracking_image(self):
        import cv2
        import numpy as np
        frame = np.random.default_rng(11).integers(0, 255, (720, 1280, 3), dtype=np.uint8)
        original = frame.copy()
        encoded = bridge.encode_preview(frame, cv2, 50000)
        self.assertLessEqual(len(encoded), 50000)
        decoded = cv2.imdecode(np.frombuffer(encoded, np.uint8), cv2.IMREAD_COLOR)
        self.assertEqual(decoded.shape[:2], (360, 640))
        np.testing.assert_array_equal(frame, original)
        compact = bridge.encode_preview(frame, cv2, 8000)
        self.assertLessEqual(len(compact), 8000)


if __name__ == '__main__':
    unittest.main()
