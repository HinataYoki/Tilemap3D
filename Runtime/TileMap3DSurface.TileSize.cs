using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的源 Tile 尺寸子系统：探测、同步与冲突告警。
    /// </summary>
    public sealed partial class TileMap3DSurface
    {
        [System.NonSerialized] private Vector2 lastWarnedCurrentSize;
        [System.NonSerialized] private Vector2 lastWarnedDetectedSize;

        /// <summary>
        /// 在源 Grid 尚未包含其它尺寸 Tile 时，同步 Tile Palette Brush 的原始单格尺寸。
        /// </summary>
        public bool TrySynchronizeSourceTileSize(Vector2 nativeTileSize)
        {
            if (sourceGrid == null)
            {
                return false;
            }

            var normalized = NormalizeSourceTileSize(nativeTileSize);
            var current = GetSourceGridCellSize();
            if (AreSourceTileSizesEqual(current, normalized))
            {
                if (!hasMixedSourceTileSizes)
                {
                    sourceTileSizeWarning = string.Empty;
                }

                return false;
            }

            if (HasSourceTiles())
            {
                ReportSourceTileSizeWarning(current, normalized, false);
                return false;
            }

            hasMixedSourceTileSizes = false;
            sourceTileSizeWarning = string.Empty;
            return SetSourceGridCellSize(normalized);
        }

        /// <summary>
        /// 仅在本次 Tile 变化包含不同原始尺寸时扫描整张源地图，正常绘制不产生额外遍历。
        /// </summary>
        /// <returns>true 当检测到 Tile 尺寸变化并已执行全图扫描时。</returns>
        private bool SynchronizeSourceTileSizeFromChangedTiles(Tilemap.SyncTile[] changedTiles)
        {
            if (changedTiles == null || changedTiles.Length == 0)
            {
                return false;
            }

            var current = GetSourceGridCellSize();
            var foundSprite = false;
            var requiresFullScan = hasMixedSourceTileSizes;
            for (var i = 0; i < changedTiles.Length; i++)
            {
                if (!TryGetSourceTileSize(changedTiles[i].tileData.sprite, out var changedSize))
                {
                    continue;
                }

                foundSprite = true;
                if (!AreSourceTileSizesEqual(current, changedSize))
                {
                    requiresFullScan = true;
                    break;
                }
            }

            if (requiresFullScan || !foundSprite && hasMixedSourceTileSizes)
            {
                SynchronizeSourceTileSizeFromExistingTiles();
                return true;
            }

            return false;
        }

        /// <summary>
        /// 从全部已绘制 Sprite 推导统一源单格尺寸；混用尺寸时保留当前有效尺寸并给出警告。
        /// </summary>
        private void SynchronizeSourceTileSizeFromExistingTiles()
        {
            if (sourceGrid == null)
            {
                hasMixedSourceTileSizes = false;
                sourceTileSizeWarning = string.Empty;
                return;
            }

            if (!TryDetectExistingSourceTileSizes(
                    out var firstSize,
                    out var differentSize,
                    out var currentSizePresent))
            {
                hasMixedSourceTileSizes = false;
                sourceTileSizeWarning = string.Empty;
                return;
            }

            var hasDifferentSize = differentSize.x >= MinimumCellSize
                && differentSize.y >= MinimumCellSize;
            hasMixedSourceTileSizes = hasDifferentSize;
            if (!hasDifferentSize)
            {
                sourceTileSizeWarning = string.Empty;
                SetSourceGridCellSize(firstSize);
                return;
            }

            if (!currentSizePresent)
            {
                SetSourceGridCellSize(firstSize);
            }

            var current = GetSourceGridCellSize();
            var conflictingSize = AreSourceTileSizesEqual(current, firstSize)
                ? differentSize
                : firstSize;
            ReportSourceTileSizeWarning(current, conflictingSize, true);
        }

        /// <summary>
        /// 枚举非空 Tile 位置并读取 Unity 已解析的最终 Sprite 尺寸。
        /// </summary>
        private bool TryDetectExistingSourceTileSizes(
            out Vector2 firstSize,
            out Vector2 differentSize,
            out bool currentSizePresent)
        {
            firstSize = Vector2.zero;
            differentSize = Vector2.zero;
            currentSizePresent = false;
            var foundSize = false;
            var current = GetSourceGridCellSize();
            var tilemaps = GetSourceTilemaps();
            for (var tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                var tilemap = tilemaps[tilemapIndex];
                if (tilemap == null || tilemap.GetUsedTilesCount() == 0)
                {
                    continue;
                }

                var bounds = tilemap.cellBounds;
                if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0)
                {
                    continue;
                }

                var end = bounds.max - Vector3Int.one;
                var tileCount = tilemap.GetTilesRangeCount(bounds.min, end);
                if (tileCount <= 0)
                {
                    continue;
                }

                var positions = new Vector3Int[tileCount];
                var tiles = new TileBase[tileCount];
                var actualCount = tilemap.GetTilesRangeNonAlloc(bounds.min, end, positions, tiles);
                for (var tileIndex = 0; tileIndex < actualCount; tileIndex++)
                {
                    if (!TryGetSourceTileSize(tilemap.GetSprite(positions[tileIndex]), out var tileSize))
                    {
                        continue;
                    }

                    currentSizePresent |= AreSourceTileSizesEqual(current, tileSize);
                    if (!foundSize)
                    {
                        firstSize = tileSize;
                        foundSize = true;
                    }
                    else if (differentSize == Vector2.zero
                        && !AreSourceTileSizesEqual(firstSize, tileSize))
                    {
                        differentSize = tileSize;
                    }
                }
            }

            return foundSize;
        }

        /// <summary>
        /// 写入源 Grid 的逻辑 Cell Size，并保持 TileMap3D 最终世界单格尺寸不变。
        /// </summary>
        private bool SetSourceGridCellSize(Vector2 nativeTileSize)
        {
            if (sourceGrid == null)
            {
                return false;
            }

            var normalized = NormalizeSourceTileSize(nativeTileSize);
            var current = GetSourceGridCellSize();
            if (AreSourceTileSizesEqual(current, normalized))
            {
                return false;
            }

            var gridCellSize = sourceGrid.cellSize;
            sourceGrid.cellSize = new Vector3(normalized.x, normalized.y, gridCellSize.z);
            ApplySourceGridTransform();
            return true;
        }

        /// <summary>
        /// 返回源 Grid 当前使用的逻辑单格尺寸；最终世界尺寸仍由 TileMap3D Cell Size 控制。
        /// </summary>
        private Vector2 GetSourceGridCellSize()
        {
            return sourceGrid != null
                ? NormalizeSourceTileSize(new Vector2(sourceGrid.cellSize.x, sourceGrid.cellSize.y))
                : Vector2.one;
        }

        /// <summary>
        /// 从 Sprite Rect 与 PPU 对应的 Bounds 中取得原始 Tile 尺寸。
        /// </summary>
        private static bool TryGetSourceTileSize(Sprite sprite, out Vector2 tileSize)
        {
            tileSize = Vector2.zero;
            if (sprite == null)
            {
                return false;
            }

            var boundsSize = sprite.bounds.size;
            if (Mathf.Abs(boundsSize.x) < MinimumCellSize || Mathf.Abs(boundsSize.y) < MinimumCellSize)
            {
                return false;
            }

            tileSize = NormalizeSourceTileSize(new Vector2(boundsSize.x, boundsSize.y));
            return true;
        }

        /// <summary>
        /// 记录源 Tile 尺寸冲突；相同告警只输出一次，且去重在字符串构造前完成，避免逐事件分配。
        /// </summary>
        private void ReportSourceTileSizeWarning(Vector2 current, Vector2 detected, bool mixedTiles)
        {
            if (!string.IsNullOrEmpty(sourceTileSizeWarning)
                && AreSourceTileSizesEqual(current, lastWarnedCurrentSize)
                && AreSourceTileSizesEqual(detected, lastWarnedDetectedSize))
            {
                return;
            }

            lastWarnedCurrentSize = current;
            lastWarnedDetectedSize = detected;
            var reason = mixedTiles ? "当前地图混用了不同尺寸的 Tile" : "当前 Brush 与已有 Tile 尺寸不同";
            var warning = "TileMap3D：" + reason + "。源 Grid 使用 "
                + FormatSourceTileSize(current) + "，检测到 " + FormatSourceTileSize(detected)
                + "。同一 TileMap3D 地面应使用统一的 Sprite 原始尺寸。";
            sourceTileSizeWarning = warning;
            Debug.LogWarning(warning, this);
        }

        /// <summary>
        /// 约束源 Tile 原始尺寸为可用于 Grid 的正数。
        /// </summary>
        private static Vector2 NormalizeSourceTileSize(Vector2 value)
        {
            return new Vector2(
                Mathf.Max(MinimumCellSize, Mathf.Abs(value.x)),
                Mathf.Max(MinimumCellSize, Mathf.Abs(value.y)));
        }

        /// <summary>
        /// 使用稳定容差比较两个源 Tile 尺寸，避免浮点导入误差反复触发同步。
        /// </summary>
        private static bool AreSourceTileSizesEqual(Vector2 first, Vector2 second)
        {
            return Mathf.Abs(first.x - second.x) <= GridCenterTolerance
                && Mathf.Abs(first.y - second.y) <= GridCenterTolerance;
        }

        /// <summary>
        /// 生成人类可读的源 Tile 尺寸文本，供 Console 与工作台提示共用。
        /// </summary>
        private static string FormatSourceTileSize(Vector2 value)
        {
            return value.x.ToString("0.###") + " × " + value.y.ToString("0.###");
        }
    }
}
