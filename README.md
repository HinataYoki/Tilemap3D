# TileMap3D

TileMap3D 是独立的 Unity 原生 Tilemap 3D 平面扩展。它复用 Unity Tile Palette、Tile、RuleTile、AnimatedTile 和 TilemapRenderer，让同一套可重复 Sprite 贴图可以直接用于水平地面、竖直侧面和倾斜斜面。

渲染完全由原生 TilemapRenderer 完成：TileMap3D 不复制 Tile 数据、不生成第二套渲染网格，只把源 Grid 旋转进 3D 空间，并为每个图层替换受 URP 光照的自定义材质。用 Tile Palette 画出的画面即是最终渲染；RuleTile、AnimatedTile 与运行时 `SetTile()` 全部原生生效。

## 核心约定

- `TileMap3DSurface` 根对象的本地 XZ 是绘制平面。
- 根对象的本地 Y 是表面法线。
- 旋转 Surface 根对象即可覆盖世界 XZ、XY、YZ 或任意斜面。
- 一个 Surface 只表示一个平面；同一模型上的非共面区域分别创建多个 Surface。
- 目标对象可以是 Unity Primitive、ProBuilder、导入 Mesh 或任意普通 GameObject。
- 当前版本不支持球面、曲面、蒙皮变形表面或自动跨越拐角。

## 渲染架构

```text
TileMap3DSurface（根：本地 XZ = 绘制面，本地 Y = 法线）
└─ Tilemap Source（Grid，相对根旋转 X+90°）
   ├─ Base（Tilemap + TilemapRenderer + TileMap3DLayer）
   └─ Layer N（可选的更多图层）
```

- **Base 图层** 使用 `TileMap3D/TilemapSurfaceCutout`：AlphaTest 队列、写入深度，带 `Offset -1, -1` 深度偏置避免与目标面 z-fighting。
- **Overlay 图层** 使用 `TileMap3D/TilemapSurfaceTransparent`：透明队列、不写深度，用于软边叠加。
- 图层之间按 Hierarchy 顺序沿法线施加 `Layer Spacing`（默认 0.002）真实几何间距。
- 两个 Shader 均支持 URP 主光阴影、附加光（含 Forward+ / Cluster 光照路径）与雾效，Cutout 图层额外接收 SSAO；逐图层的 `Receive Shadows` 开关经 MaterialPropertyBlock 控制。
- Tile 图层自身不投射阴影（平面贴层投影意义有限，刻意省略）。
- Shader 的 Depth Offset 只改变光栅化深度，不会产生世界空间缝隙。

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

该模式按固定列数、行数、单格尺寸和厚度生成封闭地面 Mesh（顶面 + 侧壁两个子网格）与 BoxCollider。未指定材质时使用包内共享的 `TileMap3D/GroundSurface` 受光材质，底色与侧壁色经 MaterialPropertyBlock 上色，不产生材质实例。

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

工作台提供 `对齐 XZ`、`对齐 XY` 和 `对齐 YZ` 快捷按钮，切换方向后会重新适配父物体；父物体尺寸变化后可点击 `适配父物体`。任意斜面可以继续使用 Transform 旋转工具手动对齐。

创建 Surface 和使用方向预设时，工具会抵消父物体已有的非等比缩放，避免 Tile 单格随 Cube 或导入模型被拉伸。父物体之后再次修改 Scale，或手动改变 Surface 相对旋转后，可以点击"归一化缩放"重新计算补偿。

## 工作台

菜单 `TileMap3D > 工作台` 打开管理窗口：

- 图层管理：新增 / 删除 Tilemap 图层，切换 Base / Overlay 渲染类型，一键设为 Tile Palette 绘制目标。
- 固定区域：列数、行数、单格尺寸；越界 Tile 警示与一键清理（支持所有已加载场景批量清理，可 Undo）。
- 表面参数：表面偏移（源 Grid 沿法线抬升）、图层间距。
- Generated Ground：厚度、底色、地面 / 侧壁材质、世界格网吸附。

## 玩法查询

`TileMap3DSurface.TryGetSurfaceInfo(worldPosition, out info)` 把世界坐标映射到 Cell 并返回命中的 Tilemap、Tile 与可选地面语义。语义映射由 `TileMap3D Surface Profile`（ScriptableObject）配置，可用于脚步声、减速区等玩法逻辑，与渲染完全解耦。

## 使用建议

- 同一 Surface 的全部 Tile 应使用统一的 Sprite 原始尺寸；工具会在检测到混用时给出警告。
- 3D 透视 / 掠射角下建议为 Tile 图集开启 mipmap，并保证至少 8px 图集 padding 与 Aniso 4+，避免远距闪烁与图集串色。
- 透明 Overlay 图层与场景中其它透明物体之间的排序遵循 Unity 透明队列规则，复杂穿插场景建议优先使用 Base（Cutout）图层。
