# 领域文档

工程技能探索代码库时，应按本文件读取和使用项目领域文档。

## 探索前先读取

- 仓库根目录的 **`GLOSSARY.md`**，或者
- 如果根目录存在 **`GLOSSARY-MAP.md`**，先按它找到各上下文的 `GLOSSARY.md`，再读取与当前主题有关的词汇表。
- 读取涉及当前工作区域的 **`docs/adr/`**。多上下文仓库还要检查 `src/<context>/docs/adr/` 中对应上下文的决策记录。

这些文件不存在时，直接继续工作，不要把缺失本身作为问题，也不要提前建议创建。`/domain-modeling` 技能会在术语或决策真正确定时按需创建它们。

## 文件结构

单上下文仓库（大多数仓库）如下：

```
/
├── GLOSSARY.md
├── docs/adr/
│   ├── 0001-event-sourced-orders.md
│   └── 0002-postgres-for-write-model.md
└── src/
```

多上下文仓库以根目录存在 `GLOSSARY-MAP.md` 为标志：

```
/
├── GLOSSARY-MAP.md
├── docs/adr/                          ← 系统级决策
└── src/
    ├── ordering/
    │   ├── GLOSSARY.md
    │   └── docs/adr/                  ← 上下文级决策
    └── billing/
        ├── GLOSSARY.md
        └── docs/adr/
```

## 使用词汇表中的术语

当输出中出现领域概念时，例如 Issue 标题、重构提案、假设或测试名称，使用 `GLOSSARY.md` 中定义的术语。不要自行改用词汇表明确避免的同义词。

如果需要的概念还没有进入词汇表，这通常意味着项目语言仍有缺口。此时记录问题，交给 `/domain-modeling` 处理。

## 标记 ADR 冲突

如果输出与现有 ADR 冲突，要明确指出，不要默默覆盖：

> _与 ADR-0007（事件溯源订单）冲突，但由于……值得重新讨论。_
