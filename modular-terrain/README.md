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
| `id` | `int` | 模块唯一标识。**0 = 未分配**；经管理器 `LoadModules()` / `CollectModules()` 收集后，会自动为 `id==0` 的模块分配正数（从已分配最大值 +1 起依次递增，多个未分配依次 +1、+2、+3…），并持久化到对应 prefab |
| `description` | `string` | 模块描述（可选）。仅用于地形推荐时的可读性输出，**不参与几何计算** |
| `moduleSize` | `Vector2` | 模块长宽（米）。**x = 长（世界 X 方向），y = 宽（世界 Z 方向）** |
| `heightZPlus` | `float` | +Z 边（z+）接连处局部高度 |
| `heightXPlus` | `float` | +X 边（x+）接连处局部高度 |
| `heightZMinus` | `float` | -Z 边（z-）接连处局部高度 |
| `heightXMinus` | `float` | -X 边（x-）接连处局部高度 |

几何约定：模块以自身 Transform 原点为底面中心（y=0 为底面），四条侧边各自从 y=0 延伸到该边高度。
**相邻拼接约定**：模块四周墙顶的「世界高度」= 布局高度（placement height）+ 该边局部高度。
相邻两模块在共享边处，墙顶世界高度必须相等才算无缝拼接；旋转（0/90/180/270，俯视顺时针）
会改变「哪条局部边」落在哪个几何侧（见下文「相邻高度校验与推荐机制」）。
**Gizmos**：用 `#if UNITY_EDITOR` 隔离，在编辑器中绘制一个「无盖无底」的盒子 —— 只画四条侧边的竖直墙面，不画顶盖与底面。

### `ModularTerrainManager`（模块化地形管理器，MonoBehaviour）

场景级管理器，持有地形模块的配置与索引。

| 字段 | 类型 | 说明 |
|---|---|---|
| `sizePrecision` | `float` | 最小尺寸精度。之后处理的所有尺寸都必须是该数的整数倍 |
| `moduleDirectories` | `List<string>` | 模块（prefab / 资源）所在目录列表（Assets 相对路径） |
| `modules` | `List<ModularTerrainModule>` | **地形模块的存储容器**：由 `LoadModules()` 根据 `moduleDirectories` 扫描资源目录、加载所有含 `ModularTerrainModule` 的资源并写入；也可用 `CollectModules()` 收集场景实例 |

辅助方法：
- `LoadModules()`（**编辑器内**）：按 `moduleDirectories` 用 `AssetDatabase` 扫描并加载所有含 `ModularTerrainModule` 的 prefab/资源，写入 `modules`；无效目录跳过并告警，重复资源自动去重；收集后自动调用 `AssignIds()`
- `CollectModules()`：收集场景中已实例化的全部模块（含未激活物体）写入 `modules`；收集后自动调用 `AssignIds()`
- `AssignIds()`（**编辑器内**）：为 `modules` 中 `id==0` 的模块自动分配正数（已分配最大值 +1 递增），并持久化到对应 prefab
- `GetModuleById(int)`：按 id 在 `modules` 中查找模块组件（无则返回 null），供命令按 id 定位目标模块
- `GetModulesWithValidSize()`：返回 `modules` 中尺寸符合 `sizePrecision` 精度的模块（按精度条件筛选）
- `IsValidSize(float)`：校验尺寸是否为精度整数倍
- `SnapToPrecision(float)`：吸附到最近的精度整数倍

> `LoadModules()` 依赖 `AssetDatabase`，仅编辑器可用（已用 `#if UNITY_EDITOR` 隔离）。类是运行时组件，
> 但「从资源目录加载模块」这一步必须在编辑器内完成。

## 配置同步（terrain.sync_config）

全局配置（sizePrecision + moduleDirectories）的唯一真相源是 **Unity 管理器预制体**；Python 仅作为下发指令的通道，不在本地保存任何副本文件。

### 配置文件

- **`unity_project.ini`**（工作流根目录）：记录 Unity 工程的 Assets 绝对路径，供 Python 端定位工程、校验模块目录是否存在于磁盘（仅警告，不参与配置存储）：
  ```ini
  [unity]
  assets_path = D:/Projects/MyTerrainGame/Assets
  ```

> **全局模块配置（sizePrecision / moduleDirectories）只存储在 Unity 管理器预制体**
> （`Assets/ModularTerrainManager.prefab`）中。Python 端不另存任何本地文件，
> 读取用 `terrain.config_get`，写入用 `terrain.config_set`。

### 全局配置命令（唯一数据源 = Unity 管理器）

全局配置的读取与写入拆分为两条**独立命令**（不再复用单命令 + action 区分），均经命令总线反射分发，由 `TerrainConfigCommands.cs` 实现：

- **`terrain.config_get`（读取）**：由 Unity 通过 API 读取管理器预制体组件的当前配置并返回
  （**不解析 .prefab 文件**）。不接收任何参数。
  - 返回 `{ source:"unity", sizePrecision, moduleDirectories, moduleCount }`。
- **`terrain.config_set`（写入）**：接收 `sizePrecision` 与 `moduleDirectories`，写入管理器预制体。
  - **管理器预制体固定位于 `Assets/ModularTerrainManager.prefab`**：
    - 不存在则创建（挂 `ModularTerrainManager` 并存为 prefab）；
    - 若在其它目录被发现，则**移回该固定位置**（实现「不允许移动到别的目录」约定）；
    - 随后写入并持久化（`SaveAssets`）。
  - 返回 `{ prefabPath, created, sizePrecision, moduleDirectories, moduleCount }`。

Python 侧用法（`unity-python-bridge/python` 下）：

```bash
# 写入：把命令行参数直接写入 Unity 管理器预制体（Python 不保存本地副本）
python -m unity_bridge terrain-config-set --precision 0.5 \
    --dir Assets/ModularTerrain/Modules --dir Assets/ModularTerrain/Ramps

# 读取：打印 Unity 管理器中的全局配置（唯一数据源，不修改任何一侧）
python -m unity_bridge terrain-config-get
```

> 写入前 Python 会用 `unity_project.ini` 中的 `assets_path` 校验每个模块目录是否真实存在于磁盘，
> 不存在仅打印警告、不阻断写入。读取时不修改任何一侧。

---

## 模块操作命令（terrain.module_*）

以下命令均挂载于 unity-python-bridge 命令总线，按 `id` 在管理器中定位目标模块
（命令会先 `LoadModules` 刷新模块列表并确保 id 已分配），再对模块预制体进行读取/修改并持久化。

| 命令 | CLI | 作用 | 关键参数 |
|---|---|---|---|
| `terrain.module_list` | `module-list`(`mlist`) | 打印所有已加载模块的信息列表（id / description / 长宽 / 四边高度） | 无 |
| `terrain.module_size` | `module-size`(`msize`) | 计算指定 id 模块尺寸：长宽、四边高度、最大高度、是否符合精度 | `id`(int, 必填) |
| `terrain.module_snap` | `module-snap`(`msnap`) | 把指定 id 模块的尺寸（sizeX/sizeZ 与四边高度）吸附到精度整数倍 | `id`(int, 必填) |
| `terrain.module_set` | `module-set`(`mset`) | 按 id 设置模块指定字段，**仅设置传入的参数**，可多参数同时设置 | `id`(int, 必填)；`--sizeX`/`--length`、`--sizeZ`/`--width`、`--hZPlus`、`--hXPlus`、`--hZMinus`、`--hXMinus`(float, 可选)、`--desc`(string, 可选，设置 description) |

> `module_set` 的字段名对照：`sizeX`/`length` = `moduleSize.x`（长/世界 X），`sizeZ`/`width` = `moduleSize.y`（宽/世界 Z），
> `hZPlus/hXPlus/hZMinus/hXMinus` = 四边高度。例如「设置 x 轴上的范围为 0-6」即 `--sizeX 6`。

Python 侧用法（`unity-python-bridge/python` 下）：

```bash
# 列出所有已加载模块（含自动分配的 id）
python -m unity_bridge module-list

# 计算 id=1 模块的尺寸
python -m unity_bridge module-size --id 1

# 将 id=2 模块的尺寸吸附到精度整数倍
python -m unity_bridge module-snap --id 2

# 仅设置 id=1 的 X 长度为 6，以及 +Z 边高度为 3（只改这两项，其余不动）
python -m unity_bridge module-set --id 1 --sizeX 6 --hZPlus 3

# 同时设置多个：X 长度 8、Z 宽度 4、四边高度全部 2
python -m unity_bridge module-set --id 1 --sizeX 8 --sizeZ 4 --hZPlus 2 --hXPlus 2 --hZMinus 2 --hXMinus 2
```

> 这些命令改的是模块预制体资源本身（非场景实例），修改经 `EditorUtility.SetDirty` + `AssetDatabase.SaveAssets` 持久化。

---

## 排布（布局）命令（terrain.layout_*）

排布描述「哪些模块放在哪些网格坐标、各自朝向与高度」。与全局配置一样，**排布数据全部存储在
Unity 工程的 Resources CSV 中**，Python 侧只通过命令读写、绝不直接接触文件：

- 默认文件：`Assets/ModularTerrain/Resources/TerrainLayout.csv`（仓库内已提供，默认空，仅含表头 `x,z,moduleId,rotation,height`）。
- 每条记录字段：`x, z`（网格坐标，int）+ `moduleId, rotation, height`。

| 命令 | CLI | 作用 | 关键参数 |
|---|---|---|---|
| `terrain.layout_get` | `layout-get`(`lget`) | 读取 `[xmin,zmin,xmax,zmax]` 范围内的排布；四参数均可省略（省略返回全部） | `xmin`/`zmin`/`xmax`/`zmax`(int, 可选) |
| `terrain.layout_set` | `layout-set`(`lset`) | 在 `(x,z)` 写入单条排布（已存在则覆盖）；**写入前 Python 侧强制校验相邻高度无缝拼接**，存在高度突变则拒绝 | `x`/`z`(int)、`moduleId`(int)、`rotation`(0/90/180/270)、`height`(float) |
| `terrain.layout_clear` | `layout-clear`(`lclear`) | 清空排布，回到默认空 CSV（仅保留表头） | 无 |
| （Python 端，非 Unity 命令） | `layout-recommend`(`lrec`) | 推荐在 `(x,z)` 可无缝拼接的模块：输出每个可行模块的 `id`、描述、可用旋转与所需高度；不修改任何数据 | `--x`/`--z`(int, 必填)、`--height`(float, 可选，限定所需高度) |

**约束**：
- `rotation` 仅允许 `0 / 90 / 180 / 270`，表示**俯视视角下顺时针旋转**的角度（由后续实例化命令据此设置模块朝向）。
- `(x, z)` 为网格坐标键；`layout_set` 对同一坐标写入会覆盖旧记录。

---

## 相邻高度校验与推荐机制（Python 侧）

相邻拼接的核心约束是：**相邻两模块在共享边处的墙顶世界高度必须相等**。
墙顶世界高度 = 布局高度（placement height，排布里记录的 `height`）+ 该边局部高度（`heightZPlus` 等）。
哪条「局部边」落在哪个「几何侧」，由模块的 `rotation` 决定（俯视顺时针 0/90/180/270）。

Python 侧（`unity-python-bridge/python/unity_bridge/terrain_checks.py` 为纯几何逻辑，`client.py` 负责编排）
在每次**写入排布**或**获取推荐**时，都会**完整请求一遍全局配置、模块信息列表与当前排布**
（`terrain.config_get` + `terrain.module_list` + `terrain.layout_get`），再据此计算。

### 写入校验（layout-set 强制）

`layout-set` 在真正下发给 Unity 写入 CSV **之前**：
1. 拉取 config + 模块列表 + 当前全部排布；
2. 对目标 `(x,z)` 四周的 4 个相邻格子（若存在且能查到模块库）：
   - 计算本模块在该侧墙顶高 = `height + 局部边高度(本模块, rotation, 我方侧面)`；
   - 计算邻居在该侧墙顶高 = `邻居.height + 局部边高度(邻居模块, 邻居.rotation, 邻居侧面)`；
   - 两者差超过容差（1e-3 米）即记一条「高度不连续」错误。
3. 只要存在任一条错误，**拒绝写入**并以 `UnityBridgeError` 打印全部错误，**CSV 不会被改动**；
   全部通过才下发 `terrain.layout_set`。

侧面映射（俯视，几何侧索引 Z+=0 / X+=1 / Z-=2 / X-=3）：
- 邻居在 +X(dx=1)：我方 **X+** 边 对 邻居 **X-** 边；
- 邻居在 -X(dx=-1)：我方 **X-** 边 对 邻居 **X+** 边；
- 邻居在 +Z(dz=1)：我方 **Z+** 边 对 邻居 **Z-** 边；
- 邻居在 -Z(dz=-1)：我方 **Z-** 边 对 邻居 **Z+** 边。

旋转 k=rotation/90 步时，几何侧 `g` 上落着的局部边索引 = `(g - k) % 4`
（局部边顺序 `[heightZPlus, heightXPlus, heightZMinus, heightXMinus]`）。

### 推荐（layout-recommend，纯计算不写数据）

在目标 `(x,z)`（通常当前为空），枚举模块库中每个候选模块、4 个旋转，求一组
`(rotation, height)` 使该模块以该旋转与高度放置时能和四周已存在的相邻模块全部无缝拼接
（各邻居要求的高度一致）。输出每个可行模块的：
- `id`、`description`；
- `rotations`：`[{rotation, height}, ...]`，即「在该旋转、该高度下可无缝拼接」。

可选 `--height` 限定只返回「所需高度 == 该值」的拼接方式（例如只想找能平接在 height=0.5 上的方案）。

Python 侧用法（`unity-python-bridge/python` 下）：

```bash
# 在网格 (2,3) 放置模块 id=1：朝向 90°（俯视顺时针）、高度 0.5
# （若四周已存在相邻模块且墙顶高度与该放置不连续，会被拒绝并打印错误）
python -m unity_bridge layout-set --x 2 --z 3 --moduleId 1 --rotation 90 --height 0.5

# 推荐 (2,3) 处可无缝拼接的模块（列出可行旋转与所需高度）
python -m unity_bridge layout-recommend --x 2 --z 3

# 仅推荐「所需高度 == 0.5」的拼接方式
python -m unity_bridge layout-recommend --x 2 --z 3 --height 0.5

# 读取 x∈[0,5], z∈[0,5] 范围内的排布
python -m unity_bridge layout-get --xmin 0 --zmin 0 --xmax 5 --zmax 5

# 读取全部排布（省略范围参数）
python -m unity_bridge layout-get

# 清空排布（回到默认空 CSV）
python -m unity_bridge layout-clear
```

> 校验/推荐逻辑全部在 Python 侧（`terrain_checks.py`），与 Unity 命令总线解耦；
> 这样无论是连接真实 Unity 还是 `scripts/mock_unity_server.py` 离线联调，行为一致。
> 由于校验发生在写入前的 Python 层，若绕过 Python 直接调用 `terrain.layout_set` 不会触发校验，
> 规范工作流请始终通过 `layout-set` CLI / `client.layout_set()`。

---

## 与桥接工具的关系

`../unity-python-bridge/` 提供「Python 命令行操控 Unity Editor」的能力；本模块是地形工作流的
**数据层**。二者通过 `terrain.config_*` / `terrain.module_*` / `terrain.layout_*` 桥接命令接线——
这些命令定义在 `ModularTerrain` 命名空间下，由桥接层反射扫描所有程序集自动发现
（无需在桥接层写任何地形相关代码）。
