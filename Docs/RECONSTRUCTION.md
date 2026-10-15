# 轻量包复原记录

2026-10-07，在独立 `/private/tmp` 目录完成最新轻量工程的静态复原检查。

候选 ZIP 包含 184 个项目文件（494,557 字节），不含 Elephant、starter、模型、Python 环境、Unity 缓存、日志和录像。解压后实际执行：

```bash
python3 tools/restore_starter.py --source /path/to/pinned-local-starter --skip-env --segmenter-model /path/to/pinned-model
python3 tools/check_project.py
```

结果：`RESTORE_OK`、`STATIC_OK`、`LIGHTWEIGHT_RECOVERY_OK`。124 项 starter 来源指纹、115 个唯一 GUID、10 个精确锁定材质导入输出和 2 份精确锁定的学生运行时修复通过；对应 starter 原文件仍匹配固定 commit。新三人验证、单人相机诊断与三份 Python 适配及 `.meta` 均保留正确字节，ZIP 完整性检查通过。

最终包随后更新文档/PDF；源代码和资源仍采用这次经过复原检查的文件。此检查使用本机保留的固定 starter 和分割模型，**没有重新验证互联网下载、Python 环境安装或复原目录中的 Unity 运行**。工作工程的 Unity 编译、合成验证及实际相机记录单独见 `SETUP_VERIFICATION.md`。

新机器使用 `python3 tools/restore_starter.py`，需 Git、适用 Python 与网络。大资源从锁定 commit 获取，分割模型从官方地址获取且必须匹配 checksum；不同的现有文件会保留并停止，不自动覆盖。老师原始资源、10 个导入材质和 3 个运行时修复分别由 `starter-lock.json`、`editor-import-lock.json`、`student-patch-lock.json` 明确核对。恢复脚本不会覆盖 Assets 中的运行时修复。

另备有资源传递包：固定 starter bundle 已通过 `git bundle verify` 和实际离线 clone/checkout，124 项原始来源指纹通过。资源包中的 `RestoreOffline.command` 采用包内 bundle 和精确模型，免去 GitHub/Google 大资源下载。Python 依赖、适用 Unity 编辑器与许可需要单独准备；不把这项资源复原称为完全离线运行环境安装。
