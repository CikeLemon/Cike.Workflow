## Problem Statement

工作流实例详情页当前只有一次性 HTTP 快照 + 只读画布状态徽标，缺乏实时进度动画、容器下钻、节点执行详情面板；设计器没有调试入口，用户无法从编辑态直接发起试跑并观察执行过程。

## Solution

1. **设计器新增调试按钮**：点击后自动保存 → 弹出输入弹窗（收集工作流输入）→ 调用 DebugRun → 在新标签页打开实例详情。
2. **实例详情页升级为实时执行视图**：接入应用级 SignalR 连接，节点状态逐个亮起；支持容器下钻；右侧工具区展示实例概览 / 节点执行详情。

## User Stories

1. As a workflow developer, I want a debug button in the designer toolbar, so that I can quickly test-run my workflow without leaving the editor.
2. As a workflow developer, I want the debug button to auto-save my unsaved changes before running, so that what I see on canvas is what actually executes.
3. As a workflow developer, I want an input dialog to appear before debug launch (even if the workflow has no inputs), so that I have an explicit confirmation point and can provide test inputs.
4. As a workflow developer, I want Literal-type default expressions pre-filled in the input dialog, so that I don't have to retype known defaults.
5. As a workflow developer, I want non-Literal default expressions shown as placeholder hints (e.g. "默认: JS 表达式 `args.length`"), so that I know what the backend will evaluate if I leave the field empty.
6. As a workflow developer, I want the debug button disabled with a tooltip on published versions, so that I understand I must save a draft first.
7. As a workflow developer, I want the instance detail to open in a new tab after debug launch, so that my designer stays intact for the next iteration.
8. As a workflow developer, I want to see nodes light up in real-time (Running → Completed/Faulted) on the instance detail canvas via SignalR, so that I can watch execution progress without refreshing.
9. As a workflow developer, I want Running nodes to show a pulse animation, so that I can instantly identify which activity is currently executing.
10. As a workflow developer, I want Faulted nodes to show a red border, so that errors are visually prominent.
11. As a workflow developer, I want to drill down into container activities (Flowchart/If/Switch/ForEach/For/While) on the instance detail page, so that I can inspect nested execution status.
12. As a workflow developer, I want real-time status updates to silently apply to nodes inside containers I'm not currently viewing, so that when I drill in the state is already up-to-date.
13. As a workflow developer, I want to click a node on the instance canvas and see its execution detail (activityState, outputs) in a right dock panel, so that I can inspect what data flowed through each step.
14. As a workflow developer, I want the right panel to show an instance overview (status, workflow input/output, metadata) when no node is selected, so that I have instance-level context at a glance.
15. As a workflow developer, I want the right panel to support Pin/collapse/expand behavior consistent with the designer, so that the UX is familiar.
16. As a workflow developer, I want an execution logs section in the node detail panel (showing "暂无日志" until the backend implements it), so that the UI is ready when logs become available.
17. As a workflow developer, I want the instance header status badge to update in real-time when the workflow reaches a terminal state, so that I don't need to refresh to see completion/failure.
18. As a workflow developer, I want the SignalR connection to be an app-level singleton (Watch on enter, Unwatch on leave), so that resource usage is efficient regardless of how many instance pages I open.
19. As a workflow developer, I want loop-body nodes with multiple execution records to show the latest record, so that I see the most recent iteration's state without manual switching.
20. As a workflow developer, I want the input dialog to render type-appropriate controls (String→text, Number→number, Boolean→switch, DateTime→picker, Object/Array→JSON textarea), so that I can fill inputs efficiently.

## Implementation Decisions

### 设计器调试入口

- 调试按钮位于设计器工具栏，仅对草稿版本（`isPublished = false`）启用；已发布版本禁用 + tooltip。
- 点击流程：自动保存（复用现有 save 逻辑）→ 保存失败中断报错 → 保存成功弹出调试输入弹窗 → 用户确认 → 调用 `POST /api/v1/WorkflowInstances/DebugRun/{definitionVersionId}` → 返回 instanceId → `window.open` 新标签页打开实例详情路由。
- 调试输入弹窗始终弹出，无论工作流是否声明 Input。无 Input 时显示"该工作流无输入参数，将直接启动调试"+ 确认按钮。
- 控件映射：String→文本框, Number/Integer→数字框, Boolean→Switch, DateTime→日期时间选择器, Object→JSON textarea, isArray=true→JSON textarea。
- 默认值：Literal 表达式预填字面值；非 Literal 留空 + placeholder 展示表达式类型与摘要。不填的 key 不传入 Input 字典。
- 该端点尚未进 swagger.json，需先重新拉取 swagger 并再生成前端 API 客户端。

### 实例详情页改造

- 画布从 `interactive=false` 升级为"只读可选中可下钻"模式：节点可点击选中，容器可双击/按钮下钻，面包屑导航回上层。不可拖拽、不可连线、不可删除。
- 右侧工具区复用设计器 Dock Panel 机制（Pin/折叠/展开），承载两个互斥面板：
  - **实例概览**（未选中）：实例状态徽标、workflowState.input JSON 树、workflowState.output JSON 树、关联 ID、创建时间、定义版本链接。
  - **节点执行详情**（选中）：活动名称 + 类型 + 状态徽标、activityState JSON 树、outputs JSON 树、执行日志区域（调 Logs 端点，当前空状态"暂无日志"）。
- 不保留左侧活动面板、不保留底部问题清单。
- 顶部工具栏精简：返回按钮 + 实例名 + 状态徽标 + 定义版本链接 + 版本号 + 关联 ID。

### SignalR 实时进度

- 应用级单例 SignalR 连接（Hub 路径 `/realtime/workflow`），在 App 层或 router guard 中建立。
- 进入实例详情页 → `Watch(instanceId)` 加入分组；离开 → `Unwatch(instanceId)` 退出分组。
- 进度事件处理：`ActivityStarted` → statusMap[nodeId] = Running；`ActivityCompleted` → Completed；`ActivitySuspended` → Suspended；`ActivityFaulted` → Faulted；实例终态事件 → 更新 header 状态徽标。
- 事件载荷的 `ActivityNodeId` 直接映射画布节点（与现有 `buildActivityStatusMap` 的 activityId 匹配逻辑对齐，需确认是用 nodeId 还是 activityId）。
- 容器内节点事件静默更新数据模型，不向容器节点冒泡。
- 视觉：Running = 脉冲动画边框, Completed = 绿色徽标, Faulted = 红色边框 + 红色徽标, Suspended = 黄色徽标。

### 多次执行记录

- 循环体内同一 activityId 有多条记录时，画布状态徽标和详情面板均取 `createdAt` 最新的一条。

### 架构约束

- `useWorkflowDesigner` composable 扩展 `debugRun(input)` 方法。
- `execution.ts` 纯函数模块扩展 `getLatestRecord()` 和 `applyProgressEvent()`。
- 新建 `useInstanceExecution` composable 管理 SignalR 生命周期 + 实时状态。
- 输入弹窗、面板布局等组件局部 UI 不入自动化测试，走人工浏览器验证。

## Testing Decisions

- **好的测试**：只断言外部可观察行为（暴露的 ref 值、API 调用参数），不断言内部实现细节。
- **测试模块**：
  - `useWorkflowDesigner.test.ts`（扩展）：mock DebugRun API，断言 debugRun() 先调 save 再调 DebugRun、返回 instanceId、保存失败时不调 DebugRun。
  - `execution.test.ts`（扩展）：纯函数测试 getLatestRecord（多条取最新）、applyProgressEvent（各事件类型正确更新 statusMap）。
  - `useInstanceExecution.test.ts`（新建）：mock SignalR 连接对象，模拟 Watch/Unwatch 调用、事件推送后 statusMap 和 instanceStatus ref 的变化。
- **Prior art**：`useWorkflowDesigner.test.ts`（composable 级缝，mock API client）；`execution.test.ts`（纯函数输入输出断言）。
- **不测**：输入弹窗渲染、面板切换动画、下钻交互、脉冲 CSS——组件局部状态在 vitest node 环境下不可测。

## Out of Scope

- 工具栏动作：取消运行中实例、重新调试（从实例详情页发起）。
- 容器节点冒泡提示（"内部运行中"徽标）。
- 多次执行记录切换（"#1 / #2 / #3…"下拉）。
- 连线流动动画。
- 实例列表页对调试实例的过滤/展示改造。
- 后端 Logs 端点的实现（前端只预留 UI 空壳）。
- swagger 重新拉取与 API 客户端再生成（作为前置任务单独处理）。

## Further Notes

- ADR 0011 已记录"调试前自动保存"决策及其替代方案。
- CONTEXT.md 已新增术语：调试（Debug Run）、调试输入（Debug Input）、执行进度事件（Execution Progress Event）、实例概览（Instance Overview）、节点执行详情（Activity Execution Detail）。
- SignalR 进度事件载荷携带 `ActivityNodeId`（结构路径身份），而现有 `buildActivityStatusMap` 按 `activityId`（实例身份）匹配。实现时需确认映射关系——对于非循环场景两者一一对应；循环场景下同一 activityId 多次执行共享同一 nodeId，取最新事件的 status 即可。
- 后端 `PostDebugRunAsync` 的路径参数 `id` 实际是 `definitionVersionId`（版本行 ID），不是业务 definitionId。
