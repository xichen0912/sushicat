"""Reproduce a blocked cup model and verify poses never wait for that model."""
from pathlib import Path
import sys
import threading
import time
from types import SimpleNamespace
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from cup_detection import AsyncSemanticCupDetector, CupBox


class BlockingDetector:
    def __init__(self, *args):
        self.active = True
        self.status = 'waiting'
        self.started = threading.Event()
        self.release = threading.Event()
        self._boxes = []
        self._sample_time = None
        self.resets = 0
        self.closes = 0

    def sample(self, image, timestamp, captured_at):
        self.started.set()
        if not self.release.wait(2.):
            raise RuntimeError('Test detector was not released')
        self._boxes = [CupBox(.75, .5, .08, .12, .9)]
        self._sample_time = captured_at
        self.status = 'ready'
        return True

    def reset(self):
        self.resets += 1
        self._boxes = []
        self._sample_time = None

    def close(self):
        self.closes += 1
        self.active = False


def wait_for(predicate):
    deadline = time.monotonic() + 2.
    while time.monotonic() < deadline:
        if predicate():
            return
        time.sleep(.005)
    raise AssertionError('Cup worker did not finish within the test deadline')


def wrist_pose():
    points = [SimpleNamespace(x=.5, y=.5, visibility=.9, presence=.9)
              for _ in range(33)]
    points[11].x, points[12].x, points[16].x = .6, .4, .75
    return points


class AsyncCupTests(unittest.TestCase):
    def setUp(self):
        self.engine = BlockingDetector()
        self.detector = AsyncSemanticCupDetector('unused', True, None, None,
                                                detector_factory=lambda *args: self.engine)

    def tearDown(self):
        self.engine.release.set()
        self.detector.close()

    def test_slow_inference_does_not_block_pose_thread_or_accumulate_images(self):
        self.assertTrue(self.detector.sample(object(), 1000, 1.))
        self.assertTrue(self.engine.started.wait(1.))
        # These calls finish while the native model is still blocked.
        self.assertFalse(self.detector.sample(object(), 1400, 1.4))
        self.assertEqual(self.detector._pending, None)
        self.assertFalse(self.detector.for_people({1: wrist_pose()}, 1.4)[1]['detected'])
        self.assertEqual(self.detector.status_for(1.4), 'waiting')
        self.engine.release.set()
        wait_for(lambda: not self.detector._busy)
        self.assertTrue(self.detector.for_people({1: wrist_pose()}, 1.5)[1]['detected'])

    def test_reset_discards_inflight_previous_persons_cup(self):
        self.detector.sample(object(), 1000, 1.)
        self.assertTrue(self.engine.started.wait(1.))
        self.detector.reset()
        self.engine.release.set()
        wait_for(lambda: not self.detector._busy)
        self.assertFalse(self.detector.for_people({1: wrist_pose()}, 1.2)[1]['detected'])
        self.assertIn('after reset', self.detector.status_for(1.2))
        self.assertTrue(self.detector.sample(object(), 1400, 1.4))
        wait_for(lambda: not self.detector._busy and self.detector._pending is None)
        self.assertEqual(self.engine.resets, 1)
        self.assertTrue(self.detector.for_people({1: wrist_pose()}, 1.5)[1]['detected'])

    def test_capture_age_includes_time_spent_inference(self):
        self.detector.sample(object(), 1000, 1.)
        self.engine.release.set()
        wait_for(lambda: not self.detector._busy and self.detector._pending is None)
        self.assertFalse(self.detector.for_people({1: wrist_pose()}, 1.7)[1]['detected'])
        self.assertIn('stale', self.detector.status_for(1.7))

    def test_close_releases_native_model_and_rejects_more_samples(self):
        self.detector.close()
        self.assertFalse(self.detector.active)
        self.assertFalse(self.detector.sample(object(), 1000, 1.))
        self.assertFalse(self.detector._worker.is_alive())
        self.assertEqual(self.engine.closes, 1)

    def test_submit_rate_bounded_to_three_hz(self):
        self.engine.release.set()
        self.assertTrue(self.detector.sample(object(), 1000, 1.))
        wait_for(lambda: not self.detector._busy and self.detector._pending is None)
        self.assertFalse(self.detector.sample(object(), 1100, 1.1))
        self.assertTrue(self.detector.sample(object(), 1400, 1.4))


if __name__ == '__main__':
    unittest.main()
