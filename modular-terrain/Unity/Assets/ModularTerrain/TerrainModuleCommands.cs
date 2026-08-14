#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPythonBridge;
using Newtonsoft.Json.Linq;

namespace ModularTerrain
{
    /// <summary>
    /// 地形模块操作命令（均挂载于 unity-python-bridge 命令总线）：
    ///   terrain.module_list  - 打印所有已加载模块的信息列表
    ///   terrain.module_size  - 计算指定 id 模块的尺寸（长宽 / 四边高度 / 最大高度 / 是否符合精度）
    ///   terrain.module_snap  - 把指定 id 模块的尺寸吸附到精度整数倍
    ///   terrain.module_set   - 按 id 设置模块指定字段（仅设置传入的参数，可同时设置多个）
    ///
    /// 所有命令先经 TerrainCommandHelper 定位管理器预制体并 LoadModules（含 id 自动分配），
    /// 再按 id 在 manager.modules 中定位目标模块组件进行操作。模块组件即其 prefab 资源本体，
    /// 修改后经 EditorUtility.SetDirty + AssetDatabase.SaveAssets 持久化。
    /// </summary>
    public static class TerrainModuleCommands
    {
        // ---- 内部辅助 ----

        private static int RequireId(JObject args)
        {
            if (!args.ContainsKey("id"))
                throw new System.ArgumentException("缺少必填参数 id(int)");
            return args.Value<int>("id");
        }

        private static bool TryGetFloat(JObject args, string key, out float value)
        {
            if (args.ContainsKey(key))
            {
                value = args.Value<float>(key);
                return true;
            }
            value = 0f;
            return false;
        }

        private static ModularTerrainModule RequireModule(ModularTerrainManager mgr, int id)
        {
            var m = mgr.GetModuleById(id);
            if (m == null)
                throw new System.ArgumentException(
                    $"未找到 id={id} 的模块（可先运行 terrain-sync 或 module-list 确认已加载）");
            return m;
        }

        // ---- 命令 ----

        [BridgeCommand("terrain.module_list",
            "打印模块信息列表。返回所有已加载模块的 id / 长宽 / 四边高度。无参数")]
        public static object ListModules(BridgeContext ctx, JObject args)
        {
            bool created;
            var manager = TerrainCommandHelper.LoadManagerWithModules(out created);

            var list = new List<object>();
            foreach (var m in manager.modules)
            {
                if (m == null) continue;
                list.Add(new Dictionary<string, object>
                {
                    ["id"] = m.id,
                    ["sizeX"] = m.moduleSize.x,
                    ["sizeZ"] = m.moduleSize.y,
                    ["heightZPlus"] = m.heightZPlus,
                    ["heightXPlus"] = m.heightXPlus,
                    ["heightZMinus"] = m.heightZMinus,
                    ["heightXMinus"] = m.heightXMinus,
                });
            }

            return new Dictionary<string, object>
            {
                ["count"] = list.Count,
                ["precision"] = manager.sizePrecision,
                ["modules"] = list,
            };
        }

        [BridgeCommand("terrain.module_size",
            "计算指定 id 模块的尺寸。参数: id(int)")]
        public static object ModuleSize(BridgeContext ctx, JObject args)
        {
            int id = RequireId(args);
            bool created;
            var manager = TerrainCommandHelper.LoadManagerWithModules(out created);
            var m = RequireModule(manager, id);

            float maxH = Mathf.Max(m.heightZPlus, m.heightXPlus, m.heightZMinus, m.heightXMinus);

            return new Dictionary<string, object>
            {
                ["id"] = m.id,
                ["lengthX"] = m.moduleSize.x,
                ["widthZ"] = m.moduleSize.y,
                ["heightZPlus"] = m.heightZPlus,
                ["heightXPlus"] = m.heightXPlus,
                ["heightZMinus"] = m.heightZMinus,
                ["heightXMinus"] = m.heightXMinus,
                ["maxHeight"] = maxH,
                ["precision"] = manager.sizePrecision,
                ["isValidSize"] = manager.IsValidSize(m.moduleSize.x) && manager.IsValidSize(m.moduleSize.y),
            };
        }

        [BridgeCommand("terrain.module_snap",
            "把指定 id 模块的尺寸吸附到精度整数倍（sizeX/sizeZ 与四边高度）。参数: id(int)")]
        public static object ModuleSnap(BridgeContext ctx, JObject args)
        {
            int id = RequireId(args);
            bool created;
            var manager = TerrainCommandHelper.LoadManagerWithModules(out created);
            var m = RequireModule(manager, id);

            m.moduleSize.x = manager.SnapToPrecision(m.moduleSize.x);
            m.moduleSize.y = manager.SnapToPrecision(m.moduleSize.y);
            m.heightZPlus = manager.SnapToPrecision(m.heightZPlus);
            m.heightXPlus = manager.SnapToPrecision(m.heightXPlus);
            m.heightZMinus = manager.SnapToPrecision(m.heightZMinus);
            m.heightXMinus = manager.SnapToPrecision(m.heightXMinus);

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                ["id"] = m.id,
                ["lengthX"] = m.moduleSize.x,
                ["widthZ"] = m.moduleSize.y,
                ["heightZPlus"] = m.heightZPlus,
                ["heightXPlus"] = m.heightXPlus,
                ["heightZMinus"] = m.heightZMinus,
                ["heightXMinus"] = m.heightXMinus,
                ["precision"] = manager.sizePrecision,
                ["snapped"] = true,
            };
        }

        [BridgeCommand("terrain.module_set",
            "按 id 设置模块指定字段（仅设置传入的参数，可同时设置多个）。" +
            "参数: id(int), sizeX/length, sizeZ/width, hZPlus, hXPlus, hZMinus, hXMinus (float)")]
        public static object ModuleSet(BridgeContext ctx, JObject args)
        {
            int id = RequireId(args);
            bool created;
            var manager = TerrainCommandHelper.LoadManagerWithModules(out created);
            var m = RequireModule(manager, id);

            var changed = new List<string>();
            float v;
            if (TryGetFloat(args, "sizeX", out v)) { m.moduleSize.x = v; changed.Add("sizeX"); }
            else if (TryGetFloat(args, "length", out v)) { m.moduleSize.x = v; changed.Add("length"); }
            if (TryGetFloat(args, "sizeZ", out v)) { m.moduleSize.y = v; changed.Add("sizeZ"); }
            else if (TryGetFloat(args, "width", out v)) { m.moduleSize.y = v; changed.Add("width"); }
            if (TryGetFloat(args, "hZPlus", out v)) { m.heightZPlus = v; changed.Add("hZPlus"); }
            if (TryGetFloat(args, "hXPlus", out v)) { m.heightXPlus = v; changed.Add("hXPlus"); }
            if (TryGetFloat(args, "hZMinus", out v)) { m.heightZMinus = v; changed.Add("hZMinus"); }
            if (TryGetFloat(args, "hXMinus", out v)) { m.heightXMinus = v; changed.Add("hXMinus"); }

            if (changed.Count == 0)
                throw new System.ArgumentException("未提供任何要设置的字段（如 --sizeX 6）");

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();

            return new Dictionary<string, object>
            {
                ["id"] = m.id,
                ["changed"] = changed,
                ["sizeX"] = m.moduleSize.x,
                ["sizeZ"] = m.moduleSize.y,
                ["heightZPlus"] = m.heightZPlus,
                ["heightXPlus"] = m.heightXPlus,
                ["heightZMinus"] = m.heightZMinus,
                ["heightXMinus"] = m.heightXMinus,
            };
        }
    }
}
#endif // UNITY_EDITOR
