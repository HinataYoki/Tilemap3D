using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using static YokiFrame.Unity.TileMap3D.TileMap3DEditorUI;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3D 工作台，编排任意平面 Surface、原生 Tilemap 图层和可选烘焙操作。
    /// </summary>
    public sealed partial class TileMap3DWindow : EditorWindow
    {
        private const string WindowTitle = "TileMap3D";
        private const float SectionSpacing = 8f;
        private static readonly List<string> LayerRenderTypeChoices = new List<string>
        {
            "Base（写入深度）",
            "Overlay（透明叠加）"
        };

        [SerializeField] private TileMap3DSurface surface;
        [SerializeField] private Tilemap activeTilemap;

        private ObjectField surfaceField;
        private Label statusLabel;
        private Label outOfBoundsStatusLabel;
        private Button clearOutOfBoundsButton;
        private VisualElement content;
        private bool refreshQueued;
        private SerializedObject cachedSerializedSurface;

        /// <summary>
        /// 打开工作台，并优先绑定当前选中的 TileMap3D 地面或其 Tilemap 子对象。
        /// </summary>
        [MenuItem("TileMap3D/工作台", false, 140)]
        public static void Open()
        {
            var window = GetWindow<TileMap3DWindow>(WindowTitle);
            window.minSize = new Vector2(460f, 520f);
            window.Show();
            window.BindSurface(FindSurfaceFromSelection());
        }

        /// <summary>
        /// 打开工作台并绑定指定地面组件。
        /// </summary>
        public static void Open(TileMap3DSurface targetSurface)
        {
            var window = GetWindow<TileMap3DWindow>(WindowTitle);
            window.minSize = new Vector2(460f, 520f);
            window.Show();
            window.BindSurface(targetSurface);
        }

        /// <summary>
        /// 创建响应窄窗口的 UI Toolkit 页面，并监听 Hierarchy 结构变化。
        /// </summary>
        private void CreateGUI()
        {
            EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
            EditorApplication.hierarchyChanged += HandleHierarchyChanged;
            if (surface == null)
            {
                surface = FindSurfaceFromSelection();
            }

            RefreshPage();
        }

        /// <summary>
        /// 窗口关闭时解除编辑器事件，避免保留失效窗口引用。
        /// </summary>
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
        }

        /// <summary>
        /// 定期更新越界 Tile 数量；Surface 内部仅在数据变化后重新枚举，避免每次界面重绘扫描大地图。
        /// </summary>
        private void OnInspectorUpdate()
        {
            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 选择地面或其 Tilemap 子对象时自动切换工作台上下文。
        /// </summary>
        private void OnSelectionChange()
        {
            var selectedSurface = FindSurfaceFromSelection();
            if (selectedSurface != null && selectedSurface != surface)
            {
                BindSurface(selectedSurface);
                return;
            }

            var selectedObject = Selection.activeGameObject;
            var selectedTilemap = selectedObject != null ? selectedObject.GetComponent<Tilemap>() : null;
            if (selectedTilemap != null && IsOwnedTilemap(selectedTilemap))
            {
                activeTilemap = selectedTilemap;
                ScheduleRefresh();
            }
        }

        /// <summary>
        /// Hierarchy 中图层增删或改名后延迟刷新动态列表。
        /// </summary>
        private void HandleHierarchyChanged()
        {
            ScheduleRefresh();
        }

        /// <summary>
        /// 绑定地面并为当前编辑图层选择一个有效的原生 Tilemap。
        /// </summary>
        private void BindSurface(TileMap3DSurface targetSurface)
        {
            surface = targetSurface;
            cachedSerializedSurface = surface != null ? new SerializedObject(surface) : null;
            if (!IsOwnedTilemap(activeTilemap))
            {
                activeTilemap = FindFirstTilemap();
            }

            RefreshPage();
        }

        /// <summary>
        /// 刷新底部状态栏中的源图层、区域和渲染状态。
        /// </summary>
        private void RefreshStatus()
        {
            if (statusLabel == null)
            {
                return;
            }

            if (surface == null)
            {
                statusLabel.text = "未绑定 TileMap3D 地面";
                return;
            }

            if (surface.SourceGrid == null)
            {
                statusLabel.text = "需要创建或绑定源 Grid";
                return;
            }

            var bounds = surface.GetSurfaceBounds();
            var layerCount = surface.GetSourceTilemaps().Length;
            var renderState = "原生渲染";
            var surfaceState = surface.SurfaceMode == TileMap3DSurfaceMode.Overlay
                ? "Overlay"
                : "生成地面";
            var groundSize = surface.GroundSize;
            statusLabel.text = layerCount + " 个 Tilemap 图层 | "
                + bounds.size.x + " × " + bounds.size.y + " 格 | 单格 "
                + surface.CellSize.ToString("0.###") + " | Surface "
                + groundSize.x.ToString("0.###") + " × "
                + groundSize.y.ToString("0.###") + " | "
                + surfaceState + " / " + renderState;
        }

        /// <summary>
        /// 更新工作台中的越界计数和清理按钮，不改变 Tilemap 或 Surface 数据。
        /// </summary>
        private void RefreshOutOfBoundsTools()
        {
            if (outOfBoundsStatusLabel == null || clearOutOfBoundsButton == null)
            {
                return;
            }

            if (surface == null || surface.SourceGrid == null)
            {
                outOfBoundsStatusLabel.text = "需要源 Grid 才能检查越界 Tile";
                clearOutOfBoundsButton.SetEnabled(false);
                return;
            }

            var count = surface.CountOutOfBoundsTiles();
            outOfBoundsStatusLabel.text = count > 0
                ? "越界 Tile：" + count
                : "未检测到越界 Tile";
            clearOutOfBoundsButton.SetEnabled(count > 0);
        }

        /// <summary>
        /// 合并多次编辑器刷新请求，避免 Hierarchy 连续事件重复重建 UI。
        /// </summary>
        private void ScheduleRefresh()
        {
            if (refreshQueued)
            {
                return;
            }

            refreshQueued = true;
            EditorApplication.delayCall += () =>
            {
                refreshQueued = false;
                if (this != null)
                {
                    RefreshPage();
                }
            };
        }

        /// <summary>
        /// 返回当前地面的第一个原生 Tilemap，供默认绘制目标使用。
        /// </summary>
        private Tilemap FindFirstTilemap()
        {
            if (surface == null)
            {
                return null;
            }

            var tilemaps = surface.GetSourceTilemaps();
            return tilemaps.Length > 0 ? tilemaps[0] : null;
        }

        /// <summary>
        /// 检查 Tilemap 是否属于当前绑定的源 Grid。
        /// </summary>
        private bool IsOwnedTilemap(Tilemap tilemap)
        {
            if (surface == null || tilemap == null || surface.SourceGrid == null)
            {
                return false;
            }

            return tilemap.transform.IsChildOf(surface.SourceGrid.transform);
        }

        /// <summary>
        /// 从当前选择的地面或其子对象向上查找 TileMap3DSurface。
        /// </summary>
        private static TileMap3DSurface FindSurfaceFromSelection()
        {
            var selected = Selection.activeGameObject;
            return selected != null ? selected.GetComponentInParent<TileMap3DSurface>() : null;
        }
    }
}
