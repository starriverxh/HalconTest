# HDevelop 01～09 学习顺序

这些 `.hdev` 是 **HALCON 算法课和原型**，不是最终 EXE 要加载的库。正式运行代码在 `v2/src/Vision.Halcon`，以 C# `HOperatorSet` 调用同类算子。

按编号学习：

1. `01_generate_synthetic_pair.hdev`：生成 PCB 与针排两张确定性合成图。
2. `02_image_quality_gate.hdev`：平均灰度、对比度、边缘幅值、饱和比例。
3. `03_board_coarse_and_follow_roi.hdev`：形状模板粗定位、跟随产品位姿的 ROI。
4. `04_hole_subpixel_circle.hdev`：局部 XLD 边缘与鲁棒圆拟合。
5. `05_pin_tip_inspection.hdev`：逐根针检测、针距和直线度。
6. `06_nine_point_mapping.hdev`：拟合点与独立验证点；理解九点平面映射。
7. `07_t_axis_rotation_center.hdev`：多角度圆拟合求 T 轴真实旋转中心及回差。
8. `08_coordinate_chain_alignment.hdev`：N 点刚体配准、逐点残差和绕中心的 XYT 指令。
9. `09_complete_offline_cycle.hdev`：把 PCB、针排、映射和 XYT 串成完整离线周期。

## 第一次怎么操作

1. 用 HDevelop 打开 `01_generate_synthetic_pair.hdev`。
2. 在程序窗口最左侧第一条可执行语句处放置程序计数器。
3. 按 `F5` 连续运行；学习时使用“单步越过/单步进入”，每执行一行就看变量窗口中的图像、区域、轮廓和数值。
4. 程序到末尾后如果再次按 `F5` 没反应，执行菜单中的 **“重置程序执行”**，让程序计数器回到开头，再按 `F5`。不同 HDevelop 键位方案可能不同，因此以菜单文字为准。
5. 双击变量窗口里的 `BoardImage`、`PinImage`、Region 或 XLD，可把对象显示到图形窗口；数值变量用于核对中间计算。
6. 每个文件末尾都有一个 `...Passed` 变量。它为 `1/true` 只表示这一课的离线自检通过。

## 断点和单步应该看什么

- 图像质量课：看均值、灰度标准差、边缘幅值和饱和比例，而不是只看图“清不清楚”。
- 粗定位课：看模板返回的 Row/Column/Angle，以及 ROI 是否随位姿移动。
- 精定位课：看 XLD 轮廓、拟合圆心、半径和残差；模板分数不等于测量精度。
- 针排课：看 P1～PN 编号是否稳定、缺针是否导致数量失败、弯针是否导致直线度失败。
- 标定课：拟合点只用于求变换，验证点必须独立；重点看每个验证点残差和最大残差。
- XYT 课：看旋转中心不是原点时，为什么角度变化会同时产生 X/Y 平移分量。

这些程序使用合成仿射映射作为教学夹具，不是相机内参标定，也不能证明实机达到 `0.05 mm`。
