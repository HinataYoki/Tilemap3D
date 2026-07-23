using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// 描述世界坐标命中的 Tile、Cell、图层和通用地面语义。
    /// </summary>
    public struct TileMap3DSurfaceInfo
    {
        public TileMap3DSurface Surface;
        public Tilemap Tilemap;
        public TileBase Tile;
        public Vector3Int Cell;
        public string SurfaceId;
    }

    /// <summary>
    /// 将任意 Unity TileBase 映射为不依赖音频或角色系统的地面语义。
    /// </summary>
    [CreateAssetMenu(fileName = "TileMap3D Surface Profile", menuName = "TileMap3D/Surface Profile")]
    public sealed class TileMap3DSurfaceProfile : ScriptableObject
    {
        [Serializable]
        private struct Entry
        {
            public TileBase tile;
            public string surfaceId;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        /// <summary>
        /// 查询 Tile 对应的地面语义；未配置或空语义返回 false。
        /// </summary>
        public bool TryGetSurfaceId(TileBase tile, out string surfaceId)
        {
            if (tile != null)
            {
                for (var i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    if (entry.tile == tile && !string.IsNullOrWhiteSpace(entry.surfaceId))
                    {
                        surfaceId = entry.surfaceId;
                        return true;
                    }
                }
            }

            surfaceId = string.Empty;
            return false;
        }

        /// <summary>
        /// 新增或更新单个 Tile 的地面语义，供编辑器工具和运行时初始化使用。
        /// </summary>
        public void SetSurfaceId(TileBase tile, string surfaceId)
        {
            if (tile == null)
            {
                return;
            }

            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i].tile != tile)
                {
                    continue;
                }

                entries[i] = new Entry
                {
                    tile = tile,
                    surfaceId = surfaceId ?? string.Empty
                };
                return;
            }

            entries.Add(new Entry
            {
                tile = tile,
                surfaceId = surfaceId ?? string.Empty
            });
        }
    }
}
