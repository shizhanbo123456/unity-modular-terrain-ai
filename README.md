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
> 主线程安全执行，并包含 `scene.tree` / `mesh.bounds` / `prefab.screenshot` 等命令。
> 上层地形工作流已起步：`modular-terrain/` 提供了地形模块组件、管理器与 `terrain.sync_config`
> 配置同步命令（数据层 + 接线）。"地形 DSL / AI 生成 / 实例化命令"为后续开发项，参见下方路线图。

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
│       ├── scripts/mock_unity_server.py
│       └── requirements.txt
│
└── modular-terrain/                # ← 模块化地形模块（数据层 + 地形专属桥接命令）
    ├── README.md                   #   模块文档（组件 / 管理器 / 配置同步）
    └── Unity/Assets/ModularTerrain/  # C# 侧：复制到 Unity 项目 Assets/ 下
        ├── ModularTerrainModule.cs     # 模块地形组件（含 Gizmos 无盖无底盒子）
        ├── ModularTerrainManager.cs    # 管理器 Mono（精度 / 目录 / 模块列表）
        └── TerrainSyncConfigCommand.cs # 桥接命令 terrain.sync_config（反射自动注册）
```

> `modular-terrain/` 是地形工作流的**数据层**：组件与管理器定义地形模块规范，
> `terrain.sync_config` 命令由桥接层反射扫描所有程序集自动发现，二者已接线。
> 后续会逐步新增 `terrain/` （地形 DSL 与 AI 生成脚本）等子目录。

---

## 三、快速开始（底座验证）

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

## 四、AI 模块化地形工作流 · 路线规划

当前仅底座就绪。后续分阶段目标（待实现）：

| 阶段 | 目标 | 依赖 |
|---|---|---|
| **0. 通信底座** ✅ | Python↔Unity 命令行桥接，反射注册命令 | `unity-python-bridge`（已完成） |
| **1. 场景读能力** ✅ | `scene.tree`（树状物体层级）；查询物体/包围盒/截图等命令 | 底座（已完成） |
| **2. 实例化命令** | 新增 `terrain.spawn` 等命令：按参数在场景实例化指定 prefab 并设置 transform | 底座 + 资源库 |
| **3. 模块化地形库** 🔧 | 地形 tile 规范（`ModularTerrainModule` 组件 + `ModularTerrainManager` 管理器 + `terrain.sync_config` 配置同步已落地）；待沉淀可拼接 prefab 资源库 | `modular-terrain`（进行中） |
| **4. 地形 DSL** | Python 侧用声明式配置描述"哪里放哪块tile、如何拼接" | 阶段 2-3 |
| **5. AI 生成** | 接入大模型：从自然语言/草图生成地块配置，写入 DSL 并下发 Unity 实例化 | 阶段 4 |

> 每个阶段都会以"在 `unity-python-bridge` 里加一个 `[BridgeCommand]`"的方式落地，
> 复用现有反射分发机制，无需改动通信层。

---

## 五、设计原则

- **通信与业务解耦**：`unity-python-bridge` 只负责"安全地把命令送到 Unity 主线程并执行"，
  地形逻辑全部写在命令层和 Python 工作流侧。
- **反射驱动扩展**：新增任何操控 Unity 的能力 = 加一个 `[BridgeCommand]` 静态方法，零样板。
- **本地优先、安全**：桥接只监听 `127.0.0.1`，AI 工作流运行在本机，不暴露编辑器到网络。
