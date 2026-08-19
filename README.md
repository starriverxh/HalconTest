# HalconTest：HALCON + C# 钢针插 PCB 视觉学习项目

当前开发版本是独立重建的 **V2**。旧版代码仅作为 Legacy 对照，不再继续扩展，也不要按旧版脚本学习。

## 从这里进入

- 项目目录：`C:\Users\hcg\Documents\HalconTest\v2`
- Visual Studio 解决方案：`C:\Users\hcg\Documents\HalconTest\v2\HalconPinInsertion.V2.sln`
- 零基础入口：[V2 从这里开始](v2/docs/00-从这里开始.md)
- 项目总说明：[V2 README](v2/README.md)
- 第一次运行：双击 `v2\scripts\01_verify_v2.cmd`

## 这套工程采用的正式路线

```text
HDevelop：学习算子、逐步观察中间图、验证算法原型
                    ↓ 算法冻结后迁移
C# Vision.Halcon：HOperatorSet + halcondotnet.dll
                    ↓
Vision.Application：检测周期、坐标配准、状态机和安全门禁
                    ↓
Vision.Runtime.exe：Socket 服务、HALCON Runtime、结果追溯
                    ↑
WPF 工程站：生产监看，以及 ROI/模板/标定/配方的学习工作台
```

最终 EXE 由 C#/.NET 生成。当前正式运行链不使用 HDevEngine 动态加载 `.hdev/.hdvp`；`v2/hdevelop` 中的文件用于学习、单步调试和对照 C# 实现。

## 当前项目等级

```text
Software level      : V2 Engineering Offline
Commissioning state : Uncommissioned
MotionAllowed       : false
```

软件分层、HALCON 离线算法链、Socket、归档、安全门禁和三组合成数据回归已经实现。真实相机、镜头、光源、PLC、运动控制和端到端 `0.05 mm` 能力必须结合样件与硬件完成调试和计量验收；离线小数值不能作为实机精度证明。

具体哪些是可运行实现、哪些是教学演示、哪些必须等待实机，请查看 [量产就绪矩阵](v2/docs/12-量产就绪矩阵.md)。
