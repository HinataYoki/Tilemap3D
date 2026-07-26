using UnityEditor;
using UnityEditor.Tilemaps;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// Unity Tile Palette 绘制目标与源 Tile 尺寸的同步（partial）。
    /// </summary>
    internal static partial class TileMap3DCommands
    {
        /// <summary>
        /// 当原生 Tile Palette 开始编辑 TileMap3D 图层时恢复源 Renderer，避免绘制成功但画面不可见。
        /// </summary>
        private static void ShowSourcePreviewForPaintTarget(GameObject paintTarget)
        {
            if (paintTarget == null)
            {
                return;
            }

            var tilemap = paintTarget.GetComponent<Tilemap>();
            if (tilemap == null)
            {
                return;
            }

            var surface = tilemap.GetComponentInParent<TileMap3DSurface>();
            if (surface == null)
            {
                return;
            }

            SynchronizeSourceTileSizeFromBrush(surface, GridPaintingState.gridBrush);
            if (!surface.ShowSourcePreview)
            {
                surface.SetSourcePreviewVisible(true);
                EditorUtility.SetDirty(surface);
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// 切换 Tile Palette Brush 时按当前场景绘制目标同步源 Tile 原始尺寸。
        /// </summary>
        private static void SynchronizePaintTargetFromBrush(GridBrushBase brush)
        {
            var paintTarget = GridPaintingState.scenePaintTarget;
            if (paintTarget == null)
            {
                return;
            }

            var surface = paintTarget.GetComponentInParent<TileMap3DSurface>();
            if (surface != null)
            {
                SynchronizeSourceTileSizeFromBrush(surface, brush);
            }
        }

        /// <summary>
        /// SceneView 重绘时捕获同一个 Default Brush 内部的新选区，确保首笔预览比例正确。
        /// </summary>
        private static void SynchronizePaintTargetFromSceneView(SceneView sceneView)
        {
            var paintTarget = GridPaintingState.scenePaintTarget;
            if (paintTarget == null)
            {
                return;
            }

            if (paintTarget != sCachedPaintTarget)
            {
                sCachedPaintTarget = paintTarget;
                sCachedPaintSurface = paintTarget.GetComponentInParent<TileMap3DSurface>();
            }

            if (sCachedPaintSurface != null)
            {
                SynchronizeSourceTileSizeFromBrush(sCachedPaintSurface, GridPaintingState.gridBrush);
            }
        }

        /// <summary>
        /// 使用 Default Brush 记录的 Palette Cell Size 设置源 Grid，不修改 Tile 或 Sprite 资产。
        /// </summary>
        private static void SynchronizeSourceTileSizeFromBrush(
            TileMap3DSurface surface,
            GridBrushBase brush)
        {
            var gridBrush = brush as GridBrush;
            if (surface == null || gridBrush == null)
            {
                return;
            }

            var pickedCellSize = gridBrush.lastPickedCellSize;
            if (pickedCellSize.x <= 0f || pickedCellSize.y <= 0f)
            {
                return;
            }

            if (!surface.TrySynchronizeSourceTileSize(
                    new Vector2(pickedCellSize.x, pickedCellSize.y)))
            {
                return;
            }

            EditorUtility.SetDirty(surface);
            if (surface.SourceGrid != null)
            {
                EditorUtility.SetDirty(surface.SourceGrid);
            }

            SceneView.RepaintAll();
        }
    }
}
