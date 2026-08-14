# UnityPythonBridge — 通过 Python 命令行操控 Unity Editor

在 Unity Editor 运行时，通过 Python 命令行工具对编辑器进行操控。架构上采用 **TCP + 单行 JSON** 协议，C# 侧通过**反射**自动注册命令，新增命令零样板代码。

---

## 一、架构总览

```
┌─────────────────────────────┐          ┌───────────────────────────────────┐
│         Python 侧            │          │            Unity Editor            │
│                             │          │                                   │
│  ┌───────────────────────┐  │  TCP     │  ┌─────────────────────────────┐  │
│  │ cli.py (命令行入口)    │  │ JSON行    │  │ BridgeServer (TCP 监听)      │  │
│  │   tree / list / ...   │──┼─────────▶│  │   · 仅监听 127.0.0.1         │  │
│  └──────────┬────────────┘  │          │   · 后台线程收/发              │  │
│             │               │          │  └──────────┬──────────────────┘  │
│  ┌──────────▼────────────┐  │          │             │ 投递 (线程安全队列)   │
│  │ client.py              │  │          │  ┌──────────▼──────────────────┐  │
│  │  · socket 收发          │  │          │  │ MainThreadRunner            │  │
│  │  · JSON 编解码          │  │          │  │  · 主线程队列                │  │
│  │  · call(cmd, **args)   │  │          │  └──────────┬──────────────────┘  │
│  └───────────────────────┘  │          │             │ 主线程 Flush          │
│                             │          │  ┌──────────▼──────────────────┐  │
│                             │          │  │ BridgeDispatcher (反射分发)  │  │
│                             │          │  │  · 扫描 [BridgeCommand] 特性  │  │
│                             │          │  └──────────┬──────────────────┘  │
│                             │          │             │                     │
│                             │          │  ┌──────────▼──────────────────┐  │
│                             │          │  │ Commands/                   │  │
│                             │          │  │  SceneTreeCommand           │  │
│                             │          │  │  SystemCommands             │  │
│                             │          │  └─────────────────────────────┘  │
└─────────────────────────────┘          └───────────────────────────────────┘
```

**核心设计决策：**

| 决策点 | 方案 | 理由 |
|---|---|---|
| 通信协议 | TCP + 单行 JSON（UTF-8） | 简单可靠、调试直观（可用 netcat 直接发命令） |
| 监听地址 | `127.0.0.1` 仅本机 | 避免局域网暴露风险 |
| 线程模型 | 后台线程收发 + **主线程队列执行** | Unity API 只能主线程访问，`EditorApplication.update` 驱动队列，Edit Mode 与 Play Mode 均安全可用 |
| 命令注册 | **反射扫描 `[BridgeCommand]` 特性** | 新增命令只需写一个静态方法类，零改动现有代码 |
| 数据格式 | 请求 `{id, cmd, args}` / 响应 `{id, ok, data\|error}` | 支持并发请求（按 id 匹配），错误与数据分离 |
| Python 依赖 | 纯标准库 | 零安装成本，Python 3.8+ |

**为什么需要反射：** 分发器在静态构造时扫描所有程序集，凡是带 `[BridgeCommand]` 特性的静态方法都会自动注册。后续你要实现"通过反射调用任意 Unity 对象方法/属性"等高级命令时，可以复用同一套分发机制，Python 侧只需传 `cmd + args` 即可。

---

## 命令总览（所有流经命令总线的命令）

> 本桥接层是**命令总线**：所有命令（无论来自本工具的原生命令，还是来自 `modular-terrain` 等挂载在总线上的「插件」命令）都经 `BridgeServer` 接收、`BridgeDispatcher` 反射分发、主线程执行。下方为当前**全部命令**的统一编目，新增命令也在此登记。

| 命令 (bus name) | 类别 | 提供方 | 功能 | Python CLI | 关键参数 |
|---|---|---|---|---|---|
| `scene.tree` | 场景读取 | 桥接层（原生） | 树状返回当前激活场景的物体层级 | `tree` | `components`(bool, 可选，显组件类型) |
| `mesh.bounds` | 资源查询 | 桥接层（原生） | 计算 Assets 中 mesh / 模型 / prefab 的轴对齐包围盒（AABB，多网格合并） | `mesh-bounds`（别名 `bounds`） | `path`(string, Assets 相对路径) |
| `prefab.screenshot` | 资源查询 | 桥接层（原生） | 隔离复制 prefab 到 `(9999,9999,9999)` + 相机环绕 `LookAt` 渲染存 PNG（支持正交/透视、`fov`、`bg`、补光） | `screenshot`（别名 `shot`） | `path`、`output`(.png)、`offset`("x,y,z")、`orthographic`、`fov`、`width`、`height`、`bg`、`light` |
| `bridge.ping` | 系统 | 桥接层（原生） | 连通性测试，返回 `pong` + 服务器时间 | 无专用子命令（用 `client.ping()` 或 `client.call("bridge.ping")` / 原始 TCP） | 无 |
| `bridge.list_commands` | 系统 | 桥接层（原生） | 列出所有已注册命令（含插件命令） | `list`（别名 `ls`） | 无 |
| `terrain.sync_config` | 地形 / 全局配置 | modular-terrain（总线插件） | 全局模块配置**读写**：`action="write"` 将 sizePrecision+moduleDirectories 写入管理器预制体；`action="read"` 由 Unity 返回其当前配置（经 Unity API，不解析 prefab 文件） | `terrain-sync`（别名 `tsync`；`--read` 进入读取模式） | 写：`sizePrecision`(>0)、`moduleDirectories`(array\<string\>)；读：无 |
| `terrain.module_list` | 地形 / 模块 | modular-terrain（总线插件） | 打印所有已加载模块的信息列表（id / 长宽 / 四边高度） | `module-list`（别名 `mlist`） | 无 |
| `terrain.module_size` | 地形 / 模块 | modular-terrain（总线插件） | 计算指定 id 模块的尺寸（长宽 / 四边高度 / 最大高度 / 是否符合精度） | `module-size`（别名 `msize`） | `id`(int, 必填) |
| `terrain.module_snap` | 地形 / 模块 | modular-terrain（总线插件） | 把指定 id 模块的尺寸（sizeX/sizeZ 与四边高度）吸附到精度整数倍 | `module-snap`（别名 `msnap`） | `id`(int, 必填) |
| `terrain.module_set` | 地形 / 模块 | modular-terrain（总线插件） | 按 id 设置模块指定字段（仅设置传入的参数，可多参数同时设置） | `module-set`（别名 `mset`） | `id`(int, 必填)、`sizeX`/`length`、`sizeZ`/`width`、`hZPlus`、`hXPlus`、`hZMinus`、`hXMinus`(float, 可选) |

**参数与返回结构**详见各命令专节：`mesh.bounds` 见「四-A」，`prefab.screenshot` 见「四-B」，地形相关命令（`terrain.sync_config` 及 `terrain.module_*`）见 **[modular-terrain/README.md](../modular-terrain/README.md)**（这些命令由 terrain 模块提供，仅挂载于本总线，但同样流经本命令总线）。
新增任意命令的方式见「五、如何扩展新命令」——只需写一个带 `[BridgeCommand]` 的静态方法，无需改动总线。

---

## 二、目录结构

```
UnityPythonBridge/
├── Unity/                                # ← 复制到 Unity 项目 Assets/ 下
│   └── Assets/UnityPythonBridge/
│       ├── Editor/
│       │   └── BridgeWindow.cs           # 控制窗口（启动/停止服务器、日志）
│       └── Runtime/
│           ├── BridgeCommandAttribute.cs # [BridgeCommand] 命令特性
│           ├── BridgeContext.cs          # 执行上下文 + 委托定义
│           ├── BridgeDispatcher.cs       # 反射扫描 + 命令分发
│           ├── BridgeServer.cs           # TCP 服务器（单行 JSON 协议）
│           ├── MainThreadRunner.cs       # 主线程执行队列
│           └── Commands/
│               ├── SceneTreeCommand.cs   # 命令 scene.tree（第一个功能）
│               ├── MeshBoundsCommand.cs   # 命令 mesh.bounds（包围盒计算）
│               ├── PrefabScreenshotCommand.cs  # 命令 prefab.screenshot（隔离复制+相机截图）
│               └── SystemCommands.cs     # bridge.ping / bridge.list_commands
│
└── python/                               # Python 侧（无需安装依赖）
    ├── unity_bridge/
    │   ├── __init__.py
    │   ├── client.py                     # TCP/JSON 客户端 UnityClient
    │   ├── cli.py                        # 命令行入口（tree / list / mesh-bounds / screenshot / terrain-sync）
    │   └── __main__.py                   # 支持 python -m unity_bridge
    ├── scripts/
    │   └── mock_unity_server.py          # 模拟 Unity 侧协议，无 Unity 也能联调
    └── requirements.txt
```

---

## 三、使用步骤

### 1. Unity 侧

1. 将 `Unity/Assets/UnityPythonBridge` 整个文件夹复制到你的 Unity 项目 `Assets/` 下。
2. **安装 Newtonsoft.Json 依赖**（Unity 官方包）：在 `Packages/manifest.json` 的 `dependencies` 中加入：

   ```json
   "com.unity.nuget.newtonsoft-json": "3.2.1"
   ```

   （建议通过 Package Manager → `+` → Add package by name 安装）

3. 打开菜单 **Tools → Unity Python Bridge**，点击「启动服务器」，看到日志提示监听 `127.0.0.1:21927` 即成功。
   - 也可以直接用菜单 **Tools → Unity Python Bridge → Start Server**。
   - Edit Mode 和 Play Mode 均可使用（命令在主线程执行）。

### 2. Python 侧

```bash
cd python

# 打印当前场景物体层级树（第一个命令功能）
python -m unity_bridge tree

# 附带显示每个物体的组件类型
python -m unity_bridge tree --components

# 输出原始 JSON（供程序化处理）
python -m unity_bridge tree --json

# 查看 Unity 侧所有可用命令
python -m unity_bridge list

# 计算 Assets 中网格/模型/预制体的轴对齐包围盒
python -m unity_bridge mesh-bounds Assets/Models/Rock.fbx

# 预制体同样支持；bounds 为 mesh-bounds 的别名，--json 输出原始数据
python -m unity_bridge bounds Assets/Prefabs/Tree.prefab --json

# 将预制体复制到场景隔离位置并截图保存为 PNG
#   path/output 为位置参数；--offset 为相机相对预制体的位置（必填，格式 "x,y,z"）
python -m unity_bridge screenshot Assets/Prefabs/Tree.prefab out/tree.png --offset "3,2,5"

# 正交相机 + 指定视野/分辨率/背景色；shot 为 screenshot 的别名
python -m unity_bridge shot Assets/Prefabs/Rock.fbx out/rock.png --offset "0,0,-8" \
    --orthographic --fov 3 --width 1280 --height 720 --bg "0.2,0.2,0.2,1"

# 将 Python 端地形配置同步到 Unity 管理器预制体（tsync 为别名）
#   直接给参数：Python 会写回仓库根 terrain_config.json 再同步
python -m unity_bridge terrain-sync --precision 0.5 \
    --dir Assets/ModularTerrain/Modules --dir Assets/ModularTerrain/Ramps
#   或读取已有 JSON 配置同步（不覆盖文件）
python -m unity_bridge tsync --config ../../terrain_config.json

# 自定义端口
python -m unity_bridge tree --port 21928
```

### 3. 无 Unity 环境联调（可选）

```bash
# 终端 A：启动模拟服务器（复刻 Unity 侧协议）
python scripts/mock_unity_server.py

# 终端 B：正常使用 CLI
python -m unity_bridge tree --components
```

---

## 四、树状输出示例

```
Scene: DemoScene  (3 个根物体)
Main Camera  [Transform, Camera, AudioListener]
Directional Light  [Transform, Light]
Player  [Transform, CharacterController, PlayerController]
├── Body  [Transform, Animator]
│   ├── LeftArm  [Transform]
│   └── RightArm  [Transform]
└── Head  [Transform, SkinnedMeshRenderer]
    └── Hat (inactive)  [Transform]
```

---

## 四-A、mesh.bounds 命令（计算包围盒）

计算 Assets 中**网格 / 模型 / 预制体**的轴对齐包围盒（AABB）。C# 侧使用
`AssetDatabase` 加载资源、`mesh.bounds` / `renderer.bounds` 计算，结果返回三个轴
的坐标范围，形如 `x:-2~6, y:-0.5~2, z:1~6`。

**参数**：`path`（string）—— 目标在 Assets 中的相对路径。可带或不带 `Assets/` 前缀；
支持 `.mesh`（网格）、`.fbx`/`.obj`/`.blend` 等（模型）、`.prefab`（预制体）。

**多网格处理**：若 fbx 模型或 prefab 内含多个网格，命令会实例化到原点（根变换重置为
identity，取几何固有范围），合并其下所有 `MeshRenderer` 与 `SkinnedMeshRenderer` 的包围盒，
返回能包围所有网格的合并包围盒。

**返回结构**：

```json
{
  "path": "Assets/Models/Rock.fbx",
  "resolvedPath": "Assets/Models/Rock.fbx",
  "type": "model",
  "min":  { "x": -2, "y": -0.5, "z": 1 },
  "max":  { "x": 6,  "y": 2,   "z": 6 },
  "center": { "x": 2, "y": 0.75, "z": 3.5 },
  "size": { "x": 8, "y": 2.5, "z": 5 },
  "format": "x:-2~6, y:-0.5~2, z:1~6"
}
```

**命令行**：

```bash
python -m unity_bridge mesh-bounds Assets/Models/Rock.fbx
# 文本输出：
#   path  : Assets/Models/Rock.fbx
#   type  : model
#   bounds: x:-2~6, y:-0.5~2, z:1~6
#     min : (-2, -0.5, 1)
#     max : (6, 2, 6)
#     size: (8, 2.5, 5)

python -m unity_bridge bounds Assets/Prefabs/Tree.prefab --json   # bounds 为 mesh-bounds 的别名
```

> 提示：`format` 字段即 `x:min~max, y:..., z:...` 可读格式；`min/max/center/size`
> 为机器可解析的数值，方便后续地形拼接计算。

---

## 四-B、prefab.screenshot 命令（预制体截图）

将目标预制体**复制到当前场景的隔离位置 `(9999,9999,9999)`**（远离原点，避免与场景中已有
物体重叠/碰撞），创建一台相机移动到相对预制体的位置并 `LookAt` 看向它，渲染后保存为 PNG，
**最后销毁临时复制的预制体与创建的相机**，不污染场景。

**参数**：

| 参数 | 类型 | 必填 | 说明 |
|---|---|---|---|
| `path` | string | ✅ | 预制体（或模型文件）在 Assets 中的相对路径 |
| `offset` | Vector3 | ✅ | 相机**相对预制体位置**的偏移，格式 `{x,y,z}` / `[x,y,z]` / `"x,y,z"` |
| `output` | string | ✅ | PNG 输出路径，**必须以 `.png` 结尾**（父目录会自动创建） |
| `orthographic` | bool | ❌ | 是否正交相机，默认 `false`（透视） |
| `fov` | number | ❌ | 视野：透视时=`fieldOfView`，正交时=`orthographicSize`；**默认使用 Unity 默认大小** |
| `width` | int | ❌ | 输出图片宽，默认 `1920` |
| `height` | int | ❌ | 输出图片高，默认 `1080` |
| `bg` | string | ❌ | 背景色 `"r,g,b[,a]"`（分量 0~1），默认**透明** |
| `light` | number | ❌ | 补光强度，默认 `0`（不补光）；`>0` 时在相机就位后追加一盏**与相机朝向一致的平行光**，相机完成即销毁 |

**坐标约定**：相机世界位置 = 隔离位置 `(9999,9999,9999)` + `offset`；`LookAt` 朝向隔离位置。
因此其它场景物体位于相机背后（约 9999 单位外），不会进入画面。

**返回结构**：

```json
{
  "path": "Assets/Prefabs/Tree.prefab",
  "resolvedPath": "Assets/Prefabs/Tree.prefab",
  "output": "C:\\...\\out\\tree.png",
  "cameraType": "perspective",
  "width": 1920,
  "height": 1080,
  "cameraPosition": { "x": 10002, "y": 10001, "z": 10004 },
  "lookAt": { "x": 9999, "y": 9999, "z": 9999 },
  "bytes": 10570
}
```

**命令行**：

```bash
# 透视相机，默认 1920x1080、透明背景
python -m unity_bridge screenshot Assets/Prefabs/Tree.prefab out/tree.png --offset "3,2,5"
# 文本输出：
#   prefab : Assets/Prefabs/Tree.prefab
#   output : C:\...\out\tree.png
#   camera : perspective  1920x1080
#   camPos : (10002.0, 10001.0, 10004.0)
#   lookAt : (9999, 9999, 9999)
#   bytes  : 10570

# 正交相机 + 自定义视野/分辨率/背景色
python -m unity_bridge shot Assets/Prefabs/Rock.fbx out/rock.png --offset "0,0,-8" \
    --orthographic --fov 3 --width 1280 --height 720 --bg "0.2,0.2,0.2,1"

# 补光（>=1 为正向）：相机就位后追加一盏与相机同向的平行光，照亮相机所见正面
python -m unity_bridge screenshot Assets/Prefabs/Tree.prefab out/tree_lit.png --offset "3,2,5" --light 1.5
```

> 注意：截图使用**当前激活场景的灯光**渲染。若场景没有平行光，预制体可能偏暗——
> 请确保截图时场景具备合适照明（你的地形编辑器工作流通常已有定向光）。
> 也可直接用 `--light <强度>` 让命令临时追加一盏与相机同向的平行光补光，`light=0`（默认）
> 则不补光；该补光在相机渲染完成后立即销毁，不会留在场景里。

---

## 五、如何扩展新命令（核心能力）

新增命令 = 新建一个静态方法 + 打上特性，**不需要改任何其他代码**：

```csharp
// Runtime/Commands/MyCommands.cs
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace UnityPythonBridge.Commands
{
    public static class MyCommands
    {
        [BridgeCommand("debug.log", "在 Unity Console 打印一行日志。参数: message(string)")]
        public static object Log(BridgeContext ctx, JObject args)
        {
            var message = args.Value<string>("message") ?? "";
            Debug.Log("[Bridge] " + message);
            return new { logged = true, length = message.Length };
        }
    }
}
```

保存后重新编译，Python 侧即可使用：

```bash
python -m unity_bridge call debug.log --message "hello"
```

> `call` 通用子命令暂未实现（当前有 tree / list），如有需要可以加，或直接用 Python API：
> ```python
> from unity_bridge import UnityClient
> with UnityClient() as c:
>     c.call("debug.log", message="hello")
> ```

**命令签名约定：**

```csharp
public static object MethodName(BridgeContext ctx, Newtonsoft.Json.Linq.JObject args)
```

- `ctx`：执行上下文（预留扩展位，如注入日志/连接信息）
- `args`：请求参数，用 `args.Value<T>("key")` 读取，缺省安全
- 返回值：任意可 JSON 序列化对象（匿名类、JObject、JArray、基本类型均可）

---

## 六、协议参考

```jsonc
// 请求（一行）
{"id": 1, "cmd": "scene.tree", "args": {"components": true}}

// 成功响应（一行）
{"id": 1, "ok": true, "data": { "...": "..." }}

// 失败响应（一行）
{"id": 1, "ok": false, "error": "未知命令: xxx（可用 bridge.list_commands 查看全部命令）"}
```

---

## 七、安全与注意事项

- 服务器**只绑定 127.0.0.1**，仅本机进程可访问，不会暴露到局域网。
- 命令在主线程执行，避免 Unity API 跨线程调用崩溃。
- 关闭 Bridge 窗口会自动停止服务器，不留后台线程。
- 若要在打包后的 Player 中使用，请自行评估：本项目针对 **Editor 开发期工具** 场景。
