from pathlib import Path
import argparse, hashlib, platform, subprocess, sys

DEFAULT_INDEX = 'https://pypi.tuna.tsinghua.edu.cn/simple'
OFFICIAL_INDEX = 'https://pypi.org/simple'


def select_requirements(tracking, system, machine, version):
    compatible = tracking / 'dependency-lock-macos-arm64-py312.txt'
    if system == 'darwin' and machine == 'arm64' and version == '3.12' and compatible.is_file():
        return compatible
    pinned = tracking / 'dependency-lock-macos-arm64.txt'
    if system == 'darwin' and machine == 'arm64' and version == '3.14' and pinned.is_file():
        return pinned
    return tracking / 'requirements.txt'


def install_dependencies(python, requirements, index_url=None):
    # Explicit index choices are respected; the default has an overseas fallback.
    indices = [index_url] if index_url else [DEFAULT_INDEX, OFFICIAL_INDEX]
    last_error = None
    for index in indices:
        try:
            subprocess.run([str(python), '-m', 'pip', 'install', '--disable-pip-version-check',
                            '--only-binary=:all:', '--timeout', '30', '--retries', '2',
                            '--index-url', index, '-r', str(requirements)], check=True)
            return
        except subprocess.CalledProcessError as error:
            last_error = error
            if index != indices[-1]:
                print('Package mirror could not complete installation; trying official PyPI.', flush=True)
    raise SystemExit('Camera dependencies could not be installed (pip exit '
                     + str(last_error.returncode) + '). Check the connection/index above and use '
                     'a 64-bit Python installation with matching binary wheels. '
                     'On Apple Silicon, Python 3.12 is the preferred camera runtime. '
                     'To select PyPI explicitly, add --index-url https://pypi.org/simple.')

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--index-url', help='Package index URL; default: Tsinghua, with PyPI fallback.')
    parser.add_argument('--fist-model', type=Path,
                        help='Optional local gesture_recognizer.task to install beside bridge.py')
    parser.add_argument('--require-fist-model', action='store_true',
                        help='Fail setup/check-only when the fist model is not installed')
    args = parser.parse_args()
    if sys.version_info < (3, 10):
        raise SystemExit('Use Python 3.10 or newer to run this installer.')
    tracking = Path(__file__).resolve().parent / 'DigiPhantStarter/Tracking'
    for name in ('bridge.py', 'identity_tracking.py', 'region_tracking.py', 'segmentation_support.py', 'encode_recording.py',
                 'interaction_support.py', 'cup_detection.py', 'fist_detection.py', 'efficientdet_lite0.tflite',
                 'pose_landmarker_full.task', 'selfie_segmenter.tflite', 'requirements.txt'):
        if not (tracking / name).is_file():
            raise SystemExit('Missing camera resource: ' + name)
    cup_model = tracking / 'efficientdet_lite0.tflite'
    if (cup_model.stat().st_size != 4602795 or hashlib.sha256(cup_model.read_bytes()).hexdigest()
            != '0720bf247bd76e6594ea28fa9c6f7c5242be774818997dbbeffc4da460c723bb'):
        raise SystemExit('Cup recognition model does not match the bundled official model.')
    fist_model = tracking / 'gesture_recognizer.task'
    if args.fist_model:
        if not args.fist_model.is_file() or args.fist_model.stat().st_size <= 0:
            raise SystemExit('Fist model path is missing or empty: ' + str(args.fist_model))
        temporary = fist_model.with_suffix('.download')
        temporary.write_bytes(args.fist_model.read_bytes())
        temporary.replace(fist_model)
    fist_ready = fist_model.is_file() and fist_model.stat().st_size > 0
    if args.require_fist_model and not fist_ready:
        raise SystemExit('Missing gesture_recognizer.task. Supply --fist-model with the official MediaPipe model.')
    if args.check_only:
        suffix = ' fist model ready.' if fist_ready else ' WARNING: fist model missing; water/wood fist interactions stay paused.'
        print('CAMERA_PACKAGE_RESOURCES_OK; no camera opened or dependencies installed.' + suffix)
        return
    preferred_env = tracking / '.venv312'
    legacy_env = tracking / '.venv'
    env = preferred_env if preferred_env.exists() else legacy_env
    python = env / ('Scripts/python.exe' if sys.platform == 'win32' else 'bin/python')
    if not env.exists():
        subprocess.run([sys.executable, '-m', 'venv', str(env)], check=True)
    if not python.is_file():
        raise SystemExit('Camera environment is incomplete: ' + str(env))
    version = subprocess.check_output([str(python), '-c',
        'import sys; print(str(sys.version_info.major) + "." + str(sys.version_info.minor))'], text=True).strip()
    requirements = select_requirements(tracking, sys.platform, platform.machine(), version)
    print('Camera Python: ' + str(python) + ' (' + version + '); requirements: ' + requirements.name)
    install_dependencies(python, requirements, args.index_url)
    try:
        subprocess.run([str(python), '-m', 'pip', 'check'], check=True)
        subprocess.run([str(python), '-c',
                        'import cv2, mediapipe, numpy; '
                        'from mediapipe.tasks.python import vision; '
                        'assert hasattr(vision, "PoseLandmarker") and hasattr(vision, "ObjectDetector") and hasattr(vision, "GestureRecognizer")'],
                       check=True)
    except subprocess.CalledProcessError:
        raise SystemExit('Installed camera dependencies failed validation. Review the error above. '
                         'This environment has not been marked ready; no camera was opened.')
    if fist_ready:
        print('CAMERA_ENVIRONMENT_READY. Open Unity and select Upper body / Camera mode.')
    else:
        print('CAMERA_ENVIRONMENT_READY_WITHOUT_FIST_MODEL. Install gesture_recognizer.task before fist interactions.')

if __name__ == '__main__':
    main()
