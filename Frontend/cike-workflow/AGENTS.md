## Frontend

前端位于 `Frontend/cike-workflow`，包管理器 **pnpm**。技术栈：Vue 3（组合式 API + `<script setup>`）+ TypeScript + Vite，UI 基于 **shadcn-vue**（`new-york` 风格、`zinc` 基色、lucide 图标）+ Tailwind CSS 4（CSS 变量主题，暗色模式走 `.dark` class）。

### 结构

| 路径                      | 职责                                                 |
| ------------------------- | ---------------------------------------------------- |
| `src/components/ui/`      | shadcn-vue 生成的基础组件（由 CLI 生成，见下方约定） |
| `src/components/`         | 业务组件                                             |
| `src/components/layout/`  | 应用外壳布局组件（L1/L2 导航、顶栏、面包屑、主题切换） |
| `src/composables/`        | 组合式函数                                           |
| `src/layouts/`            | 页面布局（`AppLayout` 带侧栏 / `BlankLayout` 全屏） |
| `src/router/`             | vue-router 路由表与路由配置                          |
| `src/views/`              | 页面级视图组件（与路由一一对应）                     |
| `src/lib/`                | 纯工具（`cn()` 等），不含 Vue 依赖                   |
| `src/style.css`           | Tailwind 入口 + 主题 CSS 变量（`:root` / `.dark`）   |

路径别名 `@/` → `src/`。shadcn-vue 配置见 `components.json`。

### Commands

```bash
cd Frontend/cike-workflow

# 本地开发（Vite）
pnpm dev

# 类型检查 + 构建
pnpm build

# 添加 shadcn-vue 基础组件（不要手写 src/components/ui/ 下的文件）
pnpm dlx shadcn-vue@latest add <component>
```

### 约定

- **基础组件靠 CLI 生成**：`src/components/ui/` 下的组件一律通过 `shadcn-vue add` 引入后按需微调，禁止从零手写；CLI 配置以 `components.json` 为准
- **ui/ 与业务组件分层**：业务组件放 `src/components/`，PascalCase 命名、文件名与组件名一致；对 `ui/` 组件的定制若被多处复用，封装成业务包装组件，不在 `ui/` 源码上反复打补丁
- **组件命名模式**：表单类独立为 `*Form.vue`，对话框类独立为 `*Dialog.vue` / `*Modal.vue`（确认框用 `AlertDialog`），不内联在页面或其他组件里
- **样式**：业务样式只负责布局与组合（间距、排列、尺寸），不改 `ui/` 组件的圆角 / 边框 / 阴影 / 配色等外观底座；颜色一律走主题变量（`bg-primary`、`text-muted-foreground` 等），禁止硬编码色值、禁止使用 `src/style.css` 未定义的 token；多 class 合并用 `@/lib/utils` 的 `cn()`
- **图标**：统一用 `@lucide/vue`，禁止引入 `lucide-vue-next` 或其他图标库
- **工作流设计器**：图编辑引擎统一用 **AntV X6 3.x**（`@antv/x6` + `@antv/x6-vue-shape`），Vue SFC 通过 `x6-vue-shape` 的 `register()` 注册为节点 shape；官方插件按需引入（3.x 起 `History` / `Clipboard` / `Keyboard` / `Selection` / `MiniMap` / `Snapline` / `Stencil` / `Dnd` / `Export` / `Scroller` / `Transform` 已从主包统一导出，不再需要装 `@antv/x6-plugin-*` 子包）；禁止引入 vue-flow / react-flow / bpmn-js / LogicFlow 等替代图编辑库
- **验证**：改动后跑 `pnpm build`（含 vue-tsc 类型检查）确认无类型错误
- **当前设施边界**：router 已落地（vue-router 4，路由表见 `src/router/index.ts`）；状态管理 / ESLint / 测试尚未落地，引入前先与用户确认方案

## 约定

- **提交信息**：Conventional Commits（feat / fix / test / docs / refactor）+ 中文描述，如 `feat:工作流定义保存/发布全链路`
- **语言**：与用户交流、提交信息、`.claude/rules/` 与项目文档用中文；代码标识符与注释用英文
- **变更体积**：非机械改动预计超过 ~500 行时，先提出拆分方案再动手
- **分层纪律**：业务逻辑不进 `Service.Open`——宿主只做组装与入口
- **自动格式化**：代码改完后自动格式化——后端对改动的项目跑 `dotnet format <项目>.csproj`，前端 lint 脚本落地后跑 `pnpm lint:fix`（当前未配置）——无需请求批准；但只提交自己改动文件的格式化结果，不把无关文件扫进同一提交
- **测试随行**：改动工作流执行、序列化、仓储等核心逻辑时，必须同步新增或更新测试（写法遵循 `.claude/rules/testing.md`，核心行为优先集成测试）；修 bug 先写复现测试再修
- **文件大小**：新功能优先放新文件；单文件超过 ~800 行时不再往里堆，除非有明确理由

## 禁止事项

- **不反编译**：不使用 dnSpy / ILSpy / dotPeek / ildasm 等工具反编译 DLL 或其他二进制文件。遇到不懂的框架/库行为，按顺序处理：① 读源码与官方文档 → ② 网络搜索 → ③ 向用户提问。不允许凭训练记忆猜测框架行为。
- **不手改生成文件**：EF Core 迁移（`*ModelSnapshot.cs`、`*.Designer.cs`）只能由 `dotnet ef` 命令生成；确需手改必须先征得用户同意。
- **不擅自引入依赖**：新增 NuGet 包或 pnpm 包之前必须询问用户。
- **不破坏测试**：不允许为了让测试通过而修改断言、删除或跳过测试。
- **不扩大范围**：只改任务要求的文件，不顺手重构、重命名或清理无关代码。
- **不隐藏问题**：测试失败、验证不通过、无法确认的行为必须如实报告，不允许含糊带过或声称完成。
