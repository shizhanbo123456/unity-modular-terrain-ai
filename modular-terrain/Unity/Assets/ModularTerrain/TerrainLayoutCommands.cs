#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityPythonBridge;
using Newtonsoft.Json.Linq;

namespace ModularTerrain
{
    /// <summary>
    /// 地形排布（布局）命令。排布数据**全部存储在 Unity 工程的 Resources CSV** 中
    /// （默认 <c>Assets/ModularTerrain/Resources/TerrainLayout.csv</c>，默认空，仅含表头），
    /// Python 侧只通过本文件命令读写，绝不直接接触文件：
    ///
    ///   terrain.layout_get   - 读取 [xmin,zmin,xmax,zmax] 范围内的排布（四参数均可省略，省略返回全部）。
    ///   terrain.layout_set   - 在 (x,z) 处写入单条排布：moduleId, rotation(0/90/180/270), height(float)。
    ///   terrain.layout_clear - 清空排布，回到默认空文件（仅保留表头）。
    ///   terrain.layout_load  - 按 (x,z) 加载/刷新单个地形块：刷新模块库与排布后，从 CSV 读取该格排布，
    ///                          实例化对应模块预制体到场景（相邻格自动贴合，因本工作流所有模块同尺寸）。
    ///   terrain.layout_unload - 卸载（销毁）(x,z) 处已实例化的地形模块；该坐标无实例则忽略。
    ///
    /// 每条排布记录字段：(x, z) 网格坐标 + (moduleId, rotation, height)。
    /// rotation 仅允许 0/90/180/270，表示**俯视视角下顺时针旋转**的角度（由后续实例化命令据此设置朝向）。
    ///
    /// CSV 的读取/写入与数据结构统一由 <see cref="TerrainLayoutIO"/> 负责，
    /// 与 <see cref="ModularTerrainManager"/> 共用，避免重复解析逻辑。
    /// </summary>
    public static class TerrainLayoutCommands
    {
        private static readonly int[] ValidRotations = { 0, 90, 180, 270 };

        // ---- 内部辅助 ----

        private static int ReqInt(JObject args, string key)
        {
            if (!args.ContainsKey(key))
                throw new System.ArgumentException($"缺少必填参数 {key}(int)");
            return args.Value<int>(key);
        }

        private static float ReqFloat(JObject args, string key)
        {
            if (!args.ContainsKey(key))
                throw new System.ArgumentException($"缺少必填参数 {key}(float)");
            return args.Value<float>(key);
        }

        private static Dictionary<string, object> CellToDict(Vector2Int k, TerrainLayoutCell c)
        {
            return new Dictionary<string, object>
            {
                ["x"] = k.x,
                ["z"] = k.y,
                ["moduleId"] = c.moduleId,
                ["rotation"] = c.rotation,
                ["height"] = c.height,
            };
        }

        // ---- 命令 ----

        [BridgeCommand("terrain.layout_get",
            "读取范围内地形排布。参数: xmin,zmin,xmax,zmax(int，均可省略；省略返回全部)")]
        public static object LayoutGet(BridgeContext ctx, JObject args)
        {
            bool hasRange = args.ContainsKey("xmin") && args.ContainsKey("zmin")
                          && args.ContainsKey("xmax") && args.ContainsKey("zmax");
            int xmin = hasRange ? args.Value<int>("xmin") : int.MinValue;
            int zmin = hasRange ? args.Value<int>("zmin") : int.MinValue;
            int xmax = hasRange ? args.Value<int>("xmax") : int.MaxValue;
            int zmax = hasRange ? args.Value<int>("zmax") : int.MaxValue;

            var all = TerrainLayoutIO.Read();
            var entries = new List<object>();
            foreach (var kv in all)
            {
                int x = kv.Key.x;
                int z = kv.Key.y;
                if (x >= xmin && x <= xmax && z >= zmin && z <= zmax)
                    entries.Add(CellToDict(kv.Key, kv.Value));
            }

            return new Dictionary<string, object>
            {
                ["count"] = entries.Count,
                ["range"] = new Dictionary<string, int>
                {
                    ["xmin"] = xmin, ["zmin"] = zmin, ["xmax"] = xmax, ["zmax"] = zmax,
                },
                ["entries"] = entries,
                ["csvPath"] = TerrainLayoutIO.CsvPath,
            };
        }

        [BridgeCommand("terrain.layout_set",
            "写入单个排布（按 x,z 更新，存在则覆盖）。参数: x,z(int 网格坐标), " +
            "moduleId(int), rotation(0/90/180/270 俯视顺时针), height(float)")]
        public static object LayoutSet(BridgeContext ctx, JObject args)
        {
            int x = ReqInt(args, "x");
            int z = ReqInt(args, "z");
            int moduleId = ReqInt(args, "moduleId");
            int rotation = ReqInt(args, "rotation");
            float height = ReqFloat(args, "height");

            if (System.Array.IndexOf(ValidRotations, rotation) < 0)
                throw new System.ArgumentException(
                    "rotation 仅允许取值 0/90/180/270（俯视视角顺时针旋转）");

            var all = TerrainLayoutIO.Read();
            Vector2Int key = new Vector2Int(x, z);
            bool created = !all.ContainsKey(key);
            all[key] = new TerrainLayoutCell(moduleId, rotation, height);
            TerrainLayoutIO.WriteAll(all);

            return new Dictionary<string, object>
            {
                ["csvPath"] = TerrainLayoutIO.CsvPath,
                ["created"] = created,
                ["x"] = x,
                ["z"] = z,
                ["moduleId"] = moduleId,
                ["rotation"] = rotation,
                ["height"] = height,
                ["total"] = all.Count,
            };
        }

        [BridgeCommand("terrain.layout_clear",
            "清空地形排布，回到默认空文件（仅保留表头）。无参数")]
        public static object LayoutClear(BridgeContext ctx, JObject args)
        {
            TerrainLayoutIO.WriteAll(new Dictionary<Vector2Int, TerrainLayoutCell>());

            return new Dictionary<string, object>
            {
                ["csvPath"] = TerrainLayoutIO.CsvPath,
                ["cleared"] = true,
                ["total"] = 0,
            };
        }

        // ---- 场景实例化（按坐标加载/卸载单个地形块） ----

        /// <summary>
        /// 取得场景中活动的 ModularTerrainManager 实例（供实例化命令使用）。
        /// 场景中没有则依据固定预制体 Assets/ModularTerrainManager.prefab 实例化一个。
        /// </summary>
        private static ModularTerrainManager GetSceneManager()
        {
            var mgr = Object.FindObjectOfType<ModularTerrainManager>();
            if (mgr == null)
            {
                GameObject prefab = TerrainCommandHelper.LoadOrCreateManagerPrefab(out _);
                GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                mgr = inst.GetComponent<ModularTerrainManager>();
            }
            return mgr;
        }

        [BridgeCommand("terrain.layout_load",
            "按网格坐标 (x,z) 加载/刷新单个地形块：刷新模块库与排布后，从 CSV 读取该格排布，" +
            "实例化对应模块到场景（相邻格自动贴合，因本工作流所有模块同尺寸）。参数: x,z(int 网格坐标)")]
        public static object LayoutLoad(BridgeContext ctx, JObject args)
        {
            int x = ReqInt(args, "x");
            int z = ReqInt(args, "z");
            var mgr = GetSceneManager();
            mgr.LoadModules();        // 刷新模块引用（捕获最新 tile 库）
            mgr.LoadLayoutFromCsv();  // 刷新排布（捕获最新的 layout_set 写入）
            Vector2Int key = new Vector2Int(x, z);
            bool hasCell = mgr.layout.ContainsKey(key);
            int moduleId = hasCell ? mgr.layout[key].moduleId : -1;
            mgr.LoadTerrainModule(x, z);  // 内部按坐标实例化（已加载则先卸再载）
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["z"] = z,
                ["loaded"] = hasCell,
                ["moduleId"] = moduleId,
                ["moduleSize"] = mgr.moduleSize,
            };
        }

        [BridgeCommand("terrain.layout_unload",
            "卸载（销毁）网格坐标 (x,z) 处已实例化的地形模块。若该坐标无实例则忽略。" +
            "参数: x,z(int 网格坐标)")]
        public static object LayoutUnload(BridgeContext ctx, JObject args)
        {
            int x = ReqInt(args, "x");
            int z = ReqInt(args, "z");
            var mgr = GetSceneManager();
            bool existed = mgr.HasLoaded(x, z);
            mgr.UnloadTerrainModule(x, z);
            return new Dictionary<string, object>
            {
                ["x"] = x,
                ["z"] = z,
                ["unloaded"] = existed,
            };
        }
    }
}
#endif // UNITY_EDITOR
