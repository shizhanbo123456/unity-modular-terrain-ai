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
                 "也可由 CollectModules() 收集场景中的实例。")]
        public List<ModularTerrainModule> modules = new List<ModularTerrainModule>();

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

#if UNITY_EDITOR
        [ContextMenu("根据目录加载模块 (LoadModules)")]
        private void EditorLoadModules() { LoadModules(); }
#endif

        [ContextMenu("收集场景中的地形模块 (CollectModules)")]
        private void EditorCollectModules() { CollectModules(); }
    }
}
