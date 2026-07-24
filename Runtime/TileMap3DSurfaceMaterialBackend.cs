using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 把原生 Tilemap 的 Cell 数据转换为索引纹理，并在目标 Mesh 同几何上执行覆盖材质 pass。
    /// </summary>
    internal sealed class TileMap3DSurfaceMaterialBackend
    {
        private const string ShaderName = "TileMap3D/SurfaceMaterial";
        private const string MaterialResourcePath = "TileMap3D/TileMap3DSurfaceMaterial";
        private const string RendererObjectName = "TileMap3D Surface Material Renderer";
        private const int MaximumLayers = 8;
        private const int MaximumSourceTextures = 8;
        private const int MaximumSpriteIndex = ushort.MaxValue;
        private const int SpriteLookupRowCount = 3;
        private const int DataTextureCount = 3;
        private const int BytesPerRgba32Pixel = 4;
        private const int CpuAndGpuCopies = 2;
        private const long MaximumDataMemoryBytes = 256L * 1024L * 1024L;
        private const float DefaultNormalThreshold = 0.9f;
        private const float MinimumPlaneTolerance = 0.001f;

        private static readonly int CellDataId = Shader.PropertyToID("_CellData");
        private static readonly int TransformDataId = Shader.PropertyToID("_TransformData");
        private static readonly int ColorDataId = Shader.PropertyToID("_ColorData");
        private static readonly int SpriteLookupId = Shader.PropertyToID("_SpriteLookup");
        private static readonly int SurfaceWorldToLocalId = Shader.PropertyToID("_SurfaceWorldToLocal");
        private static readonly int SurfaceRectId = Shader.PropertyToID("_SurfaceRect");
        private static readonly int SurfaceNormalId = Shader.PropertyToID("_SurfaceNormalWS");
        private static readonly int CellDimensionsId = Shader.PropertyToID("_CellDimensions");
        private static readonly int PlaneToleranceId = Shader.PropertyToID("_PlaneTolerance");
        private static readonly int NormalThresholdId = Shader.PropertyToID("_NormalThreshold");
        private static readonly int SpriteCountId = Shader.PropertyToID("_SpriteCount");
        private static readonly int ReceiveShadowsId = Shader.PropertyToID(
            "_TileMap3DReceiveShadows");

        private readonly TileMap3DSurface surface;
        private readonly List<AnimatedCell> animatedCells = new List<AnimatedCell>();

        private GameObject rendererObject;
        private MeshFilter backendMeshFilter;
        private MeshRenderer backendRenderer;
        private MeshFilter targetMeshFilter;
        private MeshRenderer targetRenderer;
        private Material material;
        private Texture2DArray cellData;
        private Texture2DArray transformData;
        private Texture2DArray colorData;
        private Texture2D spriteLookup;
        private Color32[][] cellPixels;
        private int cellColumns;
        private int cellRows;
        private int layerCount;
        private int spriteCount;

        public string Warning { get; private set; }
        public bool IsActive => backendRenderer != null && backendRenderer.enabled;

        /// <summary>
        /// 绑定所属 Surface；后端资源由 Rebuild 和 Dispose 显式管理。
        /// </summary>
        public TileMap3DSurfaceMaterialBackend(TileMap3DSurface owner)
        {
            surface = owner;
        }

        /// <summary>
        /// 从当前 Tilemap 全量重建 Cell 索引、Sprite 查询表和目标 Mesh 覆盖渲染器。
        /// </summary>
        public bool Rebuild()
        {
            Warning = string.Empty;
            ReleaseDataResources();
            if (!SystemInfo.supports2DArrayTextures)
            {
                return Fail("当前图形平台不支持 Texture2DArray，已回退原生 TilemapRenderer。");
            }

            if (!SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf))
            {
                return Fail("当前图形平台不支持 RGBAHalf Sprite 查询纹理，已回退原生 TilemapRenderer。");
            }

            var target = surface.transform.parent;
            if (target == null)
            {
                return Fail("Surface 没有直接父物体，无法取得目标 Mesh。");
            }

            targetMeshFilter = target.GetComponent<MeshFilter>();
            targetRenderer = target.GetComponent<MeshRenderer>();
            if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null
                || targetRenderer == null)
            {
                return Fail("直接父物体需要 MeshFilter、MeshRenderer 和有效 Mesh。");
            }

            var bounds = surface.GetBakeBounds();
            var columns = bounds.size.x;
            var rows = bounds.size.y;
            if (columns > SystemInfo.maxTextureSize || rows > SystemInfo.maxTextureSize)
            {
                return Fail(
                    "Cell 区域 " + columns + " × " + rows
                    + " 超过当前平台索引纹理上限 " + SystemInfo.maxTextureSize + "。");
            }

            var tilemaps = CollectRenderableLayers();
            if (tilemaps.Count > MaximumLayers)
            {
                return Fail(
                    "SurfaceMaterial 当前最多支持 " + MaximumLayers
                    + " 个可见图层，当前为 " + tilemaps.Count + " 个。");
            }

            layerCount = tilemaps.Count;
            var textureDepth = Mathf.Max(1, layerCount);
            var cellCountLong = (long)columns * rows;
            var estimatedDataBytes = cellCountLong * textureDepth * DataTextureCount
                * BytesPerRgba32Pixel * CpuAndGpuCopies;
            if (estimatedDataBytes > MaximumDataMemoryBytes)
            {
                return Fail(
                    "Cell 区域 " + columns + " × " + rows + " × " + textureDepth
                    + " 层预计需要 " + FormatMemorySize(estimatedDataBytes)
                    + " 的 SurfaceMaterial 数据，超过 " + FormatMemorySize(MaximumDataMemoryBytes)
                    + " 上限，已回退原生 TilemapRenderer。");
            }

            cellPixels = new Color32[textureDepth][];
            var transformPixels = new Color32[textureDepth][];
            var colorPixels = new Color32[textureDepth][];
            var cellCount = (int)cellCountLong;
            for (var layerIndex = 0; layerIndex < textureDepth; layerIndex++)
            {
                cellPixels[layerIndex] = new Color32[cellCount];
                transformPixels[layerIndex] = CreateIdentityTransformPixels(cellCount);
                colorPixels[layerIndex] = new Color32[cellCount];
            }

            var spriteIndices = new Dictionary<Sprite, int>();
            var sprites = new List<Sprite> { null };
            var textureSlots = new Dictionary<Texture2D, int>();
            var sourceTextures = new List<Texture2D>();
            animatedCells.Clear();
            for (var layerIndex = 0; layerIndex < tilemaps.Count; layerIndex++)
            {
                if (!BuildLayerData(
                        tilemaps[layerIndex],
                        layerIndex,
                        bounds,
                        columns,
                        spriteIndices,
                        sprites,
                        textureSlots,
                        sourceTextures,
                        transformPixels[layerIndex],
                        colorPixels[layerIndex]))
                {
                    return false;
                }
            }

            if (!CreateDataTextures(columns, rows, textureDepth, transformPixels, colorPixels)
                || !CreateSpriteLookup(sprites, textureSlots))
            {
                return false;
            }

            if (!CreateMaterial(sourceTextures)
                || !EnsureRendererObject(target, targetMeshFilter.sharedMesh))
            {
                return false;
            }

            cellColumns = columns;
            cellRows = rows;
            spriteCount = sprites.Count;
            ApplyMaterialProperties(cellColumns, cellRows, spriteCount);
            ApplyRendererMaterials(targetMeshFilter.sharedMesh.subMeshCount);
            UpdateRendererState();
            return true;
        }

        /// <summary>
        /// 同步目标 Transform、Renderer 状态和 AnimatedTile；编辑态同时恢复可能被场景保存清理的材质参数。
        /// </summary>
        public void Update()
        {
            if (material == null || backendRenderer == null)
            {
                return;
            }

            if (targetMeshFilter == null || targetMeshFilter.sharedMesh == null
                || targetRenderer == null)
            {
                backendRenderer.enabled = false;
                return;
            }

            if (backendMeshFilter.sharedMesh != targetMeshFilter.sharedMesh)
            {
                Rebuild();
                return;
            }

            if (Application.isPlaying)
            {
                ApplySurfaceTransformProperties();
            }
            else
            {
                ApplyMaterialProperties(cellColumns, cellRows, spriteCount);
            }

            UpdateAnimatedCells();
            UpdateRendererState();
        }

        /// <summary>
        /// Surface 或父层级停用时释放 GPU 数据并关闭 Renderer，但保留隐藏对象供再次启用复用。
        /// </summary>
        public void Suspend()
        {
            ReleaseDataResources();
            Warning = string.Empty;
        }

        /// <summary>
        /// 释放隐藏渲染对象、材质和所有按 Surface 创建的非持久化纹理。
        /// </summary>
        public void Dispose()
        {
            ReleaseDataResources();
            ReleaseRendererObject();
            rendererObject = null;
            backendMeshFilter = null;
            backendRenderer = null;
            targetMeshFilter = null;
            targetRenderer = null;
            Warning = string.Empty;
        }

        /// <summary>
        /// 清理指定 Surface 在当前编辑器生命周期内留下的临时覆盖 Renderer。
        /// </summary>
        internal static void ReleaseRendererObjectsOwnedBy(TileMap3DSurface owner)
        {
            if (owner == null || owner.transform.parent == null)
            {
                return;
            }

            var target = owner.transform.parent;
            for (var i = target.childCount - 1; i >= 0; i--)
            {
                var child = target.GetChild(i);
                var rendererOwner = child.GetComponent<TileMap3DSurfaceMaterialRendererOwner>();
                if (rendererOwner != null && rendererOwner.Surface == owner)
                {
                    ReleaseObject(child.gameObject);
                }
            }
        }

        /// <summary>
        /// 收集当前启用的 Tilemap 图层，保持 Hierarchy 顺序作为材质合成顺序。
        /// </summary>
        private List<Tilemap> CollectRenderableLayers()
        {
            var result = new List<Tilemap>();
            var tilemaps = surface.GetSourceTilemaps(true);
            for (var i = 0; i < tilemaps.Length; i++)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null || !tilemap.gameObject.activeSelf || !tilemap.enabled)
                {
                    continue;
                }

                var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
                if (tilemapRenderer == null || !tilemapRenderer.enabled)
                {
                    continue;
                }

                result.Add(tilemap);
            }

            return result;
        }

        /// <summary>
        /// 解析单层 Tile 的最终 Sprite、颜色、变换和动画帧，并写入 CPU 索引数组。
        /// </summary>
        private bool BuildLayerData(
            Tilemap tilemap,
            int layerIndex,
            BoundsInt bounds,
            int columns,
            Dictionary<Sprite, int> spriteIndices,
            List<Sprite> sprites,
            Dictionary<Texture2D, int> textureSlots,
            List<Texture2D> sourceTextures,
            Color32[] transformPixels,
            Color32[] colorPixels)
        {
            var iTilemap = new ITilemap(tilemap);
            for (var y = 0; y < bounds.size.y; y++)
            {
                for (var x = 0; x < bounds.size.x; x++)
                {
                    var cell = new Vector3Int(bounds.xMin + x, bounds.yMin + y, 0);
                    var tile = tilemap.GetTile(cell);
                    if (tile == null)
                    {
                        continue;
                    }

                    var sprite = tilemap.GetSprite(cell);
                    var animationData = default(TileAnimationData);
                    var hasAnimation = tile.GetTileAnimationData(cell, iTilemap, ref animationData)
                        && animationData.animatedSprites != null
                        && animationData.animatedSprites.Length > 0;
                    if (hasAnimation)
                    {
                        sprite = animationData.animatedSprites[0];
                    }

                    if (!TryRegisterSprite(
                            sprite,
                            spriteIndices,
                            sprites,
                            textureSlots,
                            sourceTextures,
                            out var spriteIndex))
                    {
                        return false;
                    }

                    if (spriteIndex == 0)
                    {
                        continue;
                    }

                    var pixelIndex = y * columns + x;
                    cellPixels[layerIndex][pixelIndex] = EncodeSpriteIndex(spriteIndex);
                    transformPixels[pixelIndex] = EncodeTransform(tilemap.GetTransformMatrix(cell));
                    colorPixels[pixelIndex] = tilemap.GetColor(cell) * tilemap.color;
                    if (hasAnimation && !TryRegisterAnimation(
                            tilemap,
                            layerIndex,
                            pixelIndex,
                            animationData,
                            spriteIndices,
                            sprites,
                            textureSlots,
                            sourceTextures))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// 注册 AnimatedTile 的全部 Sprite 帧，并记录运行时只更新索引所需的数据。
        /// </summary>
        private bool TryRegisterAnimation(
            Tilemap tilemap,
            int layerIndex,
            int pixelIndex,
            TileAnimationData animationData,
            Dictionary<Sprite, int> spriteIndices,
            List<Sprite> sprites,
            Dictionary<Texture2D, int> textureSlots,
            List<Texture2D> sourceTextures)
        {
            var frameIndices = new int[animationData.animatedSprites.Length];
            for (var i = 0; i < animationData.animatedSprites.Length; i++)
            {
                if (!TryRegisterSprite(
                        animationData.animatedSprites[i],
                        spriteIndices,
                        sprites,
                        textureSlots,
                        sourceTextures,
                        out frameIndices[i]))
                {
                    return false;
                }
            }

            animatedCells.Add(new AnimatedCell
            {
                layerIndex = layerIndex,
                pixelIndex = pixelIndex,
                frameIndices = frameIndices,
                animationSpeed = animationData.animationSpeed,
                animationStartTime = animationData.animationStartTime,
                animationFrameRate = Mathf.Max(0.01f, tilemap.animationFrameRate),
                flags = animationData.flags,
                currentFrame = 0
            });
            return true;
        }

        /// <summary>
        /// 为 Sprite 分配稳定的 16-bit 索引和最多八张源纹理槽位。
        /// </summary>
        private bool TryRegisterSprite(
            Sprite sprite,
            Dictionary<Sprite, int> spriteIndices,
            List<Sprite> sprites,
            Dictionary<Texture2D, int> textureSlots,
            List<Texture2D> sourceTextures,
            out int spriteIndex)
        {
            if (sprite == null)
            {
                spriteIndex = 0;
                return true;
            }

            if (spriteIndices.TryGetValue(sprite, out spriteIndex))
            {
                return true;
            }

            if (sprite.packed && (sprite.packingMode == SpritePackingMode.Tight
                    || sprite.packingRotation != SpritePackingRotation.None))
            {
                return Fail(
                    "Sprite “" + sprite.name
                    + "” 使用旋转或 Tight SpriteAtlas 打包，SurfaceMaterial 当前无法保持其 UV。");
            }

            var texture = sprite.texture;
            if (texture == null)
            {
                return Fail("Sprite “" + sprite.name + "” 没有可采样的源纹理。");
            }

            if (!textureSlots.ContainsKey(texture))
            {
                if (sourceTextures.Count >= MaximumSourceTextures)
                {
                    return Fail(
                        "单个 SurfaceMaterial 最多支持 " + MaximumSourceTextures
                        + " 张源纹理，请拆分 Surface 或使用 SpriteAtlas。");
                }

                textureSlots.Add(texture, sourceTextures.Count);
                sourceTextures.Add(texture);
            }

            if (sprites.Count >= MaximumSpriteIndex
                || sprites.Count >= SystemInfo.maxTextureSize)
            {
                return Fail("单个 Surface 的唯一 Sprite 数量超过当前 SpriteLookup 上限。");
            }

            spriteIndex = sprites.Count;
            spriteIndices.Add(sprite, spriteIndex);
            sprites.Add(sprite);
            return true;
        }

        /// <summary>
        /// 创建三个按格 Texture2DArray，并上传 Sprite 索引、变换和颜色数据。
        /// </summary>
        private bool CreateDataTextures(
            int columns,
            int rows,
            int textureDepth,
            Color32[][] transformPixels,
            Color32[][] colorPixels)
        {
            cellData = CreateDataTexture(columns, rows, textureDepth, "TileMap3D Cell Data");
            transformData = CreateDataTexture(columns, rows, textureDepth, "TileMap3D Transform Data");
            colorData = CreateDataTexture(columns, rows, textureDepth, "TileMap3D Color Data");
            if (cellData == null || transformData == null || colorData == null)
            {
                return Fail("无法创建 SurfaceMaterial Cell 数据纹理。");
            }

            for (var layerIndex = 0; layerIndex < textureDepth; layerIndex++)
            {
                cellData.SetPixels32(cellPixels[layerIndex], layerIndex);
                transformData.SetPixels32(transformPixels[layerIndex], layerIndex);
                colorData.SetPixels32(colorPixels[layerIndex], layerIndex);
            }

            cellData.Apply(false, false);
            transformData.Apply(false, false);
            colorData.Apply(false, false);
            return true;
        }

        /// <summary>
        /// 为 Cell 数据创建 Point/Clamp、线性采样且不进入场景序列化的 Texture2DArray。
        /// </summary>
        private static Texture2DArray CreateDataTexture(
            int width,
            int height,
            int depth,
            string textureName)
        {
            var texture = new Texture2DArray(
                width,
                height,
                depth,
                TextureFormat.RGBA32,
                false,
                true)
            {
                name = textureName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            return texture;
        }

        /// <summary>
        /// 创建 Sprite 索引到 UV Rect、源纹理槽位和原生几何边界的查询纹理。
        /// </summary>
        private bool CreateSpriteLookup(
            List<Sprite> sprites,
            Dictionary<Texture2D, int> textureSlots)
        {
            spriteLookup = new Texture2D(
                Mathf.Max(1, sprites.Count),
                SpriteLookupRowCount,
                TextureFormat.RGBAHalf,
                false,
                true)
            {
                name = "TileMap3D Sprite Lookup",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (var spriteIndex = 1; spriteIndex < sprites.Count; spriteIndex++)
            {
                var sprite = sprites[spriteIndex];
                var texture = sprite.texture;
                var textureRect = sprite.textureRect;
                var minimumU = (textureRect.xMin + 0.5f) / texture.width;
                var minimumV = (textureRect.yMin + 0.5f) / texture.height;
                var maximumU = (textureRect.xMax - 0.5f) / texture.width;
                var maximumV = (textureRect.yMax - 0.5f) / texture.height;
                spriteLookup.SetPixel(
                    spriteIndex,
                    0,
                    new Color(minimumU, minimumV, maximumU, maximumV));
                spriteLookup.SetPixel(
                    spriteIndex,
                    1,
                    new Color(textureSlots[texture], 0f, 0f, 0f));
                spriteLookup.SetPixel(
                    spriteIndex,
                    2,
                    CalculateNormalizedSpriteGeometry(
                        sprite.vertices,
                        GetSpriteCellSize(sprite),
                        GetNormalizedSpritePivot(sprite)));
            }

            spriteLookup.Apply(false, false);
            return true;
        }

        /// <summary>
        /// 把原生 Sprite 顶点范围归一化到完整 Cell，避免 Tight Mesh 的裁切区域被铺满后放大。
        /// </summary>
        private static Vector4 CalculateNormalizedSpriteGeometry(
            Vector2[] vertices,
            Vector2 sourceCellSize,
            Vector2 normalizedPivot)
        {
            if (vertices == null || vertices.Length == 0)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            var minimum = vertices[0];
            var maximum = vertices[0];
            for (var i = 1; i < vertices.Length; i++)
            {
                minimum = Vector2.Min(minimum, vertices[i]);
                maximum = Vector2.Max(maximum, vertices[i]);
            }

            var sourceWidth = Mathf.Max(MinimumPlaneTolerance, Mathf.Abs(sourceCellSize.x));
            var sourceHeight = Mathf.Max(MinimumPlaneTolerance, Mathf.Abs(sourceCellSize.y));
            var normalizedMinimum = new Vector2(
                minimum.x / sourceWidth + normalizedPivot.x,
                minimum.y / sourceHeight + normalizedPivot.y);
            var normalizedMaximum = new Vector2(
                maximum.x / sourceWidth + normalizedPivot.x,
                maximum.y / sourceHeight + normalizedPivot.y);
            if (normalizedMaximum.x - normalizedMinimum.x < MinimumPlaneTolerance
                || normalizedMaximum.y - normalizedMinimum.y < MinimumPlaneTolerance)
            {
                return new Vector4(0f, 0f, 1f, 1f);
            }

            return new Vector4(
                Mathf.Clamp01(normalizedMinimum.x),
                Mathf.Clamp01(normalizedMinimum.y),
                Mathf.Clamp01(normalizedMaximum.x),
                Mathf.Clamp01(normalizedMaximum.y));
        }

        /// <summary>
        /// 返回 Sprite 完整 Rect 对应的世界尺寸；不能使用 Tight Mesh 自身的 Bounds，否则会丢失透明边界。
        /// </summary>
        private static Vector2 GetSpriteCellSize(Sprite sprite)
        {
            return new Vector2(
                sprite.rect.width / Mathf.Max(MinimumPlaneTolerance, sprite.pixelsPerUnit),
                sprite.rect.height / Mathf.Max(MinimumPlaneTolerance, sprite.pixelsPerUnit));
        }

        /// <summary>
        /// 将 Sprite Pivot 归一化到完整 Rect，供紧凑网格顶点恢复其在 Cell 中的实际偏移。
        /// </summary>
        private static Vector2 GetNormalizedSpritePivot(Sprite sprite)
        {
            return new Vector2(
                sprite.pivot.x / Mathf.Max(1f, sprite.rect.width),
                sprite.pivot.y / Mathf.Max(1f, sprite.rect.height));
        }

        /// <summary>
        /// 格式化 GPU 与 CPU 纹理总占用预估，便于用户调整固定区域或切换后端。
        /// </summary>
        private static string FormatMemorySize(long bytes)
        {
            return (bytes / (1024f * 1024f)).ToString("0.0") + " MiB";
        }

        /// <summary>
        /// 从 Resources 模板或 Shader 创建当前 Surface 专属材质并绑定全部 GPU 数据。
        /// </summary>
        private bool CreateMaterial(List<Texture2D> sourceTextures)
        {
            var template = Resources.Load<Material>(MaterialResourcePath);
            if (template != null)
            {
                material = new Material(template);
            }
            else
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    return Fail("找不到 Shader “" + ShaderName + "”。");
                }

                material = new Material(shader);
            }

            material.name = "TileMap3D Surface Material Runtime";
            material.hideFlags = HideFlags.HideAndDontSave;
            material.SetTexture(CellDataId, cellData);
            material.SetTexture(TransformDataId, transformData);
            material.SetTexture(ColorDataId, colorData);
            material.SetTexture(SpriteLookupId, spriteLookup);
            for (var textureIndex = 0; textureIndex < MaximumSourceTextures; textureIndex++)
            {
                var texture = textureIndex < sourceTextures.Count
                    ? sourceTextures[textureIndex]
                    : Texture2D.whiteTexture;
                material.SetTexture("_TileTexture" + textureIndex, texture);
            }

            return true;
        }

        /// <summary>
        /// 创建与目标 Mesh 完全重合的隐藏覆盖渲染器，不复制或替换目标材质和 Collider。
        /// </summary>
        private bool EnsureRendererObject(Transform target, Mesh targetMesh)
        {
            if (rendererObject == null)
            {
                // 旧版本会把覆盖 Renderer 写入场景。仅修改 HideFlags 不会删除旧 YAML，
                // 因此新后端首次接管时必须销毁旧对象，再创建真正不参与保存的临时对象。
                var previousRendererObject = FindRendererObject(target);
                if (previousRendererObject != null)
                {
#if UNITY_EDITOR
                    var requiresSceneMigration = !Application.isPlaying
                        && (previousRendererObject.hideFlags & HideFlags.DontSaveInEditor) == 0;
#endif
                    ReleaseObject(previousRendererObject);
#if UNITY_EDITOR
                    if (requiresSceneMigration
                        && surface.gameObject.scene.IsValid()
                        && surface.gameObject.scene.isLoaded)
                    {
                        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
                    }
#endif
                }
            }

            if (rendererObject == null || rendererObject.transform.parent != target)
            {
                if (rendererObject != null)
                {
                    ReleaseObject(rendererObject);
                }

                rendererObject = new GameObject(RendererObjectName)
                {
                    // 覆盖 Renderer 引用运行时材质，必须整体避开场景保存，否则 Unity 会清空非 Properties 参数。
                    hideFlags = HideFlags.HideAndDontSave,
                    layer = target.gameObject.layer
                };
                rendererObject.transform.SetParent(target, false);
                rendererObject.transform.localPosition = Vector3.zero;
                rendererObject.transform.localRotation = Quaternion.identity;
                rendererObject.transform.localScale = Vector3.one;
            }

            rendererObject.hideFlags = HideFlags.HideAndDontSave;
            var rendererOwner = rendererObject.GetComponent<TileMap3DSurfaceMaterialRendererOwner>();
            if (rendererOwner == null)
            {
                rendererOwner = rendererObject.AddComponent<TileMap3DSurfaceMaterialRendererOwner>();
            }

            rendererOwner.SetSurface(surface);
            backendMeshFilter = rendererObject.GetComponent<MeshFilter>();
            if (backendMeshFilter == null)
            {
                backendMeshFilter = rendererObject.AddComponent<MeshFilter>();
            }

            backendRenderer = rendererObject.GetComponent<MeshRenderer>();
            if (backendRenderer == null)
            {
                backendRenderer = rendererObject.AddComponent<MeshRenderer>();
            }

            if (backendMeshFilter == null || backendRenderer == null)
            {
                return Fail("无法创建目标 Mesh 的 SurfaceMaterial 覆盖渲染器。");
            }

            backendMeshFilter.sharedMesh = targetMesh;
            backendRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backendRenderer.receiveShadows = targetRenderer.receiveShadows;
            backendRenderer.lightProbeUsage = LightProbeUsage.Off;
            backendRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            backendRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return true;
        }

        /// <summary>
        /// 从目标直接子物体中找回本 Surface 在当前编辑器生命周期内创建的覆盖 Renderer。
        /// </summary>
        private GameObject FindRendererObject(Transform target)
        {
            if (target == null)
            {
                return null;
            }

            for (var i = 0; i < target.childCount; i++)
            {
                var child = target.GetChild(i);
                var rendererOwner = child.GetComponent<TileMap3DSurfaceMaterialRendererOwner>();
                if (rendererOwner != null && rendererOwner.Surface == surface)
                {
                    return child.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 释放当前或仍可从目标层级找回的临时覆盖 Renderer，并清空关联组件缓存。
        /// </summary>
        private void ReleaseRendererObject()
        {
            if (rendererObject == null)
            {
                rendererObject = FindRendererObject(surface.transform.parent);
            }

            if (rendererObject != null)
            {
                ReleaseObject(rendererObject);
            }
        }

        /// <summary>
        /// 为目标 Mesh 的每个 SubMesh 绑定同一个透明覆盖材质，保证所有共面区域可被 Shader 判定。
        /// </summary>
        private void ApplyRendererMaterials(int subMeshCount)
        {
            var materials = new Material[Mathf.Max(1, subMeshCount)];
            for (var i = 0; i < materials.Length; i++)
            {
                materials[i] = material;
            }

            backendRenderer.sharedMaterials = materials;
        }

        /// <summary>
        /// 写入固定区域、Cell 尺寸、图层数和 Surface 世界变换参数。
        /// </summary>
        private void ApplyMaterialProperties(int columns, int rows, int spriteCount)
        {
            var size = surface.GroundSize;
            material.SetVector(
                SurfaceRectId,
                new Vector4(-size.x * 0.5f, -size.y * 0.5f, size.x * 0.5f, size.y * 0.5f));
            material.SetVector(
                CellDimensionsId,
                new Vector4(columns, rows, layerCount, surface.CellSize));
            material.SetFloat(SpriteCountId, spriteCount);
            material.SetFloat(
                PlaneToleranceId,
                Mathf.Max(MinimumPlaneTolerance, surface.CellSize * 0.002f));
            material.SetFloat(NormalThresholdId, DefaultNormalThreshold);
            ApplySurfaceTransformProperties();
        }

        /// <summary>
        /// Surface 或父级移动旋转时同步矩阵和世界法线，不重建 Cell 数据。
        /// </summary>
        private void ApplySurfaceTransformProperties()
        {
            material.SetMatrix(SurfaceWorldToLocalId, surface.transform.worldToLocalMatrix);
            material.SetVector(SurfaceNormalId, surface.transform.up);
        }

        /// <summary>
        /// 仅修改发生帧切换的 AnimatedTile 索引，并在一帧内合并 GPU 上传。
        /// </summary>
        private void UpdateAnimatedCells()
        {
            if (animatedCells.Count == 0 || cellData == null)
            {
                return;
            }

            var dirtyLayers = 0;
            for (var i = 0; i < animatedCells.Count; i++)
            {
                var animatedCell = animatedCells[i];
                var frame = GetAnimationFrame(animatedCell);
                if (frame == animatedCell.currentFrame)
                {
                    continue;
                }

                animatedCell.currentFrame = frame;
                animatedCells[i] = animatedCell;
                cellPixels[animatedCell.layerIndex][animatedCell.pixelIndex] =
                    EncodeSpriteIndex(animatedCell.frameIndices[frame]);
                dirtyLayers |= 1 << animatedCell.layerIndex;
            }

            if (dirtyLayers == 0)
            {
                return;
            }

            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                if ((dirtyLayers & (1 << layerIndex)) != 0)
                {
                    cellData.SetPixels32(cellPixels[layerIndex], layerIndex);
                }
            }

            cellData.Apply(false, false);
        }

        /// <summary>
        /// 按 Unity TileAnimationData 的速度、起始时间和循环标记计算当前 Sprite 帧。
        /// </summary>
        private static int GetAnimationFrame(AnimatedCell animatedCell)
        {
            if (animatedCell.frameIndices.Length <= 1
                || (animatedCell.flags & TileAnimationFlags.PauseAnimation) != 0)
            {
                return 0;
            }

            var animationTime = Application.isPlaying
                ? (animatedCell.flags & TileAnimationFlags.UnscaledTime) != 0
                    ? Time.unscaledTime
                    : Time.time
                : Time.realtimeSinceStartup;
            // Unity 的 animationStartTime 是进入动画序列的时间偏移，AnimatedTile
            // 用它表达起始帧，因此必须加到当前时间而不是作为延迟时间相减。
            var elapsed = Mathf.Max(0f, animationTime + animatedCell.animationStartTime);
            var frameValue = Mathf.FloorToInt(
                elapsed * animatedCell.animationFrameRate
                * Mathf.Max(0.01f, animatedCell.animationSpeed));
            if ((animatedCell.flags & TileAnimationFlags.LoopOnce) != 0)
            {
                return Mathf.Min(frameValue, animatedCell.frameIndices.Length - 1);
            }

            return frameValue % animatedCell.frameIndices.Length;
        }

        /// <summary>
        /// 同步目标 Layer、渲染层、排序和 enabled 状态，保持原 Renderer 的空间行为。
        /// </summary>
        private void UpdateRendererState()
        {
            if (rendererObject == null || backendRenderer == null || targetRenderer == null)
            {
                return;
            }

            rendererObject.layer = targetRenderer.gameObject.layer;
            backendRenderer.renderingLayerMask = targetRenderer.renderingLayerMask;
            backendRenderer.sortingLayerID = targetRenderer.sortingLayerID;
            backendRenderer.sortingOrder = targetRenderer.sortingOrder + 1;
            backendRenderer.receiveShadows = targetRenderer.receiveShadows;
            material.SetFloat(ReceiveShadowsId, targetRenderer.receiveShadows ? 1f : 0f);
            backendRenderer.enabled = surface.isActiveAndEnabled && targetRenderer.enabled;
        }

        /// <summary>
        /// 生成全部为单位 2D 矩阵的 Cell 变换数组，空 Cell 也保持稳定默认值。
        /// </summary>
        private static Color32[] CreateIdentityTransformPixels(int count)
        {
            var pixels = new Color32[count];
            var identity = EncodeTransform(Matrix4x4.identity);
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = identity;
            }

            return pixels;
        }

        /// <summary>
        /// 把 Sprite 索引低八位和高八位写入 RG，A 标记 Cell 有效。
        /// </summary>
        private static Color32 EncodeSpriteIndex(int spriteIndex)
        {
            return new Color32(
                (byte)(spriteIndex & 0xff),
                (byte)((spriteIndex >> 8) & 0xff),
                0,
                byte.MaxValue);
        }

        /// <summary>
        /// 把 Tile 逆变换的 2×2 矩阵编码到 RGBA8，供 Shader 恢复旋转和翻转 UV。
        /// </summary>
        private static Color32 EncodeTransform(Matrix4x4 tileTransform)
        {
            var determinant = tileTransform.m00 * tileTransform.m11
                - tileTransform.m01 * tileTransform.m10;
            var inverse = Mathf.Abs(determinant) > Mathf.Epsilon
                ? tileTransform.inverse
                : Matrix4x4.identity;
            return new Color32(
                EncodeSigned(inverse.m00),
                EncodeSigned(inverse.m01),
                EncodeSigned(inverse.m10),
                EncodeSigned(inverse.m11));
        }

        /// <summary>
        /// 将 -1..1 的矩阵分量编码为 Shader 可精确还原零值的字节。
        /// </summary>
        private static byte EncodeSigned(float value)
        {
            return (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 127f + 128f), 0, 255);
        }

        /// <summary>
        /// 记录失败原因、停用残留覆盖渲染器并返回 false。
        /// </summary>
        private bool Fail(string message)
        {
            Warning = message;
            if (backendRenderer != null)
            {
                backendRenderer.enabled = false;
            }

            return false;
        }

        /// <summary>
        /// 释放可重建的材质和数据纹理，保留隐藏 Renderer 对象以减少层级抖动。
        /// </summary>
        private void ReleaseDataResources()
        {
            ReleaseObject(material);
            ReleaseObject(cellData);
            ReleaseObject(transformData);
            ReleaseObject(colorData);
            ReleaseObject(spriteLookup);
            material = null;
            cellData = null;
            transformData = null;
            colorData = null;
            spriteLookup = null;
            cellPixels = null;
            cellColumns = 0;
            cellRows = 0;
            layerCount = 0;
            spriteCount = 0;
            animatedCells.Clear();
            if (backendRenderer != null)
            {
                backendRenderer.enabled = false;
            }
        }

        /// <summary>
        /// 按运行状态销毁仅供当前 Surface 使用的非持久化 Unity 对象。
        /// </summary>
        private static void ReleaseObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>
        /// 保存单个 AnimatedTile Cell 的帧索引和 Unity 动画时序参数。
        /// </summary>
        private struct AnimatedCell
        {
            public int layerIndex;
            public int pixelIndex;
            public int[] frameIndices;
            public float animationSpeed;
            public float animationStartTime;
            public float animationFrameRate;
            public TileAnimationFlags flags;
            public int currentFrame;
        }
    }
}
