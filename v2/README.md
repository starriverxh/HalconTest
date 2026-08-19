# HalconPinInsertion V2

这是一套重新建立的 HALCON + C# 工业视觉学习项目，目标工艺是：使用 XYZT 机构把一排回弹钢针对准并插入 PCB 的对应孔位。V2 不再从旧演示工程继续堆功能，而是按需求、光学、数据、算法、标定、运动、运行服务、工程站、追溯和验收的常规流程组织。

## 文件在哪里

```text
C:\Users\hcg\Documents\HalconTest\v2
```

主解决方案：

```text
C:\Users\hcg\Documents\HalconTest\v2\HalconPinInsertion.V2.sln
```

最重要的说明：[docs/00-从这里开始.md](docs/00-从这里开始.md)。

## 最快使用方法

不用 PowerShell，直接双击 CMD：

1. `scripts\01_verify_v2.cmd`：构建全部项目，运行所有软件/HALCON/WPF测试和 HDevelop 01～09。
2. `scripts\02_start_runtime.cmd`：启动 C# + HALCON Runtime 服务，窗口保持打开。
3. `scripts\03_trigger_offline_once.cmd`：模拟上位机通过 Socket 触发一次。
4. `scripts\04_start_engineering_ui.cmd`：启动模块化 WPF 工程站。
5. `scripts\05_run_dataset_regression.cmd`：Runtime 运行时，回归正常/缺针/弯针三组数据。
6. `scripts\06_open_v2_solution.cmd`：打开 V2 solution。
7. `scripts\07_open_hdevelop_lessons.cmd`：从第一个 HDevelop 教学程序开始。

当前服务端口：

```text
9200 Production  当前未调试完成，inspect被拒绝
9201 Engineering 本机离线读图和学习
```

## 先看事实边界

“已真实实现”只表示当前软件中有可执行源码和测试证据，不表示真实设备已经达到精度或节拍。三类能力必须分开理解：

| 范围 | 已真实实现（当前软件） | 教学/离线演示 | 待硬件与量产验证 |
|---|---|---|---|
| C# 运行链 | Socket、单消费者、File 读图、HALCON 算子、点集配准、结果/原图保存、离线运动门禁 | 使用合成 PCB/针排图片和合成仿射矩阵 | 真实相机触发、轴快照、PLC 握手、异常恢复 |
| HALCON 算法 | Blob 候选、图像质量、`edges_sub_pix`、圆拟合、逐点坐标与残差 | HDevelop 01～09；当前三例合成数据 | 真实针尖几何、反光/污染/翘曲、多批次鲁棒性 |
| ROI/模板/标定 | Shape Model 读取/匹配分支和仿射映射源码存在 | WPF ROI 画布、模板按钮、九点/T 中心数据均为教学模型；没有可发布 `.shm` | 真实图 Viewer、ROI 写配方、模板验证、内参/畸变和实机标定 |
| 配方与发布 | SHA-256 校验库及其单元测试存在 | 当前 Runtime 直接读取 `config/recipes/*.json` | 不可变发布包、审批签名、活动索引、兼容检查和回滚接入 Runtime |
| 精度结论 | XYT 数学公式和安全门禁有软件测试 | 合成图残差只验证软件链 | 端到端 `0.05 mm`、GR&R、Cmk/Cpk、插入力与长稳 |

详细逐项状态见 [量产就绪矩阵](docs/12-量产就绪矩阵.md)。

## 已实际跑通的软件纵向结果

不是固定返回值。下面结果由 Socket → Runtime → HALCON 两张合成图 → 合成平面映射 → N 点配准 → JSON/原图保存这条软件链实际计算得到；“实际跑通”不代表使用了真实产品或真实相机：

| 样本 | 实际结果 | 关键证据 |
|---|---|---|
| nominal-offline-pair | EngineeringOnly | 5孔、5针、逐点残差、MotionAllowed=false |
| missing-centre-pin | Rejected / PinMissing | 实际检测 4/5 |
| bent-last-pin | Rejected / PinBent | 最大直线度误差约 0.0480 mm |

当前三例合成数据回归：`3/3 passed`。正常合成图的最大刚体残差约 `4.64×10⁻⁸ mm`，它只说明理想合成坐标和数学链一致，绝不能当作真实设备精度、算法准确度或完整数据集验收。

## 工程结构

```text
src/
├─ Vision.Contracts/          Socket和工程DTO
├─ Vision.Domain/             坐标、配方、结果和失败语义
├─ Vision.Application/        检测周期、状态机、刚体拟合和运动门禁
├─ Vision.Halcon/             双文件相机、质量、粗定位、ROI、亚像素孔/针
├─ Vision.Infrastructure/     哈希校验库、结果和事件持久化（发布包尚未接入Runtime）
├─ Vision.Runtime/            单消费者队列、幂等、生产/工程端口
├─ Vision.Engineering.Wpf/    10个Feature的模块化工程站
├─ Vision.Trigger.Client/     最小Socket上位机示例
└─ Vision.Dataset.Cli/        数据集哈希和纵向回归

tests/
├─ Vision.Domain.Tests/
├─ Vision.Application.Tests/
├─ Vision.Architecture.Tests/
├─ Vision.Runtime.Tests/
├─ Vision.Infrastructure.Tests/
└─ Vision.Engineering.Wpf.Tests/
```

HALCON 学习：

```text
hdevelop/
├─ 01_generate_synthetic_pair.hdev
├─ 02_image_quality_gate.hdev
├─ 03_board_coarse_and_follow_roi.hdev
├─ 04_hole_subpixel_circle.hdev
├─ 05_pin_tip_inspection.hdev
├─ 06_nine_point_mapping.hdev
├─ 07_t_axis_rotation_center.hdev
├─ 08_coordinate_chain_alignment.hdev
└─ 09_complete_offline_cycle.hdev
```

正式 C# 算法使用 `HOperatorSet + halcondotnet.dll`。`.hdev` 用于学习和原型，不由最终 EXE 直接加载。

## WPF 工程站内容

工程站已按 Feature 分开，不再是一个大 `MainViewModel`：

- 生产工作台；
- 手动相机/调试；
- 项目开发向导；
- 图像与 ROI 工作台；
- 模板管理；
- 标定中心；
- 配方中心；
- 数据集回归；
- 质量追溯；
- 系统维护。

ROI 教学模型可以编辑 Rectangle1、Rectangle2、Circle、Polygon、名称、Image/ProductPose 坐标和“是否跟随”属性，但画布绘制的是合成 PCB，不读取 Runtime 图像、不保存配方，也没有把这些 ROI 传给 `Vision.Halcon`。每个页面都有“为什么、输入、输出、HALCON 算子、参数单位/物理意义、中间结果、失败码、验收标准”。只有生产页的离线检测按钮连接本机 Runtime 工程端口；模板、标定、配方、回归和追溯数据仍由教学 Gateway 提供。

## 关键技术纠正

V2 已移除这些旧演示概念：

- 不再用固定针排姿态生成量产 OK；
- 不再用 `boardPose - pinPose` 计算补偿；
- 不再把 Shape Model 分数当最终定位精度；
- 不再把“最大安全补偿量”和“补偿后最大残差”混为 0.05 mm；
- 不再把产品 NG、测量无效和系统故障压成一个 `IsOk`；
- 不再允许离线模拟结果授权运动。

目标量产流程应当是：

```text
ShapeModel粗定位
→ 位姿跟随ROI
→ edges_sub_pix + fit_circle_contour_xld精定位
→ P1...PN点对应
→ 二维刚体最小二乘
→ 每针残差
→ T轴真实中心换算
→ 安全与标定门禁
```

当前默认离线配方实际使用 `BlobRow`，没有加载可发布 `.shm`；C# 中虽然已有 Shape Model 读取/匹配分支，但尚未完成模板创建、独立数据验证、ROI 工件发布以及真实产品接入。因此不能把上面的目标流程理解成每一项都已在 Runtime 完成。

## 当前安全边界

```text
CommissioningState = Uncommissioned
Disposition        = EngineeringOnly（正常离线样本）
MotionAllowed      = false
```

当前只有 File 相机源和合成平面标定。真实量产前还必须完成：真实 FOV 和光学选型、PCB 下视相机、针排上视相机、内参/畸变、两个工作平面、T 轴中心、工具偏置、曝光时轴反馈、PLC 安全互锁、真实锁定数据集、GR&R、长期稳定性以及端到端 0.05 mm 验收。

## 文档导航

- [常规视觉项目开发流程](docs/02-常规视觉项目开发流程.md)
- [光学选型与误差预算](docs/03-光学选型与误差预算.md)
- [坐标系、标定与XYT](docs/04-坐标系标定与XYT原理.md)
- [HALCON算法流水线](docs/05-HALCON算法流水线.md)
- [ROI、模板、标定与配方](docs/06-ROI模板标定与配方学习流程.md)
- [Socket协议与运行状态](docs/07-Socket协议与运行状态.md)
- [离线学习与运行SOP](docs/08-离线学习与运行SOP.md)
- [测试体系与验收](docs/09-测试体系与验收标准.md)
- [设备调试与量产导入SOP](docs/10-设备调试与量产导入SOP.md)
- [术语速查](docs/11-视觉术语速查.md)
- [量产就绪矩阵](docs/12-量产就绪矩阵.md)

## 框架版本说明

当前机器只有 .NET SDK 9.0.101，因此 V2 使用 `net9.0`/`net9.0-windows` 保证可以本机编译。正式长期维护版本应在 HALCON 23.11 .NET 绑定兼容验证后冻结到合适 LTS；不要把当前目标框架当成已完成的量产生命周期选择。
