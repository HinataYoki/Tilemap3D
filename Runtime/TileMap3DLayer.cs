using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 描述原生 Tilemap 图层在 3D 平面上的渲染职责。
    /// </summary>
    public enum TileMap3DLayerType
    {
        Base,
        Overlay,
        // 旧场景序列化兼容值。Effect 的渲染行为始终等同 Overlay，编辑器不再提供此选项。
        Effect
    }

    /// <summary>
    /// 为原生 TilemapRenderer 补充 3D 深度材质和沿表面法线的图层偏移。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Tilemap), typeof(TilemapRenderer))]
    public sealed class TileMap3DLayer : MonoBehaviour
    {
        private const string BaseMaterialResourcePath = "TileMap3D/TileMap3DBase";
        private const string OverlayMaterialResourcePath = "TileMap3D/TileMap3DOverlay";
        private const string BaseShaderName = "TileMap3D/TilemapSurfaceCutout";
        private const string OverlayShaderName = "TileMap3D/TilemapSurfaceTransparent";
        private static readonly int ReceiveShadowsId = Shader.PropertyToID(
            "_TileMap3DReceiveShadows");

        [SerializeField] private TileMap3DLayerType layerType = TileMap3DLayerType.Overlay;
        [SerializeField] private Material materialOverride;
        [SerializeField, Min(0f)] private float additionalNormalOffset;
        [SerializeField] private bool receiveShadows = true;

        [NonSerialized] private Material fallbackMaterial;
        [NonSerialized] private MaterialPropertyBlock rendererProperties;

        public TileMap3DLayerType LayerType => NormalizeLayerType(layerType);
        public Material MaterialOverride => materialOverride;
        public float AdditionalNormalOffset => additionalNormalOffset;
        public bool ReceiveShadows => receiveShadows;

        /// <summary>
        /// 设置新图层的职责，并请求所属 Surface 刷新渲染状态。
        /// </summary>
        public void Configure(TileMap3DLayerType value)
        {
            layerType = NormalizeLayerType(value);
            RefreshOwnerSurface();
        }

        /// <summary>
        /// 设置本图层是否接收实时阴影，并在下一次刷新时同步原生 Renderer 与自定义 Shader。
        /// </summary>
        public void SetReceiveShadows(bool value)
        {
            if (receiveShadows == value)
            {
                return;
            }

            receiveShadows = value;
            ApplyShadowSettings();
            RefreshOwnerSurface();
        }

        /// <summary>
        /// 按 Surface 中的稳定顺序应用材质、Chunk 模式和法线偏移。
        /// </summary>
        public void ApplyRendererSettings(int layerIndex, float layerSpacing)
        {
            var tilemapRenderer = GetComponent<TilemapRenderer>();
            if (tilemapRenderer == null)
            {
                return;
            }

            tilemapRenderer.mode = TilemapRenderer.Mode.Chunk;
            var desiredMaterial = materialOverride != null
                ? materialOverride
                : GetDefaultMaterial();
            if (desiredMaterial != null && tilemapRenderer.sharedMaterial != desiredMaterial)
            {
                tilemapRenderer.sharedMaterial = desiredMaterial;
            }

            ApplyShadowSettings(tilemapRenderer);

            var localPosition = transform.localPosition;
            var normalOffset = Mathf.Max(0f, layerIndex * layerSpacing + additionalNormalOffset);
            localPosition.z = -normalOffset;
            if (transform.localPosition != localPosition)
            {
                transform.localPosition = localPosition;
            }
        }

        /// <summary>
        /// 组件启用时请求所属 Surface 恢复材质和最终图层顺序。
        /// </summary>
        private void OnEnable()
        {
            ApplyShadowSettings();
            RefreshOwnerSurface();
        }

        /// <summary>
        /// Inspector 修改图层类型、材质或偏移后刷新所属 Surface。
        /// </summary>
        private void OnValidate()
        {
            layerType = NormalizeLayerType(layerType);
            additionalNormalOffset = Mathf.Max(0f, additionalNormalOffset);
            ApplyShadowSettings();
            RefreshOwnerSurface();
        }

        /// <summary>
        /// 将图层配置同步给原生 Renderer 与自定义 Shader，旧场景加载时也不依赖 Surface 重建时机。
        /// </summary>
        private void ApplyShadowSettings()
        {
            var tilemapRenderer = GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null)
            {
                ApplyShadowSettings(tilemapRenderer);
            }
        }

        /// <summary>
        /// 写入接收阴影开关和材质属性块，保证 NativeTilemap 的自定义光照与 Renderer 行为一致。
        /// </summary>
        private void ApplyShadowSettings(TilemapRenderer tilemapRenderer)
        {
            if (tilemapRenderer.receiveShadows != receiveShadows)
            {
                tilemapRenderer.receiveShadows = receiveShadows;
            }

            if (rendererProperties == null)
            {
                rendererProperties = new MaterialPropertyBlock();
            }

            tilemapRenderer.GetPropertyBlock(rendererProperties);
            rendererProperties.SetFloat(
                ReceiveShadowsId,
                tilemapRenderer.receiveShadows ? 1f : 0f);
            tilemapRenderer.SetPropertyBlock(rendererProperties);
        }

        /// <summary>
        /// 将历史 Effect 类型归并到透明叠加渲染，避免它继续被误解为额外的 Tilemap 层级。
        /// </summary>
        private static TileMap3DLayerType NormalizeLayerType(TileMap3DLayerType value)
        {
            return value == TileMap3DLayerType.Effect
                ? TileMap3DLayerType.Overlay
                : value;
        }

        /// <summary>
        /// 组件销毁时释放仅在默认资源缺失时创建的临时材质。
        /// </summary>
        private void OnDestroy()
        {
            if (fallbackMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(fallbackMaterial);
            }
            else
            {
                DestroyImmediate(fallbackMaterial);
            }

            fallbackMaterial = null;
        }

        /// <summary>
        /// 优先复用 Resources 中的共享材质，缺失时创建当前图层专用的临时材质。
        /// </summary>
        private Material GetDefaultMaterial()
        {
            var isBaseLayer = LayerType == TileMap3DLayerType.Base;
            var resourcePath = isBaseLayer
                ? BaseMaterialResourcePath
                : OverlayMaterialResourcePath;
            var sharedMaterial = Resources.Load<Material>(resourcePath);
            if (sharedMaterial != null)
            {
                return sharedMaterial;
            }

            var shaderName = isBaseLayer ? BaseShaderName : OverlayShaderName;
            if (fallbackMaterial != null && fallbackMaterial.shader != null
                && fallbackMaterial.shader.name == shaderName)
            {
                return fallbackMaterial;
            }

            if (fallbackMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(fallbackMaterial);
                }
                else
                {
                    DestroyImmediate(fallbackMaterial);
                }
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                fallbackMaterial = null;
                return null;
            }

            fallbackMaterial = new Material(shader)
            {
                name = "TileMap3D_" + layerType + "_Fallback",
                hideFlags = HideFlags.HideAndDontSave
            };
            return fallbackMaterial;
        }

        /// <summary>
        /// 将设置变化交给所属 Surface 统一处理，独立图层则只应用自身默认状态。
        /// </summary>
        private void RefreshOwnerSurface()
        {
            var surface = GetComponentInParent<TileMap3DSurface>();
            if (surface != null)
            {
                // OnValidate 期间创建 SurfaceMaterial 覆盖对象会触发 Unity 的生命周期警告。
                // 统一交给 Surface 在安全的编辑器回调中重建。
                surface.RequestRebuild();
                return;
            }

            ApplyRendererSettings(0, 0f);
        }
    }
}
