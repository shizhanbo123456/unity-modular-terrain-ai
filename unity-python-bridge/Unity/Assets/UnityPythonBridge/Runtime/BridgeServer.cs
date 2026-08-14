using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityPythonBridge
{
    /// <summary>
    /// TCP 桥接服务器。只监听 127.0.0.1（本机），协议为单行 JSON：
    ///   请求:  {"id": 1, "cmd": "scene.tree", "args": {...}}
    ///   响应:  {"id": 1, "ok": true,  "data": {...}}
    ///         {"id": 1, "ok": false, "error": "..."}
    /// 后台线程负责监听与收发，命令执行投递到主线程队列（见 MainThreadRunner）。
    /// </summary>
    public static class BridgeServer
    {
        public const int DefaultPort = 21927;

        private static TcpListener _listener;
        private static Thread _listenThread;
        private static volatile bool _running;

        public static int Port { get; private set; } = DefaultPort;
        public static bool IsRunning => _running;

        public static void Start(int port = DefaultPort)
        {
            if (_running) return;

            Port = port;
            _running = true;
            _listenThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "UnityPythonBridge-TCP"
            };
            _listenThread.Start();
            Debug.Log($"[UnityPythonBridge] 服务器已启动，监听 127.0.0.1:{Port}（仅本机可访问）");
        }

        public static void Stop()
        {
            if (!_running) return;
            _running = false;
            try { _listener?.Stop(); } catch (Exception) { /* 忽略 */ }
            _listener = null;
            Debug.Log("[UnityPythonBridge] 服务器已停止");
        }

        private static void ListenLoop()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();

                while (_running)
                {
                    TcpClient client;
                    try
                    {
                        client = _listener.AcceptTcpClient();
                    }
                    catch (SocketException)
                    {
                        break; // 监听被主动停止
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }

                    var thread = new Thread(() => HandleClient(client))
                    {
                        IsBackground = true,
                        Name = "UnityPythonBridge-Session"
                    };
                    thread.Start();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityPythonBridge] 监听异常: {e}");
            }
            finally
            {
                _running = false;
            }
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    JObject request;
                    try
                    {
                        request = JObject.Parse(line);
                    }
                    catch (Exception e)
                    {
                        WriteError(writer, null, "JSON 解析失败: " + e.Message);
                        continue;
                    }

                    var cmd = request.Value<string>("cmd");
                    var id = request["id"];
                    var args = request["args"] as JObject ?? new JObject();

                    // 关键：切到主线程执行，避免跨线程访问 Unity API
                    MainThreadRunner.Enqueue(() =>
                    {
                        try
                        {
                            var data = BridgeDispatcher.Execute(cmd, args);
                            var resp = new JObject
                            {
                                ["id"] = id,
                                ["ok"] = true,
                                ["data"] = data != null ? JToken.FromObject(data) : JValue.CreateNull()
                            };
                            writer.WriteLine(resp.ToString(Formatting.None));
                        }
                        catch (Exception e)
                        {
                            WriteError(writer, id, e.Message);
                        }
                    });
                }
            }
        }

        private static void WriteError(TextWriter writer, JToken id, string message)
        {
            try
            {
                var resp = new JObject { ["id"] = id, ["ok"] = false, ["error"] = message };
                writer.WriteLine(resp.ToString(Formatting.None));
            }
            catch (Exception)
            {
                // 连接已断开，忽略
            }
        }
    }
}
