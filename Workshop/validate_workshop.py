#!/usr/bin/env python3
"""Check recorded evidence, never invent data or infer that a model was trained."""
import csv
import hashlib
import json
from collections import Counter, defaultdict
from pathlib import Path
import sys

BASE = Path(__file__).resolve().parent


def inside(relative):
    if not isinstance(relative, str) or not relative:
        raise ValueError('缺少相对路径')
    if Path(relative).is_absolute():
        raise ValueError('需要相对路径')
    path = (BASE / relative).resolve()
    if not path.is_relative_to(BASE):
        raise ValueError('路径不在 Workshop 内')
    return path


def main():
    errors = []
    try:
        meta = json.loads((BASE / 'dataset_metadata.json').read_text())
        datasets = {item['id']: item for item in meta['datasets']}
        if set(datasets) != {'objects', 'poses'}:
            raise ValueError('必须有 objects 和 poses 两个项目')
        for identifier, expected in [('objects', 4), ('poses', 5)]:
            labels = [item['id'] for item in datasets[identifier]['classes']]
            if len(labels) != expected or len(set(labels)) != expected:
                raise ValueError(f'{identifier} 需要 {expected} 个唯一类别')
            expected_type = 'Image' if identifier == 'objects' else 'Pose'
            if datasets[identifier]['teachable_machine_project_type'] != expected_type:
                raise ValueError(f'{identifier} 需要 TM {expected_type} 项目')
            rounds = [item['id'] for item in datasets[identifier]['training_rounds']]
            if len(rounds) != 2 or set(rounds) != {'initial', 'retrained'}:
                raise ValueError(f'{identifier} 需要 initial 与 retrained 两轮记录')
    except (OSError, ValueError, KeyError, TypeError) as exc:
        print(f'元数据结构错误：{exc}')
        return 2

    records = {}
    hashes = defaultdict(set)
    seen_contents = set()
    counts = Counter()
    added = Counter()
    try:
        lines = inside(meta['sample_manifest']).read_text().splitlines()
    except (OSError, ValueError, KeyError) as exc:
        print(f'样本清单错误：{exc}')
        return 2
    for line_no, line in enumerate(lines, 1):
        if not line.strip():
            continue
        try:
            row = json.loads(line)
            identifier = row['dataset']
            label = row['class_id']
            split = row['split']
            sample_id = row['sample_id']
            valid_labels = {item['id'] for item in datasets[identifier]['classes']}
            if label not in valid_labels or split not in {'train', 'test'}:
                raise ValueError('类别或数据划分无效')
            if not isinstance(sample_id, str) or not sample_id or sample_id in records:
                raise ValueError('样本编号为空或重复')
            if row.get('source') != 'webcam':
                raise ValueError('此次采集要求 source=webcam；其他来源不能混充')
            if not row.get('session_id') or not row.get('captured_at'):
                raise ValueError('缺少实际拍摄轮次或时间')
            if row.get('round_added') not in {'initial', 'retrained'}:
                raise ValueError('round_added 应为 initial 或 retrained')
            path = inside(row['relative_path'])
            expected_parent = inside(f'data/{identifier}/{split}/{label}')
            if not path.is_relative_to(expected_parent):
                raise ValueError('样本路径与类别/划分目录不一致')
            if not path.is_file() or path.stat().st_size == 0:
                raise ValueError('实际样本文件不存在或为空')
            if row.get('sample_kind') == 'camera_image':
                if path.suffix.lower() not in {'.jpg', '.jpeg', '.png', '.webp'}:
                    raise ValueError('camera_image 需要图像文件')
            elif row.get('sample_kind') == 'pose_keypoints' and identifier == 'poses':
                if path.suffix.lower() != '.json' or not json.loads(path.read_text()):
                    raise ValueError('关键点样本需要非空单样本 JSON')
            else:
                raise ValueError('无效 sample_kind；ZIP 不能作为逐条样本')
            digest = hashlib.sha256(path.read_bytes()).hexdigest()
            hashes[(identifier, digest)].add(split)
            if (identifier, digest) in seen_contents:
                raise ValueError('同一项目存在重复内容，不能将复制文件计为新样本')
            seen_contents.add((identifier, digest))
            records[sample_id] = row
            counts[(identifier, label, split, row['round_added'])] += 1
            if split == 'train' and row['round_added'] == 'retrained':
                added[identifier] += 1
        except (OSError, ValueError, KeyError, TypeError) as exc:
            errors.append(f'清单第 {line_no} 行：{exc}')

    for (identifier, digest), splits in hashes.items():
        if len(splits) > 1:
            errors.append(f'{identifier} 存在训练/测试内容重复，哈希 {digest[:12]}')
    print('实际登记且存在的样本（当前空模板应全部为 0）：')
    for identifier, ds in datasets.items():
        for item in ds['classes']:
            label = item['id']
            initial = counts[(identifier, label, 'train', 'initial')]
            extra = counts[(identifier, label, 'train', 'retrained')]
            test = sum(counts[(identifier, label, 'test', r)] for r in ('initial', 'retrained'))
            print(f'  {identifier}/{label}: 首轮训练 {initial}，补拍训练 {extra}，留出测试 {test}')
            if initial < 50:
                errors.append(f'{identifier}/{label} 缺少至少 50 个首轮真实训练样本')
            if test == 0:
                errors.append(f'{identifier}/{label} 缺少真实留出测试样本（建议 10 个）')
        if added[identifier] == 0:
            errors.append(f'{identifier} 缺少根据测试错误补拍的训练样本')
        for round_info in ds.get('training_rounds', []):
            round_id = round_info.get('id')
            if round_info.get('trained') is not True:
                errors.append(f'{identifier}/{round_id} 尚未记录完成真实训练')
                continue
            if not round_info.get('trained_at') or not round_info.get('shared_model_url'):
                errors.append(f'{identifier}/{round_id} 缺训练时间或真实分享链接')
            try:
                export = inside(round_info['export_directory'])
                model = json.loads((export / 'model.json').read_text())
                json.loads((export / 'metadata.json').read_text())
                weight_paths = [p for group in model.get('weightsManifest', []) for p in group['paths']]
                if not weight_paths:
                    raise ValueError('模型未声明权重文件')
                for relative in weight_paths:
                    weight = (export / relative).resolve()
                    if not weight.is_relative_to(export) or not weight.is_file() or weight.stat().st_size == 0:
                        raise ValueError('模型权重文件缺失或路径无效')
            except (OSError, ValueError, KeyError, TypeError) as exc:
                errors.append(f'{identifier}/{round_id} 模型导出错误：{exc}')

    evaluated = set()
    try:
        with inside(meta['evaluation_file']).open(newline='') as handle:
            reader = csv.DictReader(handle)
            for line_no, row in enumerate(reader, 2):
                try:
                    sample = records[row['sample_id']]
                    ds_id = row['dataset']
                    labels = {item['id'] for item in datasets[ds_id]['classes']}
                    if sample['dataset'] != ds_id or sample['split'] != 'test':
                        raise ValueError('评估需要相应项目的真实留出测试样本')
                    if row['true_label'] != sample['class_id'] or row['predicted_label'] not in labels:
                        raise ValueError('真实或预测类别无效')
                    if row['round_id'] not in {'initial', 'retrained'}:
                        raise ValueError('评估轮次无效')
                    probability = float(row['predicted_confidence'])
                    if not 0 <= probability <= 1:
                        raise ValueError('概率需在 0 至 1 之间')
                    evaluated.add((ds_id, row['true_label'], row['round_id']))
                except (ValueError, KeyError, TypeError) as exc:
                    errors.append(f'评估第 {line_no} 行：{exc}')
    except (OSError, ValueError, KeyError) as exc:
        errors.append(f'评估文件错误：{exc}')
    for identifier, ds in datasets.items():
        for round_id in ('initial', 'retrained'):
            if any((identifier, item['id'], round_id) not in evaluated for item in ds['classes']):
                errors.append(f'{identifier}/{round_id} 缺少覆盖所有类别的真实测试记录')

    if errors:
        print('\n尚未满足提交证据：')
        for message in errors:
            print(f'  - {message}')
        print('\n模板已建立；真实采集、训练、重训和评估尚需完成。没有生成或推定结果。')
        return 2
    print('\n文件与记录检查通过。仍需人工验证原始采集、TM 项目类型、模型运行与分享权限。')
    return 0


if __name__ == '__main__':
    sys.exit(main())
