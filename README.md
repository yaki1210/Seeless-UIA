# UIAgent — Windows UI Automation for AI Agents

将 agent-browser 的"感知-规划-执行"框架迁移到 Windows UI Automation (UIA)，为 AI agent 提供操控 Windows 桌面应用的能力。

## 快速开始

```powershell
cd uia && dotnet build

# 列出所有窗口
dotnet run --project UiaAgent -- windows

# structured 模式 (默认) — 保留分层结构，list/group/generic 作分组容器
dotnet run --project UiaAgent -- snapshot --pid 12345

# interactive 模式 (-i) — 扁平，仅 ref 元素
dotnet run --project UiaAgent -- snapshot --pid 12345 -i

# 附带 refs 列表 (-r)
dotnet run --project UiaAgent -- snapshot --pid 12345 -i -r

# 启动 daemon 服务
dotnet run --project UiaAgent -- daemon --port 9222
```

## 命令一览

| 命令 | 说明 |
|------|------|
| `snapshot --pid <pid> [-i] [-r] [--depth <n>]` | 对运行中进程执行 snapshot |
| `test [--app <name>] [-i] [--depth <n>]` | 启动应用并 snapshot，完成后自动关闭 |
| `windows` | 列出所有可见顶层窗口 (HWND + PID + 标题) |
| `daemon [--port <port>]` | 启动 TCP daemon，监听 NDJSON 协议 |

### 模式说明

| 模式 | 标志 | 行为 |
|------|------|------|
| 结构化 (默认) | 无 | 保留 list/group/generic 作分组容器，显示层次缩进 |
| 交互模式 | `-i` | 仅输出 ref 元素，扁平无容器，等同 agent-browser `-i` |
| 显示 refs | `-r` | 末尾附带 refs 列表 (交互模式下供 AI agent 使用) |

## 输出示例

### 结构化模式 (默认)
```
- generic "OpenCode"
  - generic
    - button "最小化" [ref=e1] clickable
    - button "关闭" [ref=e3] clickable
  - generic "项目和会话"
    - link "javawork" [ref=e16] clickable
    - button "agent-browser-0.27.0" [ref=e18] clickable
```

### 交互模式 (-i)
```
- button "最小化" [ref=e1] clickable
- button "关闭" [ref=e3] clickable
- link "javawork" [ref=e16] clickable
- button "agent-browser-0.27.0" [ref=e18] clickable
```

## 输出格式

```text
{缩进}- {role} "{name}" [disabled, ref=eN, selectorId] {kind} [{state}] : {value}
```

hints 仅保留状态信息，不重复 pattern 名称:

| kind | hints |
|------|-------|
| `clickable` | 无 |
| `editable` | 无 |
| `toggleable` | `[off]` / `[on]` / `[mixed]` |
| `selectable` | 无 |
| `expandable` | `[collapsed]` / `[expanded]` |
| `scrollable` | 无 |
| `focusable` | 无 |

## 项目结构

```
uia/
├── UiaAgent.sln
├── UiaAgent/
│   ├── Program.cs                    # CLI 入口 (snapshot / test / windows / daemon)
│   ├── Snapshot/
│   │   ├── UiaNode.cs                # 内部 IR 节点 (= TreeNode)
│   │   ├── SnapshotPipeline.cs       # 6 阶段清洗管线 (= take_snapshot)
│   │   ├── TreeBuilder.cs            # UIA 树遍历 → TreeNode 构建 (= build_tree)
│   │   ├── TreeCleaner.cs            # 过滤/穿透/聚合/去重
│   │   ├── TreeRenderer.cs           # 渲染缩进文本 (= render_tree)
│   │   ├── RoleMapping.cs            # ControlType → role 映射表
│   │   ├── ControlTypeLookup.cs      # ControlType 名称查找
│   │   └── SnapshotOptions.cs        # Snapshot 参数
│   ├── Element/
│   │   ├── RefMap.cs                 # ref ID → 元素元数据 (= RefMap)
│   │   ├── RefEntry.cs               # 单条 ref 记录 (= RefEntry)
│   │   ├── ElementResolver.cs        # 双路径定位 (= resolve_element_center)
│   │   └── RoleNameTracker.cs        # role:name 去重计数 (= RoleNameTracker)
│   ├── Interaction/
│   │   ├── ActionExecutor.cs         # 动作执行入口 (Pattern 优先, SendInput 兜底)
│   │   ├── PatternActions.cs         # UIA Pattern 动作 (Invoke/Toggle/Value...)
│   │   └── SendInputActions.cs       # Win32 SendInput fallback
│   ├── Window/
│   │   ├── WindowManager.cs          # 窗口枚举/激活/关闭
│   │   ├── ProcessManager.cs         # 进程启动/终止
│   │   └── ScreenshotCapture.cs      # 窗口截图
│   ├── Daemon/
│   │   └── DaemonServer.cs           # TCP socket + NDJSON 协议 (= daemon.rs)
│   └── Protocol/
│       ├── Request.cs                # JSON 请求模型
│       └── Response.cs               # JSON 响应模型
├── UiaAgent.Tests/                   # 单元测试 (45 个用例)
└── tests/                            # Python 集成测试脚本
    ├── test_snapshot.py
    └── output/                       # snapshot 结果 txt 输出
```

## Snapshot 处理管线

```
UIA 树获取 (TreeBuilder)
  → 噪音过滤 (零尺寸/Offscreen/TitleBar/ToolTip)
  → 容器穿透 (Pane/Group 单子节点 → 折叠)
  → 连续 Text 聚合 (类似 StaticText 合并)
  → 父-子名称去重
  → 交互性检测 (Pattern 可用性: Invoke/Toggle/Value/Selection...)
  → ref 分配 (e1, e2, ...) + RoleNameTracker 去重
  → 缩进文本渲染 (structured / interactive)
```

## Daemon 协议 (NDJSON)

```
请求: {"action":"snapshot","id":"1","interactive":false,"processId":12345}
响应: {"id":"1","success":true,"data":{"snapshot":"...","refs":{...}}}
```

支持动作: `snapshot`, `click`, `fill`, `type`, `hover`, `scroll`, `check`, `uncheck`, `focus`, `press`, `window_list`, `window_focus`, `window_close`, `app_launch`, `screenshot`, `close`

## 核心设计决策

| 决策 | 说明 |
|------|------|
| Pattern 优先, SendInput 兜底 | UIA Pattern (Invoke/Value/Toggle) 优先，Win32 SendInput 作为 fallback |
| 容器穿透 | Pane/Group 无语义值时穿透，减少噪音节点 |
| 结构化默认输出 | 保留 list/group/generic 分组容器，`-i` 切换为扁平模式 |
| hints 去冗余 | `clickable [invoke]` → `clickable`，仅保留状态信息 |
| RuntimeId 作为稳定 ID | 等价于 agent-browser 的 backend_node_id，缓存加速定位 |
| 输出兼容 | 与 agent-browser snapshot 格式一致，AI agent 无感知差异 |

## 运行测试

```powershell
# 单元测试
cd uia && dotnet test

# 集成测试 — 结构化模式
cd uia\tests && python test_snapshot.py --pid <pid>

# 集成测试 — 交互模式  Interactive elements only
cd uia\tests && python test_snapshot.py --pid <pid> -i 
#refs only
cd uia\tests && python test_snapshot.py --pid <pid> -r

# 自动启动测试
cd uia\tests && python test_snapshot.py --app notepad --test-mode
```

## 环境要求

- Windows 10/11
- .NET SDK 10.0+
- Python 3.8+ (仅集成测试脚本需要)
