# 机器视觉术语速查

| 术语 | 本项目中的含义 |
|---|---|
| FOV / Field of View | 物方一次成像覆盖范围，不是传感器尺寸 |
| WD / Working Distance | 镜头规定基准面到工件的距离，必须按镜头图纸定义 |
| DOF / Depth of Field | 在可接受清晰度内的物方高度范围，不等于测量精度 |
| Pixel size | 传感器单个像元物理尺寸，例如 `3.45 um` |
| Object resolution | 物方每像素毫米数，`FOV/pixel count` |
| Spatial resolution | 系统分辨相邻细节的能力，受像元、镜头 MTF、光源和算法共同影响 |
| MTF | 镜头传递不同空间频率对比度的能力 |
| Global shutter | 全部像素同时曝光，适合运动目标 |
| Rolling shutter | 分行曝光，运动时可能产生几何形变 |
| Telecentric lens | 远心镜头，降低物距变化造成的倍率变化 |
| Distortion | 镜头使理想几何位置发生系统偏移，需标定和验证 |
| Magnification | 传感器尺寸与物方视野尺寸的比例 |
| Bright field | 明场照明，镜面反射进入镜头时目标较亮 |
| Dark field | 暗场照明，利用边缘/缺陷散射光形成对比 |
| Coaxial light | 同轴光，经分光结构沿观察轴照明平面反光目标 |
| Backlight | 背光，目标形成轮廓剪影，适合孔和外形 |
| Dome light | 穹顶漫射光，减弱曲面金属热点 |
| Strobe | 频闪，用短时高亮脉冲冻结运动 |
| ROI | 感兴趣区域，限制检测范围、速度和误检 |
| Threshold | 灰度阈值分割，得到区域 |
| Morphology | 膨胀、腐蚀、开闭等区域形状处理 |
| Connected component | 连通域，将候选区域分开 |
| XLD contour | HALCON 亚像素轮廓数据结构 |
| Subpixel | 用灰度/轮廓模型估计像素之间的位置；不代表绝对精度自动达到亚像素大小 |
| Shape-based matching | 基于边缘形状的模板匹配，可输出位置、角度和得分 |
| HomMat2D | HALCON 二维齐次变换表示，可组合平移、旋转、尺度等 |
| Intrinsic calibration | 相机内参和畸变模型标定 |
| Extrinsic calibration | 相机坐标与世界/机器坐标间位姿标定 |
| Hand-eye calibration | 相机、机器人/机构和工具间坐标关系标定 |
| Eye-to-hand | 相机固定在机架外部观察运动机构/工件 |
| Eye-in-hand | 相机安装在运动轴或机器人末端 |
| TCP | 工具中心点；本项目应定义在实际针尖组合基准，而非安装座 |
| Fiducial | 用于定位的基准标记，可能与实际孔存在制造套准误差 |
| Repeatability | 同条件重复测量/定位的一致程度 |
| Accuracy | 测量值接近真值的程度；重复性好不等于准确 |
| Reproducibility | 换人、换装夹、换班次等条件变化后的再现程度 |
| RMSE | 均方根误差，反映总体残差，但不能替代最大误差 |
| Residual | 模型预测与计量真值之间的差 |
| Cpk | 过程能力指数，前提是过程稳定且量测系统合格 |
| GR&R | 量具重复性与再现性分析 |
| Parallax | 视差，高度变化通过透视成像转化为平面位置/尺度误差 |
| Runout | 旋转轴偏摆/跳动；T 轴转动时会造成位置变化 |
| Backlash | 反向间隙，运动方向改变时的失动量 |
| Compliance | 顺应性，结构受力时允许微量位移以降低过约束 |
| Golden sample | 经计量确认的黄金样件，用于开机复核而非替代标定 |

