using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的查询子系统：源图层枚举缓存、世界坐标 Tile 查询与越界 Tile 统计。
    /// </summary>
    public sealed partial class TileMap3DSurface
    {
        /// <summary>
        /// 返回当前参与编辑的全部原生 Tilemap。
        /// 编辑器下实时枚举；运行时使用缓存，外部在层级变化后应调用 InvalidateSourceTilemaps。
        /// </summary>
        public Tilemap[] GetSourceTilemaps()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // 编辑器下层级随时可能被外部修改，实时枚举避免陈旧缓存。
                return EnumerateSourceTilemaps();
            }
#endif
            if (sourceTilemapsCache == null)
            {
                sourceTilemapsCache = EnumerateSourceTilemaps();
            }

            return sourceTilemapsCache;
        }

        /// <summary>
        /// 标记源 Tilemap 图层列表已变化；TileMap3DLayer 的生命周期回调和图层管理工具负责调用。
        /// </summary>
        public void InvalidateSourceTilemaps()
        {
            sourceTilemapsCache = null;
            InvalidateOutOfBoundsTileCache();
        }

        /// <summary>
        /// 枚举源 Grid 下的全部 Tilemap，包含未激活图层。
        /// </summary>
        private Tilemap[] EnumerateSourceTilemaps()
        {
            return sourceGrid != null
                ? sourceGrid.GetComponentsInChildren<Tilemap>(true)
                : Array.Empty<Tilemap>();
        }

        /// <summary>
        /// 判断 Cell 是否位于当前列数、行数和起点共同定义的固定 Surface 区域内。
        /// </summary>
        public bool IsCellInsideSurfaceBounds(Vector3Int cell)
        {
            return GetSurfaceBounds().Contains(cell);
        }

        /// <summary>
        /// 把世界坐标映射到当前 Surface 的 Cell，并返回 Tile 与可选地面语义。
        /// </summary>
        public bool TryGetSurfaceInfo(Vector3 worldPosition, out TileMap3DSurfaceInfo surfaceInfo)
        {
            surfaceInfo = default;
            if (sourceGrid == null)
            {
                return false;
            }

            var localPosition = transform.InverseTransformPoint(worldPosition);
            var halfSize = GroundSize * 0.5f;
            if (localPosition.x < -halfSize.x || localPosition.x > halfSize.x
                || localPosition.z < -halfSize.y || localPosition.z > halfSize.y)
            {
                return false;
            }

            var cell = sourceGrid.WorldToCell(worldPosition);
            if (surfaceQueryLayer != null
                && surfaceQueryLayer.transform.IsChildOf(sourceGrid.transform))
            {
                var queryTile = surfaceQueryLayer.GetTile(cell);
                return TryCreateSurfaceInfo(surfaceQueryLayer, queryTile, cell, out surfaceInfo);
            }

            return TryGetSurfaceInfoFromLayers(cell, out surfaceInfo);
        }

        /// <summary>
        /// 未指定查询图层时按图层从上到下寻找可用 Tile，优先返回 Profile 命中的图层。
        /// </summary>
        private bool TryGetSurfaceInfoFromLayers(
            Vector3Int cell,
            out TileMap3DSurfaceInfo surfaceInfo)
        {
            Tilemap fallbackTilemap = null;
            TileBase fallbackTile = null;
            var tilemaps = GetSourceTilemaps();
            for (var i = tilemaps.Length - 1; i >= 0; i--)
            {
                var tilemap = tilemaps[i];
                if (tilemap == null || !tilemap.gameObject.activeSelf || !tilemap.enabled)
                {
                    continue;
                }

                var tile = tilemap.GetTile(cell);
                if (tile == null)
                {
                    continue;
                }

                if (fallbackTile == null)
                {
                    fallbackTilemap = tilemap;
                    fallbackTile = tile;
                }

                if (surfaceProfile != null
                    && surfaceProfile.TryGetSurfaceId(tile, out var surfaceId))
                {
                    surfaceInfo = new TileMap3DSurfaceInfo
                    {
                        Surface = this,
                        Tilemap = tilemap,
                        Tile = tile,
                        Cell = cell,
                        SurfaceId = surfaceId
                    };
                    return true;
                }

                if (surfaceProfile == null)
                {
                    break;
                }
            }

            return TryCreateSurfaceInfo(fallbackTilemap, fallbackTile, cell, out surfaceInfo);
        }

        /// <summary>
        /// 将非空 Tile 组装为查询结果，并从 Profile 补充可选地面语义。
        /// </summary>
        private bool TryCreateSurfaceInfo(
            Tilemap tilemap,
            TileBase tile,
            Vector3Int cell,
            out TileMap3DSurfaceInfo surfaceInfo)
        {
            if (tilemap == null || tile == null)
            {
                surfaceInfo = default;
                return false;
            }

            var surfaceId = string.Empty;
            if (surfaceProfile != null)
            {
                surfaceProfile.TryGetSurfaceId(tile, out surfaceId);
            }

            surfaceInfo = new TileMap3DSurfaceInfo
            {
                Surface = this,
                Tilemap = tilemap,
                Tile = tile,
                Cell = cell,
                SurfaceId = surfaceId
            };
            return true;
        }

        /// <summary>
        /// 统计全部源图层中的越界 Tile；同一 Cell 在多个图层有 Tile 时分别计数。
        /// </summary>
        public int CountOutOfBoundsTiles(bool forceRefresh = false)
        {
            EnsureOutOfBoundsTileCache(forceRefresh);
            return outOfBoundsTilePositions.Count;
        }

        /// <summary>
        /// 删除全部源图层中的越界 Tile，并返回实际删除数量；编辑器 Undo 由调用方登记。
        /// </summary>
        public int ClearOutOfBoundsTiles()
        {
            var clearedCount = 0;
            var tilemaps = GetSourceTilemaps();
            for (var tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                var tilemap = tilemaps[tilemapIndex];
                if (tilemap == null)
                {
                    continue;
                }

                var positions = new List<Vector3Int>();
                AppendOutOfBoundsTilePositions(tilemap, positions);
                if (positions.Count == 0)
                {
                    continue;
                }

                tilemap.SetTiles(positions.ToArray(), new TileBase[positions.Count]);
                tilemap.CompressBounds();
                clearedCount += positions.Count;
            }

            if (clearedCount > 0)
            {
                InvalidateOutOfBoundsTileCache();
                RequestRebuild();
            }

            return clearedCount;
        }

        /// <summary>
        /// 判断源 Grid 下是否已有任意 Tile 数据，避免切换 Brush 时破坏现有地图比例。
        /// </summary>
        private bool HasSourceTiles()
        {
            var tilemaps = GetSourceTilemaps();
            for (var i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] != null && tilemaps[i].GetUsedTilesCount() > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 标记越界 Tile 统计和 Scene View 警示缓存需要在下次读取时重建。
        /// </summary>
        private void InvalidateOutOfBoundsTileCache()
        {
            outOfBoundsTileCacheDirty = true;
        }

        /// <summary>
        /// 按当前固定 Surface 区域重建全部图层的越界 Tile 缓存，避免 Scene View 每帧重复枚举大地图。
        /// </summary>
        private void EnsureOutOfBoundsTileCache(bool forceRefresh = false)
        {
            if (!forceRefresh && !outOfBoundsTileCacheDirty && outOfBoundsTilePositions != null)
            {
                return;
            }

            if (outOfBoundsTilePositions == null)
            {
                outOfBoundsTilePositions = new List<Vector3Int>();
            }
            else
            {
                outOfBoundsTilePositions.Clear();
            }

            var tilemaps = GetSourceTilemaps();
            for (var tilemapIndex = 0; tilemapIndex < tilemaps.Length; tilemapIndex++)
            {
                AppendOutOfBoundsTilePositions(tilemaps[tilemapIndex], outOfBoundsTilePositions);
            }

            outOfBoundsTileCacheDirty = false;
        }

        /// <summary>
        /// 将一个原生 Tilemap 中位于固定 Surface 区域外的非空 Cell 追加到目标集合。
        /// </summary>
        private void AppendOutOfBoundsTilePositions(Tilemap tilemap, List<Vector3Int> positions)
        {
            if (tilemap == null || positions == null)
            {
                return;
            }

            var tileBounds = tilemap.cellBounds;
            if (tileBounds.size.x <= 0 || tileBounds.size.y <= 0 || tileBounds.size.z <= 0)
            {
                return;
            }

            var end = tileBounds.max - Vector3Int.one;
            var tileCount = tilemap.GetTilesRangeCount(tileBounds.min, end);
            if (tileCount <= 0)
            {
                return;
            }

            var tilePositions = new Vector3Int[tileCount];
            var tiles = new TileBase[tileCount];
            var actualCount = tilemap.GetTilesRangeNonAlloc(
                tileBounds.min,
                end,
                tilePositions,
                tiles);
            var validBounds = GetSurfaceBounds();
            for (var tileIndex = 0; tileIndex < actualCount; tileIndex++)
            {
                if (!validBounds.Contains(tilePositions[tileIndex]))
                {
                    positions.Add(tilePositions[tileIndex]);
                }
            }
        }
    }
}
