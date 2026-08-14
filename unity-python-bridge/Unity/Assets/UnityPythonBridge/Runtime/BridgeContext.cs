namespace UnityPythonBridge
{
    /// <summary>
    /// 命令执行上下文。每个请求执行时创建一个。
    /// 预留字段：后续可注入日志、连接信息、全局配置等，避免修改命令签名。
    /// </summary>
    public class BridgeContext
    {
        // 预留：public TextWriter Log { get; set; }
        // 预留：public string ClientId { get; set; }
    }

    /// <summary>
    /// 命令处理器委托。与 [BridgeCommand] 标记的方法签名一致。
    /// </summary>
    /// <param name="ctx">执行上下文</param>
    /// <param name="args">请求参数（JSON 对象，可空）</param>
    /// <returns>任意可被 JSON 序列化的结果（JToken/匿名对象/基本类型）</returns>
    public delegate object BridgeCommandHandler(BridgeContext ctx, Newtonsoft.Json.Linq.JObject args);

    /// <summary>已注册命令的元信息。</summary>
    public sealed class BridgeCommandInfo
    {
        public string Name { get; }
        public string Description { get; }
        public BridgeCommandHandler Handler { get; }

        public BridgeCommandInfo(string name, string description, BridgeCommandHandler handler)
        {
            Name = name;
            Description = description;
            Handler = handler;
        }
    }
}
