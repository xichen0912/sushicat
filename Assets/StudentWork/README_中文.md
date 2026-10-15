# StudentWork：团队场景与实验记录

这是作业的起步工程扩展。它帮助创建团队场景并记录虚拟象的运动代理数据；真实表演、观察、讨论与最终视频需要团队实际完成。

## 第一次打开

1. 用工程指定的 Unity 6000.6.3f1 打开项目根目录，等待 Universal RP 17.6.0 等包解析、资源导入和脚本编译。
2. Console 没有编译错误后，打开已保存的 `Assets/StudentWork/Scenes/TeamPerformance.unity`。首次创建或需要检查缺少的课程/记录器时才运行 **Student Work → Prepare Team Scene**；已有工程无需重建。
3. 准备菜单从 `Assets/DigiPhant/Scenes/DigiPhant.unity` 创建团队副本，在新副本内注入 Savannah Course，保留 `TeamPerformance_BeforeSavannah*.unity` 备份。
4. 无摄像头检查先保存并停止 Play，运行 **Student Work → Validate Setup Without Camera** 或新增 **Validate Three Performer Fixes Without Camera**。若需 Play 滑块检查，先临时关闭 Inspector 的 **Show Camera Preview**；默认开启时进入 Play 会自动使用相机，即使选择 TestSliders。原有 Main Camera 按固定世界坐标偏移跟随虚拟象。
5. 正式真人测试开启相机预览，进入 Play，选 **3 / Upper body / Camera**，按非镜像预览从左到右站成 P1-P3；等待 `visible / inputs ready`，再设置十秒 neutral pose。握拳交互需要 `gesture_recognizer.task` 已安装并显示 `gesture recognizer ready`。缺失提示会列出对应动作，例如 P1 LeftHandHeight。`RESET REQUIRED` 时先恢复站位，点击 Reassign people 后重新校准。单人相机已连接但 neutral 未完成，三人机位与分工仍需实际验证。
6. 需要记录一次实验时，在右上角填写试次名称，点 **Start simulation log**。试次结束后点 **Stop and save simulation log**。它不同时启动表演视频录制；视频使用原有左侧 **Record performance**。
7. 在 `Logs/Simulation/<UTC时间-唯一编号>/` 查看 `trajectory.csv`、`dwell.csv` 和 `summary.json`。打包后的独立应用会写到 `Application.persistentDataPath/Logs/Simulation`。

## 三人模式用握拳捡木头

打开 `UpperBodyThreePersonCourse.unity`，或在 `P3UpperBodyPractice.unity` 的运行面板选择 **Team**，重新让三个人完成 Camera 和 neutral pose。完成 `Tree → Water` 后，P1 把大象带到橙色的木头标记附近并保持停稳，P2 协助对准木头，P3 将右手握拳并保持约 0.35 秒，木头会附着在象鼻末端。右侧面板会显示拾取成功，随后把木头带到终点。

拾取只在三人 Team 模式、P3 的握拳信号有效且大象距离木头约 2.6 Unity units 内生效。首次启动相机桥接时会尝试下载官方 `gesture_recognizer.task`；缺少模型时会安全暂停握拳交互。重新开始课程或停止 Play 会把木头恢复到原来的场景位置。

每次修改角色映射、模式或测量阈值后，请开始新的试次，保持一次记录的条件一致。记录器本身不录制摄像头图像或声音。

## 水交互（握拳）

完成绕树后，P1 将大象带到原水盆附近并停稳。P3 保持右手入镜，张开手后握拳约 0.45 秒即可完成一次符号化的水交互；不再要求杯子检测或右手上下移动。水盆仍是课程顺序的停留区域，系统只记录信号，不模拟真实液体。

## 新副本采用的设置

- 保留参考场景的三人、全身模式、九项关节映射和原有前进/转向映射。
- 新副本的有限移动半径由 15 改为 30 Unity units。参考舞台半径 15 会挡住课程后半段；课程公开要求的半径为 30。这里是从零搭建时作出的选择，原参考场景不变。
- 隐藏新副本内的旧 `Stage` 和距离网格，让课程地面显示清楚。
- 如果项目尚无默认渲染管线，将提供的 URP pipeline 与 renderer 复制到 `StudentWork/Settings`，再引用副本。已有 URP 设置保留；已有其他管线时停止并给出提示。
- 把团队场景加入并启用在 Build Settings 中。生成器与记录器不会在导入项目时自动执行。
- 运行时 Controller/Locomotion 已有明确学生修复：Camera 多人组丢失任一角色时暂停行走/转向，持续失踪要求重新校准；保留人数临时切换前的自定义行走来源；分开显示人可见与动作就绪，拒绝非激活槽位。每包刷新角色存在性，修复 `minimumConfidence=0` 时遗漏角色继承旧时间的边界。新字段 `requireAllPerformersForTravel` 默认开启。starter 中对应 Unity 原文件保留。
- Python 运行时使用保守的独立角色 tracker：三人/四人初始化人数必须准确；歧义、额外人或默认超过 2 秒的角色失踪后锁定，显式重置后恢复。这仍是位置匹配，不能保证人在交叉之后身份正确。

再次执行 **Prepare Team Scene** 会打开已有团队场景，保留角色映射、移动半径、摄像机和记录器设置；已有课程不会被重新生成。缺少课程或记录器时，只补上缺失部分。需要有意重新生成课程时，使用原有 Savannah 菜单，并先保存场景。

`Student Work Runtime` 是独立根物体。不要把记录器放进 `Savannah Course [layout 4]`；生成器管理这个课程根物体，重复注入时可能替换它。课程重新生成后，记录器对旧课程/地标的引用可能失效，需要在 Inspector 重新指定，或在保留旧日志后删除记录器组件并再次执行准备菜单创建新组件。

## 数据如何解释

每个 CSV 行都标记 `data_kind=virtual_proxy_simulation`。位置、速度、距离以 Unity units 为单位，不能直接写成真实象的米、速度或行为。

`trajectory.csv` 记录从点击 Start 起的相对实时时间、三维坐标、每帧实际位移速度、控制器目标速度、累计水平路程、离路径距离、最接近的路段投影位置、当前地标范围、输入模式、人数与校准状态。相对时间不使用现实日期；文件夹名和 `startedUtc` 仅用于区分实验文件。

累计路程按每个观察帧的水平位移相加，CSV 默认约每 0.1 秒采样一次。单帧移动超过 5 units 的位置跳变（如回到起点）不计入路程；超过 0.5 秒的帧间隔也不计入路程或停留。两种排除都有累计计数，`summary.json` 保存阈值。`observed_time_s` 是参与计算的观察时间，可能短于总试次时间。

四个地标默认使用水平半径 4.5 units 的观察区。`inside_time_s` 是处于观察区内的时间；`stationary_inside_time_s` 另外要求实际位移速度不超过 0.1 units/s。处于重叠区时分别累计。它们是几何位置代理，不证明虚拟象正在进食、饮水或有某种体验。

路径投影值可能因倒退或靠近其他路段而减少、跳变，不能据此单独判定按顺序完成路线。停止记录的最后一行保留最后观察到的位置，避免退出 Play 时自动复位污染轨迹。

课程只有视觉网格和装饰地标，没有生成碰撞器。这个记录器没有碰撞事件数据。请结合实际表演视频、团队观察和试次条件解释结果，不要把代理记录写成未经观察的动物行为。

## 本次验证范围

2026-10-07 已在 **Unity 6000.6.3f1 / arm64，revision 45d8eee7de74** 完成 URP 17.6.0 包解析、资源导入和 C# 编译。团队场景、注入前备份、学生 URP 副本及 Build Settings 已实际保存；Game 视图可正确显示纹理大象与障碍场。

**Student Work → Validate Setup Without Camera** 已在编辑模式实际通过坐姿合成包、校准、腿部控制、跟踪超时，以及 26 项步态、世界移动、跟随镜头、转向、手势叠加、多人信号、边界和输入模式重置检查。17:33:50 最新重跑输出 `STUDENT_LOCOMOTION_VALIDATION_OK` 与 `STUDENT_SETUP_VALIDATION_OK`，Console 为 0 errors / 1 warning；警告来自未修改的教师 Savannah 验证器第 116 行的 `AppDomain.GetAssemblies` UAC0005 诊断，没有阻止编译或检查。这些确定性检查不使用摄像头，也不验证实际 Play 重入。验证结束会重新打开保存的场景并恢复有效选择。

另已完成一次无摄像头 **Play / TestSliders** 检查：行走、停止和回到起点。记录器实际写出 `Logs/Simulation/20261007-082714-b7c0a720/`，时长 14.253390 秒、142 条轨迹数据行，数值均为有限值，`data_kind=virtual_proxy_simulation`、`received_frames=0`。这是搭建测试日志，没有放入真人试验表 `Docs/TEST_LOG.csv`，不代表三人表演、路线完成或现场研究数据。

最新静态检查通过 124 项来源指纹、115 个唯一 GUID、10 个明确材质导入输出和 3 个运行时适配；轻量工程包现有 185 个文件，已在本机打包核对；Git/Drive 尚未发布。`editor-import-lock.json` 与 `student-patch-lock.json` 分别锁定确切材质/运行时 SHA-256，核对固定原文件。教师编辑器验证脚本中的模式遗漏和编辑模式生命周期广播问题保留在原文件，学生验证目录采用独立修正。

Python 适配套件 109 项、原有 tracking 套件 11 项、Workshop 采集工具 8 项无相机测试通过。新增三人 Unity 菜单在 17:34:05 实际输出 `THREE_PERFORMER_VALIDATION_OK`，检查独立角色、遮挡诊断、组失踪暂停、来源切换、非激活槽位和零置信度阈值下的缺失角色；本轮编译通过。软件测试不能作为三人真实相机识别率。完整修复与操作见项目根目录 `Docs/THREE_PERSON_BUGS_中文.md`。

用户允许 macOS Unity Hub 相机授权后，第二次 Unity 单人诊断实际接通索引 0：40.075 秒，429 个 pose 包；348 次状态观察中有 306 次 live preview、50 次 tracked、0 次 mapped-ready，手部信号不足及 tracking 中断使 neutral 未成功。JSON 为 `Logs/CameraDiagnostics/Unity-20261007T094436983Z/diagnostic.json`，如实试次已在 `Docs/TEST_LOG.csv`。未保存照片/视频，手势和三人未评估；距离、光照、丢失次数、FPS 未测量。此前命令行和首次 Unity 权限失败无帧属于历史尝试。

重试可先保存场景、停止 Play，运行 **Student Work → Run Single Person Camera Diagnostic**。工具临时用 1 / Seated / Camera，第 10 秒起只在动作就绪时校准，约 40 秒自动停止、恢复保存的三人场景，只保存状态 JSON。让两肩、双手全程可见、双手腰高保持中立。Seated 的 LeftFootLift/RightFootLift 由手高代替，此处无需为了通道名改成全身拍摄。

单人成功 neutral、实际动作及丢失恢复、Play 重入校准、三人合练、完整路线、视频录制播放、表演数据和 Workshop 训练/评估仍需实际完成。姓名、队名、成员与提交链接也待填写。完整记录见项目根目录 `Docs/SETUP_VERIFICATION.md`。

参考控制器在 Play 中默认启动摄像头预览，即使选择 TestSliders；仅做无摄像头检查时，先在 Inspector 临时关闭 **Show Camera Preview**，正式追踪前重新开启。生成器自身的 **DigiPhant → Savannah → Validate Generator** 可在保存场景后另行运行，本次记录不声称该菜单已经通过。

最新单人机位复查约 40.052 秒，收到 589 个 pose 包、358/395 次实时预览观察，但没有 confident P1 或全部映射就绪，neutral 仍未通过。诊断未保存照片/视频，已停止 Play 并恢复三人场景；详见 Docs/SETUP_VERIFICATION.md（学生目录可从项目根打开）及 TEST_LOG。
