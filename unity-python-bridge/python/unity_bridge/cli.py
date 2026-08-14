"""Unity Bridge 命令行入口。

用法:
    python -m unity_bridge tree                 # 打印当前场景物体层级树
    python -m unity_bridge tree --components     # 同时显示组件类型
    python -m unity_bridge tree --json           # 输出原始 JSON
    python -m unity_bridge list                  # 列出 Unity 侧所有可用命令
    python -m unity_bridge mesh-bounds Assets/.../Rock.fbx   # 计算网格/模型/预制体包围盒
"""

from __future__ import annotations

import argparse
import json
import sys
from typing import List, Optional

from .client import DEFAULT_HOST, DEFAULT_PORT, UnityBridgeError, UnityClient

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
