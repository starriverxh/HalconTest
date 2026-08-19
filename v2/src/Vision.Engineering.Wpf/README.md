# Vision Engineering WPF V2

这是独立的工程工作站 UI。它不引用 `HalconDotNet`，所有未来真实算法和设备能力都必须通过
`IEngineeringWorkbenchGateway` 对接 Application/Runtime 边界。生产工作台现在使用
`RuntimeEngineeringWorkbenchGateway` 连接 `127.0.0.1:9201`，会真实执行离线 HALCON 周期；其余尚未接入
Runtime 工程用例的模板/标定/回归页面使用明确标注的教学数据。

## 启动

先双击 `v2/scripts/02_start_runtime.cmd`，再双击 `v2/scripts/04_start_engineering_ui.cmd`。

当前显示 `ENGINEERING OFFLINE / MotionAllowed=false`。离线图片得到的 XYT 只能用于学习界面和数据流，
Runtime 从结果构造层禁止运动，也没有运动命令接口。

## 页面与代码边界

每个页面位于独立的 `Features/<FeatureName>` 目录，包含自己的 View 与 ViewModel：

- `Production`：生产结果、周期状态和离线触发；
- `Manual`：相机、光源和图像质量；
- `ProjectWizard`：常规开发 Gate；
- `ImageRoi`：算法节点和四种 ROI；
- `Templates`：Shape Model 训练/验证；
- `Calibration`：内参、九点、T轴和独立验证；
- `Recipes`：草稿、验证、审批、激活生命周期；
- `Regression`：黄金数据集和发布 Gate；
- `Traceability`：单件证据链和离线复盘；
- `Maintenance`：设备健康、报警和审计。

`ShellViewModel` 只负责组合与导航。各 Feature 不共享一个巨型 `MainViewModel`。

## ROI 演示

在“图像与ROI工作台”选择工具后：

- `Rectangle1` / `Rectangle2`：按住左键拖动；
- `Circle`：从圆心向外拖动；
- `Polygon`：左键逐点添加，右键完成；
- ROI 可命名，可选择 `Image` 或 `ProductPose` 坐标空间，并记录是否跟随粗定位位姿；
- 右侧属性面板可数值编辑，当前阶段只写工程草稿。

## 自测

双击 `v2/scripts/01_verify_v2.cmd`；它会构建 WPF 并运行该测试程序集。

测试工程不依赖外部测试框架，适合当前离线 NuGet 环境；它验证 Shell 可构造、Feature 模块化、
学习证据完整、ROI 类型与 MVVM 命令、离线运动保护、标定/回归演示，以及 UI 程序集不引用 HALCON。
