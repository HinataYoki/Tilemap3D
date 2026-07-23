using UnityEngine;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 标记 SurfaceMaterial 临时覆盖 Renderer 的所属 Surface，使其在当前编辑器生命周期内可被准确复用或清理。
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class TileMap3DSurfaceMaterialRendererOwner : MonoBehaviour
    {
        [SerializeField, HideInInspector] private TileMap3DSurface surface;

        public TileMap3DSurface Surface => surface;

        /// <summary>
        /// 绑定生成该覆盖 Renderer 的 Surface；只由 SurfaceMaterial 后端在安全重建阶段调用。
        /// </summary>
        internal void SetSurface(TileMap3DSurface value)
        {
            surface = value;
        }
    }
}
