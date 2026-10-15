"""Pure filesystem/configuration regression tests; never import or open OpenCV."""
from contextlib import redirect_stderr
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest

from collect_samples import CollectionStore, load_configuration, parse_args


class CollectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        # Test fixture bytes and manifests exist only in a disposable temp dir.
        self.metadata = {
            "sample_manifest": "samples.jsonl",
            "status": "scaffold_only_uncollected_untrained",
            "datasets": [
                {"id": identifier, "classes": [{"id": label}],
                 "training_rounds": [{"id": "initial", "trained": False},
                                     {"id": "retrained", "trained": False}],
                 "collection": {"camera": None, "location": None, "sessions": []}}
                for identifier, label in [("objects", "bottle"), ("poses", "t_pose")]
            ],
        }
        (self.base / "dataset_metadata.json").write_text(json.dumps(self.metadata))
        (self.base / "samples.jsonl").write_text("")

    def arguments(self, dataset="objects", label="bottle", split="train", round_added="initial"):
        return parse_args(["--dataset", dataset, "--class", label, "--split", split,
                           "--round", round_added, "--camera", "0"])

    def test_all_options_are_required(self):
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as result:
            parse_args(["--dataset", "objects", "--class", "bottle"])
        self.assertEqual(result.exception.code, 2)

    def test_negative_camera_is_rejected(self):
        with redirect_stderr(io.StringIO()), self.assertRaises(SystemExit):
            parse_args(["--dataset", "objects", "--class", "bottle", "--split", "train",
                        "--round", "initial", "--camera", "-1"])

    def test_unknown_class_fails_without_writing_session(self):
        with self.assertRaisesRegex(ValueError, "class"):
            load_configuration(self.base, self.arguments(label="unknown"))
        self.assertFalse((self.base / "sessions").exists())

    def test_metadata_path_escape_is_rejected(self):
        self.metadata["sample_manifest"] = "../outside.jsonl"
        (self.base / "dataset_metadata.json").write_text(json.dumps(self.metadata))
        with self.assertRaisesRegex(ValueError, "路径"):
            CollectionStore(self.base, self.arguments())

    def test_raw_hash_duplicate_across_test_and_train_is_rejected(self):
        test = CollectionStore(self.base, self.arguments(split="test"))
        test.start_session()
        frame_hash = hashlib.sha256(b"synthetic raw frame fixture").hexdigest()
        test.save_capture(b"synthetic encoded test fixture", frame_hash, "2026-01-01T00:00:00+00:00")
        test.finish_session()
        train = CollectionStore(self.base, self.arguments(split="train"))
        train.start_session()
        with self.assertRaisesRegex(ValueError, "重复"):
            train.save_capture(b"different encoding of same frame fixture", frame_hash,
                               "2026-01-01T00:01:00+00:00")
        train.finish_session()
        rows = (self.base / "samples.jsonl").read_text().splitlines()
        self.assertEqual(len(rows), 1)
        self.assertEqual(json.loads(rows[0])["split"], "test")

    def test_encoded_file_duplicate_is_rejected_without_raw_hash_match(self):
        store = CollectionStore(self.base, self.arguments())
        store.start_session()
        store.save_capture(b"synthetic duplicate fixture", "a" * 64, "2026-01-01T00:00:00+00:00")
        with self.assertRaisesRegex(ValueError, "重复"):
            store.save_capture(b"synthetic duplicate fixture", "b" * 64, "2026-01-01T00:00:01+00:00")
        self.assertEqual(store.session["captured_samples"], 1)
        store.finish_session()

    def test_manifest_and_path_keep_explicit_test_split(self):
        store = CollectionStore(self.base, self.arguments(split="test", round_added="retrained"))
        store.start_session()
        row = store.save_capture(b"synthetic manifest fixture", "c" * 64,
                                 "2026-01-01T00:00:00+00:00")
        store.finish_session()
        self.assertTrue(row["relative_path"].startswith("data/objects/test/bottle/"))
        self.assertEqual(row["split"], "test")
        self.assertEqual(row["round_added"], "retrained")
        self.assertEqual(row["captured_at"], "2026-01-01T00:00:00+00:00")
        metadata = json.loads((self.base / "dataset_metadata.json").read_text())
        self.assertFalse(metadata["datasets"][0]["training_rounds"][0]["trained"])
        self.assertIsNone(metadata["datasets"][0]["collection"]["location"])
        self.assertEqual(metadata["status"], "camera_images_recorded_training_pending")
        self.assertIsNotNone(json.loads(store.session_path.read_text())["ended_at"])

    def test_pose_camera_images_are_not_claimed_as_keypoints(self):
        store = CollectionStore(self.base, self.arguments(dataset="poses", label="t_pose"))
        store.start_session()
        row = store.save_capture(b"synthetic pose fixture", "d" * 64,
                                 "2026-01-01T00:00:00+00:00")
        self.assertEqual(row["sample_kind"], "camera_image")
        self.assertEqual(row["label_status"], "operator_selected_not_automatically_verified")
        self.assertIn("not TM Pose keypoints", store.session["notes"])
        store.finish_session()


if __name__ == "__main__":
    unittest.main()
