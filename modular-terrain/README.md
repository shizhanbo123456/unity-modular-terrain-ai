# 模块化地形（Modular Terrain）

地形工作流的**第二个子工具**：在 Unity 中描述与组织「模块化地形」的数据结构。
本模块只负责**数据 + 编辑器可视化**，后续的 AI 生成、实例化、拼接逻辑会基于这里的组件扩展。

## 目录结构

```
modular-terrain/
├── Unity/                      # 直接导入 Unity 工程的文件夹（内容映射 Assets/）
│   └── Assets/
│       └── ModularTerrain/     # 本模块 C# 代码
│           ├── ModularTerrainModule.cs    # 模块地形组件
│           └── ModularTerrainManager.cs   # 模块化地形管理器（Mono）
└── README.md
```

导入方式：把 `Unity/Assets/` 下的 `ModularTerrain/` 拷进你的 Unity 工程 `Assets/` 即可。

## 组件说明

### `ModularTerrainModule`（模块地形组件）

挂在 GameObject 上，描述一个矩形地形模块（tile）。

| 字段 | 类型 | 说明 |
|---|---|---|
| `moduleSize` | `Vector2` | 模块长宽（米）。**x = 长（世界 X 方向），y = 宽（世界 Z 方向）** |
| `heightZPlus` | `float` | +Z 边（z+）接连处局部高度 |
| `heightXPlus` | `float` | +X 边（x+）接连处局部高度 |
| `heightZMinus` | `float` | -Z 边（z-）接连处局部高度 |
| `heightXMinus` | `float` | -X 边（x-）接连处局部高度 |

几何约定：模块以自身 Transform 原点为底面中心（y=0 为底面），四条侧边各自从 y=0 延伸到该边高度。
**Gizmos**：用 `#if UNITY_EDITOR` 隔离，在编辑器中绘制一个「无盖无底」的盒子 —— 只画四条侧边的竖直墙面，不画顶盖与底面。

### `ModularTerrainManager`（模块化地形管理器，MonoBehaviour）

场景级管理器，持有地形模块的配置与索引。

| 字段 | 类型 | 说明 |
|---|---|---|
| `sizePrecision` | `float` | 最小尺寸精度。之后处理的所有尺寸都必须是该数的整数倍 |
| `moduleDirectories` | `List<string>` | 模块（prefab / 资源）所在目录列表（Assets 相对路径） |
| `modules` | `List<ModularTerrainModule>` | 场景中所有地形模块组件（可由 `CollectModules` 自动收集） |

辅助方法：`CollectModules()`（收集场景全部模块）、`IsValidSize(float)`（校验是否为精度整数倍）、
`SnapToPrecision(float)`（吸附到最近的精度整数倍）。本类为运行时组件，不依赖 UnityEditor。

## 与桥接工具的关系

`../unity-python-bridge/` 提供「Python 命令行操控 Unity Editor」的能力；本模块是地形工作流的
**数据层**。后续可通过桥接工具下发命令（例如读取/写入模块、批量实例化、调用管理器收集模块等），
但目前二者尚未接线——本模块先独立提供可导入、可可视化的组件基础。
