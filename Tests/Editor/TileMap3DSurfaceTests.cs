using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Tilemaps;
using YokiFrame.Unity.TileMap3D;

namespace TileMap3D.Tests
{
    /// <summary>
    /// 验证平面 Surface 两种承载方式和原生 TilemapRenderer 的核心契约。
    /// </summary>
    public sealed class TileMap3DSurfaceTests
    {
        private GameObject root;
        private readonly List<Object> transientObjects = new List<Object>();

        /// <summary>
        /// 每个测试使用独立根对象，避免 ExecuteAlways 组件相互影响。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            transientObjects.Clear();
            root = new GameObject("TileMap3D Test Root");
            root.SetActive(false);
        }

        /// <summary>
        /// 测试结束后立即销毁临时层级和非持久化 Mesh。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            for (var i = 0; i < transientObjects.Count; i++)
            {
                if (transientObjects[i] != null)
                {
                    Object.DestroyImmediate(transientObjects[i]);
                }
            }

            transientObjects.Clear();
        }

        /// <summary>
        /// 默认材质由 UPM 包自身提供，安装时不应向使用项目创建或修复资源。
        /// </summary>
        [Test]
        public void DefaultMaterials_AreLoadedFromPackagedResources()
        {
            var baseMaterial = Resources.Load<Material>("TileMap3D/TileMap3DBase");
            var overlayMaterial = Resources.Load<Material>("TileMap3D/TileMap3DOverlay");

            Assert.That(baseMaterial, Is.Not.Null);
            Assert.That(overlayMaterial, Is.Not.Null);
            Assert.That(baseMaterial.shader, Is.Not.Null);
            Assert.That(overlayMaterial.shader, Is.Not.Null);
            Assert.That(baseMaterial.shader.name, Is.EqualTo("TileMap3D/TilemapSurfaceCutout"));
            Assert.That(overlayMaterial.shader.name, Is.EqualTo("TileMap3D/TilemapSurfaceTransparent"));
        }

        /// <summary>
        /// Overlay Surface 只承载 Tilemap，不应向任意目标对象注入地面组件。
        /// </summary>
        [Test]
        public void OverlaySurface_DoesNotCreateGeneratedGeometry()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.Overlay);
            root.SetActive(true);
            surface.Rebuild();

            Assert.That(root.GetComponent<MeshFilter>(), Is.Null);
            Assert.That(root.GetComponent<MeshRenderer>(), Is.Null);
            Assert.That(root.GetComponent<BoxCollider>(), Is.Null);
            Assert.That(surface.SurfaceMode, Is.EqualTo(TileMap3DSurfaceMode.Overlay));
            Assert.That(surface.ShowOutOfBoundsTilePreview, Is.True);
        }

        /// <summary>
        /// GeneratedGround 继续创建双子网格 Mesh 和与厚度一致的 BoxCollider。
        /// </summary>
        [Test]
        public void GeneratedGround_RebuildCreatesMeshAndCollider()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.GeneratedGround);
            surface.SetGroundLayout(6, 4, 2f);
            root.SetActive(true);
            surface.Rebuild();

            var meshFilter = root.GetComponent<MeshFilter>();
            var boxCollider = root.GetComponent<BoxCollider>();
            Assert.That(meshFilter, Is.Not.Null);
            Assert.That(meshFilter.sharedMesh, Is.Not.Null);
            Assert.That(meshFilter.sharedMesh.subMeshCount, Is.EqualTo(2));
            Assert.That(boxCollider, Is.Not.Null);
            Assert.That(boxCollider.size.x, Is.EqualTo(12f).Within(0.001f));
            Assert.That(boxCollider.size.z, Is.EqualTo(8f).Within(0.001f));
            Assert.That(boxCollider.size.y, Is.EqualTo(surface.Thickness).Within(0.001f));
        }

        /// <summary>
        /// 原生 Base 与 Overlay 图层应使用不同深度材质，并沿 Surface 法线分层。
        /// </summary>
        [Test]
        public void NativeLayers_ApplyDepthMaterialsAndNormalOffsets()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.Overlay);
            surface.SetRenderOffsets(0.02f, 0.003f);

            var gridObject = new GameObject("Grid");
            gridObject.transform.SetParent(root.transform, false);
            gridObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var grid = gridObject.AddComponent<Grid>();
            var baseLayer = CreateLayer(gridObject.transform, "Base", TileMap3DLayerType.Base);
            var overlayLayer = CreateLayer(
                gridObject.transform,
                "Overlay",
                TileMap3DLayerType.Overlay);
            surface.SetSourceGrid(grid);
            root.SetActive(true);
            surface.Rebuild();

            var baseRenderer = baseLayer.GetComponent<TilemapRenderer>();
            var overlayRenderer = overlayLayer.GetComponent<TilemapRenderer>();
            baseLayer.SetReceiveShadows(false);
            overlayLayer.SetReceiveShadows(true);
            surface.Rebuild();
            Assert.That(baseRenderer.sharedMaterial, Is.Not.Null);
            Assert.That(overlayRenderer.sharedMaterial, Is.Not.Null);
            Assert.That(
                baseRenderer.sharedMaterial.shader.name,
                Is.EqualTo("TileMap3D/TilemapSurfaceCutout"));
            Assert.That(
                overlayRenderer.sharedMaterial.shader.name,
                Is.EqualTo("TileMap3D/TilemapSurfaceTransparent"));
            Assert.That(baseLayer.transform.localPosition.z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                overlayLayer.transform.localPosition.z,
                Is.EqualTo(-0.003f).Within(0.0001f));
            Assert.That(gridObject.transform.localPosition.y, Is.EqualTo(0.02f).Within(0.0001f));
            Assert.That(baseRenderer.forceRenderingOff, Is.False);
            Assert.That(overlayRenderer.forceRenderingOff, Is.False);
            Assert.That(baseRenderer.receiveShadows, Is.False);
            Assert.That(overlayRenderer.receiveShadows, Is.True);
            var properties = new MaterialPropertyBlock();
            baseRenderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_TileMap3DReceiveShadows"), Is.Zero);
            overlayRenderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_TileMap3DReceiveShadows"), Is.EqualTo(1f));
        }

        /// <summary>
        /// 新建 NativeTilemap 图层应默认接收实时阴影，避免沿用 Unity 2D Tilemap 的关闭状态。
        /// </summary>
        [Test]
        public void NativeLayer_ReceivesShadowsByDefault()
        {
            var layerObject = new GameObject("Shadow Receiver Layer");
            layerObject.transform.SetParent(root.transform, false);
            layerObject.AddComponent<Tilemap>();
            var tilemapRenderer = layerObject.AddComponent<TilemapRenderer>();
            var layer = layerObject.AddComponent<TileMap3DLayer>();

            layer.ApplyRendererSettings(0, 0f);

            Assert.That(layer.ReceiveShadows, Is.True);
            Assert.That(tilemapRenderer.receiveShadows, Is.True);
            var properties = new MaterialPropertyBlock();
            tilemapRenderer.GetPropertyBlock(properties);
            Assert.That(properties.GetFloat("_TileMap3DReceiveShadows"), Is.EqualTo(1f));
        }

        /// <summary>
        /// 旧场景中的 Effect 类型应自动归并到 Overlay，避免历史序列化值继续表现为额外的渲染类型。
        /// </summary>
        [Test]
        public void LegacyEffectLayerType_NormalizesToOverlay()
        {
            var layerObject = new GameObject("Legacy Effect Layer");
            layerObject.transform.SetParent(root.transform, false);
            layerObject.AddComponent<Tilemap>();
            layerObject.AddComponent<TilemapRenderer>();
            var layer = layerObject.AddComponent<TileMap3DLayer>();

            layer.Configure(TileMap3DLayerType.Effect);

            Assert.That(layer.LayerType, Is.EqualTo(TileMap3DLayerType.Overlay));
        }

        /// <summary>
        /// XY 侧面挂在非等比缩放物体下时，应能把三个 Surface 局部轴恢复为单位世界长度。
        /// </summary>
        [Test]
        public void NormalizeWorldScale_CompensatesAxisAlignedParentScale()
        {
            root.transform.localScale = new Vector3(4f, 3f, 0.5f);
            var surfaceObject = new GameObject("Wall Surface");
            surfaceObject.transform.SetParent(root.transform, false);
            surfaceObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var surface = surfaceObject.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.Overlay);
            surface.NormalizeWorldScale();
            root.SetActive(true);

            Assert.That(
                surface.transform.TransformVector(Vector3.right).magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                surface.transform.TransformVector(Vector3.up).magnitude,
                Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                surface.transform.TransformVector(Vector3.forward).magnitude,
                Is.EqualTo(1f).Within(0.0001f));
        }

        /// <summary>
        /// Generated Ground 应默认启用世界格网吸附，并且只修正绘制平面的格网相位。
        /// </summary>
        [Test]
        public void AlignToWorldGrid_SnapsValidRegionOriginWithoutChangingNormalPosition()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.GeneratedGround);
            surface.SetGroundLayout(8, 8, 1f);
            CreateSourceTilemap(surface, out var grid);
            root.SetActive(true);
            root.transform.position = new Vector3(1.08f, 2f, 0f);
            var updateMethod = typeof(TileMap3DSurface).GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(surface.KeepWorldGridAligned, Is.True);
            Assert.That(updateMethod, Is.Not.Null);
            updateMethod.Invoke(surface, null);

            var gridOrigin = grid.CellToWorld(surface.GetBakeBounds().min);
            Assert.That(gridOrigin.x, Is.EqualTo(Mathf.Round(gridOrigin.x)).Within(0.0001f));
            Assert.That(gridOrigin.z, Is.EqualTo(Mathf.Round(gridOrigin.z)).Within(0.0001f));
            Assert.That(root.transform.position.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(root.transform.position.y, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(surface.AlignToWorldGrid(), Is.False);
        }

        /// <summary>
        /// 世界格网吸附应使用缩放后的真实 Cell 尺寸，不能按未缩放的 Surface CellSize 吸附。
        /// </summary>
        [Test]
        public void AlignToWorldGrid_UsesScaledWorldCellDimensions()
        {
            root.transform.localScale = new Vector3(2f, 1f, 3f);
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.GeneratedGround);
            surface.SetGroundLayout(8, 8, 1f);
            CreateSourceTilemap(surface, out var grid);
            root.transform.position = new Vector3(0.37f, 2f, 0.82f);
            root.SetActive(true);

            Assert.That(surface.AlignToWorldGrid(), Is.True);

            var gridOrigin = grid.CellToWorld(surface.GetBakeBounds().min);
            var gridRight = grid.CellToWorld(surface.GetBakeBounds().min + Vector3Int.right) - gridOrigin;
            var gridUp = grid.CellToWorld(surface.GetBakeBounds().min + Vector3Int.up) - gridOrigin;
            var rightCoordinate = Vector3.Dot(gridOrigin, gridRight.normalized);
            var upCoordinate = Vector3.Dot(gridOrigin, gridUp.normalized);
            Assert.That(
                rightCoordinate / gridRight.magnitude,
                Is.EqualTo(Mathf.Round(rightCoordinate / gridRight.magnitude)).Within(0.0001f));
            Assert.That(
                upCoordinate / gridUp.magnitude,
                Is.EqualTo(Mathf.Round(upCoordinate / gridUp.magnitude)).Within(0.0001f));
            Assert.That(root.transform.position.y, Is.EqualTo(2f).Within(0.0001f));
        }

        /// <summary>
        /// 旧版 Generated Ground 升级后也应自动启用世界格网吸附，不能继续保留小数相位。
        /// </summary>
        [Test]
        public void LegacyGeneratedGround_UpgradeEnablesWorldGridAlignment()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.GeneratedGround);
            surface.SetGroundLayout(8, 8, 1f);
            CreateSourceTilemap(surface, out var grid);
            SetPrivateField(surface, "layoutVersion", 4);
            SetPrivateField(surface, "keepWorldGridAligned", false);
            root.transform.position = new Vector3(1.08f, 2f, 0f);
            root.SetActive(true);

            InvokePrivate(surface, "Update");

            var gridOrigin = grid.CellToWorld(surface.GetBakeBounds().min);
            Assert.That(surface.KeepWorldGridAligned, Is.True);
            Assert.That(gridOrigin.x, Is.EqualTo(Mathf.Round(gridOrigin.x)).Within(0.0001f));
            Assert.That(gridOrigin.z, Is.EqualTo(Mathf.Round(gridOrigin.z)).Within(0.0001f));
            Assert.That(root.transform.position.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(root.transform.position.y, Is.EqualTo(2f).Within(0.0001f));
        }

        /// <summary>
        /// XZ Overlay 应按父 Cube 的世界宽深自动得到四列三行，并落在顶面。
        /// </summary>
        [Test]
        public void FitToTargetBounds_XZ_MatchesTargetSizeAndTopFace()
        {
            var target = CreateCubeTarget("XZ Target", new Vector3(4f, 0.5f, 3f));
            var surface = CreateOverlaySurface(target, Quaternion.identity);

            Assert.That(surface.TryFitToTargetBounds(), Is.True);
            Assert.That(surface.Columns, Is.EqualTo(4));
            Assert.That(surface.Rows, Is.EqualTo(3));
            Assert.That(surface.GroundSize.x, Is.EqualTo(4f).Within(0.0001f));
            Assert.That(surface.GroundSize.y, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(
                    surface.transform.position,
                    target.transform.TransformPoint(new Vector3(0f, 0.5f, 0f))),
                Is.LessThan(0.0001f));
        }

        /// <summary>
        /// XY Overlay 应把父 Cube 的 X/Y 尺寸映射为列/行，并落在法线朝向的外侧面。
        /// </summary>
        [Test]
        public void FitToTargetBounds_XY_MatchesTargetSizeAndOuterFace()
        {
            var target = CreateCubeTarget("XY Target", new Vector3(4f, 3f, 0.5f));
            var surface = CreateOverlaySurface(target, Quaternion.Euler(-90f, 0f, 0f));

            Assert.That(surface.TryFitToTargetBounds(), Is.True);
            Assert.That(surface.Columns, Is.EqualTo(4));
            Assert.That(surface.Rows, Is.EqualTo(3));
            Assert.That(
                Vector3.Distance(
                    surface.transform.position,
                    target.transform.TransformPoint(new Vector3(0f, 0f, -0.5f))),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(surface.transform.position, target.transform.position),
                Is.GreaterThan(0.1f));
        }

        /// <summary>
        /// 非整格目标尺寸应向上补齐完整格，避免边缘没有可绘制单元格。
        /// </summary>
        [Test]
        public void FitToTargetBounds_RoundsTargetSizeUpToWholeCells()
        {
            var target = CreateCubeTarget("Fractional Target", new Vector3(4.2f, 0.5f, 3.1f));
            var surface = CreateOverlaySurface(target, Quaternion.identity);

            Assert.That(surface.TryFitToTargetBounds(), Is.True);
            Assert.That(surface.Columns, Is.EqualTo(5));
            Assert.That(surface.Rows, Is.EqualTo(4));
        }

        /// <summary>
        /// 父对象没有可用几何范围时，适配应失败且保留用户手动设置的布局与位置。
        /// </summary>
        [Test]
        public void FitToTargetBounds_WithoutTargetGeometry_PreservesLayout()
        {
            var surface = CreateOverlaySurface(root, Quaternion.identity);
            surface.SetGroundLayout(7, 5, 0.5f);
            surface.transform.localPosition = new Vector3(1f, 2f, 3f);
            var originalPosition = surface.transform.localPosition;

            Assert.That(surface.TryFitToTargetBounds(), Is.False);
            Assert.That(surface.Columns, Is.EqualTo(7));
            Assert.That(surface.Rows, Is.EqualTo(5));
            Assert.That(surface.CellSize, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(
                Vector3.Distance(surface.transform.localPosition, originalPosition),
                Is.LessThan(0.0001f));
        }

        /// <summary>
        /// 固定 Surface 区域应只接受定义范围内的 XY Cell，并拒绝其它 Z 层。
        /// </summary>
        [Test]
        public void SurfaceBounds_OnlyContainsConfiguredCellRegion()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.Overlay);
            surface.SetGroundLayout(2, 3, 1f);

            Assert.That(surface.IsCellInsideSurfaceBounds(new Vector3Int(0, 0, 0)), Is.True);
            Assert.That(surface.IsCellInsideSurfaceBounds(new Vector3Int(1, 2, 0)), Is.True);
            Assert.That(surface.IsCellInsideSurfaceBounds(new Vector3Int(2, 2, 0)), Is.False);
            Assert.That(surface.IsCellInsideSurfaceBounds(new Vector3Int(-1, 0, 0)), Is.False);
            Assert.That(surface.IsCellInsideSurfaceBounds(new Vector3Int(0, 3, 0)), Is.False);
            Assert.That(surface.IsCellInsideSurfaceBounds(new Vector3Int(0, 0, 1)), Is.False);
        }

        /// <summary>
        /// 清理越界 Tile 时应覆盖全部图层，同时保留固定 Surface 区域内的数据。
        /// </summary>
        [Test]
        public void ClearOutOfBoundsTiles_RemovesOnlyCellsOutsideSurfaceAcrossAllLayers()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.Overlay);
            var baseTilemap = CreateSourceTilemap(surface, out var grid);
            var overlayTilemap = CreateLayer(
                    grid.transform,
                    "Overlay",
                    TileMap3DLayerType.Overlay)
                .GetComponent<Tilemap>();
            surface.SetGroundLayout(2, 2, 1f);
            var tile = CreateTestTile(Color.white);
            var baseInsideCell = new Vector3Int(0, 0, 0);
            var overlayInsideCell = new Vector3Int(1, 1, 0);
            var baseOutsideCell = new Vector3Int(2, 0, 0);
            var overlayOutsideCell = new Vector3Int(-1, 1, 0);
            baseTilemap.SetTile(baseInsideCell, tile);
            baseTilemap.SetTile(baseOutsideCell, tile);
            overlayTilemap.SetTile(overlayInsideCell, tile);
            overlayTilemap.SetTile(overlayOutsideCell, tile);
            overlayTilemap.SetTile(baseOutsideCell, tile);

            root.SetActive(true);
            Assert.That(surface.CountOutOfBoundsTiles(), Is.EqualTo(3));
            Assert.That(surface.ClearOutOfBoundsTiles(), Is.EqualTo(3));
            Assert.That(baseTilemap.GetTile(baseInsideCell), Is.SameAs(tile));
            Assert.That(overlayTilemap.GetTile(overlayInsideCell), Is.SameAs(tile));
            Assert.That(baseTilemap.GetTile(baseOutsideCell), Is.Null);
            Assert.That(overlayTilemap.GetTile(overlayOutsideCell), Is.Null);
            Assert.That(surface.CountOutOfBoundsTiles(), Is.Zero);
        }

        /// <summary>
        /// 禁用 Surface 期间不会订阅 Tilemap 事件，批量工具必须能强制重建越界缓存。
        /// </summary>
        [Test]
        public void CountOutOfBoundsTiles_ForceRefreshReadsDisabledSurfaceChanges()
        {
            var surface = root.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(
                TileMap3DSurfaceMode.Overlay);
            var tilemap = CreateSourceTilemap(surface, out _);
            surface.SetGroundLayout(1, 1, 1f);
            var tile = CreateTestTile(Color.white);
            root.SetActive(true);
            Assert.That(surface.CountOutOfBoundsTiles(), Is.Zero);

            root.SetActive(false);
            tilemap.SetTile(new Vector3Int(1, 0, 0), tile);

            Assert.That(surface.CountOutOfBoundsTiles(), Is.Zero);
            Assert.That(surface.CountOutOfBoundsTiles(true), Is.EqualTo(1));
        }

        /// <summary>
        /// 世界坐标查询应返回命中的 Tile、Cell 和 Profile 配置的通用地面语义。
        /// </summary>
        [Test]
        public void SurfaceQuery_ReturnsTileCellAndConfiguredSurfaceId()
        {
            var target = CreateCubeTarget("Surface Query Target", new Vector3(4f, 0.5f, 3f));
            var surface = CreateOverlaySurface(target, Quaternion.identity);
            Assert.That(surface.TryFitToTargetBounds(), Is.True);
            var tilemap = CreateSourceTilemap(surface, out var grid);
            var tile = CreateTestTile(Color.gray);
            var cell = Vector3Int.zero;
            tilemap.SetTile(cell, tile);
            var profile = ScriptableObject.CreateInstance<TileMap3DSurfaceProfile>();
            transientObjects.Add(profile);
            profile.SetSurfaceId(tile, "Stone");
            surface.SetSurfaceProfile(profile);

            root.SetActive(true);
            surface.Rebuild();
            var worldPosition = grid.GetCellCenterWorld(cell);

            Assert.That(surface.TryGetSurfaceInfo(worldPosition, out var info), Is.True);
            Assert.That(info.Surface, Is.SameAs(surface));
            Assert.That(info.Tilemap, Is.SameAs(tilemap));
            Assert.That(info.Tile, Is.SameAs(tile));
            Assert.That(info.Cell, Is.EqualTo(cell));
            Assert.That(info.SurfaceId, Is.EqualTo("Stone"));
        }

        /// <summary>
        /// 创建带标准 Cube Mesh、Renderer 和 BoxCollider 的目标对象。
        /// </summary>
        private GameObject CreateCubeTarget(string name, Vector3 scale)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name;
            target.transform.SetParent(root.transform, false);
            target.transform.localScale = scale;
            return target;
        }

        /// <summary>
        /// 在目标下创建指定局部方向的 Overlay Surface，不额外生成 Grid 或 Tilemap。
        /// </summary>
        private static TileMap3DSurface CreateOverlaySurface(
            GameObject target,
            Quaternion rotation)
        {
            var surfaceObject = new GameObject("Overlay Surface");
            surfaceObject.transform.SetParent(target.transform, false);
            surfaceObject.transform.localRotation = rotation;
            var surface = surfaceObject.AddComponent<TileMap3DSurface>();
            surface.ConfigureForCreation(TileMap3DSurfaceMode.Overlay);
            return surface;
        }

        /// <summary>
        /// 为 Surface 创建标准旋转 Grid 和单个 Base Tilemap，并立即绑定数据源。
        /// </summary>
        private static Tilemap CreateSourceTilemap(TileMap3DSurface surface, out Grid grid)
        {
            var gridObject = new GameObject("Tilemap Source");
            gridObject.transform.SetParent(surface.transform, false);
            gridObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            grid = gridObject.AddComponent<Grid>();
            var layer = CreateLayer(gridObject.transform, "Base", TileMap3DLayerType.Base);
            surface.SetSourceGrid(grid);
            return layer.GetComponent<Tilemap>();
        }

        /// <summary>
        /// 创建包含单色可读纹理和 Sprite 的临时 Tile，并登记到测试清理列表。
        /// </summary>
        private Tile CreateTestTile(Color color)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { color, color, color, color });
            texture.Apply();
            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 2f, 2f),
                new Vector2(0.5f, 0.5f),
                2f);
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            transientObjects.Add(tile);
            transientObjects.Add(sprite);
            transientObjects.Add(texture);
            return tile;
        }

        /// <summary>
        /// 创建一个真实原生 Tilemap 图层，供深度材质和偏移测试复用。
        /// </summary>
        private static TileMap3DLayer CreateLayer(
            Transform parent,
            string name,
            TileMap3DLayerType layerType)
        {
            var layerObject = new GameObject(name);
            layerObject.transform.SetParent(parent, false);
            layerObject.AddComponent<Tilemap>();
            layerObject.AddComponent<TilemapRenderer>();
            var layer = layerObject.AddComponent<TileMap3DLayer>();
            layer.Configure(layerType);
            return layer;
        }

        /// <summary>
        /// 为生命周期回归测试写入未暴露的序列化字段，避免测试本身调用会立即重建的公开编辑命令。
        /// </summary>
        private static void SetPrivateField<T>(object target, string fieldName, T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "找不到测试字段：" + fieldName);
            field.SetValue(target, value);
        }

        /// <summary>
        /// 读取生命周期状态字段，确认验证回调只记录了待处理重建请求。
        /// </summary>
        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "找不到测试字段：" + fieldName);
            return (T)field.GetValue(target);
        }

        /// <summary>
        /// 直接触发私有生命周期回调，用于验证 Unity 安全阶段前后的重建时机。
        /// </summary>
        private static void InvokePrivate(object target, string methodName, params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, "找不到测试方法：" + methodName);
            method.Invoke(target, arguments);
        }
    }
}
