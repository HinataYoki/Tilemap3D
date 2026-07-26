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
    /// TileMap3D 工作台的页面与配置区构建（partial）。
    /// </summary>
    public sealed partial class TileMap3DWindow
    {
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
            var tilemaps = surface.GetSourceTilemaps();
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
            var boundsProperty = serializedSurface.FindProperty("surfaceBounds");
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
    }
}
