#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPythonBridge;
using Newtonsoft.Json.Linq;

namespace ModularTerrain
{
    /// <summary>
    /// 命令 <c>terrain.sync_config</c>：
    /// 将 Python 端的地形配置（最小尺寸精度 + 模块目录路径）同步到「管理器预制体」。
    ///
    /// 管理器预制体（挂载 ModularTerrainManager）固定位于 Assets 根目录：
    ///   <c>Assets/ModularTerrainManager.prefab</c>
    ///   - 若不存在则创建；
    ///   - 若在其它目录被发现，则移回该固定位置（实现「不允许移动到别的目录」约定）。
    ///
    /// 参数:
    ///   sizePrecision (number)      - 最小尺寸精度，必须 > 0
    ///   moduleDirectories (array)   - 模块目录列表（Assets 相对路径字符串数组）
    ///
    /// 返回:
    ///   { prefabPath, created, sizePrecision, moduleDirectories, moduleCount }
    /// </summary>
    [BridgeCommand("terrain.sync_config",
        "将 Python 端配置同步到管理器预制体。参数: sizePrecision(number>0), moduleDirectories(array<string>)")]
    public static class TerrainSyncConfigCommand
    {
        private const string ManagerPrefabPath = "Assets/ModularTerrainManager.prefab";

        public static object Execute(BridgeContext ctx, JObject args)
        {
            // 1) 校验参数
            float sizePrecision = args.Value<float>("sizePrecision");
            if (sizePrecision <= 0f)
                throw new System.ArgumentException("sizePrecision 必须为正数");

            var dirsToken = args["moduleDirectories"];
            if (dirsToken == null || dirsToken.Type != JTokenType.Array)
                throw new System.ArgumentException("moduleDirectories 必须是字符串数组");
            var directories = new List<string>();
            foreach (var t in dirsToken)
                directories.Add((string)t);

            // 2) 定位 / 创建 / 归位管理器预制体
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ManagerPrefabPath);
            bool created = false;

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
                            $"[terrain.sync_config] 管理器预制体不在固定位置，已移回 {ManagerPrefabPath}（原位置 {stray}）");
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

            // 3) 确保管理器组件存在
            var manager = prefab.GetComponent<ModularTerrainManager>();
            if (manager == null)
            {
                prefab.AddComponent<ModularTerrainManager>();
                PrefabUtility.SavePrefabAsset(prefab);
                manager = prefab.GetComponent<ModularTerrainManager>();
            }

            // 4) 写入配置并持久化
            manager.sizePrecision = sizePrecision;
            manager.moduleDirectories = directories;

            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                ["prefabPath"] = ManagerPrefabPath,
                ["created"] = created,
                ["sizePrecision"] = sizePrecision,
                ["moduleDirectories"] = directories,
                ["moduleCount"] = directories.Count,
            };
        }
    }
}
#endif // UNITY_EDITOR
