# ADR 0012: 实例状态筛选走 workflowStatuses，避开 workflowMainStatuses

## Status

Accepted

## Context

工作流实例列表需要按状态筛选。后端 `WorkflowInstanceFilter` 同时暴露两套状态过滤维度：

- `workflowStatuses[]` —— 七态枚举精确过滤（Pending / Executing / Suspended / Finished / Cancelled / Faulted / Interrupted）。
- `workflowMainStatuses[]` —— 粗粒度二分（Running / Finished），看起来更省事。

但后端 `WorkflowStatus.GetMainStatus()`（`Backend/src/Cike.Workflow.Core/Enums/WorkflowStatus.cs`）的 switch 只映射了六个状态，**未覆盖 `Interrupted`**，遇到它走 `_ =>` 直接抛 `ArgumentOutOfRangeException`。`WorkflowInstanceFilter.Apply()` 在 `workflowMainStatuses` 非空时会对每条实例调用该方法，等于埋了一个"库中存在任一已中断实例即触发 500"的地雷。

## Decision

实例列表的状态筛选**一律映射到 `workflowStatuses[]`（七态枚举多选）**，前端**永不使用** `workflowMainStatuses[]`。UI 上以一排可多选的 toggle chips 呈现全部七个状态，用户自选关注集合。

## Consequences

- 绕开 `GetMainStatus()` 对 `Interrupted` 抛异常的潜在 500，且无需等后端修复即可安全上线。
- 主动放弃了 Running/Finished 的粗粒度一键分组便利；若将来确实需要"只看运行中/只看已结束"的快捷分组，须由前端在七个枚举值上自行展开为对应的 `workflowStatuses` 子集，**不得**下推到 `workflowMainStatuses`。
- 一旦后端补齐 `GetMainStatus()` 对 `Interrupted` 的映射，本决策可重新评估，但在补齐前保持承重。

## Alternatives Considered

- **用 `workflowMainStatuses[]` 做粗筛**：被否，直接踩后端 `Interrupted` 抛异常的地雷。
- **前端先探测库中是否存在 Interrupted 实例再决定用哪套过滤**：被否，探测本身也要遍历数据、成本与复杂度都不划算，且把后端缺陷的规避责任错误地下放到前端运行时。
