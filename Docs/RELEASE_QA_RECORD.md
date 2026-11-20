# 发布验收与 QA 记录（RELEASE QA RECORD）

本文档记录 **ACT Spike** 在 [ADR-0001](adr/0001-procedural-geometric-art.md) 纯 2D 程序化几何垂直切片完成后的构建、全链路烟测、Windows 导出检查结果及手工试玩验收边界。

## 1. 已验证工具链与基线环境

- **引擎版本**：`Godot Engine v4.5.1.stable.mono.official.f62fdbde1`
- **引擎路径**：`D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe` / `D:\Godot\Godot_v4.5.1-stable_mono_win64.exe`
- **C# SDK / 目标框架**：.NET SDK `10.0.201`；`Godot.NET.Sdk/4.5.1`，`<TargetFramework>net8.0</TargetFramework>`，`<Nullable>enable</Nullable>`
- **显示与渲染**：`1280×720`，`canvas_items` / `keep`，`gl_compatibility`
- **主入口场景**：`res://Scenes/Title.tscn`（`TitleScreen`）
- **验证分支**：`spec/1-ticket-7`（基线 `spec/1-procedural-geometric-demo`）

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

## 3. Windows 独立导出检查与模板步骤

### 3.1 预设验证命令与当前状态

已配置 [`../export_presets.cfg`](../export_presets.cfg)（`Windows Desktop` / `x86_64` / `export_path="build/windows/ACT_Spike.exe"`，且 `build/` 已加入 `.gitignore`）。在本机执行导出验证命令：

```powershell
& 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path . --export-release "Windows Desktop" "build/windows/ACT_Spike.exe"
```

- **预设与脚本扫描结果**：`export_presets.cfg` 正确识别，全局脚本类注册（39 步）通过。
- **阻塞项（环境缺失导出模板）**：本机 `%APPDATA%\Godot\export_templates\` 目录为空，Godot CLI 返回以下缺失模板路径（退出码 `1`）：
  - `C:/Users/q1625/AppData/Roaming/Godot/export_templates/4.5.1.stable.mono/windows_debug_x86_64.exe`
  - `C:/Users/q1625/AppData/Roaming/Godot/export_templates/4.5.1.stable.mono/windows_release_x86_64.exe`

### 3.2 安装模板并完成导出（可重复步骤）

1. 下载并安装对应 **`Godot Engine v4.5.1.stable.mono.official.f62fdbde1`** 的 Mono 导出模板（`.tpz`）：
   - 方式 A：打开 `D:\Godot\Godot_v4.5.1-stable_mono_win64.exe` → **编辑器** → **管理导出模板** → 安装 `4.5.1.stable.mono` 模板；
   - 方式 B：将官方 `Godot_v4.5.1-stable_mono_export_templates.tpz` 中的 `templates/*` 解压至 `C:\Users\q1625\AppData\Roaming\Godot\export_templates\4.5.1.stable.mono\`，确认存在 `windows_debug_x86_64.exe` 与 `windows_release_x86_64.exe`。
2. 在仓库根目录执行导出：

```powershell
New-Item -ItemType Directory -Force build\windows | Out-Null
& 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path . --export-release "Windows Desktop" "build/windows/ACT_Spike.exe"
```

3. 将生成的 `build\windows\` 整个目录复制到脱离开发目录的干净文件夹（例如 `C:\Temp\ACT_Spike_Release\`），双击 `ACT_Spike.exe` 完成下方第 5 节手工试玩验证。

## 4. 文档对齐与旧资产清理状态

- **已更新**：`README.md`、`Docs/DEMO_EXECUTION_PLAN.md`、`Docs/PROJECT_BUILD_PLAN.md` 已全部对齐至 ADR-0001 纯 2D 程序化几何与 13 路 PCM 音效基线，清除 `320×180`、像素编辑器与生成式管线旧指引。
- **待人工/合并端清理（受限会话权限超时未执行删除）**：以下作废旧文档仍留在工作树，建议合并时执行 `git rm` 与清理：
  - 已跟踪旧文档：`Docs/ART_ASSET_WORKFLOW.md`、`Docs/PIXEL_ART_PRODUCTION_PIPELINE.md`、`Docs/NEXT_PHASE_PROMPT.md`、`Docs/PHASE_PROMPTS.md`
  - 未跟踪旧提示词与工作流：`Docs/ART_CONCEPT_PROMPTS*.md`、`Docs/COMFYUI_MODEL_DEPLOYMENT.md`、`Docs/CONCEPT_ART_*.md`、`Docs/comfyui_workflows/`、`Docs/debug.log`

## 5. 手工试玩 QA 验收清单（发布前人工确认）

- [ ] **视觉可读性（关闭 `F3`）**：默认关闭 `F3` 调试覆盖层，从 `Title` 进入教学与 Boss 战，仅靠「遗迹几何表现」剪影、三种战斗前摇信号颜色/形状、门柱状态与冷色时缓视觉场即可分辨攻防时机并击败 Boss。
- [ ] **程序化音频听感**：在有声桌面环境中体验 13 路战斗与前摇音效，确认无明显爆音/削波刺耳，连击、格挡、完美闪避与时缓升降调清晰可辨。
- [ ] **手感与时序**：顿帧/微震流畅不掉帧；按住 `Q` 开启时缓期间 `0.25s` 完美格挡/完美闪避判定依然稳定；`Esc` 暂停并恢复后时缓与重力不异常。
- [ ] **双输入路径**：
  - [ ] 仅用键鼠完成标题页导航、全教学或直接挑战 Boss、`Esc` 暂停（继续/重置/返回标题）、`F3` 调试切换与 `R` 重置。
  - [ ] 接入 Xbox 手柄完成菜单导航（左摇杆/十字键 + `A`/`B`）、战斗全部动作（`LS` / `A` / `X` / `B` / `RB` / `LB` / `Y`）及 `Start` 暂停。
- [ ] **Windows 独立导出包运行** *(待安装 `4.5.1.stable.mono` 导出模板后勾选)*：将 `build/windows/` 复制到干净目录，双击 `ACT_Spike.exe` 正常显示中文字体与几何画面，完成至少一局胜负与 `R` 重置，无控制台报错或退出残留。
