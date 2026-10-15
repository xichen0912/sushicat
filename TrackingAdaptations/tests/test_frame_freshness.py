"""Image age is a duration, and old controls remain six-channel compatible."""
import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from bridge import append_frame_freshness


class FrameFreshnessTests(unittest.TestCase):
    def packet(self):
        return dict(version=1, people=[dict(slot=1, values=[0.] * 6, confidence=[.9] * 6)],
                    diagnostics={})

    def test_capture_age_has_no_dependency_on_the_clock_origin(self):
        early, late = self.packet(), self.packet()
        append_frame_freshness(early, 10., 1, 10.3125)
        append_frame_freshness(late, 123456., 1, 123456.3125)
        self.assertEqual(early, late)
        self.assertTrue(early['hasCaptureAge'])
        self.assertEqual(early['captureAgeSeconds'], .3125)
        self.assertEqual(early['diagnostics']['captureAgeSeconds'], .3125)
        self.assertNotIn('captureTimestamp', early)

    def test_long_inference_age_is_not_clamped_into_a_fresh_frame(self):
        packet = self.packet()
        append_frame_freshness(packet, 10., 2, 12.)
        self.assertEqual(packet['captureAgeSeconds'], 2.)
        self.assertEqual(packet['frameSequence'], 2)
        self.assertEqual(packet['version'], 1)
        self.assertEqual(packet['people'][0]['values'], [0.] * 6)
        self.assertEqual(packet['people'][0]['confidence'], [.9] * 6)
        json.dumps(packet, allow_nan=False)


if __name__ == '__main__':
    unittest.main()
