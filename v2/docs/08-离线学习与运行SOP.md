# 离线学习与运行 SOP

目标：在没有真实相机和运动轴时，完成“样图 → HALCON 阶段 → C# 编排 → Socket → 结果追溯”的安全闭环。

## 1. 环境确认

当前工作站已按下列环境建立：

- Windows x64；
- .NET SDK 9.0.101；
- HALCON 23.11 Progress x64；
- `HALCONROOT=D:\Program Files\MVTec\HALCON-23.11-Progress`；
- `HALCONARCH=x64-win64`。

注意：`net9.0` 是为了当前机器可构建，不是长期量产 LTS 决策。正式冻结前必须验证 HALCON 23.11 .NET 绑定与目标 LTS（优先评估 .NET 10 LTS，不兼容则评估 .NET 8 LTS），并将版本写入发布报告。

V2 在本离线工作站关闭了 NuGet 在线漏洞查询，避免无网络时 `NU1900` 被全局“警告即错误”阻断。正式联网 CI/发布流程必须重新启用包漏洞审计，并保存依赖清单/SBOM。

## 2. 第一次验证

双击：

```text
v2\scripts\01_verify_v2.cmd
```

成功标准：

- V2 solution 全部构建成功；
- Domain/Application/Architecture/Runtime/HALCON 离线测试通过；
- 数据集 manifest 和 SHA-256 通过；
- 输出明确写出“离线工程模式，不允许运动”。

若失败，先看窗口最后一个 `[FAIL]` 项，不要盲目反复 F5。

## 3. HDevelop 学习

从 HDevelop 打开 `v2/hdevelop`，严格按下面的**实际文件名**执行。编号 06～08 分别是九点映射、
T 轴中心、双相机坐标链，不要把三者当成同一种标定：

```text
01_generate_synthetic_pair.hdev         生成PCB与针排合成图
02_image_quality_gate.hdev              图像质量门禁
03_board_coarse_and_follow_roi.hdev     Shape Model粗定位与跟随ROI
04_hole_subpixel_circle.hdev            PCB孔亚像素圆拟合
05_pin_tip_inspection.hdev              针尖检测、节距与直线度
06_nine_point_mapping.hdev              九点平面映射与独立验证点
07_t_axis_rotation_center.hdev          T轴旋转中心与正反向回差
08_coordinate_chain_alignment.hdev      双相机坐标链、N点配准与XYT
09_complete_offline_cycle.hdev          完整离线周期
```

每个程序执行前先读文件头：输入、输出、算子、参数和验收标准。

第一次 F5 到末尾后，再运行要先重置程序执行位置。建议在每个关键算子后使用单步，观察变量窗口中的 Region、XLD、Rows、Columns、Scores 和残差。

## 4. 启动 C# 运行服务

双击：

```text
v2\scripts\02_start_runtime.cmd
```

预期控制台：

```text
Mode: OfflineCommissioning
Production: 127.0.0.1:9200 (inspect disabled)
Engineering: 127.0.0.1:9201
Motion authority: DISABLED
```

保持该窗口运行。

## 5. Socket 触发一次

双击：

```text
v2\scripts\03_trigger_offline_once.cmd
```

检查响应：

- RequestId 与请求一致；
- Board/Pins 都有测量；
- 每个点有 P1...PN；
- Alignment 有 RMSE 和最大残差；
- `Disposition=EngineeringOnly`；
- `MotionAllowed=false`；
- 结果 JSON 写入 `v2/runtime/results`。
- 两张归档 PNG 写入 `v2/runtime/images/yyyyMMdd`，结果内保存帧来源和 SHA-256。

脚本随后发送 ACK，使工位回到 `Uncommissioned` 等待下一次工程触发。

## 6. 启动 WPF 工程站

双击：

```text
v2\scripts\04_start_engineering_ui.cmd
```

推荐学习顺序：

1. 项目开发向导：逐项了解需求、光学、样本、算法、标定、回归和发布；
2. 手动采图/相机：点击“采集单帧（合成）”，观察 FrameId 和四项图像质量指标；运动按钮只演示安全拒绝；
3. 图像与 ROI 工作台：在固定教学画布创建命名 ROI；**算法流水线不另设独立页面**，就在本页左栏；
4. 选中左栏 Stage 后运行“逐节点运行离线预览”，观察算子链、输出、状态和失败码；
5. 模板管理：依次点击“生成教学轮廓（内存）”和“运行教学验证数据”，理解 ROI、原点、分数分布和版本；
6. 标定中心：分别点击“九点教学拟合（内存）”和“T轴中心教学拟合”，区分平面映射与旋转中心；
7. 配方中心：理解 `Draft → Validated → Approved → Active → Retired`；当前按钮只改变 WPF 内存教学对象；
8. 数据集回归：页面按钮是 UI 教学统计；真正经过 Runtime 的三例回归使用 CMD 05；
9. 质量追溯：按 RequestId/CycleId 查看证据链，并理解离线重放不得覆盖原判定；
10. 系统维护：查看 Runtime、相机、PLC、光源阻断项以及报警恢复说明。

工程站会在各页醒目标明边界：当前只有生产页真正连接 Runtime 9201；ROI、模板、标定、配方和
页面内回归是教学内存模型，不会写入 JSON、`.shm` 或标定文件。真实离线纵向回归请使用 CMD 05。

生产页验证方法：保持 CMD 02 的 Runtime 窗口运行，进入“生产工作台”，点击“运行一次离线算法周期”。
界面应显示 `Runtime 127.0.0.1:9201 已连接并完成ACK`，来源应包含
`engineering.inspect_offline / HALCON离线双图 / Runtime写盘`，并始终显示 `MotionAllowed=false`。
如果没有先运行 CMD 02，页面应明确显示 Runtime 不可用、未执行 HALCON、未写结果，不能把失败当成模拟成功。

## 7. 运行真实离线数据集回归

保持 CMD 02 的 Runtime 窗口运行，双击：

```text
v2\scripts\05_run_dataset_regression.cmd
```

当前锁定合成数据集应输出：

```text
PASS nominal-offline-pair
PASS missing-centre-pin
PASS bent-last-pin
Dataset regression: 3/3 passed
```

这条 CMD 才会通过 Socket 调用 Runtime 9201。WPF“数据集回归”页当前用于学习指标和发布 Gate，
不会替代 CMD 05 的纵向证据。

## 8. 修改参数的正确方式

1. 复制 Active/示例配方形成 Draft；
2. 只修改一个有物理意义的参数；
3. 单张图逐 Stage 观察；
4. 跑完整 validation 数据集；
5. 查看误杀、漏检、位置偏差、残差和耗时；
6. 记录修改原因；
7. 不通过则回退，不继续堆补丁掩盖失败；
8. 通过后才进入审批/发布流程。

## 9. 常见问题

### PowerShell 脚本无法运行

V2 的日常入口均提供 `.cmd`，不依赖用户调整 PowerShell ExecutionPolicy。CMD 内部直接调用 `dotnet`。

### HDevelop 按第二次 F5 没反应

程序已经运行到末尾。使用“重置程序执行”后回到第一行，或在目标行设置程序计数器/断点。

### Runtime 提示 STATION_UNCOMMISSIONED

这是正确的安全行为。当前没有真实双相机、标定、轴反馈和安全握手，生产端口不会执行离线图。

### 数字小于 0.05，为什么仍不能运动

数字来自合成/离线图，只证明软件数学链。真实光学、标定、机械和安全链尚无证据，因此 MotionAllowed 必须为 false。

### HALCON 许可证错误

确认 HALCON 23.11 x64 安装、许可有效、`HALCONROOT/HALCONARCH` 正确，并确保启动进程能找到 `halcon.dll` 和 `halcondotnet.dll`。

## 10. 离线阶段完成定义

只有全部满足才算离线阶段完成：

- 所有自动测试通过；
- 每个 Stage 有输入/输出/算子/失败码/验收说明；
- 当前正常、缺针、弯针三组锁定合成样本得到预期结果；模糊、曝光、污染、姿态和批次扰动仍是扩充数据集的必做项；
- 重复 requestId 不重复执行；
- 结果和版本可追溯；
- EngineeringOnly 永远不授权运动；
- 已列出所有等待真实硬件关闭的量产门禁。
