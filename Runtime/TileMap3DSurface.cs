using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 控制 TileMap3DSurface 是否自己生成承载几何。
    /// </summary>
    public enum TileMap3DSurfaceMode
    {
        GeneratedGround,
        Overlay
    }

    /// <summary>
    /// 控制 3D 地面尺寸的来源。
    /// </summary>
    public enum TileMap3DSizeMode
    {
        TilemapRegion,
        Custom
    }

    /// <summary>
    /// 将原生 Grid 与 Tilemap 的绘制结果映射到固定方向的 3D 平面。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class TileMap3DSurface : MonoBehaviour
    {
        private const string BakedSurfaceShaderName = "TileMap3D/BakedSurface";
        private const int CurrentLayoutVersion = 7;
        private const float MinimumCellSize = 0.01f;
        private const float MinimumThickness = 0.01f;
        private const float MinimumSurfaceSize = 0.01f;
        private const float MinimumSurfaceOffset = 0f;
        private const float MinimumLayerSpacing = 0.0001f;
        private const float GridCenterTolerance = 0.0001f;
#if UNITY_EDITOR
        private const float OutOfBoundsGizmoFillRatio = 0.86f;
        private const float OutOfBoundsGizmoDepthRatio = 0.04f;
        private const float MinimumOutOfBoundsGizmoDepth = 0.005f;
#endif

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Grid sourceGrid;
        [SerializeField] private TileMap3DSurfaceProfile surfaceProfile;
        [SerializeField] private Tilemap surfaceQueryLayer;
        [SerializeField] private TileMap3DSurfaceMode surfaceMode = TileMap3DSurfaceMode.Overlay;
        [SerializeField, HideInInspector] private bool automaticBounds;
        [SerializeField] private BoundsInt bakeBounds = new BoundsInt(0, 0, 0, 8, 8, 1);
        [SerializeField, Min(MinimumCellSize)] private float cellSize = 1f;
        [SerializeField, HideInInspector] private bool keepWorldGridAligned;
        [SerializeField, HideInInspector] private TileMap3DSizeMode sizeMode = TileMap3DSizeMode.TilemapRegion;
        [SerializeField, HideInInspector] private Vector2 customSize = new Vector2(8f, 8f);
        [SerializeField, HideInInspector] private int layoutVersion = CurrentLayoutVersion;
        [SerializeField, Min(MinimumSurfaceOffset)] private float surfaceOffset = 0f;
        [SerializeField, Min(MinimumLayerSpacing)] private float layerSpacing = 0.002f;
        [SerializeField, Min(MinimumThickness)] private float thickness = 0.5f;
        [SerializeField] private Color surfaceColor = Color.white;
        [SerializeField] private Material groundMaterial;
        [SerializeField] private Material sideMaterial;
        [SerializeField] private bool showSourcePreview = true;
        [SerializeField] private bool showOutOfBoundsTilePreview;

        [NonSerialized] private Mesh generatedMesh;
        [NonSerialized] private Material placeholderMaterial;
        [NonSerialized] private Material defaultSideMaterial;
        [NonSerialized] private bool isRebuilding;
        [NonSerialized] private bool rebuildRequested;
        [NonSerialized] private bool synchronizeSourceTileSizeOnRebuild;
#if UNITY_EDITOR
        [NonSerialized] private bool delayedEditorRebuildScheduled;
        [NonSerialized] private bool worldGridTransformInitialized;
        [NonSerialized] private Vector3 lastWorldGridPosition;
        [NonSerialized] private Quaternion lastWorldGridRotation;
        [NonSerialized] private Vector3 lastWorldGridScale;
#endif
        [NonSerialized] private bool hasMixedSourceTileSizes;
        [NonSerialized] private string sourceTileSizeWarning;
        [NonSerialized] private Tilemap[] sourceTilemapsCache;
        [NonSerialized] private bool outOfBoundsTileCacheDirty = true;
        [NonSerialized] private List<Vector3Int> outOfBoundsTilePositions;
        [SerializeField, HideInInspector] private bool ownsGeneratedGeometry;

        public Grid SourceGrid => sourceGrid;
        public TileMap3DSurfaceProfile SurfaceProfile => surfaceProfile;
        public Tilemap SurfaceQueryLayer => surfaceQueryLayer;
        public TileMap3DSurfaceMode SurfaceMode => surfaceMode;
        public bool AutomaticBounds => automaticBounds;
        public TileMap3DSizeMode SizeMode => sizeMode;
        public int Columns => GetBakeBounds().size.x;
        public int Rows => GetBakeBounds().size.y;
        public float CellSize => cellSize;
        public Vector2 GroundSize => new Vector2(Columns * cellSize, Rows * cellSize);
        public bool KeepWorldGridAligned => keepWorldGridAligned;
        public Vector2 SourceTileSize => GetSourceGridCellSize();
        public string SourceTileSizeWarning => sourceTileSizeWarning;
        public float SurfaceOffset => surfaceOffset;
        public float LayerSpacing => layerSpacing;
        public float Thickness => thickness;
        public Color SurfaceColor => surfaceColor;
        public Material GroundMaterial => groundMaterial;
        public bool ShowSourcePreview => showSourcePreview;
        public bool ShowOutOfBoundsTilePreview => showOutOfBoundsTilePreview;

        /// <summary>
        /// 配置新建 Surface 的承载方式，不迁移已有 Tile 数据。
        /// </summary>
        public void ConfigureForCreation(TileMap3DSurfaceMode newSurfaceMode)
        {
            surfaceMode = newSurfaceMode;
            showSourcePreview = true;
            showOutOfBoundsTilePreview = true;
            keepWorldGridAligned = newSurfaceMode == TileMap3DSurfaceMode.GeneratedGround;
            layoutVersion = CurrentLayoutVersion;
            EnsureSerializedValues();
            ApplySourcePreviewVisibility();
            Rebuild();
        }

        /// <summary>
        /// 切换平面是否生成自己的 Mesh 和 BoxCollider。
        /// </summary>
        public void SetSurfaceMode(TileMap3DSurfaceMode value)
        {
            if (surfaceMode == value)
            {
                return;
            }

            surfaceMode = value;
            showSourcePreview = true;
            Rebuild();
        }

        /// <summary>
        /// 设置源 Grid 到表面法线方向的偏移和同一 Surface 的图层间距。
        /// </summary>
        public void SetRenderOffsets(float newSurfaceOffset, float newLayerSpacing)
        {
            surfaceOffset = Mathf.Max(MinimumSurfaceOffset, newSurfaceOffset);
            layerSpacing = Mathf.Max(MinimumLayerSpacing, newLayerSpacing);
            ApplySourceGridTransform();
            Rebuild();
        }

        /// <summary>
        /// 抵消父级缩放对当前局部轴长度的影响，保持平面 Tile 单格在世界空间中比例稳定。
        /// </summary>
        public void NormalizeWorldScale()
        {
            var parent = transform.parent;
            if (parent == null)
            {
                transform.localScale = Vector3.one;
                Rebuild();
                return;
            }

            var parentMatrix = parent.localToWorldMatrix;
            var localRotation = transform.localRotation;
            var worldXLength = parentMatrix.MultiplyVector(localRotation * Vector3.right).magnitude;
            var worldYLength = parentMatrix.MultiplyVector(localRotation * Vector3.up).magnitude;
            var worldZLength = parentMatrix.MultiplyVector(localRotation * Vector3.forward).magnitude;
            transform.localScale = new Vector3(
                worldXLength > Mathf.Epsilon ? 1f / worldXLength : 1f,
                worldYLength > Mathf.Epsilon ? 1f / worldYLength : 1f,
                worldZLength > Mathf.Epsilon ? 1f / worldZLength : 1f);
            Rebuild();
        }

        /// <summary>
        /// 启用 Generated Ground 的世界格网吸附，并立即修正有效区域左下角的格网相位。
        /// </summary>
        public bool EnableWorldGridAlignment()
        {
            if (surfaceMode != TileMap3DSurfaceMode.GeneratedGround)
            {
                return false;
            }

            keepWorldGridAligned = true;
            return AlignToWorldGrid();
        }

        /// <summary>
        /// 关闭 Generated Ground 的世界格网吸附，保留当前 Transform 和全部 Tile 坐标。
        /// </summary>
        public void DisableWorldGridAlignment()
        {
            keepWorldGridAligned = false;
#if UNITY_EDITOR
            worldGridTransformInitialized = false;
#endif
        }

        /// <summary>
        /// 仅沿绘制平面的两个轴移动 Surface，使有效区域左下角落在完整世界 Cell 格网上。
        /// </summary>
        public bool AlignToWorldGrid()
        {
            if (sourceGrid == null)
            {
                return false;
            }

            var gridOrigin = sourceGrid.CellToWorld(GetBakeBounds().min);
            var gridRight = sourceGrid.CellToWorld(GetBakeBounds().min + Vector3Int.right) - gridOrigin;
            var gridUp = sourceGrid.CellToWorld(GetBakeBounds().min + Vector3Int.up) - gridOrigin;
            var rightCellSize = gridRight.magnitude;
            var upCellSize = gridUp.magnitude;
            if (rightCellSize <= Mathf.Epsilon || upCellSize <= Mathf.Epsilon)
            {
                return false;
            }

            var rightDirection = gridRight / rightCellSize;
            var upDirection = gridUp / upCellSize;
            var axisDot = Vector3.Dot(rightDirection, upDirection);
            var determinant = 1f - axisDot * axisDot;
            if (determinant <= Mathf.Epsilon)
            {
                return false;
            }

            var rightCoordinate = Vector3.Dot(gridOrigin, rightDirection);
            var upCoordinate = Vector3.Dot(gridOrigin, upDirection);
            var rightOffset = Mathf.Round(rightCoordinate / rightCellSize) * rightCellSize
                - rightCoordinate;
            var upOffset = Mathf.Round(upCoordinate / upCellSize) * upCellSize - upCoordinate;
            var correction = rightDirection * ((rightOffset - axisDot * upOffset) / determinant)
                + upDirection * ((upOffset - axisDot * rightOffset) / determinant);
            if (correction.sqrMagnitude <= GridCenterTolerance * GridCenterTolerance)
            {
#if UNITY_EDITOR
                CaptureWorldGridTransform();
#endif
                return false;
            }

            transform.position += correction;
#if UNITY_EDITOR
            CaptureWorldGridTransform();
#endif
            return true;
        }

        /// <summary>
        /// 将 Overlay 的固定格子区域、中心和法线位置适配到直接父物体的平面范围。
        /// </summary>
        public bool TryFitToTargetBounds()
        {
            if (surfaceMode != TileMap3DSurfaceMode.Overlay)
            {
                return false;
            }

            var target = transform.parent;
            if (target == null
                || !TryGetTargetBounds(target, out var targetBounds, out var boundsToWorld))
            {
                return false;
            }

            NormalizeWorldScale();
            var minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            var maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            var center = targetBounds.center;
            var extents = targetBounds.extents;
            for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                var corner = center + new Vector3(
                    (cornerIndex & 1) == 0 ? -extents.x : extents.x,
                    (cornerIndex & 2) == 0 ? -extents.y : extents.y,
                    (cornerIndex & 4) == 0 ? -extents.z : extents.z);
                var worldCorner = boundsToWorld.MultiplyPoint3x4(corner);
                var surfaceCorner = transform.InverseTransformPoint(worldCorner);
                minimum = Vector3.Min(minimum, surfaceCorner);
                maximum = Vector3.Max(maximum, surfaceCorner);
            }

            var width = maximum.x - minimum.x;
            var depth = maximum.z - minimum.z;
            if (!IsFinite(width) || !IsFinite(depth)
                || width < MinimumSurfaceSize || depth < MinimumSurfaceSize)
            {
                return false;
            }

            var fittedPosition = transform.TransformPoint(new Vector3(
                (minimum.x + maximum.x) * 0.5f,
                maximum.y,
                (minimum.z + maximum.z) * 0.5f));
            transform.position = fittedPosition;
            SetGroundLayout(
                GetRequiredCellCount(width),
                GetRequiredCellCount(depth),
                cellSize);
            return true;
        }

        /// <summary>
        /// 绑定原生 Grid，并立即同步预览状态和 3D 地面尺寸。
        /// </summary>
        public void SetSourceGrid(Grid value)
        {
            if (sourceGrid == value)
            {
                return;
            }

            sourceGrid = value;
            sourceTilemapsCache = null;
            SynchronizeSourceTileSizeFromExistingTiles();
            ApplySourceGridTransform();
            ApplySourcePreviewVisibility();
            Rebuild();
        }

        /// <summary>
        /// 设置 Tile 到通用地面语义的映射 Profile，不改变 Tilemap 或渲染数据。
        /// </summary>
        public void SetSurfaceProfile(TileMap3DSurfaceProfile value)
        {
            surfaceProfile = value;
        }

        /// <summary>
        /// 指定玩法查询使用的 Tilemap；为空时按图层从上到下寻找可用 Tile。
        /// </summary>
        public void SetSurfaceQueryLayer(Tilemap value)
        {
            surfaceQueryLayer = value;
        }

        /// <summary>
        /// 在源 Grid 尚未包含其它尺寸 Tile 时，同步 Tile Palette Brush 的原始单格尺寸。
        /// </summary>
        public bool TrySynchronizeSourceTileSize(Vector2 nativeTileSize)
        {
            if (sourceGrid == null)
            {
                return false;
            }

            var normalized = NormalizeSourceTileSize(nativeTileSize);
            var current = GetSourceGridCellSize();
            if (AreSourceTileSizesEqual(current, normalized))
            {
                if (!hasMixedSourceTileSizes)
                {
                    sourceTileSizeWarning = string.Empty;
                }

                return false;
            }

            if (HasSourceTiles())
            {
                ReportSourceTileSizeWarning(current, normalized, false);
                return false;
            }

            hasMixedSourceTileSizes = false;
            sourceTileSizeWarning = string.Empty;
            return SetSourceGridCellSize(normalized);
        }

        /// <summary>
        /// 设置固定列数、行数和单格世界尺寸，不删除范围外的 Tilemap 数据。
        /// </summary>
        public void SetGroundLayout(int columns, int rows, float worldCellSize)
        {
            var origin = bakeBounds.position;
            bakeBounds = new BoundsInt(
                origin,
                new Vector3Int(Mathf.Max(1, columns), Mathf.Max(1, rows), 1));
            cellSize = Mathf.Max(MinimumCellSize, worldCellSize);
            automaticBounds = false;
            sizeMode = TileMap3DSizeMode.TilemapRegion;
            layoutVersion = CurrentLayoutVersion;
            ApplySourceGridTransform();
            Rebuild();
        }

        /// <summary>
        /// 切换原生 TilemapRenderer 预览，不改变图层自身的 enabled 状态。
        /// </summary>
        public void SetSourcePreviewVisible(bool visible)
        {
            if (showSourcePreview == visible)
            {
                return;
            }

            showSourcePreview = visible;
            ApplySourcePreviewVisibility();
            Rebuild();
        }

        /// <summary>
        /// 切换 Scene View 中的越界 Tile 警示，不改变 TilemapRenderer 或运行时渲染结果。
        /// </summary>
        public void SetOutOfBoundsTilePreviewVisible(bool visible)
        {
            if (showOutOfBoundsTilePreview == visible)
            {
                return;
            }

            showOutOfBoundsTilePreview = visible;
            if (visible)
            {
                InvalidateOutOfBoundsTileCache();
            }
        }

        /// <summary>
        /// 判断 Cell 是否位于当前列数、行数和起点共同定义的固定 Surface 区域内。
        /// </summary>
        public bool IsCellInsideSurfaceBounds(Vector3Int cell)
        {
            return GetBakeBounds().Contains(cell);
        }

        /// <summary>
        /// 统计全部源图层中的越界 Tile；同一 Cell 在多个图层有 Tile 时分别计数。
        /// </summary>
        public int CountOutOfBoundsTiles(bool forceRefresh = false)
        {
            EnsureOutOfBoundsTileCache(forceRefresh);
            return outOfBoundsTilePositions.Count;
        }

        /// <summary>
        /// 删除全部源图层中的越界 Tile，并返回实际删除数量；编辑器 Undo 由调用方登记。
        /// </summary>
        public int ClearOutOfBoundsTiles()
        {
            var clearedCount = 0;
            var tilemaps = GetSourceTilemaps(true);
            for (var tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                var tilemap = tilemaps[tilemapIndex];
                if (tilemap == null)
                {
                    continue;
                }

                var positions = new List<Vector3Int>();
                AppendOutOfBoundsTilePositions(tilemap, positions);
                if (positions.Count == 0)
                {
                    continue;
                }

                tilemap.SetTiles(positions.ToArray(), new TileBase[positions.Count]);
                tilemap.CompressBounds();
                clearedCount += positions.Count;
            }

            if (clearedCount > 0)
            {
                InvalidateOutOfBoundsTileCache();
                RequestRebuild();
            }

            return clearedCount;
        }

        /// <summary>
        /// 返回当前参与编辑的全部原生 Tilemap，调用方可自行检查 Renderer 启用状态。
        /// </summary>
        public Tilemap[] GetSourceTilemaps(bool includeInactive = true)
        {
            if (sourceTilemapsCache != null)
            {
                return sourceTilemapsCache;
            }

            sourceTilemapsCache = sourceGrid != null
                ? sourceGrid.GetComponentsInChildren<Tilemap>(includeInactive)
                : Array.Empty<Tilemap>();
            return sourceTilemapsCache;
        }

        /// <summary>
        /// 标记越界 Tile 统计和 Scene View 警示缓存需要在下次读取时重建。
        /// </summary>
        private void InvalidateOutOfBoundsTileCache()
        {
            outOfBoundsTileCacheDirty = true;
        }

        /// <summary>
        /// 按当前固定 Surface 区域重建全部图层的越界 Tile 缓存，避免 Scene View 每帧重复枚举大地图。
        /// </summary>
        private void EnsureOutOfBoundsTileCache(bool forceRefresh = false)
        {
            if (!forceRefresh && !outOfBoundsTileCacheDirty && outOfBoundsTilePositions != null)
            {
                return;
            }

            if (outOfBoundsTilePositions == null)
            {
                outOfBoundsTilePositions = new List<Vector3Int>();
            }
            else
            {
                outOfBoundsTilePositions.Clear();
            }

            var tilemaps = GetSourceTilemaps(true);
            for (var tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                AppendOutOfBoundsTilePositions(tilemaps[tilemapIndex], outOfBoundsTilePositions);
            }

            outOfBoundsTileCacheDirty = false;
        }

        /// <summary>
        /// 将一个原生 Tilemap 中位于固定 Surface 区域外的非空 Cell 追加到目标集合。
        /// </summary>
        private void AppendOutOfBoundsTilePositions(Tilemap tilemap, List<Vector3Int> positions)
        {
            if (tilemap == null || positions == null)
            {
                return;
            }

            var tileBounds = tilemap.cellBounds;
            if (tileBounds.size.x <= 0 || tileBounds.size.y <= 0 || tileBounds.size.z <= 0)
            {
                return;
            }

            var end = tileBounds.max - Vector3Int.one;
            var tileCount = tilemap.GetTilesRangeCount(tileBounds.min, end);
            if (tileCount <= 0)
            {
                return;
            }

            var tilePositions = new Vector3Int[tileCount];
            var tiles = new TileBase[tileCount];
            var actualCount = tilemap.GetTilesRangeNonAlloc(
                tileBounds.min,
                end,
                tilePositions,
                tiles);
            var surfaceBounds = GetBakeBounds();
            for (var tileIndex = 0; tileIndex < actualCount; tileIndex++)
            {
                if (!surfaceBounds.Contains(tilePositions[tileIndex]))
                {
                    positions.Add(tilePositions[tileIndex]);
                }
            }
        }

        /// <summary>
        /// 把世界坐标映射到当前 Surface 的 Cell，并返回 Tile 与可选地面语义。
        /// </summary>
        public bool TryGetSurfaceInfo(Vector3 worldPosition, out TileMap3DSurfaceInfo surfaceInfo)
        {
            surfaceInfo = default;
            if (sourceGrid == null)
            {
                return false;
            }

            var localPosition = transform.InverseTransformPoint(worldPosition);
            var halfSize = GroundSize * 0.5f;
            if (localPosition.x < -halfSize.x || localPosition.x > halfSize.x
                || localPosition.z < -halfSize.y || localPosition.z > halfSize.y)
            {
                return false;
            }

            var cell = sourceGrid.WorldToCell(worldPosition);
            if (surfaceQueryLayer != null
                && surfaceQueryLayer.transform.IsChildOf(sourceGrid.transform))
            {
                var queryTile = surfaceQueryLayer.GetTile(cell);
                return TryCreateSurfaceInfo(surfaceQueryLayer, queryTile, cell, out surfaceInfo);
            }

            Tilemap fallbackTilemap = null;
            TileBase fallbackTile = null;
            var tilemaps = GetSourceTilemaps(true);
            for (var i = tilemaps.Length - 1; i >= 0; i--)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null || !tilemap.gameObject.activeSelf || !tilemap.enabled)
                {
                    continue;
                }

                var tile = tilemap.GetTile(cell);
                if (tile == null)
                {
                    continue;
                }

                if (fallbackTile == null)
                {
                    fallbackTilemap = tilemap;
                    fallbackTile = tile;
                }

                if (surfaceProfile != null
                    && surfaceProfile.TryGetSurfaceId(tile, out var surfaceId))
                {
                    surfaceInfo = new TileMap3DSurfaceInfo
                    {
                        Surface = this,
                        Tilemap = tilemap,
                        Tile = tile,
                        Cell = cell,
                        SurfaceId = surfaceId
                    };
                    return true;
                }

                if (surfaceProfile == null)
                {
                    break;
                }
            }

            return TryCreateSurfaceInfo(fallbackTilemap, fallbackTile, cell, out surfaceInfo);
        }

        /// <summary>
        /// 将非空 Tile 组装为查询结果，并从 Profile 补充可选地面语义。
        /// </summary>
        private bool TryCreateSurfaceInfo(
            Tilemap tilemap,
            TileBase tile,
            Vector3Int cell,
            out TileMap3DSurfaceInfo surfaceInfo)
        {
            if (tilemap == null || tile == null)
            {
                surfaceInfo = default;
                return false;
            }

            var surfaceId = string.Empty;
            if (surfaceProfile != null)
            {
                surfaceProfile.TryGetSurfaceId(tile, out surfaceId);
            }

            surfaceInfo = new TileMap3DSurfaceInfo
            {
                Surface = this,
                Tilemap = tilemap,
                Tile = tile,
                Cell = cell,
                SurfaceId = surfaceId
            };
            return true;
        }

        /// <summary>
        /// 取得由 TileMap3D 列数和行数定义的固定烘焙区域。
        /// </summary>
        public BoundsInt GetBakeBounds()
        {
            return NormalizeBounds(bakeBounds);
        }

        /// <summary>
        /// 将单元格区域转换为 Grid 本地 XY 包围矩形，供烘焙投影和尺寸计算共用。
        /// </summary>
        public Rect GetGridLocalBakeRect(BoundsInt bounds)
        {
            var normalized = NormalizeBounds(bounds);
            if (sourceGrid == null)
            {
                return new Rect(
                    normalized.xMin,
                    normalized.yMin,
                    normalized.size.x,
                    normalized.size.y);
            }

            var first = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMin, normalized.yMin, 0f));
            var second = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMax, normalized.yMin, 0f));
            var third = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMin, normalized.yMax, 0f));
            var fourth = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMax, normalized.yMax, 0f));

            var minimumX = Mathf.Min(first.x, second.x, third.x, fourth.x);
            var maximumX = Mathf.Max(first.x, second.x, third.x, fourth.x);
            var minimumY = Mathf.Min(first.y, second.y, third.y, fourth.y);
            var maximumY = Mathf.Max(first.y, second.y, third.y, fourth.y);
            return Rect.MinMaxRect(minimumX, minimumY, maximumX, maximumY);
        }

        /// <summary>
        /// 生成或刷新顶面、封闭侧壁、底面和 BoxCollider。
        /// </summary>
        public void Rebuild()
        {
            if (isRebuilding)
            {
                return;
            }

            InvalidateOutOfBoundsTileCache();
            isRebuilding = true;
            try
            {
                RebuildInternal();
            }
            finally
            {
                isRebuilding = false;
            }
        }

        /// <summary>
        /// 请求在安全时机重建 Surface；供 Tilemap 图层的生命周期回调使用，避免在 OnValidate 内创建对象。
        /// </summary>
        internal void RequestRebuild()
        {
            RequestRebuild(false);
        }

        /// <summary>
        /// 合并连续重建请求，并在编辑器验证结束后或运行时下一次安全更新中统一执行。
        /// </summary>
        private void RequestRebuild(bool synchronizeSourceTileSize)
        {
            rebuildRequested = true;
            synchronizeSourceTileSizeOnRebuild |= synchronizeSourceTileSize;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                ScheduleDelayedEditorRebuild();
            }
#endif
        }

        /// <summary>
        /// 执行已合并的重建请求；若正在重建或对象停用，保留请求等待后续安全时机。
        /// </summary>
        private void ProcessRequestedRebuild()
        {
            if (this == null || !rebuildRequested || isRebuilding || !isActiveAndEnabled)
            {
                return;
            }

            var synchronizeSourceTileSize = synchronizeSourceTileSizeOnRebuild;
            rebuildRequested = false;
            synchronizeSourceTileSizeOnRebuild = false;
            if (synchronizeSourceTileSize)
            {
                SynchronizeSourceTileSizeFromExistingTiles();
            }

            Rebuild();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 将编辑器重建延迟到 Unity 完成 OnValidate、CheckConsistency 和序列化校验之后。
        /// </summary>
        private void ScheduleDelayedEditorRebuild()
        {
            if (delayedEditorRebuildScheduled)
            {
                return;
            }

            delayedEditorRebuildScheduled = true;
            EditorApplication.delayCall += ExecuteDelayedEditorRebuild;
        }

        /// <summary>
        /// 处理一次延迟重建，并在请求仍未消费时重新排队以避开层级回调重入。
        /// </summary>
        private void ExecuteDelayedEditorRebuild()
        {
            EditorApplication.delayCall -= ExecuteDelayedEditorRebuild;
            delayedEditorRebuildScheduled = false;
            if (this == null)
            {
                return;
            }

            ProcessRequestedRebuild();
            if (this != null)
            {
                // 保存/校验结束后材质状态可能没有触发 SceneView 自己的重绘。
                SceneView.RepaintAll();
                if (rebuildRequested && isActiveAndEnabled)
                {
                    ScheduleDelayedEditorRebuild();
                }
            }
        }

        /// <summary>
        /// 组件停用或销毁时撤销延迟回调，避免回调继续持有场景对象引用。
        /// </summary>
        private void CancelDelayedEditorRebuild()
        {
            EditorApplication.delayCall -= ExecuteDelayedEditorRebuild;
            delayedEditorRebuildScheduled = false;
        }
#endif

        /// <summary>
        /// 写入持久化烘焙产物，并按设置隐藏原生 Tilemap 预览。
        /// 已废弃：BakedTexture 渲染模式已移除，保留此方法签名以避免旧代码编译报错。
        /// </summary>
        [System.Obsolete("BakedTexture 模式已移除，此方法不再执行任何操作。")]
        public void ApplyBakeOutput(Texture2D texture, Material material, BoundsInt bounds, string id)
        {
        }

        /// <summary>
        /// 返回用于稳定生成资源文件名的组件标识。
        /// 已废弃：BakedTexture 渲染模式已移除。
        /// </summary>
        [System.Obsolete("BakedTexture 模式已移除，此方法不再执行任何操作。")]
        public string EnsureBakeId()
        {
            return string.Empty;
        }

        /// <summary>
        /// 组件启用时恢复非序列化 Mesh、材质和源图层可见性。
        /// </summary>
        private void OnEnable()
        {
            Tilemap.tilemapTileChanged -= HandleTilemapChanged;
            Tilemap.tilemapTileChanged += HandleTilemapChanged;
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= HandleUndoRedo;
            Undo.undoRedoPerformed += HandleUndoRedo;
#endif
            InvalidateOutOfBoundsTileCache();
            EnsureSerializedValues();
            RequestRebuild(true);
        }

        /// <summary>
        /// 组件停用时解除原生 Tilemap 静态事件，避免编辑器域重载后保留场景引用。
        /// </summary>
        private void OnDisable()
        {
            Tilemap.tilemapTileChanged -= HandleTilemapChanged;
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= HandleUndoRedo;
            CancelDelayedEditorRebuild();
            worldGridTransformInitialized = false;
#endif
            rebuildRequested = false;
            synchronizeSourceTileSizeOnRebuild = false;
        }

        /// <summary>
        /// 每帧仅同步 Surface 变换、目标 Renderer 状态和 AnimatedTile 帧索引。
        /// </summary>
        private void Update()
        {
#if UNITY_EDITOR
            MaintainWorldGridAlignmentInEditor();
#endif
            ProcessRequestedRebuild();
        }

        /// <summary>
        /// Inspector 数据变化后约束参数并刷新 3D 表面。
        /// </summary>
        private void OnValidate()
        {
            InvalidateOutOfBoundsTileCache();
            EnsureSerializedValues();
            RequestRebuild(true);
        }

        /// <summary>
        /// Grid 下新增或移除 Tilemap 时刷新预览状态和 3D 表面。
        /// </summary>
        private void OnTransformChildrenChanged()
        {
            sourceTilemapsCache = null;
            InvalidateOutOfBoundsTileCache();
            RequestRebuild(true);
        }

        /// <summary>
        /// 组件销毁时释放仅供当前场景实例使用的临时资源。
        /// </summary>
        private void OnDestroy()
        {
            Tilemap.tilemapTileChanged -= HandleTilemapChanged;
#if UNITY_EDITOR
            Undo.undoRedoPerformed -= HandleUndoRedo;
            CancelDelayedEditorRebuild();
#endif
            ReleaseGeneratedObject(generatedMesh);
            ReleaseGeneratedObject(placeholderMaterial);
            ReleaseGeneratedObject(defaultSideMaterial);
            generatedMesh = null;
            placeholderMaterial = null;
            defaultSideMaterial = null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑模式下检测 Generated Ground 的世界变换，移动或旋转后恢复完整 Cell 格网相位。
        /// </summary>
        private void MaintainWorldGridAlignmentInEditor()
        {
            if (Application.isPlaying
                || !keepWorldGridAligned
                || surfaceMode != TileMap3DSurfaceMode.GeneratedGround
                || sourceGrid == null)
            {
                worldGridTransformInitialized = false;
                return;
            }

            var worldScale = transform.lossyScale;
            if (worldGridTransformInitialized
                && transform.position == lastWorldGridPosition
                && transform.rotation == lastWorldGridRotation
                && worldScale == lastWorldGridScale)
            {
                return;
            }

            AlignToWorldGrid();
            CaptureWorldGridTransform();
        }

        /// <summary>
        /// 记录对齐后的世界变换，避免 ExecuteAlways 在静止状态重复计算和写入。
        /// </summary>
        private void CaptureWorldGridTransform()
        {
            worldGridTransformInitialized = true;
            lastWorldGridPosition = transform.position;
            lastWorldGridRotation = transform.rotation;
            lastWorldGridScale = transform.lossyScale;
        }

        /// <summary>
        /// Undo 或 Redo 恢复 Tilemap 数据后使越界缓存失效。
        /// </summary>
        private void HandleUndoRedo()
        {
            if (this == null)
            {
                return;
            }

            InvalidateOutOfBoundsTileCache();
            if (showOutOfBoundsTilePreview)
            {
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// 开启警示时在 Scene View 绘制固定区域边框和所有含 Tile 的越界 Cell。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!showOutOfBoundsTilePreview || sourceGrid == null || !isActiveAndEnabled)
            {
                return;
            }

            EnsureOutOfBoundsTileCache();
            var gridCellSize = sourceGrid.cellSize;
            var cellWidth = Mathf.Max(MinimumCellSize, Mathf.Abs(gridCellSize.x));
            var cellHeight = Mathf.Max(MinimumCellSize, Mathf.Abs(gridCellSize.y));
            var cellDepth = Mathf.Max(
                MinimumOutOfBoundsGizmoDepth,
                Mathf.Min(cellWidth, cellHeight) * OutOfBoundsGizmoDepthRatio);
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            Gizmos.matrix = sourceGrid.transform.localToWorldMatrix;

            var validRect = GetGridLocalBakeRect(GetBakeBounds());
            var validCenter = new Vector3(validRect.center.x, validRect.center.y, -cellDepth);
            Gizmos.color = new Color(1f, 0.68f, 0.12f, 0.95f);
            Gizmos.DrawWireCube(
                validCenter,
                new Vector3(validRect.width, validRect.height, cellDepth));

            var warningSize = new Vector3(
                cellWidth * OutOfBoundsGizmoFillRatio,
                cellHeight * OutOfBoundsGizmoFillRatio,
                cellDepth);
            for (var i = 0; i < outOfBoundsTilePositions.Count; i++)
            {
                var cell = outOfBoundsTilePositions[i];
                var cellCenter = sourceGrid.CellToLocalInterpolated(
                    new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z));
                cellCenter.z -= cellDepth;
                Gizmos.color = new Color(1f, 0.22f, 0.08f, 0.28f);
                Gizmos.DrawCube(cellCenter, warningSize);
                Gizmos.color = new Color(1f, 0.35f, 0.08f, 0.95f);
                Gizmos.DrawWireCube(cellCenter, warningSize);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
#endif

        /// <summary>
        /// 选中 Surface 时绘制本地 XZ 边界和本地 Y 法线，辅助对齐墙面与斜面。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            GetSurfaceRect(out var minimumX, out var maximumX, out var minimumZ, out var maximumZ);
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.9f);
            var center = new Vector3(
                (minimumX + maximumX) * 0.5f,
                0f,
                (minimumZ + maximumZ) * 0.5f);
            var size = new Vector3(maximumX - minimumX, 0f, maximumZ - minimumZ);
            Gizmos.DrawWireCube(center, size);
            var normalLength = Mathf.Max(0.25f, cellSize * 0.5f);
            Gizmos.DrawLine(center, center + Vector3.up * normalLength);
            Gizmos.DrawSphere(center + Vector3.up * normalLength, normalLength * 0.08f);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }

        /// <summary>
        /// Tile Palette 或运行时 SetTile 改动当前源 Grid 时刷新材质索引，不改变固定区域。
        /// </summary>
        private void HandleTilemapChanged(Tilemap changedTilemap, Tilemap.SyncTile[] changedTiles)
        {
            if (sourceGrid == null || changedTilemap == null
                || !changedTilemap.transform.IsChildOf(sourceGrid.transform))
            {
                return;
            }

            InvalidateOutOfBoundsTileCache();
            // NativeTilemap: TilemapRenderer が visual updates natively；
            // 只有检测到 Tile 尺寸变化时才需要重建（Grid Scale 需要同步）。
            if (SynchronizeSourceTileSizeFromChangedTiles(changedTiles))
            {
                RequestRebuild();
            }
        }

        /// <summary>
        /// 执行一次不允许重入的原生图层、可选 Mesh、材质和碰撞体更新。
        /// </summary>
        private void RebuildInternal()
        {
            ApplySourceGridTransform();
            ApplyLayerRendererSettings();
            ApplySourcePreviewVisibility();
            if (surfaceMode == TileMap3DSurfaceMode.Overlay)
            {
                DisableOwnedGeneratedGeometry();
                return;
            }

            EnsureGeneratedGeometryComponents(
                out var meshFilter,
                out var meshRenderer,
                out var boxCollider);
            GetSurfaceRect(out var minimumX, out var maximumX, out var minimumZ, out var maximumZ);

            var vertices = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var topTriangles = new List<int>(6);
            var sideTriangles = new List<int>(30);
            AddTopQuad(vertices, uv, topTriangles, minimumX, maximumX, minimumZ, maximumZ);
            AddClosedSides(vertices, uv, sideTriangles, minimumX, maximumX, minimumZ, maximumZ);

            EnsureGeneratedMesh();
            generatedMesh.Clear();
            generatedMesh.indexFormat = vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            generatedMesh.SetVertices(vertices);
            generatedMesh.SetUVs(0, uv);
            generatedMesh.subMeshCount = 2;
            generatedMesh.SetTriangles(topTriangles, 0, false);
            generatedMesh.SetTriangles(sideTriangles, 1, false);
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateTangents();
            generatedMesh.RecalculateBounds();

            var topMaterial = groundMaterial != null ? groundMaterial : GetPlaceholderMaterial();
            meshFilter.sharedMesh = generatedMesh;
            meshRenderer.sharedMaterials = new[]
            {
                topMaterial,
                sideMaterial != null ? sideMaterial : GetDefaultSideMaterial()
            };
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;

            var width = Mathf.Max(MinimumSurfaceSize, maximumX - minimumX);
            var depth = Mathf.Max(MinimumSurfaceSize, maximumZ - minimumZ);
            boxCollider.center = new Vector3(
                (minimumX + maximumX) * 0.5f,
                -thickness * 0.5f,
                (minimumZ + maximumZ) * 0.5f);
            boxCollider.size = new Vector3(width, thickness, depth);
            boxCollider.enabled = true;
        }

        /// <summary>
        /// 为 GeneratedGround 补齐必需组件；Overlay 模式不会修改目标 3D 物体的组件。
        /// </summary>
        private void EnsureGeneratedGeometryComponents(
            out MeshFilter meshFilter,
            out MeshRenderer meshRenderer,
            out BoxCollider boxCollider)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            ownsGeneratedGeometry = true;
            meshRenderer.enabled = true;
            boxCollider.enabled = true;
        }

        /// <summary>
        /// 切换到 Overlay 时仅停用由 TileMap3D 自己生成的几何，不触碰外部目标物体。
        /// </summary>
        private void DisableOwnedGeneratedGeometry()
        {
            if (!ownsGeneratedGeometry)
            {
                return;
            }

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }

            var boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.enabled = false;
            }
        }

        /// <summary>
        /// 为旧图层补齐 TileMap3DLayer，并按 Hierarchy 顺序应用材质与法线偏移。
        /// </summary>
        private void ApplyLayerRendererSettings()
        {
            var tilemaps = GetSourceTilemaps(true);
            for (var i = 0; i < tilemaps.Length; i++)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null)
                {
                    continue;
                }

                var layer = tilemap.GetComponent<TileMap3DLayer>();
                if (layer == null)
                {
                    layer = tilemap.gameObject.AddComponent<TileMap3DLayer>();
                    if (i == 0)
                    {
                        layer.Configure(TileMap3DLayerType.Base);
                    }
                }

                layer.ApplyRendererSettings(i, layerSpacing);
            }
        }

        /// <summary>
        /// 以组件原点为地面顶面的水平中心，计算固定尺寸的本地 XZ 范围。
        /// </summary>
        private void GetSurfaceRect(
            out float minimumX,
            out float maximumX,
            out float minimumZ,
            out float maximumZ)
        {
            var groundSize = GroundSize;
            var halfWidth = Mathf.Max(MinimumSurfaceSize, groundSize.x) * 0.5f;
            var halfDepth = Mathf.Max(MinimumSurfaceSize, groundSize.y) * 0.5f;
            minimumX = -halfWidth;
            maximumX = halfWidth;
            minimumZ = -halfDepth;
            maximumZ = halfDepth;
        }

        /// <summary>
        /// 按 Mesh、3D Collider、Renderer 的优先级取得目标范围及其世界变换。
        /// </summary>
        private static bool TryGetTargetBounds(
            Transform target,
            out Bounds targetBounds,
            out Matrix4x4 boundsToWorld)
        {
            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                targetBounds = meshFilter.sharedMesh.bounds;
                boundsToWorld = target.localToWorldMatrix;
                return true;
            }

            var boxCollider = target.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                targetBounds = new Bounds(boxCollider.center, boxCollider.size);
                boundsToWorld = target.localToWorldMatrix;
                return true;
            }

            var meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                targetBounds = meshCollider.sharedMesh.bounds;
                boundsToWorld = target.localToWorldMatrix;
                return true;
            }

            var targetRenderer = target.GetComponent<Renderer>();
            if (targetRenderer != null)
            {
                targetBounds = targetRenderer.bounds;
                boundsToWorld = Matrix4x4.identity;
                return true;
            }

            targetBounds = default;
            boundsToWorld = Matrix4x4.identity;
            return false;
        }

        /// <summary>
        /// 将目标平面长度向上取整到完整格，并过滤矩阵运算产生的微小浮点误差。
        /// </summary>
        private int GetRequiredCellCount(float length)
        {
            var requiredCells = length / Mathf.Max(MinimumCellSize, cellSize);
            return Mathf.Max(1, Mathf.CeilToInt(requiredCells - GridCenterTolerance));
        }

        /// <summary>
        /// 检查 Bounds 投影结果是否为可用于布局计算的有限数值。
        /// </summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        /// <summary>
        /// 追加地面顶面四边形，使用完整 0-1 UV 承载烘焙贴图。
        /// </summary>
        private static void AddTopQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ)
        {
            var start = vertices.Count;
            vertices.Add(new Vector3(minimumX, 0f, minimumZ));
            vertices.Add(new Vector3(maximumX, 0f, minimumZ));
            vertices.Add(new Vector3(minimumX, 0f, maximumZ));
            vertices.Add(new Vector3(maximumX, 0f, maximumZ));
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 1f));
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            triangles.Add(start + 1);
        }

        /// <summary>
        /// 追加四面侧壁和底面，使地面成为具有实际厚度的封闭模型。
        /// </summary>
        private void AddClosedSides(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ)
        {
            var bottom = -thickness;
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, 0f, minimumZ),
                new Vector3(maximumX, 0f, minimumZ),
                new Vector3(minimumX, bottom, minimumZ),
                new Vector3(maximumX, bottom, minimumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, 0f, maximumZ),
                new Vector3(minimumX, bottom, maximumZ),
                new Vector3(maximumX, 0f, maximumZ),
                new Vector3(maximumX, bottom, maximumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, 0f, minimumZ),
                new Vector3(minimumX, bottom, minimumZ),
                new Vector3(minimumX, 0f, maximumZ),
                new Vector3(minimumX, bottom, maximumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(maximumX, 0f, minimumZ),
                new Vector3(maximumX, 0f, maximumZ),
                new Vector3(maximumX, bottom, minimumZ),
                new Vector3(maximumX, bottom, maximumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, bottom, minimumZ),
                new Vector3(maximumX, bottom, minimumZ),
                new Vector3(minimumX, bottom, maximumZ),
                new Vector3(maximumX, bottom, maximumZ));
        }

        /// <summary>
        /// 按指定绕序追加一个侧壁或底面四边形。
        /// </summary>
        private static void AddEdgeQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth)
        {
            var start = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            vertices.Add(fourth);
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 1f));
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        /// <summary>
        /// 创建一次并复用当前组件的临时 Mesh，避免编辑器反复分配。
        /// </summary>
        private void EnsureGeneratedMesh()
        {
            if (generatedMesh != null)
            {
                return;
            }

            generatedMesh = new Mesh
            {
                name = "TileMap3D_GeneratedSurface",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        /// <summary>
        /// 在尚未烘焙时创建轻量占位材质，帮助用户确认 3D 地面范围。
        /// </summary>
        private Material GetPlaceholderMaterial()
        {
            if (placeholderMaterial != null)
            {
                placeholderMaterial.SetColor(BaseColorId, surfaceColor);
                return placeholderMaterial;
            }

            var shader = Shader.Find(BakedSurfaceShaderName);
            if (shader == null)
            {
                return null;
            }

            placeholderMaterial = new Material(shader)
            {
                name = "TileMap3D_Placeholder",
                hideFlags = HideFlags.HideAndDontSave
            };
            placeholderMaterial.SetTexture(BaseMapId, Texture2D.whiteTexture);
            placeholderMaterial.SetColor(BaseColorId, surfaceColor);
            return placeholderMaterial;
        }

        /// <summary>
        /// 未指定侧壁材质时创建与地面底色协调的纯色临时材质。
        /// </summary>
        private Material GetDefaultSideMaterial()
        {
            var sideColor = new Color(
                surfaceColor.r * 0.55f,
                surfaceColor.g * 0.55f,
                surfaceColor.b * 0.55f,
                1f);
            if (defaultSideMaterial != null)
            {
                defaultSideMaterial.SetColor(BaseColorId, sideColor);
                return defaultSideMaterial;
            }

            var shader = Shader.Find(BakedSurfaceShaderName);
            if (shader == null)
            {
                return null;
            }

            defaultSideMaterial = new Material(shader)
            {
                name = "TileMap3D_DefaultSide",
                hideFlags = HideFlags.HideAndDontSave
            };
            defaultSideMaterial.SetTexture(BaseMapId, Texture2D.whiteTexture);
            defaultSideMaterial.SetColor(BaseColorId, sideColor);
            return defaultSideMaterial;
        }

        /// <summary>
        /// 对所有源 TilemapRenderer 应用统一预览开关，同时保留用户的图层启用设置。
        /// </summary>
        private void ApplySourcePreviewVisibility()
        {
            var tilemaps = GetSourceTilemaps(true);
            for (var i = 0; i < tilemaps.Length; i++)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null)
                {
                    continue;
                }

                var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
                if (tilemapRenderer != null)
                {
                    tilemapRenderer.forceRenderingOff = !showSourcePreview;
                }
            }
        }

        /// <summary>
        /// 约束序列化参数，防止零尺寸 Mesh 和非法区域。
        /// </summary>
        private void EnsureSerializedValues()
        {
            UpgradeLegacyLayout();
            bakeBounds = NormalizeBounds(bakeBounds);
            cellSize = Mathf.Max(MinimumCellSize, cellSize);
            automaticBounds = false;
            sizeMode = TileMap3DSizeMode.TilemapRegion;
            customSize.x = Mathf.Max(MinimumSurfaceSize, customSize.x);
            customSize.y = Mathf.Max(MinimumSurfaceSize, customSize.y);
            surfaceOffset = Mathf.Max(MinimumSurfaceOffset, surfaceOffset);
            layerSpacing = Mathf.Max(MinimumLayerSpacing, layerSpacing);
            thickness = Mathf.Max(MinimumThickness, thickness);
        }

        /// <summary>
        /// 首次加载旧版地面时保留尺寸和烘焙显示，避免新原生模式改变已有场景。
        /// </summary>
        private void UpgradeLegacyLayout()
        {
            if (layoutVersion >= CurrentLayoutVersion)
            {
                return;
            }

            if (layoutVersion < 1)
            {
                if (automaticBounds && TryGetOccupiedBounds(out var occupiedBounds))
                {
                    bakeBounds = occupiedBounds;
                }

                cellSize = GetCurrentWorldCellSize();
                automaticBounds = false;
                sizeMode = TileMap3DSizeMode.TilemapRegion;
            }

            if (layoutVersion < 2)
            {
                surfaceMode = TileMap3DSurfaceMode.GeneratedGround;
                surfaceOffset = 0f;
                layerSpacing = Mathf.Max(MinimumLayerSpacing, layerSpacing);
            }

            if (layoutVersion < 3 && surfaceMode == TileMap3DSurfaceMode.Overlay)
            {
                showSourcePreview = true;
            }

            if (layoutVersion < 5)
            {
                // Generated Ground 与 Overlay 默认共享同一世界格网相位，旧地面升级后也立即恢复该契约。
                keepWorldGridAligned = surfaceMode == TileMap3DSurfaceMode.GeneratedGround;
            }

            if (layoutVersion < 6)
            {
                // BakedTexture/SurfaceMaterial 旧场景：强制显示原生 Tilemap，归零表面偏移。
                showSourcePreview = true;
                surfaceOffset = 0f;
            }

            layoutVersion = CurrentLayoutVersion;
        }

        /// <summary>
        /// 合并全部有效 Tilemap 的已占用区域，仅供旧版自动区域迁移使用。
        /// </summary>
        private bool TryGetOccupiedBounds(out BoundsInt occupiedBounds)
        {
            var tilemaps = GetSourceTilemaps(true);
            var hasOccupiedBounds = false;
            occupiedBounds = default;
            for (var i = 0; i < tilemaps.Length; i++)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null || !tilemap.gameObject.activeInHierarchy || tilemap.GetUsedTilesCount() == 0)
                {
                    continue;
                }

                var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
                if (tilemapRenderer == null || !tilemapRenderer.enabled)
                {
                    continue;
                }

                var current = tilemap.cellBounds;
                current.zMin = 0;
                current.zMax = 1;
                if (!hasOccupiedBounds)
                {
                    occupiedBounds = current;
                    hasOccupiedBounds = true;
                    continue;
                }

                occupiedBounds.xMin = Mathf.Min(occupiedBounds.xMin, current.xMin);
                occupiedBounds.yMin = Mathf.Min(occupiedBounds.yMin, current.yMin);
                occupiedBounds.xMax = Mathf.Max(occupiedBounds.xMax, current.xMax);
                occupiedBounds.yMax = Mathf.Max(occupiedBounds.yMax, current.yMax);
            }

            if (hasOccupiedBounds)
            {
                occupiedBounds = NormalizeBounds(occupiedBounds);
            }

            return hasOccupiedBounds;
        }

        /// <summary>
        /// 从升级前的 Grid Cell Size 与 Transform Scale 推导当前世界单格尺寸。
        /// </summary>
        private float GetCurrentWorldCellSize()
        {
            if (sourceGrid == null)
            {
                return Mathf.Max(MinimumCellSize, cellSize);
            }

            var width = Mathf.Abs(sourceGrid.cellSize.x * sourceGrid.transform.localScale.x);
            if (width >= MinimumCellSize)
            {
                return width;
            }

            var depth = Mathf.Abs(sourceGrid.cellSize.y * sourceGrid.transform.localScale.y);
            return Mathf.Max(MinimumCellSize, depth);
        }

        /// <summary>
        /// 仅在本次 Tile 变化包含不同原始尺寸时扫描整张源地图，正常绘制不产生额外遍历。
        /// </summary>
        /// <returns>true 当检测到 Tile 尺寸变化并已执行全图扫描时。</returns>
        private bool SynchronizeSourceTileSizeFromChangedTiles(Tilemap.SyncTile[] changedTiles)
        {
            if (changedTiles == null || changedTiles.Length == 0)
            {
                return false;
            }

            var current = GetSourceGridCellSize();
            var foundSprite = false;
            var requiresFullScan = hasMixedSourceTileSizes;
            for (var i = 0; i < changedTiles.Length; i++)
            {
                if (!TryGetSourceTileSize(changedTiles[i].tileData.sprite, out var changedSize))
                {
                    continue;
                }

                foundSprite = true;
                if (!AreSourceTileSizesEqual(current, changedSize))
                {
                    requiresFullScan = true;
                    break;
                }
            }

            if (requiresFullScan || !foundSprite && hasMixedSourceTileSizes)
            {
                SynchronizeSourceTileSizeFromExistingTiles();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 从全部已绘制 Sprite 推导统一源单格尺寸；混用尺寸时保留当前有效尺寸并给出警告。
        /// </summary>
        private void SynchronizeSourceTileSizeFromExistingTiles()
        {
            if (sourceGrid == null)
            {
                hasMixedSourceTileSizes = false;
                sourceTileSizeWarning = string.Empty;
                return;
            }

            if (!TryDetectExistingSourceTileSizes(
                    out var firstSize,
                    out var differentSize,
                    out var currentSizePresent))
            {
                hasMixedSourceTileSizes = false;
                sourceTileSizeWarning = string.Empty;
                return;
            }

            var hasDifferentSize = differentSize.x >= MinimumCellSize
                && differentSize.y >= MinimumCellSize;
            hasMixedSourceTileSizes = hasDifferentSize;
            if (!hasDifferentSize)
            {
                sourceTileSizeWarning = string.Empty;
                SetSourceGridCellSize(firstSize);
                return;
            }

            if (!currentSizePresent)
            {
                SetSourceGridCellSize(firstSize);
            }

            var current = GetSourceGridCellSize();
            var conflictingSize = AreSourceTileSizesEqual(current, firstSize)
                ? differentSize
                : firstSize;
            ReportSourceTileSizeWarning(current, conflictingSize, true);
        }

        /// <summary>
        /// 枚举非空 Tile 位置并读取 Unity 已解析的最终 Sprite 尺寸。
        /// </summary>
        private bool TryDetectExistingSourceTileSizes(
            out Vector2 firstSize,
            out Vector2 differentSize,
            out bool currentSizePresent)
        {
            firstSize = Vector2.zero;
            differentSize = Vector2.zero;
            currentSizePresent = false;
            var foundSize = false;
            var current = GetSourceGridCellSize();
            var tilemaps = GetSourceTilemaps(true);
            for (var tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                var tilemap = tilemaps[tilemapIndex];
                if (tilemap == null || tilemap.GetUsedTilesCount() == 0)
                {
                    continue;
                }

                var bounds = tilemap.cellBounds;
                if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                {
                    continue;
                }

                var end = bounds.max - Vector3Int.one;
                var tileCount = tilemap.GetTilesRangeCount(bounds.min, end);
                if (tileCount <= 0)
                {
                    continue;
                }

                var positions = new Vector3Int[tileCount];
                var tiles = new TileBase[tileCount];
                var actualCount = tilemap.GetTilesRangeNonAlloc(bounds.min, end, positions, tiles);
                for (var tileIndex = 0; tileIndex < actualCount; tileIndex++)
                {
                    if (!TryGetSourceTileSize(tilemap.GetSprite(positions[tileIndex]), out var tileSize))
                    {
                        continue;
                    }

                    currentSizePresent |= AreSourceTileSizesEqual(current, tileSize);
                    if (!foundSize)
                    {
                        firstSize = tileSize;
                        foundSize = true;
                    }
                    else if (differentSize == Vector2.zero
                        && !AreSourceTileSizesEqual(firstSize, tileSize))
                    {
                        differentSize = tileSize;
                    }
                }
            }

            return foundSize;
        }

        /// <summary>
        /// 判断源 Grid 下是否已有任意 Tile 数据，避免切换 Brush 时破坏现有地图比例。
        /// </summary>
        private bool HasSourceTiles()
        {
            var tilemaps = GetSourceTilemaps(true);
            for (var i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] != null && tilemaps[i].GetUsedTilesCount() > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 写入源 Grid 的逻辑 Cell Size，并保持 TileMap3D 最终世界单格尺寸不变。
        /// </summary>
        private bool SetSourceGridCellSize(Vector2 nativeTileSize)
        {
            if (sourceGrid == null)
            {
                return false;
            }

            var normalized = NormalizeSourceTileSize(nativeTileSize);
            var current = GetSourceGridCellSize();
            if (AreSourceTileSizesEqual(current, normalized))
            {
                return false;
            }

            var gridCellSize = sourceGrid.cellSize;
            sourceGrid.cellSize = new Vector3(normalized.x, normalized.y, gridCellSize.z);
            ApplySourceGridTransform();
            return true;
        }

        /// <summary>
        /// 返回源 Grid 当前使用的逻辑单格尺寸；最终世界尺寸仍由 TileMap3D Cell Size 控制。
        /// </summary>
        private Vector2 GetSourceGridCellSize()
        {
            return sourceGrid != null
                ? NormalizeSourceTileSize(new Vector2(sourceGrid.cellSize.x, sourceGrid.cellSize.y))
                : Vector2.one;
        }

        /// <summary>
        /// 从 Sprite Rect 与 PPU 对应的 Bounds 中取得原始 Tile 尺寸。
        /// </summary>
        private static bool TryGetSourceTileSize(Sprite sprite, out Vector2 tileSize)
        {
            tileSize = Vector2.zero;
            if (sprite == null)
            {
                return false;
            }

            var boundsSize = sprite.bounds.size;
            if (Mathf.Abs(boundsSize.x) < MinimumCellSize || Mathf.Abs(boundsSize.y) < MinimumCellSize)
            {
                return false;
            }

            tileSize = NormalizeSourceTileSize(new Vector2(boundsSize.x, boundsSize.y));
            return true;
        }

        /// <summary>
        /// 记录源 Tile 尺寸冲突；相同告警只输出一次，避免连续绘制刷屏。
        /// </summary>
        private void ReportSourceTileSizeWarning(Vector2 current, Vector2 detected, bool mixedTiles)
        {
            var reason = mixedTiles ? "当前地图混用了不同尺寸的 Tile" : "当前 Brush 与已有 Tile 尺寸不同";
            var warning = "TileMap3D：" + reason + "。源 Grid 使用 "
                + FormatSourceTileSize(current) + "，检测到 " + FormatSourceTileSize(detected)
                + "。同一 TileMap3D 地面应使用统一的 Sprite 原始尺寸。";
            if (sourceTileSizeWarning == warning)
            {
                return;
            }

            sourceTileSizeWarning = warning;
            Debug.LogWarning(warning, this);
        }

        /// <summary>
        /// 约束源 Tile 原始尺寸为可用于 Grid 的正数。
        /// </summary>
        private static Vector2 NormalizeSourceTileSize(Vector2 value)
        {
            return new Vector2(
                Mathf.Max(MinimumCellSize, Mathf.Abs(value.x)),
                Mathf.Max(MinimumCellSize, Mathf.Abs(value.y)));
        }

        /// <summary>
        /// 使用稳定容差比较两个源 Tile 尺寸，避免浮点导入误差反复触发同步。
        /// </summary>
        private static bool AreSourceTileSizesEqual(Vector2 first, Vector2 second)
        {
            return Mathf.Abs(first.x - second.x) <= GridCenterTolerance
                && Mathf.Abs(first.y - second.y) <= GridCenterTolerance;
        }

        /// <summary>
        /// 生成人类可读的源 Tile 尺寸文本，供 Console 与工作台提示共用。
        /// </summary>
        private static string FormatSourceTileSize(Vector2 value)
        {
            return value.x.ToString("0.###") + " × " + value.y.ToString("0.###");
        }

        /// <summary>
        /// 缩放并平移源 Grid，使逻辑单格匹配 Surface、中心对齐并保持法线偏移。
        /// </summary>
        private void ApplySourceGridTransform()
        {
            if (sourceGrid == null)
            {
                return;
            }

            if (sourceGrid.transform.IsChildOf(transform))
            {
                var localPosition = sourceGrid.transform.localPosition;
                localPosition.y = surfaceOffset;
                if (sourceGrid.transform.localPosition != localPosition)
                {
                    sourceGrid.transform.localPosition = localPosition;
                }
            }

            var sourceWidth = Mathf.Max(MinimumCellSize, Mathf.Abs(sourceGrid.cellSize.x));
            var sourceDepth = Mathf.Max(MinimumCellSize, Mathf.Abs(sourceGrid.cellSize.y));
            var targetScale = new Vector3(cellSize / sourceWidth, cellSize / sourceDepth, 1f);
            if (sourceGrid.transform.localScale != targetScale)
            {
                sourceGrid.transform.localScale = targetScale;
            }

            var gridRect = GetGridLocalBakeRect(GetBakeBounds());
            var gridLocalCenter = new Vector3(gridRect.center.x, gridRect.center.y, 0f);
            var currentWorldCenter = sourceGrid.transform.TransformPoint(gridLocalCenter);
            var currentSurfaceCenter = transform.InverseTransformPoint(currentWorldCenter);
            var targetWorldCenter = transform.TransformPoint(
                new Vector3(0f, currentSurfaceCenter.y, 0f));
            var worldOffset = targetWorldCenter - currentWorldCenter;
            if (worldOffset.sqrMagnitude > GridCenterTolerance * GridCenterTolerance)
            {
                sourceGrid.transform.position += worldOffset;
            }
        }

        /// <summary>
        /// 把烘焙区域修正为至少一个 XY 单元格和固定一层 Z。
        /// </summary>
        private static BoundsInt NormalizeBounds(BoundsInt value)
        {
            value.size = new Vector3Int(
                Mathf.Max(1, value.size.x),
                Mathf.Max(1, value.size.y),
                1);
            value.zMin = 0;
            value.zMax = 1;
            return value;
        }

        /// <summary>
        /// 按运行模式安全释放临时 UnityEngine.Object。
        /// </summary>
        private static void ReleaseGeneratedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
