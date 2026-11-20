# 发布验收与 QA 记录（RELEASE QA RECORD）

本文档记录 **ACT Spike** 在 [ADR-0001](adr/0001-procedural-geometric-art.md) 纯 2D 程序化几何垂直切片完成后的构建、全链路烟测、Windows 导出检查结果及手工试玩验收边界。

## 1. 已验证工具链与基线环境

- **引擎版本**：`Godot Engine v4.5.1.stable.mono.official.f62fdbde1`
- **引擎路径**：`D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe` / `D:\Godot\Godot_v4.5.1-stable_mono_win64.exe`
- **C# SDK / 目标框架**：.NET SDK `10.0.201`；`Godot.NET.Sdk/4.5.1`，`<TargetFramework>net8.0</TargetFramework>`，`<Nullable>enable</Nullable>`
- **显示与渲染**：`1280×720`，`canvas_items` / `keep`，`gl_compatibility`
- **主入口场景**：`res://Scenes/Title.tscn`（`TitleScreen`）
- **验证分支**：`spec/1-procedural-geometric-demo`
- **导出模板版本**：`4.5.1.stable.mono`（已由 `Godot_v4.5.1-stable_mono_export_templates.tpz` 导入至 `%APPDATA%\Godot\export_templates\4.5.1.stable.mono\`）

## 2. 自动化验证结果（`verify.ps1`）

执行命令：

```powershell
powershell -ExecutionPolicy Bypass -File verify.ps1
```

### 2.1 编译门

- `dotnet build "ACT Spike.csproj" --no-restore -warnaserror`：**通过**（`0 个警告`、`0 个错误`）。

### 2.2 全链路 Headless 烟测（`ACT_SMOKE=1 --quit-after 3600`）

1. **入口与全场景实例化（`FullChainSmoke`）**：
   - 验证 `main_scene == "res://Scenes/Title.tscn"` 且 `ACT_SMOKE=1` 启动后经 `TitleScreen` 自动进入 `Scenes/CoreLoop.tscn`。
   - 校验 `res://Scenes` 目录下全部 **13 个 `.tscn` 场景**加载、实例化、入树与清理销毁无异常、无残留或递归 `SmokeTest`，且全程无 `Engine.TimeScale` 或暂停状态泄漏：
     - `Scenes/Title.tscn`（`TitleScreen`）
     - `Scenes/Tutorial/Movement.tscn` → `Attack.tscn` → `Skill.tscn` → `Defense.tscn` → `Dodge.tscn` → `PerfectDefense.tscn` → `TimeSlow.tscn` → `Combat.tscn`（8 关 `TutorialStage`）
     - `Scenes/FormalRoute.tscn` → `Scenes/FormalEncounter.tscn`（`FormalStage`）
     - `Scenes/CoreLoop.tscn`（`TestArena`）
     - `Scenes/Victory.tscn`（`VictoryScreen`）
   - 验证 `TimeSlow.tscn` 激活时缓后销毁场景时，`TimeSlowManager._ExitTree()` 自动将 `Engine.TimeScale` 恢复为 `1.0`。
   - 验证 `CombatSfxId` 全部 **13 路**程序化 16-bit PCM 缓冲非空（`pcm.Length > 0` 且 `AudioStreamWav.Data.Length > 0`）。
2. **遗迹几何场景与视觉契约（`RuinStageSmoke` + `PresentationVisualSmoke`）**：
   - 验证主题渐进（`Tutorial -> FormalRoute -> FormalEncounter -> BossArena`）、`CoreLoop` 门柱与碰撞体、8 教学关与正式关布局、`PlayerVisual` / `EnemyVisual` / `CombatFeedbackLayer`、粒子/环/残影/时缓场渐隐及零 `Engine.TimeScale` 破坏。
3. **程序化 PCM16 音效契约（`ProceduralAudioSmoke`）**：
   - 验证 13 路波形峰值范围、RMS 能量、端点连续性、去重与唯一性、8 声部复音池、空敌人安全绑定、`--headless` 静音模式及 `_ExitTree` 退订。
4. **HUD / 标题 / 暂停 / 胜利流程（`HudFlowSmoke`）**：
   - 验证 `pause`（`Esc` / Xbox `Start`）与 `toggle_debug`（`F3`）绑定、暂停期间 `TimeSlow` 安全性、`CombatHud` 层级排序与默认关闭调试层、事件横幅与 `VictoryScreen`。
5. **Boss 战全流程与重置卫生（`SmokeTest`）**：
   - 玩家攻击、Boss 普通攻击未防御命中、破防抓取完美闪避（`+20` 时缓）、延迟范围移动脱离、时缓激活/释放、普通攻击完美格挡震慑、技能与斩击击败 Boss、玩家死亡、`Engine.TimeScale == 1.0` 退出检查（`[Smoke] ALL PASS`），以及 `R` 重置后新实例恢复满血/Boss 存活/满时缓资源/`Engine.TimeScale == 1.0`（`[Smoke] PASS R reset`）。

## 3. Windows 独立导出检查与可重复构建

### 3.1 预设配置与模板状态

已配置 [`../export_presets.cfg`](../export_presets.cfg)（`Windows Desktop` / `x86_64`，GUI 预设默认 `export_path=".builds/ACT_Spike.exe"`，`.builds/` 与 `build/` 均已加入 `.gitignore`）。

- **导出模板校验**：`%APPDATA%\Godot\export_templates\4.5.1.stable.mono\version.txt` 为 `4.5.1.stable.mono`（通过 Godot 编辑器导入 `Godot_v4.5.1-stable_mono_export_templates.tpz` 安装），包含 `windows_debug_x86_64.exe`、`windows_release_x86_64.exe` 及控制台包装器模板。
- **GUI 验收发布包**：`.builds/build_20261120/` 已成功构建并通过验收，包含 `ACT_Spike.exe`、`ACT_Spike.console.exe`、`ACT_Spike.pck` 与 `data_ACT Spike_windows_x86_64/`。

### 3.2 命令行可重复导出验证（已通过）

在仓库根目录执行以下命令验证 headless CLI 导出：

```powershell
New-Item -ItemType Directory -Force build\windows | Out-Null
& 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path . --export-release "Windows Desktop" "build/windows/ACT_Spike.exe"
```

- **执行结果**：退出码 `0`（`[ DONE ] dotnet_publish_project`、`[ DONE ] savepack`）。
- **生成产物**（`build/windows/`）：
  - `ACT_Spike.exe`（`96,823,808` 字节）
  - `ACT_Spike.console.exe`（`49,664` 字节）
  - `ACT_Spike.pck`（`2,352,384` 字节）
  - `data_ACT Spike_windows_x86_64/`（.NET 8 运行库目录）

## 4. 文档对齐与旧资产清理状态

- **已更新**：`README.md`、`Docs/DEMO_EXECUTION_PLAN.md`、`Docs/PROJECT_BUILD_PLAN.md` 已全部对齐至 ADR-0001 纯 2D 程序化几何与 13 路 PCM 音效基线，清除 `320×180`、像素编辑器与生成式管线旧指引。
- **已清理作废文件（遵循 ADR-0001）**：
  - 已从仓库移除：`Docs/ART_ASSET_WORKFLOW.md`、`Docs/PIXEL_ART_PRODUCTION_PIPELINE.md`、`Docs/NEXT_PHASE_PROMPT.md`、`Docs/PHASE_PROMPTS.md`
  - 已从工作树清理：`Docs/ART_CONCEPT_PROMPTS*.md`、`Docs/COMFYUI_MODEL_DEPLOYMENT.md`、`Docs/CONCEPT_ART_*.md`、`Docs/comfyui_workflows/`、`Docs/debug.log`

## 5. 手工试玩 QA 验收记录（2026-11-20 · `.builds/build_20261120`）

- [x] **程序化音频听感**：`2026-11-20` 于独立构建 `.builds/build_20261120` 验收通过（音效表现正常）。
- [x] **双输入路径与调试开关**：
  - [x] 键鼠输入（含 `F3` 调试覆盖层切换）于 `.builds/build_20261120` 验收通过。
  - [x] 手柄输入于 `.builds/build_20261120` 验收通过。
- [x] **Windows 独立导出包运行**：已于 `2026-11-20` 通过 Godot GUI 导入 `Godot_v4.5.1-stable_mono_export_templates.tpz` 完成 `.builds/build_20261120` 独立构建并完成启动与游玩验收；命令行 `--export-release "Windows Desktop" "build/windows/ACT_Spike.exe"` 亦已验证退出码 `0`。
