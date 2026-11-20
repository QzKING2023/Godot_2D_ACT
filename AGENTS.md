## 代理技能

### Issue 跟踪器

项目的问题和规格说明存放在 GitHub Issues 中，使用 `gh` CLI 操作。详见 `Docs/agents/issue-tracker.md`。

### 分流标签

项目使用默认的 `needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human` 和 `wontfix` 标签。详见 `Docs/agents/triage-labels.md`。

### 领域文档

项目采用单上下文的 `GLOSSARY.md` 和 `Docs/adr/` 布局。详见 `Docs/agents/domain.md`。

### 构建与验证

- 完整校验（0 警告构建 + 全阶段 `ACT_SMOKE=1` 烟测日志断言）：运行 `powershell -ExecutionPolicy Bypass -File verify.ps1`（本机 Godot 控制台默认路径 `D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe`，可用 `GODOT_CONSOLE` 覆盖）。
- 评审规范见 `CODING_STANDARDS.md`；本机默认 Shell 为 Windows PowerShell 5.1（语句分隔用 `;`）。
