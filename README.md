# TileMap3D

TileMap3D 是独立的 Unity 原生 Tilemap 3D 平面扩展。它复用 Unity Tile Palette、Tile、RuleTile、AnimatedTile 和 TilemapRenderer，让同一套可重复 Sprite 贴图可以直接用于水平地面、竖直侧面和倾斜斜面。

TileMap3D 默认不把大地图烘焙为单张颜色贴图。Overlay 默认使用 `SurfaceMaterial`：Tilemap 继续保存 Tile、规则和动画，运行时只把每格 Sprite 索引、颜色与变换上传到 GPU，再在目标 Mesh 的相同几何上执行覆盖 Pass。目标 3D 物体继续负责自己的 Mesh、原材质和 Collider。

## 核心约定

- `TileMap3DSurface` 根对象的本地 XZ 是绘制平面。
- 根对象的本地 Y 是表面法线。
- 旋转 Surface 根对象即可覆盖世界 XZ、XY、YZ 或任意斜面。
- 一个 Surface 只表示一个平面；同一模型上的非共面区域分别创建多个 Surface。
- 目标对象可以是 Unity Primitive、ProBuilder、导入 Mesh 或任意普通 GameObject。
- 当前版本不支持球面、曲面、蒙皮变形表面或自动跨越拐角。

## 创建方式

### 生成 3D 地面

菜单：

- `TileMap3D > 创建 3D Tilemap 地面`
- `GameObject > TileMap3D > 创建 3D Tilemap 地面`

创建结构：

```text
TileMap3D Ground
├─ TileMap3DSurface
├─ MeshFilter / MeshRenderer / BoxCollider
└─ Tilemap Source (Grid)
   └─ Base (Tilemap + TilemapRenderer + TileMap3DLayer)
```

该模式保留原来的固定列数、行数、单格尺寸和厚度，并生成封闭地面 Mesh。新对象默认使用 `NativeTilemap`，烘焙仍是可选操作。

### 覆盖已有 3D 平面

选择目标物体后使用：

- `TileMap3D > 创建平面覆盖 Surface`
- `GameObject > TileMap3D > 创建平面覆盖 Surface`

创建结构：

```text
Any 3D Object
└─ TileMap3D Surface
   ├─ TileMap3DSurface (Overlay)
   └─ Tilemap Source (Grid)
      └─ Base (Tilemap + TilemapRenderer + TileMap3DLayer)
```

Overlay 不生成、不替换也不关闭目标对象的 MeshRenderer 或 Collider。它作为子对象随目标物体移动；创建时会优先读取直接父物体的 `MeshFilter`、`BoxCollider`、`MeshCollider` 或 `Renderer` 范围，自动匹配当前平面的尺寸、中心和外侧表面。

Overlay 默认使用 `SurfaceMaterial`。源 TilemapRenderer 会被隐藏，但 Tilemap 数据仍然存在并可继续用 Unity Tile Palette、RuleTile、AnimatedTile 和运行时 `SetTile()` 修改。TileMap3D 会创建一个不保存到场景的同 Mesh 覆盖渲染器；它不会复制目标碰撞体，也不会改写目标原材质。Shader 的 Depth Offset 只改变光栅化深度，不会产生 `0.015` 之类的世界空间缝隙。

工作台提供 `对齐 XZ`、`对齐 XY` 和 `对齐 YZ` 快捷按钮，切换方向后会重新适配父物体；父物体尺寸变化后可点击 `适配父物体`。任意斜面可以继续使用 Transform 旋转工具手动对齐。当前阶段不会自动点击 Mesh 三角形提取共面轮廓。

创建 Surface 和使用方向预设时，工具会抵消父物体已有的非等比缩放，避免 Tile 单格随 Cube 或导入模型被拉伸。父物体之后再次修改 Scale，或手动改变 Surface 相对旋转后，可以点击“归一化缩放”重新计算补偿。

如果直接父物体没有可读取的 Mesh、3D Collider 或 Renderer，自动适配会保持现有行列与位置不变，此时可继续手动设置 Transform 和固定区域。

## Surface 尺寸

- “列数”和“行数”定义固定原生 Tilemap 区域。
- “单格尺寸”定义每格在 Surface 上的最终世界尺寸。
- Surface 尺寸始终是 `列数 × 单格尺寸`、`行数 × 单格尺寸`。
- Overlay 自动适配时按 `ceil(目标平面尺寸 ÷ 单格尺寸)` 计算行列；非整格目标会向外补齐到完整格。
- 根对象 Pivot 位于固定区域中心，方便对齐目标 3D 平面。
- `SurfaceMaterial` 始终使用目标 Mesh 的相同几何，“表面偏移”不会参与最终显示。
- “表面偏移”和“图层间距”只用于 `NativeTilemap` 兼容渲染和原生预览。

TileMap3D 会从当前 Tile Palette Brush 或已绘制 Sprite 自动检测源 Tile 原始尺寸，并通过 Grid Scale 保持最终单格世界尺寸。它不会修改 Tile、RuleTile、Sprite PPU 或 Importer。

## 图层与渲染后端

每个 Tilemap 图层都有一个 `TileMap3DLayer` 类型：

| 渲染类型 | 用途 | 深度行为 |
|---|---|---|
| `Base` | 地砖、泥土、草地等基础图层 | Alpha Clip、ZWrite On |
| `Overlay` | 边缘、裂纹、草叶、AnimatedTile 和半透明效果 | Alpha Blend、ZWrite Off、ZTest LEqual |

`SurfaceMaterial` 会按 Hierarchy 顺序把最多 8 个可见图层在同一个目标 Mesh Pass 中进行 Alpha 合成。它保存的是“列数 × 行数 × 图层数”的索引数据，不会按每格像素数复制一张大地图颜色贴图。一个 Surface 当前最多引用 8 张源纹理；使用 SpriteAtlas 可以明显减少纹理槽数量。

每个真实 Tilemap 都是独立图层，层数由 Source Grid 下的 Tilemap 数量决定，不由渲染类型下拉框限制。工作台只提供 `Base` 和 `Overlay` 两种渲染类型；旧场景中的 `Effect` 会自动作为 `Overlay` 兼容处理。

`NativeTilemap` 是兼容后端。Base 使用 `TileMap3D/TilemapSurfaceCutout`，Overlay 使用 `TileMap3D/TilemapSurfaceTransparent`，并通过真实法线偏移分层。它适合不受 SurfaceMaterial 支持范围覆盖的 Sprite、目标对象或平台。

不同高度、不同方向的 Surface 依靠真实 3D 深度遮挡。Sorting Order 只用于同一 Surface 内部的图层关系，不应拿来强制覆盖另一个空间平面。

目标 3D 物体建议使用会写入深度的 Opaque 或 AlphaTest 材质。透明目标 Mesh 本身无法提供可靠的空间遮挡。

## 绘制与动态能力

1. 打开 `TileMap3D > 工作台`。
2. 创建或选择一个 Surface。
3. 在“原生 Tilemap 图层”中选择当前编辑层。
4. 打开 Unity Tile Palette。
5. 直接使用项目已有的 Tile、RuleTile、RuleOverrideTile、RandomTile、AnimatedTile 或自定义 TileBase。

Overlay 的 `SurfaceMaterial` 模式保留 Unity Tilemap 作为唯一数据源：

- RuleTile 仍由 Unity 解析邻接和最终 Sprite。
- AnimatedTile 的帧索引会在运行时更新。
- `Tilemap.SetTile()` 会重建对应 Surface 的索引数据并更新画面。
- Tile 颜色、90 度旋转、翻转和矩形 SpriteAtlas 可以继续使用。
- 源 TilemapRenderer 仅作为编辑数据与兼容回退，不作为最终显示平面。

`NativeTilemap` 则直接使用 Unity TilemapRenderer 的动画、Chunk 合批和视锥裁剪。SurfaceMaterial 遇到无 MeshFilter、旋转/Tight Atlas、超过 8 个图层、超过 8 张源纹理、按格数据预计超过 256 MiB 或平台不支持 Texture2DArray 时，会显示原因并自动回退到 NativeTilemap，避免静默渲染错误。

TileMap3D 不建立第二套格子数据或规则系统，也不会为每个 Tile 创建 GameObject。

## 越界 Tile 检查

固定“列数”和“行数”定义的是 Surface 可显示的唯一矩形区域。Tile Palette 仍可在该区域外写入 Tilemap 数据，但 `SurfaceMaterial` 不会显示这些格子。

- 在工作台“原生 Tilemap 图层”区域打开“显示越界 Tile 警示”，Scene View 会以橙红色格标出全部图层中落在范围外的非空 Tile，同时用橙色边框显示合法区域。
- “越界 Tile”数量按图层统计；同一 Cell 在两个图层都有 Tile 时会计为两个，清理时也会同时删除。
- 点击“清理越界 Tile”后确认，工具会删除全部源图层的越界数据，并支持 Unity `Undo` 恢复。
- 警示仅用于编辑器 Scene View，不会开启原生 `TilemapRenderer`、改变 SurfaceMaterial 渲染，也不会进入打包内容。

## 地面语义与脚步声

创建 `Assets > Create > TileMap3D > Surface Profile`，在 Profile 中把项目已有的 `TileBase` 映射为稳定的 `surfaceId`，例如 `Stone`、`Mud`、`Grass`。把 Profile 赋给 Surface 的“地面语义 Profile”；“玩法查询图层”为空时会从最上层向下查找，指定后则只查询该 Tilemap。

角色脚步系统应对 3D Collider 做 Raycast，并用命中点查询 Tile。TileMap3D 只返回通用语义，不依赖项目的音频实现：

```csharp
if (Physics.Raycast(origin, Vector3.down, out var hit, distance, groundMask)
    && groundSurface.TryGetSurfaceInfo(hit.point, out var info))
{
    footstepAudio.Play(info.SurfaceId);
}
```

查询结果同时包含 `Surface`、`Tilemap`、`Tile`、`Cell` 和 `SurfaceId`。同一个 `surfaceId` 还可以复用于脚印、摩擦、移动速度或粒子效果；不要通过读取 GPU 颜色判断地面类型。

## 3D 几何与碰撞

- Overlay 模式完全使用目标物体已有的 Mesh 和 3D Collider。
- GeneratedGround 模式生成封闭 Mesh 和 BoxCollider，厚度沿本地负 Y 延伸。
- `NativeTilemap` 与 `SurfaceMaterial` 使用 URP 3D 光照，会接受环境光、主方向光和附加点光/聚光灯；指定 `Material Override` 时，由该材质决定是否仍受光。
- `NativeTilemap` 是否接收实时阴影由各图层的 `TileMap3DLayer.Receive Shadows` 控制，默认开启；该设置会同步到 `TilemapRenderer` 和自定义 Shader。紧贴 GeneratedGround 顶面不会产生自阴影，只有实际遮挡物会投下阴影。
- `SurfaceMaterial` 是否接收实时阴影跟随目标 `MeshRenderer`。实时 Tile 图层本身不投射阴影，避免透明轮廓和多层叠加产生错误阴影。
- 不转换 TilemapCollider2D。
- Tile 生成的 GameObject 仍然作为独立场景对象存在。
- 高速 Rigidbody 仍应使用 Continuous 或 Continuous Dynamic 碰撞检测。

## 可选烘焙

只有 `GeneratedGround` 可以执行“烘焙并应用”。现有流程会把所有静态 Tilemap 图层合成为 PNG 和 Material，并应用到生成地面的顶面。

默认输出目录为 `Assets/TileMap3DGenerated`。烘焙后会切换到 `BakedTexture` 模式；切回 `NativeTilemap` 后，原生图层会重新作为最终显示。

Overlay Surface 不提供烘焙按钮，因为它不持有目标地面 Mesh。大地图、AnimatedTile 或运行时可修改地图应保持 `SurfaceMaterial`；只有出现兼容性警告时才切换或回退 `NativeTilemap`。

## 兼容与限制

- 新建 GeneratedGround 默认使用 `NativeTilemap`；新建 Overlay 默认使用 `SurfaceMaterial`。
- 旧 Tilemap 图层缺少 `TileMap3DLayer` 时会按首层 Base、后续层 Overlay 自动补齐。
- 当前版本使用矩形行列范围；不规则平台边界 Mask、Stencil 和 Scene View 点击平面自动创建属于后续能力。
- SurfaceMaterial 只覆盖单个平面，不会自动贴合曲面或跨越两个非共面表面。
- 半透明效果仍遵守透明渲染限制；实体地面优先使用 Base Alpha Clip 图层。


## 安装边界

- Editor 只依赖 Unity 原生 UI Toolkit 与 `Unity.2D.Tilemap.Editor`。
- 为兼容已有场景和外部脚本，现有 C# 类型命名空间暂不迁移；这不构成程序集依赖。
