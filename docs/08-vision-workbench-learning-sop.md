# HALCON 视觉开发工作台：学习与调机 SOP

本文对应 `Vision.Desktop` 的四个页面。它不是只演示界面：按钮通过 TCP 请求
`Vision.Service`，实际执行 `HalconDotNet.HOperatorSet`。调试阶段在工作台确定参数，
验证通过后再把参数写入配方，生产时只运行固定流程。

## 1. 先分清三个层次

```text
WPF 工程界面
  负责：画 ROI、输入参数、显示结果、收集示教点
            │ TCP JSON
            ▼
Vision.Service
  负责：调用 HALCON 算子、保存模型/标定、返回数值
            │
            ▼
HALCON Runtime
  负责：图像、区域、轮廓、模板、标定矩阵的计算
```

WPF 画出的框不是 HALCON Region。控件先把窗口坐标换算成原图像素坐标，服务端再用
`gen_rectangle1` 创建 HALCON Region。图像采用 `(row, column)`，机械采用 `(X, Y)`：

- `row`：从上到下增加，对应图像纵坐标；
- `column`：从左到右增加，对应图像横坐标；
- `X/Y`：机械坐标，方向由设备坐标系定义；
- 不能直接假定 `column=X、row=Y`，旋转、比例和轴方向由标定矩阵处理。

## 2. 启动

最简单的方法是双击：

```text
scripts\07_start_workstation.cmd
```

也可以分别双击：

```text
scripts\05_start_halcon_service.cmd
scripts\06_start_desktop.cmd
```

服务端和界面必须是两个进程。若端口 9100 已被旧服务占用，先关闭旧服务窗口，再启动。
改过服务端代码以后只重启 WPF 不够，必须重启 `Vision.Service`。

## 3. 页面 1：运行检测

这是生产流程入口。当前离线版执行：

1. `read_image` 代替相机采集；
2. 灰度化；
3. PCB 圆孔 Blob 定位；
4. 像素点通过九点标定矩阵变换到机械坐标；
5. 计算 PCB 位姿和针排基准的 `ΔX / ΔY / ΔTheta`；
6. 按 0.05 mm、角度、分数和标定误差限制判 OK/NG；
7. 保存原图、JSON 结果和日志。

调试参数未确认前，不要把本页的结果直接用于运动控制。

## 4. 页面 2：ROI 与图像处理

### 4.1 操作步骤

1. 选择 `data/offline/synthetic_pcb_good.png`；
2. 在图像上按住鼠标左键拖出矩形；
3. 设置阈值 `0~65`、半径 `15~22 px`、最小圆度 `0.85`；
4. 点击“执行 HALCON 预处理”；
5. 在“原图+结果”和“二值结果”之间切换；
6. 查看圆心、半径、圆度、候选数、筛选数和耗时。

### 4.2 实际算子链

```text
read_image
  → count_channels / rgb1_to_gray
  → gen_rectangle1
  → reduce_domain
  → threshold
  → connection
  → select_shape(area, circularity)
  → area_center / circularity
  → region_to_bin（只用于显示二值结果）
```

### 4.3 参数意义

| 参数 | 物理意义 | 调参现象 |
|---|---|---|
| ThresholdMin/Max | 保留的灰度范围 | 范围过宽会把背景/导线连进来 |
| Min/MaxRadiusPx | 由面积换算的等效半径 | 应来自样本统计，不是凭感觉设置 |
| MinCircularity | `4πA/P²` 类圆度指标 | 越接近 1 越圆，缺口和粘连会下降 |
| ROI | 限制搜索区域 | 提速并隔离干扰，但必须留定位漂移余量 |

调参建议先只调阈值，再调面积/半径，最后调圆度。每次只改变一类参数，并用 OK、边界、
NG 样本验证。不要用单张好图把参数卡得过紧。

## 5. 页面 3：Shape Model 模板匹配

Blob 适合按灰度和几何特征筛选；形状模板适合定位具有稳定轮廓、允许旋转和一定遮挡的目标。

### 5.1 创建模板

1. 在具有唯一轮廓的区域画 ROI；
2. 设角度起点/范围，例如 `-10° / 20°`；
3. 设置 `Contrast`，它控制哪些边缘进入模型；
4. 点击创建，服务端执行 `create_shape_model`；
5. 模型保存到 `runtime/models/*.shm`。

ROI 不宜只框一个高度重复的圆孔，否则可能得到多个同样合理的位置。示例图推荐框住整排五孔和
两条橙色线，构成更唯一的轮廓组合。

### 5.2 匹配

点击匹配后执行：

```text
read_shape_model
  → find_shape_model
  → 返回 row / column / angle / score
  → clear_shape_model
```

`score` 越接近 1 越相似。生产阈值必须用样本分布确定：统计正常品最低分和异常品最高分，并保留
安全间隔。模型句柄使用后必须 `clear_shape_model`，只释放 C# `HTuple` 不能代替清理模型资源。

当前工作台演示的是模型创建和定位。正式 PCB 项目通常会用匹配位姿建立刚性变换，把检测 ROI
跟随产品位置移动，再在局部 ROI 内做精定位/缺陷检测。

## 6. 页面 4：九点标定示教

### 6.1 原理

工作台使用二维仿射模型：

```text
[X]   [a b c] [column]
[Y] = [d e f] [ row  ]
                [ 1   ]
```

`vector_to_hom_mat2d` 根据像素点和机械点拟合六个参数；`affine_trans_point_2d` 把点变换到机械坐标。
仿射模型能表达平移、旋转、X/Y 比例和少量剪切。它不能补偿严重镜头畸变；高精度项目应先做
镜头/相机标定和畸变校正，再做图像到机械平面的标定。

### 6.2 实机示教步骤

1. 固定相机、镜头焦距/光圈、工作距离和光源，禁止标定后再移动；
2. 平台走到第 1 个位置，采图；
3. 在标定页点击标记附近，再点“HALCON 精定位最近圆心”；鼠标只给搜索范围，最终
   `row/column` 来自 `threshold + area_center`，不能用人工点击精度做 0.05 mm 标定；
4. 从运动控制器读取同一标记对应的机械 `X/Y`，输入并加入；
5. 覆盖视野四角、四边中部和中心，共 9 点；
6. 另外采集 3~5 个未参与拟合的位置，取消“用于拟合”，作为独立验证点；
7. 点击拟合并保存；
8. 查看每点预测 X/Y、误差、RMSE、最大拟合误差、最大验证误差；
9. 重新随机走点验证，不得只看参与拟合的 9 点。

“载入示例 CSV”用于学习离线数据。真实设备不能沿用示例矩阵，必须按本机重新示教。

### 6.3 0.05 mm 项目的判定建议

总误差预算不能把 0.05 mm 全分给视觉。建议先分解为相机成像、特征提取、标定、机构重复精度、
运动跟随和针排弹性等部分。视觉标定通常应明显优于最终公差，例如：

- 拟合 RMSE 目标不大于 0.01 mm；
- 独立验证最大误差目标不大于 0.02 mm；
- 重复拍摄同一位置做 30 次统计；
- 覆盖温升、振动、不同批次 PCB、亮度波动和 Z 高度变化；
- 用 GR&R/能力分析确认，而不是只看一次测量。

## 7. 调试参数怎样进入生产

工作台当前会保存：

```text
runtime/previews/       预处理二值图
runtime/models/         HALCON .shm 模型
runtime/calibration/    标定 JSON 和示教 CSV
```

点击“保存为带版本生产配方”后，Blob ROI/参数和 `BlobRow/ShapeModel` 定位模式会原子写入
`config/vision.recipe.json`，旧版本保存在 `runtime/recipe-history/`。为避免生产周期中途切换算法，
当前实现要求重启 `Vision.Service` 才激活新版本。量产批准前仍应继续增加审批、权限和受控回滚；
操作员只能切换已批准配方，工程师才允许修改。

## 8. 从离线图切换到真实相机

工作台和算法服务不需要重写。将 `ICamera` 的文件实现替换为 GigE/USB3 实现：

```text
当前：HOperatorSet.ReadImage
实机：OpenFramegrabber → GrabImageStart → GrabImageAsync
```

然后增加曝光、增益、触发模式、丢帧重连和超时状态。连接相机后必须重新做 ROI、模板、九点标定、
误差验证和完整生产验收，离线合成图参数只能作为软件功能验证。
