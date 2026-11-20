# Coding Standards

本文件仅记录评审期由代码审查执行的判断题规则；编译警告与全阶段烟测由 `verify.ps1` 强制校验。

## 战斗与时序契约

- 战斗判定窗口、冷却与前摇倒计时基于 `Time.GetTicksMsec()` 真实时间戳计算，与帧率和 `Engine.TimeScale` 解耦。
- `Engine.TimeScale` 仅由 `TimeSlowManager` 独占控制；视觉顿帧、打击反馈与退出清理均在局部表现计时器内完成，确保退出场景后 `Engine.TimeScale` 归位为 `1.0`。

## 表现层接缝与领域语言

- 控制器对外暴露的表现事件负载使用不可变 `readonly record struct`，且仅在权威状态切换或伤害实际生效后触发。
- 跨状态派生的有效能力（如正面护盾是否处于生效态）由权威控制器暴露单一布尔属性，调用方不重复拼装多字段状态组合；不为当前票据无调用者的场景预先添加空虚钩子。
- 命名与注释沿用 `GLOSSARY.md` 定义的领域术语，并遵守 `Docs/adr/0001-procedural-geometric-art.md` 的纯 2D 程序化几何与原生 `1280×720`（`canvas_items` / `keep`）约束。

## 视觉原语与接口一致性

- 新的程序化几何视觉与正式 HUD 统一复用 `ACTSpike.ACT.Visual` 下的 `RuinGeometryPalette`、`GeometricDraw` 与 `UiFonts`，将共享语义色与回退字体集中在单一权威来源。
- 同一模块内的几何绘制原语保持一致的参数类型（优先使用 `Rect2` / `Vector2`，避免散传坐标与宽高）。
- C# 新增玩法与表现代码归入对应 `ACTSpike.ACT.*` 命名空间，保持节点与控制器职责单一。
