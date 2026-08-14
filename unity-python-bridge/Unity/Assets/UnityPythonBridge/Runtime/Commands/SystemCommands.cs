#if UNITY_EDITOR
using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace UnityPythonBridge.Commands
{
    /// <summary>系统级命令：连通性测试、命令列表等。</summary>
    public static class SystemCommands
    {
        [BridgeCommand("bridge.ping", "连通性测试，成功返回 pong 与服务器时间")]
        public static object Ping(BridgeContext ctx, JObject args)
        {
            return new
            {
                pong = true,
                time = DateTime.UtcNow.ToString("o")
            };
        }

        [BridgeCommand("bridge.list_commands", "列出所有已通过反射注册的命令")]
        public static object ListCommands(BridgeContext ctx, JObject args)
        {
            var arr = new JArray();
            foreach (var kv in BridgeDispatcher.CommandMap.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                arr.Add(new JObject
                {
                    ["name"] = kv.Key,
                    ["description"] = kv.Value.Description
                });
            }
            return arr;
        }
    }
}
#endif // UNITY_EDITOR
