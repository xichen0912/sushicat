"""Closed-fist association tests; no camera or gesture model required."""
from types import SimpleNamespace
import sys
import time
from unittest.mock import patch
from pathlib import Path
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from fist_detection import FistGestureDetector, FistGestureWorker, associate_gestures


def point(x, y):
    return SimpleNamespace(x=x, y=y, visibility=.95, presence=.95)


def pose(right=(.75, .5), left=(.25, .5)):
    landmarks = [point(.5, .5) for _ in range(33)]
    landmarks[11], landmarks[12] = point(.3, .3), point(.7, .3)
    landmarks[15], landmarks[16] = point(*left), point(*right)
    return landmarks


def category(name, score=.9):
    return [SimpleNamespace(category_name=name, score=score)]


class FistAssociationTests(unittest.TestCase):
    def test_closed_fist_maps_to_nearest_anatomical_right_wrist(self):
        result = associate_gestures({3: pose()}, [[point(.75, .5)]], [category('Closed_Fist', .92)])
        self.assertTrue(result[3]['rightFistClosed'])
        self.assertAlmostEqual(result[3]['rightFistConfidence'], .92)
        self.assertFalse(result[3]['leftFistClosed'])

    def test_both_hands_can_be_associated_to_one_performer(self):
        result = associate_gestures(
            {1: pose()},
            [[point(.25, .5)], [point(.75, .5)]],
            [category('Closed_Fist', .9), category('Open_Palm', .8)])
        self.assertTrue(result[1]['leftFistClosed'])
        self.assertFalse(result[1]['rightFistClosed'])
        self.assertAlmostEqual(result[1]['rightFistConfidence'], .8)

    def test_open_hand_is_available_but_not_closed(self):
        result = associate_gestures({1: pose()}, [[point(.25, .5)]], [category('Open_Palm', .88)])
        self.assertFalse(result[1]['leftFistClosed'])
        self.assertAlmostEqual(result[1]['leftFistConfidence'], .88)
        self.assertEqual(result[1]['rightFistConfidence'], 0.)

    def test_distant_hand_is_discarded(self):
        result = associate_gestures({1: pose()}, [[point(.99, .99)]], [category('Closed_Fist')])
        self.assertEqual(result[1]['leftFistConfidence'], 0.)
        self.assertEqual(result[1]['rightFistConfidence'], 0.)


class FistDetectorLifecycleTests(unittest.TestCase):
    def test_empty_assignments_skip_native_inference(self):
        calls = []
        detector = FistGestureDetector.__new__(FistGestureDetector)
        detector._recognizer = SimpleNamespace(
            recognize_for_video=lambda *_: calls.append(True), close=lambda: None)
        detector.status = 'gesture recognizer ready'
        detector.inference_ms = 12.
        self.assertEqual(detector.detect(object(), {}, 100), {})
        self.assertEqual(calls, [])
        self.assertEqual(detector.inference_ms, 0.)

    def test_frame_error_does_not_poison_later_frames_and_rebuilds(self):
        detector = FistGestureDetector.__new__(FistGestureDetector)
        detector._initialized = True
        detector._consecutive_errors = 0
        detector._last_error = None
        detector.inference_ms = 0.
        closed = []
        outcomes = [RuntimeError('Graph has errors'), None]
        def recognize(_):
            outcome = outcomes.pop(0)
            if outcome:
                raise outcome
            return SimpleNamespace(hand_landmarks=[[point(.25, .5)]], gestures=[category('Closed_Fist', .9)])
        detector._recognizer = SimpleNamespace(recognize=recognize, close=lambda: closed.append(True))
        failed = detector.detect(object(), {1: pose()}, 100)
        self.assertIn('frame error', detector.status)
        self.assertEqual(failed[1]['leftFistConfidence'], 0.)
        recovered = detector.detect(object(), {1: pose()}, 101)  # the next frame works again
        self.assertTrue(recovered[1]['leftFistClosed'])
        detector._recognizer = SimpleNamespace(recognize=lambda _: (_ for _ in ()).throw(RuntimeError('broken')),
                                               close=lambda: closed.append(True))
        for timestamp in range(3):
            detector.detect(object(), {1: pose()}, 200 + timestamp)
        self.assertIsNone(detector._recognizer)  # recreated on the next frame
        self.assertFalse(detector._initialized)
        self.assertEqual(closed, [True])

    def test_no_download_mode_fails_without_network(self):
        with self.subTest('offline model is optional'):
            with patch('fist_detection.urllib.request.urlopen', side_effect=AssertionError('network call')):
                detector = FistGestureDetector('/definitely/missing/gesture_recognizer.task',
                                                True, None, None, download_missing=False)
        self.assertIsNone(detector._recognizer)
        self.assertIn('missing', detector.status)

    def test_deferred_initialization_runs_inside_worker_thread(self):
        import threading
        detector = FistGestureDetector('/definitely/missing/gesture_recognizer.task',
                                        True, None, None, download_missing=False,
                                        defer_initialization=True)
        self.assertIn('queued', detector.status)
        calls = []

        def initialize():
            calls.append(threading.current_thread().name)
            detector._initialized = True
            detector.status = 'gesture recognizer ready'

        detector._initialize = initialize
        worker = FistGestureWorker(detector)
        worker.submit(object(), {1: pose()}, 100, 1, 1, (1,))
        deadline = time.monotonic() + .8
        while not calls and time.monotonic() < deadline:
            time.sleep(.005)
        self.assertEqual(calls, ['DigiPhant-FistRecognizer'])
        worker.close()


class FistWorkerTests(unittest.TestCase):
    def test_submit_is_non_blocking_and_close_joins_thread(self):
        import threading
        started, release, closed = threading.Event(), threading.Event(), threading.Event()

        def detect(*_):
            started.set()
            release.wait(1.)
            return {1: dict(leftFistClosed=False, leftFistConfidence=0.,
                            rightFistClosed=True, rightFistConfidence=.9)}

        detector = SimpleNamespace(status='ready', inference_ms=12., detect=detect,
                                   close=closed.set)
        worker = FistGestureWorker(detector, join_timeout=1.)
        began = time.monotonic()
        self.assertTrue(worker.submit(object(), {1: pose()}, 100, 1, 7, (1,)))
        self.assertLess(time.monotonic() - began, .1)
        self.assertTrue(started.wait(.5))
        release.set()
        deadline = time.monotonic() + .8
        result = None
        while result is None and time.monotonic() < deadline:
            result = worker.poll(2, 7, (1,))
            time.sleep(.005)
        self.assertIsNotNone(result)
        self.assertTrue(result[1]['rightFistClosed'])
        worker.close()
        self.assertFalse(worker.alive)
        self.assertTrue(closed.is_set())

    def test_revision_and_slot_mismatch_never_publishes_result(self):
        detector = SimpleNamespace(status='ready', inference_ms=0.,
                                   detect=lambda *_: {1: dict(rightFistClosed=True,
                                                              rightFistConfidence=.9)},
                                   close=lambda: None)
        worker = FistGestureWorker(detector, max_result_age=2.)
        worker.submit(object(), {1: pose()}, 100, 3, 4, (1,))
        deadline = time.monotonic() + .8
        while worker._result is None and time.monotonic() < deadline:
            time.sleep(.005)
        self.assertIsNone(worker.poll(4, 5, (1,)))
        # A mismatched result is discarded; a later matching frame cannot
        # accidentally reuse it after a role revision.
        self.assertIsNone(worker.poll(4, 4, (2,)))
        worker.close()

    def test_invalidation_discards_an_inflight_result_even_when_revision_matches(self):
        import threading
        started, release = threading.Event(), threading.Event()

        def detect(*_):
            started.set()
            release.wait(1.)
            return {1: dict(rightFistClosed=True, rightFistConfidence=.9)}

        detector = SimpleNamespace(status='ready', inference_ms=0., detect=detect,
                                   close=lambda: None)
        worker = FistGestureWorker(detector, max_result_age=2.)
        worker.submit(object(), {1: pose()}, 100, 3, 4, (1,))
        self.assertTrue(started.wait(.5))
        worker.invalidate()
        release.set()
        deadline = time.monotonic() + .8
        while worker._thread.is_alive() and worker._result is None and time.monotonic() < deadline:
            time.sleep(.005)
        self.assertIsNone(worker.poll(4, 4, (1,)))
        worker.close()


if __name__ == '__main__':
    unittest.main()
