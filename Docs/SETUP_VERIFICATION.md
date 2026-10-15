# 搭建与验证记录

日期：2026-10-07（Asia/Singapore）。本记录区分工程检查、合成输入和现场研究；没有把搭建日志作为真人试验结果。

## 编辑器与保存的工程

- 实际编辑器：**Unity 6000.6.3f1，revision 45d8eee7de74，macOS Apple Silicon / arm64**；路径 `/Applications/Unity/Unity-6000.6.3f1/Unity.app`。与同学确认使用的版本一致。
- URP **17.6.0** 已解析并生成 `Packages/packages-lock.json`；完整资源导入、运行时及编辑器 C# 程序集编译通过。Game 视图已正确显示带纹理的大象、黄色地面和绿色树木。
- 已保存 `Assets/StudentWork/Scenes/TeamPerformance.unity`，以及注入前备份 `Assets/StudentWork/Scenes/TeamPerformance_BeforeSavannah.unity`。
- `StudentWork/Settings/TeamURP.asset` 和 renderer 副本引用有效；团队场景已在 Build Settings 中启用，场景 GUID 与 `.meta` 相符。

## 场景与代码检查

保存场景中有且仅有一个 DigiPhantController、locomotion 和轨迹记录器：P3 课程使用三人 / Upper body，初始 TestSliders，九项身体滑条及前进/转向滑条为中立值；移动半径 30。骨骼、Animator、移动根与跟随镜头引用均有效。

Savannah 为固定 layout 4。记录器位于课程外的独立根物体；11 个路径点与 Open path 网格成对顶点的中心一致，四个地标观察区引用完整。此课程的地标是装饰性几何，没有碰撞事件测量。

最新静态检查已通过 **124 项来源指纹、115 个唯一 GUID、10 个材质导入输出、3 个运行时适配**，`.meta` 齐全；轻量工程包现有 **185 个文件**，已在本机打包核对；尚未发布到 Git/Drive。Unity 导入只对 10 个材质的 `_Color` 作小于 `0.000001` 的浮点舍入；`editor-import-lock.json` 记录锁定来源与确切导入输出 SHA-256。三份运行时修改用 `student-patch-lock.json` 锁定确切输出，核对 starter 中未改的固定原文件。检查器不按数值容差忽略其他变化。

教师参考场景和编辑器工具没有改写。当前导入的 `DigiPhantController.cs`、`DigiPhantLocomotion.cs` 有明确的学生修复；`DigiPhantStarter/DigiPhant/Runtime/` 中对应原文件保持固定版本。保留的教师验证问题包括：UDP 检查后未为后续坐姿合成包恢复 Camera 模式；在 Edit 模式 `SendMessage("OnEnable")` 触发两个原生 `ShouldRunBehaviour()` 断言。学生目录使用独立、注明来源的验证流程：坐姿包显式选择 Camera；运动验证使用公开 `SetInputMode(TestSliders)` 测试清除校准和运动，避免手动广播生命周期回调。

## 已实际运行的检查

**Student Work → Validate Setup Without Camera** 在 17:33:50（Asia/Singapore）最新 UI 重跑通过，Console 为 **0 errors / 1 warning**，出现：

- `STUDENT_LOCOMOTION_VALIDATION_OK`：26 项运动断言，覆盖步态、世界位移、跟随镜头、前后运动、原地和移动转向、手势叠加及不累积、多人信号平均/求和、跟踪丢失与过期、边界、停止和输入模式重置。
- `STUDENT_SETUP_VALIDATION_OK`：坐姿合成输入、基线校准、腿部旋转响应与跟踪超时回归，以及上述运动检查。

保留的 1 条警告是 UAC0005，来自未修改的教师 `Assets/SavannahCourse/Editor/SavannahCourseValidation.cs:116` 使用 `AppDomain.GetAssemblies` 的诊断；没有阻止本轮编译和菜单验证。历史“0 warnings”是较早的搭建检查，不能当作最新 Console 状态。

这些是编辑模式中的合成包和逐帧检查；没有打开摄像头，不验证实际 Play 重入。每次结束（包括断言抛出时）重新打开原来保存的场景，不保存合成测试状态，并恢复有效选择及 Inspector 重绘。历史 Editor.log 保留早先的教师验证错误，最新通过记录不删除历史。

另外实际进入 Play，以 **TestSliders** 完成无摄像头的行走、停止和回到起点检查。轨迹记录器开始/停止后生成三个文件：`trajectory.csv`、`dwell.csv`、`summary.json`，实际目录为：

`Logs/Simulation/20261007-082714-b7c0a720/`

这次日志名为 `SetupSmokeTestSliders`，持续 **14.253390 秒**；142 条轨迹数据行（不含标题行），2,272 个数值字段均为有限值。`dataKind` / `data_kind` 为 `virtual_proxy_simulation`，`initialInputMode=TestSliders`，所有行 `received_frames=0`、输入模式为 TestSliders，记录动作只有 Idle 和 Walk。这证明滑条运行与日志写入，不证明摄像头、真人校准、按序完成路线、真实大象行为或碰撞。

该滑块搭建日志未加入 `Docs/TEST_LOG.csv`；试验表现已另有第二次真实单人相机诊断的如实记录。模拟日志采用 Unity units；不要换写成未经实测的米、真实象速度或真实行为结果。

## 本轮三人代码修复与实际验证

代码对照可复现自定义行走/转向来源在 3 → 1 → 3 后被改为 P1、三人包接受非激活 P4、原 tracker 没有长缺失身份失效与持续歧义锁定等问题。补充修复 `minimumConfidence=0` 时遗漏槽位/空包/人数或模式不匹配仍继承旧时间而报告存在的边界。另新增默认多人组保护，防止 P2/P3 丢失而 P1 独自继续移动，并使校准缺失提示定位到具体 P/动作。它们是否就是老师口头提到的 bug 尚未确认。

Python 适配套件已通过 **109 项**，原有 **11 项** tracking/preview/recording 测试通过；Workshop 采集工具 **8 项**纯文件/参数测试通过。测试不使用真实相机或 Gesture Recognizer 模型；Workshop 的真实样本和评估清单仍为空。

新菜单 **Student Work → Validate Three Performer Fixes Without Camera** 在 **17:34:05** 实际输出 `THREE_PERFORMER_VALIDATION_OK`，本轮 Unity 编译通过。覆盖独立 P1/P2/P3、缺失动作校准提示、P2/P3 丢失停止、持续缺失重新校准、单脚置信度变化、自定义来源切换/Average 权重、非激活槽位及零置信度阈值下的遗漏/空包/人数与模式不匹配边界。菜单结束恢复保存的团队场景；这些是合成输入和代码回归，未打开相机，不验证真人身份准确率或三人现场表演。

详细修复、固定站位与 `RESET REQUIRED` 恢复流程见 `THREE_PERSON_BUGS_中文.md`。角色位置追踪仍无法保证交叉之后的真实身份；三人真人、丢失恢复、A/B 帧率与最终成片仍需现场测量。

## 实际单人相机诊断

命令行及首次 Unity 尝试因权限失败，没有取得图像帧；这些是先前失败历史。用户允许 macOS 的 Unity Hub 相机授权后，第二次 **Student Work → Run Single Person Camera Diagnostic** 在 1 / Seated / Camera、索引 0 下实际接通相机。诊断 JSON：

`Logs/CameraDiagnostics/Unity-20261007T094436983Z/diagnostic.json`

记录时长 **40.0753185 秒**，收到 **429 个 pose 包**。**348 次状态观察**中，**306 次 live preview、50 次 tracked、0 次 mapped-ready**；neutral 始终未成功。数值是不同种类的计数：pose 包不等于观察样本，不把它们换算为摄像头 FPS、识别率或身份正确率。

诊断曾指出手部相关信号置信度不足，并有 `P1 tracking` 中断提示。Seated 中 LeftFootLift/RightFootLift 由左右手高替代，不能因为这两个名字便推断脚出镜不足。现有记录不足以确定具体距离、光照或误识别原因；丢失次数、机位距离、光照及 FPS 均未测量。

`ever_calibrated=false`、`gesture_test=not_assessed`、`three_person_test=not_assessed`；没有成功的校准后信号范围。`raw_images_saved=false`、`video_saved=false`；没有保存照片/视频，只有状态 JSON。工具结束后 `restored_saved_scene=true`，恢复保存的三人团队场景。实际失败试次已写入 `Docs/TEST_LOG.csv`，不属于 Workshop 训练数据或最终表演视频。

调整单人取景后可重试：保存场景、停止 Play，运行 **Student Work → Run Single Person Camera Diagnostic**。它临时设 1 / Seated / Camera，约第 10 秒开始检查动作就绪，未就绪每约 1 秒再检查；只有就绪才调用 neutral 校准，约 40 秒自动停止并恢复保存场景。让两肩、双手全程入镜，双手腰高保持中立；没有自动手势验证或自动拍照。

## 仍需现场完成

单人取景调整及成功 neutral、实际手势控制、遮挡/丢失恢复与 Play 重入校准；真实三人分工、排练和完整路线；实际对比改进；合屏表演视频录制及播放核对；表演试次观察/数据；Workshop 真样本采集、训练、评估与讨论；姓名、队名、成员及 Git/Drive 提交链接。已连接的相机诊断和合成回归不能替代这些尚未完成的证据。

参考控制器默认在 Play 启动摄像头预览，即使仍选择 TestSliders。只检查滑条时先临时关闭 **Show Camera Preview**；正式人体追踪前重新开启，并在改变人数、模式或槽位后重新校准。当前保存的团队场景保留摄像头预览开启设置，供后续真人检查。

## 最新机位复查

随后从 Unity 的相同单人诊断入口复查 40.052 秒：接收 589 个 pose 包，395 次状态观察中 358 次实时预览、0 次 confident P1、0 次全部映射动作就绪，校准未通过。相机权限与传输已经正常；本轮没有足够可信的人物姿态，无法进一步判断具体物理原因。记录为 `Logs/CameraDiagnostics/Unity-20261007T101809751Z/diagnostic.json`，并已加入 TEST_LOG。未保存照片/视频；没有动作成功或三人测试声明。结束后 Play 已停止，保存的三人场景恢复。
