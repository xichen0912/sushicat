"""Restore omitted instructor assets without overwriting student adaptations."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import urllib.error
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
MODEL_URL = ('https://storage.googleapis.com/mediapipe-models/image_segmenter/'
             'selfie_segmenter/float16/latest/selfie_segmenter.tflite')
CUP_MODEL_URL = ('https://storage.googleapis.com/mediapipe-models/object_detector/'
                 'efficientdet_lite0/int8/1/efficientdet_lite0.tflite')
FIST_MODEL_URL = ('https://storage.googleapis.com/mediapipe-models/gesture_recognizer/'
                  'gesture_recognizer/float16/1/gesture_recognizer.task')


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def download(url, target):
    """urllib first; fall back to the system curl when Python has no CA bundle
    (common with python.org builds on macOS before Install Certificates.command)."""
    try:
        urllib.request.urlretrieve(url, target)
    except urllib.error.URLError as error:
        if shutil.which('curl') is None:
            raise
        print(f'urllib download failed ({error.reason}); retrying with curl.', flush=True)
        subprocess.run(['curl', '-fsSL', '-o', str(target), url], check=True)


def copy_missing(source, target):
    if source.is_dir():
        target.mkdir(parents=True, exist_ok=True)
        for item in source.iterdir():
            copy_missing(item, target / item.name)
    elif not target.exists():
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    elif sha256(source) != sha256(target):
        raise RuntimeError(f'Existing asset differs; preserved, not overwritten: {target}')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, help='Optional existing exact-revision local starter clone')
    parser.add_argument('--skip-env', action='store_true', help='Asset/static reconstruction only')
    parser.add_argument('--segmenter-model', type=Path, help='Optional existing exact-checksum model')
    parser.add_argument('--cup-detector-model', type=Path, help='Optional existing exact-checksum cup ObjectDetector model')
    parser.add_argument('--no-download-cup-model', action='store_true',
                        help='Keep restore offline when the optional cup detector model is absent')
    parser.add_argument('--fist-detector-model', type=Path,
                        help='Optional local MediaPipe Gesture Recognizer model; no network download is attempted by restore')
    args = parser.parse_args()
    lock = json.loads((ROOT / 'starter-lock.json').read_text())
    target = ROOT / 'DigiPhantStarter'
    if not target.exists():
        subprocess.run(['git', 'clone', str(args.source or lock['url']), str(target)], check=True)
        subprocess.run(['git', '-C', str(target), 'checkout', '--detach', lock['commit']], check=True)
        subprocess.run(['git', '-C', str(target), 'remote', 'set-url', 'origin', lock['url']], check=True)
    revision = subprocess.check_output(['git', '-C', str(target), 'rev-parse', 'HEAD'], text=True).strip()
    if revision != lock['commit']:
        raise RuntimeError('Existing starter revision differs. Resolve it explicitly; no automatic reset.')
    fingerprints = json.loads((target / 'asset-checksums.json').read_text())
    for name, expected in fingerprints.items():
        if name.startswith('Elephant/') or name == 'Elephant.meta' or name == 'Tracking/pose_landmarker_full.task':
            if sha256(target / name) != expected:
                raise RuntimeError(f'Starter fingerprint mismatch: {name}')
    copy_missing(target / 'Elephant', ROOT / 'Assets' / 'Elephant')
    copy_missing(target / 'Elephant.meta', ROOT / 'Assets' / 'Elephant.meta')
    tracking = target / 'Tracking'
    model = tracking / 'selfie_segmenter.tflite'
    model_lock = json.loads((ROOT / 'segmenter-lock.json').read_text())
    if not model.exists():
        temp = model.with_suffix('.download')
        if args.segmenter_model:
            shutil.copy2(args.segmenter_model, temp)
        else:
            download(MODEL_URL, temp)
        if sha256(temp) != model_lock['sha256']:
            temp.unlink()
            raise RuntimeError('Model at upstream latest has changed. Restore the pinned checksum model or review a new version.')
        temp.replace(model)
    if sha256(model) != model_lock['sha256']:
        raise RuntimeError('Existing segmenter model checksum differs; file preserved.')
    cup_lock = json.loads((ROOT / 'cup-detector-lock.json').read_text())
    if (cup_lock.get('model') != 'efficientdet_lite0.tflite'
            or cup_lock.get('task') != 'MediaPipe ObjectDetector'
            or cup_lock.get('category_allowlist') != ['cup']
            or cup_lock.get('url') != CUP_MODEL_URL
            or not isinstance(cup_lock.get('bytes'), int) or cup_lock['bytes'] <= 0
            or len(cup_lock.get('sha256', '')) != 64
            or any(char not in '0123456789abcdef' for char in cup_lock['sha256'])):
        raise RuntimeError('Cup detector lock differs from the official semantic cup model contract.')
    cup_model = tracking / cup_lock['model']
    if not cup_model.exists():
        temp = cup_model.with_suffix('.download')
        try:
            if args.cup_detector_model:
                shutil.copy2(args.cup_detector_model, temp)
            elif args.no_download_cup_model:
                print('WARNING: cup detector model is absent; cup diagnostics are disabled for this restore.')
                temp = None
            else:
                download(CUP_MODEL_URL, temp)
            if temp is not None and (temp.stat().st_size != cup_lock['bytes'] or sha256(temp) != cup_lock['sha256']):
                raise RuntimeError('Cup detector size/checksum differs from the pinned model; no model was installed.')
            if temp is not None:
                temp.replace(cup_model)
        finally:
            if temp is not None and temp.exists():
                temp.unlink()
    if cup_model.exists() and (cup_model.stat().st_size != cup_lock['bytes'] or sha256(cup_model) != cup_lock['sha256']):
        raise RuntimeError('Existing cup detector model size/checksum differs; file preserved.')
    adapted = ROOT / 'TrackingAdaptations'
    # Keep this list in lockstep with bridge.py imports.  A clean starter clone
    # has none of the student helpers, so omitting one here makes restoration
    # appear successful while the bridge fails at import time.
    for name in ('bridge.py', 'segmentation_support.py', 'identity_tracking.py',
                 'region_tracking.py', 'interaction_support.py', 'cup_detection.py',
                 'fist_detection.py'):
        dest = tracking / name
        if dest.exists() and dest.read_bytes() != (adapted / name).read_bytes():
            if name == 'bridge.py' and sha256(dest) == (ROOT / 'tools' / 'reference_bridge.sha256').read_text().strip():
                pass
            else:
                raise RuntimeError(f'Existing tracking adaptation differs; preserved: {dest}')
        shutil.copy2(adapted / name, dest)
    fist_model = tracking / 'gesture_recognizer.task'
    if args.fist_detector_model:
        if not args.fist_detector_model.is_file() or args.fist_detector_model.stat().st_size <= 0:
            raise RuntimeError('Fist detector model path is missing or empty; no model was installed.')
        copy_target = fist_model.with_suffix('.download')
        shutil.copy2(args.fist_detector_model, copy_target)
        copy_target.replace(fist_model)
    elif not fist_model.is_file():
        print('WARNING: gesture_recognizer.task is not installed. Download it from ' + FIST_MODEL_URL
              + ' or pass --fist-detector-model; fist-gated interactions will stay paused.')
    if not args.skip_env:
        env = tracking / '.venv'
        python = env / ('Scripts/python.exe' if sys.platform == 'win32' else 'bin/python')
        if not env.exists():
            subprocess.run([sys.executable, '-m', 'venv', str(env)], check=True)
        subprocess.run([str(python), '-m', 'pip', 'install', '-r', str(tracking / 'requirements.txt')], check=True)
        subprocess.run([str(python), '-m', 'pip', 'check'], check=True)
    print('RESTORE_OK: pinned assets and adapted tracking restored; Unity import/live checks still pending.')


if __name__ == '__main__':
    main()
