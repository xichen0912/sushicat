#!/usr/bin/env python3
"""Interactively save real camera frames; never generate, train, or upload samples.

Importing this module does not import OpenCV or open a camera. All capture options
are mandatory so a test session cannot silently become a training session.
"""
from __future__ import annotations

import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import fcntl
import hashlib
import json
import os
from pathlib import Path
import re
import sys
import time
import uuid

BASE = Path(__file__).resolve().parent
LABEL_ID = re.compile(r"[A-Za-z0-9][A-Za-z0-9_-]*\Z")
IMAGE_SUFFIXES = {".jpg", ".jpeg", ".png", ".webp"}


def timestamp() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="milliseconds")


def inside(base: Path, relative: str) -> Path:
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute():
        raise ValueError("需要 Workshop 内的相对路径")
    target = (base / relative).resolve()
    if not target.is_relative_to(base.resolve()):
        raise ValueError("路径不能离开 Workshop")
    return target


def camera_index(value: str) -> int:
    try:
        index = int(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("camera 必须是非负整数索引") from exc
    if index < 0:
        raise argparse.ArgumentTypeError("camera 必须是非负整数索引")
    return index


def parse_args(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", choices=("objects", "poses"), required=True)
    parser.add_argument("--class", dest="class_id", required=True)
    parser.add_argument("--split", choices=("train", "test"), required=True)
    parser.add_argument("--round", dest="round_added", choices=("initial", "retrained"), required=True)
    parser.add_argument("--camera", type=camera_index, required=True)
    return parser.parse_args(argv)


def load_configuration(base: Path, args):
    metadata_path = inside(base, "dataset_metadata.json")
    metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
    matches = [item for item in metadata["datasets"] if item["id"] == args.dataset]
    if len(matches) != 1:
        raise ValueError("dataset_metadata.json 中项目不存在或重复")
    dataset = matches[0]
    labels = [item["id"] for item in dataset["classes"]]
    if len(labels) != len(set(labels)) or any(not LABEL_ID.fullmatch(item) for item in labels):
        raise ValueError("元数据类别 ID 必须唯一，且只含字母、数字、下划线或连字符")
    if args.class_id not in labels:
        raise ValueError(f"--class 无效；{args.dataset} 可用类别：{', '.join(labels)}")
    if args.split not in {"train", "test"}:
        raise ValueError("split 必须明确指定 train 或 test")
    if args.round_added not in {item["id"] for item in dataset["training_rounds"]}:
        raise ValueError("采集轮次不在 dataset_metadata.json 中")
    if args.camera < 0:
        raise ValueError("camera 必须是非负整数")
    manifest = inside(base, metadata["sample_manifest"])
    destination = inside(base, f"data/{args.dataset}/{args.split}/{args.class_id}")
    return metadata, dataset, manifest, destination


def write_json(path: Path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8") as handle:
            json.dump(value, handle, ensure_ascii=False, indent=2)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        temporary.replace(path)
    finally:
        temporary.unlink(missing_ok=True)


@contextmanager
def collection_lock(base: Path):
    # One manifest writer at a time; released automatically on exit or a crash.
    lock_path = inside(base, "sessions/.collection.lock")
    lock_path.parent.mkdir(parents=True, exist_ok=True)
    with lock_path.open("a") as handle:
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as exc:
            raise ValueError("已有另一个采集程序运行；先按 Q 退出它") from exc
        try:
            yield
        finally:
            fcntl.flock(handle, fcntl.LOCK_UN)


class CollectionStore:
    """Filesystem evidence store; can be tested with bytes without a camera."""

    def __init__(self, base: Path, args):
        self.base = base.resolve()
        self.args = args
        self.metadata, self.dataset, self.manifest, self.destination = load_configuration(self.base, args)
        self.content_hashes = set()
        self.frame_hashes = set()
        self.sample_ids = set()
        if self.manifest.exists():
            for line_number, line in enumerate(self.manifest.read_text(encoding="utf-8").splitlines(), 1):
                if not line.strip():
                    continue
                try:
                    row = json.loads(line)
                    identifier = row["sample_id"]
                    if not isinstance(identifier, str) or not identifier or identifier in self.sample_ids:
                        raise ValueError("重复或空 sample_id")
                    self.sample_ids.add(identifier)
                    if row["dataset"] != args.dataset:
                        continue
                    sample_path = inside(self.base, row["relative_path"])
                    expected_parent = inside(self.base, f"data/{args.dataset}/{row['split']}/{row['class_id']}")
                    if row["split"] not in {"train", "test"} or not sample_path.is_relative_to(expected_parent):
                        raise ValueError("记录路径与 train/test/类别不一致")
                    if not sample_path.is_file():
                        raise ValueError("登记文件不存在")
                    self.content_hashes.add(hashlib.sha256(sample_path.read_bytes()).hexdigest())
                    if row.get("frame_sha256"):
                        self.frame_hashes.add(row["frame_sha256"])
                except (KeyError, TypeError, ValueError, OSError) as exc:
                    raise ValueError(f"样本清单第 {line_number} 行有错误；先修正：{exc}") from exc
        # Also guard against files present on disk but not yet in the manifest.
        for split in ("train", "test"):
            data_root = inside(self.base, f"data/{args.dataset}/{split}")
            if data_root.exists():
                for path in data_root.rglob("*"):
                    if path.is_file() and path.suffix.lower() in IMAGE_SUFFIXES:
                        checked = inside(self.base, path.relative_to(self.base).as_posix())
                        self.content_hashes.add(hashlib.sha256(checked.read_bytes()).hexdigest())
        self.session = None
        self.session_path = None

    def start_session(self):
        if self.session is not None:
            raise ValueError("采集 session 已开始")
        session_id = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "Z_" + uuid.uuid4().hex[:8]
        self.session_path = inside(self.base, f"sessions/{session_id}.json")
        self.session = {
            "session_id": session_id,
            "dataset": self.args.dataset,
            "class_id": self.args.class_id,
            "split": self.args.split,
            "round_added": self.args.round_added,
            "camera_index": self.args.camera,
            "source": "webcam",
            "started_at": timestamp(),
            "ended_at": None,
            "captured_samples": 0,
            "label_status": "operator_selected_not_automatically_verified",
            "sample_kind": "camera_image",
            "notes": "Raw camera frames only. Pose images are not TM Pose keypoints or a trained Pose model.",
        }
        collection = self.dataset.setdefault("collection", {})
        if not isinstance(collection.setdefault("sessions", []), list):
            raise ValueError("collection.sessions 必须是列表")
        collection["camera"] = f"OpenCV camera index {self.args.camera}"
        collection["sessions"].append({
            "session_id": session_id,
            "relative_path": self.session_path.relative_to(self.base).as_posix(),
            "started_at": self.session["started_at"],
        })
        write_json(self.session_path, self.session)
        write_json(inside(self.base, "dataset_metadata.json"), self.metadata)

    def save_capture(self, png_bytes: bytes, frame_hash: str, captured_at: str):
        if self.session is None:
            raise ValueError("尚未开始真实采集 session")
        digest = hashlib.sha256(png_bytes).hexdigest()
        if digest in self.content_hashes or frame_hash in self.frame_hashes:
            raise ValueError("重复的完全相同帧已跳过（包含其他类别或 train/test 中的帧）")
        number = self.session["captured_samples"] + 1
        sample_id = f"{self.args.dataset}_{self.args.class_id}_{self.session['session_id']}_{number:04d}"
        if sample_id in self.sample_ids:
            raise ValueError("sample_id 已存在")
        path = self.destination / (sample_id + ".png")
        # Resolve again so a replaced directory symlink cannot redirect a write.
        path = inside(self.base, path.relative_to(self.base).as_posix())
        path.parent.mkdir(parents=True, exist_ok=True)
        row = {
            "sample_id": sample_id,
            "dataset": self.args.dataset,
            "class_id": self.args.class_id,
            "split": self.args.split,
            "relative_path": path.relative_to(self.base).as_posix(),
            "source": "webcam",
            "sample_kind": "camera_image",
            "session_id": self.session["session_id"],
            "captured_at": captured_at,
            "round_added": self.args.round_added,
            "sha256": digest,
            "frame_sha256": frame_hash,
            "label_status": "operator_selected_not_automatically_verified",
        }
        with path.open("xb") as handle:
            handle.write(png_bytes)
            handle.flush()
            os.fsync(handle.fileno())
        try:
            self.manifest.parent.mkdir(parents=True, exist_ok=True)
            with self.manifest.open("a", encoding="utf-8") as handle:
                handle.write(json.dumps(row, ensure_ascii=False) + "\n")
                handle.flush()
                os.fsync(handle.fileno())
        except OSError:
            path.unlink(missing_ok=True)
            raise
        self.content_hashes.add(digest)
        self.frame_hashes.add(frame_hash)
        self.sample_ids.add(sample_id)
        self.session["captured_samples"] = number
        write_json(self.session_path, self.session)
        if self.metadata.get("status") == "scaffold_only_uncollected_untrained":
            self.metadata["status"] = "camera_images_recorded_training_pending"
            write_json(inside(self.base, "dataset_metadata.json"), self.metadata)
        return row

    def finish_session(self):
        if self.session is not None:
            self.session["ended_at"] = timestamp()
            write_json(self.session_path, self.session)


def capture(base: Path, args) -> int:
    with collection_lock(base):
        store = CollectionStore(base, args)
        # Only this explicit execution path imports OpenCV or opens the camera.
        try:
            import cv2
        except ImportError as exc:
            raise ValueError("缺少 OpenCV；使用 DigiPhantStarter/Tracking/.venv/bin/python 运行") from exc
        camera = cv2.VideoCapture(args.camera)
        window = "Workshop - Space capture / Q quit"
        try:
            if not camera.isOpened():
                raise ValueError("无法打开指定摄像头；检查 macOS 权限与 camera 索引")
            ok, frame = camera.read()
            frame_at = timestamp()
            if not ok or frame is None:
                raise ValueError("摄像头没有返回有效图像；未登记采集 session")
            store.start_session()
            print(f"采集：{args.dataset}/{args.class_id}，{args.split}，{args.round_added}。")
            print("空格保存当前真实帧；Q 退出。每次拍摄前变化角度、距离或姿态，不要只连续按键。")
            print("类别是你选定的标签，程序不检查镜头里是否真的符合；测试样本不要用于训练。")
            if args.dataset == "poses":
                print("此处保存原相机图片，不生成关键点/姿态模型；仍需在 Teachable Machine 的 Pose Project 创建并训练。")
            last_capture = float("-inf")
            message = "Space: save one frame; Q: quit"
            message_until = time.monotonic() + 3
            while True:
                preview = frame.copy()
                info = f"{args.dataset} / {args.class_id} / {args.split} / {args.round_added}"
                cv2.putText(preview, info, (12, 28), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
                cv2.putText(preview, f"Saved this session: {store.session['captured_samples']}", (12, 56), cv2.FONT_HERSHEY_SIMPLEX, 0.6, (0, 255, 0), 2)
                cv2.putText(preview, "Change view between captures. Test stays separate.", (12, 84), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 1)
                if time.monotonic() < message_until:
                    cv2.putText(preview, message, (12, 112), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
                cv2.imshow(window, preview)
                key = cv2.waitKey(20) & 0xFF
                if key in (ord("q"), ord("Q"), 27):
                    break
                if cv2.getWindowProperty(window, cv2.WND_PROP_VISIBLE) < 1:
                    break
                if key == ord(" ") and time.monotonic() - last_capture >= 0.5:
                    last_capture = time.monotonic()
                    encoded, png = cv2.imencode(".png", frame)
                    if not encoded:
                        raise ValueError("当前帧编码失败；没有写入样本")
                    frame_hash = hashlib.sha256(
                        str((frame.shape, str(frame.dtype))).encode("ascii") + frame.tobytes()
                    ).hexdigest()
                    try:
                        row = store.save_capture(png.tobytes(), frame_hash, frame_at)
                        print(f"已保存 {row['relative_path']}")
                        message = f"Saved {store.session['captured_samples']}"
                    except ValueError as exc:
                        print(exc)
                        message = "Duplicate frame skipped; change the view"
                    message_until = time.monotonic() + 2
                ok, frame = camera.read()
                frame_at = timestamp()
                if not ok or frame is None:
                    raise ValueError("摄像头中断；已保存的样本保留")
        finally:
            camera.release()
            cv2.destroyAllWindows()
            store.finish_session()
        print(f"本轮保存 {store.session['captured_samples']} 个真实帧；没有训练或上传模型。")
    return 0


def main(argv=None) -> int:
    args = parse_args(argv)
    try:
        # Check labels and metadata before opening a device or creating a session.
        load_configuration(BASE, args)
        return capture(BASE, args)
    except KeyboardInterrupt:
        print("\n采集已停止；已保存的真实样本保留。")
        return 130
    except (ValueError, OSError, KeyError, TypeError) as exc:
        print(f"采集未完成：{exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
