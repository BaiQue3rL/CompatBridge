# CompatBridge Logo 设计与生成说明

## 当前正式版本

当前版本使用精确矢量几何重绘，不再依赖生成式图像决定最终轮廓。

- 上方为蓝色桥拱和左右桥墩；
- 下方为青色兼容环；
- 图形以垂直中心线严格左右镜像；
- 下方两端同高、同宽，并与上方内拱边界对齐；
- 品牌色为 Microsoft/Edge 生态关联色 `#0067B8` 和 `#00A4C7`；
- 不使用 Edge、IE、Windows 的商标轮廓或渐变。

矢量源文件为 `assets/compatbridge-logo.svg`。运行
`build/Build-Logo.ps1 -Promote` 可生成透明 PNG、小尺寸预览和包含
16、20、24、32、40、48、64、128、256 像素图层的 Windows ICO。

## 初始概念

初始概念由 Codex 内置 ImageGen 生成，再进行色键移除和多尺寸图标处理。

最终提示词：

> Edit the generated CompatBridge logo concept while preserving only its core
> idea of a bridge integrated with an abstract compatibility loop. Simplify
> dramatically into one clean, compact, vector-like symbol. Remove both
> floating circular dots, the amber piece, every gradient and texture. Use
> deep cobalt blue #0067B8 and cyan #00A4C7. Make the upper shape read as a
> sturdy bridge arch with two short piers, while the lower negative space
> forms a subtle continuous C-shaped compatibility loop. Use thick geometry,
> balanced symmetry, crisp edges, generous padding, and a silhouette legible
> at 16×16. No text, literal letters, browser logos, shadows, 3D, mockup or
> watermark.

初始版本的生产处理：

- 洋红色键背景移除为透明通道；
- 蓝色和青色归一为固定品牌色；
- 生成 16、20、24、32、40、48、64、128、256 像素 ICO 图层；
- 图标为原创桥梁/兼容环意象，不使用 Edge、IE 或 Windows 商标。

初始版本下方兼容环用左右错位的缺口暗示字母 C。该处理在放大后产生明显的
视觉不平衡，因此当前正式版本已改为严格镜像的 U 形兼容环。
