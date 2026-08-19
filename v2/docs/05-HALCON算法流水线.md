# HALCON 算法流水线

正式流程不是一个 `Inspect()` 黑盒。每个 Stage 都必须暴露输入、输出、指标、失败码和耗时，工程站才能帮助初学者判断“到底是哪一步坏了”。

## 先分清“流程标准”和“当前实现”

本章按常规量产视觉工位应有的完整 Stage 讲原理。当前 V2 是可执行的工程离线纵向链，不是每个量产 Stage 都已关闭：

| 内容 | 当前状态 |
|---|---|
| 文件双图、来源标记、原图 PNG/SHA-256 归档 | 已在 C# Runtime 实现并测试 |
| 图像质量、PCB/针排 Blob、XLD 亚像素圆、P1…PN、平面映射、刚体配准、逐针残差 | 已在 C# HALCON 链实现并用合成数据回归 |
| Shape Model 粗定位 | C# 分支和 HDevelop 教学已写；默认配方仍是 `BlobRow`，没有已验证 `.shm` 发布工件 |
| Rectangle2/Circle/Polygon ROI 编辑与位姿跟随 | WPF 教学内存模型和 HDevelop 原理已写；尚未保存进产品配方并供 Runtime 消费 |
| 相机内参、畸变校正、真实九点/T轴采点 | 有原理、文件格式和教学计算；必须等真实光学、相机和轴反馈 |
| 帧 ID、曝光时轴快照、硬件指纹、不可变发布包、叠加图 | 量产目标，当前 Runtime 尚未完整实现 |

因此学习时可以逐 Stage 对照代码，但只有量产就绪矩阵标为“已实现”的能力才可当作现状。

## Stage 0：采集与帧完整性

| 项目 | 内容 |
|---|---|
| 为什么 | 防止旧帧、重复帧、错误相机和轴运动中的图像进入算法 |
| 输入 | 相机角色、硬触发 ID、曝光参数、轴位置快照 |
| HALCON/C# | 文件模式 `read_image`；实机用厂商/HALCON采集接口 |
| 输出 | FrameId、时间戳、宽高、相机角色、图像句柄 |
| 失败码 | `CameraNotReady`、`AcquisitionTimeout`、`FrameSequenceInvalid` |
| 验收 | requestId、触发 ID、FrameId 和轴反馈一一对应，无旧帧 |

文件读图只是 `FileFrameSource`，不能把它命名成真实相机。

## Stage 1：灰度化与图像质量门禁

常用算子：

```text
count_channels
rgb1_to_gray
get_domain
intensity
sobel_amp
threshold（统计饱和区）
area_center
```

关键参数：

| 参数 | 单位 | 物理意义 |
|---|---|---|
| MinimumMeanGray | gray | 目标 ROI 平均灰度下限，不是越亮越好 |
| MaximumMeanGray | gray | 平均灰度上限 |
| MinimumGrayDeviation | gray | 灰度对比度下限 |
| MinimumMeanEdgeAmplitude | gray | 清晰边缘能量下限 |
| MaximumSaturatedFraction | ratio | 饱和像素面积比例上限 |

输出：Mean、Deviation、EdgeAmplitude、SaturatedFraction 和质量叠加图。

失败码：`ImageTooDark`、`ImageTooBright`、`ImageLowContrast`、`ImageBlurred`、`ImageSaturated`。

验收：用不同曝光、光强、虚焦和环境光样本做 ROC/分布分析，阈值不能只由一张好图决定。质量门禁失败后短路，不继续给出补偿。

## Stage 2：畸变校正/工作平面准备

正式相机模型可使用 `change_radial_distortion_image`、`map_image` 或基于标定数据的世界平面转换。若采用远心镜头，也不能默认零畸变，仍需实测。

输出必须注明：原始像素 `{I}`、去畸变像素 `{Iu}` 或机器坐标 `{M}`，不能混用。

验收：独立计量点覆盖中心和四角，改变已验证范围内 Z 后误差仍在预算内。

## Stage 3：Shape Model 粗定位 PCB

常用算子：

```text
create_shape_model
set_shape_model_origin
write_shape_model / read_shape_model
find_shape_model
clear_shape_model
```

Shape Model 输出 `(Row, Column, Angle, Score)`，它的职责是找到产品大致姿态并驱动 ROI 跟随，不直接作为最终孔中心精度。

关键参数：

| 参数 | 意义 | 常见错误 |
|---|---|---|
| AngleStart/Extent | 允许来料角度范围 | 范围过大导致耗时和周期歧义 |
| NumLevels | 图像金字塔层数 | 过高可能丢小特征 |
| Contrast/MinContrast | 进入模型和匹配的边缘对比 | 用训练图硬凑，换批次失败 |
| MinScore | 最低相关分数 | 把分数当毫米精度 |
| MaxOverlap | 多候选重叠限制 | 未检查第二候选歧义 |
| Greediness | 搜索速度/完整性折中 | 过高可能漏掉边界样本 |

输出：粗位姿、首选与次选分数、匹配轮廓、耗时。

失败码：`BoardNotFound`、`BoardScoreTooLow`、模板歧义（生产实现应单独编码）。

验收：训练、调参和验证图分开；覆盖允许的 XYT、曝光和批次；验证位置误差和耗时分布，而不是只看分数。

## Stage 4：位姿跟随 ROI

训练时 ROI 定义在产品参考坐标。运行时用粗定位的刚体变换把每个命名 ROI 变到当前图像：

```text
vector_angle_to_rigid
affine_trans_region
affine_trans_contour_xld
reduce_domain
```

ROI 类型至少包括 Rectangle1、Rectangle2、Circle、Polygon；每个 ROI 有名称、所属 Stage、坐标空间、包含/排除属性和版本。

失败条件：ROI 越界、面积过小、跟随变换无效、多个关键 ROI 重叠异常。

验收：用已知位姿的图检查 ROI 跟随误差；Viewer 缩放/平移只改变显示，不改变原图坐标。

## Stage 5：PCB 孔亚像素精定位

推荐思路之一：

```text
reduce_domain
edges_sub_pix
select_contours_xld / segment_contours_xld
fit_circle_contour_xld
```

也可使用 HALCON Metrology Model：`create_metrology_model`、`add_metrology_object_circle_measure`、`apply_metrology_model`、`get_metrology_object_result`。

为何不以 `area_center` 为最终结果：Blob 重心会随阈值、绿油、污渍、局部反光和孔内背景变化；亚像素圆边缘拟合能输出轮廓残差和半径，更容易建立物理验收。

每个孔输出：

```text
Id, Row, Column, RadiusPx, EdgeScore, FitResidualPx,
MachineXmm, MachineYmm, IsValid
```

关键参数：平滑 Sigma、边缘阈值、极性、圆弧最小覆盖角、半径范围、拟合鲁棒方法和最大残差。

失败码：`BoardFeatureCountMismatch`、`PointCorrespondenceFailed`，生产版本还应区分半径超差、轮廓覆盖不足和拟合残差过高。

验收：每孔对独立计量真值、全 FOV、不同曝光/污渍做偏差和重复性统计。

## Stage 6：针尖和针排精定位

背光轮廓方案常用：

```text
threshold
connection
select_shape
boundary / edges_sub_pix
segment_contours_xld
fit_line_contour_xld / fit_circle_contour_xld
```

针尖几何取决于真实形状：圆头可拟合圆/对称轴，平头可拟合两侧线与端线，尖头可拟合两侧线交点。不能在没看到真实针尖图前把一种模型写死。

输出：每根针的 ID、中心/尖点、宽度、针距、直线度、轮廓分数、局部残差和机器坐标。

失败码：`PinRowNotFound`、`PinMissing`、`PinBent`、`PinPitchOutOfTolerance`。

验收：缺针/弯针/污染关键 NG 错误放行必须为 0（在冻结验收集的定义范围内）；正常针的重复性和真值偏差满足误差预算。

## Stage 7：排序、编号和几何一致性

粗略按 Column 排序并不总是可靠：产品旋转、镜像、180°反向或多排针会改变顺序。应使用产品坐标、方向特征、基准孔/Mark 和名义拓扑分配 ID。

检查：

- PCB 和针排 ID 集完全一致；
- ID 唯一；
- 相邻距离与名义针距一致；
- 点列方向没有镜像/反向；
- 至少两个非重合点；二维稳定姿态建议三个以上有良好跨度的点。

失败码：`PointCorrespondenceFailed`。

## Stage 8：像素到机器坐标

仿射学习模式：

```text
vector_to_hom_mat2d
affine_trans_point_2d
```

正式相机模型：去畸变后把点投到已标定工作平面。每个输出点必须携带 ID、坐标系和 score。

失败码：`CalibrationMissing`、`CalibrationExpired`、`CalibrationResidualTooHigh`。

验收：独立验证点最大误差、重复性和 Z 敏感性通过；硬件指纹与标定包一致。

## Stage 9：N 点刚体配准和逐针残差

输入：`PinsInMachine[P1..PN]` 和 `HolesInMachine[P1..PN]`。

输出：刚体角度、刚体平移、T 轴命令、RMSE、最大残差和每针残差。

门禁分开检查：

- `MaximumPredictedResidualMm`：对齐后几何还能否插入；
- `MaximumSafeCommandX/Y/Theta`：设备能否安全执行这么大的补偿。

板偏 0.8 mm 但残差小，可能是可补偿的正常产品；板偏 0.02 mm 但一根弯针残差 0.08 mm，仍应拒绝。

失败码：`AlignmentResidualTooHigh`、`CorrectionOutOfRange`。

## Stage 10：运动许可、保存和返回

只有以下条件全部满足，才允许 `MotionAllowed=true`：

```text
真实PCB测量有效
AND 真实针排测量有效
AND 标定/硬件指纹有效
AND 点对应完整
AND 逐针残差通过
AND 轴命令在安全范围
AND 安全互锁和轴状态通过
AND 系统存储/服务健康
AND 当前模式已正式Commissioned
```

离线模拟即使全部几何数字“看起来正确”，也必须 `EngineeringOnly / MotionAllowed=false`。

保存证据：原图、叠加图、必要中间图、每阶段指标/耗时、所有点、XYT、失败码、状态历史、配方/模型/标定/算法哈希、相机帧 ID 和轴快照。

## HDevelop 中 F5 为什么第二次没有反应

F5 是从当前程序计数器继续运行。第一次运行到程序末尾后，计数器已经在结束位置，再按 F5 没有新语句可执行。

学习时使用：

- 运行/继续：F5；
- 单步：使用 HDevelop 工具栏或“程序执行”菜单里的 Step；
- 断点：在目标行设置断点，再 F5；
- 重新执行：先选择“重置程序执行/Reset Program Execution”，或把程序计数器重新设到第一行；
- 看中间图：在关键 `disp_obj` 后暂停，查看变量窗口中的 `HObject/HTuple`。

V2 的 `01～09` 程序故意按阶段拆开，让你每次只理解一种输入、算子和输出；`09` 才是完整离线串联。
