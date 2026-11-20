# 2D ACT Demo 执行计划（ADR-0001 收口版）

> 本文档已按 [`adr/0001-procedural-geometric-art.md`](adr/0001-procedural-geometric-art.md)、[`../GLOSSARY.md`](../GLOSSARY.md) 与 [`../CODING_STANDARDS.md`](../CODING_STANDARDS.md) 对齐至当前已完成的纯 2D 程序化几何垂直切片基线。

## 已锁定目标

交付一个可在 Windows 独立运行的 2D ACT 垂直切片 Demo（原生 `1280×720`，`canvas_items` / `keep` 缩放），全程采用**「遗迹几何表现」**（硬边剪影切面、分层荒原高塔背景、动态形变与几何斩击/护盾/残影）与**零外部资产程序化 PCM16 音效**：

- **完整 13 场景通关链路**：
  1. `Scenes/Title.tscn`（中文极简几何标题页，主入口；`ACT_SMOKE=1` 时自动转入 `CoreLoop.tscn`）
  2. 8 个独立机制教学关 `Scenes/Tutorial/{Movement,Attack,Skill,Defense,Dodge,PerfectDefense,TimeSlow,Combat}.tscn`
  3. `Scenes/FormalRoute.tscn`（无敌人的荒原推进段）
  4. `Scenes/FormalEncounter.tscn`（前置遭遇战，清敌解锁出口门柱）
  5. `Scenes/CoreLoop.tscn`（核心 Boss 场地：低阶台阶、Boss 警戒门柱、右侧通关门柱）
  6. `Scenes/Victory.tscn`（通关结算页，支持「再战 Boss」与「返回标题」）
- **玩家核心循环**：移动、跳跃、普通攻击、`K` 方向位移技能、闪避、按住格挡、按住 `Q` 时缓、局内 `Esc` 暂停与 `R` 重置当前关。
- **Boss 三种战斗前摇信号与应对契约**：
  1. `NORMAL STRIKE`（金色扇形扫掠前摇）：可格挡或闪避（`0.25s` 真实时间戳完美格挡可震慑 Boss）。
  2. `BREAK GRAB`（绯红矩形区 + 收缩 X 不可格挡警示）：破防抓取，必须用闪避或 `K` 位移躲避。
  3. `DELAYED AREA`（紫罗兰收缩倒计时圆环）：延迟范围攻击，通过移动脱离或开启时缓争取调整窗口。
- **程序化音画与正式 HUD**：
  - 默认正式中文 HUD（左斜切 HP / GUARD / TIME 资源条、顶部目标条、底部 Boss 血条与事件横幅），按 `F3` 切换调试覆盖层。
  - `ProceduralSfxBank` 启动时在内存合成 13 个 22050 Hz 16-bit 单声道 PCM 战斗/前摇/时缓音效，无外部 `.wav`/`.ogg` 依赖。
- **可重复验证与打包交付**：`verify.ps1` 强制校验 0 警告编译与全链路 `ACT_SMOKE=1` 烟测；`export_presets.cfg` 配置 Windows Desktop (`x86_64`) 导出。

## 范围控制（Scope Out）

不包含第二关、新 Boss、普通小怪图鉴扩展、装备/背包/技能树/掉落系统、长剧情、完整 BGM 作曲、外部图片/精灵表/3D 模型资产，以及 `D:\Godot\Program` 项目的任何修改。

## 架构与实现现状（已完成模块）

1. **核心战斗与真实时间戳时序**
   - `CombatResolver` 与 `PlayerController` / `EnemyController` 使用 `Time.GetTicksMsec()` 真实时间戳判定 `0.25s` 完美格挡与完美闪避。
   - `TimeSlowManager` 独占控制 `Engine.TimeScale`（`0.35x`，最大资源 `100`，消耗 `30/s`，恢复 `12/s`，完美闪避返还 `+20`），并在暂停、重置与退树时强制归位 `1.0`。
   - 控制器通过不可变 `readonly record struct` 表现事件（`CombatPresentationEvents.cs`）向外广播状态，不耦合渲染或音频内部细节。
2. **遗迹几何表现与场景进度氛围**
   - `RuinGeometryPalette`、`GeometricDraw`、`UiFonts` 提供统一色彩、多边形/斜切条绘制与中文回退字体缓存。
   - `PlayerVisual`、`EnemyVisual`、`CombatFeedbackLayer` 实现硬边剪影切面形变、残影、斩击弧、多边形护盾、三种前摇、冷色时缓视觉场及局部非破坏顿帧/镜头微震。
   - `RuinEnvironmentLayer` 与 `RuinStageTheme` 为全流程提供「干旱草原荒原」「古代金属巨构」「中央遗迹高塔」与「荒原暖侧光」，并随教学 → 正式路线 → 遭遇战 → Boss 场地渐进改变色温、高塔尺度与柱廊密度；所有门柱用发光菱形信标与状态变色替代旧调试文字。
3. **标题页、教学/正式流程、HUD 与暂停/胜利页**
   - `TitleScreen`、`PauseMenuOverlay`（`Layer=20`, `ProcessMode=Always`）、`CombatHud`、`VictoryScreen` 共用 `GeometricMenuController` 与硬边几何面板，完整支持键鼠与 Xbox 手柄导航。
4. **程序化 PCM16 战斗音效**
   - `CombatSfxId`、`ProceduralSfxBank`、`CombatAudioController` 覆盖 13 路战斗/前摇/时缓音效，带直流去偏、余弦防爆音包络与 8 声部复音池；headless 与 `ACT_SMOKE=1` 下自动静音运行。

## 可重复自动化检查

```powershell
# 完整验收门（0 警告构建 + 全链路 headless ACT_SMOKE=1 烟测 + R 重置）
powershell -ExecutionPolicy Bypass -File verify.ps1

# 仅执行 0 警告编译
dotnet build "ACT Spike.csproj" --no-restore -warnaserror
```

`verify.ps1` 运行 `D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe`（可用 `$env:GODOT_CONSOLE` 覆盖）以 `--headless --path . --log-file .godot\headless-smoke.log --quit-after 3600` 启动并注入 `ACT_SMOKE=1`，验证：

- 从 `Scenes/Title.tscn` 自动重定向进入 `Scenes/CoreLoop.tscn`；
- `FullChainSmoke` 遍历加载、实例化并干净销毁全部 13 个 `.tscn` 场景、校验跳转链、`Engine.TimeScale == 1.0` 卫生及 13 路 PCM16 缓冲非空；
- `RuinStageSmoke`、`PresentationVisualSmoke`、`ProceduralAudioSmoke`、`HudFlowSmoke` 全部契约；
- 玩家攻击、Boss 三种攻击前摇与应对、真实时间戳完美格挡/闪避、时缓消耗与恢复、死亡、presentation events、`[Smoke] ALL PASS` 与 `[Smoke] PASS R reset`（含重置后 `Engine.TimeScale == 1.0`），且日志中无 `[Smoke] FAIL`。

## 手工试玩与发布验收边界

1. **视觉可读性**：关闭 `F3` 调试层，仅凭「遗迹几何表现」剪影、三种战斗前摇信号、门柱颜色与时缓视觉场完成从教学到 Boss 通关。
2. **音频与手感**：非 headless 桌面运行下验证 13 路程序化音效音量平衡、无爆音破音、顿帧与震动流畅不卡顿。
3. **双输入路径**：实机测试键鼠与 Xbox 手柄全部战斗映射及标题/暂停/胜利界面菜单导航（无跨设备阻塞或串扰）。
4. **Windows 独立导出**：在安装 Godot `4.5.1.stable.mono` 导出模板后执行 `--export-release "Windows Desktop" "build/windows/ACT_Spike.exe"`，并在干净目录双击验证启动、游玩、胜负与重置（详情见 [`RELEASE_QA_RECORD.md`](RELEASE_QA_RECORD.md)）。
