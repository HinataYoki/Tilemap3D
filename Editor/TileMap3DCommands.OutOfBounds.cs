using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace YokiFrame.Unity.TileMap3D
{
    /// <summary>
    /// TileMap3D 越界 Tile 的统计与批量清理命令（partial）。
    /// </summary>
    internal static partial class TileMap3DCommands
    {
        /// <summary>
        /// 以单个 Undo 操作清理当前 Surface 全部图层中超出固定列、行区域的 Tile。
        /// </summary>
        public static int ClearOutOfBoundsTiles(TileMap3DSurface surface)
        {
            if (surface == null || surface.CountOutOfBoundsTiles() <= 0)
            {
                return 0;
            }

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("清理 TileMap3D 越界 Tile");
            var clearedCount = ClearOutOfBoundsTilesWithoutUndoGroup(
                surface,
                "清理 TileMap3D 越界 Tile");
            if (clearedCount > 0)
            {
                SceneView.RepaintAll();
            }

            Undo.CollapseUndoOperations(undoGroup);
            return clearedCount;
        }

        /// <summary>
        /// 统计所有已加载场景中全部 TileMap3D Surface 的越界 Tile 数量。
        /// </summary>
        public static int CountOutOfBoundsTilesInLoadedScenes()
        {
            var surfaces = GetLoadedSceneSurfaces();
            var count = 0;
            for (var i = 0; i < surfaces.Count; i++)
            {
                count += surfaces[i].CountOutOfBoundsTiles(true);
            }

            return count;
        }

        /// <summary>
        /// 以单个 Undo 操作清理所有已加载场景中全部 Surface 的越界 Tile。
        /// </summary>
        public static int ClearOutOfBoundsTilesInLoadedScenes()
        {
            var surfaces = GetLoadedSceneSurfaces();
            var pendingCount = 0;
            for (var i = 0; i < surfaces.Count; i++)
            {
                pendingCount += surfaces[i].CountOutOfBoundsTiles(true);
            }

            if (pendingCount <= 0)
            {
                return 0;
            }

            const string undoName = "清理场景中所有 TileMap3D 越界 Tile";
            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            var clearedCount = 0;
            for (var i = 0; i < surfaces.Count; i++)
            {
                clearedCount += ClearOutOfBoundsTilesWithoutUndoGroup(surfaces[i], undoName);
            }

            if (clearedCount > 0)
            {
                SceneView.RepaintAll();
            }

            Undo.CollapseUndoOperations(undoGroup);
            return clearedCount;
        }

        /// <summary>
        /// 登记指定 Surface 的 Tilemap Undo 后执行清理，由调用方统一管理 Undo 分组。
        /// </summary>
        private static int ClearOutOfBoundsTilesWithoutUndoGroup(
            TileMap3DSurface surface,
            string undoName)
        {
            if (surface == null || surface.CountOutOfBoundsTiles(true) <= 0)
            {
                return 0;
            }

            var tilemaps = surface.GetSourceTilemaps();
            for (var i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] != null)
                {
                    Undo.RegisterCompleteObjectUndo(tilemaps[i], undoName);
                }
            }

            var clearedCount = surface.ClearOutOfBoundsTiles();
            if (clearedCount <= 0)
            {
                return 0;
            }

            for (var i = 0; i < tilemaps.Length; i++)
            {
                if (tilemaps[i] != null)
                {
                    EditorUtility.SetDirty(tilemaps[i]);
                }
            }

            surface.Rebuild();
            EditorUtility.SetDirty(surface);
            return clearedCount;
        }

        /// <summary>
        /// 收集所有已加载普通场景中的 Surface，包含禁用对象并排除 Prefab 资源。
        /// </summary>
        private static List<TileMap3DSurface> GetLoadedSceneSurfaces()
        {
            var surfaces = new List<TileMap3DSurface>();
            for (var sceneIndex = 0; sceneIndex < EditorSceneManager.sceneCount; sceneIndex++)
            {
                var scene = EditorSceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    continue;
                }

                var rootObjects = scene.GetRootGameObjects();
                for (var rootIndex = 0; rootIndex < rootObjects.Length; rootIndex++)
                {
                    rootObjects[rootIndex].GetComponentsInChildren(true, surfaces);
                }
            }

            return surfaces;
        }
    }
}
