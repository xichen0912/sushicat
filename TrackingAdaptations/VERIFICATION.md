# 实际完成的搭建验证

2026-10-07，在此电脑的 Python 3.14.8 arm64 虚拟环境中完成：

- 109 项适配单元及回归测试全部通过；starter 原有 11 项 Python 测试也通过。测试不包含真实相机和 Gesture Recognizer 模型。
- `extract_features`、`PerformerTracker`、`requested_count`、`encode_preview`
  的 AST 与固定版本原始源码一致；原始 pose JSON 构造也一致。
- 新的独立 `StablePerformerTracker` 已接入 main，未替换原始 tracker 定义。
  合成位置序列复现原实现的三个问题：歧义后下一帧自动恢复；长时间
  消失后复用旧位置；三人模式把第四名旁观者纳入初始角色并排除右侧
  成员。新策略在初始人数不符时不分配，在歧义、额外人员或单个角色
  跟踪间隙超过 2 秒后锁定；只有显式重新分配才能解锁。已验证检测顺序
  改变、短暂缺失、各角色独立计时、锁定持久性和重置后的左到右分配。
  计时使用可注入的单调时钟，测试没有等待真实时间或打开摄像头。
- 用真实 `selfie_segmenter.tflite`（249,537 字节）调用独立
  `ImageSegmenter.segment_for_video`，成功完成两次推理，并确认中间的
  0.1 秒调用被 5 Hz 上限跳过。
- 实际模型 API 的标签为 `['selfie']`，输出类别掩码为 uint8、形状
  `(256, 256, 1)`；合成背景图输出 255，代表隐式背景。人物前景对应
  类别索引 0，因此按 `mask == 0` 统计占比，不能按 `mask > 0` 判断前景。
- MediaPipe 0.10.35 / OpenCV 4.14.0 / NumPy 2.5.3。日志显示 CPU XNNPACK
  推理成功；无可用 GPU 上下文的提示没有妨碍此次测试。

完整合成测试记录在 `synthetic_smoke_result.json`。测试生成图片只有背景
和一个色块，前景占比为 0.0%；这一数据不能用作多人互动、分割准确率、
实时帧率或课程实验结果。

本节 Python 验证未打开摄像头或运行 Unity，也未测试实际三人或四人。真人轮廓效果、
角色控制、交叉遮挡、A/B 性能差异、录像和团队分工都仍需实际执行和记录。

MediaPipe SDK 曾输出一条失败的 Clearcut 遥测上传日志；适配代码本身没有
任何摄像头图像上传功能，模型推理在本机执行，预览使用原有 localhost UDP。
该日志属于依赖库行为，不应将搭建说明解读为已审计整个依赖库的所有网络行为。

## 三人握拳交互的合成验收

`tests/test_three_person_fist_acceptance.py` 是一个不打开摄像头的三人
Play-through rehearsal。它构造 P1/P2/P3 的肩膀和手腕坐标，然后调用生产
桥接使用的 `associate_gestures`，覆盖以下五个场景：三人同时握拳/张手、两
人手腕在中点交叉、P2 短暂遮挡、开拳/握拳识别抖动，以及检测顺序从左到右
变为右到左后的角色重分配。

运行命令：

```bash
cd TrackingAdaptations
python3 -m unittest discover -s tests -p 'test_three_person_fist_acceptance.py'
```

通过标准是 5 项测试全部通过。交叉中点会被保守地判为 `No fresh`，不会把
P2 的手误分给 P1；清晰的下一帧会恢复 P2。短遮挡只清除缺失角色，不能沿用
上一帧的闭拳信号；抖动序列只有最后连续三帧才满足模拟保持条件。检测顺序
变化只改变输入列表顺序，不改变已经分配的 P1/P2/P3 角色。

该测试证明的是坐标关联和数据包边界的确定性，不是 MediaPipe Gesture
Recognizer 的真实识别率、摄像头帧率或 Unity Play 结果。真实三人验收仍需
在 Unity 中打开 `UpperBodyThreePersonCourse.unity`，确认状态面板显示
`gesture recognizer ready`，再用三人分别站在左/中/右位置重复这五个动作并
记录日志。

运行时的 Gesture Recognizer 由 `FistGestureWorker` 单独线程负责：桥接主循环
先发送姿态包，再提交最新的手部帧；模型下载和 MediaPipe 初始化也延迟到第一
个已分配角色的后台任务。结果最多允许滞后三帧或 0.30 秒，身份 revision、
槽位集合或显式重置变化都会使正在运行的旧任务失效。
