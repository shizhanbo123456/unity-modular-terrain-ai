#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace UnityPythonBridge.Commands
{
    /// <summary>
    /// 预制体截图命令：把目标预制体复制到场景中的隔离位置，创建相机进行截图并保存为 PNG，
    /// 完成后销毁临时复制的预制体与创建的相机。
    ///
    /// 参数:
    ///   path (string)        - 目标预制体在 Assets 中的相对路径（.prefab 或模型文件）
    ///   offset (Vector3)     - 相机相对于预制体位置 (9999,9999,9999) 的偏移，{x,y,z} / [x,y,z] / "x,y,z"
    ///   output (string)      - PNG 输出路径（必须以 .png 结尾）
    ///   orthographic (bool)  - 是否使用正交相机，默认 false（透视）
    ///   fov (number)         - 视野：透视时=fieldOfView，正交时=orthographicSize；默认使用 Unity 默认大小
    ///   width (int)          - 输出图片宽，默认 1920
    ///   height (int)         - 输出图片高，默认 1080
    ///   bg (string)          - 背景色 "r,g,b[,a]"（0~1），默认透明
    ///
    /// 返回:
    ///   { path, resolvedPath, output, cameraType, width, height, cameraPosition{x,y,z}, lookAt{x,y,z}, bytes }
    /// </summary>
    public static class PrefabScreenshotCommand
    {
        // 远离原点的隔离位置，避免与场景中已有物体重叠 / 碰撞
        private static readonly Vector3 Isolation = new Vector3(9999f, 9999f, 9999f);

        [BridgeCommand("prefab.screenshot",
            "将目标预制体复制到场景隔离位置并截图保存为 PNG。参数: path(string), offset{x,y,z}, " +
            "output(string,.png), orthographic(bool,默认false), fov(number,默认Unity默认), " +
            "width(int,默认1920), height(int,默认1080), bg(string r,g,b,a,默认透明)")]
        public static object Capture(BridgeContext ctx, JObject args)
        {
            var path = args.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("prefab.screenshot 需要参数 path（预制体在 Assets 中的相对路径）");

            var output = args.Value<string>("output");
            if (string.IsNullOrEmpty(output))
                throw new ArgumentException("prefab.screenshot 需要参数 output（PNG 输出路径）");
            if (!output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("output 必须是 .png 文件路径（当前: " + output + "）");

            var offset = ParseVector3(args["offset"], "offset");
            bool orthographic = Has(args, "orthographic") && args.Value<bool>("orthographic");
            double? fov = Has(args, "fov") ? (double?)args.Value<double>("fov") : null;
            int width = Has(args, "width") ? args.Value<int>("width") : 1920;
            int height = Has(args, "height") ? args.Value<int>("height") : 1080;
            Color bg = ParseColor(args["bg"]);

            var resolved = Normalize(path);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(resolved);
            if (go == null)
                throw new InvalidOperationException(
                    $"找不到预制体/模型: {resolved}（需为 Assets 下的 .prefab 或模型文件）");

            var outDir = Path.GetDirectoryName(Path.GetFullPath(output));
            if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

            GameObject instance = null;
            GameObject camGo = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                // 1) 复制到场景隔离位置（默认旋转/缩放，保证只看几何本身）
                instance = (GameObject)PrefabUtility.InstantiatePrefab(go);
                instance.transform.position = Isolation;
                instance.transform.rotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                // 2) 创建相机：移动到相对位置后 LookAt 预制体
                camGo = new GameObject("BridgeScreenshotCamera");
                var cam = camGo.AddComponent<Camera>();
                var camPos = Isolation + offset;
                camGo.transform.position = camPos;
                camGo.transform.LookAt(Isolation);

                cam.orthographic = orthographic;
                if (fov.HasValue)
                {
                    if (orthographic) cam.orthographicSize = (float)fov.Value;
                    else cam.fieldOfView = (float)fov.Value;
                }
                cam.clearFlags = CameraClearFlags.Color;
                cam.backgroundColor = bg;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 5000f;
                cam.aspect = (float)width / height;
                cam.targetTexture = null;

                // 3) 渲染到 RenderTexture 并回读为 PNG
                rt = RenderTexture.GetTemporary(width, height, 24,
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = null;

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(output, png);

                return new JObject
                {
                    ["path"] = path,
                    ["resolvedPath"] = resolved,
                    ["output"] = Path.GetFullPath(output),
                    ["cameraType"] = orthographic ? "orthographic" : "perspective",
                    ["width"] = width,
                    ["height"] = height,
                    ["cameraPosition"] = VecToJ(camPos),
                    ["lookAt"] = VecToJ(Isolation),
                    ["bytes"] = png.Length,
                };
            }
            finally
            {
                // 4) 无论成功与否，销毁临时对象，避免污染场景
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static bool Has(JObject args, string key)
        {
            var t = args[key];
            return t != null && t.Type != JTokenType.Null;
        }

        private static Vector3 ParseVector3(JToken token, string name)
        {
            if (token == null || token.Type == JTokenType.Null)
                throw new ArgumentException($"prefab.screenshot 需要参数 {name}（Vector3）");

            if (token.Type == JTokenType.Object)
                return new Vector3(token.Value<float>("x"), token.Value<float>("y"), token.Value<float>("z"));

            if (token.Type == JTokenType.Array)
            {
                var a = (JArray)token;
                if (a.Count != 3) throw new ArgumentException($"{name} 数组必须是 3 个元素 [x,y,z]");
                return new Vector3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
            }

            if (token.Type == JTokenType.String)
            {
                var parts = token.Value<string>().Split(',');
                if (parts.Length != 3) throw new ArgumentException($"{name} 字符串格式应为 'x,y,z'");
                var ci = CultureInfo.InvariantCulture;
                return new Vector3(
                    float.Parse(parts[0].Trim(), ci),
                    float.Parse(parts[1].Trim(), ci),
                    float.Parse(parts[2].Trim(), ci));
            }

            throw new ArgumentException($"{name} 无法解析为 Vector3");
        }

        private static Color ParseColor(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new Color(0f, 0f, 0f, 0f); // 默认透明背景

            if (token.Type == JTokenType.String)
            {
                var parts = token.Value<string>().Split(',');
                if (parts.Length < 3 || parts.Length > 4)
                    throw new ArgumentException("bg 格式应为 'r,g,b[,a]'（0~1）");
                var ci = CultureInfo.InvariantCulture;
                float r = float.Parse(parts[0].Trim(), ci);
                float g = float.Parse(parts[1].Trim(), ci);
                float b = float.Parse(parts[2].Trim(), ci);
                float a = parts.Length == 4 ? float.Parse(parts[3].Trim(), ci) : 1f;
                return new Color(r, g, b, a);
            }

            if (token.Type == JTokenType.Object)
            {
                float a = (token["a"] != null && token["a"].Type != JTokenType.Null)
                    ? token.Value<float>("a") : 1f;
                return new Color(token.Value<float>("r"), token.Value<float>("g"), token.Value<float>("b"), a);
            }

            throw new ArgumentException("bg 无法解析为颜色");
        }

        private static JObject VecToJ(Vector3 v) =>
            new JObject { ["x"] = v.x, ["y"] = v.y, ["z"] = v.z };

        private static string Normalize(string path)
        {
            var p = path.Replace('\\', '/').Trim();
            if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                p = "Assets/" + p.TrimStart('/');
            return p;
        }
    }
}
#endif // UNITY_EDITOR
