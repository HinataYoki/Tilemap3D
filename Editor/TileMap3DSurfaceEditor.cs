using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static YokiFrame.Unity.TileMap3D.TileMap3DEditorUI;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的轻量 Inspector，详细配置统一由工作台承载。
    /// </summary>
    [CustomEditor(typeof(TileMap3DSurface))]
    public sealed class TileMap3DSurfaceEditor : UnityEditor.Editor
    {
        /// <summary>
        /// 创建使用 TileMap3D 自有 UI Toolkit 样式的核心引用、快捷操作和烘焙状态界面。
        /// </summary>
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            root.style.marginTop = Spacing.SM;
            TileMap3DEditorUI.Apply(root, TileMap3DEditorStyleProfile.CoreOnly);

            var surface = (TileMap3DSurface)target;
            var summary = new Label("任意方向的平面 Surface、原生 TilemapRenderer 与可选生成地面。")
            {
                style = { whiteSpace = WhiteSpace.Normal }
            };
            summary.style.color = new StyleColor(Colors.TextSecondary);
            summary.style.marginBottom = Spacing.SM;
            root.Add(summary);

            AddProperty(root, "surfaceMode", "承载方式");
            AddProperty(root, "sourceGrid", "源 Grid");
            AddProperty(root, "surfaceProfile", "地面语义 Profile");
            AddProperty(root, "surfaceQueryLayer", "玩法查询图层");
            AddProperty(root, "showSourcePreview", "显示原生 Tilemap");
            AddProperty(root, "showOutOfBoundsTilePreview", "显示越界 Tile 警示");
            AddProperty(root, "surfaceOffset", "表面偏移");
            AddProperty(root, "layerSpacing", "图层间距");
            if (surface.SurfaceMode == TileMap3DSurfaceMode.GeneratedGround)
            {
                AddProperty(root, "groundMaterial", "地面基底材质");
                AddProperty(root, "sideMaterial", "侧壁材质");
            }

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.flexWrap = Wrap.Wrap;
            actions.style.marginTop = Spacing.SM;
            actions.Add(CreatePrimaryButton("打开 TileMap3D", () => TileMap3DWindow.Open(surface)));
            actions.Add(CreateSecondaryButton("重建 Surface", () => RebuildSurface(surface)));
            root.Add(actions);

            var tilemapCount = surface.GetSourceTilemaps().Length;
            var renderText = "原生渲染";
            var status = new Label(tilemapCount + " 个 Tilemap 图层 | " + renderText);
            status.style.marginTop = Spacing.SM;
            status.style.color = new StyleColor(Colors.TextSecondary);
            root.Add(status);
            root.Bind(serializedObject);
            return root;
        }

        /// <summary>
        /// 按序列化字段名添加一个 PropertyField，字段缺失时保持 Inspector 可用。
        /// </summary>
        private void AddProperty(VisualElement root, string propertyName, string label)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property != null)
            {
                root.Add(new PropertyField(property, label));
            }
        }

        /// <summary>
        /// 通过 Undo 刷新原生图层和可选生成几何，并重绘 Scene View。
        /// </summary>
        private static void RebuildSurface(TileMap3DSurface surface)
        {
            Undo.RecordObject(surface, "重建 TileMap3D 地面");
            surface.Rebuild();
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
        }
    }
}
