using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的公开配置入口：承载方式、数据源绑定、布局与对齐。
    /// </summary>
    public sealed partial class TileMap3DSurface
    {
        /// <summary>
        /// 配置新建 Surface 的承载方式，不迁移已有 Tile 数据。
        /// </summary>
        public void ConfigureForCreation(TileMap3DSurfaceMode newSurfaceMode)
        {
            surfaceMode = newSurfaceMode;
            showSourcePreview = true;
            showOutOfBoundsTilePreview = true;
            keepWorldGridAligned = newSurfaceMode == TileMap3DSurfaceMode.GeneratedGround;
            layoutVersion = CurrentLayoutVersion;
            EnsureSerializedValues();
            ApplySourcePreviewVisibility();
            Rebuild();
        }

        /// <summary>
        /// 切换平面是否生成自己的 Mesh 和 BoxCollider。
        /// </summary>
        public void SetSurfaceMode(TileMap3DSurfaceMode value)
        {
            if (surfaceMode == value)
            {
                return;
            }

            surfaceMode = value;
            showSourcePreview = true;
            Rebuild();
        }

        /// <summary>
        /// 设置源 Grid 到表面法线方向的偏移和同一 Surface 的图层间距。
        /// </summary>
        public void SetRenderOffsets(float newSurfaceOffset, float newLayerSpacing)
        {
            surfaceOffset = Mathf.Max(MinimumSurfaceOffset, newSurfaceOffset);
            layerSpacing = Mathf.Max(MinimumLayerSpacing, newLayerSpacing);
            ApplySourceGridTransform();
            Rebuild();
        }

        /// <summary>
        /// 绑定原生 Grid，并立即同步预览状态和 3D 地面尺寸。
        /// </summary>
        public void SetSourceGrid(Grid value)
        {
            if (sourceGrid == value)
            {
                return;
            }

            sourceGrid = value;
            InvalidateSourceTilemaps();
            SynchronizeSourceTileSizeFromExistingTiles();
            ApplySourceGridTransform();
            ApplySourcePreviewVisibility();
            Rebuild();
        }

        /// <summary>
        /// 设置 Tile 到通用地面语义的映射 Profile，不改变 Tilemap 或渲染数据。
        /// </summary>
        public void SetSurfaceProfile(TileMap3DSurfaceProfile value)
        {
            surfaceProfile = value;
        }

        /// <summary>
        /// 指定玩法查询使用的 Tilemap；为空时按图层从上到下寻找可用 Tile。
        /// </summary>
        public void SetSurfaceQueryLayer(Tilemap value)
        {
            surfaceQueryLayer = value;
        }

        /// <summary>
        /// 设置固定列数、行数和单格世界尺寸，不删除范围外的 Tilemap 数据。
        /// </summary>
        public void SetGroundLayout(int columns, int rows, float worldCellSize)
        {
            var origin = surfaceBounds.position;
            surfaceBounds = new BoundsInt(
                origin,
                new Vector3Int(Mathf.Max(1, columns), Mathf.Max(1, rows), 1));
            cellSize = Mathf.Max(MinimumCellSize, worldCellSize);
            layoutVersion = CurrentLayoutVersion;
            ApplySourceGridTransform();
            Rebuild();
        }

        /// <summary>
        /// 切换原生 TilemapRenderer 预览，不改变图层自身的 enabled 状态。
        /// </summary>
        public void SetSourcePreviewVisible(bool visible)
        {
            if (showSourcePreview == visible)
            {
                return;
            }

            showSourcePreview = visible;
            ApplySourcePreviewVisibility();
            Rebuild();
        }

        /// <summary>
        /// 切换 Scene View 中的越界 Tile 警示，不改变 TilemapRenderer 或运行时渲染结果。
        /// </summary>
        public void SetOutOfBoundsTilePreviewVisible(bool visible)
        {
            if (showOutOfBoundsTilePreview == visible)
            {
                return;
            }

            showOutOfBoundsTilePreview = visible;
            if (visible)
            {
                InvalidateOutOfBoundsTileCache();
            }
        }

        /// <summary>
        /// 抵消父级缩放对当前局部轴长度的影响，保持平面 Tile 单格在世界空间中比例稳定。
        /// </summary>
        public void NormalizeWorldScale()
        {
            var parent = transform.parent;
            if (parent == null)
            {
                transform.localScale = Vector3.one;
                Rebuild();
                return;
            }

            var parentMatrix = parent.localToWorldMatrix;
            var localRotation = transform.localRotation;
            var worldXLength = parentMatrix.MultiplyVector(localRotation * Vector3.right).magnitude;
            var worldYLength = parentMatrix.MultiplyVector(localRotation * Vector3.up).magnitude;
            var worldZLength = parentMatrix.MultiplyVector(localRotation * Vector3.forward).magnitude;
            transform.localScale = new Vector3(
                worldXLength > Mathf.Epsilon ? 1f / worldXLength : 1f,
                worldYLength > Mathf.Epsilon ? 1f / worldYLength : 1f,
                worldZLength > Mathf.Epsilon ? 1f / worldZLength : 1f);
            Rebuild();
        }

        /// <summary>
        /// 启用 Generated Ground 的世界格网吸附，并立即修正有效区域左下角的格网相位。
        /// </summary>
        public bool EnableWorldGridAlignment()
        {
            if (surfaceMode != TileMap3DSurfaceMode.GeneratedGround)
            {
                return false;
            }

            keepWorldGridAligned = true;
            return AlignToWorldGrid();
        }

        /// <summary>
        /// 关闭 Generated Ground 的世界格网吸附，保留当前 Transform 和全部 Tile 坐标。
        /// </summary>
        public void DisableWorldGridAlignment()
        {
            keepWorldGridAligned = false;
#if UNITY_EDITOR
            worldGridTransformInitialized = false;
#endif
        }

        /// <summary>
        /// 仅沿绘制平面的两个轴移动 Surface，使有效区域左下角落在完整世界 Cell 格网上。
        /// </summary>
        public bool AlignToWorldGrid()
        {
            if (sourceGrid == null)
            {
                return false;
            }

            var gridOrigin = sourceGrid.CellToWorld(GetSurfaceBounds().min);
            var gridRight = sourceGrid.CellToWorld(GetSurfaceBounds().min + Vector3Int.right) - gridOrigin;
            var gridUp = sourceGrid.CellToWorld(GetSurfaceBounds().min + Vector3Int.up) - gridOrigin;
            var rightCellSize = gridRight.magnitude;
            var upCellSize = gridUp.magnitude;
            if (rightCellSize <= Mathf.Epsilon || upCellSize <= Mathf.Epsilon)
            {
                return false;
            }

            var rightDirection = gridRight / rightCellSize;
            var upDirection = gridUp / upCellSize;
            var axisDot = Vector3.Dot(rightDirection, upDirection);
            var determinant = 1f - axisDot * axisDot;
            if (determinant <= Mathf.Epsilon)
            {
                return false;
            }

            var rightCoordinate = Vector3.Dot(gridOrigin, rightDirection);
            var upCoordinate = Vector3.Dot(gridOrigin, upDirection);
            var rightOffset = Mathf.Round(rightCoordinate / rightCellSize) * rightCellSize
                - rightCoordinate;
            var upOffset = Mathf.Round(upCoordinate / upCellSize) * upCellSize - upCoordinate;
            var correction = rightDirection * ((rightOffset - axisDot * upOffset) / determinant)
                + upDirection * ((upOffset - axisDot * rightOffset) / determinant);
            if (correction.sqrMagnitude <= GridCenterTolerance * GridCenterTolerance)
            {
#if UNITY_EDITOR
                CaptureWorldGridTransform();
#endif
                return false;
            }

            transform.position += correction;
#if UNITY_EDITOR
            CaptureWorldGridTransform();
#endif
            return true;
        }

        /// <summary>
        /// 将 Overlay 的固定格子区域、中心和法线位置适配到直接父物体的平面范围。
        /// </summary>
        public bool TryFitToTargetBounds()
        {
            if (surfaceMode != TileMap3DSurfaceMode.Overlay)
            {
                return false;
            }

            var target = transform.parent;
            if (target == null
                || !TryGetTargetBounds(target, out var targetBounds, out var boundsToWorld))
            {
                return false;
            }

            NormalizeWorldScale();
            GetTargetCornersLocalRange(targetBounds, boundsToWorld, out var minimum, out var maximum);
            var width = maximum.x - minimum.x;
            var depth = maximum.z - minimum.z;
            if (!IsFinite(width) || !IsFinite(depth)
                || width < MinimumSurfaceSize || depth < MinimumSurfaceSize)
            {
                return false;
            }

            var fittedPosition = transform.TransformPoint(new Vector3(
                (minimum.x + maximum.x) * 0.5f,
                maximum.y,
                (minimum.z + maximum.z) * 0.5f));
            transform.position = fittedPosition;
            SetGroundLayout(
                GetRequiredCellCount(width),
                GetRequiredCellCount(depth),
                cellSize);
            return true;
        }

        /// <summary>
        /// 把目标包围盒八个角投影到 Surface 本地空间，求得覆盖范围。
        /// </summary>
        private void GetTargetCornersLocalRange(
            Bounds targetBounds,
            Matrix4x4 boundsToWorld,
            out Vector3 minimum,
            out Vector3 maximum)
        {
            minimum = new Vector3(
                float.PositiveInfinity,
                float.PositiveInfinity,
                float.PositiveInfinity);
            maximum = new Vector3(
                float.NegativeInfinity,
                float.NegativeInfinity,
                float.NegativeInfinity);
            var center = targetBounds.center;
            var extents = targetBounds.extents;
            for (var cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                var corner = center + new Vector3(
                    (cornerIndex & 1) == 0 ? -extents.x : extents.x,
                    (cornerIndex & 2) == 0 ? -extents.y : extents.y,
                    (cornerIndex & 4) == 0 ? -extents.z : extents.z);
                var worldCorner = boundsToWorld.MultiplyPoint3x4(corner);
                var surfaceCorner = transform.InverseTransformPoint(worldCorner);
                minimum = Vector3.Min(minimum, surfaceCorner);
                maximum = Vector3.Max(maximum, surfaceCorner);
            }
        }

        /// <summary>
        /// 按 Mesh、3D Collider、Renderer 的优先级取得目标范围及其世界变换。
        /// </summary>
        private static bool TryGetTargetBounds(
            Transform target,
            out Bounds targetBounds,
            out Matrix4x4 boundsToWorld)
        {
            var meshFilter = target.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                targetBounds = meshFilter.sharedMesh.bounds;
                boundsToWorld = target.localToWorldMatrix;
                return true;
            }

            var boxCollider = target.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                targetBounds = new Bounds(boxCollider.center, boxCollider.size);
                boundsToWorld = target.localToWorldMatrix;
                return true;
            }

            var meshCollider = target.GetComponent<MeshCollider>();
            if (meshCollider != null && meshCollider.sharedMesh != null)
            {
                targetBounds = meshCollider.sharedMesh.bounds;
                boundsToWorld = target.localToWorldMatrix;
                return true;
            }

            var targetRenderer = target.GetComponent<Renderer>();
            if (targetRenderer != null)
            {
                targetBounds = targetRenderer.bounds;
                boundsToWorld = Matrix4x4.identity;
                return true;
            }

            targetBounds = default;
            boundsToWorld = Matrix4x4.identity;
            return false;
        }

        /// <summary>
        /// 将目标平面长度向上取整到完整格，并过滤矩阵运算产生的微小浮点误差。
        /// </summary>
        private int GetRequiredCellCount(float length)
        {
            var requiredCells = length / Mathf.Max(MinimumCellSize, cellSize);
            return Mathf.Max(1, Mathf.CeilToInt(requiredCells - GridCenterTolerance));
        }

        /// <summary>
        /// 检查 Bounds 投影结果是否为可用于布局计算的有限数值。
        /// </summary>
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
