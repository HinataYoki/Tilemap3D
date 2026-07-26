#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3DSurface 的编辑器专属子系统：延迟重建调度、世界格网维护与越界警示 Gizmos。
    /// </summary>
    public sealed partial class TileMap3DSurface
    {
        private const float OutOfBoundsGizmoFillRatio = 0.86f;
        private const float OutOfBoundsGizmoDepthRatio = 0.04f;
        private const float MinimumOutOfBoundsGizmoDepth = 0.005f;

        /// <summary>
        /// 将编辑器重建延迟到 Unity 完成 OnValidate、CheckConsistency 和序列化校验之后。
        /// </summary>
        private void ScheduleDelayedEditorRebuild()
        {
            if (delayedEditorRebuildScheduled)
            {
                return;
            }

            delayedEditorRebuildScheduled = true;
            EditorApplication.delayCall += ExecuteDelayedEditorRebuild;
        }

        /// <summary>
        /// 处理一次延迟重建，并在请求仍未消费时重新排队以避开层级回调重入。
        /// </summary>
        private void ExecuteDelayedEditorRebuild()
        {
            EditorApplication.delayCall -= ExecuteDelayedEditorRebuild;
            delayedEditorRebuildScheduled = false;
            if (this == null)
            {
                return;
            }

            ProcessRequestedRebuild();
            if (this != null)
            {
                // 保存/校验结束后材质状态可能没有触发 SceneView 自己的重绘。
                SceneView.RepaintAll();
                if (rebuildRequested && isActiveAndEnabled)
                {
                    ScheduleDelayedEditorRebuild();
                }
            }
        }

        /// <summary>
        /// 组件停用或销毁时撤销延迟回调，避免回调继续持有场景对象引用。
        /// </summary>
        private void CancelDelayedEditorRebuild()
        {
            EditorApplication.delayCall -= ExecuteDelayedEditorRebuild;
            delayedEditorRebuildScheduled = false;
        }

        /// <summary>
        /// 编辑模式下检测 Generated Ground 的世界变换，移动或旋转后恢复完整 Cell 格网相位。
        /// </summary>
        private void MaintainWorldGridAlignmentInEditor()
        {
            if (Application.isPlaying
                || !keepWorldGridAligned
                || surfaceMode != TileMap3DSurfaceMode.GeneratedGround
                || sourceGrid == null)
            {
                worldGridTransformInitialized = false;
                return;
            }

            var worldScale = transform.lossyScale;
            if (worldGridTransformInitialized
                && transform.position == lastWorldGridPosition
                && transform.rotation == lastWorldGridRotation
                && worldScale == lastWorldGridScale)
            {
                return;
            }

            AlignToWorldGrid();
            CaptureWorldGridTransform();
        }

        /// <summary>
        /// 记录对齐后的世界变换，避免 ExecuteAlways 在静止状态重复计算和写入。
        /// </summary>
        private void CaptureWorldGridTransform()
        {
            worldGridTransformInitialized = true;
            lastWorldGridPosition = transform.position;
            lastWorldGridRotation = transform.rotation;
            lastWorldGridScale = transform.lossyScale;
        }

        /// <summary>
        /// Undo 或 Redo 恢复 Tilemap 数据后使越界缓存失效。
        /// </summary>
        private void HandleUndoRedo()
        {
            if (this == null)
            {
                return;
            }

            InvalidateOutOfBoundsTileCache();
            if (showOutOfBoundsTilePreview)
            {
                SceneView.RepaintAll();
            }
        }

        /// <summary>
        /// 开启警示时在 Scene View 绘制固定区域边框和所有含 Tile 的越界 Cell。
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!showOutOfBoundsTilePreview || sourceGrid == null || !isActiveAndEnabled)
            {
                return;
            }

            EnsureOutOfBoundsTileCache();
            var gridCellSize = sourceGrid.cellSize;
            var cellWidth = Mathf.Max(MinimumCellSize, Mathf.Abs(gridCellSize.x));
            var cellHeight = Mathf.Max(MinimumCellSize, Mathf.Abs(gridCellSize.y));
            var cellDepth = Mathf.Max(
                MinimumOutOfBoundsGizmoDepth,
                Mathf.Min(cellWidth, cellHeight) * OutOfBoundsGizmoDepthRatio);
            var previousMatrix = Gizmos.matrix;
            var previousColor = Gizmos.color;
            Gizmos.matrix = sourceGrid.transform.localToWorldMatrix;

            var validRect = GetGridLocalSurfaceRect(GetSurfaceBounds());
            var validCenter = new Vector3(validRect.center.x, validRect.center.y, -cellDepth);
            Gizmos.color = new Color(1f, 0.68f, 0.12f, 0.95f);
            Gizmos.DrawWireCube(
                validCenter,
                new Vector3(validRect.width, validRect.height, cellDepth));

            var warningSize = new Vector3(
                cellWidth * OutOfBoundsGizmoFillRatio,
                cellHeight * OutOfBoundsGizmoFillRatio,
                cellDepth);
            for (var i = 0; i < outOfBoundsTilePositions.Count; i++)
            {
                var cell = outOfBoundsTilePositions[i];
                var cellCenter = sourceGrid.CellToLocalInterpolated(
                    new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z));
                cellCenter.z -= cellDepth;
                Gizmos.color = new Color(1f, 0.22f, 0.08f, 0.28f);
                Gizmos.DrawCube(cellCenter, warningSize);
                Gizmos.color = new Color(1f, 0.35f, 0.08f, 0.95f);
                Gizmos.DrawWireCube(cellCenter, warningSize);
            }

            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
#endif
