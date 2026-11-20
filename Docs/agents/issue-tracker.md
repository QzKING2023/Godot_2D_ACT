# Issue 跟踪器：GitHub

本仓库的问题和规格说明都存放在 GitHub Issues 中。所有操作使用 `gh` CLI。

## 约定

- **创建 Issue**：`gh issue create --title "..." --body "..."`。多行正文使用 heredoc。
- **读取 Issue**：`gh issue view <number> --json number,title,body,labels,comments`。
- **列出 Issue**：`gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，按需要添加 `--label` 和 `--state` 过滤。
- **建立父子 Issue**：`gh issue create --parent <parent> ...`，或随后执行 `gh issue edit <parent> --add-sub-issue <child>`（`gh` 2.94 及以上）。旧版 `gh` 使用 `gh api --method POST repos/<owner>/<repo>/issues/<parent>/sub_issues -F sub_issue_id=<child-db-id>`。这里的数据库 ID 用于下面的阻塞关系。如果无法使用子 Issue，在子 Issue 正文开头写 `Part of #<parent>`。
- **评论 Issue**：`gh issue comment <number> --body "..."`。
- **添加或移除标签**：`gh issue edit <number> --add-label "..."` 或 `gh issue edit <number> --remove-label "..."`。
- **关闭 Issue**：`gh issue close <number> --comment "..."`。

在仓库克隆目录中运行时，`gh` 会根据 `git remote -v` 自动判断仓库。

## 单票完成闭环

- 单张子票（正文含 `## Parent` 或 `Part of #<parent>`）在通过 `verify.ps1` 与双轴 `/code-review` 并提交到本地分支后，立即执行 `gh issue close <number> --comment '...'` 关闭该票，评论中记录提交 SHA、审查结论与 `verify.ps1` 结果。
- 校验近期提交引用的子票是否存在漏关单：运行 `powershell -ExecutionPolicy Bypass -File verify.ps1 -CheckIssues`。
- 当前本地主开发分支为 `master`（未与远端早期 `origin/main` 合并），发起 `/code-review` 时显式指定本地基准 commit SHA，而非 `origin/main`。

### Windows PowerShell 5.1 使用提示

- 读取 Issue / PR 时始终附加 `--json ...`，确保非交互式管道下稳定输出。
- 传入含空格、反引号或引号的 `--body` / `--comment` 时，优先使用单引号字符串或通过 `--body-file -` 从标准输入传入。
- 串联多条命令使用 `;` 配合 `$LASTEXITCODE` 检查（PowerShell 5.1 不支持 `&&`）。

## Pull Request 是否作为分流入口

**PR 不作为请求入口。** 如果本仓库未来把外部 PR 当作功能请求，可以将此项改为 `yes`，`/triage` 会读取这个设置。

设置为 `yes` 后，PR 使用与 Issue 相同的标签和状态，并使用对应的 `gh pr` 命令：

- **读取 PR**：`gh pr view <number> --comments` 和 `gh pr diff <number>`。
- **列出需要分流的外部 PR**：`gh api --paginate 'repos/{owner}/{repo}/pulls?state=open' --jq '.[] | select(.author_association | IN("OWNER","MEMBER","COLLABORATOR") | not) | {number, title, author: .user.login, author_association, labels: [.labels[].name]}'`。
- **评论、添加标签或关闭**：使用 `gh pr comment`、`gh pr edit --add-label`、`gh pr edit --remove-label` 和 `gh pr close`。

GitHub 的 Issue 和 PR 共用编号空间。因此看到单独的 `#42` 时，先执行 `gh pr view 42`，失败后再执行 `gh issue view 42`。

## 技能要求“发布到 Issue 跟踪器”时

创建 GitHub Issue。

## 技能要求“读取相关任务”时

按上面的“读取 Issue”方式执行。

## Wayfinder 操作

`/wayfinder` 使用以下约定。地图是一个带有 `wayfinder:map` 标签的 Issue，正文保存目标、备注、已做决策和未决问题。地图的任务是它的子 Issue。

- **地图**：创建一个带 `wayfinder:map` 标签的 Issue，命令为 `gh issue create --label wayfinder:map`。
- **子任务**：将任务链接为地图的 GitHub 子 Issue。如果无法使用子 Issue，在正文开头写 `Part of #<map>`。任务使用 `wayfinder:<type>` 标签，其中 `<type>` 为 `research`、`prototype`、`grilling` 或 `task`。任务被认领后，分配给当前驱动开发者。
- **阻塞关系**：GitHub 原生 Issue 依赖关系是唯一的可视化来源。使用 `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>` 添加关系。`<blocker-db-id>` 是阻塞 Issue 的数据库 ID，可通过 `gh api repos/<owner>/<repo>/issues/<n> --jq .id` 获取，不是 `#number` 或 `node_id`。GitHub 通过 `issue_dependencies_summary.blocked_by` 报告仍未关闭的阻塞项。如果原生依赖不可用，在子任务正文开头写 `Blocked by: #<n>, #<n>`。所有阻塞项关闭后，任务才算解除阻塞。
- **前沿查询**：列出地图的开放子任务，排除仍有开放阻塞项或已被分配的任务，按地图中的顺序选择第一个可执行任务。
- **认领**：使用 `gh issue edit <n> --add-assignee @me`。认领必须是本次会话的第一次写入操作。
- **解决**：先使用 `gh issue comment <n> --body "<answer>"` 记录答案，再使用 `gh issue close <n>` 关闭任务，最后把简短结论和链接追加到地图的已做决策中。
