# If/Switch 条件用前端 builder 树（conditions + combineCondition 递归）存 customProperties，单向编译为 Javascript 表达式执行

设计器要为 If 的条件与 Switch 每个分支的条件提供可视化配置。后端表达式类型集合冻结（Literal / Javascript / Liquid / Variable / WorkflowInput），既不新增"条件"类型、也不解释任何前端结构。我们决定：**编辑真源是一棵前端专属的 builder 树，存进活动的 `customProperties`（后端不透明扩展袋，原样存还、从不解释）；每次变更单向编译为 `{ type:"Javascript", value:"<js>" }` 写入真正的条件表达式（If→`condition`，Switch→`case.value`），后端只执行编译产物。**

树的形状**直接对应交互布局**：一个条件组 = 一排扁平比较行 `conditions[]` + 至多一个内缩子组 `combineCondition`，二者由组上的 `conditionType`(and/or) 接合；递归 `combineCondition` 得到任意深度嵌套。每个条件（If 的 `ifCondition`、Switch 的 `caseConditions[i]`）带判别子 `type`：`custom` 走 builder 树，其余是"逃生舱"直接编辑裸表达式。

## Schema（存于 customProperties.customExpression）

```jsonc
ifCondition / caseConditions[i] = {
  type: "custom" | "Literal" | "Javascript" | "Liquid",
  value:
    | { conditionType: "and" | "or",
        conditions: [ { left: Operand, operator: Op, right: Operand } ],
        combineCondition?: <同形状递归> }   // type = custom
    | true | false                          // type = Literal
    | "<code>"                              // type = Javascript / Liquid
}
Operand = { type: "Literal" | "Variable" | "Input" | "WorkflowInput" | "Javascript"(仅存量), value: any, dataType?: "string"|"number"|"boolean"|"datetime" }
```

**所有表达式槽位一律复用 ExpressionEditor，靠 `allowedTypes` 白名单收敛**（不另造控件）：操作数槽白名单 = `Literal / Variable / Input`；整条件逃生舱白名单 = `Literal / Javascript / Liquid`。`Javascript` 操作数仅为存量数据保留（编译原样内联、ExpressionEditor 对当前类型保持可见），新编辑选不到；`Liquid` 永不可作操作数。`WorkflowInput` 是 `Input` 的旧别名，同路编译。

Switch 的 `caseConditions[i].label` 投影为出端口；`mode`（首个优先/匹配所有）仍留在 Switch 活动上，不进本容器。If 用 `ifCondition`、Switch 用 `caseConditions[]`，两字段分开、不合并。

## 编译契约（前端纯函数）

- **操作数**：`Literal` 按 `dataType` 出 JS 字面量（string 加引号转义、number 原样、boolean `true/false`、datetime→`new Date('ISO').getTime()`）；`Variable`→`getVariable('名字')`、`Input`/`WorkflowInput`→`getInput('名字')`（后端 Jint 注册的两个访问器）；存量 `Javascript` 原样内联（空脚本降级为 `undefined`，保证产物语法合法）。
- **运算符**：`=→===`、`!=→!==`、`> >= < <=` 原样；字符串另有 `contains/notContains/startsWith/endsWith`（编译为 `String(l).includes(r)` 等）；一元 `empty/notEmpty` 对 string/number/datetime 可用。
- **组**：`conditions[]` 用 `conditionType` 的 `&&`/`||` 连接，再与 `"(" + compile(combineCondition) + ")"` 用同一操作符接合；嵌套子组整体加括号保证优先级；某侧为空退化为恒等（空组 = `true`）。
- **运算符/控件按 dataType 分集**：number、datetime = `= != > >= < <= empty notEmpty`；string 另加 `contains notContains startsWith endsWith`；boolean = `= !=`。Literal 控件按 dataType 渲染（文本/数字/开关/日期时间）。dataType 仅挂在 Literal 操作数上；比较的有效 dataType 取“引用操作数（Variable/Input 按名字解析到工作流变量/输入定义的 CLR 类型名，左优先于右）→ 右侧字面量 → 左侧字面量 → string”的回退链。引用类型可解析时它是权威：显示层把该行 Literal 操作数的 dataType 选择器锁定为引用类型，编译层经 `normalizeGroup` 派生钉型（value 按目标类型强制），存库 builder 树不被改写；builder 树是非响应式普通对象且名字编辑由 ExpressionEditor 自有命令写入，组编辑器订阅 designer revision 重新派生运算符集与引用类型。

## 布局契约（组内，递归同形）

每个条件组渲染为左右两栏加底部动作行：**左 rail 栏** = 居中的且/或接合钮（单字 + 上下箭头图标，点击在 and/or 间切换，非下拉），上下各接一段竖线（rail）；**右内容栏** = 一排比较行与至多一个内缩虚线子组，缩进在 rail 右侧；**底部动作行** = `[+ 添加规则] [+ 组合条件]`，左边缘与且/或钮左对齐（同一 x），不放在顶部。接合钮为弱虚边框 ghost 样式，每一层深度一致；子组（combineCondition）递归套用同一形状。该契约是 `conditions + combineCondition` 交互布局的直接序列化（参照 Octopus 指标告警条件配置），不随组内容增减而改变接合钮/动作行位置。

## Considered Options

- 后端新增原生 Condition 表达式类型直接存树：被否。用户明确"后端不处理前端的东西"，且突破后端冻结约束。
- 通用 n-ary 条件 AST（children 混排叶子与子组）：被否。表达力虽全，但模型与界面之间隔一层翻译；本产品的交互就是"扁平行 + 一个内缩子组 + 接合操作符"，`conditions + combineCondition` 正是该交互的直接序列化，渲染/编辑零翻译，且已被生产系统（Octopus 指标告警条件配置）验证。
- 受控 activeType + 漂移检测决定显示 builder 还是代码：被否。判别子 `type` 已存于数据，一次字段读取即可，无需状态机。
- builder 操作数支持 Liquid：被否。编译产物是 Javascript、由 Jint 执行，Jint 内无 Liquid 渲染器，Liquid 操作数无法内联；Liquid 仅保留为整条件逃生舱。
- 操作数与逃生舱自制编辑控件（字面量/脚本二选、裸 Monaco）：被否。与设计器统一表达式编辑体验割裂（图标切换器、变量/输入名字下拉、切类型 value 契约全部要重造）；ExpressionEditor 已具备 `allowedTypes` 白名单能力，复用即得一致体验与 ADR 0004/0005 语义。
- 编译产物随每条编辑命令一起进撤销栈（复合命令）：被否（改为派生同步）。编译是 spec 的纯函数，没有独立撤销意义；进栈会使每次编辑/撤销变成两步。改为表单监听 revision、容器存在时把编译产物直接同步进真条件字段（不进栈）；容器不存在（老数据未编辑）时不同步，避免空 builder 的 `true` 覆盖存量条件。

## Consequences

- `customProperties` 承载编辑真源并随定义进版本；老数据无 `customExpression` 时从现有 condition 反推种子（Javascript→Javascript、Liquid→Liquid、Literal 布尔→Literal）。
- 显示哪种编辑器由 `type` 一次读取决定：`custom`→builder；其余→把 spec 本身当 expression 交给 ExpressionEditor（allowedTypes 限 Literal/Javascript/Liquid 逃生舱三型，Literal 插槽渲染真/假开关）。**顶层判别子切换器是一个 ExpressionEditor 同款仅图标按钮，置于条件区行尾（右侧）**，弹层列 可视化/字面量/JavaScript/Liquid 四型（custom 用树形图标，其余复用 ExpressionEditor 解析的 type/code/droplet 图标）；逃生舱模式下以 `hideTypeSwitcher` 隐藏 ExpressionEditor 自带切换器，保证顶层类型只由这一个切换器拥有（默认 custom）。
- 编译单向且**派生**：操作数/逃生舱编辑由 ExpressionEditor 自有命令写入容器；表单在 revision 变化时把 `compileCondition(spec)` 同步进真条件字段（If.condition / Switch.cases[i].value），不反向解析 JS 回树。
- 新增纯逻辑缝 `core/designer/conditionCompile`（树→JS），vitest 覆盖；ConditionEditor UI 另计。
