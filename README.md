# HalconTest：钢针插PCB视觉学习与生产骨架

这是一个可运行的 **C# + HALCON Runtime + WPF 离线视觉工程工作台**。它目前属于可调试的 S1 阶段，
不代表已经通过 0.05 mm 实机量产验收。正式运行代码不再通过 HDevEngine 加载 `.hdvp`，而是直接引用
`halcondotnet.dll` 并调用 `HOperatorSet`。HDevelop文件仍保留，用于学习、算法原型和逐算子调试。

当前已实现的离线闭环：

```text
WPF / PLC / 上位机
        │ TCP JSON
        ▼
Vision.Service.exe
        ├─ 离线图像采集（ICamera）
        ├─ 配方加载与校验
        ├─ 九点仿射标定与独立验证点
        ├─ PCB五孔Blob检测
        ├─ 像素(row,column)→机械(X,Y)
        ├─ 针排固定基准（相机未安装前）
        ├─ XYT补偿与0.05 mm判定
        ├─ 原图、JSON结果、JSONL日志保存
        └─ 返回OK/NG、X、Y、Theta、Score
                 │
                 ▼
           HALCON Runtime 23.11
```

## 运行前提

- Windows x64；
- .NET SDK 9.0；
- HALCON 23.11 Progress x64（Development或Runtime）；
- 有效HALCON许可证；
- `HALCONROOT`指向HALCON安装目录。本机示例：

  ```text
  D:\Program Files\MVTec\HALCON-23.11-Progress
  ```

项目引用：`%HALCONROOT%\bin\dotnet35\halcondotnet.dll`。

## 最快运行

### 1. 完整验证

双击：`scripts\04_verify_all.cmd`

验证内容包括：解决方案编译、17项 Socket/HALCON 测试、C# HalconDotNet 离线视觉闭环和 HDevelop 学习程序。

离线视觉期望结果：

```text
HoleCount = 5
IsOk       = true
X          = +0.018 mm
Y          = -0.012 mm
Theta      = +0.006 deg
Score      = 1.000
```

### 2. 启动正式C# HALCON服务

双击：`scripts\05_start_halcon_service.cmd`

服务监听：`127.0.0.1:9100`。

### 3. 启动WPF界面

保持服务窗口运行，再双击：`scripts\06_start_desktop.cmd`

界面使用 `CommunityToolkit.Mvvm`，现包含四个真实功能页：

1. 运行检测；
2. 鼠标绘制 ROI、Blob 参数预览、原图/二值图切换；
3. HALCON Shape Model 创建、`.shm` 保存和匹配；
4. 点击取像素点、机械 X/Y 录入、九点拟合、独立验证和误差表。

工程界面通过 Socket 调用服务端 HALCON 算子，不在 WPF 内伪造算法结果。完整用法见
[视觉开发工作台学习与调机 SOP](docs/08-vision-workbench-learning-sop.md)。

### 也可以一次启动两个程序

先关闭占用 9100 端口的旧服务，再双击：`scripts\07_start_workstation.cmd`。

### 4. 不开界面直接触发

运行：`scripts\03_trigger_once.cmd`

## C#工程结构

```text
src/
├─ Vision.Service/                    # 量产Socket视觉服务
│  ├─ Program.cs                      # 依赖组装和程序入口
│  ├─ Communication/
│  │  ├─ VisionTcpServer.cs
│  │  └─ Protocol.cs
│  ├─ Acquisition/
│  │  ├─ ICamera.cs
│  │  └─ HalconCamera.cs              # 当前为HALCON离线读图
│  ├─ Configuration/
│  │  ├─ ServiceOptions.cs
│  │  ├─ CameraOptions.cs
│  │  └─ VisionRecipe.cs
│  ├─ Vision/
│  │  ├─ CalibrationService.cs
│  │  ├─ PcbHoleLocator.cs
│  │  ├─ PinTipLocator.cs
│  │  ├─ CoordinateTransformer.cs
│  │  ├─ XYThetaCalculator.cs
│  │  ├─ PinInspectionService.cs
│  │  ├─ VisionBackend.cs
│  │  └─ VisionResult.cs
│  ├─ Workbench/
│  │  ├─ IVisionWorkbench.cs
│  │  ├─ VisionWorkbenchService.cs    # ROI/Blob/模板/交互标定的真实HALCON算子
│  │  └─ WorkbenchModels.cs
│  └─ Storage/
│     ├─ ImageStore.cs
│     └─ ResultLogger.cs
│
├─ Vision.Desktop/                    # WPF操作端
│  ├─ MainWindow.xaml
│  ├─ Controls/VisionImageViewer.*    # 图像像素坐标换算、ROI和结果叠加
│  ├─ ViewModels/MainViewModel.cs
│  └─ Services/VisionServiceClient.cs
│
└─ Trigger.Client/                    # 无WPF的Socket命令行客户端
```

## HDevelop文件现在的作用

```text
halcon/programs/*.hdev       算法学习、逐行执行、观察中间图像
halcon/procedures/*.hdvp     原始模块参考，用来对照C#实现
src/Vision.Service/Vision/*  正式运行的C# HALCON源码
```

正式EXE不加载`.hdev/.hdvp`，也不依赖`hdevenginedotnet.dll`。它依赖`halcondotnet.dll`、HALCON原生运行库和许可证。

## 当前硬件边界

现在没有真实相机，因此：

- `HalconCamera`用`HOperatorSet.ReadImage`读取模拟PCB图；
- `PinTipLocator`使用调机后保存的固定针排基准；
- `ICamera`和`PinTipLocator`均已隔离，后续可以替换为GigE相机采集和真实针尖定位；
- 运动控制、Z轴插入、硬触发IO尚未接入。

不得把当前模拟结果直接用于真实机构。实机必须重新完成镜头/光源验证、相机标定、针排基准标定、GR&R和安全互锁验证。

## 配方与输出

主配方：`config/vision.recipe.json`

包含离线图像路径、孔阈值/半径/圆度/数量、九点CSV、标定模型路径、针排机械基准以及XY/T/分数/标定误差公差。

运行数据写入：

```text
runtime/images/       原始检测图
runtime/results/      单次检测JSON
runtime/logs/         服务JSONL日志
runtime/calibration/  C#生成的标定模型
runtime/models/       工作台生成的HALCON形状模型
runtime/previews/     工作台生成的二值预览
```

这些运行文件默认不提交Git。

## 深入学习

- [C# HALCON生产开发流程](docs/07-csharp-halcon-production-development.md)
- [视觉开发工作台学习与调机 SOP](docs/08-vision-workbench-learning-sop.md)
- [量产就绪缺口矩阵与验收门禁](docs/09-production-readiness-gap-matrix.md)
- [需求与架构](docs/01-requirements-and-architecture.md)
- [相机、镜头、光源选型](docs/02-optics-and-hardware-selection.md)
- [标定和调机](docs/03-calibration-halcon-and-commissioning.md)
- [Socket协议](docs/04-socket-protocol.md)
- [视觉术语](docs/05-vision-glossary.md)
- [生产SOP](docs/06-production-sop.md)
