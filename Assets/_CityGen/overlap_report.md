# CityGen 重叠检测

场景：`Assets/StudentWork/Scenes/UpperBodyThreePersonCourse_City.unity`，种子 1930，建筑 208 栋。

规则：建筑离道路中心线 ≥ 4.6 m（半路宽 2.1 + 人行道 2 + 退让 0.5）；不压 4 个触发区、4 个地标、保留树（含玩法树 Acacia 18）、大象起点、喷泉广场（各外扩 0.5–1 m）；建筑之间不重叠；喷泉在原水池 3.0 × 2.5 范围内。

**结果：0 处重叠。**

## 检查过的保护区
- Tree Zone: x -14.0…-2.0, z -2.0…14.0
- Water Zone: x 4.6…14.6, z -27.0…-15.0
- Log Zone: x 10.0…20.0, z 8.6…16.6
- Finish Zone: x -19.5…-12.5, z 13.5…22.5
- 01 Feeding bush: x -17.1…-12.9, z -7.1…-3.1
- 02 Water basin: x 7.1…12.1, z -23.3…-18.8
- 03 Fallen log: x 12.5…17.5, z 10.6…14.6
- 04 Finish marker: x -17.1…-14.8, z 14.1…21.9
- Acacia 15: x -13.5…-6.5, z -7.5…-2.5
- Acacia 16: x -2.1…6.1, z -12.0…-4.0
- Acacia 17: x 5.1…12.9, z 2.2…9.8
- Acacia 18: x -10.7…-5.3, z 8.4…15.6
- Elephant start: x -5.0…5.0, z -5.0…5.0
- Fountain plaza: x 5.1…14.1, z -25.5…-16.5
