# CityGen 第 0 步：场景调查报告

调查日期：2026-10-11。本步骤**只读**，没有修改任何场景、脚本或资源；新增的只有这份报告（以及 Unity 需要的 `.meta`）。

数据来源：直接解析场景 YAML、课程生成器源码 `Assets/SavannahCourse/Editor/SavannahCourseGenerator.cs`、网格资源的包围盒。坐标单位 = Unity 单位（米）。尺寸为**世界坐标轴对齐包围盒（AABB）**，X 为东西、Z 为南北、Y 为高度。

---

## 1. 主场景与环境

| 项目 | 结果 |
|---|---|
| Unity 版本 | **6000.6.3f1**（revision 45d8eee7de74） |
| 渲染管线 | **URP 17.6.0**（GraphicsSettings 已指定 URP 资产，可用 Volume 后处理） |
| 主场景（建议） | **`Assets/StudentWork/Scenes/UpperBodyThreePersonCourse.unity`**（Build Settings 第 0 个，三人正式课程） |
| 同一张地图的其它场景 | `P1/P2/P3UpperBodyPractice.unity`：116 个课程物体坐标完全相同；`P3SoloPractice.unity`、`TeamPerformance.unity`：相同，只少 4 个触发区 |
| 你最近在用的场景 | `P2UpperBodyPractice.unity`（与主场景地图一致） |
| 参考图 | **`Assets/_Reference/` 不存在**，`ref_street_1.png / ref_street_2.png` 不在项目里（你在对话里贴的两张图我能看到） |

层级（根物体）：

```
UpperBodyThreePersonCourse
├─ DigiPhant Controls          控制器（脚本）
├─ Stage                       60×60 平面（已禁用，旧舞台）
├─ Main Camera                 (6.32, 4.80, 8.02)
├─ Key Light                   方向光，yaw -35°
├─ Elephant Travel             玩家（大象）根
├─ Stage distance grid         4 米网格线（已禁用）
├─ Savannah Course [layout 4]  课程地图：地面 / 道路 / 路标 / 18 棵树 / 4 个地标
├─ Student Work Runtime        数据记录器（脚本）
└─ Upper Body Course Interactions  课程管理器 + 4 个触发区 + 4 个地面文字
```

---

## 2. 道路（奶白色路）

| 物体 | 世界坐标 | 尺寸 / 说明 | 材质 |
|---|---|---|---|
| `Savannah Course [layout 4]/Open path` | (0, 0.035, 0) | **一整块网格**，AABB 42.2 × 39.2（X −21.1…21.1，Z −19.1…20.1），**路宽 4.2**（中心线两侧各 2.1），转角为斜接 | Pale sand path.mat |
| `Start line` | (0, 0.055, 0) | 4.2 × 0.2 的起跑线 | Ivory markers.mat |
| `Route markers/Direction 1…10` | 见下 | 每个由 2 片 V 形 Chevron 组成，约 0.53 × 0.53，贴地 0.055 | Ivory markers.mat |

**路线中心线**（来自生成器 `Route[]`，世界坐标 X,Z）——从中心出发、绕外圈一周、在左上角结束，形成 G 字形，**终点不闭合**：

```
(0,0) → (0,6) → (−14,6) → (−19,1) → (−19,−12) → (−14,−17)
      → (14,−17) → (19,−12) → (19,13) → (14,18) → (−16,18)
```

方向箭头位置：D1 (0, 1.5)、D2 (−1.5, 6)、D3 (−15.06, 4.94)、D4 (−19, −0.5)、D5 (−17.94, −13.06)、D6 (−12.5, −17)、D7 (15.06, −15.94)、D8 (19, −10.5)、D9 (17.94, 14.06)、D10 (12.5, 18)。

> 换材质对功能没有影响：任何脚本都不读道路网格，大象移动也不依赖道路碰撞。

---

## 3. 水池（青蓝色圆盘）

| 物体 | 世界坐标 | 尺寸 | 材质 |
|---|---|---|---|
| `Landmarks (4, decorative)/02 Water basin`（组） | (9.557, 0.01, −21.0) | — | — |
| 　└ `Stone …`（八边形石盆） | (9.557, 0.24, −21.0) | 3.0 × 0.46 × 2.5 | Basin stone.mat |
| 　└ `Still …`（水面） | (9.557, 0.48, −21.0) | 2.55 × 0.03 × 2.05 | Still blue water.mat |

- 水池本身**没有脚本、没有 Collider**。
- 但 **`ThreePersonCourseManager.waterBasin` 引用了 `02 Water basin` 这个 Transform**：用它的位置判断"离水池 2.8 米内才能喝水"，自动吸附（`WaterDrinkAssist`）也读它。
- 配套触发区：`Water Zone`，BoxCollider（Trigger），中心 (9.557, 1.51, −21)，范围 X 5.56…13.56、Z −26…−16。
- 位置说明：水池在**南侧道路（z = −17）的外侧**，不在地图中心。你俯视截图里的"顶部"对应世界坐标的南边（截图视角相对世界旋转了约 180°）。

---

## 4. 木头（棕色长条）

| 物体 | 世界坐标 | 尺寸 | 材质 |
|---|---|---|---|
| `Landmarks (4, decorative)/03 Fallen log`（组） | (15.0, 0.01, 12.557)，yaw −25° | — | — |
| 　└ `Timber`（八边形圆木） | (15.0, 0.41, 12.557) | AABB 3.06 × 0.8 × 1.99（圆木约长 3.0、直径 0.8，斜放 −25°） | Bark.mat |
| 　└ `Cut end` × 2 | (16.37, 0.41, 13.20)、(13.63, 0.41, 11.92) | 0.29 × 0.64 × 0.59 | Pale sand path.mat |

- **`LogInteraction.logTransform` 和课程管理器都引用 `03 Fallen log`**。
- 用鼻子捡木头时，程序会把 **`03 Fallen log` 整个移到大象鼻尖下面**（运行时改父物体）。所以木头的新外观必须作为 `03 Fallen log` 的子物体，才会跟着被捡起来。
- 配套触发区：`Log Zone`，BoxCollider（Trigger）+ `LogInteraction.cs`，中心 (15, 1.51, 12.557)，X 11…19、Z 9.56…15.56。
- 位置：东侧道路（x = 19）的内侧。你截图里的"左侧"对应世界坐标的东边。

---

## 5. 树木（18 棵金合欢 + 1 丛灌木）

每棵树 = `Trunk` + 2 根 `Branch` + 2 片 `Flat canopy`，材质 Bark / Olive foliage / Sage foliage，**都没有 Collider**。

| 树 | 位置 (X, Z) | 树冠占地 X × Z | 最高点 |
|---|---|---|---|
| Acacia 01 | (−26, −24) | 6.7 × 5.4 | 4.7 |
| Acacia 02 | (−26, −10) | 6.2 × 4.8 | 4.5 |
| Acacia 03 | (−26, 6) | 5.1 × 6.4 | 4.3 |
| Acacia 04 | (−26, 24) | 5.0 × 6.6 | 4.3 |
| Acacia 05 | (−14, −26) | 5.6 × 4.0 | 4.4 |
| Acacia 06 | (0, −26) | 6.0 × 7.3 | 5.0 |
| Acacia 07 | (14, −26) | 6.4 × 6.1 | 4.3 |
| Acacia 08 | (26, −24) | 6.0 × 5.0 | 4.2 |
| Acacia 09 | (26, −8) | 5.3 × 6.3 | 4.9 |
| Acacia 10 | (26, 8) | 6.8 × 6.7 | 4.8 |
| Acacia 11 | (26, 24) | 5.7 × 6.0 | 4.9 |
| Acacia 12 | (12, 26) | 6.9 × 6.5 | 4.0 |
| Acacia 13 | (−4, 26) | 6.7 × 4.5 | 4.9 |
| Acacia 14 | (−20, 26) | 7.0 × 6.4 | 4.7 |
| Acacia 15 | (−10, −5) | 6.1 × 4.1 | 4.0 |
| Acacia 16 | (2, −8) | 7.3 × 7.0 | 4.8 |
| Acacia 17 | (9, 6) | 6.7 × 6.6 | 4.3 |
| **Acacia 18** | **(−8, 12)** | 4.5 × 6.1 | 4.5 |

- ⚠️ **Acacia 18 是玩法物体**：`TreeInteraction.treeTrunk` 引用它的 `Trunk`，"01 绕树"关卡的距离判定就用这棵树。**必须保留、不能移动。**
- `01 Feeding bush`（装饰灌木）：(−15, 0.01, −5.11)，占地约 2.2 × 2.0，高 1.8，无脚本引用。
- `04 Finish marker`（终点拱门）：(−16, 0.01, 18)，两根柱子 + 横幅，宽 5.8、高 3.9，无脚本引用（终点判定用的是 `Finish Zone`）。

---

## 6. 玩法物体（一律不删、不移动、不改组件）

| 物体 | 世界坐标 | 组件 | 作用 |
|---|---|---|---|
| `Elephant Travel` | (0, 0.01, 0)，朝 +Z | Transform（运动根） | **玩家**：白色大象就挂在下面（预制体 `Elephant — collective puppet`，身长约 5.07） |
| `DigiPhant Controls` | (0, 0, 0) | DigiPhantController、DigiPhantLocomotion、WaterCupInteraction、ControlDiagnosticsLogger | 摄像头输入、移动、喝水判定 |
| `Upper Body Course Interactions` | (0, 0.01, 0) | ThreePersonCourseManager | 课程流程：树 → 水 → 木头 → 终点 |
| `Tree Zone` | (−8, 1.535, 6)，yaw −90° | BoxCollider **Trigger** + TreeInteraction | 绕树判定区，X −13…−3、Z −1…13 |
| `Water Zone` | (9.557, 1.51, −21)，yaw 90° | BoxCollider **Trigger** | 喝水区，X 5.56…13.56、Z −26…−16 |
| `Log Zone` | (15, 1.51, 12.557) | BoxCollider **Trigger** + LogInteraction | 木头区，X 11…19、Z 9.56…15.56 |
| `Finish Zone` | (−16, 1.535, 18)，yaw −90° | BoxCollider **Trigger** | 终点区，X −18.5…−13.5、Z 14.5…21.5 |
| 4 个地面文字 | 01 TREE (−8, 0.29, 0.8)、02 WATER (9.56, 0.26, −15.8)、03 LOG (9.8, 0.26, 12.56)、04 FINISH (−16, 0.29, 12.8) | TextMesh（橙色） | 关卡提示 |
| `Student Work Runtime` | (0, 0, 0) | SimulationTelemetryLogger（引用课程根 `routeAnchor`） | 轨迹记录 |
| `Main Camera` | (6.32, 4.80, 8.02) | Camera、AudioListener | 被 `DigiPhantLocomotion.followCamera` 引用；运行时还会挂上 `HighFollowCamera` |
| `Key Light` | (0, 0, 0)，yaw −35° | Directional Light | 主光 |
| 运行时自动挂载 | — | `HighFollowCamera`、`WaterDrinkAssist` | Play 时自动添加，不在场景文件里 |

**脚本对场景物体的引用**（都必须保持名称和层级关系）：

```
TreeInteraction        → Tree Zone, Acacia 18/Trunk, Elephant Travel
LogInteraction         → Log Zone, 03 Fallen log
ThreePersonCourseManager → DigiPhant Controls, Tree Zone, Log Zone, Water Zone, 02 Water basin, Finish Zone
DigiPhantLocomotion    → Elephant Travel, 大象 Animator、elephant_Main_ctrl, Main Camera
SimulationTelemetryLogger → Elephant Travel, Savannah Course [layout 4]
```

> ❓ **"红橙绿的虚线"在场景文件里没有找到对应的物体。** 场景里没有 LineRenderer、Gizmo 脚本，运行时也不生成虚线。最可能是下面几种之一，请你确认：
> 1. Scene 视图里 4 个 **Trigger 区**的线框（Unity 会画碰撞框边线）；
> 2. 路上的 10 组 **方向箭头**（象牙白色 V 形，连起来像虚线）；
> 3. 摄像头预览里的 **P1/P2/P3 区域框**（绿 / 橙 / 蓝，只在预览画面里，不在 3D 场景中）。
>
> 能发一张带虚线的截图最好。

---

## 7. 地面

| 物体 | 世界坐标 | 尺寸 | 材质 |
|---|---|---|---|
| `Savannah Course [layout 4]/Ground` | (0, 0, 0) | **60 × 60**（X、Z 各 −30…30），厚 0.06 | Ochre ground.mat（黄色） |
| `Stage`（已禁用） | (0, 0, 0) | 60 × 60 平面 + MeshCollider | Stage.mat |
| `Stage distance grid`（已禁用） | — | 22 条 4 米网格线 | Stage Lines.mat |

地面外面没有任何东西，相机拍到地图边缘以外是空的。

---

## 8. 和改造计划有关的事实与冲突（需要你确认）

1. **大象不受碰撞影响**：移动直接改 Transform，`stageRadius = 0`（不限范围），课程物体也都没有 Collider。所以新建筑**挡不住大象**，玩家可以开进楼里。建筑只能做视觉上的避让；要做"墙"的话，得另外加一个只影响移动的边界，这会改到移动逻辑，需要你同意。
2. **可放建筑的空间很小**（地图只有 60 × 60）：
   - 外圈：道路外缘到地面边缘，东西两侧约 **8.9 米**（x 21.1→30），南侧约 **10.9 米**（z −19.1→−30），北侧约 **9.9 米**（z 20.1→30）。
   - 南侧外圈有水池和 Water Zone（x 5.6…13.6），北侧外圈有终点区。
   - 内圈：道路内缘以内约 x −16.9…16.9、z −14.9…15.9，要扣掉中间那段路（x −2.1…2.1、z 0…8.1 和 z 3.9…8.1 的西向段）、Tree Zone、Log Zone、Acacia 18。
   - 15～30 层的天际线高楼建议放在 **±30 地面边界以外**（再铺一圈城市地面），不占用可玩区域。
3. **喷泉不在中心**：水池在南侧路外 (9.56, −21)，硬性规则要求原地替换。所以"中心广场让喷泉成为视觉焦点"做不到。可选：
   - (a) 在喷泉周围做一个小广场；
   - (b) 中心起点附近另做一个广场（雕像或钟楼），喷泉留在南侧。
4. **喷泉的"迁移"**：水池没有 Collider 和脚本，只是被管理器引用了 Transform。最稳妥的做法是：**保留 `02 Water basin` 这个物体（只关掉它两个子物体的 MeshRenderer），新喷泉放在 CityGen 下、同一位置**。这样所有引用都不用改。
5. **木头例外**：因为捡木头会把 `03 Fallen log` 挂到鼻子上，新木头外观**必须作为它的子物体**，不能放在 CityGen 下面，否则捡起来时只会带走原来的圆木。另外木头会被举起来，做成"木箱堆"会很重很怪，**建议保持圆木 / 木头捆形状**，只升级模型细节和木纹。
6. **树的"删除"**：建议不删除，而是**关掉 MeshRenderer**，层级不变，随时可以恢复。**Acacia 18 必须保留可见**（它是绕树关卡要绕的那棵树）。
7. **场景副本**：只复制主场景 → `UpperBodyThreePersonCourse_City.unity`。三个单人练习场景不会自动变成城市；如果需要，可以之后用同一个生成器在它们的副本上再跑一次。副本不会加进 Build Settings，除非你要求。
8. **截图**：Unity 正开着，我不能直接操作 Unity 界面。截图会通过一个编辑器菜单（例如 **CityGen → Capture Screenshots**）由你点一下生成：俯视 + 1.7 米人视角，1920×1080，保存到 `Assets/_CityGen/screenshots/`。
9. **参考图**：要不要把你贴的两张图存进 `Assets/_Reference/`（ref_street_1.png / ref_street_2.png）？它们只作为本地风格参考，不放进任何场景。
10. **原创性**：所有招牌、海报、旗帜都用原创图案和自造文字，不使用参考图里的城市名、标志或角色。

---

## 9. 等你确认的问题（回复编号即可）

- **Q1** 主场景用 `UpperBodyThreePersonCourse.unity` 吗？
- **Q2** "红橙绿虚线"是第 6 节里的哪一种？
- **Q3** 喷泉广场选 (a) 还是 (b)？
- **Q4** 同意第 4、5、6 条的做法吗（保留原物体，只关渲染；木头外观挂在原物体下面；树只关渲染）？
- **Q5** 要不要给建筑加一个"挡住大象"的边界（会改移动逻辑），还是只做视觉避让？
- **Q6** 要不要把两张参考图存进 `Assets/_Reference/`？
