"""Independent local ImageSegmenter preview, separate from PoseLandmarker.

The official one-class selfie segmenter labels person as 0 and background as 255.
No image or mask is uploaded; only the existing preview travels over loopback UDP.
"""
from pathlib import Path

MODEL_URL = ('https://storage.googleapis.com/mediapipe-models/image_segmenter/'
             'selfie_segmenter/float16/latest/selfie_segmenter.tflite')
MODEL_NAME = 'selfie_segmenter.tflite'
SAMPLE_INTERVAL_SECONDS = 0.2
OUTLINE_COLOR = (255, 230, 60)  # OpenCV BGR: bright cyan.
FOREGROUND_CATEGORY = 0
BACKGROUND_CATEGORY = 255


def foreground_mask(category_mask):
    """Copy an H x W selfie category mask into a boolean foreground mask.

    Accept the singleton channel form too. Reject confidence maps and unexpected
    categories instead of accidentally calling all nonzero pixels people.
    """
    import numpy as np
    categories = np.asarray(category_mask)
    if categories.ndim == 3 and categories.shape[2] == 1:
        categories = categories[:, :, 0]
    if categories.ndim != 2 or categories.size == 0:
        raise ValueError('Expected a nonempty H x W selfie category mask')
    if categories.dtype.kind not in 'biu':
        raise ValueError('Category mask must contain integer category indices')
    if not np.all((categories == BACKGROUND_CATEGORY) | (categories == FOREGROUND_CATEGORY)):
        raise ValueError('Selfie category mask must use background=255 and person=0')
    return (categories == FOREGROUND_CATEGORY).copy()


def foreground_occupancy(mask):
    """Fraction of sampled image pixels labeled person, in the range 0..1."""
    import numpy as np
    mask = np.asarray(mask)
    if mask.ndim != 2 or mask.size == 0 or mask.dtype.kind != 'b':
        raise ValueError('Expected a nonempty H x W boolean foreground mask')
    return float(np.count_nonzero(mask) / mask.size)


def draw_outline(frame, mask, cv2):
    """Draw only foreground boundaries; preserve the camera/pose preview."""
    import numpy as np
    foreground_occupancy(mask)  # Validate before making an OpenCV image.
    h, w = frame.shape[:2]
    binary = np.asarray(mask, dtype=np.uint8) * 255
    if binary.shape != (h, w):
        binary = cv2.resize(binary, (w, h), interpolation=cv2.INTER_NEAREST)
    contours, _ = cv2.findContours(binary, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    thickness = max(2, round(max(w / 640, h / 480) * 2))
    cv2.drawContours(frame, contours, -1, (20, 20, 20), thickness + 2, cv2.LINE_AA)
    cv2.drawContours(frame, contours, -1, OUTLINE_COLOR, thickness, cv2.LINE_AA)


class SegmentationPreview:
    """Synchronous VIDEO-mode segmentation sampled at a maximum of 5 Hz.

    Inference shares RGB capture data, with an independent detector and monotonic
    timestamps. The latest sampled mask is held between samples. Missing assets
    or segmenter errors explicitly disable this optional preview, leaving pose
    tracking usable. This preview does not assign performer identity.
    """
    def __init__(self, model_path, enabled, vision, tasks_python):
        self._segmenter = None
        self._mask = None
        self._occupancy = None
        self._last_sample_time = None
        self._last_timestamp = -1
        self.status = 'disabled by --no-segmentation'
        model_path = Path(model_path)
        if not enabled:
            print('Segmentation disabled by --no-segmentation (A/B mode).')
            return
        if not model_path.is_file():
            self.status = 'disabled: missing selfie_segmenter.tflite'
            print(f'Segmentation disabled: model missing at {model_path}.')
            print(f'Download the official model from {MODEL_URL}')
            return
        try:
            options = vision.ImageSegmenterOptions(
                base_options=tasks_python.BaseOptions(model_asset_path=str(model_path)),
                running_mode=vision.RunningMode.VIDEO,
                output_category_mask=True,
                output_confidence_masks=False)
            self._segmenter = vision.ImageSegmenter.create_from_options(options)
            if self._segmenter.labels != ['selfie']:
                raise ValueError(f'Expected official one-class selfie model; labels={self._segmenter.labels}')
            self.status = 'waiting for first sample'
            print('Independent ImageSegmenter enabled; foreground outline, target 5 Hz.')
        except Exception as error:
            self._disable(error)

    @property
    def active(self):
        return self._segmenter is not None

    def _disable(self, error):
        self.status = f'disabled: {type(error).__name__}'
        print(f'Segmentation disabled after {type(error).__name__}: {error}')
        self.close()
        self._mask = None
        self._occupancy = None

    def sample(self, image, timestamp_ms, now_seconds):
        if not self.active:
            return False
        if (self._last_sample_time is not None and
                now_seconds - self._last_sample_time < SAMPLE_INTERVAL_SECONDS):
            return False
        self._last_sample_time = now_seconds
        timestamp_ms = max(self._last_timestamp + 1, timestamp_ms)
        self._last_timestamp = timestamp_ms
        try:
            result = self._segmenter.segment_for_video(image, timestamp_ms)
            if result.category_mask is None:
                raise ValueError('ImageSegmenter returned no category mask')
            self._mask = foreground_mask(result.category_mask.numpy_view())
            self._occupancy = foreground_occupancy(self._mask)
            self.status = f'foreground {self._occupancy:.1%} | target 5 Hz'
            return True
        except Exception as error:
            self._disable(error)
            return False

    def draw(self, frame, cv2):
        if self._mask is not None:
            draw_outline(frame, self._mask, cv2)
        label = f'Segmentation: {self.status}'
        cv2.putText(frame, label, (12, 51), cv2.FONT_HERSHEY_SIMPLEX, .5,
                    (20, 20, 20), 3, cv2.LINE_AA)
        cv2.putText(frame, label, (12, 51), cv2.FONT_HERSHEY_SIMPLEX, .5,
                    OUTLINE_COLOR, 1, cv2.LINE_AA)

    def close(self):
        segmenter, self._segmenter = self._segmenter, None
        if segmenter is not None:
            try:
                segmenter.close()
            except Exception as error:
                print(f'Segmentation cleanup warning: {error}')
