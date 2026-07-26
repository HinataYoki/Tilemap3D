using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的几何与材质子系统：生成地面 Mesh、共享基底材质与源 Grid 变换同步。
    /// </summary>
    public sealed partial class TileMap3DSurface
    {
        /// <summary>
        /// 取得由 TileMap3D 列数和行数定义的固定 Surface 区域。
        /// </summary>
        public BoundsInt GetSurfaceBounds()
        {
            return NormalizeBounds(surfaceBounds);
        }

        /// <summary>
        /// 将单元格区域转换为 Grid 本地 XY 包围矩形，供警示 Gizmos 和尺寸计算共用。
        /// </summary>
        public Rect GetGridLocalSurfaceRect(BoundsInt bounds)
        {
            var normalized = NormalizeBounds(bounds);
            if (sourceGrid == null)
            {
                return new Rect(
                    normalized.xMin,
                    normalized.yMin,
                    normalized.size.x,
                    normalized.size.y);
            }

            var first = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMin, normalized.yMin, 0f));
            var second = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMax, normalized.yMin, 0f));
            var third = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMin, normalized.yMax, 0f));
            var fourth = sourceGrid.CellToLocalInterpolated(
                new Vector3(normalized.xMax, normalized.yMax, 0f));

            var minimumX = Mathf.Min(first.x, second.x, third.x, fourth.x);
            var maximumX = Mathf.Max(first.x, second.x, third.x, fourth.x);
            var minimumY = Mathf.Min(first.y, second.y, third.y, fourth.y);
            var maximumY = Mathf.Max(first.y, second.y, third.y, fourth.y);
            return Rect.MinMaxRect(minimumX, minimumY, maximumX, maximumY);
        }

        /// <summary>
        /// 生成或刷新 GeneratedGround 的封闭 Mesh、基底材质与 BoxCollider。
        /// </summary>
        private void RebuildGeneratedGeometry()
        {
            EnsureGeneratedGeometryComponents(
                out var meshFilter,
                out var meshRenderer,
                out var boxCollider);
            GetSurfaceRect(out var minimumX, out var maximumX, out var minimumZ, out var maximumZ);
            BuildGroundMesh(minimumX, maximumX, minimumZ, maximumZ);
            meshFilter.sharedMesh = generatedMesh;
            ApplyGroundMaterials(meshRenderer);
            meshRenderer.shadowCastingMode = ShadowCastingMode.On;
            meshRenderer.receiveShadows = true;
            ConfigureGroundCollider(boxCollider, minimumX, maximumX, minimumZ, maximumZ);
        }

        /// <summary>
        /// 重建顶面与封闭侧壁两个子网格并刷新法线、切线和包围盒。
        /// </summary>
        private void BuildGroundMesh(
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ)
        {
            var vertices = new List<Vector3>(24);
            var uv = new List<Vector2>(24);
            var topTriangles = new List<int>(6);
            var sideTriangles = new List<int>(30);
            AddTopQuad(vertices, uv, topTriangles, minimumX, maximumX, minimumZ, maximumZ);
            AddClosedSides(vertices, uv, sideTriangles, minimumX, maximumX, minimumZ, maximumZ);

            EnsureGeneratedMesh();
            generatedMesh.Clear();
            generatedMesh.indexFormat = vertices.Count > ushort.MaxValue
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            generatedMesh.SetVertices(vertices);
            generatedMesh.SetUVs(0, uv);
            generatedMesh.subMeshCount = 2;
            generatedMesh.SetTriangles(topTriangles, 0, false);
            generatedMesh.SetTriangles(sideTriangles, 1, false);
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateTangents();
            generatedMesh.RecalculateBounds();
        }

        /// <summary>
        /// 指定顶面与侧壁材质；未指定时使用共享基底材质并经 MaterialPropertyBlock 上色。
        /// </summary>
        private void ApplyGroundMaterials(MeshRenderer meshRenderer)
        {
            var sharedGroundMaterial = GetSharedGroundMaterial();
            var topMaterial = groundMaterial != null ? groundMaterial : sharedGroundMaterial;
            var wallMaterial = sideMaterial != null ? sideMaterial : sharedGroundMaterial;
            meshRenderer.sharedMaterials = new[] { topMaterial, wallMaterial };

            if (groundPropertyBlock == null)
            {
                groundPropertyBlock = new MaterialPropertyBlock();
            }

            if (groundMaterial == null)
            {
                groundPropertyBlock.Clear();
                groundPropertyBlock.SetColor(BaseColorId, surfaceColor);
                meshRenderer.SetPropertyBlock(groundPropertyBlock, 0);
            }
            else
            {
                meshRenderer.SetPropertyBlock(null, 0);
            }

            if (sideMaterial == null)
            {
                groundPropertyBlock.Clear();
                groundPropertyBlock.SetColor(BaseColorId, GetDefaultSideColor());
                meshRenderer.SetPropertyBlock(groundPropertyBlock, 1);
            }
            else
            {
                meshRenderer.SetPropertyBlock(null, 1);
            }
        }

        /// <summary>
        /// 复用 UPM 包内置的共享基底材质；缺失时创建当前实例专用的临时材质。
        /// </summary>
        private Material GetSharedGroundMaterial()
        {
            var sharedMaterial = Resources.Load<Material>(GroundMaterialResourcePath);
            if (sharedMaterial != null)
            {
                return sharedMaterial;
            }

            if (fallbackGroundMaterial != null)
            {
                return fallbackGroundMaterial;
            }

            var shader = Shader.Find(GroundSurfaceShaderName);
            if (shader == null)
            {
                if (!groundShaderMissingWarned)
                {
                    groundShaderMissingWarned = true;
                    Debug.LogWarning(
                        "TileMap3D：缺少 Resources/" + GroundMaterialResourcePath + " 材质且找不到 "
                        + GroundSurfaceShaderName + " Shader，生成地面将没有材质。",
                        this);
                }

                return null;
            }

            fallbackGroundMaterial = new Material(shader)
            {
                name = "TileMap3D_GroundFallback",
                hideFlags = HideFlags.HideAndDontSave
            };
            return fallbackGroundMaterial;
        }

        /// <summary>
        /// 由地面底色推导侧壁默认色，保持两者协调。
        /// </summary>
        private Color GetDefaultSideColor()
        {
            return new Color(
                surfaceColor.r * SideColorMultiplier,
                surfaceColor.g * SideColorMultiplier,
                surfaceColor.b * SideColorMultiplier,
                1f);
        }

        /// <summary>
        /// 按当前范围与厚度配置地面 BoxCollider。
        /// </summary>
        private void ConfigureGroundCollider(
            BoxCollider boxCollider,
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ)
        {
            var width = Mathf.Max(MinimumSurfaceSize, maximumX - minimumX);
            var depth = Mathf.Max(MinimumSurfaceSize, maximumZ - minimumZ);
            boxCollider.center = new Vector3(
                (minimumX + maximumX) * 0.5f,
                -thickness * 0.5f,
                (minimumZ + maximumZ) * 0.5f);
            boxCollider.size = new Vector3(width, thickness, depth);
            boxCollider.enabled = true;
        }

        /// <summary>
        /// 为 GeneratedGround 补齐必需组件；Overlay 模式不会修改目标 3D 物体的组件。
        /// </summary>
        private void EnsureGeneratedGeometryComponents(
            out MeshFilter meshFilter,
            out MeshRenderer meshRenderer,
            out BoxCollider boxCollider)
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null)
            {
                meshFilter = gameObject.AddComponent<MeshFilter>();
            }

            meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
            }

            boxCollider = GetComponent<BoxCollider>();
            if (boxCollider == null)
            {
                boxCollider = gameObject.AddComponent<BoxCollider>();
            }

            ownsGeneratedGeometry = true;
            meshRenderer.enabled = true;
            boxCollider.enabled = true;
        }

        /// <summary>
        /// 切换到 Overlay 时仅停用由 TileMap3D 自己生成的几何，不触碰外部目标物体。
        /// </summary>
        private void DisableOwnedGeneratedGeometry()
        {
            if (!ownsGeneratedGeometry)
            {
                return;
            }

            var meshRenderer = GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.enabled = false;
            }

            var boxCollider = GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.enabled = false;
            }
        }

        /// <summary>
        /// 以组件原点为地面顶面的水平中心，计算固定尺寸的本地 XZ 范围。
        /// </summary>
        private void GetSurfaceRect(
            out float minimumX,
            out float maximumX,
            out float minimumZ,
            out float maximumZ)
        {
            var groundSize = GroundSize;
            var halfWidth = Mathf.Max(MinimumSurfaceSize, groundSize.x) * 0.5f;
            var halfDepth = Mathf.Max(MinimumSurfaceSize, groundSize.y) * 0.5f;
            minimumX = -halfWidth;
            maximumX = halfWidth;
            minimumZ = -halfDepth;
            maximumZ = halfDepth;
        }

        /// <summary>
        /// 追加地面顶面四边形，UV 覆盖完整 0-1 区间。
        /// </summary>
        private static void AddTopQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ)
        {
            var start = vertices.Count;
            vertices.Add(new Vector3(minimumX, 0f, minimumZ));
            vertices.Add(new Vector3(maximumX, 0f, minimumZ));
            vertices.Add(new Vector3(minimumX, 0f, maximumZ));
            vertices.Add(new Vector3(maximumX, 0f, maximumZ));
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 1f));
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            triangles.Add(start + 1);
        }

        /// <summary>
        /// 追加四面侧壁和底面，使地面成为具有实际厚度的封闭模型。
        /// </summary>
        private void AddClosedSides(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            float minimumX,
            float maximumX,
            float minimumZ,
            float maximumZ)
        {
            var bottom = -thickness;
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, 0f, minimumZ),
                new Vector3(maximumX, 0f, minimumZ),
                new Vector3(minimumX, bottom, minimumZ),
                new Vector3(maximumX, bottom, minimumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, 0f, maximumZ),
                new Vector3(minimumX, bottom, maximumZ),
                new Vector3(maximumX, 0f, maximumZ),
                new Vector3(maximumX, bottom, maximumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, 0f, minimumZ),
                new Vector3(minimumX, bottom, minimumZ),
                new Vector3(minimumX, 0f, maximumZ),
                new Vector3(minimumX, bottom, maximumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(maximumX, 0f, minimumZ),
                new Vector3(maximumX, 0f, maximumZ),
                new Vector3(maximumX, bottom, minimumZ),
                new Vector3(maximumX, bottom, maximumZ));
            AddEdgeQuad(vertices, uv, triangles,
                new Vector3(minimumX, bottom, minimumZ),
                new Vector3(maximumX, bottom, minimumZ),
                new Vector3(minimumX, bottom, maximumZ),
                new Vector3(maximumX, bottom, maximumZ));
        }

        /// <summary>
        /// 按指定绕序追加一个侧壁或底面四边形。
        /// </summary>
        private static void AddEdgeQuad(
            List<Vector3> vertices,
            List<Vector2> uv,
            List<int> triangles,
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 fourth)
        {
            var start = vertices.Count;
            vertices.Add(first);
            vertices.Add(second);
            vertices.Add(third);
            vertices.Add(fourth);
            uv.Add(new Vector2(0f, 1f));
            uv.Add(new Vector2(1f, 1f));
            uv.Add(new Vector2(0f, 0f));
            uv.Add(new Vector2(1f, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start + 1);
            triangles.Add(start + 3);
            triangles.Add(start + 2);
        }

        /// <summary>
        /// 创建一次并复用当前组件的临时 Mesh，避免编辑器反复分配。
        /// </summary>
        private void EnsureGeneratedMesh()
        {
            if (generatedMesh != null)
            {
                return;
            }

            generatedMesh = new Mesh
            {
                name = "TileMap3D_GeneratedSurface",
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        /// <summary>
        /// 缩放并平移源 Grid，使逻辑单格匹配 Surface、中心对齐并保持法线偏移。
        /// </summary>
        private void ApplySourceGridTransform()
        {
            if (sourceGrid == null)
            {
                return;
            }

            if (sourceGrid.transform.IsChildOf(transform))
            {
                var localPosition = sourceGrid.transform.localPosition;
                localPosition.y = surfaceOffset;
                if (sourceGrid.transform.localPosition != localPosition)
                {
                    sourceGrid.transform.localPosition = localPosition;
                }
            }

            var sourceWidth = Mathf.Max(MinimumCellSize, Mathf.Abs(sourceGrid.cellSize.x));
            var sourceDepth = Mathf.Max(MinimumCellSize, Mathf.Abs(sourceGrid.cellSize.y));
            var targetScale = new Vector3(cellSize / sourceWidth, cellSize / sourceDepth, 1f);
            if (sourceGrid.transform.localScale != targetScale)
            {
                sourceGrid.transform.localScale = targetScale;
            }

            var gridRect = GetGridLocalSurfaceRect(GetSurfaceBounds());
            var gridLocalCenter = new Vector3(gridRect.center.x, gridRect.center.y, 0f);
            var currentWorldCenter = sourceGrid.transform.TransformPoint(gridLocalCenter);
            var currentSurfaceCenter = transform.InverseTransformPoint(currentWorldCenter);
            var targetWorldCenter = transform.TransformPoint(
                new Vector3(0f, currentSurfaceCenter.y, 0f));
            var worldOffset = targetWorldCenter - currentWorldCenter;
            if (worldOffset.sqrMagnitude > GridCenterTolerance * GridCenterTolerance)
            {
                sourceGrid.transform.position += worldOffset;
            }
        }

        /// <summary>
        /// 把 Surface 区域修正为至少一个 XY 单元格和固定一层 Z。
        /// </summary>
        private static BoundsInt NormalizeBounds(BoundsInt value)
        {
            value.size = new Vector3Int(
                Mathf.Max(1, value.size.x),
                Mathf.Max(1, value.size.y),
                1);
            value.zMin = 0;
            value.zMax = 1;
            return value;
        }

        /// <summary>
        /// 按运行模式安全释放临时 UnityEngine.Object。
        /// </summary>
        private static void ReleaseGeneratedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        /// <summary>
        /// 选中 Surface 时绘制本地 XZ 边界和本地 Y 法线，辅助对齐墙面与斜面。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            GetSurfaceRect(out var minimumX, out var maximumX, out var minimumZ, out var maximumZ);
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.9f);
            var center = new Vector3(
                (minimumX + maximumX) * 0.5f,
                0f,
                (minimumZ + maximumZ) * 0.5f);
            var size = new Vector3(maximumX - minimumX, 0f, maximumZ - minimumZ);
            Gizmos.DrawWireCube(center, size);
            var normalLength = Mathf.Max(0.25f, cellSize * 0.5f);
            Gizmos.DrawLine(center, center + Vector3.up * normalLength);
            Gizmos.DrawSphere(center + Vector3.up * normalLength, normalLength * 0.08f);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
