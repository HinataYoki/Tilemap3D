using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 提供相对父物体局部坐标的常用平面方向预设。
    /// </summary>
    internal enum TileMap3DPlanePreset
    {
        XZ,
        XY,
        YZ
    }

    /// <summary>
    /// 提供 TileMap3D 场景对象、原生 Grid 和 Tilemap 图层的标准创建入口。
    /// </summary>
    [InitializeOnLoad]
    internal static partial class TileMap3DCommands
    {
        private const int LayerSortingStep = 10;
        private static GameObject sCachedPaintTarget;
        private static TileMap3DSurface sCachedPaintSurface;

        /// <summary>
        /// 监听 Unity Tile Palette 的目标切换与绘制事件，确保烘焙后首次编辑仍有实时预览。
        /// </summary>
        static TileMap3DCommands()
        {
            GridPaintingState.scenePaintTargetChanged -= OnPaintTargetChanged;
            GridPaintingState.scenePaintTargetChanged += OnPaintTargetChanged;
            GridPaintingState.scenePaintTargetEdited -= OnPaintTargetChanged;
            GridPaintingState.scenePaintTargetEdited += OnPaintTargetChanged;
            GridPaintingState.brushChanged -= SynchronizePaintTargetFromBrush;
            GridPaintingState.brushChanged += SynchronizePaintTargetFromBrush;
            SceneView.duringSceneGui -= SynchronizePaintTargetFromSceneView;
            SceneView.duringSceneGui += SynchronizePaintTargetFromSceneView;
        }

        private static void OnPaintTargetChanged(GameObject target)
        {
            ShowSourcePreviewForPaintTarget(target);
            sCachedPaintTarget = null;
            sCachedPaintSurface = null;
        }

        /// <summary>
        /// 从 TileMap3D 菜单创建完整的地面结构。
        /// </summary>
        [MenuItem("TileMap3D/创建 3D Tilemap 地面", false, 141)]
        private static void CreateFromTileMap3DMenu()
        {
            CreateSurface(null);
        }

        /// <summary>
        /// 从 GameObject 菜单创建地面，并在存在上下文对象时挂到其下方。
        /// </summary>
        [MenuItem("GameObject/TileMap3D/创建 3D Tilemap 地面", false, 10)]
        private static void CreateFromGameObjectMenu(MenuCommand command)
        {
            CreateSurface(command.context as GameObject);
        }

        /// <summary>
        /// 从 TileMap3D 菜单创建不生成 Mesh 和 Collider 的原生平面覆盖 Surface。
        /// </summary>
        [MenuItem("TileMap3D/创建平面覆盖 Surface", false, 142)]
        private static void CreateOverlayFromTileMap3DMenu()
        {
            CreateOverlaySurface(Selection.activeGameObject);
        }

        /// <summary>
        /// 从 GameObject 菜单把平面覆盖 Surface 挂到当前上下文对象下方。
        /// </summary>
        [MenuItem("GameObject/TileMap3D/创建平面覆盖 Surface", false, 11)]
        private static void CreateOverlayFromGameObjectMenu(MenuCommand command)
        {
            CreateOverlaySurface(command.context as GameObject);
        }

        /// <summary>
        /// 创建带原生 Grid、基础 Tilemap、封闭 Mesh 和 BoxCollider 的场景对象。
        /// </summary>
        public static TileMap3DSurface CreateSurface(GameObject parent)
        {
            return CreateSurfaceInternal(
                parent,
                "TileMap3D Ground",
                TileMap3DSurfaceMode.GeneratedGround,
                "创建 TileMap3D 地面");
        }

        /// <summary>
        /// 创建可挂到任意 3D 物体下方的平面覆盖 Surface，不生成或修改目标几何和碰撞。
        /// </summary>
        public static TileMap3DSurface CreateOverlaySurface(GameObject parent)
        {
            return CreateSurfaceInternal(
                parent,
                "TileMap3D Surface",
                TileMap3DSurfaceMode.Overlay,
                "创建 TileMap3D 平面 Surface");
        }

        /// <summary>
        /// 为已有 TileMap3DSurface 创建嵌入式原生 Grid，并旋转到地面 XZ 平面。
        /// </summary>
        public static Grid CreateSourceGrid(TileMap3DSurface surface)
        {
            if (surface == null)
            {
                return null;
            }

            if (surface.SourceGrid != null)
            {
                return surface.SourceGrid;
            }

            var sourceObject = new GameObject(TileMap3DSurface.SourceGridObjectName);
            Undo.RegisterCreatedObjectUndo(sourceObject, "创建 TileMap3D 源 Grid");
            Undo.SetTransformParent(sourceObject.transform, surface.transform, "挂载 TileMap3D 源 Grid");
            sourceObject.transform.localPosition = new Vector3(0f, surface.SurfaceOffset, 0f);
            sourceObject.transform.localRotation = TileMap3DSurface.SourceGridLocalRotation;
            sourceObject.transform.localScale = Vector3.one;
            var grid = Undo.AddComponent<Grid>(sourceObject);
            grid.cellSize = Vector3.one;
            grid.cellGap = Vector3.zero;
            surface.SetSourceGrid(grid);
            EditorUtility.SetDirty(surface);
            return grid;
        }

        /// <summary>
        /// 在源 Grid 下新增一个真实 Tilemap 图层，并设置稳定的原生排序值。
        /// </summary>
        public static Tilemap AddLayer(TileMap3DSurface surface)
        {
            if (surface == null)
            {
                return null;
            }

            var grid = surface.SourceGrid != null ? surface.SourceGrid : CreateSourceGrid(surface);
            var existingLayers = surface.GetSourceTilemaps();
            var desiredName = existingLayers.Length == 0 ? "Base" : "Layer " + (existingLayers.Length + 1);
            var sortingOrder = existingLayers.Length == 0 ? 0 : GetNextSortingOrder(existingLayers);
            var layerType = existingLayers.Length == 0
                ? TileMap3DLayerType.Base
                : TileMap3DLayerType.Overlay;
            return AddLayer(surface, desiredName, sortingOrder, layerType);
        }

        /// <summary>
        /// 为已有 Tilemap 补齐图层元数据，并通过 Undo 记录用户可见的组件创建。
        /// </summary>
        public static TileMap3DLayer EnsureLayerComponent(
            Tilemap tilemap,
            TileMap3DLayerType defaultType)
        {
            if (tilemap == null)
            {
                return null;
            }

            var layer = tilemap.GetComponent<TileMap3DLayer>();
            if (layer == null)
            {
                layer = Undo.AddComponent<TileMap3DLayer>(tilemap.gameObject);
                layer.Configure(defaultType);
                EditorUtility.SetDirty(tilemap.gameObject);
            }

            return layer;
        }

        /// <summary>
        /// 将 Surface 根对象对齐到父物体局部 XZ、XY 或 YZ 平面，不改变格子数据。
        /// </summary>
        public static void AlignSurface(
            TileMap3DSurface surface,
            TileMap3DPlanePreset planePreset)
        {
            if (surface == null)
            {
                return;
            }

            Undo.RecordObject(surface, "对齐 TileMap3D 平面");
            Undo.RecordObject(surface.transform, "对齐 TileMap3D 平面");
            switch (planePreset)
            {
                case TileMap3DPlanePreset.XY:
                    surface.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    break;
                case TileMap3DPlanePreset.YZ:
                    surface.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.right);
                    break;
                default:
                    surface.transform.localRotation = Quaternion.identity;
                    break;
            }

            if (!surface.TryFitToTargetBounds())
            {
                surface.NormalizeWorldScale();
            }
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 让 Overlay Surface 自动匹配直接父物体的 Mesh 或 3D Collider 平面范围。
        /// </summary>
        public static bool FitSurfaceToParent(TileMap3DSurface surface)
        {
            if (surface == null || surface.SurfaceMode != TileMap3DSurfaceMode.Overlay)
            {
                return false;
            }

            Undo.RecordObject(surface, "适配 TileMap3D 父物体范围");
            Undo.RecordObject(surface.transform, "适配 TileMap3D 父物体范围");
            if (!surface.TryFitToTargetBounds())
            {
                return false;
            }

            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
            return true;
        }

        /// <summary>
        /// 抵消父级非等比缩放对 Surface 三个局部轴的长度影响，保持新 Tile 单格比例稳定。
        /// </summary>
        public static void NormalizeSurfaceWorldScale(TileMap3DSurface surface)
        {
            if (surface == null)
            {
                return;
            }

            Undo.RecordObject(surface.transform, "归一化 TileMap3D Surface 缩放");
            surface.NormalizeWorldScale();
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
        }

        /// <summary>
        /// 为 Generated Ground 启用持续世界格网吸附，并以可撤销方式修正当前相位。
        /// </summary>
        public static bool EnableWorldGridAlignment(TileMap3DSurface surface)
        {
            if (surface == null || surface.SurfaceMode != TileMap3DSurfaceMode.GeneratedGround)
            {
                return false;
            }

            Undo.RecordObject(surface, "启用 TileMap3D 世界格网对齐");
            Undo.RecordObject(surface.transform, "对齐 TileMap3D 世界格网");
            surface.EnableWorldGridAlignment();
            EditorUtility.SetDirty(surface);
            EditorUtility.SetDirty(surface.transform);
            SceneView.RepaintAll();
            return true;
        }

        /// <summary>
        /// 把指定 Tilemap 设为 Unity Tile Palette 的当前场景绘制目标。
        /// </summary>
        public static void SetPaintTarget(Tilemap tilemap, bool openPalette = true)
        {
            if (tilemap == null)
            {
                return;
            }

            ShowSourcePreviewForPaintTarget(tilemap.gameObject);
            Selection.activeGameObject = tilemap.gameObject;
            GridPaintingState.scenePaintTarget = tilemap.gameObject;
            if (openPalette && !EditorApplication.ExecuteMenuItem("Window/2D/Tile Palette"))
            {
                Debug.LogWarning("TileMap3D 无法打开 Unity Tile Palette，请从 Window > 2D > Tile Palette 手动打开。");
            }
        }

        /// <summary>
        /// 创建具名 Tilemap 图层，供首次 Base 图层和后续动态图层共用。
        /// </summary>
        private static Tilemap AddLayer(
            TileMap3DSurface surface,
            string desiredName,
            int sortingOrder,
            TileMap3DLayerType layerType)
        {
            var grid = surface.SourceGrid != null ? surface.SourceGrid : CreateSourceGrid(surface);
            if (grid == null)
            {
                return null;
            }

            var layerName = GameObjectUtility.GetUniqueNameForSibling(grid.transform, desiredName);
            var layerObject = new GameObject(layerName);
            Undo.RegisterCreatedObjectUndo(layerObject, "新增 TileMap3D Tilemap 图层");
            Undo.SetTransformParent(layerObject.transform, grid.transform, "挂载 TileMap3D Tilemap 图层");
            layerObject.transform.localPosition = Vector3.zero;
            layerObject.transform.localRotation = Quaternion.identity;
            layerObject.transform.localScale = Vector3.one;
            var tilemap = Undo.AddComponent<Tilemap>(layerObject);
            tilemap.color = Color.white;
            var tilemapRenderer = Undo.AddComponent<TilemapRenderer>(layerObject);
            tilemapRenderer.mode = TilemapRenderer.Mode.Chunk;
            tilemapRenderer.sortingOrder = sortingOrder;
            var layer = Undo.AddComponent<TileMap3DLayer>(layerObject);
            layer.Configure(layerType);
            surface.SetSourcePreviewVisible(true);
            surface.Rebuild();
            EditorUtility.SetDirty(surface);
            SetPaintTarget(tilemap, false);
            return tilemap;
        }

        /// <summary>
        /// 创建完整 Surface 层级，新对象先保持停用以避免配置前生成错误的承载组件。
        /// </summary>
        private static TileMap3DSurface CreateSurfaceInternal(
            GameObject parent,
            string objectName,
            TileMap3DSurfaceMode surfaceMode,
            string undoName)
        {
            var root = new GameObject(objectName);
            root.SetActive(false);
            Undo.RegisterCreatedObjectUndo(root, undoName);
            if (parent != null)
            {
                root.transform.SetPositionAndRotation(
                    parent.transform.position,
                    parent.transform.rotation);
                root.transform.localScale = Vector3.one;
                Undo.SetTransformParent(root.transform, parent.transform, undoName);
            }

            if (surfaceMode == TileMap3DSurfaceMode.GeneratedGround)
            {
                Undo.AddComponent<MeshFilter>(root);
                Undo.AddComponent<MeshRenderer>(root);
                Undo.AddComponent<BoxCollider>(root);
            }

            var surface = Undo.AddComponent<TileMap3DSurface>(root);
            surface.ConfigureForCreation(surfaceMode);
            if (surfaceMode != TileMap3DSurfaceMode.Overlay
                || !surface.TryFitToTargetBounds())
            {
                surface.NormalizeWorldScale();
            }
            var grid = CreateSourceGrid(surface);
            var baseLayer = AddLayer(surface, "Base", 0, TileMap3DLayerType.Base);
            surface.SetSourceGrid(grid);
            root.SetActive(true);
            surface.Rebuild();
            SetPaintTarget(baseLayer, false);
            Selection.activeGameObject = baseLayer.gameObject;
            EditorGUIUtility.PingObject(baseLayer.gameObject);
            TileMap3DWindow.Open(surface);
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            return surface;
        }

        /// <summary>
        /// 在现有 TilemapRenderer 排序之后分配下一个图层排序值。
        /// </summary>
        private static int GetNextSortingOrder(Tilemap[] tilemaps)
        {
            var maximum = 0;
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
                    maximum = Mathf.Max(maximum, tilemapRenderer.sortingOrder);
                }
            }

            return maximum + LayerSortingStep;
        }
    }
}
