import importlib.util
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import segmentation_support as segmentation


class MaskTests(unittest.TestCase):
    def test_foreground_category_and_occupancy(self):
        categories = np.array([[255, 0, 0], [255, 255, 0]], dtype=np.uint8)
        mask = segmentation.foreground_mask(categories)
        self.assertEqual(mask.dtype, np.bool_)
        self.assertEqual(segmentation.foreground_occupancy(mask), .5)
        categories[:] = 255
        self.assertEqual(segmentation.foreground_occupancy(mask), .5)

    def test_singleton_channel_and_extremes(self):
        for value, expected in ((255, 0.), (0, 1.)):
            mask = segmentation.foreground_mask(np.full((3, 4, 1), value, dtype=np.int32))
            self.assertEqual(mask.shape, (3, 4))
            self.assertEqual(segmentation.foreground_occupancy(mask), expected)

    def test_unexpected_masks_are_rejected(self):
        for categories in (np.array([[0., .6]]), np.array([[0, 2]]),
                           np.array([0, 1]), np.zeros((2, 2, 2), dtype=np.uint8),
                           np.zeros((0, 2), dtype=np.uint8)):
            with self.subTest(shape=categories.shape, dtype=categories.dtype):
                with self.assertRaises(ValueError):
                    segmentation.foreground_mask(categories)
        with self.assertRaises(ValueError):
            segmentation.foreground_occupancy(np.array([[0, 1]], dtype=np.uint8))

    @unittest.skipUnless(importlib.util.find_spec('cv2'), 'OpenCV not installed in this interpreter')
    def test_contour_output_keeps_foreground_interior(self):
        import cv2
        frame = np.full((100, 120, 3), 90, dtype=np.uint8)
        # Different mask size verifies nearest-neighbor scaling to camera size.
        mask = np.zeros((50, 60), dtype=np.bool_)
        mask[10:40, 15:45] = True
        segmentation.draw_outline(frame, mask, cv2)
        np.testing.assert_array_equal(frame[50, 60], [90, 90, 90])
        np.testing.assert_array_equal(frame[5, 5], [90, 90, 90])
        self.assertTrue(np.any(np.all(frame == segmentation.OUTLINE_COLOR, axis=2)))


class FakeSegmenter:
    def __init__(self):
        self.calls = []
        self.closed = False
        self.categories = np.array([[255, 0], [0, 255]], dtype=np.uint8)
        self.labels = ['selfie']
        self.error = None

    def segment_for_video(self, image, timestamp):
        self.calls.append((image, timestamp))
        if self.error:
            raise self.error
        return SimpleNamespace(category_mask=SimpleNamespace(numpy_view=lambda: self.categories))

    def close(self):
        self.closed = True


class SessionTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.model = Path(self.temp.name) / segmentation.MODEL_NAME
        self.model.write_bytes(b'test-model')
        self.detector = FakeSegmenter()
        self.options = []
        def options(**kwargs):
            self.options.append(kwargs)
            return kwargs
        self.vision = SimpleNamespace(
            ImageSegmenterOptions=options,
            RunningMode=SimpleNamespace(VIDEO='VIDEO'),
            ImageSegmenter=SimpleNamespace(create_from_options=lambda opts: self.detector))
        self.tasks_python = SimpleNamespace(BaseOptions=lambda **kwargs: kwargs)

    def tearDown(self):
        self.temp.cleanup()

    def create(self, enabled=True, path=None):
        return segmentation.SegmentationPreview(path or self.model, enabled,
                                                self.vision, self.tasks_python)

    def test_separate_video_segmenter_options(self):
        preview = self.create()
        opts = self.options[0]
        self.assertEqual(opts['running_mode'], 'VIDEO')
        self.assertEqual(opts['base_options']['model_asset_path'], str(self.model))
        self.assertTrue(opts['output_category_mask'])
        self.assertFalse(opts['output_confidence_masks'])
        preview.close()
        self.assertTrue(self.detector.closed)

    def test_five_hz_cap_held_mask_and_monotonic_timestamp(self):
        preview = self.create()
        self.assertTrue(preview.sample('frame-1', 1000, 10.0))
        self.assertFalse(preview.sample('frame-2', 1100, 10.1))
        self.assertIn('50.0%', preview.status)
        self.assertTrue(preview.sample('frame-3', 999, 10.201))
        self.assertEqual(self.detector.calls, [('frame-1', 1000), ('frame-3', 1001)])
        self.assertEqual(preview._occupancy, .5)
        preview.close()

    def test_missing_model_explicitly_disables(self):
        preview = self.create(path=self.model.with_name('missing.tflite'))
        self.assertFalse(preview.active)
        self.assertIn('missing', preview.status)
        self.assertEqual(self.options, [])
        self.assertFalse(preview.sample('frame', 1, 0.0))

    def test_ab_flag_bypasses_model_loading(self):
        preview = self.create(enabled=False)
        self.assertFalse(preview.active)
        self.assertIn('--no-segmentation', preview.status)
        self.assertEqual(self.options, [])

    def test_bad_mask_disables_without_killing_bridge(self):
        preview = self.create()
        self.detector.categories = np.array([[3]], dtype=np.uint8)
        self.assertFalse(preview.sample('frame', 1, 0.0))
        self.assertFalse(preview.active)
        self.assertIn('ValueError', preview.status)
        self.assertTrue(self.detector.closed)
        self.assertIsNone(preview._mask)
        self.assertFalse(preview.sample('another-frame', 2, .5))

    def test_different_model_labels_are_rejected(self):
        self.detector.labels = ['background', 'person']
        preview = self.create()
        self.assertFalse(preview.active)
        self.assertIn('ValueError', preview.status)
        self.assertTrue(self.detector.closed)


if __name__ == '__main__':
    unittest.main()
