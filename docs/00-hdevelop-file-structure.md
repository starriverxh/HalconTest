# HDevelop文件结构：怎样类比C#

> 当前`dev`分支的正式生产代码已经改为C#直接调用`HalconDotNet/HOperatorSet`。本章中的HDevEngine和`.hdvp`内容保留用于理解HALCON模块化与历史方案；正式入口请看`docs/07-csharp-halcon-production-development.md`。

你的理解基本正确，但要稍微修正：

| HALCON/HDevelop | 类似的C#概念 |
|---|---|
| .hdev中的main | Program.cs里的Main方法 |
| .hdev里的局部procedure | 同一项目里的方法/函数 |
| 外部.hdvp | 单独的可复用方法文件/小型函数库 |
| procedure的iconic参数 | HObject、HImage、HRegion参数 |
| procedure的control参数 | 数字、字符串、数组、矩阵参数 |
| HDevEngine | C#加载并执行HDevelop过程的运行引擎 |

.hdvp通常不是一个C# class，更接近“一个公开方法及其实现”。HALCON主要按过程式编程组织。

## 单文件教学版

01_single_file_learning_demo.hdev 内含：

~~~text
main
├─ generate_synthetic_pcb
├─ locate_pcb_holes_local
├─ calibrate_nine_point_local
├─ transform_image_points_local
└─ calculate_xytheta_correction_local
~~~

所有局部过程保存在一个.hdev中，打开即可识别，最适合学习。

## 模块化生产版

~~~text
offline_production_demo.hdev / HDevEngine
├─ calibrate_nine_point.hdvp
├─ locate_pcb_holes.hdvp
├─ transform_image_points.hdvp
├─ calculate_xytheta_correction.hdvp
└─ inspect_pin_row.hdvp
~~~

外部过程必须满足以下任一条件：

1. 所在目录加入HDevelop外部过程搜索路径；
2. 在HDevelop项目中显式加入；
3. HDevEngine使用SetProcedurePath指定。

你截图中“未知的算子或函数 calibrate_nine_point”，就是主程序已经打开，但HDevelop不知道外部过程目录在哪里。

## HDevelop手工配置外部过程路径

不同界面语言菜单名称可能略有差异。进入编辑菜单的参数选择/首选项，找到程序或过程的外部过程路径，加入：

~~~text
C:\Users\hcg\Documents\HalconTest\halcon\procedures
~~~

重新打开模块化主程序。如果只是学习，直接使用单文件版，不需要配置。

## 以后改成相机

单文件教学程序目前调用：

~~~hdevelop
generate_synthetic_pcb (ProductImage, GroundTruthRows, GroundTruthColumns)
~~~

用本地图替换：

~~~hdevelop
read_image (ProductImage, '你的图片路径')
~~~

接相机后替换为：

~~~hdevelop
open_framegrabber (...)
grab_image_async (ProductImage, AcqHandle, -1)
~~~

后面的灰度转换、孔定位、坐标转换和补偿逻辑保持不变。不过真实图片的阈值、ROI、孔径像素范围和光源参数必须重新调试。
