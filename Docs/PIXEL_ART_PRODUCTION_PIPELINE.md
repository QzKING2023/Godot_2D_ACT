# 像素美术生产线

## 已确定方案

使用 GPT Image 生成概念和参考图，再在 Pixelorama 中手工描图和清理选中的姿态。LibreSprite 作为备用编辑器。运行时资产由清理后的源文件导出 PNG，生成图像不会直接复制进最终 Sprite Sheet。

这样可以把一致性和像素判断放在人工审核环节，同时利用图像生成完成创意探索、剪影探索、姿态探索和背景研究。Pixelorama 是免费且支持 Windows 的像素美术编辑器，具备逐帧动画和调色板工作流。LibreSprite 是免费开源的动画精灵编辑器。只有当时间轴或导出成为瓶颈时，才考虑引入 Aseprite。

## Demo 的视觉约束

- 逻辑构图为 320×180。
- 角色和大型道具使用 32×32 工作网格。
- 环境瓦片和小道具使用 16×16 或 32×32。
- 采用侧视剪影，只制作一个朝向，再进行水平翻转。
- 整个 Demo 使用一套有限调色板，先从 8–12 个颜色开始。只有在可读性需要时才增加有记录的强调色。
- 统一描边方式、光照方向和接地线规则。
- Godot 使用 Nearest 过滤、Lossless 压缩和尽量整数倍的缩放。
- 受击闪白、前摇圆环、拖尾和时缓色调尽量使用 Godot 绘制或粒子完成，避免制作大量手工帧。

## 资产预算

第一版可展示内容只需要以下资产：

| 类别 | 必需资产 | 处理建议 |
| --- | --- | --- |
| 玩家 | 待机、攻击、闪避、受击、死亡或胜利关键帧 | 可以延长静止帧，优先保证剪影和攻击方向 |
| Boss | 待机、前摇、三种攻击关键帧、受击、死亡 | 如果时序和效果清楚，每种攻击一个强姿势就够用 |
| 环境 | 一套模块化瓦片、少量平台、两个可复用道具、2–3 层背景 | 保持形状简单且可重复 |
| VFX | 攻击闪白、前摇、闪避拖尾、受击闪白、时缓色调 | 优先使用 Godot 程序化效果 |
| UI | 生命、格挡、时缓资源、Boss 生命、胜利和失败面板 | 使用一套像素字体或高对比度备用字体 |

这个资产预算刻意保持较小。增加帧数无法替代清晰的剪影和可读的攻击前摇。

## 单个资产工作流

### 0. 先做一次小型工具练习

在制作玩家或 Boss 前，使用 Pixelorama 描摹一个 16×16 图标，导出透明 PNG，并将它导入一个临时 Godot 场景。只学习缩放、铅笔、图层、帧、调色板和导出。练习限制在一小时内，目标是消除工具摩擦，不是制作正式资产。

### 1. 写一句资产说明

示例：`Boss 攻击 2：破防突进，朝左，突进前打开身体，只有危险窗口使用红色强调。`

说明中包含角色、视角、目标画布、动作、可读提示和调色板限制。

### 2. 使用 GPT Image 生成参考图

为角色保留一张标准参考图，并放在资产目录中。后续编辑继续使用同一张参考图，以保持服装、剪影和调色方向。当前 OpenAI 图像工具支持图像输入和编辑，Image API 支持多张参考图及标记引导的局部编辑。清理迭代使用偏重精确编辑的模型，早期构思使用速度更快的模型。每次保存提示词和选中的版本，并写入来源记录。

概念图提示词模板：

```text
Side-view 2D action game character concept, [ROLE], full body, facing left,
strong readable silhouette at 32x32 pixels, simple costume shapes, limited
8-12 color palette, one dark outline color, one accent color, flat background,
no text, no UI, no photorealism, no soft gradients, no extra characters.
This is a design reference for manual pixel tracing, not a final sprite sheet.
```

姿态图提示词模板：

```text
Use the attached character reference and preserve the same costume, silhouette,
palette direction, and left-facing side view. Show five separated pose references:
idle, attack anticipation, attack contact, dodge, and hitstun. Keep each pose
large and readable on a plain background. No text, no labels, no UI, no extra
characters. These are references for manual pixel cleanup at 32x32.
```

环境图提示词模板：

```text
Side-view modular pixel-art environment reference for a 320x180 2D action demo:
[THEME]. Show a small set of repeatable platform, wall, floor, and prop shapes.
Use a restrained 8-12 color palette, clear value separation from the player,
flat planes, no text, no characters, and no perspective distortion. This is a
composition reference for manual 16x16 and 32x32 tile drawing.
```

第一轮不要要求生成完整 Sprite Sheet。一次只生成一个角色、一个姿态图或一个环境研究图。

### 3. 在 Pixelorama 中描图

对每张选中的参考图执行以下步骤：

1. 先创建目标尺寸的画布。
2. 将参考图放在锁定的参考图层。
3. 使用 1 像素铅笔描摹剪影，去除抗锯齿边缘。
4. 应用已锁定的调色板，合并相近颜色。
5. 在 4–8 倍缩放下清理像素块和描边，再回到 320×180 原生尺寸检查。
6. 在姿态审核通过前，把线稿、填充、阴影和效果保留在不同图层。
7. 将可编辑源文件（`.pxo` 或 `.aseprite`）与导出的 PNG 帧放在一起。

目标是得到统一的剪影和可用于时序的姿态，不是逐像素复制生成图像。

### 4. 导出前审核

资产满足以下条件后才算批准：

- 在原生构图尺寸下能读懂。
- 接地线和锚点与其他帧一致。
- 只使用已锁定调色板，或存在有记录的例外。
- 没有意外抗锯齿、半透明边缘或不一致描边。
- 可以替换帧而不需要修改玩法代码。
- 源文件和来源记录齐全。

### 5. 导入 Godot

- 按目标尺寸导出透明 PNG。
- 使用 Nearest 过滤和 Lossless 压缩导入。
- 尽量使用整数倍缩放。
- 批量导出前，在运行场景中验证一个玩家帧、一个 Boss 帧、一个瓦片、一个效果和一个 HUD 元素。
- 锚点和碰撞形状放在玩法场景或数据中，不隐藏在图像编辑器文件里。

## 来源记录

在项目旁保存一个 CSV 或 Markdown 表，每个外部或生成资产占一行：

| 字段 | 必填内容 |
| --- | --- |
| 资产名称和用途 | 例如 `boss_attack_guard_break` |
| 来源 | GPT Image、Pixelorama、CC0 URL 或本地路径 |
| 提示词和参考图路径 | 生成素材使用的完整提示词和输入参考图 |
| 模型、版本和日期 | 模型名称、选中版本和生成日期 |
| 许可证和署名 | 需要的声明，或 `generated / no external source` |
| 修改内容 | 描图、调色板压缩、清理和动画编辑 |
| 运行时状态 | `approved`、`placeholder` 或 `rejected` |

生成图像是源素材，不等同于自动获得的许可证，也不代表自动达到质量要求。保留原始参考图和清理后的运行时资产，确保后续替换时不会丢失决策记录。

## 美术时间盒和备选方案

- 第一个风格试制资产限制在 90 分钟内。
- 两轮清理后仍未通过原生尺寸可读性检查时，改用更简单的剪影或程序化占位。
- 单个资产超过 3 小时时，冻结为占位资源并继续推进玩法计划。
- 第 7 周风格试制通过前，不批量生成资源。
- 美术流程落后时，先削减背景细节和动画帧，保留前摇、受击反馈和可玩循环。

## 工具建议

先使用 Pixelorama 作为主编辑器，因为它免费并支持 Windows。Pixelorama 工作流不合适时使用 LibreSprite。第一周手工清理后，如果真正的瓶颈是时间轴或导出流程，再考虑 Aseprite。
