"""Unity Bridge 命令行入口。

用法:
    python -m unity_bridge tree                 # 打印当前场景物体层级树
    python -m unity_bridge tree --components     # 同时显示组件类型
    python -m unity_bridge tree --json           # 输出原始 JSON
    python -m unity_bridge list                  # 列出 Unity 侧所有可用命令
    python -m unity_bridge mesh-bounds Assets/.../Rock.fbx   # 计算网格/模型/预制体包围盒
    python -m unity_bridge screenshot Assets/Prefabs/Tree.prefab out/tree.png \
        --offset "3,2,5" [--orthographic] [--fov 50] [--width 1920] [--height 1080] \
        [--bg "0.2,0.2,0.2,1"] [--light 1.5]
    python -m unity_bridge terrain-sync --precision 0.5 \
        --dir Assets/ModularTerrain/Modules --dir Assets/ModularTerrain/Ramps
    python -m unity_bridge terrain-sync --read   # 读取 Unity 管理器中的全局配置
"""

from __future__ import annotations

import argparse
import configparser
import json
import sys
from pathlib import Path
from typing import List, Optional

from .client import DEFAULT_HOST, DEFAULT_PORT, UnityBridgeError, UnityClient


def _repo_root() -> Path:
    """cli.py 位于 <repo>/unity-python-bridge/python/unity_bridge/，向上 4 级即仓库根。"""
    return Path(__file__).resolve().parents[3]


def _load_ini_assets_path(ini_path: Path) -> Optional[str]:
    """读取 ini 中 [unity] assets_path（Unity 工程 Assets 绝对路径），不存在返回 None。"""
    if not ini_path.exists():
        return None
    cp = configparser.ConfigParser()
    try:
        cp.read(ini_path, encoding="utf-8")
        return cp.get("unity", "assets_path").strip()
    except (configparser.NoSectionError, configparser.NoOptionError, OSError):
        return None

# 树形绘制字符
_TEE = "├── "
_LAST = "└── "
_PIPE = "│   "
_SPACE = "    "


def render_tree(node: dict) -> List[str]:
    """把 C# 返回的场景树节点渲染成 ├── / └── 风格的文本行（根节点无前缀）。"""
    lines: List[str] = [_format_label(node)]
    children = node.get("children") or []
    for i, child in enumerate(children):
        is_last = i == len(children) - 1
        _render(child, "", _LAST if is_last else _TEE, lines)
    return lines


def _format_label(node: dict) -> str:
    label = node.get("name", "?")
    if not node.get("active", True):
        label += " (inactive)"
    if node.get("components"):
        label += "  [" + ", ".join(node["components"]) + "]"
    return label


def _render(node: dict, prefix: str, connector: str, out: List[str]) -> None:
    out.append(prefix + connector + _format_label(node))

    children = node.get("children") or []
    child_prefix = prefix + (_SPACE if connector == _LAST else _PIPE)
    for i, child in enumerate(children):
        is_last = i == len(children) - 1
        _render(child, child_prefix, _LAST if is_last else _TEE, out)


def _cmd_tree(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.scene_tree(components=args.components)

    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0

    print(f"Scene: {data.get('name', '?')}  ({data.get('rootCount', '?')} 个根物体)")
    for root in data.get("roots", []):
        for line in render_tree(root):
            print(line)
    return 0


def _cmd_list(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        commands = client.list_commands()
    for c in commands:
        name = c.get("name", "?")
        desc = c.get("description", "")
        print(f"{name:<24} {desc}")
    return 0


def _cmd_mesh_bounds(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.mesh_bounds(args.path)

    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0

    print(f"path  : {data.get('resolvedPath')}")
    print(f"type  : {data.get('type')}")
    print(f"bounds: {data.get('format')}")
    mn, mx, sz = data.get("min", {}), data.get("max", {}), data.get("size", {})
    print(f"  min : ({mn.get('x')}, {mn.get('y')}, {mn.get('z')})")
    print(f"  max : ({mx.get('x')}, {mx.get('y')}, {mx.get('z')})")
    print(f"  size: ({sz.get('x')}, {sz.get('y')}, {sz.get('z')})")
    return 0


def _parse_vec3(s: str) -> dict:
    parts = [float(x) for x in s.split(",")]
    if len(parts) != 3:
        raise ValueError("需要 3 个分量，格式 'x,y,z'")
    return {"x": parts[0], "y": parts[1], "z": parts[2]}


def _cmd_screenshot(args) -> int:
    try:
        offset = _parse_vec3(args.offset)
    except ValueError as e:
        print(f"[错误] offset 解析失败: {e}", file=sys.stderr)
        return 1

    if not args.output.lower().endswith(".png"):
        print(f"[错误] output 必须是 .png 文件路径（当前: {args.output}）", file=sys.stderr)
        return 1

    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.prefab_screenshot(
            path=args.path,
            offset=offset,
            output=args.output,
            orthographic=args.orthographic,
            fov=args.fov,
            width=args.width,
            height=args.height,
            bg=args.bg,
            light=args.light,
        )

    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0

    cp = data.get("cameraPosition", {})
    la = data.get("lookAt", {})
    print(f"prefab : {data.get('resolvedPath')}")
    print(f"output : {data.get('output')}")
    print(f"camera : {data.get('cameraType')}  {data.get('width')}x{data.get('height')}")
    print(f"camPos : ({cp.get('x')}, {cp.get('y')}, {cp.get('z')})")
    print(f"lookAt : ({la.get('x')}, {la.get('y')}, {la.get('z')})")
    light_val = data.get("fillLight", 0)
    print(f"light  : {light_val if light_val else '无补光'}")
    print(f"bytes  : {data.get('bytes')}")
    return 0


def _cmd_terrain_sync(args) -> int:
    # 读取模式：打印 Unity 管理器中存储的全局配置（唯一数据源，不读任何本地文件）
    if args.read:
        with UnityClient(args.host, args.port, args.timeout) as client:
            unity_cfg = client.get_terrain_config()

        print("=== Unity 管理器全局配置（唯一数据源） ===")
        print(f"  source           : {unity_cfg.get('source')}")
        print(f"  sizePrecision    : {unity_cfg.get('sizePrecision')}")
        print(f"  moduleDirectories: {unity_cfg.get('moduleDirectories')}")
        print(f"  moduleCount      : {unity_cfg.get('moduleCount')}")
        return 0

    # 写入模式：把命令行传入的配置写入 Unity 管理器预制体；Python 侧不保存任何本地副本
    if args.precision is None:
        print("[错误] 写入模式必须指定 --precision", file=sys.stderr)
        return 1
    precision = args.precision
    directories = list(args.dir or [])

    # 用 ini 中的 Assets 路径校验模块目录是否存在于磁盘（仅警告，不参与配置存储）
    repo = _repo_root()
    assets_path = _load_ini_assets_path(repo / "unity_project.ini")
    if assets_path:
        for d in directories:
            rel = d.lstrip("Assets/").lstrip("/")
            abs_d = Path(assets_path) / rel
            if not abs_d.exists():
                print(f"[警告] 模块目录在磁盘上不存在: {abs_d}", file=sys.stderr)

    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.sync_terrain_config(precision, directories)

    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0

    print(f"prefab    : {data.get('prefabPath')}")
    print(f"created   : {data.get('created')}")
    print(f"precision : {data.get('sizePrecision')}")
    print(f"dirs      : {data.get('moduleCount')} 个 -> {data.get('moduleDirectories')}")
    return 0


def _cmd_module_list(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.module_list()
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"precision : {data.get('precision')}")
    print(f"count     : {data.get('count')}")
    for m in data.get("modules", []):
        print(f"  id={m['id']:<3} sizeX={m['sizeX']} sizeZ={m['sizeZ']} "
              f"h(z+={m['heightZPlus']}, x+={m['heightXPlus']}, "
              f"z-={m['heightZMinus']}, x-={m['heightXMinus']})")
    return 0


def _cmd_module_size(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.module_size(args.id)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"id         : {data.get('id')}")
    print(f"lengthX    : {data.get('lengthX')}")
    print(f"widthZ     : {data.get('widthZ')}")
    print(f"heightZ+   : {data.get('heightZPlus')}")
    print(f"heightX+   : {data.get('heightXPlus')}")
    print(f"heightZ-   : {data.get('heightZMinus')}")
    print(f"heightX-   : {data.get('heightXMinus')}")
    print(f"maxHeight  : {data.get('maxHeight')}")
    print(f"precision  : {data.get('precision')}")
    print(f"isValidSize: {data.get('isValidSize')}")
    return 0


def _cmd_module_snap(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.module_snap(args.id)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"id         : {data.get('id')}")
    print(f"lengthX    : {data.get('lengthX')}")
    print(f"widthZ     : {data.get('widthZ')}")
    print(f"heightZ+   : {data.get('heightZPlus')}")
    print(f"heightX+   : {data.get('heightXPlus')}")
    print(f"heightZ-   : {data.get('heightZMinus')}")
    print(f"heightX-   : {data.get('heightXMinus')}")
    print(f"precision  : {data.get('precision')}  (已吸附)")
    return 0


def _cmd_module_set(args) -> int:
    fields = {}
    for key in ("sizeX", "length", "sizeZ", "width", "hZPlus", "hXPlus", "hZMinus", "hXMinus"):
        val = getattr(args, key, None)
        if val is not None:
            fields[key] = val
    if not fields:
        print("[错误] 未提供任何要设置的字段（如 --sizeX 6）", file=sys.stderr)
        return 1

    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.module_set(args.id, **fields)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"id      : {data.get('id')}")
    print(f"changed : {data.get('changed')}")
    print(f"sizeX   : {data.get('sizeX')}")
    print(f"sizeZ   : {data.get('sizeZ')}")
    print(f"hZ+ x+ z- x- : {data.get('heightZPlus')} {data.get('heightXPlus')} "
          f"{data.get('heightZMinus')} {data.get('heightXMinus')}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="unity-bridge",
        description="通过 Python 命令行操控 Unity Editor（TCP/JSON 协议）",
    )
    parser.add_argument("--host", default=DEFAULT_HOST, help=f"Unity 地址（默认 {DEFAULT_HOST}）")
    parser.add_argument("--port", type=int, default=DEFAULT_PORT, help=f"Unity 端口（默认 {DEFAULT_PORT}）")
    parser.add_argument("--timeout", type=float, default=10.0, help="连接/响应超时秒数（默认 10）")

    sub = parser.add_subparsers(dest="command", required=True)

    p_tree = sub.add_parser("tree", help="以树状结构打印当前场景中的物体名称")
    p_tree.add_argument("--components", action="store_true", help="同时显示每个物体的组件类型")
    p_tree.add_argument("--json", action="store_true", help="输出原始 JSON 而非树形文本")
    p_tree.set_defaults(func=_cmd_tree)

    p_list = sub.add_parser("list", aliases=["ls"], help="列出 Unity 侧所有已注册的命令")
    p_list.set_defaults(func=_cmd_list)

    p_bounds = sub.add_parser(
        "mesh-bounds", aliases=["bounds"],
        help="计算 Assets 中网格/模型/预制体的轴对齐包围盒")
    p_bounds.add_argument("path", help="目标在 Assets 中的相对路径（.mesh / 模型文件 / .prefab）")
    p_bounds.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_bounds.set_defaults(func=_cmd_mesh_bounds)

    p_shot = sub.add_parser(
        "screenshot", aliases=["shot"],
        help="将预制体复制到场景隔离位置并截图保存为 PNG")
    p_shot.add_argument("path", help="目标预制体在 Assets 中的相对路径（.prefab / 模型文件）")
    p_shot.add_argument("output", help="PNG 输出路径（必须以 .png 结尾）")
    p_shot.add_argument("--offset", required=True,
                        help="相机相对预制体的位置，格式 'x,y,z'（如 '3,2,5'）")
    p_shot.add_argument("--orthographic", action="store_true", help="使用正交相机（默认透视）")
    p_shot.add_argument("--fov", type=float, default=None,
                        help="视野：透视=fieldOfView，正交=orthographicSize（默认 Unity 默认）")
    p_shot.add_argument("--width", type=int, default=1920, help="输出图片宽（默认 1920）")
    p_shot.add_argument("--height", type=int, default=1080, help="输出图片高（默认 1080）")
    p_shot.add_argument("--bg", default=None,
                        help="背景色 'r,g,b[,a]'（0~1，默认透明）")
    p_shot.add_argument("--light", type=float, default=0.0,
                        help="补光强度（默认 0 不补光；>0 时追加与相机同向平行光）")
    p_shot.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_shot.set_defaults(func=_cmd_screenshot)

    p_sync = sub.add_parser(
        "terrain-sync", aliases=["tsync"],
        help="将 Python 端地形配置同步到 Unity 管理器预制体")
    p_sync.add_argument("--precision", type=float, default=None,
                        help="最小尺寸精度（正数）。写入模式必填")
    p_sync.add_argument("--dir", action="append", default=[],
                        help="模块目录（Assets 相对路径），可多次指定")
    p_sync.add_argument("--read", action="store_true",
                        help="读取模式：打印 Unity 管理器中的全局配置（不修改任何一侧）")
    p_sync.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_sync.set_defaults(func=_cmd_terrain_sync)

    p_mlist = sub.add_parser(
        "module-list", aliases=["mlist"],
        help="打印所有已加载地形模块的信息列表")
    p_mlist.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_mlist.set_defaults(func=_cmd_module_list)

    p_msize = sub.add_parser(
        "module-size", aliases=["msize"],
        help="计算指定 id 模块的尺寸（长宽/四边高度/最大高度/是否符合精度）")
    p_msize.add_argument("--id", type=int, required=True, help="目标模块 id")
    p_msize.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_msize.set_defaults(func=_cmd_module_size)

    p_msnap = sub.add_parser(
        "module-snap", aliases=["msnap"],
        help="把指定 id 模块的尺寸吸附到精度整数倍")
    p_msnap.add_argument("--id", type=int, required=True, help="目标模块 id")
    p_msnap.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_msnap.set_defaults(func=_cmd_module_snap)

    p_mset = sub.add_parser(
        "module-set", aliases=["mset"],
        help="按 id 设置模块指定字段（仅设置传入的参数，可同时设置多个）")
    p_mset.add_argument("--id", type=int, required=True, help="目标模块 id")
    p_mset.add_argument("--sizeX", type=float, default=None, help="设置 moduleSize.x（长/世界X）")
    p_mset.add_argument("--length", type=float, default=None, help="同 --sizeX（别名）")
    p_mset.add_argument("--sizeZ", type=float, default=None, help="设置 moduleSize.y（宽/世界Z）")
    p_mset.add_argument("--width", type=float, default=None, help="同 --sizeZ（别名）")
    p_mset.add_argument("--hZPlus", type=float, default=None, help="设置 +Z 边高度")
    p_mset.add_argument("--hXPlus", type=float, default=None, help="设置 +X 边高度")
    p_mset.add_argument("--hZMinus", type=float, default=None, help="设置 -Z 边高度")
    p_mset.add_argument("--hXMinus", type=float, default=None, help="设置 -X 边高度")
    p_mset.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_mset.set_defaults(func=_cmd_module_set)

    return parser


def main(argv: Optional[List[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return args.func(args)
    except UnityBridgeError as e:
        print(f"[错误] {e}", file=sys.stderr)
        return 1
    except ConnectionError as e:
        print(f"[错误] 连接失败: {e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
