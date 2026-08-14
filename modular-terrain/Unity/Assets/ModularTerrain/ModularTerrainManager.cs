using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ModularTerrain
{
    /// <summary>
    /// 模块化地形管理器（场景级 MonoBehaviour）。
    ///
    /// 字段:
    ///   sizePrecision (float)
    ///        最小尺寸精度。之后处理的所有尺寸都必须是该数的整数倍
    ///        （可用 IsValidSize 校验、SnapToPrecision 规范化）。
    ///   moduleDirectories (List&lt;string&gt;)
    ///        模块（prefab / 资源）所在的目录列表，使用 Assets 相对路径。
    ///   modules (List&lt;ModularTerrainModule&gt;)
    ///        地形模块的存储容器。由 LoadModules() 根据 moduleDirectories 扫描资源
    ///        目录、加载所有包含 ModularTerrainModule 的资源并写入；
    ///        也可由 CollectModules() 收集场景中已实例化的模块。
    ///
    /// 注意：本类为运行时组件（不依赖 UnityEditor），可直接随场景进入 Player；
    ///       但「从资源目录加载模块」（LoadModules）依赖 AssetDatabase，仅在编辑器内可用，
    ///       相关代码已用 #if UNITY_EDITOR 隔离。Gizmos 绘制逻辑在
    ///       ModularTerrainModule 内同样用 #if UNITY_EDITOR 隔离。
    /// </summary>
    public class ModularTerrainManager : MonoBehaviour
    {
        [Tooltip("最小尺寸精度。之后处理的所有尺寸都必须是该数的整数倍。")]
        public float sizePrecision = 0.5f;

        [Tooltip("模块（prefab / 资源）所在的目录列表（Assets 相对路径）。")]
        public List<string> moduleDirectories = new List<string>();

        [Tooltip("地形模块的存储：由 LoadModules() 根据 moduleDirectories 扫描资源目录加载；" +
                 "也可由 CollectModules() 收集场景中的实例。两者收集后都会自动为 id==0 的模块" +
                 "分配正数 ID（AssignIds：已分配最大值 +1 递增）。")]
        public List<ModularTerrainModule> modules = new List<ModularTerrainModule>();

        [Header("地形排布（CSV 全量缓存）")]
        [Tooltip("Awake 时从 TerrainLayout.csv 全量读取，键为网格坐标 Vector2Int(x,z)，" +
                 "值为该格的排布信息（模块 id / 旋转 / 高度）。供 LoadTerrainModule 按坐标实例化。")]
        public Dictionary<Vector2Int, TerrainLayoutCell> layout =
            new Dictionary<Vector2Int, TerrainLayoutCell>();

        [Tooltip("已加载（实例化）的地形模块 GameObject，键为网格坐标 Vector2Int(x,z)，" +
                 "供 UnloadTerrainModule 精确销毁。")]
        private Dictionary<Vector2Int, GameObject> loadedInstances =
            new Dictionary<Vector2Int, GameObject>();

        [Header("网格步进（世界米）")]
        [Tooltip("网格坐标到世界位置的步长。cell(x,z) 的底面中心世界坐标 = " +
                 "管理器位置 + (x*gridStepX, height, z*gridStepZ)。")]
        public float gridStepX = 10f;
        public float gridStepZ = 10f;

        /// <summary>
        /// 根据 moduleDirectories 列出的 Assets 相对目录，扫描并加载所有包含
        /// ModularTerrainModule 的资源（prefab 等），将组件引用写入 <see cref="modules"/>。
        /// 仅编辑器可用（依赖 AssetDatabase）。无效目录会被跳过并告警；重复资源自动去重。
        /// </summary>
#if UNITY_EDITOR
        public void LoadModules()
        {
            modules = new List<ModularTerrainModule>();
            foreach (string raw in moduleDirectories)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string dir = raw.Replace('\\', '/').Trim();
                if (!dir.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                    dir = "Assets/" + dir.TrimStart('/');

                if (!AssetDatabase.IsValidFolder(dir))
                {
                    Debug.LogWarning($"[ModularTerrainManager] 跳过无效目录: {dir}");
                    continue;
                }

                string[] guids = AssetDatabase.FindAssets("t:ModularTerrainModule", new[] { dir });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    ModularTerrainModule m = prefab.GetComponent<ModularTerrainModule>();
                    if (m != null && !modules.Contains(m))
                        modules.Add(m);
                }
            }

            AssignIds();
        }

        /// <summary>
        /// 为 <see cref="modules"/> 中 id==0 的模块自动分配正数 ID：
        /// 从「已分配 ID 的最大值」+1 起依次递增（多个未分配依次 +1、+2、+3…）。
        /// 仅编辑器可用（需持久化到对应 prefab 资源）。
        /// </summary>
        public void AssignIds()
        {
            int maxId = 0;
            foreach (var m in modules)
                if (m != null && m.id > maxId) maxId = m.id;

            bool changed = false;
            foreach (var m in modules)
            {
                if (m != null && m.id == 0)
                {
                    maxId++;
                    m.id = maxId;
                    EditorUtility.SetDirty(m);
                    changed = true;
                }
            }
            if (changed) AssetDatabase.SaveAssets();
        }
#endif

        /// <summary>
        /// 收集场景中所有 ModularTerrainModule（含未激活物体）并写入 <see cref="modules"/>。
        /// 与 LoadModules 不同：此方法面向「已在场景中实例化的模块」，而非资源目录。
        /// </summary>
        public void CollectModules()
        {
            modules = new List<ModularTerrainModule>(
                FindObjectsOfType<ModularTerrainModule>(true));
#if UNITY_EDITOR
            AssignIds();
#endif
        }

        /// <summary>
        /// 按 ID 在 <see cref="modules"/> 中查找模块组件（无则返回 null）。
        /// 运行时可用，供命令按 id 定位目标模块。
        /// </summary>
        public ModularTerrainModule GetModuleById(int targetId)
        {
            foreach (var m in modules)
                if (m != null && m.id == targetId) return m;
            return null;
        }

        /// <summary>
        /// 返回 <see cref="modules"/> 中尺寸符合 sizePrecision 精度的模块
        /// （moduleSize.x、moduleSize.y 均为精度整数倍）。
        /// 便于在加载后按精度条件筛选可用模块。
        /// </summary>
        public List<ModularTerrainModule> GetModulesWithValidSize()
        {
            var result = new List<ModularTerrainModule>();
            foreach (var m in modules)
            {
                if (m == null) continue;
                if (IsValidSize(m.moduleSize.x) && IsValidSize(m.moduleSize.y))
                    result.Add(m);
            }
            return result;
        }

        /// <summary>
        /// 判断一个尺寸是否为 <see cref="sizePrecision"/> 的整数倍（带极小浮点容差）。
        /// </summary>
        public bool IsValidSize(float size)
        {
            if (sizePrecision <= 0f) return true;
            float ratio = size / sizePrecision;
            return Mathf.Abs(ratio - Mathf.Round(ratio)) < 1e-4f;
        }

        /// <summary>
        /// 把一个尺寸吸附到最近的精度整数倍（用于规范化）。
        /// </summary>
        public float SnapToPrecision(float size)
        {
            if (sizePrecision <= 0f) return size;
            return Mathf.Round(size / sizePrecision) * sizePrecision;
        }

        // ---- 地形排布：CSV 全量缓存 + 按坐标加载/卸载 ----

        /// <summary>
        /// 从 CSV 全量读取地形排布到 <see cref="layout"/> 字典（键为网格坐标 Vector2Int(x,z)）。
        /// 文件不存在时 layout 置为空字典。供 Awake 与手动刷新调用。
        /// </summary>
        public void LoadLayoutFromCsv()
        {
            layout = TerrainLayoutIO.Read();
        }

        /// <summary>
        /// 唤醒时全量读取 CSV，使 layout 字典立即可供加载/卸载使用。
        /// </summary>
        private void Awake()
        {
            LoadLayoutFromCsv();
        }

        /// <summary>
        /// 按网格坐标 (x,z) 从 <see cref="layout"/> 读取对应排布信息，实例化对应模块预制体到场景。
        /// 若该坐标无排布记录，或未找到对应 id 的模块，则打印告警并不执行。
        /// 该坐标已存在实例时，先卸载旧实例再重新加载（便于刷新最新数据）。
        /// </summary>
        public void LoadTerrainModule(int x, int z)
        {
            Vector2Int key = new Vector2Int(x, z);
            if (!layout.TryGetValue(key, out TerrainLayoutCell cell))
            {
                Debug.LogWarning($"[ModularTerrainManager] 坐标 ({x},{z}) 无排布记录，跳过加载。");
                return;
            }

            ModularTerrainModule module = GetModuleById(cell.moduleId);
            if (module == null)
            {
                Debug.LogWarning(
                    $"[ModularTerrainManager] 坐标 ({x},{z}) 指定的模块 id={cell.moduleId} 未找到，跳过加载。");
                return;
            }

            // 已加载则先卸载，便于重新加载最新数据
            if (loadedInstances.TryGetValue(key, out GameObject existing))
            {
                DestroyInstance(existing);
                loadedInstances.Remove(key);
            }

            GameObject inst = InstantiateModule(module.gameObject);
            inst.transform.SetParent(transform, true);

            Vector3 worldPos = transform.position + new Vector3(x * gridStepX, cell.height, z * gridStepZ);
            inst.transform.position = worldPos;
            inst.transform.rotation = Quaternion.Euler(0f, cell.rotation, 0f);
            inst.name = $"TerrainModule_{x}_{z}_id{cell.moduleId}";

            loadedInstances[key] = inst;
        }

        /// <summary>
        /// 卸载（销毁）网格坐标 (x,z) 处已实例化的地形模块。若该坐标无已加载实例则忽略。
        /// </summary>
        public void UnloadTerrainModule(int x, int z)
        {
            Vector2Int key = new Vector2Int(x, z);
            if (loadedInstances.TryGetValue(key, out GameObject inst))
            {
                DestroyInstance(inst);
                loadedInstances.Remove(key);
            }
        }

        /// <summary>
        /// 实例化模块预制体：编辑器内用 PrefabUtility 保持预制体连接，运行时用 Instantiate。
        /// </summary>
        private GameObject InstantiateModule(GameObject prefab)
        {
#if UNITY_EDITOR
            return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
#else
            return Instantiate(prefab);
#endif
        }

        /// <summary>
        /// 销毁已加载实例：编辑器非运行期用 Undo.DestroyObjectImmediate，运行期/运行时用 Destroy。
        /// </summary>
        private void DestroyInstance(GameObject go)
        {
            if (go == null) return;
#if UNITY_EDITOR
            if (UnityEditor.EditorApplication.isPlaying)
                Destroy(go);
            else
                UnityEditor.Undo.DestroyObjectImmediate(go);
#else
            Destroy(go);
#endif
        }

#if UNITY_EDITOR
        [ContextMenu("根据目录加载模块 (LoadModules)")]
        private void EditorLoadModules() { LoadModules(); }

        [ContextMenu("加载全部排布模块")]
        private void EditorLoadAll()
        {
            if (modules.Count == 0) LoadModules();
            LoadLayoutFromCsv();
            foreach (var k in new List<Vector2Int>(layout.Keys))
                LoadTerrainModule(k.x, k.y);
        }

        [ContextMenu("卸载全部排布模块")]
        private void EditorUnloadAll()
        {
            foreach (var k in new List<Vector2Int>(loadedInstances.Keys))
                UnloadTerrainModule(k.x, k.y);
        }
#endif

        [ContextMenu("收集场景中的地形模块 (CollectModules)")]
        private void EditorCollectModules() { CollectModules(); }
    }
}
