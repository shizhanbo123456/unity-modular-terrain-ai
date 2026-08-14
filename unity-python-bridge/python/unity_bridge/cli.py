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
    python -m unity_bridge terrain-config-set --size 10 \
        --dir Assets/ModularTerrain/Modules --dir Assets/ModularTerrain/Ramps
    python -m unity_bridge terrain-config-get     # 读取 Unity 管理器中的全局配置
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


def _cmd_terrain_config_get(args) -> int:
    """读取模式：打印 Unity 管理器中存储的全局配置（唯一数据源，不读任何本地文件）。"""
    with UnityClient(args.host, args.port, args.timeout) as client:
        unity_cfg = client.get_terrain_config()

    if args.json:
        print(json.dumps(unity_cfg, ensure_ascii=False, indent=2))
        return 0

    print("=== Unity 管理器全局配置（唯一数据源） ===")
    print(f"  source           : {unity_cfg.get('source')}")
    print(f"  moduleSize       : {unity_cfg.get('moduleSize')}")
    print(f"  moduleDirectories: {unity_cfg.get('moduleDirectories')}")
    print(f"  moduleCount      : {unity_cfg.get('moduleCount')}")
    return 0


def _cmd_terrain_config_set(args) -> int:
    """写入模式：把命令行传入的配置写入 Unity 管理器预制体；Python 侧不保存任何本地副本。"""
    if args.size is None:
        print("[错误] 必须指定 --size（统一模块尺寸，正数，米）", file=sys.stderr)
        return 1
    module_size = args.size
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
        data = client.set_terrain_config(module_size, directories)

    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0

    print(f"prefab    : {data.get('prefabPath')}")
    print(f"created   : {data.get('created')}")
    print(f"moduleSize: {data.get('moduleSize')}")
    print(f"dirs      : {data.get('moduleCount')} 个 -> {data.get('moduleDirectories')}")
    return 0


def _cmd_module_list(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.module_list()
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"moduleSize: {data.get('moduleSize')}")
    print(f"count     : {data.get('count')}")
    for m in data.get("modules", []):
        desc = m.get("description", "")
        print(f"  id={m['id']:<3} desc={desc!r}")
        print(f"      h(z+={m['heightZPlus']}, x+={m['heightXPlus']}, "
              f"z-={m['heightZMinus']}, x-={m['heightXMinus']})")
    return 0


def _cmd_module_set(args) -> int:
    fields = {}
    for key in ("hZPlus", "hXPlus", "hZMinus", "hXMinus"):
        val = getattr(args, key, None)
        if val is not None:
            fields[key] = val
    if getattr(args, "desc", None) is not None:
        fields["description"] = args.desc
    if not fields:
        print("[错误] 未提供任何要设置的字段（如 --hZPlus 0.5 或 --desc '...'）", file=sys.stderr)
        return 1

    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.module_set(args.id, **fields)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"id      : {data.get('id')}")
    print(f"changed : {data.get('changed')}")
    print(f"hZ+ x+ z- x- : {data.get('heightZPlus')} {data.get('heightXPlus')} "
          f"{data.get('heightZMinus')} {data.get('heightXMinus')}")
    if "description" in (data.get("changed") or []):
        print(f"desc    : {data.get('description')}")
    return 0


def _cmd_layout_get(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.layout_get(
            xmin=args.xmin, zmin=args.zmin, xmax=args.xmax, zmax=args.zmax)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    r = data.get("range", {})
    print(f"csv   : {data.get('csvPath')}")
    print(f"range : x[{r.get('xmin')},{r.get('xmax')}] z[{r.get('zmin')},{r.get('zmax')}]")
    print(f"count : {data.get('count')}")
    for e in data.get("entries", []):
        print(f"  (x={e['x']}, z={e['z']}) moduleId={e['moduleId']} "
              f"rotation={e['rotation']} height={e['height']}")
    return 0


def _cmd_layout_set(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.layout_set(args.x, args.z, args.moduleId, args.rotation, args.height)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"csv     : {data.get('csvPath')}")
    print(f"created : {data.get('created')}")
    print(f"pos     : (x={data.get('x')}, z={data.get('z')})")
    print(f"moduleId: {data.get('moduleId')}")
    print(f"rotation: {data.get('rotation')}")
    print(f"height  : {data.get('height')}")
    print(f"total   : {data.get('total')} 条排布")
    return 0


def _cmd_layout_clear(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.layout_clear()
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"csv    : {data.get('csvPath')}")
    print(f"cleared: {data.get('cleared')}  total={data.get('total')}")
    return 0


def _cmd_layout_load(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.layout_load(args.x, args.z)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"pos      : (x={data.get('x')}, z={data.get('z')})")
    print(f"loaded   : {data.get('loaded')}")
    print(f"moduleId : {data.get('moduleId')}")
    print(f"moduleSize: {data.get('moduleSize')}  (统一模块尺寸，因所有模块同尺寸)")
    return 0


def _cmd_layout_unload(args) -> int:
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.layout_unload(args.x, args.z)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    print(f"pos      : (x={data.get('x')}, z={data.get('z')})")
    print(f"unloaded : {data.get('unloaded')}")
    return 0


def _cmd_layout_recommend(args) -> int:
    """推荐在 (x,z) 处可无缝拼接的模块（纯 Python 几何计算，不修改任何数据）。"""
    with UnityClient(args.host, args.port, args.timeout) as client:
        data = client.recommend_placement(args.x, args.z, args.height)
    if args.json:
        print(json.dumps(data, ensure_ascii=False, indent=2))
        return 0
    recs = data.get("recommendations", [])
    print(f"target       : (x={data.get('x')}, z={data.get('z')})  moduleSize={data.get('moduleSize')}")
    if data.get("desiredHeight") is not None:
        print(f"desiredHeight: {data.get('desiredHeight')}")
    print(f"count        : {data.get('count')}")
    if not recs:
        print("  （无可用模块：无法与四周现有模块无缝拼接，或四周模块库缺失）")
        return 0
    for r in recs:
        desc = r.get("description", "")
        print(f"  module id={r['id']}  desc={desc!r}")
        for rot in r.get("rotations", []):
            print(f"      rotation={rot['rotation']}  height={rot['height']}")
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

    p_cfg_get = sub.add_parser(
        "terrain-config-get", aliases=["tget"],
        help="读取 Unity 管理器中的全局配置（moduleSize + moduleDirectories）")
    p_cfg_get.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_cfg_get.set_defaults(func=_cmd_terrain_config_get)

    p_cfg_set = sub.add_parser(
        "terrain-config-set", aliases=["tset"],
        help="将全局模块配置（moduleSize + moduleDirectories）写入 Unity 管理器预制体")
    p_cfg_set.add_argument("--size", type=float, default=None,
                           help="统一模块尺寸（正数，米，本工作流所有模块同尺寸），必填")
    p_cfg_set.add_argument("--dir", action="append", default=[],
                           help="模块目录（Assets 相对路径），可多次指定")
    p_cfg_set.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_cfg_set.set_defaults(func=_cmd_terrain_config_set)

    p_mlist = sub.add_parser(
        "module-list", aliases=["mlist"],
        help="打印所有已加载地形模块的信息列表（id / 描述 / 四边高度）")
    p_mlist.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_mlist.set_defaults(func=_cmd_module_list)

    p_mset = sub.add_parser(
        "module-set", aliases=["mset"],
        help="按 id 设置模块指定字段（四边高度 / 描述；尺寸由管理器统一参数持有，不可在此设置）")
    p_mset.add_argument("--id", type=int, required=True, help="目标模块 id")
    p_mset.add_argument("--hZPlus", type=float, default=None, help="设置 +Z 边高度")
    p_mset.add_argument("--hXPlus", type=float, default=None, help="设置 +X 边高度")
    p_mset.add_argument("--hZMinus", type=float, default=None, help="设置 -Z 边高度")
    p_mset.add_argument("--hXMinus", type=float, default=None, help="设置 -X 边高度")
    p_mset.add_argument("--desc", default=None, help="设置模块描述（字符串）")
    p_mset.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_mset.set_defaults(func=_cmd_module_set)

    p_lget = sub.add_parser(
        "layout-get", aliases=["lget"],
        help="读取范围内地形排布（数据存于 Unity Resources CSV）")
    p_lget.add_argument("--xmin", type=int, default=None, help="网格范围下界 X（省略=全部）")
    p_lget.add_argument("--zmin", type=int, default=None, help="网格范围下界 Z（省略=全部）")
    p_lget.add_argument("--xmax", type=int, default=None, help="网格范围上界 X（省略=全部）")
    p_lget.add_argument("--zmax", type=int, default=None, help="网格范围上界 Z（省略=全部）")
    p_lget.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_lget.set_defaults(func=_cmd_layout_get)

    p_lset = sub.add_parser(
        "layout-set", aliases=["lset"],
        help="写入单个地形排布（数据存于 Unity Resources CSV）")
    p_lset.add_argument("--x", type=int, required=True, help="网格坐标 X")
    p_lset.add_argument("--z", type=int, required=True, help="网格坐标 Z")
    p_lset.add_argument("--moduleId", type=int, required=True, help="模块 id")
    p_lset.add_argument("--rotation", type=int, required=True, choices=[0, 90, 180, 270],
                        help="旋转角度：0/90/180/270（俯视视角顺时针）")
    p_lset.add_argument("--height", type=float, required=True, help="高度（float）")
    p_lset.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_lset.set_defaults(func=_cmd_layout_set)

    p_lclear = sub.add_parser(
        "layout-clear", aliases=["lclear"],
        help="清空地形排布，回到默认空 CSV")
    p_lclear.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_lclear.set_defaults(func=_cmd_layout_clear)

    p_lload = sub.add_parser(
        "layout-load", aliases=["lload"],
        help="按 (x,z) 加载/刷新单个地形块到 Unity 场景（需先 layout-set 写好该格）")
    p_lload.add_argument("--x", type=int, required=True, help="网格坐标 X")
    p_lload.add_argument("--z", type=int, required=True, help="网格坐标 Z")
    p_lload.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_lload.set_defaults(func=_cmd_layout_load)

    p_lunload = sub.add_parser(
        "layout-unload", aliases=["lunload"],
        help="卸载（销毁）(x,z) 处已实例化的地形块")
    p_lunload.add_argument("--x", type=int, required=True, help="网格坐标 X")
    p_lunload.add_argument("--z", type=int, required=True, help="网格坐标 Z")
    p_lunload.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_lunload.set_defaults(func=_cmd_layout_unload)

    p_lrec = sub.add_parser(
        "layout-recommend", aliases=["lrec"],
        help="推荐在 (x,z) 可无缝拼接的模块（含可用旋转与所需高度），不修改数据")
    p_lrec.add_argument("--x", type=int, required=True, help="目标网格坐标 X")
    p_lrec.add_argument("--z", type=int, required=True, help="目标网格坐标 Z")
    p_lrec.add_argument("--height", type=float, default=None,
                        help="期望放置高度（可选）；给定后只返回所需高度==该值的旋转")
    p_lrec.add_argument("--json", action="store_true", help="输出原始 JSON 而非文本")
    p_lrec.set_defaults(func=_cmd_layout_recommend)

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
