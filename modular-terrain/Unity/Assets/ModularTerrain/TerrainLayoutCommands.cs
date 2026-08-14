#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
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
    ///
    /// 每条排布记录字段：(x, z) 网格坐标 + (moduleId, rotation, height)。
    /// rotation 仅允许 0/90/180/270，表示**俯视视角下顺时针旋转**的角度（由后续实例化命令据此设置朝向）。
    ///
    /// 文件以 AssetDatabase 路径声明，磁盘读写通过 Application.dataPath 拼接；写后 Refresh 让 Unity 可见。
    /// </summary>
    public static class TerrainLayoutCommands
    {
        private const string CsvPath = "Assets/ModularTerrain/Resources/TerrainLayout.csv";
        private const string Header = "x,z,moduleId,rotation,height";
        private static readonly int[] ValidRotations = { 0, 90, 180, 270 };

        private class Entry
        {
            public int x;
            public int z;
            public int moduleId;
            public int rotation;
            public float height;
        }

        // ---- 内部辅助 ----

        private static string DiskPath()
        {
            // Application.dataPath 在磁盘上以 "Assets" 结尾，故拼接 "Assets/" 之后的相对路径。
            return Application.dataPath + CsvPath.Substring("Assets".Length).Replace('\\', '/');
        }

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

        private static List<Entry> ReadAll()
        {
            var list = new List<Entry>();
            string file = DiskPath();
            if (!File.Exists(file))
                return list;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0)
                    continue;
                if (i == 0 && line.StartsWith("x,"))
                    continue; // 表头
                string[] p = line.Split(',');
                if (p.Length < 5)
                    continue;
                list.Add(new Entry
                {
                    x = int.Parse(p[0], CultureInfo.InvariantCulture),
                    z = int.Parse(p[1], CultureInfo.InvariantCulture),
                    moduleId = int.Parse(p[2], CultureInfo.InvariantCulture),
                    rotation = int.Parse(p[3], CultureInfo.InvariantCulture),
                    height = float.Parse(p[4], CultureInfo.InvariantCulture),
                });
            }
            return list;
        }

        private static void WriteAll(List<Entry> list)
        {
            string file = DiskPath();
            string dir = Path.GetDirectoryName(file);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine(Header);
            foreach (var e in list)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4}", e.x, e.z, e.moduleId, e.rotation, e.height));
            }
            File.WriteAllText(file, sb.ToString());
            AssetDatabase.Refresh();
        }

        private static Dictionary<string, object> EntryToDict(Entry e)
        {
            return new Dictionary<string, object>
            {
                ["x"] = e.x,
                ["z"] = e.z,
                ["moduleId"] = e.moduleId,
                ["rotation"] = e.rotation,
                ["height"] = e.height,
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

            var all = ReadAll();
            var entries = new List<object>();
            foreach (var e in all)
            {
                if (e.x >= xmin && e.x <= xmax && e.z >= zmin && e.z <= zmax)
                    entries.Add(EntryToDict(e));
            }

            return new Dictionary<string, object>
            {
                ["count"] = entries.Count,
                ["range"] = new Dictionary<string, int>
                {
                    ["xmin"] = xmin, ["zmin"] = zmin, ["xmax"] = xmax, ["zmax"] = zmax,
                },
                ["entries"] = entries,
                ["csvPath"] = CsvPath,
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

            var all = ReadAll();
            Entry found = all.Find(e => e.x == x && e.z == z);
            bool created = found == null;
            if (found == null)
            {
                found = new Entry();
                all.Add(found);
            }
            found.x = x;
            found.z = z;
            found.moduleId = moduleId;
            found.rotation = rotation;
            found.height = height;

            WriteAll(all);

            return new Dictionary<string, object>
            {
                ["csvPath"] = CsvPath,
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
            string file = DiskPath();
            string dir = Path.GetDirectoryName(file);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(file, Header + "\n");
            AssetDatabase.Refresh();

            return new Dictionary<string, object>
            {
                ["csvPath"] = CsvPath,
                ["cleared"] = true,
                ["total"] = 0,
            };
        }
    }
}
#endif // UNITY_EDITOR
