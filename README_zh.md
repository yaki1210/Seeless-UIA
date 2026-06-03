# SeelessUIA

[English](README.md)

面向 AI agent 的 Windows UI Automation 命令行工具。通过 Microsoft UI Automation（UIA）将桌面应用暴露为结构化可访问树——附带稳定的元素引用（ref）和原生交互命令。

采用 snapshot + ref 模型：先截图获取元素，再通过 ref 交互。为 AI agent 工作流设计，在 token 昂贵的场景下，结构化数据优于像素推断。

## 定位

- **UIA 优先的自动化底层**，适用于暴露有意义可访问树的 Win32、WPF、WinForms、UWP 桌面应用
- 提供**稳定的 ref**（`e1`、`e2`……），agent 可对其进行 click、fill、expand、验证
- 返回**结构化状态**：role、name、value、checked、expanded、selected、automationId、bounding rect
- 以轻量 CLI + 常驻 TCP daemon（自动启动）运行，适合 agent 管道化调用

## 不是

- **不是通用视觉桌面 agent**——它不"看"屏幕，它读取 UIA 树。未暴露 UIA 的应用（canvas 渲染、自定义框架、部分 Electron 应用）将返回空或不完整的快照
- **不是浏览器场景中 agent-browser 的替代品**。如果你在浏览器里操作网页，直接用 agent-browser / CDP
- **不是截图到操作的转换工具**。截图仅用于本地 VLM 模型的视觉验证，不作为主要发现机制

## 快速开始

```bash
# 安装
npm install -g seeless-uia

# 需要：Windows、.NET 10 Runtime
```

```bash
# 操作计算器
seeless-uia app launch calc
seeless-uia wait 2000
seeless-uia snapshot -i          # 获取可交互元素及 ref
seeless-uia click e28            # 点击"5"
seeless-uia get text control:Text  # 读取显示结果

# 操作 VS Code
seeless-uia app launch code
seeless-uia wait 2000
seeless-uia snapshot -i          # 标签页、按钮、文件树、状态栏
seeless-uia find text "资源管理器" click
```

## 核心循环

```
1. windows          发现窗口 → w1、w2、w3
2. snapshot w1 -i    获取快照 → e1、e2、e3……
3. click e2          通过 ref 交互
4. snapshot -i --diff ␤证变更（added/removed/modified）
```

Ref 在窗口状态变更前始终有效。对话框打开、标签切换、展开折叠后，需重新获取快照。

**节省 token 的技巧：**
- 用 `get value`、`find text "X" text` 或 `wait --text "X"` 验证文本——无需快照
- `snapshot -i --diff` 仅返回变更元素（内部仍执行完整快照，消耗相同）
- 对 AI 消费优先使用纯文本输出而非 `--json`——字符更少、无转义开销

## 核心命令

| 类别 | 命令 |
|------|------|
| **发现** | `windows`、`window wN`、`app launch <name>`、`ping`、`close [wN]` |
| **快照** | `snapshot [wN] [-i] [-c] [-d <n>] [--diff] [--json]` |
| **交互** | `click`、`dblclick`、`fill`、`type`、`press`、`hover`、`scroll`、`check`、`uncheck`、`focus`、`expand`、`collapse`、`select` |
| **读取** | `get text`、`get value`、`get box`、`get count`、`get attr` |
| **检查** | `is visible`、`is enabled`、`is checked` |
| **查找** | `find role/text/label/placeholder <条件> <动作>` |
| **等待** | `wait <sel>`、`wait <ms>`、`wait --text <文本>` |
| **原始** | `keyboard type`、`keydown/up`、`mouse move/down/up/wheel`、`clipboard read/write/copy/paste`、`screenshot` |

完整命令参考见 [技能文档](skill-data/core/SKILL.md)。

## 兼容性

SeelessUIA 依赖应用暴露 UIA 可访问树。不同 UI 框架覆盖度不同：

| 框架 | 支持程度 | 备注 |
|------|---------|------|
| **UWP** | 良好 | 设置、计算器——完整树访问 |
| **WPF** | 良好 | 标准控件良好暴露 |
| **Win32** | 部分 | 标准控件可用；系统/高权限进程可能返回空树 |
| **Electron** | 部分 | 文件树、标签页、按钮暴露；headings/ARIA/输入区常缺失 |
| **自定义渲染** | 差 | Canvas、自绘控件极少暴露 UIA |

详见 [docs/compatibility.md](docs/compatibility.md)。欢迎贡献测试数据。

## 快照输出

```
- document "project - Visual Studio Code" [ref=e1] scrollable
  - tablist [actions-container]
    - tab "资源管理器 (Ctrl+Shift+E)" [ref=e2] selectable
    - tab "搜索 (Ctrl+Shift+F)" [ref=e3] selectable
  - tree "文件资源管理器" [ref=e48] clickable
    - treeitem "src" [ref=e49] selectable
      - generic "E:\\project\\src" [ref=e50] clickable
```

每行包含：缩进树深度、role、名称、属性 `[ref, state, hints]`、交互类型 `(clickable, selectable, editable...)`。

## 从源码安装

```bash
git clone https://github.com/yaki1210/Seeless-UIA
cd Seeless-UIA
dotnet build SeelessUIA.slnx -c Release
# 直接运行：
src\bin\Release\net10.0-windows\SeelessUIA.exe snapshot -i
```

**环境要求：** Windows、.NET 10 SDK（Windows Desktop workload）。

## 文档

- [AI agent 技能文档](skill-data/core/SKILL.md) — 完整命令参考、pipeline 内部结构、故障排除
- [应用兼容性](docs/compatibility.md) — 已测试应用及已知限制
- [命令参考](skill-data/core/references/commands.md) — 完整参数 + JSON schema 参考

## 架构

CLI → TCP daemon（自动启动，端口 9222）→ UIA COM API。Daemon 跨命令维护一个 RefMap + WindowRegistry。快照执行五阶段管道：Build → Clean → Detect Interactivity → Assign Refs → Render。

## 许可证

MIT
