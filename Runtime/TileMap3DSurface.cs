using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
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
    /// 将原生 Grid 与 Tilemap 的绘制结果映射到固定方向的 3D 平面。
    /// 按职责拆分为多个 partial 文件：Configuration / Geometry / Query / TileSize / Migration / Editor。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed partial class TileMap3DSurface : MonoBehaviour
    {
        private const string GroundSurfaceShaderName = "TileMap3D/GroundSurface";
        private const string GroundMaterialResourcePath = "TileMap3D/TileMap3DGround";
        private const int CurrentLayoutVersion = 7;
        private const float MinimumCellSize = 0.01f;
        private const float MinimumThickness = 0.01f;
        private const float MinimumSurfaceSize = 0.01f;
        private const float MinimumSurfaceOffset = 0f;
        private const float MinimumLayerSpacing = 0.0001f;
        private const float GridCenterTolerance = 0.0001f;
        private const float SideColorMultiplier = 0.55f;

        /// <summary>源 Grid 子对象的约定名称，创建工具与测试共用。</summary>
        public const string SourceGridObjectName = "Tilemap Source";

        /// <summary>源 Grid 相对 Surface 的约定旋转：把 Tilemap 的 XY 平面映射到 Surface 本地 XZ。</summary>
        public static readonly Quaternion SourceGridLocalRotation = Quaternion.Euler(90f, 0f, 0f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] private Grid sourceGrid;
        [SerializeField] private TileMap3DSurfaceProfile surfaceProfile;
        [SerializeField] private Tilemap surfaceQueryLayer;
        [SerializeField] private TileMap3DSurfaceMode surfaceMode = TileMap3DSurfaceMode.Overlay;
        // 仅作为 layoutVersion < 1 旧场景迁移的输入，运行期不再具有语义。
        [SerializeField, HideInInspector] private bool automaticBounds;
        [SerializeField, FormerlySerializedAs("bakeBounds")]
        private BoundsInt surfaceBounds = new BoundsInt(0, 0, 0, 8, 8, 1);
        [SerializeField, Min(MinimumCellSize)] private float cellSize = 1f;
        [SerializeField, HideInInspector] private bool keepWorldGridAligned;
        [SerializeField, HideInInspector] private int layoutVersion = CurrentLayoutVersion;
        [SerializeField, Min(MinimumSurfaceOffset)] private float surfaceOffset = 0f;
        [SerializeField, Min(MinimumLayerSpacing)] private float layerSpacing = 0.002f;
        [SerializeField, Min(MinimumThickness)] private float thickness = 0.5f;
        [SerializeField] private Color surfaceColor = Color.white;
        [SerializeField] private Material groundMaterial;
        [SerializeField] private Material sideMaterial;
        [SerializeField] private bool showSourcePreview = true;
        [SerializeField] private bool showOutOfBoundsTilePreview;
        [SerializeField, HideInInspector] private bool ownsGeneratedGeometry;

        [NonSerialized] private Mesh generatedMesh;
        [NonSerialized] private Material fallbackGroundMaterial;
        [NonSerialized] private MaterialPropertyBlock groundPropertyBlock;
        [NonSerialized] private bool groundShaderMissingWarned;
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

        public Grid SourceGrid => sourceGrid;
        public TileMap3DSurfaceProfile SurfaceProfile => surfaceProfile;
        public Tilemap SurfaceQueryLayer => surfaceQueryLayer;
        public TileMap3DSurfaceMode SurfaceMode => surfaceMode;
        public int Columns => GetSurfaceBounds().size.x;
        public int Rows => GetSurfaceBounds().size.y;
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
        /// 生成或刷新顶面、封闭侧壁、底面和 BoxCollider。
        /// </summary>
        public void Rebuild()
        {
            if (isRebuilding)
            {
                return;
            }

            InvalidateSourceTilemaps();
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

            RebuildGeneratedGeometry();
        }

        /// <summary>
        /// 组件启用时恢复非序列化 Mesh、材质和源图层可见性。
        /// </summary>
        private void OnEnable()
        {
            Tilemap.tilemapTileChanged -= HandleTilemapChanged;
            Tilemap.tilemapTileChanged += HandleTilemapChanged;
#if UNITY_EDITOR
            UnityEditor.Undo.undoRedoPerformed -= HandleUndoRedo;
            UnityEditor.Undo.undoRedoPerformed += HandleUndoRedo;
#endif
            InvalidateSourceTilemaps();
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
            UnityEditor.Undo.undoRedoPerformed -= HandleUndoRedo;
            CancelDelayedEditorRebuild();
            worldGridTransformInitialized = false;
#endif
            rebuildRequested = false;
            synchronizeSourceTileSizeOnRebuild = false;
        }

        /// <summary>
        /// 每帧仅同步编辑器格网吸附状态并消费待处理的重建请求。
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
        /// Surface 直接子物体变化（如源 Grid 挂载或移除）时刷新缓存与 3D 表面。
        /// 注意：Tilemap 图层属于 Grid 的子物体，其增删由 TileMap3DLayer 的生命周期回调负责失效缓存。
        /// </summary>
        private void OnTransformChildrenChanged()
        {
            InvalidateSourceTilemaps();
            RequestRebuild(true);
        }

        /// <summary>
        /// 组件销毁时释放仅供当前场景实例使用的临时资源。
        /// </summary>
        private void OnDestroy()
        {
            Tilemap.tilemapTileChanged -= HandleTilemapChanged;
#if UNITY_EDITOR
            UnityEditor.Undo.undoRedoPerformed -= HandleUndoRedo;
            CancelDelayedEditorRebuild();
#endif
            ReleaseGeneratedObject(generatedMesh);
            ReleaseGeneratedObject(fallbackGroundMaterial);
            generatedMesh = null;
            fallbackGroundMaterial = null;
        }

        /// <summary>
        /// Tile Palette 或运行时 SetTile 改动当前源 Grid 时刷新缓存，不改变固定区域。
        /// </summary>
        private void HandleTilemapChanged(Tilemap changedTilemap, Tilemap.SyncTile[] changedTiles)
        {
            if (sourceGrid == null || changedTilemap == null
                || !changedTilemap.transform.IsChildOf(sourceGrid.transform))
            {
                return;
            }

            InvalidateOutOfBoundsTileCache();
            // 渲染更新由原生 TilemapRenderer 自行完成；
            // 只有检测到 Tile 原始尺寸变化时才需要重建（同步 Grid Scale）。
            if (SynchronizeSourceTileSizeFromChangedTiles(changedTiles))
            {
                RequestRebuild();
            }
        }

        /// <summary>
        /// 为旧图层补齐 TileMap3DLayer，并按 Hierarchy 顺序应用材质与法线偏移。
        /// </summary>
        private void ApplyLayerRendererSettings()
        {
            var tilemaps = GetSourceTilemaps();
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
        /// 对所有源 TilemapRenderer 应用统一预览开关，同时保留用户的图层启用设置。
        /// </summary>
        private void ApplySourcePreviewVisibility()
        {
            var tilemaps = GetSourceTilemaps();
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
            surfaceBounds = NormalizeBounds(surfaceBounds);
            cellSize = Mathf.Max(MinimumCellSize, cellSize);
            surfaceOffset = Mathf.Max(MinimumSurfaceOffset, surfaceOffset);
            layerSpacing = Mathf.Max(MinimumLayerSpacing, layerSpacing);
            thickness = Mathf.Max(MinimumThickness, thickness);
        }
    }
}
