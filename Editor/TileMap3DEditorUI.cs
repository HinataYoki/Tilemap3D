using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3D 自己持有的 UI Toolkit 控件和设计令牌。
    /// 该类型不引用 YokiFrame 程序集，工具可以独立安装和编译。
    /// </summary>
    internal static class TileMap3DEditorUI
    {
        /// <summary>TileMap3D 使用的间距令牌。</summary>
        internal static class Spacing
        {
            public const float XS = 4f;
            public const float SM = 8f;
            public const float MD = 12f;
        }

        /// <summary>TileMap3D 编辑器的颜色令牌。</summary>
        internal static class Colors
        {
            public static readonly Color TextPrimary = new Color(0.93f, 0.95f, 0.98f, 1f);
            public static readonly Color TextSecondary = new Color(0.66f, 0.71f, 0.78f, 1f);
            public static readonly Color BrandPrimary = new Color(0.20f, 0.62f, 0.95f, 1f);
            public static readonly Color BorderDefault = new Color(0.20f, 0.25f, 0.32f, 1f);
            public static readonly Color LayerToolbar = new Color(0.12f, 0.15f, 0.20f, 1f);
            public static readonly Color Panel = new Color(0.105f, 0.125f, 0.16f, 1f);
            public static readonly Color PanelHeader = new Color(0.13f, 0.16f, 0.20f, 1f);
        }

        /// <summary>工作台中使用的短图标文本。</summary>
        internal static class KitIcons
        {
            public const string SPATIALKIT = "[T]";
            public const string SETTINGS = "[S]";
            public const string STACK = "[L]";
            public const string TARGET = "[B]";
            public const string REFRESH = "[R]";
        }

        /// <summary>工作台样式范围。</summary>
        internal enum TileMap3DEditorStyleProfile
        {
            CoreOnly,
            Full
        }

        /// <summary>工作台页面的固定区域。</summary>
        internal sealed class KitPageScaffold
        {
            public VisualElement Root { get; }
            public VisualElement Toolbar { get; }
            public VisualElement StatusBar { get; }
            public VisualElement Content { get; }

            /// <summary>保存页面根节点、工具栏、状态栏和内容节点。</summary>
            public KitPageScaffold(
                VisualElement root,
                VisualElement toolbar,
                VisualElement statusBar,
                VisualElement content)
            {
                Root = root;
                Toolbar = toolbar;
                StatusBar = statusBar;
                Content = content;
            }
        }

        /// <summary>给 Inspector 或窗口根节点应用 TileMap3D 的基础视觉令牌。</summary>
        internal static void Apply(VisualElement root, TileMap3DEditorStyleProfile profile)
        {
            if (root == null)
            {
                return;
            }

            root.AddToClassList("tilemap3d-editor-root");
            root.style.flexDirection = FlexDirection.Column;
            root.style.color = new StyleColor(Colors.TextPrimary);
            root.style.backgroundColor = new StyleColor(Colors.Panel);
            root.style.paddingLeft = profile == TileMap3DEditorStyleProfile.Full ? Spacing.MD : Spacing.SM;
            root.style.paddingRight = profile == TileMap3DEditorStyleProfile.Full ? Spacing.MD : Spacing.SM;
            root.style.paddingTop = Spacing.SM;
            root.style.paddingBottom = Spacing.SM;
        }

        /// <summary>创建带标题、工具栏、状态栏和内容区的工作台页面。</summary>
        internal static KitPageScaffold CreateKitPageScaffold(
            string title,
            string subtitle,
            string icon,
            string kicker,
            VisualElement headerActions)
        {
            var root = new VisualElement();
            root.AddToClassList("tilemap3d-page");
            root.style.flexDirection = FlexDirection.Column;
            root.style.flexGrow = 1f;

            var header = new VisualElement();
            header.AddToClassList("tilemap3d-page__header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.paddingBottom = Spacing.SM;

            var iconLabel = new Label(icon ?? string.Empty);
            iconLabel.style.width = 42f;
            iconLabel.style.fontSize = 14f;
            iconLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            iconLabel.style.color = new StyleColor(Colors.BrandPrimary);
            header.Add(iconLabel);

            var titleBlock = new VisualElement();
            titleBlock.style.flexGrow = 1f;
            var kickerLabel = new Label(kicker ?? string.Empty);
            kickerLabel.style.fontSize = 10f;
            kickerLabel.style.color = new StyleColor(Colors.TextSecondary);
            titleBlock.Add(kickerLabel);
            var titleLabel = new Label(title ?? string.Empty);
            titleLabel.style.fontSize = 16f;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new StyleColor(Colors.TextPrimary);
            titleBlock.Add(titleLabel);
            var subtitleLabel = new Label(subtitle ?? string.Empty);
            subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
            subtitleLabel.style.color = new StyleColor(Colors.TextSecondary);
            titleBlock.Add(subtitleLabel);
            header.Add(titleBlock);

            if (headerActions != null)
            {
                headerActions.style.flexDirection = FlexDirection.Row;
                headerActions.style.flexWrap = Wrap.Wrap;
                header.Add(headerActions);
            }

            root.Add(header);

            var toolbar = new VisualElement();
            toolbar.AddToClassList("tilemap3d-page__toolbar");
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.flexWrap = Wrap.Wrap;
            toolbar.style.paddingTop = Spacing.XS;
            toolbar.style.paddingBottom = Spacing.XS;
            toolbar.style.borderTopWidth = 1f;
            toolbar.style.borderBottomWidth = 1f;
            toolbar.style.borderTopColor = new StyleColor(Colors.BorderDefault);
            toolbar.style.borderBottomColor = new StyleColor(Colors.BorderDefault);
            root.Add(toolbar);

            var statusBar = new VisualElement();
            statusBar.AddToClassList("tilemap3d-page__status");
            statusBar.style.flexDirection = FlexDirection.Row;
            statusBar.style.alignItems = Align.Center;
            statusBar.style.minHeight = 24f;
            statusBar.style.paddingTop = Spacing.XS;
            statusBar.style.paddingBottom = Spacing.XS;
            root.Add(statusBar);

            var content = new VisualElement();
            content.AddToClassList("tilemap3d-page__content");
            content.style.flexGrow = 1f;
            content.style.minHeight = 0f;
            root.Add(content);

            return new KitPageScaffold(root, toolbar, statusBar, content);
        }

        /// <summary>创建带标题、说明和可选尾部操作的配置面板。</summary>
        internal static (VisualElement panel, VisualElement body) CreateKitSectionPanel(
            string title,
            string subtitle,
            string icon,
            VisualElement trailing = null)
        {
            var panel = new VisualElement();
            panel.AddToClassList("tilemap3d-section");
            panel.style.flexDirection = FlexDirection.Column;
            panel.style.marginBottom = Spacing.SM;
            panel.style.backgroundColor = new StyleColor(Colors.Panel);
            panel.style.borderTopWidth = 1f;
            panel.style.borderBottomWidth = 1f;
            panel.style.borderLeftWidth = 1f;
            panel.style.borderRightWidth = 1f;
            panel.style.borderTopColor = new StyleColor(Colors.BorderDefault);
            panel.style.borderBottomColor = new StyleColor(Colors.BorderDefault);
            panel.style.borderLeftColor = new StyleColor(Colors.BorderDefault);
            panel.style.borderRightColor = new StyleColor(Colors.BorderDefault);

            var header = new VisualElement();
            header.AddToClassList("tilemap3d-section__header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.minHeight = 38f;
            header.style.paddingLeft = Spacing.SM;
            header.style.paddingRight = Spacing.SM;
            header.style.backgroundColor = new StyleColor(Colors.PanelHeader);

            var iconLabel = new Label(icon ?? string.Empty);
            iconLabel.style.width = 32f;
            iconLabel.style.color = new StyleColor(Colors.BrandPrimary);
            iconLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(iconLabel);

            var titleBlock = new VisualElement();
            titleBlock.style.flexGrow = 1f;
            var titleLabel = new Label(title ?? string.Empty);
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = new StyleColor(Colors.TextPrimary);
            titleBlock.Add(titleLabel);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var subtitleLabel = new Label(subtitle);
                subtitleLabel.style.fontSize = 10f;
                subtitleLabel.style.color = new StyleColor(Colors.TextSecondary);
                subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
                titleBlock.Add(subtitleLabel);
            }

            header.Add(titleBlock);
            if (trailing != null)
            {
                trailing.style.flexShrink = 0f;
                header.Add(trailing);
            }

            panel.Add(header);
            var body = new VisualElement();
            body.AddToClassList("tilemap3d-section__body");
            body.style.paddingLeft = Spacing.SM;
            body.style.paddingRight = Spacing.SM;
            body.style.paddingTop = Spacing.SM;
            body.style.paddingBottom = Spacing.SM;
            body.style.minHeight = 0f;
            panel.Add(body);
            return (panel, body);
        }

        /// <summary>创建主要操作按钮。</summary>
        internal static Button CreatePrimaryButton(string text, Action action)
        {
            return CreateButton(text, action, Colors.BrandPrimary, Colors.TextPrimary, 28f);
        }

        /// <summary>创建次要操作按钮。</summary>
        internal static Button CreateSecondaryButton(string text, Action action)
        {
            return CreateButton(text, action, Colors.LayerToolbar, Colors.TextPrimary, 28f);
        }

        /// <summary>创建工具栏中的主要操作按钮。</summary>
        internal static Button CreateToolbarPrimaryButton(string text, Action action)
        {
            var button = CreatePrimaryButton(text, action);
            button.style.marginLeft = Spacing.XS;
            button.style.marginRight = Spacing.XS;
            return button;
        }

        /// <summary>创建带短图标文本的工具栏按钮。</summary>
        internal static Button CreateToolbarButtonWithIcon(string icon, string text, Action action)
        {
            var button = CreateSecondaryButton((icon ?? string.Empty) + " " + (text ?? string.Empty), action);
            button.style.marginLeft = Spacing.XS;
            button.style.marginRight = Spacing.XS;
            return button;
        }

        /// <summary>创建紧凑按钮，用于图层和局部操作。</summary>
        internal static Button CreateSmallButton(string text, Action action)
        {
            var button = CreateButton(text, action, Colors.LayerToolbar, Colors.TextPrimary, 24f);
            button.style.minWidth = 32f;
            button.style.paddingLeft = 6f;
            button.style.paddingRight = 6f;
            button.style.marginRight = 4f;
            return button;
        }

        /// <summary>配置工作台底部状态标签，避免依赖外部 USS。</summary>
        internal static void ConfigureStatusLabel(Label label)
        {
            if (label == null)
            {
                return;
            }

            label.AddToClassList("tilemap3d-status-label");
            label.style.flexGrow = 1f;
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.color = new StyleColor(Colors.TextSecondary);
        }

        /// <summary>按语义创建按钮并设置统一的 Toolkit 布局。</summary>
        private static Button CreateButton(string text, Action action, Color background, Color foreground, float height)
        {
            var button = new Button();
            button.text = text ?? string.Empty;
            if (action != null)
            {
                button.clicked += action;
            }

            button.style.minHeight = height;
            button.style.paddingLeft = 10f;
            button.style.paddingRight = 10f;
            button.style.marginRight = 4f;
            button.style.backgroundColor = new StyleColor(background);
            button.style.color = new StyleColor(foreground);
            button.style.borderBottomWidth = 1f;
            button.style.borderBottomColor = new StyleColor(Colors.BorderDefault);
            return button;
        }

    }

}
