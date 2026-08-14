using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ModularTerrain
{
    /// <summary>
    /// 地形排布（布局）的共享数据结构与 CSV 读写。
    ///
    /// 文件固定位于 <c>Assets/ModularTerrain/Resources/TerrainLayout.csv</c>，
    /// 字段：<c>x,z,moduleId,rotation,height</c>（首行为表头）。
    /// 该文件同时被以下两者使用，故抽出共享层避免重复解析：
    ///   - <see cref="ModularTerrainManager"/>（运行时按网格坐标加载/卸载模块，Awake 全量读入字典）；
    ///   - <see cref="TerrainLayoutCommands"/>（桥接命令的读取/写入）。
    ///
    /// 本类不依赖 UNITY_EDITOR，可在运行时（含打包后）调用：
    ///   - 编辑器/编辑器运行期：优先读取磁盘上的 CSV 文件（命令写入的位置）；
    ///   - 打包运行期：回退到 Resources 内嵌的 TerrainLayout（TextAsset）文本。
    /// </summary>

    /// <summary>
    /// 单个网格坐标的排布信息（信息结构体）。
    /// 作为 <see cref="ModularTerrainManager.layout"/> 字典的值，键为网格坐标 <see cref="Vector2Int"/>(x,z)。
    /// </summary>
    [System.Serializable]
    public struct TerrainLayoutCell
    {
        /// <summary>模块唯一 ID（对应 ModularTerrainModule.id）。</summary>
        public int moduleId;

        /// <summary>俯视视角下顺时针旋转角度，仅允许 0/90/180/270。</summary>
        public int rotation;

        /// <summary>该格底面中心的世界高度（y）。</summary>
        public float height;

        public TerrainLayoutCell(int moduleId, int rotation, float height)
        {
            this.moduleId = moduleId;
            this.rotation = rotation;
            this.height = height;
        }
    }

    public static class TerrainLayoutIO
    {
        /// <summary>CSV 的 Assets 相对路径（固定）。</summary>
        public const string CsvPath = "Assets/ModularTerrain/Resources/TerrainLayout.csv";

        private const string Header = "x,z,moduleId,rotation,height";

        /// <summary>CSV 在磁盘上的绝对路径（Application.dataPath 拼接）。</summary>
        public static string DiskPath()
        {
            return Application.dataPath + CsvPath.Substring("Assets".Length).Replace('\\', '/');
        }

        /// <summary>
        /// 全量读取 CSV，返回以网格坐标 (x,z) 为键的字典。
        /// 文件不存在且 Resources 内也无 TerrainLayout 时，返回空字典（不抛异常）。
        /// </summary>
        public static Dictionary<Vector2Int, TerrainLayoutCell> Read()
        {
            string text = null;
            string file = DiskPath();
            if (File.Exists(file))
            {
                text = File.ReadAllText(file);
            }
            else
            {
                // 打包运行期：CSV 已作为 Resources 资源内嵌
                TextAsset ta = Resources.Load<TextAsset>("TerrainLayout");
                if (ta != null) text = ta.text;
            }

            var dict = new Dictionary<Vector2Int, TerrainLayoutCell>();
            if (string.IsNullOrEmpty(text)) return dict;

            string[] lines = text.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (i == 0 && line.StartsWith("x,")) continue; // 表头

                string[] p = line.Split(',');
                if (p.Length < 5) continue;

                int x = int.Parse(p[0], CultureInfo.InvariantCulture);
                int z = int.Parse(p[1], CultureInfo.InvariantCulture);
                int moduleId = int.Parse(p[2], CultureInfo.InvariantCulture);
                int rotation = int.Parse(p[3], CultureInfo.InvariantCulture);
                float height = float.Parse(p[4], CultureInfo.InvariantCulture);

                dict[new Vector2Int(x, z)] = new TerrainLayoutCell(moduleId, rotation, height);
            }
            return dict;
        }

        /// <summary>
        /// 将字典写回 CSV（按坐标排序，便于人工阅读）。空字典写入后仅保留表头。
        /// </summary>
        public static void WriteAll(Dictionary<Vector2Int, TerrainLayoutCell> dict)
        {
            string file = DiskPath();
            string dir = Path.GetDirectoryName(file);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Header);

            var keys = new List<Vector2Int>(dict.Keys);
            keys.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            foreach (var k in keys)
            {
                TerrainLayoutCell c = dict[k];
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4}", k.x, k.y, c.moduleId, c.rotation, c.height));
            }

            File.WriteAllText(file, sb.ToString());

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
        }
    }
}
