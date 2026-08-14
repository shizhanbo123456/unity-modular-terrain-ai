#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPythonBridge;
using Newtonsoft.Json.Linq;

namespace ModularTerrain
{
    /// <summary>
    /// 命令 <c>terrain.sync_config</c>：全局模块配置（sizePrecision + moduleDirectories）的
    /// 读取与写入，均经 unity-python-bridge 命令总线分发。
    ///
    ///   action = "write"（默认）：将 Python 端配置写入 Unity 管理器预制体。
    ///   action = "read"：由 Unity 通过 API 读取预制体组件的当前配置并返回（不解析 .prefab 文件），
    ///        供 Python 端比对「Python 记录值」与「Unity 实际值」。
    ///
    /// 管理器预制体固定位于 Assets/ModularTerrainManager.prefab（不存在则创建；在别处则移回）。
    /// </summary>
    [BridgeCommand("terrain.sync_config",
        "全局模块配置读写。参数: action(\"write\"|\"read\", 默认 write), sizePrecision(number>0), moduleDirectories(array<string>)")]
    public static class TerrainSyncConfigCommand
    {
        public static object Execute(BridgeContext ctx, JObject args)
        {
            string action = args.Value<string>("action") ?? "write";

            bool created;
            GameObject prefab = TerrainCommandHelper.LoadOrCreateManagerPrefab(out created);
            var manager = prefab.GetComponent<ModularTerrainManager>();
            if (manager == null)
            {
                prefab.AddComponent<ModularTerrainManager>();
                PrefabUtility.SavePrefabAsset(prefab);
                manager = prefab.GetComponent<ModularTerrainManager>();
            }

            // ---- 读取：由 Unity 返回其当前配置 ----
            if (action == "read")
            {
                return new Dictionary<string, object>
                {
                    ["source"] = "unity",
                    ["sizePrecision"] = manager.sizePrecision,
                    ["moduleDirectories"] = manager.moduleDirectories,
                    ["moduleCount"] = manager.moduleDirectories.Count,
                };
            }

            // ---- 写入：同时修改 Unity 预制体（Python 侧记录值由 CLI 负责写回 json） ----
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
