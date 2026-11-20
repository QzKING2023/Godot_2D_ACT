## 代理技能

### Issue 跟踪器

读取、认领与单票验收后关闭 GitHub Issues 使用 `gh` CLI，完成标准与命令见 `Docs/agents/issue-tracker.md`。

### 分流标签

项目使用默认的 `needs-triage`、`needs-info`、`ready-for-agent`、`ready-for-human` 和 `wontfix` 标签。详见 `Docs/agents/triage-labels.md`。

### 领域文档

项目采用单上下文的 `GLOSSARY.md` 和 `Docs/adr/` 布局。详见 `Docs/agents/domain.md`。

### 构建与验证

- 完整校验（0 警告构建 + 全阶段 `ACT_SMOKE=1` 紧凑烟测摘要，完整日志落盘 `.godot/headless-smoke.log`）：运行 `powershell -ExecutionPolicy Bypass -File verify.ps1`（可用 `-VerboseSmoke` 输出全量日志，`-CheckIssues` 校验近期提交引用票据是否已关闭；本机 Godot 默认路径 `D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe`，可用 `GODOT_CONSOLE` 覆盖）。
- 评审规范见 `CODING_STANDARDS.md`；本地预提交构建护栏见 `Scripts/hooks/pre-commit`（`git config core.hooksPath Scripts/hooks`）；本机默认 Shell 为 Windows PowerShell 5.1（语句分隔用 `;`）。
