"""Mock Unity Bridge 服务器 —— 在没有 Unity 环境时验证 Python 客户端/CLI。

行为完全复刻 Unity 侧 BridgeServer 的协议（单行 JSON）。
用法:
    python scripts/mock_unity_server.py [--port 21927]
"""

import argparse
import json
import socket
import threading

# 模拟 Unity 场景树（与 C# SceneTreeCommand 返回结构一致）
MOCK_SCENE = {
    "type": "scene",
    "name": "DemoScene",
    "path": "Assets/Scenes/Demo.unity",
    "buildIndex": 0,
    "rootCount": 3,
    "roots": [
        {"name": "Main Camera", "active": True, "components": ["Transform", "Camera", "AudioListener"], "children": []},
        {"name": "Directional Light", "active": True, "components": ["Transform", "Light"], "children": []},
        {
            "name": "Player", "active": True,
            "components": ["Transform", "CharacterController", "PlayerController"],
            "children": [
                {"name": "Body", "active": True, "components": ["Transform", "Animator"],
                 "children": [
                     {"name": "LeftArm", "active": True, "components": ["Transform"], "children": []},
                     {"name": "RightArm", "active": True, "components": ["Transform"], "children": []},
                 ]},
                {"name": "Head", "active": True, "components": ["Transform", "SkinnedMeshRenderer"],
                 "children": [
                     {"name": "Hat", "active": False, "components": ["Transform"], "children": []},
                 ]},
            ],
        },
    ],
}

COMMANDS = [
    {"name": "bridge.ping", "description": "连通性测试，成功返回 pong 与服务器时间"},
    {"name": "bridge.list_commands", "description": "列出所有已通过反射注册的命令"},
    {"name": "scene.tree", "description": "以树状结构返回当前场景中的物体层级。参数: components(bool)"},
    {"name": "mesh.bounds", "description": "计算 Assets 中网格/模型/预制体的轴对齐包围盒。参数: path(string)"},
]


def handle_client(client: socket.socket) -> None:
    with client:
        stream = client.makefile("rwb", buffering=0)
        while True:
            line = stream.readline()
            if not line:
                break
            try:
                req = json.loads(line.decode("utf-8"))
            except json.JSONDecodeError:
                stream.write((json.dumps({"ok": False, "error": "JSON 解析失败"}) + "\n").encode("utf-8"))
                continue

            cmd = req.get("cmd")
            args = req.get("args") or {}
            if cmd == "bridge.ping":
                data = {"pong": True, "time": "2026-08-14T03:00:00Z"}
            elif cmd == "bridge.list_commands":
                data = COMMANDS
            elif cmd == "scene.tree":
                data = json.loads(json.dumps(MOCK_SCENE))  # 深拷贝，避免污染全局
                if not args.get("components"):
                    for root in data["roots"]:
                        strip_components(root)
            elif cmd == "mesh.bounds":
                data = mock_mesh_bounds(args.get("path", ""))
            else:
                data = None

            resp = {"id": req.get("id"), "ok": True, "data": data}
            stream.write((json.dumps(resp, ensure_ascii=False) + "\n").encode("utf-8"))


def strip_components(node) -> None:
    node.pop("components", None)
    for child in node.get("children", []):
        strip_components(child)


def mock_mesh_bounds(path: str) -> dict:
    """离线模拟 mesh.bounds：返回与示例一致的包围盒，并回显传入路径。

    真实计算由 Unity 侧 AssetDatabase + mesh.bounds / renderer.bounds 完成；
    此处仅用于在无 Unity 环境时验证 Python 客户端与协议。
    """
    resolved = path if path.startswith("Assets/") else "Assets/" + path.lstrip("/")
    ext = resolved.lower().rsplit(".", 1)[-1] if "." in resolved else ""
    type_name = "prefab" if ext == "prefab" else ("mesh" if ext == "mesh" else "model")
    return {
        "path": path,
        "resolvedPath": resolved,
        "type": type_name,
        "min": {"x": -2, "y": -0.5, "z": 1},
        "max": {"x": 6, "y": 2, "z": 6},
        "center": {"x": 2, "y": 0.75, "z": 3.5},
        "size": {"x": 8, "y": 2.5, "z": 5},
        "format": "x:-2~6, y:-0.5~2, z:1~6",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Mock Unity Bridge 服务器")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=21927)
    args = parser.parse_args()

    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((args.host, args.port))
    server.listen(5)
    print(f"[mock] Unity Bridge 模拟服务器已启动: {args.host}:{args.port} (Ctrl+C 退出)")

    try:
        while True:
            client, addr = server.accept()
            print(f"[mock] 新连接: {addr}")
            threading.Thread(target=handle_client, args=(client,), daemon=True).start()
    except KeyboardInterrupt:
        print("\n[mock] 已退出")
    finally:
        server.close()


if __name__ == "__main__":
    main()
