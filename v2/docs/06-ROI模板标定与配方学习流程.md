# ROI、模板、标定与配方学习流程

## ROI 不是屏幕上的一个红框

正式 ROI 是版本化算法工件，至少包含：

```text
RoiId / Name
OwnerStage
ShapeType
CoordinateSpace（Image 或 Product）
Geometry
IncludeOrExclude
FollowBoardPose
IsLocked
Version
```

显示画布的缩放、留黑边和 DPI 不得改变 ROI 的原图坐标。保存前检查越界、尺寸过小、重复名称、关键 ROI 缺失和跟随空间错误。

## 推荐的 ROI 示教步骤

1. 导入一组代表性 OK 图，而不是只选最漂亮的一张；
2. 选择产品参考图和基准姿态；
3. 为 Shape Model 建立 `Board.CoarseModel` ROI；
4. 为每个孔建立 `Board.Hole.P1...PN` 跟随 ROI；
5. 为针排建立 `Pins.Row` 和每根针的局部跟随 ROI；
6. 设置包含区/排除区，避免文字、焊盘或治具进入模型；
7. 运行位姿扰动图，确认 ROI 跟随而不是固定在屏幕；
8. 保存到 Draft 配方，执行静态校验和数据集回归；
9. 通过后生成新版本，不直接修改 Active 配方。

## Shape Model 完整生命周期

```text
选择训练样本
→ 创建模型ROI和排除区
→ 设置角度/尺度/极性/Contrast
→ 显示真正进入模型的XLD轮廓
→ 设置模型原点（绑定产品CAD基准）
→ 在独立OK/边界/NG图批量匹配
→ 分析首选/次选分数、定位偏差和耗时
→ 冻结阈值
→ 保存.shm、元数据、训练图哈希和HALCON版本
→ Validated → Approved → Published → Active
```

模型原点很重要：`find_shape_model` 的 Row/Column 代表模型原点。如果原点只是训练 ROI 的默认中心，而 C# 把它当 PCB CAD 基准，后续全部坐标都会出现稳定偏差。

## 九点标定向导应该怎样工作

1. 固定并记录相机、镜头、焦距、光圈、工作距离、分辨率和光源；
2. 控制器移动到规划的 9 个拟合点；
3. 每点由 HALCON 自动精定位标定特征；
4. 同步读取机械 X/Y 和曝光时轴反馈，不手抄；
5. 每点重复采集，检查定位离散；
6. 检查重复点、共线、FOV 覆盖和异常点；
7. 拟合后显示每个残差向量，而不是只显示一个 RMSE；
8. 运动到未参与拟合的独立验证点；
9. 随机顺序重复验证，避免回差被固定路径掩盖；
10. 保存原图、点表、矩阵、报告、硬件指纹和审批记录。

## 配方不是一个随时覆盖的 JSON

产品发布包示例：

```text
published-recipes/PCB-A/2.1.0/
├─ manifest.json
├─ recipe.json
├─ models/
│  └─ board.shm
├─ reference/
│  └─ golden.png
└─ validation/
   └─ report.json
```

工位标定包示例：

```text
station-calibrations/STATION-01/CAL-20260819-001/
├─ manifest.json
├─ board-camera.json
├─ board-plane-transform.json
├─ pin-camera.json
├─ pin-plane-transform.json
├─ t-axis-pivot.json
├─ tool-transform.json
└─ validation-report.json
```

生命周期：

```text
Draft → Validate → Approved → Published（不可修改）→ Active → Retired
```

激活前必须：

1. 验证 JSON schema 和参数范围；
2. 校验每个文件 SHA-256；
3. 校验相机/镜头/分辨率/Z 平面与标定兼容；
4. 加载 HALCON 模型；
5. 用黄金图 dry-run；
6. 确认数据集回归报告通过且签名版本一致；
7. 原子切换活动快照；
8. 候选失败时继续使用旧快照。

## 数据集回归如何替你判断算法好坏

初学者不应该靠“这一张图看起来对”判断。数据集页至少展示：

- 样本总数及类别分布；
- OK→NG 误杀数、NG→OK 漏检数；
- 每个失败码的混淆情况；
- X/Y/角度和每点残差相对真值的 P50/P95/Max；
- P50/P95/P99 耗时和超时数；
- 当前 Active 与候选 Draft 的 A/B 差异；
- 失败样本的原图、中间 Stage、参数和叠加图。

冻结验收集必须带 manifest 和 SHA-256。更换任何图片或标签都会形成新数据集版本，不能静默覆盖。

## WPF 工程站的四个工作域

```text
生产工作台：只运行和看结果，不改参数
视觉工程：采图、ROI、模板、算法节点、标定、配方、回归
质量追溯：记录复盘、统计、报警和审计
系统维护：设备、存储、许可证、用户、备份和恢复
```

V2 UI 在没有硬件时始终显示“工程离线 / 禁止运动”。学习说明面板针对每个 Stage 展示：为什么需要、输入输出、HALCON 算子、参数单位和物理含义、中间结果、失败码及验收标准。
