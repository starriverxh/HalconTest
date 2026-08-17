# C#直接调用HALCON：生产开发流程

## 1. 最终运行边界

本工程采用：

```text
HDevelop做实验和算法验证
        ↓
C#使用HalconDotNet/HOperatorSet实现正式算法
        ↓
Visual Studio/Rider编译Vision.Service.exe
        ↓
HALCON Runtime执行底层算子
```

生产运行不加载`.hdvp`，但保留`.hdev/.hdvp`用作算法参考、故障复现和算子级调试。

## 2. 模块职责

| 模块 | 输入 | 输出 | 当前状态 |
|---|---|---|---|
| `ICamera` | 触发、可选图片路径 | `HObject` | 文件图实现 |
| `CalibrationService` | 九点CSV | 仿射矩阵、RMSE、最大误差 | 已实现 |
| `PcbHoleLocator` | 灰度图、Blob配方 | 孔中心/半径/圆度 | 已实现 |
| `CoordinateTransformer` | 像素点、仿射矩阵 | 机械XY | 已实现 |
| `PinTipLocator` | 针排基准 | 针排姿态 | 固定基准实现 |
| `XYThetaCalculator` | PCB姿态、针排姿态 | XYT补偿 | 已实现 |
| `PinInspectionService` | 单次请求 | OK/NG及完整结果 | 已实现 |

## 3. 一次触发的顺序

1. Socket收到`trigger`并校验请求；
2. 全局触发锁防止同一相机并发采图；
3. `ICamera.CaptureAsync`获取图像；
4. 彩色图通过`Rgb1ToGray`转灰度；
5. `Threshold → Connection → SelectShape → AreaCenter → Circularity`定位五孔；
6. 按图像列坐标从左到右排序；
7. `AffineTransPoint2d`转换为机械坐标；
8. 由首末孔求PCB角度、全部孔求PCB中心；
9. 读取针排机械基准并计算`Board - Tool`；
10. 检查孔数、分数、XY/T公差、标定误差；
11. 保存原图、结果JSON和日志；
12. 返回Socket响应。

## 4. 坐标系约定

HALCON图像坐标：

```text
row    向下增加
column 向右增加
```

二维几何算子的输入顺序通常是：

```text
x = column
y = row
```

九点标定建立：

```text
(column, row) → (MachineX, MachineY)
```

角度必须在同一机械坐标系中比较。图像中“水平”的孔排经过带旋转的标定矩阵后，在机械坐标中不一定是`0°`。当前配方中的`toolReferencePose.thetaDeg`因此使用机械坐标角度，而不是图像角度。

## 5. 标定生命周期

九点标定不是每个产品执行一次。推荐生命周期：

```text
安装/维护模式
→ 采九点
→ 拟合矩阵
→ 用未参与拟合的检查点验证
→ 保存runtime/calibration模型

生产触发
→ 加载已验证矩阵
→ 只做定位和坐标转换
```

CSV变化时，程序通过SHA-256指纹自动重新计算标定模型。

## 6. 接真实相机时怎么改

新增一个实现`ICamera`的类，例如`GigEVisionCamera : ICamera`，内部负责：

- `OpenFramegrabber`；
- 曝光、增益、触发模式设置；
- `GrabImageAsync`；
- 超时、断线重连；
- 关闭句柄和资源释放。

`PinInspectionService`、Socket、WPF、结果保存不需要随相机品牌修改。

## 7. 接真实针排定位时怎么改

当前`PinTipLocator`返回调机固定基准。真实项目可选择：

- 另一台上视相机检测针尖；
- 同一相机通过机构切换视野；
- 高精度工装完成针排TCP标定并周期复核。

只替换`PinTipLocator`的实现，保持输出`ToolPose`不变。

## 8. 量产前仍需完成

- 相机/镜头/光源实物打样；
- 畸变模型和工作平面标定；
- 相机、PCB、针排、运动轴坐标方向核对；
- 重复性、再现性、温漂、振动测试；
- 针脚缺失、弯曲、反光、污渍和错板负样本；
- 硬触发、运动到位、Z轴安全高度和急停互锁；
- 配方权限、审计日志、版本回滚；
- HALCON Runtime许可证部署验证。
