# unity-modular-terrain-ai

> 通过 AI + Python 工作流，在 Unity Editor 中**自动搭建模块化地形**。

本项目旨在把"在 Unity 里手动拼地形"变成一条可由 AI 驱动、脚本化的自动化流水线：
用 Python 侧编写地形 DSL / 调用大模型生成地块配置，再通过命令行桥接把指令下发到
Unity Editor，由反射机制实例化、摆放、拼接模块化地形 prefab，最终在场景里组装出完整地形。

`unity-python-bridge/` 是本仓库的**基础设施层**——它让 Python 能命令行操控 Unity Editor，
也是后续所有地形自动化功能落地的通信底座。

---

## 一、总体架构

```
┌─────────────────────────────┐         ┌───────────────────────────────────┐
│       Python 工作流侧         │  TCP    │           Unity Editor            │
│                             │  JSON行   │                                   │
│  ┌───────────────────────┐  │         │  ┌─────────────────────────────┐  │
│  │ terrain DSL / AI 生成  │  │         │  │ unity-python-bridge         │  │
│  │ 地块配置、拼接规则      │──┼────────▶│  │  BridgeServer + 反射分发     │  │
│  └──────────┬────────────┘  │         │  │  scene.tree / 地形命令 ...    │  │
│             │               │         │  └──────────────┬──────────────┘  │
│  ┌──────────▼────────────┐  │         │                 │ 实例化 / 摆放     │
│  │ unity_bridge CLI / API │  │         │  ┌──────────────▼──────────────┐  │
│  │  操控 Unity 的命令入口  │  │         │  │ 模块化地形 Prefab 库         │  │
│  └───────────────────────┘  │         │  │ TileA / TileB / Connector... │  │
│                             │         │  └─────────────────────────────┘  │
└─────────────────────────────┘         └───────────────────────────────────┘
```

> **底座已就绪**：`unity-python-bridge/` 已实现 TCP + 单行 JSON 协议、反射自动注册命令、
> 主线程安全执行，当前共 **14 条命令**（桥接层原生 5 条 + modular-terrain 插件 9 条），详见下方「三、可用命令一览」。
> 上层地形工作流已起步：`modular-terrain/` 提供了地形模块组件、管理器、排布缓存与各 `terrain.*` 命令（数据层 + 接线）。
> "地形 DSL / AI 生成 / 实例化命令"为后续开发项，参见下方路线图。

---

## 二、目录结构

```
unity-modular-terrain-ai/
├── README.md                       # ← 本文件：项目总览与路线
├── unity_project.ini               # 工作流根配置：记录 Unity 工程 Assets 绝对路径（仅定位工程，不存配置）
│
├── unity-python-bridge/            # ← 命令行操控 Unity Editor 的基础设施（已就绪）
│   ├── README.md                   #   桥接工具详细文档（架构/协议/扩展方法）
│   ├── Unity/                      #   C# 侧：复制到 Unity 项目 Assets/ 下
│   │   └── Assets/UnityPythonBridge/
│   │       ├── Editor/BridgeWindow.cs
│   │       └── Runtime/…           #   BridgeServer / Dispatcher / 命令 / 特性
│   └── python/                     #   Python 侧：纯标准库，零依赖
│       ├── unity_bridge/           #   client.py / cli.py / __main__.py
│       │   └── terrain_checks.py   #   相邻边高度校验 + 模块推荐（纯几何，供 layout-set/layout-recommend 复用）
│       ├── scripts/mock_unity_server.py
│       └── requirements.txt
│
└── modular-terrain/                # ← 模块化地形模块（数据层 + 地形专属桥接命令）
    ├── README.md                   #   模块文档（组件 / 管理器 / 配置同步 / 排布）
    └── Unity/Assets/ModularTerrain/  # C# 侧：复制到 Unity 项目 Assets/ 下
        ├── ModularTerrainModule.cs     # 模块地形组件（含 id / description / 四边高度 + Gizmos 无盖无底盒子）
        ├── ModularTerrainManager.cs    # 管理器 Mono（精度 / 目录 / 模块列表 / CSV 全量缓存 / 按坐标加载·卸载）
        ├── TerrainLayoutIO.cs          # 共享 CSV 读写 + 信息结构体 TerrainLayoutCell（不依赖 UNITY_EDITOR）
        ├── TerrainConfigCommands.cs    # 桥接命令 terrain.config_get / terrain.config_set（反射自动注册）
        ├── TerrainModuleCommands.cs    # 桥接命令 terrain.module_*
        ├── TerrainLayoutCommands.cs    # 桥接命令 terrain.layout_*（排布，数据存于 Resources CSV，复用 TerrainLayoutIO）
        └── Resources/
            └── TerrainLayout.csv       # 地形排布数据（默认空，仅表头；由 Unity 命令读写）
```

> `modular-terrain/` 是地形工作流的**数据层**：组件与管理器定义地形模块规范，
> `terrain.config_*` / `terrain.module_*` / `terrain.layout_*` 命令由桥接层反射扫描所有程序集自动发现，二者已接线。
> 管理器在 `Awake` 时全量读取 CSV 缓存为 `Dictionary<Vector2Int, TerrainLayoutCell>`，并对外暴露
> `LoadTerrainModule(int,int)` / `UnloadTerrainModule(int,int)` 按网格坐标实例化 / 销毁模块 prefab。
> Python 侧的 `layout-recommend` 还会在写入/推荐前强制校验相邻模块墙顶高度无缝拼接（详见「三」附加能力）。
> 后续会逐步新增 `terrain/` （地形 DSL 与 AI 生成脚本）等子目录。

---

## 三、可用命令一览（共 14 条）

所有命令都流经 `unity-python-bridge` 命令总线（TCP + 单行 JSON，反射分发，主线程执行）。
按提供方分为两组；详细参数与返回结构见 **[unity-python-bridge/README.md](unity-python-bridge/README.md)**。

**A. 桥接层原生命令（5 条）**

| 命令 | CLI | 作用 |
|---|---|---|
| `scene.tree` | `tree` | 树状打印当前场景物体层级（`--components` 显示组件） |
| `mesh.bounds` | `mesh-bounds` / `bounds` | 计算 Assets 中 mesh/模型/prefab 的轴对齐包围盒 |
| `prefab.screenshot` | `screenshot` / `shot` | 隔离复制 prefab 并渲染存 PNG（支持正交/透视/补光） |
| `bridge.ping` | （无，用 `client.ping()`） | 连通性测试，返回 pong + 时间 |
| `bridge.list_commands` | `list` / `ls` | 列出所有已注册命令 |

**B. modular-terrain 插件命令（9 条）**

| 命令 | CLI | 作用 |
|---|---|---|
| `terrain.config_get` | `terrain-config-get` / `tget` | 读取 Unity 管理器中的全局配置（唯一数据源 = Unity 管理器预制体） |
| `terrain.config_set` | `terrain-config-set` / `tset` | 将全局配置写入 Unity 管理器预制体 |
| `terrain.module_list` | `module-list` / `mlist` | 打印所有已加载模块信息列表 |
| `terrain.module_size` | `module-size` / `msize` | 计算指定 id 模块尺寸 |
| `terrain.module_snap` | `module-snap` / `msnap` | 把指定 id 模块尺寸吸附到精度整数倍 |
| `terrain.module_set` | `module-set` / `mset` | 按 id 设置模块指定字段（可多参数） |
| `terrain.layout_get` | `layout-get` / `lget` | 读取范围内地形排布（数据存于 Unity Resources CSV） |
| `terrain.layout_set` | `layout-set` / `lset` | 在 (x,z) 写入单条排布（moduleId/rotation/height） |
| `terrain.layout_clear` | `layout-clear` / `lclear` | 清空排布，回到默认空 CSV |

> 插件命令的 C# 实现位于 `modular-terrain/Unity/Assets/ModularTerrain/`，由桥接层反射自动发现，无需在桥接层写任何地形相关代码。

**Python 侧附加能力**（非 Unity 命令，纯几何计算，逻辑在 `unity-python-bridge/python/unity_bridge/terrain_checks.py`）：
- `layout-set`(`lset`) 写入前**强制校验相邻模块墙顶高度无缝拼接**，存在高度突变则拒绝写入。
- `layout-recommend`(`lrec`) 推荐在指定网格坐标可无缝拼接的模块（含可行旋转与所需高度），不修改数据。
- 两者每次都会先完整请求一遍全局配置、模块信息列表与当前排布，再在 Python 侧计算。

---

## 四、快速开始（底座验证）

1. **Unity 侧**：把 `unity-python-bridge/Unity/Assets/UnityPythonBridge` 拷入你的项目
   `Assets/`，安装 `com.unity.nuget.newtonsoft-json`，菜单 **Tools → Unity Python Bridge →
   Start Server**。
2. **Python 侧**：

   ```bash
   cd unity-python-bridge/python
   python -m unity_bridge tree --components   # 树状打印当前场景物体（含组件）
   python -m unity_bridge list                 # 查看所有可用命令
   ```

详细用法、协议、如何扩展新命令见 **[unity-python-bridge/README.md](unity-python-bridge/README.md)**。

---

## 五、AI 模块化地形工作流 · 路线规划

当前底座与地形数据层已就绪（阶段 3 进行中）。后续分阶段目标：

| 阶段 | 目标 | 依赖 |
|---|---|---|
| **0. 通信底座** ✅ | Python↔Unity 命令行桥接，反射注册命令 | `unity-python-bridge`（已完成） |
| **1. 场景读能力** ✅ | `scene.tree`（树状物体层级）；查询物体/包围盒/截图等命令 | 底座（已完成） |
| **2. 实例化命令** | 新增 `terrain.spawn` 等命令：按参数在场景实例化指定 prefab 并设置 transform | 底座 + 资源库 |
| **3. 模块化地形库** 🔧 | 地形 tile 规范（`ModularTerrainModule` 组件 + `ModularTerrainManager` 管理器 + `terrain.config_get/config_set` 全局配置 + `terrain.module_*` 模块管理 + `terrain.layout_*` 排布 CSV + 管理器按坐标 `Load/UnloadTerrainModule` 运行时实例化）；待沉淀可拼接 prefab 资源库 | `modular-terrain`（进行中） |
| **4. 地形 DSL** | Python 侧用声明式配置描述"哪里放哪块tile、如何拼接" | 阶段 2-3 |
| **5. AI 生成** | 接入大模型：从自然语言/草图生成地块配置，写入 DSL 并下发 Unity 实例化 | 阶段 4 |

> 每个阶段都会以"在 `unity-python-bridge` 里加一个 `[BridgeCommand]`"的方式落地，
> 复用现有反射分发机制，无需改动通信层。

---

## 六、设计原则

- **通信与业务解耦**：`unity-python-bridge` 只负责"安全地把命令送到 Unity 主线程并执行"，
  地形逻辑全部写在命令层和 Python 工作流侧。
- **反射驱动扩展**：新增任何操控 Unity 的能力 = 加一个 `[BridgeCommand]` 静态方法，零样板。
- **本地优先、安全**：桥接只监听 `127.0.0.1`，AI 工作流运行在本机，不暴露编辑器到网络。
