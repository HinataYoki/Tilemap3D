using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 把原生 Tilemap 的最终 Sprite、颜色和变换合成为 TileMap3D 地面贴图。
    /// </summary>
    internal static class TileMap3DBaker
    {
        private const string BakeShaderName = "Hidden/TileMap3D/BakeSprite";
        private const string SurfaceShaderName = "TileMap3D/BakedSurface";
        private const int HighCompressionQuality = 90;
        private const int NormalCompressionQuality = 60;
        private const int LowCompressionQuality = 25;

        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// 烘焙指定 TileMap3D 地面，并保存贴图、材质和组件引用。
        /// </summary>
        public static TileMap3DBakeResult Bake(TileMap3DSurface surface)
        {
            if (surface == null || surface.SourceGrid == null)
            {
                return TileMap3DBakeResult.Failed("请先绑定包含 Tilemap 的 Grid。");
            }

            if (surface.SurfaceMode == TileMap3DSurfaceMode.Overlay)
            {
                return TileMap3DBakeResult.Failed(
                    "Overlay Surface 默认直接使用原生 TilemapRenderer，不生成可承载烘焙材质的地面 Mesh。");
            }

            var layers = CollectBakeLayers(surface);
            if (layers.Count == 0)
            {
                return TileMap3DBakeResult.Failed("当前 Grid 下没有启用且包含 Tile 的 Tilemap 图层。");
            }

            var bakeShader = Shader.Find(BakeShaderName);
            var surfaceShader = Shader.Find(SurfaceShaderName);
            if (bakeShader == null || surfaceShader == null)
            {
                return TileMap3DBakeResult.Failed("TileMap3D Shader 尚未导入，请先完成 Unity 编译。");
            }

            var bounds = surface.GetBakeBounds();
            var actualPixelsPerCell = CalculatePixelsPerCell(surface, bounds);
            var width = bounds.size.x * actualPixelsPerCell;
            var height = bounds.size.y * actualPixelsPerCell;
            var renderTexture = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            renderTexture.name = "TileMap3D_BakeTarget";
            renderTexture.filterMode = surface.BakeFilterMode;
            renderTexture.wrapMode = TextureWrapMode.Clamp;

            var commandBuffer = new CommandBuffer { name = "TileMap3D Bake" };
            var temporaryMeshes = new List<Mesh>();
            var temporaryMaterials = new List<Material>();
            Texture2D readableTexture = null;
            var previousActive = RenderTexture.active;
            var previousSrgbWrite = GL.sRGBWrite;
            var skippedSpriteCount = 0;

            try
            {
                var clearColor = surface.SurfaceColor;
                clearColor.a = 1f;
                var gridRect = surface.GetGridLocalBakeRect(bounds);
                commandBuffer.SetRenderTarget(renderTexture);
                commandBuffer.SetViewport(new Rect(0f, 0f, width, height));
                commandBuffer.ClearRenderTarget(false, true, clearColor);
                commandBuffer.SetViewProjectionMatrices(
                    Matrix4x4.identity,
                    Matrix4x4.Ortho(
                        gridRect.xMin,
                        gridRect.xMax,
                        gridRect.yMin,
                        gridRect.yMax,
                        -10f,
                        10f));

                for (var layerIndex = 0; layerIndex < layers.Count; layerIndex++)
                {
                    var layer = layers[layerIndex];
                    var layerMeshes = BuildLayerMeshes(
                        surface.SourceGrid,
                        layer,
                        bounds,
                        out var skippedInLayer);
                    skippedSpriteCount += skippedInLayer;
                    for (var meshIndex = 0; meshIndex < layerMeshes.Count; meshIndex++)
                    {
                        var layerMesh = layerMeshes[meshIndex];
                        var material = new Material(bakeShader)
                        {
                            name = "TileMap3D_Bake_" + layer.name,
                            hideFlags = HideFlags.HideAndDontSave
                        };
                        material.SetTexture(MainTextureId, layerMesh.Texture);
                        temporaryMeshes.Add(layerMesh.Mesh);
                        temporaryMaterials.Add(material);
                        commandBuffer.DrawMesh(layerMesh.Mesh, Matrix4x4.identity, material);
                    }
                }

                if (temporaryMeshes.Count == 0)
                {
                    return TileMap3DBakeResult.Failed("烘焙区域内没有可渲染的 Tile Sprite。");
                }

                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.ExecuteCommandBuffer(commandBuffer);
                RenderTexture.active = renderTexture;
                readableTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = surface.name + "_Baked_Readback"
                };
                readableTexture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                readableTexture.Apply(false, false);

                return SaveBakeAssets(
                    surface,
                    readableTexture,
                    surfaceShader,
                    bounds,
                    width,
                    height,
                    actualPixelsPerCell,
                    skippedSpriteCount,
                    layers.Count);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                return TileMap3DBakeResult.Failed("TileMap3D 烘焙失败：" + exception.Message);
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
                RenderTexture.active = previousActive;
                commandBuffer.Release();
                for (var i = 0; i < temporaryMaterials.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryMaterials[i]);
                }

                for (var i = 0; i < temporaryMeshes.Count; i++)
                {
                    UnityEngine.Object.DestroyImmediate(temporaryMeshes[i]);
                }

                if (readableTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(readableTexture);
                }

                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        /// <summary>
        /// 收集可见的原生 Tilemap，并按 Sorting Layer、Order 和层级顺序排列。
        /// </summary>
        private static List<Tilemap> CollectBakeLayers(TileMap3DSurface surface)
        {
            var sourceTilemaps = surface.GetSourceTilemaps(true);
            var layers = new List<Tilemap>(sourceTilemaps.Length);
            var hierarchyOrder = new Dictionary<Tilemap, int>(sourceTilemaps.Length);
            for (var i = 0; i < sourceTilemaps.Length; i++)
            {
                var tilemap = sourceTilemaps[i];
                if (tilemap == null || !tilemap.gameObject.activeInHierarchy || tilemap.GetUsedTilesCount() == 0)
                {
                    continue;
                }

                var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
                if (tilemapRenderer == null || !tilemapRenderer.enabled)
                {
                    continue;
                }

                hierarchyOrder[tilemap] = i;
                layers.Add(tilemap);
            }

            layers.Sort((left, right) =>
            {
                var leftRenderer = left.GetComponent<TilemapRenderer>();
                var rightRenderer = right.GetComponent<TilemapRenderer>();
                var sortingLayerComparison = SortingLayer.GetLayerValueFromID(leftRenderer.sortingLayerID)
                    .CompareTo(SortingLayer.GetLayerValueFromID(rightRenderer.sortingLayerID));
                if (sortingLayerComparison != 0)
                {
                    return sortingLayerComparison;
                }

                var orderComparison = leftRenderer.sortingOrder.CompareTo(rightRenderer.sortingOrder);
                return orderComparison != 0
                    ? orderComparison
                    : hierarchyOrder[left].CompareTo(hierarchyOrder[right]);
            });
            return layers;
        }

        /// <summary>
        /// 在最大纹理尺寸约束内计算实际每格像素，避免 GPU 或导入器拒绝输出。
        /// </summary>
        private static int CalculatePixelsPerCell(TileMap3DSurface surface, BoundsInt bounds)
        {
            var supportedMaximum = Mathf.Min(surface.MaximumTextureSize, SystemInfo.maxTextureSize);
            var maximumByWidth = Mathf.Max(1, supportedMaximum / bounds.size.x);
            var maximumByHeight = Mathf.Max(1, supportedMaximum / bounds.size.y);
            return Mathf.Max(1, Mathf.Min(surface.PixelsPerCell, maximumByWidth, maximumByHeight));
        }

        /// <summary>
        /// 为一个 Tilemap 按 Sprite 纹理分组生成合批 Mesh，并统计没有 Sprite 的 Tile。
        /// </summary>
        private static List<TileMap3DBakeMesh> BuildLayerMeshes(
            Grid sourceGrid,
            Tilemap tilemap,
            BoundsInt bakeBounds,
            out int skippedSpriteCount)
        {
            var groups = new List<TileMeshGroupBuilder>();
            var geometryCache = new Dictionary<Sprite, SpriteGeometry>();
            skippedSpriteCount = 0;
            tilemap.RefreshAllTiles();

            var cellBounds = tilemap.cellBounds;
            var minimumX = Mathf.Max(bakeBounds.xMin, cellBounds.xMin);
            var maximumX = Mathf.Min(bakeBounds.xMax, cellBounds.xMax);
            var minimumY = Mathf.Max(bakeBounds.yMin, cellBounds.yMin);
            var maximumY = Mathf.Min(bakeBounds.yMax, cellBounds.yMax);
            for (var y = minimumY; y < maximumY; y++)
            {
                for (var x = minimumX; x < maximumX; x++)
                {
                    var position = new Vector3Int(x, y, 0);
                    if (tilemap.GetTile(position) == null)
                    {
                        continue;
                    }

                    var sprite = tilemap.GetSprite(position);
                    if (sprite == null || sprite.texture == null)
                    {
                        skippedSpriteCount++;
                        continue;
                    }

                    if (!geometryCache.TryGetValue(sprite, out var geometry))
                    {
                        geometry = new SpriteGeometry(sprite);
                        geometryCache.Add(sprite, geometry);
                    }

                    var group = GetOrCreateGroup(groups, geometry.Texture);
                    AddTileGeometry(sourceGrid, tilemap, position, geometry, group);
                }
            }

            var result = new List<TileMap3DBakeMesh>(groups.Count);
            for (var i = 0; i < groups.Count; i++)
            {
                var group = groups[i];
                if (group.Vertices.Count > 0)
                {
                    result.Add(new TileMap3DBakeMesh(group.Texture, group.CreateMesh(tilemap.name, i)));
                }
            }

            return result;
        }

        /// <summary>
        /// 取得指定纹理的 Mesh 构建器；首次出现时保留插入顺序以稳定透明叠加。
        /// </summary>
        private static TileMeshGroupBuilder GetOrCreateGroup(
            List<TileMeshGroupBuilder> groups,
            Texture2D texture)
        {
            for (var i = 0; i < groups.Count; i++)
            {
                if (groups[i].Texture == texture)
                {
                    return groups[i];
                }
            }

            var group = new TileMeshGroupBuilder(texture);
            groups.Add(group);
            return group;
        }

        /// <summary>
        /// 把一个 Tile 的最终 Sprite 顶点、原生变换和颜色追加到 Grid 本地烘焙平面。
        /// </summary>
        private static void AddTileGeometry(
            Grid sourceGrid,
            Tilemap tilemap,
            Vector3Int position,
            SpriteGeometry geometry,
            TileMeshGroupBuilder group)
        {
            var vertexStart = group.Vertices.Count;
            var cellCenter = tilemap.GetCellCenterLocal(position);
            var cellTransform = tilemap.orientationMatrix * tilemap.GetTransformMatrix(position);
            var color = tilemap.color * tilemap.GetColor(position);
            for (var i = 0; i < geometry.Vertices.Length; i++)
            {
                var spriteVertex = geometry.Vertices[i];
                var transformed = cellTransform.MultiplyPoint3x4(
                    new Vector3(spriteVertex.x, spriteVertex.y, 0f));
                var tilemapLocal = cellCenter + transformed;
                var world = tilemap.transform.TransformPoint(tilemapLocal);
                var gridLocal = sourceGrid.transform.InverseTransformPoint(world);
                group.Vertices.Add(new Vector3(gridLocal.x, gridLocal.y, 0f));
                group.Uv.Add(geometry.Uv[i]);
                group.Colors.Add(color);
            }

            for (var i = 0; i < geometry.Triangles.Length; i++)
            {
                group.Triangles.Add(vertexStart + geometry.Triangles[i]);
            }
        }

        /// <summary>
        /// 将 GPU 合成结果保存为 PNG，配置压缩并创建或更新地面材质。
        /// </summary>
        private static TileMap3DBakeResult SaveBakeAssets(
            TileMap3DSurface surface,
            Texture2D readableTexture,
            Shader surfaceShader,
            BoundsInt bounds,
            int width,
            int height,
            int actualPixelsPerCell,
            int skippedSpriteCount,
            int layerCount)
        {
            var outputFolder = surface.OutputFolder.Replace('\\', '/').TrimEnd('/');
            if (!outputFolder.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return TileMap3DBakeResult.Failed("烘焙输出目录必须位于 Assets 下。");
            }

            EnsureAssetFolder(outputFolder);
            Undo.RecordObject(surface, "烘焙 TileMap3D 地面");
            var bakeId = surface.EnsureBakeId();
            var shortId = bakeId.Length > 8 ? bakeId.Substring(0, 8) : bakeId;
            var baseName = SanitizeFileName(surface.name) + "_" + shortId;
            var texturePath = outputFolder + "/" + baseName + "_Albedo.png";
            var materialPath = outputFolder + "/" + baseName + "_Surface.mat";
            File.WriteAllBytes(Path.GetFullPath(texturePath), readableTexture.EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.alphaIsTransparency = false;
                importer.mipmapEnabled = surface.GenerateMipMaps;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = surface.BakeFilterMode;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = surface.MaximumTextureSize;
                ApplyCompression(importer, surface.TextureCompression);
                importer.SaveAndReimport();
            }

            var bakedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (bakedTexture == null)
            {
                return TileMap3DBakeResult.Failed("烘焙贴图写入后无法重新导入：" + texturePath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(surfaceShader) { name = baseName + "_Surface" };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            else
            {
                material.shader = surfaceShader;
            }

            material.SetTexture(BaseMapId, bakedTexture);
            material.SetColor(BaseColorId, Color.white);
            EditorUtility.SetDirty(material);
            surface.ApplyBakeOutput(bakedTexture, material, bounds, bakeId);
            EditorUtility.SetDirty(surface);
            if (surface.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
            }

            AssetDatabase.SaveAssets();
            var warnings = new List<string>();
            if (actualPixelsPerCell < surface.PixelsPerCell)
            {
                warnings.Add("受最大贴图尺寸限制，实际降为每格 " + actualPixelsPerCell + " 像素");
            }

            if (skippedSpriteCount > 0)
            {
                warnings.Add(skippedSpriteCount + " 个 Tile 没有静态 Sprite，已跳过");
            }

            return TileMap3DBakeResult.Succeeded(
                texturePath,
                materialPath,
                width,
                height,
                layerCount,
                string.Join("；", warnings));
        }

        /// <summary>
        /// 将 TileMap3D 压缩选项映射为 Unity TextureImporter 设置。
        /// </summary>
        private static void ApplyCompression(
            TextureImporter importer,
            TileMap3DTextureCompression compression)
        {
            switch (compression)
            {
                case TileMap3DTextureCompression.Uncompressed:
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.compressionQuality = HighCompressionQuality;
                    break;
                case TileMap3DTextureCompression.LowQuality:
                    importer.textureCompression = TextureImporterCompression.CompressedLQ;
                    importer.compressionQuality = LowCompressionQuality;
                    break;
                case TileMap3DTextureCompression.NormalQuality:
                    importer.textureCompression = TextureImporterCompression.Compressed;
                    importer.compressionQuality = NormalCompressionQuality;
                    break;
                default:
                    importer.textureCompression = TextureImporterCompression.CompressedHQ;
                    importer.compressionQuality = HighCompressionQuality;
                    break;
            }
        }

        /// <summary>
        /// 逐级创建 Assets 内的输出目录，保证 AssetDatabase 能正确导入产物。
        /// </summary>
        private static void EnsureAssetFolder(string assetFolder)
        {
            var parts = assetFolder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>
        /// 移除场景对象名中不允许出现在文件名里的字符。
        /// </summary>
        private static string SanitizeFileName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                builder.Append(Array.IndexOf(invalidCharacters, value[i]) >= 0 ? '_' : value[i]);
            }

            return builder.Length > 0 ? builder.ToString() : "TileMap3D";
        }

        /// <summary>
        /// 缓存一个 Sprite 的静态网格数据，避免大地图对相同切片反复分配数组。
        /// </summary>
        private sealed class SpriteGeometry
        {
            public readonly Texture2D Texture;
            public readonly Vector2[] Vertices;
            public readonly Vector2[] Uv;
            public readonly ushort[] Triangles;

            /// <summary>
            /// 从 Unity Sprite 读取可直接用于 Tilemap 合成的紧凑网格。
            /// </summary>
            public SpriteGeometry(Sprite sprite)
            {
                Texture = sprite.texture;
                Vertices = sprite.vertices;
                Uv = sprite.uv;
                Triangles = sprite.triangles;
            }
        }

        /// <summary>
        /// 累积同一 Sprite 纹理的 Tile 顶点，降低编辑器烘焙 Draw Call。
        /// </summary>
        private sealed class TileMeshGroupBuilder
        {
            public readonly Texture2D Texture;
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector2> Uv = new List<Vector2>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<int> Triangles = new List<int>();

            /// <summary>
            /// 创建指定纹理对应的 Mesh 构建缓存。
            /// </summary>
            public TileMeshGroupBuilder(Texture2D texture)
            {
                Texture = texture;
            }

            /// <summary>
            /// 将累积数据转换为一次性烘焙 Mesh。
            /// </summary>
            public Mesh CreateMesh(string layerName, int groupIndex)
            {
                var mesh = new Mesh
                {
                    name = "TileMap3D_Bake_" + layerName + "_" + groupIndex,
                    hideFlags = HideFlags.HideAndDontSave,
                    indexFormat = Vertices.Count > ushort.MaxValue
                        ? IndexFormat.UInt32
                        : IndexFormat.UInt16
                };
                mesh.SetVertices(Vertices);
                mesh.SetUVs(0, Uv);
                mesh.SetColors(Colors);
                mesh.SetTriangles(Triangles, 0, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        /// 关联一次临时烘焙 Mesh 与其 Sprite 纹理。
        /// </summary>
        private sealed class TileMap3DBakeMesh
        {
            public readonly Texture2D Texture;
            public readonly Mesh Mesh;

            /// <summary>
            /// 保存命令缓冲绘制所需的 Mesh 与纹理。
            /// </summary>
            public TileMap3DBakeMesh(Texture2D texture, Mesh mesh)
            {
                Texture = texture;
                Mesh = mesh;
            }
        }
    }

    /// <summary>
    /// 描述一次 TileMap3D 烘焙的持久化结果。
    /// </summary>
    internal sealed class TileMap3DBakeResult
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }
        public string Warning { get; private set; }
        public string TexturePath { get; private set; }
        public string MaterialPath { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int LayerCount { get; private set; }

        /// <summary>
        /// 创建成功结果并记录输出路径、尺寸、图层数和降级提示。
        /// </summary>
        public static TileMap3DBakeResult Succeeded(
            string texturePath,
            string materialPath,
            int width,
            int height,
            int layerCount,
            string warning)
        {
            return new TileMap3DBakeResult
            {
                Success = true,
                TexturePath = texturePath,
                MaterialPath = materialPath,
                Width = width,
                Height = height,
                LayerCount = layerCount,
                Warning = warning
            };
        }

        /// <summary>
        /// 创建失败结果，供工作台显示明确原因。
        /// </summary>
        public static TileMap3DBakeResult Failed(string error)
        {
            return new TileMap3DBakeResult
            {
                Success = false,
                Error = error
            };
        }
    }
}
