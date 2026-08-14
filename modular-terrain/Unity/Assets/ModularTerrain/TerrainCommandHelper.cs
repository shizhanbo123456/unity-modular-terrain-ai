#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ModularTerrain
{
    /// <summary>
    /// 地形相关桥接命令的共享辅助：定位 / 创建 / 归位「管理器预制体」。
    /// 固定路径 <c>Assets/ModularTerrainManager.prefab</c>：
    ///   - 不存在则创建（挂 ModularTerrainManager 并存为 prefab）；
    ///   - 若在其它目录被发现，则移回该固定位置（实现「不允许移动到别的目录」约定）。
    /// 供 terrain.sync_config 与各 terrain.module_* 命令复用，避免重复逻辑。
    /// </summary>
    public static class TerrainCommandHelper
    {
        public const string ManagerPrefabPath = "Assets/ModularTerrainManager.prefab";

        /// <summary>
        /// 定位 / 创建 / 归位管理器预制体，返回其 GameObject（已确保挂载 ModularTerrainManager）。
        /// </summary>
        public static GameObject LoadOrCreateManagerPrefab(out bool created)
        {
            created = false;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            if (prefab == null)
            {
                // 在其它目录找同组件预制体，移回固定位置（不允许移动到别的目录）
                string[] guids = AssetDatabase.FindAssets("t:ModularTerrainManager");
                if (guids.Length > 0)
                {
                    string stray = AssetDatabase.GUIDToAssetPath(guids[0]);
                    if (stray != ManagerPrefabPath)
                    {
                        AssetDatabase.MoveAsset(stray, ManagerPrefabPath);
                        Debug.LogWarning(
                            $"[Terrain] 管理器预制体不在固定位置，已移回 {ManagerPrefabPath}（原位置 {stray}）");
                    }
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
                }

                if (prefab == null)
                {
                    var go = new GameObject("ModularTerrainManager");
                    go.AddComponent<ModularTerrainManager>();
                    prefab = PrefabUtility.SaveAsPrefabAsset(go, ManagerPrefabPath);
                    Object.DestroyImmediate(go);
                    created = true;
                }
            }
            return prefab;
        }

        /// <summary>
        /// 取得管理器组件（必要时补挂），并刷新 modules（含 id 自动分配）。
        /// </summary>
        public static ModularTerrainManager LoadManagerWithModules(out bool created)
        {
            GameObject prefab = LoadOrCreateManagerPrefab(out created);
            var manager = prefab.GetComponent<ModularTerrainManager>();
            if (manager == null)
            {
                prefab.AddComponent<ModularTerrainManager>();
                PrefabUtility.SavePrefabAsset(prefab);
                manager = prefab.GetComponent<ModularTerrainManager>();
            }
            manager.LoadModules();
            return manager;
        }
    }
}
#endif // UNITY_EDITOR
