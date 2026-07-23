using UnityEditor;
using UnityEngine;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 在编辑器中维护 TileMap3D 原生渲染和目标 Mesh 材质后端所需的共享材质资源。
    /// </summary>
    [InitializeOnLoad]
    internal static class TileMap3DDefaultAssets
    {
        private const string ResourcesFolder = "Assets/Plugins/Tilemap3D/Resources";
        private const string MaterialFolder = ResourcesFolder + "/TileMap3D";
        private const string BaseMaterialPath = MaterialFolder + "/TileMap3DBase.mat";
        private const string OverlayMaterialPath = MaterialFolder + "/TileMap3DOverlay.mat";
        private const string SurfaceMaterialPath = MaterialFolder + "/TileMap3DSurfaceMaterial.mat";
        private const string BaseShaderName = "TileMap3D/TilemapSurfaceCutout";
        private const string OverlayShaderName = "TileMap3D/TilemapSurfaceTransparent";
        private const string SurfaceShaderName = "TileMap3D/SurfaceMaterial";

        /// <summary>
        /// 域重载后延迟创建资源，避免 Shader 仍在导入时得到空引用。
        /// </summary>
        static TileMap3DDefaultAssets()
        {
            EditorApplication.delayCall += EnsureDefaultMaterials;
        }

        /// <summary>
        /// 创建或修复供 Runtime Resources.Load 使用的原生图层和目标 Mesh 覆盖材质。
        /// </summary>
        private static void EnsureDefaultMaterials()
        {
            var baseShader = Shader.Find(BaseShaderName);
            var overlayShader = Shader.Find(OverlayShaderName);
            var surfaceShader = Shader.Find(SurfaceShaderName);
            if (baseShader == null || overlayShader == null || surfaceShader == null)
            {
                return;
            }

            EnsureFolder("Assets/Plugins/Tilemap3D", "Resources");
            EnsureFolder(ResourcesFolder, "TileMap3D");
            var changed = EnsureMaterial(BaseMaterialPath, baseShader)
                | EnsureMaterial(OverlayMaterialPath, overlayShader)
                | EnsureMaterial(SurfaceMaterialPath, surfaceShader);
            if (changed)
            {
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// 保证父目录下存在指定子目录，不直接操作 Unity YAML 或 meta 文件。
        /// </summary>
        private static void EnsureFolder(string parentFolder, string childName)
        {
            var childPath = parentFolder + "/" + childName;
            if (!AssetDatabase.IsValidFolder(childPath))
            {
                AssetDatabase.CreateFolder(parentFolder, childName);
            }
        }

        /// <summary>
        /// 创建共享材质，或在 Shader 变化时更新已有资源并返回是否发生修改。
        /// </summary>
        private static bool EnsureMaterial(string assetPath, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, assetPath);
                return true;
            }

            if (material.shader == shader)
            {
                return false;
            }

            material.shader = shader;
            EditorUtility.SetDirty(material);
            return true;
        }
    }
}
