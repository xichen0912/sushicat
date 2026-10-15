# 三人控制：发现的问题、修复与现场验收

2026-10-07。老师口头提醒的具体 bug 尚未提供；作业 brief 和两份课堂 PDF 没有给出该 bug 的名称、错误日志或复现步骤。以下来自固定 starter 与本工程代码检查，不能说已确认就是老师指的那一个。

当前已完成代码修复、无摄像头回归及一次接通的真实单人相机诊断；该诊断没有成功 neutral，实际手势、三人交叉遮挡、帧率和成片仍待现场测试。P1/P2/P3 是临时控制槽位，不是人脸识别或真实身份认证。

## 可复现的问题与已采用的处理

| 问题或设计风险 | 固定 starter 的行为 | 本工程的处理 | 适用边界 |
|---|---|---|---|
| 3 → 1 → 3 后自定义行走来源丢失 | `ClampPerformers` 把行走/转向来源的 P2、P3 数值改为 P1，再切回三人也无法恢复原分工 | 保留序列化的原来源，读取时跳过当前未激活的槽位；跳过项也不计入 Average 的权重 | 涉及自定义多来源 locomotion；默认只用 P1 的行走来源可能看不出此问题。身体关节的内建人数映射仍按 starter 规则切换 |
| P2/P3 出画而大象继续走 | 默认行走/转向由 P1 驱动，P1 有效时不必等待 P2/P3 | 新 `requireAllPerformersForTravel=true`：Camera、两人及以上时任何当前成员失踪都暂停世界移动；持续失踪要求重新中立校准 | 这是团队协作的保护策略。短暂丢失恢复且未清除校准时可以继续；身体局部输入仍各自检查置信度。它不证明剩余槽位身份正确 |
| 人显示 visible，脚或手被挡时校准失败，提示不能定位原因 | 有任何一项有效信号便可显示 visible；原校准会拒绝缺失动作，但只显示泛化提示 | 区分“人仍可见”和“全部所需动作就绪”，列出例如 `P1 LeftFootLift` 的缺失动作；校准失败明确清除有效状态 | 原流程已能拒绝无效中立，不是把遮挡帧认成正确校准。新增诊断帮助调机位；单脚低置信度不会被当作整个人消失 |
| 三人初始化时旁人抢到槽位 | 检测到四人时原分配取最左边三人，可能包含旁观者 | 三人/四人初始化必须检测到恰好指定人数；运行中多出检测者则锁住分配并要求重置 | 依据模型检测到的人数；未检测到的旁人无法触发此检查。单人/双人初始化保留原行为 |
| 长时间丢失或一次歧义之后自动使用旧位置恢复角色 | 原 tracker 保存旧位置；没有角色的失效时间；歧义只拒绝当前帧，下一帧可以重新继续 | 独立 `StablePerformerTracker` 为每个角色记录单调时钟；缺失超过默认 2 秒、位置匹配歧义或额外人时锁住全部分配，显式重置后才重新建立槽位 | 2 秒是身份分配阈值，与 Unity 的 0.5 秒 trackingTimeout 不同。短缺失可以保留附近角色，但不保证真实身份 |
| 三人包中的第四槽位仍被接受 | 包检查允许 1-4，未额外限制当前激活人数 | 当前三人拒绝 P4，`IsTracked(4)` 也不会把非激活槽位报告成可追踪 | 防止协议与组大小不一致，不是新增对真实人物的识别 |
| 置信度阈值调到 0 后，遗漏角色仍短暂显示存在 | 原包清零置信度，却保留上次 `lastSeen`；阈值 0 时 `confidence=0` 仍满足阈值，遗漏角色可继续报告 tracked，直到旧时间过期 | 每个有效包先清除所有角色的存在时间，再只为包内角色赋新时间；人数/上下身模式不匹配也清除存在时间，`IsTracked` 明确拒绝未出现标记 | 默认阈值仍为 0.5；修复的是 0 阈值配置边界，不意味着 0 阈值适合实际表演。空包不会保留任何角色 |

Unity 的分组保护在检测到缺失角色时关闭速度/转向输入，不会用平滑继续向前漂移。如果整个数据流停止，需要先经过当前帧的失效时间检查；“暂停”不代表任意摄像头或网络故障都能零延迟发现。持续失踪清除 neutral 的时间从 Unity 发现角色缺失后计算。局部关节低置信度按原映射回归，不因一只脚被挡而强制全组反复校准。

## 固定来源与修改位置

- Unity 学生修复在 `Assets/DigiPhant/Runtime/DigiPhantController.cs` 和 `DigiPhantLocomotion.cs`，保留原命名空间、协议和骨骼映射。`DigiPhantStarter/DigiPhant/Runtime/` 的对应原文件保持固定版本；教师编辑器验证工具也没有修改。
- Python 在 `TrackingAdaptations/bridge.py` 使用新增 `identity_tracking.py`；实际运行的 starter `Tracking` 已应用学生版。原 `PerformerTracker` 类保留作回归对照，运行时使用 `StablePerformerTracker`。姿态特征提取、六项信号和 pose UDP JSON schema 保留。
- 额外的 `ImageSegmenter` 只提供合并人体轮廓及画面占比；它不区分 P1/P2/P3，也不能修复身份交换。
- `ThreePerformerValidation.cs` 新增菜单 `Student Work → Validate Three Performer Fixes Without Camera`。它使用合成 packet，不启动摄像头，结束后恢复保存场景。

## 无摄像头验证与证据范围

Python 适配套件已通过 109 项测试，覆盖固定站位、检测顺序变化、短缺失、长缺失、歧义锁定、旁人、显式重置、拳信号映射和人数边界。原有 11 项 Python 测试也通过。这些是人工坐标与软件协议回归，不能给出真人身份识别率。

新 Unity 菜单在 **17:34:05（Asia/Singapore）** 实际输出 `THREE_PERFORMER_VALIDATION_OK`，本轮 C# 编译通过。检查覆盖：三个独立身体角色；脚被遮挡的校准诊断；P2/P3 缺失时行走停止；持续缺失后重新校准；单脚置信度短缺；3 → 1 → 3 自定义来源保留和 Average 权重；非激活槽位拒绝；`minimumConfidence=0` 下遗漏角色、空包及人数/模式不匹配不继承存在性。17:33:50 也重跑通过坐姿与 26 项 locomotion，输出 `STUDENT_SETUP_VALIDATION_OK` 与 `STUDENT_LOCOMOTION_VALIDATION_OK`。此前无相机 Play 滑块和轨迹导出属于搭建检查。

当次代码验证 Console 为 **0 errors / 1 warning**；保留警告是未改教师 `SavannahCourseValidation.cs:116` 的 UAC0005 `AppDomain.GetAssemblies` 诊断，没有阻止编译或验证。最新静态检查通过 124 项来源指纹、115 个唯一 GUID、10 个确切材质导入与 3 个确切运行时适配输出，核对固定原始来源；轻量工程包现有 185 个文件，已在本机打包核对；Git/Drive 尚未发布。

可运行 Python 回归：在 `TrackingAdaptations/` 用 `../DigiPhantStarter/Tracking/.venv/bin/python -m unittest discover -s tests -v`。Unity 新菜单要求停止 Play，且只打开一个已保存、没有未保存改动的场景。

## 三人正式操作

1. 打开 `Assets/StudentWork/Scenes/TeamPerformance.unity`。退出其他 bridge 和 Workshop 采集窗口，避免占用同一相机。
2. 固定机位，全身模式下把三人脚、髋、肩、手都拍进画面。按**非镜像预览画面左到右**站成 P1/P2/P3；贴地面站位标记，旁观者先退出画面。
3. Unity Play 会启动本机 bridge；P3 课程选择 **3 / Upper body / Camera**。查看每个槽位是 `visible / inputs ready`，并确认拳模型状态为 ready。若有 `P1 LeftHandHeight` 等提示，先解决对应遮挡，不要连续重复校准。
4. 点 **Set neutral pose (10 seconds)**，三人脚着地、肩放松、左手腰高，保持中立，等待 `Neutral pose saved`。人数、上下身模式或角色重置后都重新校准。
5. 每次只让一位成员运动，其他人保持中立，确认前腿/行走转向、后腿/尾巴、头鼻耳分别对应 P1/P2/P3；再共同慢速导航。
6. 若预览出现 `RESET REQUIRED`，先停下。恢复原站位、移走旁人，点击 Unity **Reassign people**，检查新槽位后重新校准。独立 OpenCV 窗口也可按 **R**，但仍需在 Unity 重新校准；Unity 自动 bridge 没有独立窗口时使用 Unity 按钮。
7. 紧急停止可关闭 **Enable locomotion** 或退出 Play。不要通过关闭 `requireAllPerformersForTravel` 绕过角色丢失提示来拍成片。
8. 分别录制每人控制演示和集体障碍场尝试，启动独立模拟日志，结束后核对 MP4、CSV 与试次条件。只把真正发生的现场结果填入 `Docs/TEST_LOG.csv`。

## 仍然存在的局限与待测项

位置匹配没有人的外观或身份信息。两个人快速互换位置、交叉动作恰好形成唯一近邻解时，算法可能没有歧义提示却交换角色；锁定机制减少若干不安全恢复路径，不能保证所有交叉都正确。当前解决办法是固定站位、保持间距、避免穿插，出错后重置并校准。若必须允许自由交叉，需要外观追踪、标识物或多机位方案及新的真人验证；本工程没有实现这些方法。

真人待测：单人成功 neutral 与实际手势、三人全身机位、校准成功率、每人分工、P2/P3 出画暂停及恢复、超过两秒丢失/歧义后的重置、旁人进入、交叉身份风险、分割开启/关闭的帧率与延迟、合屏视频播放。姓名、队名、个人适合该角色的原因、团队 Git/Drive、Part 1 样本与训练仍由真实信息完成。报告的合成输入结果不能替代这些证据。

## 实际相机发现与下一次重试

此前命令行和首次 Unity 尝试因权限失败无帧。用户允许 macOS Unity Hub 相机授权后，第二次 Unity 单人诊断已实际连通，日志是 `Logs/CameraDiagnostics/Unity-20261007T094436983Z/diagnostic.json`：**40.075 秒、429 个 pose 包**；348 次状态观察中 **306 次 live preview、50 次 tracked、0 次 mapped-ready**。手部相关信号置信度不足，并有 `P1 tracking` 中断，因此 neutral 始终未成功，不能继续声称手势或三人控制通过。

这是单人取景/追踪就绪问题的现场证据，不能由此判定老师所说的三人 bug 或量化三人身份误配。距离、光照、丢失次数及 FPS 没有测量；包计数和状态观察计数不是同一种指标。未保存原图/视频，实际试次已登记 `Docs/TEST_LOG.csv`，不属于 Part 1 训练数据或最终成片。

调整后可在保存场景、停止 Play 时运行 **Student Work → Run Single Person Camera Diagnostic**。工具临时设 1 / Seated / Camera，约第 10 秒起只在全部所需信号就绪时校准，约 40 秒自动停止并恢复保存的三人场景，只保存诊断 JSON。让两肩、双手一直入镜、双手腰高保持中立。坐姿模式的 `LeftFootLift`/`RightFootLift` 是手高代理，不需要为了这两个通道名把机位改成拍脚。成功 neutral 后还需另做真实手势及三人测试。

最新单人机位复查约 40.052 秒，收到 589 个 pose 包、358/395 次实时预览观察，但没有 confident P1 或全部映射就绪，neutral 仍未通过。诊断未保存照片/视频，已停止 Play 并恢复三人场景；详见 Docs/SETUP_VERIFICATION.md（学生目录可从项目根打开）及 TEST_LOG。
