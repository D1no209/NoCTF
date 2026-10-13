# KoH 岩石材质调整

2026-10-13。按用户提供的 KoH 参考图优化中央山体的贴图、纹理和明暗关系。

- 新增灰褐色层状岩石贴图，包含碎裂岩片、矿物颗粒与细裂隙。
- 采用三个方向的混合投影，避免俯视 UV 在陡壁上拉长；双尺度采样减弱重复感。
- 从同一贴图计算表面凹凸，叠加沉积层纹理、局部凹陷遮蔽与少量台地粉尘。只新增一张共享 GPU 纹理，不新增地形 draw call。
- 采用中性偏暖的岩石照明，压暗谷地和陡壁，减弱等高线覆盖强度。
- 山体顶点位置、旗帜位置、控制归属、排行和计分逻辑不变。
- 异步贴图加载后只触发一次按需重绘；布局移除或场景销毁后不再重绘旧纹理。

实际页面已检查 3 / 12 据点，未发现着色器编译错误。3 据点为 9 个几何体、7 个纹理；12 据点为 27 个几何体、16 个纹理。相对旧版本仅增加一张岩石纹理。类型检查、架构审计和两项既有渲染生命周期测试通过。本轮不是新的 15 分钟性能验收。

[实际运行截图](koh-rock-texture-1920.jpg)

## 贴图来源与提示词

使用内置 `image_gen` 生成，未使用 CLI/API 后备路径。实际输出为 1254×1254 PNG，已复制进项目：[koh-stratified-rock.png](../../../backend/src/NoCTF.API/ClientApp/app/assets/images/spectator/koh-stratified-rock.png)。参考图用于指导岩石材质，不是网页背景或山体模型的替代图片。

生成提示词原文：

> Use case: photorealistic-natural. Asset type: seamless physically based rock albedo texture for a real-time 3D topographic mountain battlefield. Generate one 2048x2048 square, edge-to-edge, perfectly tileable orthographic surface scan of weathered stratified charcoal-grey and muted warm taupe sedimentary rock. Dense thin broken stone laminae, angular fractured slate plates, hairline fissures, tiny mineral grains, a little dry grey-brown dust lodged in cracks. Rich natural geological detail at several scales with subdued color variation. Flat diffuse neutral illumination, no baked directional highlights, no cast shadows, no perspective, no visible horizon, no objects. Even coverage, no large isolated boulders, no broad black cavities. This is a usable texture swatch, not a picture of a mountain or a terrain map. No contours, no blue or red lines, no flags, no UI, no labels, no text, no border, no vegetation, no snow, no water. Matte dry rock, low saturation, not glossy, not green. Tile seamlessly on all four edges.
