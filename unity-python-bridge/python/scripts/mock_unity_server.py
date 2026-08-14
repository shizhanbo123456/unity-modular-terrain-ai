"""Mock Unity Bridge 服务器 —— 在没有 Unity 环境时验证 Python 客户端/CLI。

行为完全复刻 Unity 侧 BridgeServer 的协议（单行 JSON）。
用法:
    python scripts/mock_unity_server.py [--port 21927]
"""

import argparse
import json
import os
import socket
import struct
import threading
import zlib

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
    {"name": "prefab.screenshot", "description": "将预制体复制到场景隔离位置并截图保存为 PNG。参数: path(string), offset{x,y,z}, output(string,.png), orthographic(bool), fov(number), width(int), height(int), bg(string)"},
    {"name": "terrain.config_get", "description": "读取 Unity 管理器预制体中的全局模块配置。无参数"},
    {"name": "terrain.config_set", "description": "将全局模块配置写入 Unity 管理器预制体。参数: sizePrecision(number>0), moduleDirectories(array<string>)"},
    {"name": "terrain.module_list", "description": "打印模块信息列表（含 description）。无参数"},
    {"name": "terrain.module_size", "description": "计算指定 id 模块的尺寸。参数: id(int)"},
    {"name": "terrain.module_snap", "description": "把指定 id 模块的尺寸吸附到精度整数倍。参数: id(int)"},
    {"name": "terrain.module_set", "description": "按 id 设置模块指定字段。参数: id(int), sizeX/length, sizeZ/width, hZPlus, hXPlus, hZMinus, hXMinus(float), description(string)"},
    {"name": "terrain.layout_get", "description": "读取范围内地形排布。参数: xmin,zmin,xmax,zmax(int，均可省略；省略返回全部)"},
    {"name": "terrain.layout_set", "description": "写入单个排布（Python 侧写入前强制校验相邻高度无缝拼接）。参数: x,z(int 网格坐标), moduleId(int), rotation(0/90/180/270 俯视顺时针), height(float)"},
    {"name": "terrain.layout_clear", "description": "清空地形排布，回到默认空 CSV。无参数"},
]

# 离线模拟的地形模块与全局配置（仅用于无 Unity 环境联调）
MOCK_MODULES = [
    {"id": 1, "sizeX": 10, "sizeZ": 10, "heightZPlus": 1, "heightXPlus": 1,
     "heightZMinus": 1, "heightXMinus": 1, "description": "标准平地 10x10（四边等高）"},
    {"id": 2, "sizeX": 20, "sizeZ": 10, "heightZPlus": 2, "heightXPlus": 2,
     "heightZMinus": 2, "heightXMinus": 2, "description": "加高平地 20x10（四边等高）"},
    {"id": 3, "sizeX": 10, "sizeZ": 10, "heightZPlus": 3, "heightXPlus": 1,
     "heightZMinus": 1, "heightXMinus": 1, "description": "北高南低斜坡 10x10（+Z 边局部高 3）"},
]
MOCK_UNITY_CONFIG = {"sizePrecision": 0.5, "moduleDirectories": ["Assets/ModularTerrain/Modules"]}
MOCK_LAYOUT: list = []  # 离线模拟的地形排布：[{x,z,moduleId,rotation,height}, ...]


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
            try:
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
                elif cmd == "prefab.screenshot":
                    data = mock_screenshot(args)
                elif cmd == "terrain.config_get":
                    data = mock_config_get()
                elif cmd == "terrain.config_set":
                    data = mock_config_set(args)
                elif cmd == "terrain.module_list":
                    data = mock_module_list()
                elif cmd == "terrain.module_size":
                    data = mock_module_size(args)
                elif cmd == "terrain.module_snap":
                    data = mock_module_snap(args)
                elif cmd == "terrain.module_set":
                    data = mock_module_set(args)
                elif cmd == "terrain.layout_get":
                    data = mock_layout_get(args)
                elif cmd == "terrain.layout_set":
                    data = mock_layout_set(args)
                elif cmd == "terrain.layout_clear":
                    data = mock_layout_clear()
                else:
                    raise KeyError(f"未知命令: {cmd}")
                resp = {"id": req.get("id"), "ok": True, "data": data}
            except Exception as e:
                resp = {"id": req.get("id"), "ok": False, "error": str(e)}

            stream.write((json.dumps(resp, ensure_ascii=False) + "\n").encode("utf-8"))


def strip_components(node) -> None:
    node.pop("components", None)
    for child in node.get("children", []):
        strip_components(child)


def _png_chunk(tag: bytes, data: bytes) -> bytes:
    chunk = tag + data
    return struct.pack(">I", len(data)) + chunk + struct.pack(">I", zlib.crc32(chunk) & 0xFFFFFFFF)


def make_png(width: int, height: int, rgba: bytes) -> bytes:
    """生成一个最小合法的 RGBA PNG（仅用于离线联调，不代表真实渲染结果）。"""
    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)  # 8-bit, RGBA
    raw = bytearray()
    for y in range(height):
        raw.append(0)  # 每行滤波字节
        raw += rgba[y * width * 4:(y + 1) * width * 4]
    idat = zlib.compress(bytes(raw))
    return sig + _png_chunk(b"IHDR", ihdr) + _png_chunk(b"IDAT", idat) + _png_chunk(b"IEND", b"")


def mock_screenshot(args: dict) -> dict:
    """离线模拟 prefab.screenshot：生成占位 PNG 并回显相机位置/朝向。

    真实渲染由 Unity 侧完成（RenderTexture + cam.Render + EncodeToPNG）；
    此处仅用于在无 Unity 环境时验证 Python 客户端、参数校验与文件写出逻辑。
    """
    path = args.get("path", "")
    offset = args.get("offset") or {"x": 0, "y": 0, "z": 0}
    output = args.get("output", "")
    if not output.lower().endswith(".png"):
        raise ValueError("output 必须是 .png 文件路径")

    orthographic = bool(args.get("orthographic", False))
    width = int(args.get("width", 1920))
    height = int(args.get("height", 1080))
    light = float(args.get("light", 0.0) or 0.0)

    iso = {"x": 9999, "y": 9999, "z": 9999}
    cam_pos = {k: iso[k] + float(offset.get(k, 0)) for k in ("x", "y", "z")}

    out_dir = os.path.dirname(os.path.abspath(output))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
    png = make_png(width, height, bytes([40, 120, 130, 255]) * (width * height))
    with open(output, "wb") as f:
        f.write(png)

    return {
        "path": path,
        "resolvedPath": path if path.startswith("Assets/") else "Assets/" + path.lstrip("/"),
        "output": os.path.abspath(output),
        "cameraType": "orthographic" if orthographic else "perspective",
        "width": width,
        "height": height,
        "cameraPosition": cam_pos,
        "lookAt": iso,
        "fillLight": light if light > 0 else 0,
        "bytes": len(png),
    }


def mock_config_get() -> dict:
    """离线模拟 terrain.config_get：返回 Unity 当前全局配置。"""
    return {
        "source": "unity",
        "sizePrecision": MOCK_UNITY_CONFIG["sizePrecision"],
        "moduleDirectories": MOCK_UNITY_CONFIG["moduleDirectories"],
        "moduleCount": len(MOCK_UNITY_CONFIG["moduleDirectories"]),
    }


def mock_config_set(args: dict) -> dict:
    """离线模拟 terrain.config_set：回显并记忆全局配置。"""
    size_precision = float(args.get("sizePrecision", 0.5))
    directories = list(args.get("moduleDirectories", []))
    if size_precision <= 0:
        raise ValueError("sizePrecision 必须为正数")
    MOCK_UNITY_CONFIG["sizePrecision"] = size_precision
    MOCK_UNITY_CONFIG["moduleDirectories"] = directories
    return {
        "prefabPath": "Assets/ModularTerrainManager.prefab",
        "created": True,
        "sizePrecision": size_precision,
        "moduleDirectories": directories,
        "moduleCount": len(directories),
    }


def _find_mock_module(module_id: int) -> dict:
    for m in MOCK_MODULES:
        if m["id"] == module_id:
            return m
    raise KeyError(f"未找到 id={module_id} 的模块")


def mock_module_list() -> dict:
    """离线模拟 terrain.module_list。"""
    return {
        "count": len(MOCK_MODULES),
        "precision": MOCK_UNITY_CONFIG["sizePrecision"],
        "modules": [dict(m) for m in MOCK_MODULES],
    }


def mock_module_size(args: dict) -> dict:
    """离线模拟 terrain.module_size。"""
    module_id = int(args["id"])
    m = _find_mock_module(module_id)
    max_h = max(m["heightZPlus"], m["heightXPlus"], m["heightZMinus"], m["heightXMinus"])
    precision = MOCK_UNITY_CONFIG["sizePrecision"]
    valid = (abs(round(m["sizeX"] / precision) - m["sizeX"] / precision) < 1e-4
             and abs(round(m["sizeZ"] / precision) - m["sizeZ"] / precision) < 1e-4)
    return {
        "id": m["id"], "lengthX": m["sizeX"], "widthZ": m["sizeZ"],
        "heightZPlus": m["heightZPlus"], "heightXPlus": m["heightXPlus"],
        "heightZMinus": m["heightZMinus"], "heightXMinus": m["heightXMinus"],
        "maxHeight": max_h, "precision": precision, "isValidSize": valid,
    }


def mock_module_snap(args: dict) -> dict:
    """离线模拟 terrain.module_snap：把尺寸吸附到精度整数倍。"""
    module_id = int(args["id"])
    m = _find_mock_module(module_id)
    precision = MOCK_UNITY_CONFIG["sizePrecision"]

    def snap(v):
        return round(v / precision) * precision

    m["sizeX"] = snap(m["sizeX"])
    m["sizeZ"] = snap(m["sizeZ"])
    m["heightZPlus"] = snap(m["heightZPlus"])
    m["heightXPlus"] = snap(m["heightXPlus"])
    m["heightZMinus"] = snap(m["heightZMinus"])
    m["heightXMinus"] = snap(m["heightXMinus"])
    return {
        "id": m["id"], "lengthX": m["sizeX"], "widthZ": m["sizeZ"],
        "heightZPlus": m["heightZPlus"], "heightXPlus": m["heightXPlus"],
        "heightZMinus": m["heightZMinus"], "heightXMinus": m["heightXMinus"],
        "precision": precision, "snapped": True,
    }


def mock_module_set(args: dict) -> dict:
    """离线模拟 terrain.module_set：仅设置传入的字段。"""
    module_id = int(args["id"])
    m = _find_mock_module(module_id)
    changed = []
    for key in ("sizeX", "length", "sizeZ", "width", "hZPlus", "hXPlus", "hZMinus", "hXMinus"):
        if key in args and args[key] is not None:
            val = float(args[key])
            if key in ("sizeX", "length"):
                m["sizeX"] = val
            elif key in ("sizeZ", "width"):
                m["sizeZ"] = val
            else:
                m[key] = val
            changed.append(key)
    if "description" in args and args["description"] is not None:
        m["description"] = str(args["description"])
        changed.append("description")
    if not changed:
        raise ValueError("未提供任何要设置的字段")
    return {
        "id": m["id"], "changed": changed,
        "sizeX": m["sizeX"], "sizeZ": m["sizeZ"],
        "heightZPlus": m["heightZPlus"], "heightXPlus": m["heightXPlus"],
        "heightZMinus": m["heightZMinus"], "heightXMinus": m["heightXMinus"],
        "description": m["description"],
    }


def mock_layout_get(args: dict) -> dict:
    """离线模拟 terrain.layout_get：按范围过滤排布（省略范围返回全部）。"""
    has_range = all(k in args for k in ("xmin", "zmin", "xmax", "zmax"))
    xmin = int(args["xmin"]) if has_range else None
    zmin = int(args["zmin"]) if has_range else None
    xmax = int(args["xmax"]) if has_range else None
    zmax = int(args["zmax"]) if has_range else None

    entries = []
    for e in MOCK_LAYOUT:
        if has_range and not (xmin <= e["x"] <= xmax and zmin <= e["z"] <= zmax):
            continue
        entries.append(dict(e))
    return {
        "count": len(entries),
        "range": {"xmin": xmin if has_range else "all",
                  "zmin": zmin if has_range else "all",
                  "xmax": xmax if has_range else "all",
                  "zmax": zmax if has_range else "all"},
        "entries": entries,
        "csvPath": "Assets/ModularTerrain/Resources/TerrainLayout.csv",
    }


def mock_layout_set(args: dict) -> dict:
    """离线模拟 terrain.layout_set：按 (x,z) 写入/覆盖单条排布。"""
    x = int(args["x"]); z = int(args["z"])
    module_id = int(args["moduleId"]); rotation = int(args["rotation"]); height = float(args["height"])
    if rotation not in (0, 90, 180, 270):
        raise ValueError("rotation 仅允许 0/90/180/270（俯视视角顺时针）")

    found = None
    for e in MOCK_LAYOUT:
        if e["x"] == x and e["z"] == z:
            found = e
            break
    created = found is None
    if found is None:
        found = {"x": x, "z": z}
        MOCK_LAYOUT.append(found)
    found["x"] = x; found["z"] = z; found["moduleId"] = module_id
    found["rotation"] = rotation; found["height"] = height
    return {
        "csvPath": "Assets/ModularTerrain/Resources/TerrainLayout.csv",
        "created": created, "x": x, "z": z, "moduleId": module_id,
        "rotation": rotation, "height": height, "total": len(MOCK_LAYOUT),
    }


def mock_layout_clear() -> dict:
    """离线模拟 terrain.layout_clear：清空排布。"""
    MOCK_LAYOUT.clear()
    return {
        "csvPath": "Assets/ModularTerrain/Resources/TerrainLayout.csv",
        "cleared": True, "total": 0,
    }


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
