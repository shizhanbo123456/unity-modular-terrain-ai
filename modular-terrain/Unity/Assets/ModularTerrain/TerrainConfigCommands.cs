#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPythonBridge;
using Newtonsoft.Json.Linq;

namespace ModularTerrain
{
    /// <summary>
    /// 全局模块配置（sizePrecision + moduleDirectories）的读取与写入命令，
    /// 均经 unity-python-bridge 命令总线分发。拆分为两条独立命令（不再复用单命令 + action 区分）：
    ///
    ///   terrain.config_get —— 读取 Unity 管理器预制体中的全局配置（经 Unity API，不解析 .prefab 文件）。
    ///   terrain.config_set —— 将 sizePrecision + moduleDirectories 写入 Unity 管理器预制体。
    ///
    /// Unity 管理器是全局配置的唯一数据源，Python 端不另存任何本地副本。
    /// 管理器预制体固定位于 Assets/ModularTerrainManager.prefab（不存在则创建；在别处则移回）。
    /// </summary>
    public static class TerrainConfigCommands
    {
        [BridgeCommand("terrain.config_get",
            "读取 Unity 管理器预制体中的全局模块配置（sizePrecision + moduleDirectories）。无参数。")]
        public static object ConfigGet(BridgeContext ctx, JObject args)
        {
            bool created;
            GameObject prefab = TerrainCommandHelper.LoadOrCreateManagerPrefab(out created);
            var manager = prefab.GetComponent<ModularTerrainManager>();
            if (manager == null)
            {
                prefab.AddComponent<ModularTerrainManager>();
                PrefabUtility.SavePrefabAsset(prefab);
                manager = prefab.GetComponent<ModularTerrainManager>();
            }

            return new Dictionary<string, object>
            {
                ["source"] = "unity",
                ["sizePrecision"] = manager.sizePrecision,
                ["moduleDirectories"] = manager.moduleDirectories,
                ["moduleCount"] = manager.moduleDirectories.Count,
            };
        }

        [BridgeCommand("terrain.config_set",
            "将全局模块配置写入 Unity 管理器预制体。参数: sizePrecision(number>0), moduleDirectories(array<string>)")]
        public static object ConfigSet(BridgeContext ctx, JObject args)
        {
            bool created;
            GameObject prefab = TerrainCommandHelper.LoadOrCreateManagerPrefab(out created);
            var manager = prefab.GetComponent<ModularTerrainManager>();
            if (manager == null)
            {
                prefab.AddComponent<ModularTerrainManager>();
                PrefabUtility.SavePrefabAsset(prefab);
                manager = prefab.GetComponent<ModularTerrainManager>();
            }

            float sizePrecision = args.Value<float>("sizePrecision");
            if (sizePrecision <= 0f)
                throw new System.ArgumentException("sizePrecision 必须为正数");

            var dirsToken = args["moduleDirectories"];
            if (dirsToken == null || dirsToken.Type != JTokenType.Array)
                throw new System.ArgumentException("moduleDirectories 必须是字符串数组");
            var directories = new List<string>();
            foreach (var t in dirsToken)
                directories.Add((string)t);

            manager.sizePrecision = sizePrecision;
            manager.moduleDirectories = directories;

            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                ["prefabPath"] = TerrainCommandHelper.ManagerPrefabPath,
                ["created"] = created,
                ["sizePrecision"] = sizePrecision,
                ["moduleDirectories"] = directories,
                ["moduleCount"] = directories.Count,
            };
        }
    }
}
#endif // UNITY_EDITOR
