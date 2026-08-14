using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityPythonBridge.Commands
{
    /// <summary>
    /// 场景树命令：以树状结构返回当前激活场景中的物体层级。
    /// 参数:
    ///   components (bool, 可选) - 为 true 时每个节点附带组件类型列表
    /// 返回结构:
    ///   { type, name, path, buildIndex, rootCount, roots: [ { name, active, children: [...] } ] }
    /// </summary>
    public static class SceneTreeCommand
    {
        [BridgeCommand("scene.tree",
            "以树状结构返回当前场景中的物体层级。参数: components(bool) 是否附带组件类型")]
        public static object Tree(BridgeContext ctx, JObject args)
        {
            bool withComponents = args.Value<bool?>("components") ?? false;

            var scene = SceneManager.GetActiveScene();

            var root = new JObject
            {
                ["type"] = "scene",
                ["name"] = scene.name,
                ["path"] = scene.path,
                ["buildIndex"] = scene.buildIndex,
                ["rootCount"] = scene.rootCount,
                ["roots"] = new JArray()
            };

            var roots = (JArray)root["roots"];
            foreach (var go in scene.GetRootGameObjects())
            {
                roots.Add(Describe(go.transform, withComponents));
            }

            return root;
        }

        private static JObject Describe(Transform t, bool withComponents)
        {
            var node = new JObject
            {
                ["name"] = t.gameObject.name,
                ["active"] = t.gameObject.activeSelf,
                ["children"] = new JArray()
            };

            if (withComponents)
            {
                var comps = new JArray();
                foreach (var c in t.gameObject.GetComponents<Component>())
                {
                    if (c == null) continue;
                    comps.Add(c.GetType().Name);
                }
                node["components"] = comps;
            }

            var children = (JArray)node["children"];
            for (var i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child == null) continue;
                children.Add(Describe(child, withComponents));
            }

            return node;
        }
    }
}
