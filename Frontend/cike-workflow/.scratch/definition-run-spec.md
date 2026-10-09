Status: ready-for-agent
Issue: https://github.com/CikeLemon/Cike.Workflow/issues/29

# 工作流列表运行最新已发布版本

## Problem Statement

工作流列表页（定义列表）目前只能查看、编辑、移动、删除定义，无法从列表直接发起一次正式运行。用户想跑一个已经发布的工作流时，必须先进入设计器（而设计器的运行入口是"调试运行"，只对草稿最新版本开放、跑的是未发布的编辑态内容），无法针对"最新已发布版本"这一冻结快照发起正式执行。缺少一个从列表一键运行已发布工作流、填写工作流输入、并跳转到实例详情观察执行进度的入口。

## Solution

在工作流列表的定义行操作列增加"运行"入口，仅对该定义的**最新已发布版本**开放：

- 定义行的操作列新增一个"运行"图标按钮，只有当该定义存在最新已发布版本（列表已返回其**版本行 ID**）时才可点击；未发布的定义按钮禁用并给出说明。
- 点击运行后，按最新已发布版本 ID 拉取该版本的**选项（Options）**，取出其中的**工作流输入（Inputs）**声明，弹出输入采集对话框。
- 用户在对话框内按输入类型填写工作流输入（沿用调试运行的既有输入表单体验：类型化控件、Literal 默认值预填、非 Literal 以占位提示、留空字段不传由后端求值默认表达式）。
- 确认后以正式运行方式（非调试）派发该已发布版本，拿到实例 ID 后**当前页跳转**到实例详情，用户即可在实时执行视图中观察节点状态逐个亮起。

运行的是冻结的已发布快照，因此不涉及自动保存，也不受定义是否为系统/只读的影响——只要有最新已发布版本就能运行。

## User Stories

1. As a workflow operator, I want a "运行" button on each definition row in the workflow list, so that I can start a published workflow without opening the designer.
2. As a workflow operator, I want the "运行" button to be enabled only when the definition has a latest published version, so that I never accidentally try to run something that isn't published.
3. As a workflow operator, I want the "运行" button disabled with an explanatory tooltip ("未发布，无法运行") on unpublished definitions, so that I understand why I can't run it.
4. As a workflow operator, I want the run to always target the latest published version (by its version row id returned in the list), so that I execute the frozen, approved snapshot rather than any draft.
5. As a workflow operator, I want clicking "运行" to load that published version's Options and read its Inputs, so that the input form reflects exactly what the published version declares.
6. As a workflow operator, I want a loading state in the dialog while the published version's Options are being fetched, so that I know the system is working.
7. As a workflow operator, I want a clear error message (via the shared API-error extraction) if fetching the published version's Options fails, so that I know the run didn't start and why.
8. As a workflow operator, I want an input dialog to open even when the published version declares no Inputs, so that I have an explicit confirmation point before dispatch (consistent with debug run behavior).
9. As a workflow operator, I want the input dialog to render type-appropriate controls (String→text, Number/Integer→number, Boolean→switch, DateTime→datetime picker, Object/JSON/Array→JSON textarea), so that I can fill inputs efficiently.
10. As a workflow operator, I want Literal-type default expressions pre-filled in the input dialog, so that I don't have to retype known defaults.
11. As a workflow operator, I want non-Literal default expressions shown as placeholder hints, so that I know what the backend will evaluate if I leave a field empty.
12. As a workflow operator, I want empty (non-Boolean) input fields to be omitted from the dispatched input payload, so that the backend evaluates their default expressions.
13. As a workflow operator, I want confirming the dialog to dispatch the published version as a formal run (isDebug = false) with the collected input, so that the execution is a real run rather than a debug run.
14. As a workflow operator, I want the confirm button to show a "启动中…" state and be disabled while the run request is in flight, so that I can't double-submit.
15. As a workflow operator, I want a clear error message if the run request fails, so that I know the run didn't start and can retry.
16. As a workflow operator, I want the app to navigate (in the current tab) to the instance detail page of the newly created instance after a successful run, so that I can immediately watch execution progress.
17. As a workflow developer, I want the input-collection dialog to be a shared, reusable component (generalized from the debug run dialog), so that debug run and formal run present a consistent input experience without duplicated form logic.
18. As a workflow developer, I want the shared dialog's title, description, and confirm-button text to be configurable via props, so that the debug entry keeps its "启动调试" wording while the run entry uses run-specific wording.
19. As a workflow developer, I want the existing designer debug run flow to keep working unchanged after the dialog is generalized and relocated, so that this feature doesn't regress the designer.
20. As a workflow operator, I want to be able to run system and read-only definitions that have a published version, so that run capability isn't blocked by edit-level restrictions (running does not mutate the definition).
21. As a workflow operator, I want the "运行" button to sit alongside the existing edit/move/delete actions and follow the same hover-reveal and icon-button styling, so that the row's action affordances stay visually consistent.

## Implementation Decisions

### 泛化并迁移输入采集对话框（共用组件）

- 将现有位于工作流设计器目录下的调试运行输入对话框提炼为通用业务组件，迁移到业务组件根目录（与 `DefinitionFormDialog.vue`、`MoveDefinitionDialog.vue` 同级），因为它不再只服务设计器。
- 重命名为 `WorkflowRunDialog.vue`（遵循仓库 `*Dialog.vue` 命名惯例）。
- 新增可配置 props：`title`、`description`、`confirmText`；保留既有 `open`、`inputs`（`InputDefinition[]`）、`running`、`error` props 与 `update:open`、`confirm`（携带构建好的 input 载荷）事件。
- 输入表单行为**原样保留**：类型→控件映射、Literal 默认值预填、非 Literal 占位提示、空字段（Boolean 除外）不进 payload、Boolean/Number/JSON 的值强制转换逻辑均不变。
- 更新设计器宿主组件对该对话框的 import 与用法：调试入口通过 props 传入原标题/文案/按钮文字，行为与外观保持不变。

### 列表页运行入口

- 在定义列表页的定义行操作列新增"运行"图标按钮，图标取自 `@lucide/vue`（Play），沿用现有 `icon-xs` ghost 按钮与 hover 显现样式。
- **启用条件**：列表返回的定义数据中最新已发布版本的**版本行 ID**（`publishedVersionId`）非空；否则禁用并给出 tooltip「未发布，无法运行」。`isSystem` / `isReadonly` **不影响**运行可用性。
- 点击运行：以该版本行 ID 调用"按 ID 取定义版本详情"端点，从返回的 `options.inputs` 得到 `InputDefinition[]`，再打开运行对话框；拉取期间对话框内呈现 loading，失败用仓库共享的 API 错误提取工具给出中文提示。
- 确认运行：调用正式运行端点（`POST /api/v1/WorkflowInstances/Run`，请求体为 `DispatchWorkflowDefinitionRequest`），传入 `definitionVersionId = publishedVersionId`、`input = 收集的载荷`、`isDebug = false`；`correlationId`、`properties`、`parentWorkflowInstanceId`、`triggerActivityId` 等其余字段一律留空/不传。
- 成功拿到实例 ID 后，用 router 当前页跳转到实例详情路由（`instance-detail`，参数为当前 workspaceId 与返回的 instanceId）。
- 运行请求在途时禁用确认按钮并显示"启动中…"；失败在对话框内展示错误、不关闭对话框，允许重试。

### 契约与词汇对齐

- "运行"派发的是**已发布版本**这一冻结快照，与 CONTEXT.md 中"已发布版本不可修改"的语义一致；因不涉及内容修改，故无自动保存步骤（区别于设计器的调试运行）。
- 版本定位一律使用**版本行 ID**（int64），符合 CONTEXT.md 中"API 路径参数 `{id}` 指版本行 ID 而非业务 ID"的约定。
- 输入采集对话框复用"调试输入（Debug Input）"的既有交互契约（始终弹出、Literal 预填、留空由后端求值默认表达式）。

### 架构约束

- 运行编排逻辑（拉 Options → 调 Run → 跳转）作为极薄的视图层胶水，直接实现在定义列表视图组件内，不新增 composable。
- 不改动 `useWorkflowDesigner` 的调试运行链路（`debugRun`），仅让其宿主组件改用泛化后的共享对话框。

## Testing Decisions

- **好的测试**：只断言外部可观察行为（暴露的 ref 值、API 调用参数），不断言内部实现细节。
- **本规格不新增自动化测试缝**。理由：
  - 唯一有实质逻辑的可测单元是"输入 payload 构建 + 类型控件映射"，它内聚在泛化后的共享对话框组件里；该组件（原调试运行对话框）在既有先例中明确**不入自动化测试**（见 `spec-instance-debug.md`："输入弹窗、面板布局等组件局部 UI 不入自动化测试，走人工浏览器验证"）。
  - 运行编排是极薄的视图层胶水；仓库测试仅落在 composable 与 core 层，视图层无测试先例。为不引入低价值的新缝，本功能不抽 composable、不加视图测试。
- **Prior art**：`spec-instance-debug.md` 与其对应的调试运行实现——输入弹窗组件不做自动化测试、走人工浏览器验证；`useWorkflowDesigner.test.ts` 仅在存在 composable 缝时对 API 调用参数做断言（本功能无对应 composable）。
- **验证方式**：人工浏览器验证覆盖——未发布定义按钮禁用+tooltip；已发布定义点击后拉 Options、弹出输入对话框（有/无输入两种）；类型化控件与 Literal 预填正确；确认后正式运行（isDebug=false）并当前页跳转实例详情；拉取失败与运行失败的错误提示；系统/只读定义可运行。
- 改完跑 `pnpm build`（含 vue-tsc 类型检查）确认无类型错误。

## Out of Scope

- correlationId、properties、parentWorkflowInstanceId、triggerActivityId 等高级派发选项的 UI 暴露。
- 运行历史、批量运行、从列表内联查看运行状态或实例进度。
- 运行非最新版本 / 运行草稿版本（仅最新已发布版本）。
- 权限/角色对运行入口的可见性控制。
- 对输入采集对话框的表单校验规则增强（沿用调试运行现状）。

## Further Notes

- 运行端点（`POST /api/v1/WorkflowInstances/Run`）与请求体 `DispatchWorkflowDefinitionRequest` 已在生成的 API 客户端中就绪，无需重新拉取 swagger 或再生成客户端。
- 列表 DTO 已包含 `publishedVersion`（版本号，用于徽章）与 `publishedVersionId`（版本行 ID，用于运行定位）；本功能首次消费 `publishedVersionId`。
- 泛化对话框后，设计器调试运行与列表正式运行共享同一份输入表单实现，后续对输入控件的任何改动会同时影响两个入口，需在改动时一并回归。
