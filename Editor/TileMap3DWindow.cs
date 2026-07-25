using System;
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
    public sealed class TileMap3DWindow : EditorWindow
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
        /// 重建工作台页面，所有配置区位于同一个可滚动纵向工作区。
        /// </summary>
        private void RefreshPage()
        {
            var root = rootVisualElement;
            if (root == null)
            {
                return;
            }

            root.Clear();
            outOfBoundsStatusLabel = null;
            clearOutOfBoundsButton = null;
            var headerActions = new VisualElement();
            headerActions.style.flexDirection = FlexDirection.Row;
            headerActions.Add(CreateToolbarPrimaryButton("新建地面", CreateSurface));
            headerActions.Add(CreateSecondaryButton("新建平面", CreateOverlaySurface));
            var page = CreateKitPageScaffold(
                "TileMap3D",
                "在任意平坦 3D 表面上直接使用 Unity Tilemap。",
                KitIcons.SPATIALKIT,
                "TILEMAP3D / PLANAR SURFACE RENDERER",
                headerActions);
            TileMap3DEditorUI.Apply(page.Root, TileMap3DEditorStyleProfile.Full);
            root.Add(page.Root);

            BuildToolbar(page.Toolbar);
            statusLabel = new Label();
            ConfigureStatusLabel(statusLabel);
            page.StatusBar.Add(statusLabel);

            content = page.Content;
            content.style.flexDirection = FlexDirection.Column;
            content.style.flexGrow = 1f;
            content.style.minHeight = 0f;
            BuildContent();
            RefreshStatus();
        }

        /// <summary>
        /// 创建 Surface 选择、重建和可选烘焙主命令栏，并允许按钮在窄窗口换行。
        /// </summary>
        private void BuildToolbar(VisualElement toolbar)
        {
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.flexWrap = Wrap.Wrap;
            surfaceField = new ObjectField("Surface")
            {
                objectType = typeof(TileMap3DSurface),
                value = surface,
                allowSceneObjects = true
            };
            surfaceField.style.minWidth = 220f;
            surfaceField.style.flexGrow = 1f;
            surfaceField.RegisterValueChangedCallback(
                evt => BindSurface(evt.newValue as TileMap3DSurface));
            toolbar.Add(surfaceField);
            toolbar.Add(CreateToolbarButtonWithIcon(KitIcons.REFRESH, "重建", RebuildSurface));
        }

        /// <summary>
        /// 根据绑定状态创建空页面，或创建源图层、表面和烘焙三个配置区。
        /// </summary>
        private void BuildContent()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.style.minHeight = 0f;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Auto;
            scroll.contentContainer.style.paddingBottom = 24f;
            content.Add(scroll);

            if (surface == null)
            {
                var empty = CreateKitSectionPanel(
                    "开始使用 TileMap3D",
                    "创建生成地面，或把平面 Surface 挂到任意 3D 物体下方。",
                    KitIcons.SPATIALKIT);
                empty.body.Add(CreatePrimaryButton("创建 3D Tilemap 地面", CreateSurface));
                empty.body.Add(CreateSecondaryButton("创建平面覆盖 Surface", CreateOverlaySurface));
                scroll.Add(empty.panel);
                return;
            }

            scroll.Add(BuildSourceSection());
            scroll.Add(BuildSurfaceSection());
        }

        /// <summary>
        /// 构建原生 Grid 绑定、Tile Palette 入口和不限数量的 Tilemap 图层列表。
        /// </summary>
        private VisualElement BuildSourceSection()
        {
            var addButton = CreateSmallButton("+", AddLayer);
            addButton.tooltip = "新增原生 Tilemap 图层";
            var section = CreateKitSectionPanel(
                "原生 Tilemap 图层",
                "图层与规则继续由 Unity Tile Palette 管理",
                KitIcons.STACK,
                addButton);
            section.panel.style.marginBottom = SectionSpacing;
            section.body.Add(new IMGUIContainer(DrawSourceSettings));

            var layerList = new VisualElement();
            layerList.style.marginTop = 4f;
            var tilemaps = surface.GetSourceTilemaps(true);
            if (tilemaps.Length == 0)
            {
                var emptyLabel = new Label("尚未创建 Tilemap 图层");
                emptyLabel.style.marginBottom = 4f;
                layerList.Add(emptyLabel);
            }
            else
            {
                for (var i = 0; i < tilemaps.Length; i++)
                {
                    if (tilemaps[i] != null)
                    {
                        layerList.Add(BuildLayerRow(tilemaps[i]));
                    }
                }
            }

            section.body.Add(layerList);
            var outOfBoundsTools = new VisualElement();
            outOfBoundsTools.style.flexDirection = FlexDirection.Row;
            outOfBoundsTools.style.alignItems = Align.Center;
            outOfBoundsTools.style.flexWrap = Wrap.Wrap;
            outOfBoundsTools.style.marginTop = 6f;
            outOfBoundsTools.style.marginBottom = 4f;
            var previewToggle = new Toggle("显示越界 Tile 警示")
            {
                value = surface.ShowOutOfBoundsTilePreview
            };
            previewToggle.tooltip = "仅在 Scene View 标出固定 Surface 区域外的 Tile，不影响最终渲染";
            previewToggle.style.marginRight = 8f;
            previewToggle.RegisterValueChangedCallback(evt => SetOutOfBoundsPreview(evt.newValue));
            outOfBoundsTools.Add(previewToggle);
            outOfBoundsStatusLabel = new Label();
            outOfBoundsStatusLabel.style.flexGrow = 1f;
            outOfBoundsStatusLabel.style.minWidth = 150f;
            outOfBoundsStatusLabel.style.marginRight = 8f;
            outOfBoundsTools.Add(outOfBoundsStatusLabel);
            clearOutOfBoundsButton = CreateSecondaryButton("清理越界 Tile", ClearOutOfBoundsTiles);
            outOfBoundsTools.Add(clearOutOfBoundsButton);
            var clearAllOutOfBoundsButton = CreateSecondaryButton(
                "清理场景中所有越界 Tile",
                ClearAllOutOfBoundsTilesInLoadedScenes);
            clearAllOutOfBoundsButton.tooltip = "清理所有已加载场景中全部 TileMap3D Surface 的越界 Tile";
            outOfBoundsTools.Add(clearAllOutOfBoundsButton);
            section.body.Add(outOfBoundsTools);
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            if (surface.SourceGrid == null)
            {
                actions.Add(CreatePrimaryButton("创建源 Grid", CreateSourceGrid));
            }
            else
            {
                actions.Add(CreatePrimaryButton("打开 Tile Palette", OpenTilePalette));
                actions.Add(CreateSecondaryButton("新增 Tilemap 图层", AddLayer));
            }

            section.body.Add(actions);
            RefreshOutOfBoundsTools();
            return section.panel;
        }

        /// <summary>
        /// 构建单个原生 Tilemap 图层行，提供类型、绘制目标和删除命令。
        /// </summary>
        private VisualElement BuildLayerRow(Tilemap tilemap)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 30f;
            row.style.marginBottom = 2f;

            var nameLabel = new Label(tilemap.name + (tilemap == activeTilemap ? "  [当前]" : string.Empty));
            nameLabel.style.flexGrow = 1f;
            nameLabel.style.minWidth = 120f;
            row.Add(nameLabel);
            var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            var orderText = tilemapRenderer != null
                ? "Order " + tilemapRenderer.sortingOrder
                : "无 Renderer";
            var orderLabel = new Label(orderText);
            orderLabel.style.minWidth = 70f;
            row.Add(orderLabel);
            var defaultType = tilemap.transform.GetSiblingIndex() == 0
                ? TileMap3DLayerType.Base
                : TileMap3DLayerType.Overlay;
            var layer = TileMap3DCommands.EnsureLayerComponent(tilemap, defaultType);
            var layerType = layer != null ? layer.LayerType : defaultType;
            var renderTypeField = new PopupField<string>(
                LayerRenderTypeChoices,
                layerType == TileMap3DLayerType.Base ? 0 : 1);
            renderTypeField.style.minWidth = 125f;
            renderTypeField.tooltip = "渲染类型不限制 Tilemap 层数：每个 Tilemap 都可独立选择 Base 或 Overlay。";
            renderTypeField.RegisterValueChangedCallback(evt => ChangeLayerRenderType(tilemap, evt.newValue));
            row.Add(renderTypeField);
            row.Add(CreateSmallButton("编辑", () => SetActiveLayer(tilemap)));
            var deleteButton = CreateSmallButton("×", () => DeleteLayer(tilemap));
            deleteButton.tooltip = "删除该 Tilemap 图层";
            row.Add(deleteButton);
            return row;
        }

        /// <summary>
        /// 构建平面方向、承载方式、固定区域和原生渲染参数设置。
        /// </summary>
        private VisualElement BuildSurfaceSection()
        {
            var section = CreateKitSectionPanel(
                "3D 平面 Surface",
                "Surface 本地 XZ 是绘制平面，本地 Y 是表面法线",
                KitIcons.SETTINGS);
            section.panel.style.marginBottom = SectionSpacing;
            section.body.Add(new IMGUIContainer(DrawSurfaceSettings));
            var orientationActions = new VisualElement();
            orientationActions.style.flexDirection = FlexDirection.Row;
            orientationActions.style.flexWrap = Wrap.Wrap;
            orientationActions.Add(CreateSecondaryButton(
                "对齐 XZ",
                () => AlignSurface(TileMap3DPlanePreset.XZ)));
            orientationActions.Add(CreateSecondaryButton(
                "对齐 XY",
                () => AlignSurface(TileMap3DPlanePreset.XY)));
            orientationActions.Add(CreateSecondaryButton(
                "对齐 YZ",
                () => AlignSurface(TileMap3DPlanePreset.YZ)));
            orientationActions.Add(CreateSecondaryButton(
                "适配父物体",
                FitSurfaceToParent));
            orientationActions.Add(CreateSecondaryButton(
                "归一化缩放",
                NormalizeSurfaceWorldScale));
            var alignGridButton = CreateSecondaryButton(
                "对齐世界格网",
                EnableWorldGridAlignment);
            alignGridButton.tooltip = "启用持续吸附，并把有效区域左下角对齐到完整世界 Cell";
            alignGridButton.SetEnabled(surface.SurfaceMode == TileMap3DSurfaceMode.GeneratedGround);
            orientationActions.Add(alignGridButton);
            section.body.Add(orientationActions);
            return section.panel;
        }

        /// <summary>
        /// 绘制源 Grid 和 TilemapRenderer 预览开关。
        /// </summary>
        private void DrawSourceSettings()
        {
            if (surface == null)
            {
                return;
            }

            var serializedSurface = cachedSerializedSurface;
            if (serializedSurface == null) return;
            serializedSurface.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedSurface.FindProperty("sourceGrid"), new GUIContent("源 Grid"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("showSourcePreview"),
                new GUIContent("显示原生 Tilemap"));

            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("surfaceProfile"),
                new GUIContent("地面语义 Profile"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("surfaceQueryLayer"),
                new GUIContent("玩法查询图层"));
            if (EditorGUI.EndChangeCheck())
            {
                serializedSurface.ApplyModifiedProperties();
                surface.Rebuild();
                EditorUtility.SetDirty(surface);
                SceneView.RepaintAll();
                ScheduleRefresh();
            }

            if (surface.SourceGrid != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Vector2Field("源 Tile 尺寸（自动）", surface.SourceTileSize);
                }
            }

            if (!string.IsNullOrEmpty(surface.SourceTileSizeWarning))
            {
                EditorGUILayout.HelpBox(surface.SourceTileSizeWarning, MessageType.Warning);
            }
        }

        /// <summary>
        /// 绘制 Surface 承载方式、原生渲染、固定布局和可选生成地面参数。
        /// </summary>
        private void DrawSurfaceSettings()
        {
            if (surface == null)
            {
                return;
            }

            var serializedSurface = cachedSerializedSurface;
            if (serializedSurface == null) return;
            serializedSurface.Update();
            var surfaceModeProperty = serializedSurface.FindProperty("surfaceMode");
            var boundsProperty = serializedSurface.FindProperty("bakeBounds");
            var cellSizeProperty = serializedSurface.FindProperty("cellSize");
            var bounds = boundsProperty.boundsIntValue;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(surfaceModeProperty, new GUIContent("承载方式"));
            var selectedSurfaceMode = (TileMap3DSurfaceMode)surfaceModeProperty.enumValueIndex;
            EditorGUILayout.Space(2f);
            var columns = EditorGUILayout.DelayedIntField("列数", bounds.size.x);
            var rows = EditorGUILayout.DelayedIntField("行数", bounds.size.y);
            var cellSize = EditorGUILayout.DelayedFloatField("单格尺寸", cellSizeProperty.floatValue);
            columns = Mathf.Max(1, columns);
            rows = Mathf.Max(1, rows);
            cellSize = Mathf.Max(0.01f, cellSize);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Vector2Field(
                    "Surface 尺寸",
                    new Vector2(columns * cellSize, rows * cellSize));
            }

            var surfaceOffsetProperty = serializedSurface.FindProperty("surfaceOffset");
            var layerSpacingProperty = serializedSurface.FindProperty("layerSpacing");
            surfaceOffsetProperty.floatValue = Mathf.Max(
                0f,
                EditorGUILayout.DelayedFloatField("表面偏移", surfaceOffsetProperty.floatValue));

            layerSpacingProperty.floatValue = Mathf.Max(
                0.0001f,
                EditorGUILayout.DelayedFloatField("图层间距", layerSpacingProperty.floatValue));

            var editedSurfaceMode = (TileMap3DSurfaceMode)surfaceModeProperty.enumValueIndex;
            if (editedSurfaceMode == TileMap3DSurfaceMode.GeneratedGround)
            {
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("keepWorldGridAligned"),
                    new GUIContent("保持世界格网对齐"));
                var thicknessProperty = serializedSurface.FindProperty("thickness");
                var thickness = EditorGUILayout.DelayedFloatField("厚度", thicknessProperty.floatValue);
                thicknessProperty.floatValue = Mathf.Max(0.01f, thickness);
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("surfaceColor"),
                    new GUIContent("地面底色"));
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("groundMaterial"),
                    new GUIContent("地面基底材质"));
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("sideMaterial"),
                    new GUIContent("侧壁材质"));
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "NativeTilemap：独立 TilemapRenderer 平面，Shader Offset 防止与目标面 z-fighting。",
                    MessageType.Info);
            }

            if (EditorGUI.EndChangeCheck())
            {
                if (editedSurfaceMode == TileMap3DSurfaceMode.GeneratedGround)
                {
                    Undo.RecordObject(surface.transform, "修改 TileMap3D 世界格网对齐");
                }

                bounds.size = new Vector3Int(columns, rows, 1);
                boundsProperty.boundsIntValue = bounds;
                cellSizeProperty.floatValue = cellSize;
                serializedSurface.FindProperty("showSourcePreview").boolValue = true;
                serializedSurface.ApplyModifiedProperties();
                surface.Rebuild();
                if (surface.KeepWorldGridAligned)
                {
                    surface.AlignToWorldGrid();
                    EditorUtility.SetDirty(surface.transform);
                }

                EditorUtility.SetDirty(surface);
                SceneView.RepaintAll();
                RefreshStatus();
            }
        }

        /// <summary>
        /// 创建一个完整 TileMap3D 地面并绑定到当前工作台。
        /// </summary>
        private void CreateSurface()
        {
            BindSurface(TileMap3DCommands.CreateSurface(null));
        }

        /// <summary>
        /// 在当前选择对象下创建不带几何和碰撞的平面覆盖 Surface。
        /// </summary>
        private void CreateOverlaySurface()
        {
            BindSurface(TileMap3DCommands.CreateOverlaySurface(Selection.activeGameObject));
        }

        /// <summary>
        /// 为当前地面补建源 Grid 和基础 Tilemap 图层。
        /// </summary>
        private void CreateSourceGrid()
        {
            if (surface == null)
            {
                return;
            }

            TileMap3DCommands.CreateSourceGrid(surface);
            activeTilemap = TileMap3DCommands.AddLayer(surface);
            ScheduleRefresh();
        }

        /// <summary>
        /// 新增原生 Tilemap 图层并立即设为当前绘制目标。
        /// </summary>
        private void AddLayer()
        {
            if (surface == null)
            {
                return;
            }

            activeTilemap = TileMap3DCommands.AddLayer(surface);
            if (activeTilemap != null)
            {
                TileMap3DCommands.SetPaintTarget(activeTilemap, false);
            }

            ScheduleRefresh();
        }

        /// <summary>
        /// 写入 Scene View 越界 Tile 警示开关，并保留为可撤销的 Surface 配置修改。
        /// </summary>
        private void SetOutOfBoundsPreview(bool visible)
        {
            if (surface == null || surface.ShowOutOfBoundsTilePreview == visible)
            {
                return;
            }

            Undo.RecordObject(surface, "切换 TileMap3D 越界 Tile 警示");
            surface.SetOutOfBoundsTilePreviewVisible(visible);
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 统计后确认清理全部源图层的越界 Tile；清理结果可通过 Unity Undo 恢复。
        /// </summary>
        private void ClearOutOfBoundsTiles()
        {
            if (surface == null)
            {
                return;
            }

            var count = surface.CountOutOfBoundsTiles();
            if (count <= 0)
            {
                RefreshOutOfBoundsTools();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "清理越界 Tile",
                    "将删除当前 Surface 全部 Tilemap 图层中位于固定区域外的 "
                    + count + " 个 Tile。此操作可通过 Undo 恢复。",
                    "清理",
                    "取消"))
            {
                return;
            }

            var clearedCount = TileMap3DCommands.ClearOutOfBoundsTiles(surface);
            if (statusLabel != null)
            {
                statusLabel.text = clearedCount > 0
                    ? "已清理 " + clearedCount + " 个越界 Tile"
                    : "没有需要清理的越界 Tile";
            }

            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 统计后确认清理所有已加载场景中的越界 Tile；整次操作可通过一次 Undo 恢复。
        /// </summary>
        private void ClearAllOutOfBoundsTilesInLoadedScenes()
        {
            var count = TileMap3DCommands.CountOutOfBoundsTilesInLoadedScenes();
            if (count <= 0)
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "场景中没有需要清理的越界 Tile";
                }

                RefreshOutOfBoundsTools();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "清理场景中所有越界 Tile",
                    "将删除所有已加载场景的全部 TileMap3D Surface 中位于固定区域外的 "
                    + count + " 个 Tile。此操作可通过一次 Undo 恢复。",
                    "全部清理",
                    "取消"))
            {
                return;
            }

            var clearedCount = TileMap3DCommands.ClearOutOfBoundsTilesInLoadedScenes();
            if (statusLabel != null)
            {
                statusLabel.text = clearedCount > 0
                    ? "已清理场景中的 " + clearedCount + " 个越界 Tile"
                    : "场景中没有需要清理的越界 Tile";
            }

            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 修改原生 Tilemap 的渲染类型并立即刷新材质与法线偏移。
        /// </summary>
        private void ChangeLayerRenderType(Tilemap tilemap, string value)
        {
            var layerType = value == LayerRenderTypeChoices[0]
                ? TileMap3DLayerType.Base
                : TileMap3DLayerType.Overlay;

            var layer = TileMap3DCommands.EnsureLayerComponent(tilemap, layerType);
            if (layer == null)
            {
                return;
            }

            Undo.RecordObject(layer, "修改 TileMap3D 图层渲染类型");
            layer.Configure(layerType);
            EditorUtility.SetDirty(layer);
            if (surface != null)
            {
                surface.Rebuild();
                EditorUtility.SetDirty(surface);
            }

            SceneView.RepaintAll();
            ScheduleRefresh();
        }

        /// <summary>
        /// 将当前 Surface 对齐到父物体局部平面预设。
        /// </summary>
        private void AlignSurface(TileMap3DPlanePreset planePreset)
        {
            TileMap3DCommands.AlignSurface(surface, planePreset);
            RefreshStatus();
        }

        /// <summary>
        /// 按直接父物体的当前平面范围重新计算 Overlay 的位置、列数和行数。
        /// </summary>
        private void FitSurfaceToParent()
        {
            if (TileMap3DCommands.FitSurfaceToParent(surface))
            {
                RefreshStatus();
                return;
            }

            if (statusLabel != null)
            {
                statusLabel.text = "适配失败：请选择 Overlay，且直接父物体需要 Mesh、BoxCollider、MeshCollider 或 Renderer";
            }
        }

        /// <summary>
        /// 重新计算父级非等比缩放补偿，避免旋转后的 Surface 拉伸 Tile。
        /// </summary>
        private void NormalizeSurfaceWorldScale()
        {
            TileMap3DCommands.NormalizeSurfaceWorldScale(surface);
            RefreshStatus();
        }

        /// <summary>
        /// 为当前 Generated Ground 启用持续世界格网吸附，并立即修正旧场景中的小数相位。
        /// </summary>
        private void EnableWorldGridAlignment()
        {
            if (!TileMap3DCommands.EnableWorldGridAlignment(surface))
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "世界格网对齐仅用于 Generated Ground";
                }

                return;
            }

            RefreshStatus();
        }

        /// <summary>
        /// 把指定图层设为 Unity Tile Palette 的 Scene Paint Target。
        /// </summary>
        private void SetActiveLayer(Tilemap tilemap)
        {
            activeTilemap = tilemap;
            TileMap3DCommands.SetPaintTarget(tilemap);
            ScheduleRefresh();
        }

        /// <summary>
        /// 经确认后通过 Undo 删除指定原生 Tilemap 图层。
        /// </summary>
        private void DeleteLayer(Tilemap tilemap)
        {
            if (tilemap == null || !EditorUtility.DisplayDialog(
                    "删除 Tilemap 图层",
                    "确定删除图层 “" + tilemap.name + "” 及其全部 Tile 数据？",
                    "删除",
                    "取消"))
            {
                return;
            }

            if (activeTilemap == tilemap)
            {
                activeTilemap = null;
            }

            Undo.DestroyObjectImmediate(tilemap.gameObject);
            if (surface != null)
            {
                surface.Rebuild();
                EditorUtility.SetDirty(surface);
            }

            activeTilemap = FindFirstTilemap();
            ScheduleRefresh();
        }

        /// <summary>
        /// 打开 Unity Tile Palette；没有活动图层时自动选择或创建一个图层。
        /// </summary>
        private void OpenTilePalette()
        {
            if (!IsOwnedTilemap(activeTilemap))
            {
                activeTilemap = FindFirstTilemap();
            }

            if (activeTilemap == null)
            {
                AddLayer();
            }

            if (activeTilemap != null)
            {
                TileMap3DCommands.SetPaintTarget(activeTilemap);
            }
        }

        /// <summary>
        /// 根据当前设置重建原生图层和可选生成几何，不改动 Tilemap 数据。
        /// </summary>
        private void RebuildSurface()
        {
            if (surface == null)
            {
                return;
            }

            Undo.RecordObject(surface, "重建 TileMap3D 地面");
            surface.Rebuild();
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
            RefreshStatus();
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

            var bounds = surface.GetBakeBounds();
            var layerCount = surface.GetSourceTilemaps(true).Length;
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

            var tilemaps = surface.GetSourceTilemaps(true);
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
