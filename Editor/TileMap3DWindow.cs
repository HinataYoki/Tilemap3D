using System;
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

        [SerializeField] private TileMap3DSurface surface;
        [SerializeField] private Tilemap activeTilemap;

        private ObjectField surfaceField;
        private Label statusLabel;
        private VisualElement content;
        private bool refreshQueued;

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
            var bakeButton = CreateToolbarPrimaryButton("烘焙并应用", BakeSurface);
            bakeButton.SetEnabled(surface != null
                && surface.SurfaceMode == TileMap3DSurfaceMode.GeneratedGround);
            toolbar.Add(bakeButton);
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
            scroll.Add(BuildBakeSection());
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
            var typeField = new EnumField(layer != null ? layer.LayerType : defaultType);
            typeField.style.minWidth = 90f;
            typeField.tooltip = "Base 写入深度；Overlay 与 Effect 透明叠加";
            typeField.RegisterValueChangedCallback(evt => ChangeLayerType(tilemap, evt.newValue));
            row.Add(typeField);
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
            section.body.Add(orientationActions);
            return section.panel;
        }

        /// <summary>
        /// 构建贴图分辨率、过滤、压缩、输出目录和烘焙结果设置。
        /// </summary>
        private VisualElement BuildBakeSection()
        {
            var section = CreateKitSectionPanel(
                "烘焙输出",
                "GeneratedGround 的可选静态输出；原生模式无需烘焙",
                KitIcons.TARGET);
            section.body.Add(new IMGUIContainer(DrawBakeSettings));
            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            var bakeButton = CreatePrimaryButton("烘焙并应用", BakeSurface);
            bakeButton.SetEnabled(surface.SurfaceMode == TileMap3DSurfaceMode.GeneratedGround);
            actions.Add(bakeButton);
            actions.Add(CreateSecondaryButton("选择输出目录", ChooseOutputFolder));
            section.body.Add(actions);
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

            var serializedSurface = new SerializedObject(surface);
            serializedSurface.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(serializedSurface.FindProperty("sourceGrid"), new GUIContent("源 Grid"));
            using (new EditorGUI.DisabledScope(
                       surface.RenderMode == TileMap3DRenderMode.SurfaceMaterial))
            {
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("showSourcePreview"),
                    new GUIContent("显示原生 Tilemap"));
            }

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

            var serializedSurface = new SerializedObject(surface);
            serializedSurface.Update();
            var surfaceModeProperty = serializedSurface.FindProperty("surfaceMode");
            var renderModeProperty = serializedSurface.FindProperty("renderMode");
            var boundsProperty = serializedSurface.FindProperty("bakeBounds");
            var cellSizeProperty = serializedSurface.FindProperty("cellSize");
            var bounds = boundsProperty.boundsIntValue;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(surfaceModeProperty, new GUIContent("承载方式"));
            var selectedSurfaceMode = (TileMap3DSurfaceMode)surfaceModeProperty.enumValueIndex;
            EditorGUILayout.PropertyField(renderModeProperty, new GUIContent("渲染模式"));
            var selectedRenderMode = (TileMap3DRenderMode)renderModeProperty.enumValueIndex;
            if (selectedSurfaceMode == TileMap3DSurfaceMode.Overlay
                && selectedRenderMode == TileMap3DRenderMode.BakedTexture)
            {
                renderModeProperty.enumValueIndex = (int)TileMap3DRenderMode.SurfaceMaterial;
            }
            else if (selectedSurfaceMode == TileMap3DSurfaceMode.GeneratedGround
                && selectedRenderMode == TileMap3DRenderMode.SurfaceMaterial)
            {
                renderModeProperty.enumValueIndex = (int)TileMap3DRenderMode.NativeTilemap;
            }

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
            var editedRenderMode = (TileMap3DRenderMode)renderModeProperty.enumValueIndex;
            using (new EditorGUI.DisabledScope(
                       editedRenderMode == TileMap3DRenderMode.SurfaceMaterial))
            {
                surfaceOffsetProperty.floatValue = Mathf.Max(
                    0.0001f,
                    EditorGUILayout.DelayedFloatField("表面偏移", surfaceOffsetProperty.floatValue));
            }

            layerSpacingProperty.floatValue = Mathf.Max(
                0.0001f,
                EditorGUILayout.DelayedFloatField("图层间距", layerSpacingProperty.floatValue));

            var editedSurfaceMode = (TileMap3DSurfaceMode)surfaceModeProperty.enumValueIndex;
            if (editedSurfaceMode == TileMap3DSurfaceMode.GeneratedGround)
            {
                var thicknessProperty = serializedSurface.FindProperty("thickness");
                var thickness = EditorGUILayout.DelayedFloatField("厚度", thicknessProperty.floatValue);
                thicknessProperty.floatValue = Mathf.Max(0.01f, thickness);
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("surfaceColor"),
                    new GUIContent("地面底色"));
                EditorGUILayout.PropertyField(
                    serializedSurface.FindProperty("sideMaterial"),
                    new GUIContent("侧壁材质"));
            }
            else
            {
                var overlayMessage = editedRenderMode == TileMap3DRenderMode.SurfaceMaterial
                    ? "SurfaceMaterial 在目标 Mesh 同一几何上渲染，源 Tilemap 只保存数据；没有物理表面偏移。"
                    : "NativeTilemap 使用独立 TilemapRenderer 平面和表面偏移作为兼容路径。";
                EditorGUILayout.HelpBox(overlayMessage, MessageType.Info);
                if (!string.IsNullOrEmpty(surface.SurfaceMaterialWarning))
                {
                    EditorGUILayout.HelpBox(surface.SurfaceMaterialWarning, MessageType.Warning);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                bounds.size = new Vector3Int(columns, rows, 1);
                boundsProperty.boundsIntValue = bounds;
                cellSizeProperty.floatValue = cellSize;
                if ((TileMap3DRenderMode)renderModeProperty.enumValueIndex
                    == TileMap3DRenderMode.NativeTilemap)
                {
                    serializedSurface.FindProperty("showSourcePreview").boolValue = true;
                }
                else if ((TileMap3DRenderMode)renderModeProperty.enumValueIndex
                    == TileMap3DRenderMode.SurfaceMaterial)
                {
                    serializedSurface.FindProperty("showSourcePreview").boolValue = false;
                }

                serializedSurface.ApplyModifiedProperties();
                surface.Rebuild();
                EditorUtility.SetDirty(surface);
                SceneView.RepaintAll();
                RefreshStatus();
            }
        }

        /// <summary>
        /// 绘制每格像素、最大尺寸、纹理导入和持久化产物设置。
        /// </summary>
        private void DrawBakeSettings()
        {
            if (surface == null)
            {
                return;
            }

            var serializedSurface = new SerializedObject(surface);
            serializedSurface.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("pixelsPerCell"),
                new GUIContent("每格像素"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("maximumTextureSize"),
                new GUIContent("最大贴图尺寸"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("bakeFilterMode"),
                new GUIContent("过滤模式"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("generateMipMaps"),
                new GUIContent("生成 Mipmap"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("textureCompression"),
                new GUIContent("压缩质量"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("hideSourceAfterBake"),
                new GUIContent("烘焙后隐藏 Tilemap"));
            EditorGUILayout.PropertyField(
                serializedSurface.FindProperty("outputFolder"),
                new GUIContent("输出目录"));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("烘焙贴图", surface.BakedTexture, typeof(Texture2D), false);
                EditorGUILayout.ObjectField("地面材质", surface.BakedMaterial, typeof(Material), false);
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedSurface.ApplyModifiedProperties();
                EditorUtility.SetDirty(surface);
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
        /// 修改原生 Tilemap 的 3D 图层职责并立即刷新材质与法线偏移。
        /// </summary>
        private void ChangeLayerType(Tilemap tilemap, Enum value)
        {
            if (!(value is TileMap3DLayerType layerType))
            {
                return;
            }

            var layer = TileMap3DCommands.EnsureLayerComponent(tilemap, layerType);
            if (layer == null)
            {
                return;
            }

            Undo.RecordObject(layer, "修改 TileMap3D 图层类型");
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
        /// 执行 Tilemap 合成并把持久化贴图材质应用到 3D 地面。
        /// </summary>
        private void BakeSurface()
        {
            if (surface == null)
            {
                return;
            }

            var result = TileMap3DBaker.Bake(surface);
            if (!result.Success)
            {
                statusLabel.text = result.Error;
                Debug.LogError(result.Error, surface);
                return;
            }

            var message = "已烘焙 " + result.Width + " × " + result.Height
                + "，图层 " + result.LayerCount + "，已应用到 3D 地面";
            if (!string.IsNullOrEmpty(result.Warning))
            {
                message += "；" + result.Warning;
            }

            statusLabel.text = message;
            Selection.activeObject = surface.gameObject;
            SceneView.RepaintAll();
            ScheduleRefresh();
        }

        /// <summary>
        /// 选择 Assets 内的烘焙输出目录并写回组件设置。
        /// </summary>
        private void ChooseOutputFolder()
        {
            if (surface == null)
            {
                return;
            }

            var selected = EditorUtility.OpenFolderPanel(
                "选择 TileMap3D 输出目录",
                Application.dataPath,
                string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            var normalized = selected.Replace('\\', '/');
            var assetsRoot = Application.dataPath.Replace('\\', '/');
            if (!normalized.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("TileMap3D", "输出目录必须位于当前项目的 Assets 下。", "确定");
                return;
            }

            var assetPath = "Assets" + normalized.Substring(assetsRoot.Length);
            var serializedSurface = new SerializedObject(surface);
            serializedSurface.FindProperty("outputFolder").stringValue = assetPath.TrimEnd('/');
            serializedSurface.ApplyModifiedProperties();
            EditorUtility.SetDirty(surface);
            ScheduleRefresh();
        }

        /// <summary>
        /// 刷新底部状态栏中的源图层、区域和烘焙状态。
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
            var renderState = surface.RenderMode == TileMap3DRenderMode.NativeTilemap
                ? "原生渲染"
                : surface.RenderMode == TileMap3DRenderMode.SurfaceMaterial
                    ? surface.IsSurfaceMaterialActive ? "Mesh 表面材质" : "表面材质回退"
                    : surface.BakedTexture != null ? "烘焙渲染" : "等待烘焙";
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
