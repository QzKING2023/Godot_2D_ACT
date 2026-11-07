# 项目构建计划

可执行的十二周版本见 [`DEMO_EXECUTION_PLAN.md`](DEMO_EXECUTION_PLAN.md)。该文件把本页的里程碑细化为每周交付物、退出条件、重复检查和范围削减规则。

跨平台或低容量模型执行时，使用 [`PHASE_PROMPTS.md`](PHASE_PROMPTS.md) 中的固定前缀和对应阶段提示词。

## 已确认工具链

- Godot `4.5.1.stable.mono.official.f62fdbde1`。
- Godot.NET.Sdk `4.5.1`，目标框架 `.NET 8.0`。
- 项目路径为 `D:\Godot\ACT_Spike`。
- 正式 FTG 项目和源代码路径为 `D:\Godot\Program`。本 Spike 不修改该项目。

## C# 结构

- `Scripts/ACT/Core` 保存状态和命名契约。
- `Scripts/ACT/Input` 保存输入动作适配器。
- `Scripts/ACT/Config` 保存可导出的时序和战斗配置资源。
- 保留用户现有文件。新增玩法代码使用 ACT 命名空间，并保持场景职责小而清晰。
- 当前状态枚举包括 Idle、Move、Jump、Attack、Dodge、Block、TimeSlow 和 Hitstun。

## 输入映射

- 移动使用 WASD，计划支持左摇杆。
- 跳跃使用 Space 或 Xbox A。
- 攻击使用 J 或鼠标左键，Xbox 使用 X。
- 闪避使用 Left Shift，Xbox 使用 B。
- 格挡使用 Left Ctrl，Xbox 使用 RB。
- 时缓使用 Q，Xbox 使用 LB。
- 位移技能使用 K，Xbox 使用 Y。
- 场景切换使用满足条件后的边界触发；当前不分配交互键。
- 回合重置使用 R。

## 时序契约

战斗窗口使用真实时间戳。完美格挡窗口独立可调，默认值为 `0.25` 秒。不要把窗口固定换算成某个锁定帧率下的帧数。攻击生效、闪避、受击停顿和资源恢复分别配置。

## 核心循环与 MVP

第一条可玩循环是移动、跳跃、攻击一个敌人、闪避、按住格挡、消耗并恢复时缓资源、读取 HUD，然后重置或重新开始遭遇战。核心循环稳定前使用占位几何。

MVP 必须包含一个可控制角色、一个简单敌人、清晰的受击和格挡反馈、时缓资源条、基础碰撞以及可重复构建的 Windows 版本。范围保持在一个聚焦的纵向切片内。

## 十二周里程碑

1. 第 1–2 周完成输入、移动、摄像机、碰撞和占位 HUD。
2. 第 3–4 周完成攻击、敌人反应、闪避、格挡和真实时间窗口。
3. 第 5–6 周完成时缓资源、受击反馈、重置流程和第一批 QA 固定场景。
4. 第 7–8 周完成一个可展示的遭遇战、音频占位和控制器验证。
5. 第 9–10 周替换选定占位资源、调节可读性并运行回归检查。
6. 第 11–12 周完成打包、试玩修复、文档和候选发布版本审核。

## 构建与导出状态

编辑器和 C# 项目可以初始化并编译。`dotnet build` 当前通过，0 个警告，0 个错误。Windows 导出仍需要项目中的 `export_presets.cfg` 以及用户 Godot 模板目录中的 Godot `4.5.1.stable` 匹配导出模板。安装模板属于后续打包准备，不在本文档翻译任务中执行。

## QA 门槛

- 验证键盘和控制器路径中的每个映射动作。
- 使用真实经过时间验证时序，包括独立的完美格挡值。
- 多次尝试后验证碰撞和敌人重置行为。
- 在 320×180 参考构图下验证 HUD 资源变化和反馈可读性。
- 打包前运行一次干净的 C# 构建。
- 记录构建命令、引擎版本、模板版本和未解决阻塞项。

## 边界

本 Spike 隔离在 `D:\Godot\ACT_Spike`。不要修改 `D:\Godot\Program`、UE 安装文件、FTG Demo 内容、正式简历材料或其他项目。

## 当前基线

采用 Spike 优先执行。项目不自动提交或推送 Git。

## 当前 Spike 验收结果

- 当前运行入口为 `Scenes/CoreLoop.tscn`，由 `Scripts/ACT/Arena/TestArena.cs` 构建占位战斗房间。
- 已有独立的玩家、敌人、战斗解析、时缓管理器、HUD、输入适配器和可选 headless smoke test。
- 玩家循环包含水平移动、跳跃、普通攻击、格挡、闪避、K 位移技能、时缓资源和 R 重置。
- 敌人循环包含 Idle、Chase、Telegraph、Strike、Recover、Hitstun 和 Dead 状态。
- 普通攻击、格挡、闪避、完美闪避和完美格挡已有不同结果。
- 完美格挡使用独立的真实时间戳窗口，默认值为 `0.25` 秒。
- `dotnet build "D:\Godot\ACT_Spike\ACT Spike.csproj" --no-restore` 已通过，0 个警告，0 个错误。
- 使用 `D:\Godot\Godot_v4.5.1-stable_mono_win64_console.exe --headless --path . --log-file ".godot\\headless-smoke.log" --quit-after 900` 并设置 `ACT_SMOKE=1` 后通过，退出码为 0。注意：`--quit-after` 计的是主循环迭代数而非秒数。早期记录使用 `--quit-after 10`，会在 smoke 只完成第一阶段（攻击命中）时提前退出——那个退出码 0 来自 quit-after 而非 ALL PASS，属于假阳性。SmokeTest 跑完全部四个阶段后会自行 `Quit(0/1)`，900 帧是让它自然结束的安全上限（2026-11-06 审计修正）。
- Windows 导出仍受 `export_presets.cfg` 和 Godot `4.5.1.stable` 导出模板缺失影响。

## 下一步执行方案

先做第 1 周基线验收，再推进关卡内容。第 1 周的最小交付物是：

1. 确认 `Scenes/CoreLoop.tscn` 是当前运行入口，并记录引擎和构建版本。
2. 运行 `dotnet build "ACT Spike.csproj" --no-restore`。
3. 找到 Godot 4.5.1 Mono 可执行文件，运行一次 headless 启动检查。
4. 按照 `DEMO_EXECUTION_PLAN.md` 建立玩家、敌人、时缓、重置和输入的验收清单。
5. 在 Pixelorama 中完成一次 16×16 图标描图、PNG 导出和 Godot 导入练习。
6. 将每个检查结果记录为通过、失败或阻塞，并只修复会阻止最小核心运行的问题。

第 1 周完成后，才能进入第 2 周的灰盒关卡路线。每周结束时都要有一次干净构建和一次完整试玩记录。

### 第 1 周的日执行顺序

- 第 1 天：确认入口场景、引擎版本、Git 状态和构建命令，保存一次基线日志。
- 第 2 天：运行 headless 启动和 `ACT_SMOKE=1` 检查，修复会阻止启动或核心循环运行的问题。
- 第 3 天：完成键盘、Xbox Controller 和 Hitbox 输入清单，逐项记录动作是否可读、可触发、可重置。
- 第 4 天：检查 `0.25` 秒完美窗口、时缓资源和 K 位移方向，补齐缺少的验收记录。
- 第 5 天：完成 16×16 像素编辑器练习，整理本周阻塞，并确认下一周只进入灰盒关卡工作。

第 1 周的完成标准是存在一份可复查的基线记录、构建通过、headless smoke 通过、核心输入验收完成，以及一个可以导入 Godot 的练习 PNG。
