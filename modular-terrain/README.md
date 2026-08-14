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
│           ├── ModularTerrainManager.cs   # 模块化地形管理器（Mono）
│           └── TerrainSyncConfigCommand.cs # 桥接命令 terrain.sync_config（反射注册）
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
| `modules` | `List<ModularTerrainModule>` | **地形模块的存储容器**：由 `LoadModules()` 根据 `moduleDirectories` 扫描资源目录、加载所有含 `ModularTerrainModule` 的资源并写入；也可用 `CollectModules()` 收集场景实例 |

辅助方法：
- `LoadModules()`（**编辑器内**）：按 `moduleDirectories` 用 `AssetDatabase` 扫描并加载所有含 `ModularTerrainModule` 的 prefab/资源，写入 `modules`；无效目录跳过并告警，重复资源自动去重
- `CollectModules()`：收集场景中已实例化的全部模块（含未激活物体）写入 `modules`
- `GetModulesWithValidSize()`：返回 `modules` 中尺寸符合 `sizePrecision` 精度的模块（按精度条件筛选）
- `IsValidSize(float)`：校验尺寸是否为精度整数倍
- `SnapToPrecision(float)`：吸附到最近的精度整数倍

> `LoadModules()` 依赖 `AssetDatabase`，仅编辑器可用（已用 `#if UNITY_EDITOR` 隔离）。类是运行时组件，
> 但「从资源目录加载模块」这一步必须在编辑器内完成。

## 配置同步（terrain.sync_config）

管理器预制体的配置由 Python 端维护并一键同步进 Unity。

### 仓库根目录的两个配置文件

- **`unity_project.ini`**（工作流根目录）：记录 Unity 工程的 Assets 绝对路径，供 Python 端定位工程、校验模块目录是否存在：
  ```ini
  [unity]
  assets_path = D:/Projects/MyTerrainGame/Assets
  ```
- **`terrain_config.json`**（Python 端创建/维护）：保存管理器的两项配置：
  ```json
  {
    "sizePrecision": 0.5,
    "moduleDirectories": ["Assets/ModularTerrain/Modules"]
  }
  ```

### 同步命令

`terrain.sync_config`（桥接命令，由 `TerrainSyncConfigCommand.cs` 实现，反射自动注册）接收
`sizePrecision` 与 `moduleDirectories`，写入管理器预制体：

- **管理器预制体固定位于 `Assets/ModularTerrainManager.prefab`**：
  - 不存在则创建（挂 `ModularTerrainManager` 并存为 prefab）；
  - 若在其它目录被发现，则**移回该固定位置**（实现「不允许移动到别的目录」约定）；
  - 随后写入 `sizePrecision` 与 `moduleDirectories` 并持久化（`SaveAssets`）。
- 返回 `{ prefabPath, created, sizePrecision, moduleDirectories, moduleCount }`。

Python 侧用法（`unity-python-bridge/python` 下）：

```bash
# 方式一：直接给出参数，Python 端会写回 terrain_config.json 后再同步
python -m unity_bridge terrain-sync --precision 0.5 \
    --dir Assets/ModularTerrain/Modules --dir Assets/ModularTerrain/Ramps

# 方式二：读取已有 JSON 配置同步（不覆盖文件）
python -m unity_bridge terrain-sync --config ../../terrain_config.json
```

> 同步前 Python 会用 `unity_project.ini` 中的 `assets_path` 校验每个模块目录是否真实存在于磁盘，
> 不存在仅打印警告、不阻断同步。

## 与桥接工具的关系

`../unity-python-bridge/` 提供「Python 命令行操控 Unity Editor」的能力；本模块是地形工作流的
**数据层**。二者已通过 `terrain.sync_config` 桥接命令接线——该命令定义在 `ModularTerrain` 命名空间下，
由桥接层反射扫描所有程序集自动发现（无需在桥接层写任何地形相关代码）。
