# 项目构建计划（ADR-0001 完工基线）

> 详细执行与验收门槛见 [`DEMO_EXECUTION_PLAN.md`](DEMO_EXECUTION_PLAN.md)；视觉与领域词汇见 [`../GLOSSARY.md`](../GLOSSARY.md)；架构决策见 [`adr/0001-procedural-geometric-art.md`](adr/0001-procedural-geometric-art.md)。

## 已确认工具链与工程配置

- **引擎**：`Godot Engine v4.5.1.stable.mono.official.f62fdbde1`
- **C# SDK / 运行时**：`Godot.NET.Sdk/4.5.1`，目标框架 `.NET 8.0`（`ACT Spike.csproj`，`<Nullable>enable</Nullable>`，`<RootNamespace>ACTSpike</RootNamespace>`）
- **画幅与渲染器**：`1280×720`，`window/stretch/mode="canvas_items"`，`window/stretch/aspect="keep"`，`rendering/renderer/rendering_method="gl_compatibility"`
- **主入口与自动加载**：`run/main_scene="res://Scenes/Title.tscn"`，Autoload `RunSession="*res://Scripts/ACT/Core/RunSession.cs"`（`/root/RunSession`）
- **工程边界**：本仓库独立存在；不修改 `D:\Godot\Program` 或仓库外任何项目。

## C# 模块结构（`Scripts/ACT/`）

- `Scripts/ACT/Core`（`ACTSpike.ACT.Core`）：`ActNaming`、`PlayerState`（`Idle, Move, Jump, Attack, Dodge, Block, TimeSlow, Hitstun, Skill`）、`RunSession`。
- `Scripts/ACT/Input`（`ACTSpike.ACT.Input`）：`ActInput`（统一读取键鼠、Xbox 手柄与菜单导航事件）。
- `Scripts/ACT/Config`（`ACTSpike.ACT.Config`）：`CombatWindowConfig`、`TimeSlowConfig`（`[GlobalClass]` 资源）。
- `Scripts/ACT/Combat`（`ACTSpike.ACT.Combat`）：`CombatPresentationEvents`（7 个不可变 `readonly record struct`）、`CombatResolver`、`TimeSlowManager`。
- `Scripts/ACT/Player`（`ACTSpike.ACT.Player`）：`PlayerController`。
- `Scripts/ACT/Enemy`（`ACTSpike.ACT.Enemy`）：`EnemyController`（`EnemyState`、`BossAttackKind`、`BossAttackDefinition`）。
- `Scripts/ACT/Visual`（`ACTSpike.ACT.Visual`）：`RuinGeometryPalette`、`GeometricDraw`、`UiFonts`、`GeometricMenuController`、`PlayerVisual`、`EnemyVisual`、`CombatFeedbackLayer`、`RuinStageTheme`、`RuinEnvironmentLayer` 与视觉契约烟测模块。
- `Scripts/ACT/Audio`（`ACTSpike.ACT.Audio`）：`CombatSfxId`（13 路）、`ProceduralSfxBank`（内存 PCM16 合成）、`CombatAudioController`（8 声部复音池）与音频烟测模块。
- `Scripts/ACT/HUD`（`ACTSpike.ACT.HUD`）：`CombatHud`（极简中文正式 HUD + `F3` 调试层）、`PauseMenuOverlay`（`Esc`/`Start` 暂停浮层）与 HUD/流程烟测模块。
- `Scripts/ACT/Arena`（`ACTSpike.ACT.Arena`）：`TitleScreen`、`TutorialStage`、`FormalStage`、`TestArena`、`VictoryScreen`、`SmokeTest`、`FullChainSmoke`。

## 完整场景链路（13 个 `.tscn`）

1. `Scenes/Title.tscn`：中文几何标题页（「从教学开始」「直接挑战 Boss」「退出」；`ACT_SMOKE=1` 自动跳转至 `CoreLoop.tscn`）。
2. `Scenes/Tutorial/{Movement,Attack,Skill,Defense,Dodge,PerfectDefense,TimeSlow,Combat}.tscn`：8 关单机制教学链。
3. `Scenes/FormalRoute.tscn`：正式路线过渡段（无敌人）。
4. `Scenes/FormalEncounter.tscn`：正式前置遭遇战（清敌开启右侧门柱）。
5. `Scenes/CoreLoop.tscn`：Boss 战场地（台阶路线、Boss 警戒门柱、出口门柱）。
6. `Scenes/Victory.tscn`：通关结算页（「再战 Boss」「返回标题」）。

## 输入映射（`project.godot` + `ActInput.cs`）

| 动作 | 键盘 / 鼠标 | Xbox 手柄 | 说明 |
|---|---|---|---|
| `move_*` | `W / A / S / D` | 左摇杆（`LS`） | 移动与 `K` 技能位移方向 |
| `jump` | `Space` | `A` | 跳跃 |
| `attack` | `J` / 鼠标左键 | `X` | 普通斩击 |
| `dodge` | `Left Shift` | `B` | 闪避（含 `0.25s` 完美闪避） |
| `block` | `Left Ctrl`（按住） | `RB`（按住） | 格挡（起始 `0.25s` 完美格挡） |
| `time_slow` | `Q`（按住） | `LB`（按住） | 时缓视觉场（`0.35x` 流速） |
| `skill` | `K`（按住/配合方向） | `Y` | 方向位移斩击技能 |
| `reset_round` | `R` | — | 重置当前关并清空 `RunSession` |
| `pause` | `Esc` | `Start` | 切换局内暂停浮层 |
| `toggle_debug` | `F3` | — | 切换左上角调试状态覆盖层 |

## 时序与状态契约

- 所有战斗窗口、冷却与前摇倒计时使用 `Time.GetTicksMsec()` 真实时间戳，与帧率及 `Engine.TimeScale` 解耦。
- `Engine.TimeScale` 仅由 `TimeSlowManager` 独占控制；`PauseMenuOverlay` 暂停、`TimeSlowManager._ExitTree()` 与场景重置均强制将 `Engine.TimeScale` 归位 `1.0`。
- 默认 `CombatWindowConfig` 完美格挡与完美闪避窗口均为 `0.25s`；三种 Boss 攻击保持清晰区分：`Normal`（`0.70s` 前摇，可格挡/闪避）、`BreakGrab`（`0.95s` 前摇，破防不可格挡）、`DelayedArea`（`2.00s` 前摇，鼓励移位或时缓）。

## 构建、验证与 Windows 导出状态

- **自动化验收**：`powershell -ExecutionPolicy Bypass -File verify.ps1`（执行 `dotnet build "ACT Spike.csproj" --no-restore -warnaserror` 与 `ACT_SMOKE=1` headless 全链路烟测，要求 `0 个警告`、`0 个错误`、`[Smoke] ALL PASS`、`[Smoke] PASS R reset` 且无 `[Smoke] FAIL`）。
- **Windows 导出配置**：根目录已提交 `export_presets.cfg`（预设名称 `"Windows Desktop"`，架构 `x86_64`，输出 `"build/windows/ACT_Spike.exe"`，`build/` 已加入 `.gitignore`）。
- **导出模板依赖**：需要对应 Godot `4.5.1.stable.mono` 的导出模板（`%APPDATA%\Godot\export_templates\4.5.1.stable.mono\windows_debug_x86_64.exe` 与 `windows_release_x86_64.exe`）。本机当前环境验证结果与完整导出步骤记录于 [`RELEASE_QA_RECORD.md`](RELEASE_QA_RECORD.md) 与根目录 [`../README.md`](../README.md)。
