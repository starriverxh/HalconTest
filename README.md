# HALCON + C# PCB 弹性钢针插入视觉教学/生产骨架

## 2026-08-13：最简单的学习入口

直接用HDevelop打开 halcon\programs\01_single_file_learning_demo.hdev，然后按F5。这个文件包含main和所有局部过程，不需要配置外部过程路径，并会生成 data\offline\synthetic_pcb_good.png。

如果PowerShell脚本不能运行，进入scripts文件夹双击01_run_halcon_single_file.cmd。其他双击入口包括02_start_mock_service.cmd、03_trigger_once.cmd和04_verify_all.cmd。

原来的offline_production_demo.hdev是模块化示例，依赖halcon\procedures下的外部hdvp；直接打开前必须配置外部过程路径。详细原理见docs\00-hdevelop-file-structure.md。

这是一个从零开始学习机器视觉的完整工程骨架，根目录是：

~~~text
C:\Users\hcg\Documents\HalconTest
~~~

当前版本已经在本机 HALCON 23.11 Progress 和 .NET 9 上验证：

- HALCON 离线演示：九点/多点仿射标定、独立点残差、5孔定位、XYT补偿；
- C# Socket 服务：连通、请求校验、忙碌、超时、粘包/多报文、结果落盘、关闭；
- C# 上位机客户端：只使用 TCP Socket，不引用 HALCON；
- C# → HDevEngine → HALCON 外部过程 → JSON结果 → 文件追溯的完整调用链。

没有真实 PCB/针排图片、FOV、孔径和相机型号之前，工程只能完成“离线算法和生产软件骨架”。真实相机采图参数、ROI、阈值、模板以及最终 0.05 mm 能力必须拿样件在设备上调试和验收。

## 第一次怎么用

打开 PowerShell：

~~~powershell
cd C:\Users\hcg\Documents\HalconTest
~~~

### 1. 运行 HALCON 离线学习程序

~~~powershell
.\scripts\run-halcon-offline.ps1
~~~

成功时会看到 DemoPassed=1、HoleCount=5、CorrectionX=0.018 mm、CorrectionY=-0.012 mm。

然后用 HDevelop 打开 `halcon\programs\offline_production_demo.hdev`，从第一行开始单步运行，重点观察 HomMat2D、PointErrors、ValidationErrors、HoleRows、HoleColumns 和 CorrectionX/Y/Theta。

### 2. 运行不需要相机的 Socket 模拟

第一个 PowerShell：

~~~powershell
.\scripts\run-service.ps1 -Backend mock
~~~

第二个 PowerShell：

~~~powershell
.\scripts\run-client.ps1 -Command ping
.\scripts\run-client.ps1 -Command status
.\scripts\run-client.ps1 -Command trigger
.\scripts\run-client.ps1 -Command shutdown
~~~

结果保存在 `runtime\results`，运行日志保存在 `runtime\logs`。

### 3. 让 C# 真正调用 HALCON

第一个 PowerShell：

~~~powershell
.\scripts\run-service.ps1 -Backend hdevengine
~~~

第二个 PowerShell：

~~~powershell
.\scripts\run-client.ps1 -Command trigger
.\scripts\run-client.ps1 -Command shutdown
~~~

调用链是：

~~~text
Trigger.Client
  -> TCP
  -> Vision.Service
  -> HDevEngine
  -> halcon/procedures/inspect_pin_row.hdvp
  -> JSON结果和追溯文件
~~~

`inspect_pin_row.hdvp` 当前是已验证的 HDevEngine 接口冒烟过程。接真实相机后，把它内部替换为“采图 → PCB定位 → 标定变换 → 针尖坐标 → 补偿 → 判定”，C# Socket协议无需改变。

### 4. 一键验证

~~~powershell
.\scripts\verify.ps1
~~~

它会编译三个 C# 项目、执行 7 项 Socket 自测，再运行 HALCON 离线闭环。

## 学习顺序

1. 阅读 `docs/01-requirements-and-architecture.md`，先理解设备和坐标系。
2. 在 HDevelop 单步执行 `offline_production_demo.hdev`。
3. 逐个打开 `halcon/procedures` 下的外部过程，理解输入/输出接口。
4. 阅读 `docs/03-calibration-halcon-and-commissioning.md`，用自己的点替换示例标定点。
5. 运行 mock Socket，理解上位机和视觉服务为什么分开。
6. 运行 hdevengine Socket，理解 C# 如何调用 HALCON。
7. 根据真实样件完成光源 DOE，再接相机。
8. 按 `docs/06-production-sop.md` 完成标定、调试和验收。

## 文件地图

~~~text
HalconTest/
├─ README.md                         第一次从这里开始
├─ HalconPinInspection.sln           Visual Studio解决方案
├─ halcon/
│  ├─ programs/offline_production_demo.hdev
│  └─ procedures/                    标定、定位、补偿过程
├─ src/Vision.Service/               视觉Socket服务
├─ src/Trigger.Client/               纯C#上位机触发示例
├─ tests/Vision.Service.Tests/       无第三方包的端到端自测
├─ config/                           配方与标定点示例
├─ protocol/                         Socket报文样例
├─ runtime/                          日志和结果
├─ scripts/                          运行与验证入口
└─ docs/                             方案、选型、标定、协议、术语、SOP
~~~

## 真实相机接入

当前机器只检测到 HALCON 的文件采集接口。采购相机后先安装 MVTec 对应接口（常见为 GigEVision2、GenICamTL 或厂商接口），再在 HDevelop 中完成：

~~~hdevelop
info_framegrabber ('GigEVision2', 'device', Information, ValueList)
open_framegrabber (...)
set_framegrabber_param (...)
grab_image_async (...)
~~~

先将真实原图保存进 `data/offline`，把图像算法离线稳定后，再把采集过程接进 `inspect_pin_row`。

## 当前示例的边界

- 0.05 mm 是最终系统目标，不等于 HALCON 定位重复性；
- 九点仿射标定不代替镜头内参/畸变标定；
- 普通二维相机不能保证 Z，需要测高/定高与力控；
- 一排回弹针需要上视针尖检查、导向/顺应和力-位移监控；
- 硬件最终型号要由 FOV、针排长度、针/孔尺寸和样件成像 DOE 决定。
