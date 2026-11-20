# ACT Spike（遗迹行者）

基于 **Godot 4.5.1 (.NET 8 / C#)** 的 2D 动作战斗垂直切片，遵循 [ADR-0001](Docs/adr/0001-procedural-geometric-art.md) 采用纯 2D 程序化几何美术（**遗迹几何表现**硬边剪影切面，原生 `1280×720` `canvas_items` / `keep`）与启动时内存 16-bit PCM 音效合成，零外部贴图与音频资产。

## 玩法与视听概览

- **完整流程（13 个场景）**：
  - `Scenes/Title.tscn` 标题页 → 8 关教学（移动 / 攻击 / 技能 / 格挡 / 闪避 / 完美防御 / 时缓 / 实战）→ `FormalRoute` → `FormalEncounter` → `CoreLoop` Boss 战 → `Victory` 结算页。
  - 也可在标题页选择「直接挑战 Boss」直达 `CoreLoop`。
- **三类 Boss 攻击前摇信号与应对**：
  - **普通斩击（金色扇形扫掠）**：按住格挡或闪避；在命中前 `0.25s` 内起手格挡可触发**完美格挡**并震慑 Boss。
  - **破防抓取（绯红矩形区 + 收缩 X）**：不可格挡，需使用**闪避**或 **`K` 方向位移技能**避开；完美闪避可返还 `+20` 时缓能量。
  - **延迟范围（紫罗兰收缩倒计时圆环）**：长前摇范围爆发，建议提前移动脱离威胁区或开启**时缓**争取反应窗口。
- **程序化音画表现**：
  - 四层视差荒原高塔背景随关卡主题渐进变化，区域边界与出口以发光几何门柱表达解锁状态。
  - 内置 13 路 22050 Hz 单声道 PCM16 合成音效（斩击、位移、命中、受击、格挡/完美格挡、闪避/完美闪避、三种前摇警报、时缓启停），支持 8 声部复音与防爆音包络。

## 操作说明（键鼠 / Xbox 手柄）

### 战斗与关卡控制

| 功能 | 键盘 / 鼠标 | Xbox 手柄 | 说明 |
|---|---|---|---|
| 移动 / 瞄准方向 | `W` `A` `S` `D` | 左摇杆（`LS`） | 左右移动；配合 `K` 控制位移方向 |
| 跳跃 | `Space` | `A` | 跳跃 |
| 普通攻击 | `J` / 鼠标左键 | `X` | 近战斩击 |
| 闪避 | `Left Shift` | `B` | 快速突进躲避（含 `0.25s` 完美闪避窗口） |
| 格挡 | `Left Ctrl`（按住） | `RB`（按住） | 举盾格挡（起始 `0.25s` 为完美格挡窗口） |
| 时缓视觉场 | `Q`（按住） | `LB`（按住） | 开启 `0.35x` 慢放（消耗时缓能量，松开或耗尽恢复） |
| 方向位移技能 | `K`（按住 / 配合方向） | `Y` | 穿刺位移攻击，可穿越破防抓取范围 |
| 暂停菜单 | `Esc` | `Start` | 打开暂停浮层（继续 / 重置当前关 / 返回标题） |
| 重置当前关 | `R` | — | 立即重置当前场景与状态 |
| 调试覆盖层 | `F3` | — | 开关左上角状态机与战斗事件调试面板（默认关闭） |

### 菜单导航（标题页 / 暂停菜单 / 胜利页）

- **选择**：`W` / `S`、方向键上下、手柄十字键/左摇杆上下，或鼠标悬停
- **确认**：`Enter`、`Space`、`J`、Xbox `A`，或鼠标左键点击
- **返回 / 继续**：`Esc` 或 Xbox `B`（胜利页按 `R` 可立即再战 Boss）

## 构建与自动化验证

环境要求：`.NET 8.0+ SDK`、`Godot_v4.5.1-stable_mono_win64_console.exe`（默认路径 `D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe`，可通过 `$env:GODOT_CONSOLE` 覆盖）。

```powershell
# 首次在干净目录/worktree 构建前还原 NuGet 依赖
dotnet restore "ACT Spike.csproj"

# 0 警告编译校验
dotnet build "ACT Spike.csproj" --no-restore -warnaserror

# 完整自动化验收门（0 警告构建 + ACT_SMOKE=1 全链路 headless 烟测）
powershell -ExecutionPolicy Bypass -File verify.ps1
```

单独运行 headless 烟测：

```powershell
$env:ACT_SMOKE = '1'
& 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path . --log-file '.godot\headless-smoke.log' --quit-after 3600
Remove-Item Env:ACT_SMOKE
```

## Windows 独立导出

仓库已包含 `export_presets.cfg`（预设 `"Windows Desktop"`，`x86_64`，输出至 `build/windows/ACT_Spike.exe`）。

1. **安装匹配的导出模板**：确保安装与引擎版本 **`4.5.1.stable.mono.official.f62fdbde1`** 完全匹配的 Godot 4.5.1 Mono 导出模板（`.tpz` 可在 Godot 编辑器内通过「编辑器 → 管理导出模板」安装，或解压至 `%APPDATA%\Godot\export_templates\4.5.1.stable.mono\`）：
   - `%APPDATA%\Godot\export_templates\4.5.1.stable.mono\windows_debug_x86_64.exe`
   - `%APPDATA%\Godot\export_templates\4.5.1.stable.mono\windows_release_x86_64.exe`
2. **执行导出**：

```powershell
New-Item -ItemType Directory -Force build\windows | Out-Null
& 'D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe' --headless --path . --export-release "Windows Desktop" "build/windows/ACT_Spike.exe"
```

3. **启动验证**：将 `build/windows/` 整个输出目录（含 `ACT_Spike.exe`、`.pck` 与随附 `.NET` 运行库目录）复制到干净目录后双击 `ACT_Spike.exe` 运行。

## 已知限制与手工验收边界

- **无 BGM 与剧情系统**：依据垂直切片范围，仅合成 13 个战斗/前摇/时缓瞬态音效，不含持续背景音乐与多关卡扩展。
- **中文系统字体回退**：运行时通过 `UiFonts` 按顺序查找 `Microsoft YaHei UI` / `Microsoft YaHei` / `Noto Sans CJK SC` / `PingFang SC` / `SimHei` / `Segoe UI`，在精简 Windows 环境中确保保留上述任一中文字体以免退化为系统默认回退字体。
- **headless 烟测静音与渲染隔离**：`ACT_SMOKE=1` / `--headless` 下音频控制器进入安全静音模式且不进行实际像素听感/视觉验收；实机听感、无 `F3` 视觉可读性、真实手柄连机与导出包双击运行须按 [`Docs/RELEASE_QA_RECORD.md`](Docs/RELEASE_QA_RECORD.md) 手工确认。
