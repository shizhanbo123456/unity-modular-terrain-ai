"""模块化地形：相邻边高度校验与模块推荐（纯几何逻辑）。

本模块只依赖「已从 Unity 拉取的配置 / 模块信息列表 / 排布列表」字典，
不发起任何网络请求，便于在无 Unity 环境（mock）下单元测试与复用。

几何模型（与 ModularTerrainModule.cs 约定一致）：
  - 模块以自身 Transform 原点为底面中心（y=0 为底面），四周墙顶世界高度
    = 布局高度(placement height) + 该边局部高度。
  - 四个几何侧面索引：Z+ = 0, X+ = 1, Z- = 2, X- = 3。
  - 局部边高度顺序 [heightZPlus, heightXPlus, heightZMinus, heightXMinus]。
  - 旋转 rotation（0/90/180/270，俯视视角顺时针）为 k = rotation//90 步；
    几何侧面 g 上落着的局部边索引 = (g - k) % 4。
  - 相邻两模块在共享边处，墙顶世界高度必须相等才算无缝拼接。
"""

from __future__ import annotations

from typing import Any, Dict, List, Optional, Tuple

# 几何侧面索引
Z_PLUS, X_PLUS, Z_MINUS, X_MINUS = 0, 1, 2, 3
SIDE_NAMES = {Z_PLUS: "Z+", X_PLUS: "X+", Z_MINUS: "Z-", X_MINUS: "X-"}

# 四个邻居方向 -> (dx, dz, 我方侧面索引, 对方侧面索引)
#   +X 邻居：我方 +X 边(x=1) 对 对方 -X 边(x=3)
#   -X 邻居：我方 -X 边(x=3) 对 对方 +X 边(x=1)
#   +Z 邻居：我方 +Z 边(z=0) 对 对方 -Z 边(z=2)
#   -Z 邻居：我方 -Z 边(z=2) 对 对方 +Z 边(z=0)
_NEIGHBORS = [
    (1, 0, X_PLUS, X_MINUS),
    (-1, 0, X_MINUS, X_PLUS),
    (0, 1, Z_PLUS, Z_MINUS),
    (0, -1, Z_MINUS, Z_PLUS),
]

# 高度比较容差（米），用于判断墙顶是否「相等」
_HEIGHT_EPS = 1e-3


def _heights(module: Dict[str, Any]) -> List[float]:
    """返回模块四边局部高度，顺序 [Z+, X+, Z-, X-]。"""
    return [
        float(module["heightZPlus"]),
        float(module["heightXPlus"]),
        float(module["heightZMinus"]),
        float(module["heightXMinus"]),
    ]


def _opposite_side(side: int) -> int:
    """返回与给定侧面相对的侧面（Z+<->Z-，X+<->X-）。"""
    return (side + 2) % 4


def local_edge_height(module: Dict[str, Any], rotation: int, side: int) -> float:
    """返回模块在指定旋转下、几何侧面 side 所落着的局部边高度。

    side 为几何侧面索引（Z+/X+/Z-/X-）；rotation 为 0/90/180/270（俯视顺时针）。
    """
    k = ((rotation // 90) % 4 + 4) % 4
    he = _heights(module)
    return he[(side - k) % 4]


def edge_top(module: Dict[str, Any], rotation: int, base_height: float, side: int) -> float:
    """返回模块在指定旋转、布局高度 base_height 下，几何侧面 side 的墙顶世界高度。"""
    return float(base_height) + local_edge_height(module, rotation, side)


def module_by_id(modules: List[Dict[str, Any]], module_id: int) -> Optional[Dict[str, Any]]:
    """在模块信息列表中按 id 查找（无则返回 None）。"""
    for m in modules:
        if int(m["id"]) == int(module_id):
            return m
    return None


def _layout_map(layout_entries: List[Dict[str, Any]]) -> Dict[Tuple[int, int], Dict[str, Any]]:
    return {(int(e["x"]), int(e["z"])): e for e in layout_entries}


def check_placement(
    config: Dict[str, Any],
    modules: List[Dict[str, Any]],
    layout_entries: List[Dict[str, Any]],
    x: int,
    z: int,
    module_id: int,
    rotation: int,
    height: float,
) -> Tuple[bool, List[str]]:
    """校验在 (x,z) 放置 (module_id, rotation, height) 是否与四周相邻模块高度无缝拼接。

    返回 (ok, errors)：ok=True 表示通过；errors 为不连续处的可读错误列表。
    被覆盖的自身单元格会被排除在邻居之外（因为将被新模块替换）。

    注意：config 参数保留用于接口完整性（后续可按 moduleSize 校验模块尺寸与网格的匹配），
    当前相邻高度校验不依赖它。
    """
    errors: List[str] = []
    mod = module_by_id(modules, module_id)
    if mod is None:
        return False, [f"未找到 id={module_id} 的模块，无法校验相邻高度"]

    layout = _layout_map(layout_entries)
    layout.pop((int(x), int(z)), None)  # 自身单元格将被覆盖，排除

    for dx, dz, my_side, nb_side in _NEIGHBORS:
        nb = layout.get((int(x) + dx, int(z) + dz))
        if nb is None:
            continue
        nb_mod = module_by_id(modules, nb["moduleId"])
        if nb_mod is None:
            # 邻居引用了模块库里不存在的模块，无法校验，跳过（不阻断）
            continue

        my_top = edge_top(mod, int(rotation), float(height), my_side)
        nb_top = edge_top(nb_mod, int(nb["rotation"]), float(nb["height"]), nb_side)
        if abs(my_top - nb_top) > _HEIGHT_EPS:
            errors.append(
                f"与相邻模块(id={nb['moduleId']}, rotation={nb['rotation']}, "
                f"height={float(nb['height']):.4g}) 在 {SIDE_NAMES[my_side]} 边高度不连续: "
                f"本模块墙顶高={my_top:.4g} vs 邻居墙顶高={nb_top:.4g}"
            )

    return (len(errors) == 0, errors)


def recommend(
    config: Dict[str, Any],
    modules: List[Dict[str, Any]],
    layout_entries: List[Dict[str, Any]],
    x: int,
    z: int,
    desired_height: Optional[float] = None,
) -> List[Dict[str, Any]]:
    """在 (x,z) 推荐可无缝拼接的模块。

    对模块库中每个候选模块，枚举 4 个旋转，求一组 (rotation, height) 使该模块以该旋转与高度
    放置时能和四周已存在的相邻模块无缝拼接（各邻居要求的高度一致）。

    返回列表，每项为:
        {"id": int, "description": str,
         "rotations": [{"rotation": int, "height": float}, ...]}
    仅包含至少有一个可行旋转的模块。

    desired_height 若给定，则只返回「所需高度 == desired_height」的可行旋转。
    """
    layout = _layout_map(layout_entries)
    layout.pop((int(x), int(z)), None)

    # 收集存在的邻居及其「朝向我方的侧面」
    neighbors = []
    for dx, dz, _my_side, nb_side in _NEIGHBORS:
        nb = layout.get((int(x) + dx, int(z) + dz))
        if nb is not None and module_by_id(modules, nb["moduleId"]) is not None:
            neighbors.append((nb, nb_side))

    results: List[Dict[str, Any]] = []
    for mod in modules:
        feasible: List[Dict[str, Any]] = []
        for rotation in (0, 90, 180, 270):
            if not neighbors:
                # 无相邻约束：任意旋转均可，高度取 0（实际可任意，0 作为默认基准）
                feasible.append({"rotation": rotation, "height": 0.0})
                continue

            required = []
            for nb, nb_side in neighbors:
                nb_mod = module_by_id(modules, nb["moduleId"])
                my_side = _opposite_side(nb_side)
                req = (
                    float(nb["height"])
                    + local_edge_height(nb_mod, int(nb["rotation"]), nb_side)
                    - local_edge_height(mod, rotation, my_side)
                )
                required.append(req)

            if all(abs(h - required[0]) <= _HEIGHT_EPS for h in required):
                h = required[0]
                if desired_height is None or abs(h - float(desired_height)) <= _HEIGHT_EPS:
                    feasible.append({"rotation": rotation, "height": h})

        if feasible:
            results.append(
                {
                    "id": int(mod["id"]),
                    "description": str(mod.get("description", "")),
                    "rotations": feasible,
                }
            )

    return results
