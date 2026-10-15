# DigiPhant setup 与验证记录

记录日期：2026-10-07（Asia/Singapore）。当前状态：Unity 安装、许可、完整导入与 C# 编译已完成；团队场景已保存，无摄像头合成输入、运动验证和实际 Play 滑块基本检查通过，并导出模拟轨迹。单人真实相机已连通，但所需手部信号不足及追踪中断，neutral 未成功；动作控制、三人合练、表演数据与最终录制待完成。

## 实际环境

| 项目 | 实际状态 |
|---|---|
| 电脑 | macOS 27.0.1，arm64 / Apple Silicon |
| Python | 原始环境 3.14.8；桌面摄像头兼容环境 3.12.15 |
| 隔离环境 | Unity 优先使用 `DigiPhantStarter/Tracking/.venv312`，旧 `.venv` 保留作回退 |
| 依赖 | 桌面兼容环境：MediaPipe 0.10.21；opencv-contrib-python 4.11.0.86；NumPy 1.26.4 |
| Unity | 已离线安装 6000.6.3f1 / arm64，revision 45d8eee7de74；路径 `/Applications/Unity/Unity-6000.6.3f1/Unity.app`；许可验证通过，用户已修复权限，完整 app 签名验证通过 |
| Unity Hub | 已安装中国版 3.3.6 / arm64，位于 `/Applications/Unity Hub.app`；编辑器和工程已添加，Personal 许可已激活，详见 `UNITY_INSTALL_STATUS.md` |
| 渲染管线 | URP 17.6.0 已实际解析并生成 packages-lock.json；完整资源导入与 C# 编译通过 |
| 摄像头 | 用户允许 macOS Unity Hub 相机授权后，第二次 Unity 单人诊断实际接通索引 0，相机预览及 pose 包已收到；neutral 未成功，动作及三人未验收。此前命令行和首次 Unity 尝试因权限失败 |

URP 17.6.0 已在本机 Unity 编辑器中实际解析成功。老师的 starter 参考环境为 6000.6.0f1，障碍场记录的参考验证版本是 6000.6.3f1；用户确认同学使用 6000.6.3f1，本工程保持该精确版本。

## 版本与目录

- Starter：`https://github.com/kommanderpi/studentstarter.git`
- 固定 revision：`4e6dab5271c618d1c0bfb2022c41c0d37cd8d69f`；见 `starter-lock.json`。
- 克隆位于 `DigiPhantStarter/`，与 Assets 同级，不进入 Unity 的 Assets。
- `Elephant/`、`DigiPhant/`、`SavannahCourse/` 及原有 `.meta` 已完整导入 Assets。原始参考场景为 `Assets/DigiPhant/Scenes/DigiPhant.unity`。
- 正式团队场景已生成并保存：`Assets/StudentWork/Scenes/TeamPerformance.unity`。
- 注入障碍场前备份已保存：`Assets/StudentWork/Scenes/TeamPerformance_BeforeSavannah.unity`。学生 URP 资产和团队场景 Build Settings 也已准备。

日常启动：在 Hub 点击 **DigiPhant_P2**，或双击 `OpenUnity.command`，再打开已保存的 TeamPerformance 场景。需要重复搭建检查时，先保存场景，再运行 **Student Work → Validate Setup Without Camera**。

Game 内控制面板显示不全时，右键 **Game** 标签 → **Maximize** 展开视图。

## 数据路径

摄像头 → Python MediaPipe PoseLandmarker → 六个身体相对信号 → localhost UDP 5055 → Unity 校准 / 映射 / 平滑 → 大象。

同一摄像头帧 → 独立 ImageSegmenter → 人体轮廓与画面像素占比 → 原有 JPEG 预览（UDP 5057）。Python 命令接收端是 5056，协议和 pose JSON 结构保留。

Unity Game view → 参考录制器 → `Recordings/<session>/performance.mp4`。轨迹记录器单独启动 → `Logs/Simulation/<UTC-id>/trajectory.csv` 和 session 说明。运行时生成的数据不进 Git。

## 本次改动

1. 新工程的 `Assets/StudentWork/Editor/TeamSceneSetup.cs`：显式菜单创建团队副本，配置缺失 URP、隐藏参考地板、注入固定障碍场、增加记录器并添加 Build Settings。重复运行保留既有场景内容。新副本的移动半径由 15 扩到 30，避免在完整路径中途触及模板边界；没有改变原参考场景。
2. `SimulationTelemetryLogger.cs`：用户开始/停止日志，记录虚拟代理轨迹、实际位移速度、XZ 路程、最近路径投影、靠近地标和静止停留；标注跳变和帧间隙。路径投影不证明按顺序完成课程。无碰撞器的装饰性地标不生成碰撞记录。
3. `TrackingAdaptations/bridge.py`、`segmentation_support.py` 与 `identity_tracking.py`：完整可复现 Python 修改，已复制到本机 starter 的 Tracking 中；保留原始 pose 特征、六项信号和 JSON schema，运行时角色分配改用独立 `StablePerformerTracker`。三人/四人初始化要求恰好指定人数；歧义、额外人或默认超过 2 秒的角色丢失会锁住分配，显式重置后才恢复。原 `PerformerTracker` 保留作为对照。先发送已算好的 pose 包，再运行附加分割和绘制预览；分割仍可能降低后续帧率。目标采样上限 5 Hz，不是已测真人帧率。
4. 新增启动脚本、锁定信息、恢复脚本、静态检查、中文映射 / 排练 / journal 文档及 Workshop 采集框架。
5. `Assets/StudentWork/Editor/TeamSceneValidation.cs`：独立菜单在编辑模式测试坐姿合成 Camera 输入与跟踪超时，并调用学生 locomotion 验证，最后恢复之前保存的场景。拒绝 Play、未保存场景和未保存修改，不启动摄像头或 Python。教师验证菜单在 UDP 测试后遗漏 Camera 模式恢复，原始脚本保持不变。
6. `Assets/StudentWork/Editor/StudentLocomotionValidation.cs`：保留教师来源署名的验证副本，保留 26 项运动检查，仅将曾触发 `ShouldRunBehaviour` 断言的 `SendMessage("OnEnable")` 改为公开 `SetInputMode(TestSliders)` 的重置检查。它没有验证真实 Play 重入时的生命周期或摄像头校准。
7. `Assets/DigiPhant/Runtime/DigiPhantController.cs` 与 `DigiPhantLocomotion.cs`：添加所需动作就绪诊断、非激活槽位拒绝、多人角色丢失暂停/持续丢失后清除校准；修复 3 → 1 → 3 把自定义 P2/P3 行走来源改成 P1 的问题。每包显式刷新当前角色存在性，修复 `minimumConfidence=0` 时遗漏槽位、空包或模式/人数不匹配仍使用旧时间报告 visible 的边界。低置信度单脚与整个人丢失分开处理。starter 中对应 Unity 原文件及教师编辑器工具未改。完整复现、操作与边界见 `Docs/THREE_PERSON_BUGS_中文.md`。
8. `Assets/StudentWork/Editor/ThreePerformerValidation.cs`：新增 **Student Work → Validate Three Performer Fixes Without Camera**。使用合成 Camera packet 检查独立三人角色、遮挡诊断、组暂停、重新校准、自定义来源保留、非激活槽位及零阈值下的遗漏/空包/模式与人数不匹配，实际菜单已通过；不打开相机。
9. `Workshop/collect_samples.py`：所有类别/划分/轮次/相机选项显式指定，空格逐帧保存原相机 PNG 和真实采集清单；跨同一项目 train/test/类别拒绝完全重复。无自动批量生成、训练或上传。Pose 图片仍需在真正 TM Pose Project 内创建/采集/训练。
10. `Assets/StudentWork/Editor/SingleCameraValidation.cs`：显式菜单 **Student Work → Run Single Person Camera Diagnostic**，在 Play 临时设为 1 / Seated / Camera；第 10 秒开始检查全部输入就绪，只有就绪时才校准，未就绪每约 1 秒重查。运行约 40 秒后自动退出 Play、恢复保存的三人场景，保存真实状态 JSON，不保存原图或视频。

## 控制配置与恢复

通用移动演示初始为三人 / Full body。P1：前腿、左手行走、躯干转向；P2：后腿、躯干尾摆；P3：躯干头转、右手鼻卷、手距双耳。P3 握拳课程另用三人 / Upper body，以便桥接下发拳信号。具体中立姿态与参数见 `Docs/MAPPING_中文.md`。按非镜像预览从左到右分配，保持固定站位。

默认 **Show Camera Preview = true** 时，Play 会自动启动参考 launcher 管理的 bridge 和摄像头预览，**即使控制模式仍是 TestSliders**。本次仅在暂时关闭预览后进行了无摄像头 Play 滑块检查；结束后已恢复保存的场景，因此默认预览仍开启、输入为中立 TestSliders。仅检查搭建时使用无摄像头验证菜单。手动 `LaunchCamera.command` 和 Unity 自动启动不要并行。真人试验先用 1 / Seated 独自检查，再使用三人全身模式。人数或模式变化后重新校准；Reassign people 后也要重新校准。手动窗口按 Q 退出；Unity 启动的进程随退出 Play 停止。内建播放器运行需另启 bridge，当前只准备编辑器流程。

开启 Camera 后检查 `visible / inputs ready`，按十秒中立校准，等待 Neutral pose saved；左手腰高、肩松、脚着地。P3 拳课程使用 Upper body 模式，确认面板中的 Fist detector 状态为 ready，并先张开手再握拳。出现 `P1 LeftHandHeight` 等缺失提示先调整对应遮挡。Camera 两人及以上默认开启 `requireAllPerformersForTravel`，任一当前角色丢失就暂停世界移动，持续缺失需要新 neutral。Python `RESET REQUIRED` 后恢复站位，点 Reassign people，再重新校准。Unity 追踪失效阈值为 0.5 秒、最低置信度 0.5；Python 身份分配失效默认 2 秒，两者用途不同。几何匹配仍可能在交叉时误配，正式表演保持站位。需要立即停下时关闭 Enable locomotion 或 Stop Play。

单人诊断重试时先保存、停止 Play，运行 **Run Single Person Camera Diagnostic**，让两肩、双手持续入镜，手放腰高保持中立。Seated 模式用手高替代腿信号，`LeftFootLift`/`RightFootLift` 的缺失通常也来自对应手部关键点；当前单人重试不要求把脚拍进画面。该菜单不会自动验证手势；第 10 秒就绪检查与约 40 秒停止/恢复由工具执行，结果仍应按 JSON 读取。

## 实际验证

- 依赖安装成功；`pip check` 返回 No broken requirements found。
- 老师的 11 项 Python 测试通过（pose/ID/命令、UDP JPEG 预览、时间戳录制编码）。测试不打开摄像头，不证明 Unity 录像通过。
- Python 适配套件 109 项测试通过，包括分割/pose 回归、保守角色分配、拳信号映射、后台 worker 和桥接协议检查。原始 pose 特征、原 tracker 对照类、人数命令校验及 JSON 构造一致；运行时使用新 tracker。合成坐标测试不证明真实交叉正确率或拳识别率。
- 真实分割模型在合成背景图上的两次 CPU 推理成功，采样间隔限制被验证。官方输出标签 `['selfie']`：类别 0 是人体，255 是隐式背景；合成背景占比 0%。这不是准确率、真人性能或课程研究数据。
- 最新静态检查通过 124 项来源指纹、115 个唯一 GUID、10 个确切材质导入输出、3 个运行时学生适配；轻量工程包现有 185 个文件，已在本机打包核对；Git/Drive 尚未发布。`editor-import-lock.json` 和 `student-patch-lock.json` 分别锁定确切导入/修复 SHA-256，并核对固定原文件。教师编辑器工具与 starter 对应 Unity 原文件保留。
- 完整 Unity 导入约 69 秒，URP 17.6.0 包解析及项目/编辑器 C# 程序集编译通过，没有 C# 编译错误或包解析失败。
- 原 Python 3.14 / MediaPipe 0.10.35 的 Gesture Recognizer 会在当前 macOS 原生 Metal 图初始化时 abort。已安装独立 Python 3.12 / MediaPipe 0.10.21 环境；Unity 自动桥接和 `LaunchCamera.command` 优先使用 `.venv312`，避免该原生崩溃。兼容环境在无桌面 OpenGL 服务的 shell 中会返回可处理的 `kGpuService` 错误，不再直接退出进程。
- 2026-10-11 从 Unity Hub 打开 `UpperBodyThreePersonCourse` 并进入 Play：实际摄像头预览显示、收到 1280×720 帧，日志中的 `finalFistDetectorStatus` 为 `gesture recognizer ready`，`fistInferenceMs` 约 20 ms。此次画面只有 1 位真人，`maximumAssignedPeople=3` 属于区域候选峰值而非三位真人验收；neutral 未保存，三人交叉和握拳动作仍未现场完成。
- 团队场景、注入前备份及学生 URP 资产实际保存；初始着色器加载后，Game 视图正确显示带纹理的大象、地面和树木。
- **Student Work → Validate Setup Without Camera** 17:33:50 最新实际 UI 运行通过：坐姿合成输入、基线校准、腿部旋转超过 10°、跟踪超时回到中立；学生副本的 26 项逐帧 locomotion 检查通过步态、移动/转向、叠加手势、多人信号组合、追踪丢失、边界与公开输入模式重置。Console 输出 `STUDENT_SETUP_VALIDATION_OK` 和 `STUDENT_LOCOMOTION_VALIDATION_OK`，显示 **0 errors / 1 warning**。保留的警告来自未修改的教师 `SavannahCourseValidation.cs:116`，UAC0005 针对 `AppDomain.GetAssemblies`；没有阻止编译或验证。这些是编辑器中的合成输入与逐帧检查，没有打开摄像头。
- 实际 Play 滑块基本检查通过：暂时关闭 Show Camera Preview 后，用 TestSliders 观察动画与 Walk，检查停止及重置，导出 **14.253 秒、142 行轨迹**；全部数值为有限值。日志标记 `data_kind = virtual_proxy_simulation`、`initialInputMode = TestSliders`、`received_frames = 0`。随后恢复保存的团队场景，Show Camera Preview 为 true，输入为中立 TestSliders。没有打开摄像头，也未验证真实 Play 重入校准、真人追踪或三人合练。
- 教师 **DigiPhant → Validate Saved Template** 实际运行曾报 “Seated hand input failed to drive leg”。原因是 `DigiPhantSetup.cs` 第 187 行改回 `InputMode.TestSliders` 后，后续坐姿合成测试未恢复 Camera；独立学生菜单修正验证路径，未更改原脚本。
- Workshop 采集工具 8 项纯文件/参数测试通过，覆盖必填参数、无效类别/相机、路径约束、跨 train/test 完全重复、采集记录和 Pose 原图类型；测试只写临时目录，不打开摄像头，也没有给 Workshop 添加真实样本。
- 新三人 Unity 菜单 17:34:05 实际输出 `THREE_PERFORMER_VALIDATION_OK`，包含最新 `minimumConfidence=0` 缺失角色边界；本轮编译通过。它仍是无相机合成输入验证，不是三人真人结果。详情见 `Docs/SETUP_VERIFICATION.md`。
- 第二次 Unity 单人真实相机诊断持续 40.075 秒，收到 429 个 pose 包；348 次观察中 306 次 live preview、50 次 tracked、0 次 mapped-ready，neutral 从未成功。记录提示手部所需信号不足及 tracking 中断；没有测量距离、光照、丢失次数或 FPS。没有保存照片/视频，手势与三人均 `not_assessed`。JSON 为 `Logs/CameraDiagnostics/Unity-20261007T094436983Z/diagnostic.json`，实际试次已登记 `Docs/TEST_LOG.csv`；结束恢复保存场景。
- 轻量包恢复结果另见 `Docs/RECONSTRUCTION.md`；所有验证都应区分静态结果与现场结果。

## 尚需用户实际完成

调整单人取景后重试并完成真实 neutral；实际手势、Play 重入校准及丢失恢复；真实三人分工与排练；一项实际对比改进；合屏 MP4 录制与播放检查；实际表演数据；Teachable Machine 真数据训练与测试；填写姓名、队名、成员及 Git/Drive 链接。相机连通诊断不代表这些作业内容已完成。

## 轻量 Git 与恢复

本项目已初始化独立本地 Git（分支 `codex/p2-setup`），文件已暂存，尚未提交或发布到 GitHub。大象资源、模型、环境、视频、数据与 Unity 缓存已忽略。

新机器执行 `python3 tools/restore_starter.py` 恢复锁定重型资源和 Python。恢复脚本不会覆写已有不同文件；现代 Unity 升级可能修改资源序列化，需检查实际差异。打开已提交的团队演出场景，不能用参考副本覆写团队编辑。

提交版本时，应把已保存的团队场景、`.meta`、学生 URP 资产、Packages/packages-lock.json 和编辑器生成的 ProjectSettings 一并纳入 Git。当前搭建与合成验证结果不能替代现场表演的最终证据。

最新单人机位复查约 40.052 秒，收到 589 个 pose 包、358/395 次实时预览观察，但没有 confident P1 或全部映射就绪，neutral 仍未通过。诊断未保存照片/视频，已停止 Play 并恢复三人场景；详见 Docs/SETUP_VERIFICATION.md（学生目录可从项目根打开）及 TEST_LOG。
