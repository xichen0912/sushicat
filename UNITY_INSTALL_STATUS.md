# Unity 安装与工程搭建完成记录

2026-10-07（Asia/Singapore）

## 已安装

- Unity Hub 3.3.6，位置 `/Applications/Unity Hub.app`。
- Apple Silicon / arm64 原生可执行文件。
- Hub 来自 Unity 官方下载域名；其安装包 SHA-512 与官方更新 metadata 一致，macOS codesign 验证通过，Gatekeeper 识别为 Notarized Developer ID。
- 用户已自行离线安装编辑器，实际路径 `/Applications/Unity/Unity-6000.6.3f1/Unity.app`。
- 已读取编辑器 Info.plist：版本 **6000.6.3f1**，revision **45d8eee7de74**，原生 **arm64**。Gatekeeper 判定 accepted / Notarized Developer ID。原始 `.pkg` 未在 Downloads 找到，因此本次未独立核对它的 MD5。
- 已通过 Hub **Locate existing installation** 选中该 `Unity.app`；Installs 页面实际显示 **6000.6.3f1 / SILICON**，Hub 注册记录标记为手动安装。
- 用户已执行 `RepairUnityPermissions.command` 修复安装目录读取/执行权限；修复后的完整 app 签名验证通过，所需运行文件可正常读取和执行。

当前 Hub 是中国地区发行版，未安装其推荐的旧版 2022.3.62f3c1。用户已登录并激活许可，Hub 实际显示 **Current license: Personal**。

## 工程已完成导入与验证

已在 Hub 添加并打开 **DigiPhant_P2**，编辑器版本为 6000.6.3f1。启动日志确认编辑器使用自己的内置许可客户端成功解析 entitlement 并更新许可；先前国内 Hub 旧许可客户端的 handshake 警告随后恢复。

资源首次完整导入约 69 秒；`Packages/packages-lock.json` 已生成，URP **17.6.0** 实际解析成功。项目和编辑器 C# 程序集编译、加载成功，没有 C# 编译错误或包解析失败。

已保存：

- 团队场景：`Assets/StudentWork/Scenes/TeamPerformance.unity`
- 注入障碍场前备份：`Assets/StudentWork/Scenes/TeamPerformance_BeforeSavannah.unity`
- 学生 URP 资产及团队场景 Build Settings；参考场景保留。

**Student Work → Validate Setup Without Camera** 已实际通过坐姿合成输入、校准、腿部控制、跟踪超时回归及 26 项 locomotion 检查，17:33:50 最新 Console 输出 `STUDENT_SETUP_VALIDATION_OK` 与 `STUDENT_LOCOMOTION_VALIDATION_OK`。Console 为 **0 errors / 1 warning**；警告是未修改的教师 `SavannahCourseValidation.cs:116` 中 `AppDomain.GetAssemblies` 的 UAC0005 诊断，没有阻止编译或验证。验证在编辑模式执行，结束后恢复保存的团队场景。

初始着色器加载后，Game 视图已正确显示带纹理的大象、地面和树木。随后暂时关闭 **Show Camera Preview**，实际进入 Play，用 TestSliders 验证可见动画、Walk、停止/重置，并成功导出 **14.253 秒、142 行轨迹**。数值均为有限值；日志标记 `data_kind = virtual_proxy_simulation`、`initialInputMode = TestSliders`、`received_frames = 0`，全程没有打开摄像头。

检查后恢复保存的团队场景，当前 **Show Camera Preview = true**，输入为中立 TestSliders。正常进入 Play 即使选择 TestSliders 也会自动启动摄像头预览；仅检查搭建时使用上述无摄像头菜单。后续单人真实相机已连接，但 neutral 未成功；**实际动作控制、Play 重入校准、三人合练和最终录像仍未验收。**

2026-10-11 更新：原 Python 3.14 / MediaPipe 0.10.35 的 Gesture Recognizer 在 macOS Metal 初始化时会原生 abort。已安装 Python 3.12 / MediaPipe 0.10.21 到 `DigiPhantStarter/Tracking/.venv312`，Unity 与 `LaunchCamera.command` 已优先使用该环境。Unity Hub 桌面 Play 已实际显示摄像头预览，`gesture recognizer ready` 和约 20 ms 拳推理均出现；本次只有 1 位真人，三人交叉和握拳现场验收仍待三位真人。

## 已解决的问题与保留的来源

- 移除了不存在的 `com.unity.modules.webcam` 清单依赖；本机 `UnityEngine.AudioModule` 已提供 `WebCamTexture`。URP 版本保持 17.6.0。
- `OpenUnity.command` 支持实际离线安装路径和 Hub 标准路径，启动前核对精确编辑器版本。
- 原安装的 Burst 工具和部分 Mono DLL 权限不足已由用户修复，完整签名随后验证通过。
- 教师 **DigiPhant → Validate Saved Template** 在第 187 行切回 `InputMode.TestSliders` 后，没有为后续坐姿合成输入切回 Camera，因而错误报告腿部没有响应。原始教师脚本保留；新增学生目录下的验证菜单修正验证流程。
- 原有 locomotion 验证用 `SendMessage("OnEnable")` 检查重置，曾触发 `ShouldRunBehaviour` 编辑器断言。新增保留来源署名的 `StudentLocomotionValidation` 副本保留 26 项检查，将该手动生命周期调用替换为公开的 `SetInputMode(TestSliders)` 重置检查；教师源文件未修改。
- 最新静态检查通过 124 项来源指纹、115 个唯一 GUID、10 个确切材质导入输出和 3 个运行时适配；轻量工程包现有 185 个文件，已在本机打包核对；Git/Drive 尚未发布。Unity 材质导入输出用 `editor-import-lock.json`，Controller/Locomotion/CameraPreview 适配用 `student-patch-lock.json` 锁定确切 SHA-256，同时核对固定原文件；starter 中对应 Unity 原文件和教师编辑器工具仍保留。

## 后续三人修复的验证状态

安装、许可、先前导入及无相机搭建检查已经完成，无需再安装编辑器。本轮在导入的 Unity Controller/Locomotion 中添加三人组保护、校准诊断、非激活槽位校验、自定义行走来源保留及 `minimumConfidence=0` 时缺失槽位存在性的修复；Python 运行时角色分配使用新 `StablePerformerTracker`。这与安装故障是不同问题，详见 `Docs/THREE_PERSON_BUGS_中文.md`。

Python 109 项适配测试及原有 11 项测试通过，Workshop 采集工具 8 项无相机测试通过。新 **Student Work → Validate Three Performer Fixes Without Camera** 在 17:34:05 实际输出 `THREE_PERFORMER_VALIDATION_OK`，本轮 Unity 编译及零阈值缺失角色等新增检查通过，完整结果见 `Docs/SETUP_VERIFICATION.md`。

此前命令行及首次 Unity 相机尝试因权限失败，没有取得帧。用户允许 macOS 的 Unity Hub 相机授权后，第二次 Unity 单人诊断实际接通：40.075 秒、429 个 pose 包；348 次状态观察中有 306 次 live preview、50 次 tracked、0 次映射就绪，手部信号不足及追踪中断，neutral 未成功。没有保存照片/视频，手势与三人未评估。日志为 `Logs/CameraDiagnostics/Unity-20261007T094436983Z/diagnostic.json`，实际试次已登记；距离、光照、丢失次数及 FPS 未测量。

重试可保存场景、停止 Play 后运行 **Student Work → Run Single Person Camera Diagnostic**。工具临时选择 1 / Seated / Camera，第 10 秒起在动作就绪时才校准，约 40 秒自动停止、恢复保存的三人场景，只保存 JSON。两肩和双手持续入镜、双手腰高保持中立；坐姿腿信号由手高替代。真人动作、三人交叉遮挡、性能、录像及所有最终提交仍待完成。

## 现在如何打开

在 Hub 的 Projects 点击 **DigiPhant_P2**，或双击工程内 `OpenUnity.command`。打开 `Assets/StudentWork/Scenes/TeamPerformance.unity`；需要重复检查搭建时，先保存场景再运行 **Student Work → Validate Setup Without Camera**。不要重复安装编辑器或重新创建空工程。

控制面板被截断时，右键 **Game** 标签 → **Maximize** 展开视图。

## 先前下载故障记录

工程目标仍是 **Unity 6000.6.3f1 / Apple Silicon**。官方 release API 已确认该版本存在，revision `45d8eee7de74`，ARM64 编辑器安装包 5,024,671,426 字节。

官方下载 URL：

`https://download.unity3d.com/download_unity/45d8eee7de74/MacEditorInstallerArm64/Unity-6000.6.3f1.pkg`

先前访问会 HTTP 302 跳转到 Unity 中国镜像，然后镜像返回 404 NoSuchKey。也检查了参考版本 6000.6.0f1，同样受此地区镜像问题影响。之后用户自行完成离线安装，当前编辑器已安装并被 Hub 定位。

## 国内渠道核查结果

用户确认同学使用 **6000.6.3f1**，希望保留兼容性并使用国内渠道。本工程保持该版本和现有包配置。

- Unity 中国官网当前版本列表和已安装 Hub 未提供 Unity 6 / 6000 系列安装包。Hub 当前可见的最高 Unity 中国版是 2022.3.62f3c1，不能直接替代此 Unity 6 / URP 17 工程。
- 已安装中国 Hub 3.3.6 的 `UnityVersion.isForbiddenReleaseVersion()` 明确将 2023、6000 系列标记为受限；安装深链接处理器遇到这些版本会显示错误并返回。因此换用 `unityhub://6000.6.3f1/45d8eee7de74` 不能解决。
- 检查了官方国内镜像及备用入口，没有找到该版本可用的官方国内安装包；未进行证书绕过或使用非官方修改版。
- 此 Hub 的 Locate 注册流程没有相同的版本限制，已实际注册该编辑器。当前账户的编辑器 entitlement 已成功解析并更新，权限修复后工程已完整导入并编译。

## 离线安装包参考

可以由同学或老师提供官方原始的 **macOS Apple Silicon / ARM64** 安装包，通过国内网盘、AirDrop、局域网或 U 盘传递。Windows 同学也可以下载并传递 Mac 安装包；Windows `.exe` 不能在这台 Mac 安装。

需要的文件：`Unity-6000.6.3f1.pkg`

根据保存的 Unity 官方 release API metadata，核验值为：

- revision：`45d8eee7de74`
- 文件大小：`5,024,671,426` 字节
- MD5：`1eb7a7b23708ef0608be3f2dd91e347d`

这些信息供另一台电脑复原使用，本机已完成安装。收到文件后先核对大小、MD5 和 Apple 安装包签名，再手动安装，用 Hub **Installs → Locate** 添加实际安装的 `Unity.app`；使用自己的账户和适用许可验证启动。当前国内 Hub 与此账户、编辑器的组合已实测成功，不代表其他账户的许可状态。

官方说明：

- [离线安装与传递安装包](https://docs.unity3d.com/6000.0/Documentation/Manual/InstallingUnity.html)
- [Hub 添加已有编辑器](https://docs.unity.com/en-us/hub/add-editor)
- [Hub 管理许可](https://docs.unity.com/en-us/hub/manage-license)
- [目标版本 release API](https://services.api.unity.com/unity/editor/release/v1/releases?version=6000.6.3f1&limit=5)

本次未修改网络/Surge 配置，未替用户接受许可条款或登录账号，未复制他人账号/许可证，未降级工程或修改 Hub 的程序。

最新单人机位复查约 40.052 秒，收到 589 个 pose 包、358/395 次实时预览观察，但没有 confident P1 或全部映射就绪，neutral 仍未通过。诊断未保存照片/视频，已停止 Play 并恢复三人场景；详见 Docs/SETUP_VERIFICATION.md（学生目录可从项目根打开）及 TEST_LOG。
