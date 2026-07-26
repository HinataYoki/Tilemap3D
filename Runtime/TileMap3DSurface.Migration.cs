using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的旧版序列化迁移：首次加载旧场景时保留尺寸与显示行为。
    /// </summary>
    public sealed partial class TileMap3DSurface
    {
        /// <summary>
        /// 首次加载旧版地面时保留尺寸和预览行为，避免新原生模式改变已有场景。
        /// </summary>
        private void UpgradeLegacyLayout()
        {
            if (layoutVersion >= CurrentLayoutVersion)
            {
                return;
            }

            if (layoutVersion < 1)
            {
                if (automaticBounds && TryGetOccupiedBounds(out var occupiedBounds))
                {
                    surfaceBounds = occupiedBounds;
                }

                cellSize = GetCurrentWorldCellSize();
                automaticBounds = false;
            }

            if (layoutVersion < 2)
            {
                surfaceMode = TileMap3DSurfaceMode.GeneratedGround;
                surfaceOffset = 0f;
                layerSpacing = Mathf.Max(MinimumLayerSpacing, layerSpacing);
            }

            if (layoutVersion < 3 && surfaceMode == TileMap3DSurfaceMode.Overlay)
            {
                showSourcePreview = true;
            }

            if (layoutVersion < 5)
            {
                // Generated Ground 与 Overlay 默认共享同一世界格网相位，旧地面升级后也立即恢复该契约。
                keepWorldGridAligned = surfaceMode == TileMap3DSurfaceMode.GeneratedGround;
            }

            if (layoutVersion < 6)
            {
                // 旧渲染后端已移除的场景：强制显示原生 Tilemap，归零表面偏移。
                showSourcePreview = true;
                surfaceOffset = 0f;
            }

            layoutVersion = CurrentLayoutVersion;
        }

        /// <summary>
        /// 合并全部有效 Tilemap 的已占用区域，仅供旧版自动区域迁移使用。
        /// </summary>
        private bool TryGetOccupiedBounds(out BoundsInt occupiedBounds)
        {
            var tilemaps = GetSourceTilemaps();
            var hasOccupiedBounds = false;
            occupiedBounds = default;
            for (var i = 0; i < tilemaps.Length; i++)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null || !tilemap.gameObject.activeInHierarchy || tilemap.GetUsedTilesCount() == 0)
                {
                    continue;
                }

                var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
                if (tilemapRenderer == null || !tilemapRenderer.enabled)
                {
                    continue;
                }

                var current = tilemap.cellBounds;
                current.zMin = 0;
                current.zMax = 1;
                if (!hasOccupiedBounds)
                {
                    occupiedBounds = current;
                    hasOccupiedBounds = true;
                    continue;
                }

                occupiedBounds.xMin = Mathf.Min(occupiedBounds.xMin, current.xMin);
                occupiedBounds.yMin = Mathf.Min(occupiedBounds.yMin, current.yMin);
                occupiedBounds.xMax = Mathf.Max(occupiedBounds.xMax, current.xMax);
                occupiedBounds.yMax = Mathf.Max(occupiedBounds.yMax, current.yMax);
            }

            if (hasOccupiedBounds)
            {
                occupiedBounds = NormalizeBounds(occupiedBounds);
            }

            return hasOccupiedBounds;
        }

        /// <summary>
        /// 从升级前的 Grid Cell Size 与 Transform Scale 推导当前世界单格尺寸。
        /// </summary>
        private float GetCurrentWorldCellSize()
        {
            if (sourceGrid == null)
            {
                return Mathf.Max(MinimumCellSize, cellSize);
            }

            var width = Mathf.Abs(sourceGrid.cellSize.x * sourceGrid.transform.localScale.x);
            if (width >= MinimumCellSize)
            {
                return width;
            }

            var depth = Mathf.Abs(sourceGrid.cellSize.y * sourceGrid.transform.localScale.y);
            return Mathf.Max(MinimumCellSize, depth);
        }
    }
}
