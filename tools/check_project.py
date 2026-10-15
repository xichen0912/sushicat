"""Static provenance/meta/layout checks; this is not a Unity compiler."""
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REVIEWED_IMPORT_MATERIALS = frozenset((
    'Assets/DigiPhant/Materials/Stage Lines.mat',
    'Assets/DigiPhant/Materials/Stage.mat',
    'Assets/SavannahCourse/Generated/v4/Bark.mat',
    'Assets/SavannahCourse/Generated/v4/Basin stone.mat',
    'Assets/SavannahCourse/Generated/v4/Ochre ground.mat',
    'Assets/SavannahCourse/Generated/v4/Olive foliage.mat',
    'Assets/SavannahCourse/Generated/v4/Pale sand path.mat',
    'Assets/SavannahCourse/Generated/v4/Sage foliage.mat',
    'Assets/SavannahCourse/Generated/v4/Still blue water.mat',
    'Assets/SavannahCourse/Generated/v4/Terracotta markers.mat',
))
REVIEWED_RUNTIME_PATCHES = frozenset((
    'Assets/DigiPhant/Runtime/DigiPhantController.cs',
    'Assets/DigiPhant/Runtime/DigiPhantLocomotion.cs',
    'Assets/DigiPhant/Runtime/DigiPhantCameraPreview.cs',
))


def load_reviewed_patches(starter, starter_commit, errors):
    """Verify the two explicit student patches against untouched pinned originals."""
    patch_lock = json.loads((ROOT / 'student-patch-lock.json').read_text())
    if patch_lock.get('schema_version') != 1 or patch_lock.get('starter_commit') != starter_commit:
        errors.append('Student patch lock schema/source does not match the pinned starter')
        return {}
    patches = patch_lock.get('patches', {})
    if set(patches) != REVIEWED_RUNTIME_PATCHES:
        errors.append('Student patch lock must contain exactly the three reviewed runtime paths')
        return {}
    accepted = {}
    for asset, entry in patches.items():
        source = asset.removeprefix('Assets/')
        original = subprocess.check_output(
            ['git', '-C', str(starter), 'show', f'{starter_commit}:{source}'])
        if (entry.get('original_sha256') != hashlib.sha256(original).hexdigest()
                or not re.fullmatch(r'[0-9a-f]{64}', entry.get('patched_sha256', ''))
                or not entry.get('reason')):
            errors.append(f'Invalid reviewed patch fingerprint/source: {asset}')
            continue
        if (starter / source).read_bytes() != original:
            errors.append(f'Patch reference source differs from pinned Git bytes: {source}')
            continue
        accepted[asset] = entry
    return accepted


def load_reviewed_imports(starter, starter_commit, errors):
    """Accept only explicitly reviewed bytes; no material-wide or float tolerance bypass."""
    import_lock = json.loads((ROOT / 'editor-import-lock.json').read_text())
    if (import_lock.get('schema_version') != 1
            or import_lock.get('editor_version') != '6000.6.3f1'
            or import_lock.get('editor_revision') != '45d8eee7de74'
            or import_lock.get('starter_commit') != starter_commit):
        errors.append('Editor import lock version/revision/source does not match the reviewed import')
        return {}
    materials = import_lock.get('materials', {})
    if set(materials) != REVIEWED_IMPORT_MATERIALS:
        errors.append('Editor import lock must contain exactly the ten reviewed material paths')
        return {}
    accepted = {}
    for asset, entry in materials.items():
        source = asset.removeprefix('Assets/')
        original = subprocess.check_output(
            ['git', '-C', str(starter), 'show', f'{starter_commit}:{source}'])
        original_sha = hashlib.sha256(original).hexdigest()
        if (entry.get('original_sha256') != original_sha
                or not re.fullmatch(r'[0-9a-f]{64}', entry.get('imported_sha256', ''))):
            errors.append(f'Invalid reviewed import fingerprint/source: {asset}')
            continue
        if (starter / source).read_bytes() != original:
            errors.append(f'Reviewed import source differs from pinned Git bytes: {source}')
            continue
        accepted[asset] = entry
    return accepted


def main():
    errors = []
    lock = json.loads((ROOT / 'starter-lock.json').read_text())
    starter = ROOT / 'DigiPhantStarter'
    actual = subprocess.check_output(['git', '-C', str(starter), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != lock['commit']:
        errors.append('Starter revision differs')
    accepted_imports = load_reviewed_imports(starter, lock['commit'], errors)
    accepted_patches = load_reviewed_patches(starter, lock['commit'], errors)
    imported_materials = set()
    patched_runtime = set()

    def matches_source(dest, expected):
        if not dest.is_file():
            return False
        observed = hashlib.sha256(dest.read_bytes()).hexdigest()
        asset = dest.relative_to(ROOT).as_posix()
        patch = accepted_patches.get(asset)
        if patch:
            if patch['original_sha256'] == expected and patch['patched_sha256'] == observed:
                patched_runtime.add(asset)
                return True
            return False
        if observed == expected:
            return True
        entry = accepted_imports.get(asset)
        if (entry and entry['original_sha256'] == expected
                and entry['imported_sha256'] == observed):
            imported_materials.add(asset)
            return True
        return False

    checksums = json.loads((starter / 'asset-checksums.json').read_text())
    checked = 0
    for source, expected in checksums.items():
        if source == 'Tracking/pose_landmarker_full.task':
            dest = starter / source
        elif source.split('/')[0] in ('Elephant', 'DigiPhant') or source in ('Elephant.meta', 'DigiPhant.meta'):
            dest = ROOT / 'Assets' / source
        else:
            continue
        if not matches_source(dest, expected):
            errors.append(f'Fingerprint mismatch/missing: {dest.relative_to(ROOT)}')
        checked += 1
    # Course metadata and source remain exact, apart from the ten locked import outputs.
    for source in (starter / 'SavannahCourse').rglob('*'):
        if source.is_file():
            dest = ROOT / 'Assets' / source.relative_to(starter)
            if not matches_source(dest, hashlib.sha256(source.read_bytes()).hexdigest()):
                errors.append(f'Course differs/missing: {dest.relative_to(ROOT)}')
    if (ROOT / 'Assets/SavannahCourse.meta').read_bytes() != (starter / 'SavannahCourse.meta').read_bytes():
        errors.append('Course root metadata differs')
    guids = {}
    for path in (ROOT / 'Assets').rglob('*'):
        if path.name == '.DS_Store':
            continue
        if path.suffix != '.meta' and not Path(str(path) + '.meta').exists():
            errors.append(f'Missing .meta: {path.relative_to(ROOT)}')
        if path.suffix == '.meta':
            match = re.search(r'^guid: ([0-9a-f]{32})$', path.read_text(), re.MULTILINE)
            if match:
                guid = match.group(1)
                if guid in guids:
                    errors.append(f'Duplicate GUID: {path.relative_to(ROOT)} and {guids[guid]}')
                guids[guid] = str(path.relative_to(ROOT))
            else:
                errors.append(f'Malformed .meta GUID: {path.relative_to(ROOT)}')
    if patched_runtime != REVIEWED_RUNTIME_PATCHES:
        errors.append('Not all reviewed runtime patches were checked against exact patched bytes')
    for name in ('bridge.py', 'segmentation_support.py', 'identity_tracking.py', 'region_tracking.py',
                 'interaction_support.py', 'cup_detection.py', 'fist_detection.py'):
        applied = starter / 'Tracking' / name
        adapted = ROOT / 'TrackingAdaptations' / name
        if not applied.is_file() or not adapted.is_file() or applied.read_bytes() != adapted.read_bytes():
            errors.append(f'Adaptation not applied: {name}')
    segment_lock = json.loads((ROOT / 'segmenter-lock.json').read_text())
    if hashlib.sha256((starter / 'Tracking/selfie_segmenter.tflite').read_bytes()).hexdigest() != segment_lock['sha256']:
        errors.append('Segmenter checksum mismatch')
    cup_lock = json.loads((ROOT / 'cup-detector-lock.json').read_text())
    cup_url = ('https://storage.googleapis.com/mediapipe-models/object_detector/'
               'efficientdet_lite0/int8/1/efficientdet_lite0.tflite')
    if (cup_lock.get('model') != 'efficientdet_lite0.tflite'
            or cup_lock.get('task') != 'MediaPipe ObjectDetector'
            or cup_lock.get('category_allowlist') != ['cup']
            or cup_lock.get('url') != cup_url
            or not re.fullmatch(r'[0-9a-f]{64}', cup_lock.get('sha256', ''))
            or not isinstance(cup_lock.get('bytes'), int) or cup_lock['bytes'] <= 0):
        errors.append('Cup detector lock does not specify the official pinned semantic cup model')
    else:
        detector = starter / 'Tracking' / cup_lock['model']
        if (not detector.is_file() or detector.stat().st_size != cup_lock['bytes']
                or hashlib.sha256(detector.read_bytes()).hexdigest() != cup_lock['sha256']):
            errors.append('Cup detector missing or pinned size/checksum mismatch')
    json.loads((ROOT / 'Packages/manifest.json').read_text())
    print(f'Static checks: {checked} starter fingerprints; {len(guids)} unique imported/custom GUIDs')
    print(f'Reviewed exact Unity import fingerprints accepted: {len(imported_materials)} material files')
    print(f'Reviewed exact student runtime patch fingerprints accepted: {len(patched_runtime)} scripts; pinned originals verified')
    if errors:
        print('\n'.join(errors))
        raise SystemExit(1)
    print('STATIC_OK: pinned originals, explicitly locked Editor imports/student patches, metadata, models and applied Python changes.')
    print('SCOPE: Static provenance/meta checks only; this script does not assess Unity compilation/runtime, camera tracking, three-person rehearsals or video recording.')


if __name__ == '__main__':
    main()
