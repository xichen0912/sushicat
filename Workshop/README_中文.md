# Part 1：真实采集与 Teachable Machine 操作包

**当前状态：仅建立目录与模板。尚未采集相机样本、训练模型或测量识别效果。**

打开 https://teachablemachine.withgoogle.com/ 。物品使用 **Image Project → Standard image model**；人体姿势使用 **Pose Project**。不要把姿势照片放入 Image Project 后称为姿势模型。

| 项目 | 暂定类别（可改名） | 采集要求 |
|---|---|---|
| 物品 | bottle 水瓶、book 书、shoe 鞋、spoon 勺子 | 每类至少 50 张训练图；另拍建议 10 张测试图 |
| 姿势 | neutral 直立双臂放下、t_pose 双臂水平、y_pose 双臂举成 Y、squat 下蹲、side_lunge 向右侧弓步 | 每类至少 50 个训练样本；另录建议 10 个测试样本 |

姿势要让头、手、膝和脚都在画面内。可按照自己的身体条件替换姿势；保持五类轮廓清楚。改类别时，同步修改 `dataset_metadata.json` 中的 id、描述和目录。

## 本机逐帧采集工具

已提供 `collect_samples.py`，工具尚未替你打开相机或采集样本。它只在你显式执行后打开指定相机；**空格保存当前一帧，Q 或 Esc 退出**，没有自动生成 50 张的功能。先退出 Unity Play 和其他相机进程，避免设备被两个程序同时占用。以下命令在 `Workshop/` 目录运行，使用已经安装 OpenCV 的项目隔离环境：

```sh
../DigiPhantStarter/Tracking/.venv/bin/python collect_samples.py --dataset objects --class bottle --split train --round initial --camera 0
```

`--dataset`、`--class`、`--split`、`--round` 和 `--camera` 都必须明确填写。物品类别为 `bottle`、`book`、`shoe`、`spoon`；姿势类别为 `neutral`、`t_pose`、`y_pose`、`squat`、`side_lunge`，以元数据中的实际类别为准。修改类别时先改元数据，再运行采集。更换类别或 train/test 必须退出后用新命令重新启动；预览顶部一直显示本轮类别与划分。

独立测试另开一轮，保持测试图片不进入训练区：

```sh
../DigiPhantStarter/Tracking/.venv/bin/python collect_samples.py --dataset objects --class bottle --split test --round initial --camera 0
```

根据首轮识别错误补拍训练时用 `--split train --round retrained`。每次空格前变化角度、距离、光照或身体姿态；连续按键造成的几乎相同图片不能代表这些变化。程序只拒绝**完全相同**的原始帧或保存内容，并跨同一项目的类别、train/test 检查；它不能识别近似重复，也不能验证画面确实符合你选择的类别。

每张无覆盖文字的原相机帧以 PNG 存在 `data/<dataset>/<split>/<class>/`，同步登记 `samples.jsonl` 中的真实时间、session、内容哈希与 `sample_kind=camera_image`。时间是本机读取该帧的 UTC 时间，不是相机硬件提供的曝光时间。每次运行有 `sessions/<id>.json`，元数据更新实际相机索引和采集轮次；保存第一帧后把空模板状态改为“已记录相机图片，训练待完成”。地点及训练状态仍需按事实填写。工具不训练、上传、发布模型或填写预测结果。

若要保存姿势原图，可运行：

```sh
../DigiPhantStarter/Tracking/.venv/bin/python collect_samples.py --dataset poses --class t_pose --split train --round initial --camera 0
```

**姿势原图不是 TM Pose 关键点，也不是训练完成的姿态模型。** 必须另行在 Teachable Machine 创建 **Pose Project**，按它支持的实际采集/导入流程完成五类训练，保存 TM 原项目及其真实关键点数据、模型和测试记录。不要把这批图片放入 Image Project 后称为完成了 Pose Project；本机采集工具也不能证明 TM 项目已满足 50 个训练样本。

仅验证采集工具的参数、清单与跨划分去重逻辑，不会导入 OpenCV 或打开相机：

```sh
../DigiPhantStarter/Tracking/.venv/bin/python -m unittest test_collect_samples.py
```

1. **先采集训练集。** 分几轮变化角度、距离、位置、光线和背景；姿势还需变化站位与小幅关节角度。各类尽量采用相同背景与拍摄条件，避免模型靠房间或手的摆法猜类别。每类至少 50 个样本，不能把同一张图复制成 50 个。
2. **留出独立测试集。** 另开一轮拍摄，不把测试样本上传训练区。把原始训练/测试文件分别保存在 `data/<项目>/train/<类别>/` 与 `test/<类别>/`。如 TM 直接录制，保存并下载包含训练数据的项目；只有模型导出不等于保存了训练数据。保留原项目在 `projects/`。姿势项目若导出的是关键点数据，保留原格式和每个样本的索引，不用截图冒充原始姿势数据。
3. **首次训练与测试。** 记录实际参数、时间和模型版本。在 Preview 中用未见样本测试；逐条填写 `evaluation.csv`，记录真实标签、预测、概率和条件。保存错误案例。
4. **补充训练样本并重训。** 根据真实错误补拍，例如勺子反光或侧弓步被识别成下蹲。补拍样本放进训练集，`round_added` 填 `retrained`；原测试集保持不参与训练。保存两轮导出，再对相同测试集复测，比较每类正确数和错误类型。不要提前写“准确率提升”。
5. **整理证据。** 在 `samples.jsonl` 逐行登记真实样本；填写元数据中的实际采集信息、两轮训练状态与模型链接。导出的 `model.json`、`metadata.json`、权重文件分别放进 `models/<项目>/initial/` 和 `retrained/`。每次训练用的 TM 项目也应保留。
6. **检查与提交。** 执行下方命令。将模型、训练数据、数据说明、评估记录和个人日志上传自己的 Google Drive；提交物品/姿势两条分享模型链接与对应 Drive 链接。使用 `SUBMISSION_LINKS.md` 记录真实地址。

```sh
python3 validate_workshop.py
```

当前空模板应返回退出码 2，并明确列出缺少真实数据、训练与评估。程序只检查文件与记录一致性，不能替你证明拍摄、实际训练或识别效果。

`samples.jsonl` 每行是一个 JSON 对象。下面仅说明字段，不应把这条示例直接当作真实记录：

```json
{"sample_id":"填写唯一编号","dataset":"objects","class_id":"bottle","split":"train","relative_path":"data/objects/train/bottle/真实文件名.jpg","source":"webcam","sample_kind":"camera_image","session_id":"填写真实拍摄轮次","captured_at":"实际时间，含时区","round_added":"initial"}
```

`dataset` 用 `objects` 或 `poses`；`split` 用 `train` 或 `test`；`round_added` 用 `initial` 或 `retrained`。一个文件对应一个实际样本；`pose_keypoints` 格式的单样本 JSON 也可登记。整个项目 ZIP 应放在 `projects/`，不能登记成一个或数十个已确认样本。

本目录与旧的 `assignment_deliverables/` 分开。旧资料来自 COCO、类别为三类，不能替代本次四类物品与五类相机姿势采集要求。
