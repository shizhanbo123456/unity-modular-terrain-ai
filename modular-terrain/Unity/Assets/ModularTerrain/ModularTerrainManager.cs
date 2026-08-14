using System.Collections.Generic;
using UnityEngine;

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
    ///        场景中注册 / 收集到的所有地形模块组件。
    ///
    /// 注意：本类为运行时组件（不依赖 UnityEditor），可直接随场景进入 Player；
    ///       Gizmos 绘制逻辑则在 ModularTerrainModule 内用 #if UNITY_EDITOR 隔离。
    /// </summary>
    public class ModularTerrainManager : MonoBehaviour
    {
        [Tooltip("最小尺寸精度。之后处理的所有尺寸都必须是该数的整数倍。")]
        public float sizePrecision = 0.5f;

        [Tooltip("模块（prefab / 资源）所在的目录列表（Assets 相对路径）。")]
        public List<string> moduleDirectories = new List<string>();

        [Tooltip("场景中所有地形模块组件。可由 CollectModules 自动收集，或在 Inspector 中手动指定。")]
        public List<ModularTerrainModule> modules = new List<ModularTerrainModule>();

        /// <summary>
        /// 收集场景中所有 ModularTerrainModule（含未激活物体）并写入 <see cref="modules"/>。
        /// </summary>
        public void CollectModules()
        {
            modules = new List<ModularTerrainModule>(
                FindObjectsOfType<ModularTerrainModule>(true));
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

        [ContextMenu("收集场景中的地形模块")]
        private void EditorCollectModules()
        {
            CollectModules();
        }
    }
}
