"""Installer regressions: no package downloads, virtualenv changes or camera access."""
import importlib.util
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('setup_camera', ROOT / 'SetupCamera.py')
setup_camera = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(setup_camera)


class SetupCameraTests(unittest.TestCase):
    def test_missing_full_lock_uses_available_requirements(self):
        with tempfile.TemporaryDirectory() as folder:
            tracking = Path(folder)
            self.assertEqual(setup_camera.select_requirements(tracking, 'darwin', 'arm64', '3.14'),
                             tracking / 'requirements.txt')

    def test_matching_platform_uses_full_lock(self):
        with tempfile.TemporaryDirectory() as folder:
            tracking = Path(folder)
            pinned = tracking / 'dependency-lock-macos-arm64.txt'
            pinned.write_text('mediapipe==0.10.35\n')
            self.assertEqual(setup_camera.select_requirements(tracking, 'darwin', 'arm64', '3.14'), pinned)

    def test_other_platform_or_python_never_uses_mac_lock(self):
        with tempfile.TemporaryDirectory() as folder:
            tracking = Path(folder)
            (tracking / 'dependency-lock-macos-arm64.txt').touch()
            for system, machine, version in [('win32', 'AMD64', '3.14'), ('darwin', 'x86_64', '3.14'),
                                             ('darwin', 'arm64', '3.12')]:
                self.assertEqual(setup_camera.select_requirements(tracking, system, machine, version),
                                 tracking / 'requirements.txt')

    @patch.object(setup_camera.subprocess, 'run')
    def test_default_mirror_failure_falls_back_to_official_index(self, run):
        run.side_effect = [subprocess.CalledProcessError(1, ['pip']), None]
        setup_camera.install_dependencies(Path('python'), Path('requirements.txt'))
        self.assertEqual(run.call_count, 2)
        for call, expected in zip(run.call_args_list, [setup_camera.DEFAULT_INDEX, setup_camera.OFFICIAL_INDEX]):
            args = call.args[0]
            self.assertEqual(args[args.index('--index-url') + 1], expected)
            self.assertIn('--only-binary=:all:', args)
            self.assertTrue(call.kwargs['check'])

    @patch.object(setup_camera.subprocess, 'run')
    def test_explicit_index_does_not_switch_network_destinations(self, run):
        run.side_effect = subprocess.CalledProcessError(1, ['pip'])
        with self.assertRaisesRegex(SystemExit, 'could not be installed'):
            setup_camera.install_dependencies(Path('python'), Path('requirements.txt'), 'https://example.org/simple')
        self.assertEqual(run.call_count, 1)
        self.assertIn('https://example.org/simple', run.call_args.args[0])

    @patch.object(setup_camera.subprocess, 'run')
    def test_success_uses_one_index_only(self, run):
        setup_camera.install_dependencies(Path('python path'), Path('requirements path'))
        self.assertEqual(run.call_count, 1)
        self.assertEqual(run.call_args.args[0][0], 'python path')
        self.assertEqual(run.call_args.args[0][-1], 'requirements path')


if __name__ == '__main__':
    unittest.main()
