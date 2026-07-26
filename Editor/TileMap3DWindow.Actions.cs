using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using static YokiFrame.Unity.TileMap3D.TileMap3DEditorUI;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3D 工作台的命令处理（partial）。
    /// </summary>
    public sealed partial class TileMap3DWindow
    {
        /// <summary>
        /// 创建一个完整 TileMap3D 地面并绑定到当前工作台。
        /// </summary>
        private void CreateSurface()
        {
            BindSurface(TileMap3DCommands.CreateSurface(null));
        }

        /// <summary>
        /// 在当前选择对象下创建不带几何和碰撞的平面覆盖 Surface。
        /// </summary>
        private void CreateOverlaySurface()
        {
            BindSurface(TileMap3DCommands.CreateOverlaySurface(Selection.activeGameObject));
        }

        /// <summary>
        /// 为当前地面补建源 Grid 和基础 Tilemap 图层。
        /// </summary>
        private void CreateSourceGrid()
        {
            if (surface == null)
            {
                return;
            }

            TileMap3DCommands.CreateSourceGrid(surface);
            activeTilemap = TileMap3DCommands.AddLayer(surface);
            ScheduleRefresh();
        }

        /// <summary>
        /// 新增原生 Tilemap 图层并立即设为当前绘制目标。
        /// </summary>
        private void AddLayer()
        {
            if (surface == null)
            {
                return;
            }

            activeTilemap = TileMap3DCommands.AddLayer(surface);
            if (activeTilemap != null)
            {
                TileMap3DCommands.SetPaintTarget(activeTilemap, false);
            }

            ScheduleRefresh();
        }

        /// <summary>
        /// 写入 Scene View 越界 Tile 警示开关，并保留为可撤销的 Surface 配置修改。
        /// </summary>
        private void SetOutOfBoundsPreview(bool visible)
        {
            if (surface == null || surface.ShowOutOfBoundsTilePreview == visible)
            {
                return;
            }

            Undo.RecordObject(surface, "切换 TileMap3D 越界 Tile 警示");
            surface.SetOutOfBoundsTilePreviewVisible(visible);
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 统计后确认清理全部源图层的越界 Tile；清理结果可通过 Unity Undo 恢复。
        /// </summary>
        private void ClearOutOfBoundsTiles()
        {
            if (surface == null)
            {
                return;
            }

            var count = surface.CountOutOfBoundsTiles();
            if (count <= 0)
            {
                RefreshOutOfBoundsTools();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "清理越界 Tile",
                    "将删除当前 Surface 全部 Tilemap 图层中位于固定区域外的 "
                    + count + " 个 Tile。此操作可通过 Undo 恢复。",
                    "清理",
                    "取消"))
            {
                return;
            }

            var clearedCount = TileMap3DCommands.ClearOutOfBoundsTiles(surface);
            if (statusLabel != null)
            {
                statusLabel.text = clearedCount > 0
                    ? "已清理 " + clearedCount + " 个越界 Tile"
                    : "没有需要清理的越界 Tile";
            }

            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 统计后确认清理所有已加载场景中的越界 Tile；整次操作可通过一次 Undo 恢复。
        /// </summary>
        private void ClearAllOutOfBoundsTilesInLoadedScenes()
        {
            var count = TileMap3DCommands.CountOutOfBoundsTilesInLoadedScenes();
            if (count <= 0)
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "场景中没有需要清理的越界 Tile";
                }

                RefreshOutOfBoundsTools();
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "清理场景中所有越界 Tile",
                    "将删除所有已加载场景的全部 TileMap3D Surface 中位于固定区域外的 "
                    + count + " 个 Tile。此操作可通过一次 Undo 恢复。",
                    "全部清理",
                    "取消"))
            {
                return;
            }

            var clearedCount = TileMap3DCommands.ClearOutOfBoundsTilesInLoadedScenes();
            if (statusLabel != null)
            {
                statusLabel.text = clearedCount > 0
                    ? "已清理场景中的 " + clearedCount + " 个越界 Tile"
                    : "场景中没有需要清理的越界 Tile";
            }

            RefreshOutOfBoundsTools();
        }

        /// <summary>
        /// 修改原生 Tilemap 的渲染类型并立即刷新材质与法线偏移。
        /// </summary>
        private void ChangeLayerRenderType(Tilemap tilemap, string value)
        {
            var layerType = value == LayerRenderTypeChoices[0]
                ? TileMap3DLayerType.Base
                : TileMap3DLayerType.Overlay;

            var layer = TileMap3DCommands.EnsureLayerComponent(tilemap, layerType);
            if (layer == null)
            {
                return;
            }

            Undo.RecordObject(layer, "修改 TileMap3D 图层渲染类型");
            layer.Configure(layerType);
            EditorUtility.SetDirty(layer);
            if (surface != null)
            {
                surface.Rebuild();
                EditorUtility.SetDirty(surface);
            }

            SceneView.RepaintAll();
            ScheduleRefresh();
        }

        /// <summary>
        /// 将当前 Surface 对齐到父物体局部平面预设。
        /// </summary>
        private void AlignSurface(TileMap3DPlanePreset planePreset)
        {
            TileMap3DCommands.AlignSurface(surface, planePreset);
            RefreshStatus();
        }

        /// <summary>
        /// 按直接父物体的当前平面范围重新计算 Overlay 的位置、列数和行数。
        /// </summary>
        private void FitSurfaceToParent()
        {
            if (TileMap3DCommands.FitSurfaceToParent(surface))
            {
                RefreshStatus();
                return;
            }

            if (statusLabel != null)
            {
                statusLabel.text = "适配失败：请选择 Overlay，且直接父物体需要 Mesh、BoxCollider、MeshCollider 或 Renderer";
            }
        }

        /// <summary>
        /// 重新计算父级非等比缩放补偿，避免旋转后的 Surface 拉伸 Tile。
        /// </summary>
        private void NormalizeSurfaceWorldScale()
        {
            TileMap3DCommands.NormalizeSurfaceWorldScale(surface);
            RefreshStatus();
        }

        /// <summary>
        /// 为当前 Generated Ground 启用持续世界格网吸附，并立即修正旧场景中的小数相位。
        /// </summary>
        private void EnableWorldGridAlignment()
        {
            if (!TileMap3DCommands.EnableWorldGridAlignment(surface))
            {
                if (statusLabel != null)
                {
                    statusLabel.text = "世界格网对齐仅用于 Generated Ground";
                }

                return;
            }

            RefreshStatus();
        }

        /// <summary>
        /// 把指定图层设为 Unity Tile Palette 的 Scene Paint Target。
        /// </summary>
        private void SetActiveLayer(Tilemap tilemap)
        {
            activeTilemap = tilemap;
            TileMap3DCommands.SetPaintTarget(tilemap);
            ScheduleRefresh();
        }

        /// <summary>
        /// 经确认后通过 Undo 删除指定原生 Tilemap 图层。
        /// </summary>
        private void DeleteLayer(Tilemap tilemap)
        {
            if (tilemap == null || !EditorUtility.DisplayDialog(
                    "删除 Tilemap 图层",
                    "确定删除图层 “" + tilemap.name + "” 及其全部 Tile 数据？",
                    "删除",
                    "取消"))
            {
                return;
            }

            if (activeTilemap == tilemap)
            {
                activeTilemap = null;
            }

            Undo.DestroyObjectImmediate(tilemap.gameObject);
            if (surface != null)
            {
                surface.Rebuild();
                EditorUtility.SetDirty(surface);
            }

            activeTilemap = FindFirstTilemap();
            ScheduleRefresh();
        }

        /// <summary>
        /// 打开 Unity Tile Palette；没有活动图层时自动选择或创建一个图层。
        /// </summary>
        private void OpenTilePalette()
        {
            if (!IsOwnedTilemap(activeTilemap))
            {
                activeTilemap = FindFirstTilemap();
            }

            if (activeTilemap == null)
            {
                AddLayer();
            }

            if (activeTilemap != null)
            {
                TileMap3DCommands.SetPaintTarget(activeTilemap);
            }
        }

        /// <summary>
        /// 根据当前设置重建原生图层和可选生成几何，不改动 Tilemap 数据。
        /// </summary>
        private void RebuildSurface()
        {
            if (surface == null)
            {
                return;
            }

            Undo.RecordObject(surface, "重建 TileMap3D 地面");
            surface.Rebuild();
            EditorUtility.SetDirty(surface);
            SceneView.RepaintAll();
            RefreshStatus();
        }
    }
}
