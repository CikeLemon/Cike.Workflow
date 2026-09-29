# 后端测试标准

本标准定义 Backend 解决方案的测试约定。**约定为主、无硬门禁**：不设覆盖率阈值、CI 不阻断，靠 code review 执行。

前端暂无测试标准，待测试设施落地后另行补充。

## 1. 工具链（固定，不要引入替代品）

| 用途 | 工具 | 说明 |
|---|---|---|
| 测试框架 | NUnit 3 + NUnit3TestAdapter + NUnit.Analyzers | Cike.Framework 官方 samples 用 xUnit，**本项目以 NUnit 为准**，不要混用 |
| Mock | NSubstitute | 唯一 mock 库，不要引入 Moq 等 |
| 覆盖率 | coverlet.collector | 仅用于本地查看，不作为门禁 |

断言一律使用 `Assert.That` 约束模型（`Assert.That(x, Is.EqualTo(y))`）。禁止 `Assert.AreEqual` / `Assert.IsTrue` 等旧式断言；抛异常断言用 `Assert.Throws<T>` 或 `Assert.That(delegate, Throws.TypeOf<T>())`。

## 2. 测试项目组织

- 位置：`Backend/tests/<被测项目名>.Tests/`
- 一个 src 项目对应一个测试项目，引用被测项目即可，不要引用无关项目
- 测试文件目录**镜像被测代码命名空间**：`src/.../Schedulers/Internals/QueueBasedActivityScheduler.cs` → `tests/.../Schedulers/QueueBasedActivitySchedulerTest.cs`
- 测试类命名：`<被测类>Test`
- 公共 using 集中在项目 `_Imports.cs`（与 src 项目一致）
- 三个现有测试项目即范例：`Cike.Workflow.Core.Tests`（非 API 层）、`Cike.Workflow.EntityFrameworkCore.Tests`（EF Core 仓储）、`Cike.Workflow.Service.Open.Tests`（API 层）

## 3. 分层规则

按被测代码所在层划分，规则就两条：

### API 层（Service.Open）——必须走 HttpClient 测试

- 端点一律通过 `HttpClient` 发真实 HTTP 请求验证，**不直接调用端点类方法**
- 基建参考 `Cike.Workflow.Service.Open.Tests`：`WebApplicationFactory<Program>` + `TestAuthHandler`（自动通过认证）+ `CreateClient()`
- 测 HTTP 关心的东西：路由、参数校验、序列化（long→string）、认证、异常→状态码转换；业务正确性放在非 API 层的测试里

### 非 API 层（Domain / Core / Expressions / Common / Caching / Application / EntityFrameworkCore 等）——单元测试 + 性能测试

- **单元测试**为主：纯逻辑直接 `new` 被测类，依赖用 NSubstitute 构造；需要验证模块组装与服务协作（如工作流引擎全链路执行）时，用模块级 `BaseIntegrationTest`（`AddApplicationAsync<TModule>` 直连 DI 容器，不经过 HTTP），参考 `Cike.Workflow.Core.Tests`
- **性能测试**针对关键路径（引擎调度、序列化等）：标注 `[Category("Performance")]`，阈值超标用 `Assert.Warn` 告警、不做硬失败

## 4. 命名规范

测试方法采用**大驼峰三段式**：

```
<被测方法>_<场景>_<预期结果>
```

```csharp
[Test]
public void Take_WhenEmpty_ThrowsInvalidOperationException()

[Test]
public void Schedule_AfterCommit_PersistsWorkItem()

[Test]
public async Task CreateAsync_WithNameEmpty_ReturnsBadRequest()
```

- 第一段照抄被测方法名（含 `Async` 后缀）
- 第二段描述前置条件或输入（`WithXxx` / `WhenXxx` / `AfterXxx`）
- 第三段描述可观察结果（返回值 `ReturnsXxx`、异常 `ThrowsXxx`、状态变化 `PersistsXxx` / `SetsXxx`）
- 异步测试返回 `Task`，禁止 `async void`
- 场景段无法自然拆分时允许省略为两段（`Method_Expected`），但不要硬凑空场景

## 5. 编写规则

**Do：**

- 测行为不测实现——断言公共 API 的可观察结果，重构内部实现不应导致测试修改
- AAA 排列（Arrange / Act / Assert 之间空行分组），一个测试只关注一个行为焦点
- 测试数据构造器命名为 `CreateXxx` / `MakeXxx`，放测试类内 `static` 方法，多处复用时提取到测试项目 `Helpers/`
- 覆盖边界：空集合、`null`、单元素、并发场景
- 修 bug 先写复现测试（红灯）再修（绿灯），作为回归保护

**Don't：**

- 不要测纯声明代码：DTO、枚举、模块声明类、`Program`
- 不要测私有方法——通过公共 API 覆盖；测不到说明封装有问题
- 不要 mock 被测类型本身
- 不要写依赖执行顺序的测试，每个测试必须可独立运行、可独立重复
- 不要 `Thread.Sleep` / 硬编码等待；异步用 `await` 链，时序断言用轮询 helper
- 不要在测试间共享可变状态（`static` 可变字段）

## 6. 分类与运行

```bash
# 全量
dotnet test Backend/Cike.Workflow.sln

# 仅单元测试（快速反馈）
dotnet test Backend/Cike.Workflow.sln --filter "Category!=Integration"

# 指定项目
dotnet test Backend/tests/Cike.Workflow.Core.Tests
```

- 集成测试（两种 `BaseIntegrationTest` 子类）标注 `[Category("Integration")]`
- 性能测试标注 `[Category("Performance")]`，阈值超标用 `Assert.Warn` 告警、**不做硬失败**（CI 环境性能抖动大）
- 单元测试不标 Category（即默认集）

## 7. 数据库策略（已定：SQLite in-memory）

EF Core 相关测试使用 **SQLite in-memory**，基座见 `Cike.Workflow.EntityFrameworkCore.Tests/Infrastructure`（`RepositoryTestBase` 直接继承使用）：

- **组装方式与生产一致**：模块加载（`AddApplicationAsync` + `AddCikeDbContext`）不走捷径，覆盖同实体默认仓储的约定注册照常生效
- **方言覆盖**：模块加载完成后经 `Configure<CikeDbContextOptions>` 注入 SQLite 连接串（`mode=memory&cache=shared` 命名内存库）覆盖 MySQL；主连接全程保活使库不丢失，各 DbContext 经连接串自开连接、跨 Scope 可见已提交数据——引擎事件走后台 Channel 并发执行后，请求管道与后台 Scope 并发访问数据库，共享单连接对象会命令冲突，禁止回退。影子属性的 `json` 列类型在 SQLite 下按 TEXT 类型亲和性建表，生产 MySQL 映射不受影响
- **不启用环境事务**（`UnitOfWorkOptions.Enable = false`）：生产由请求管道的事务中间件统一提交，测试没有该中间件，不关闭的话仓储写入停留在未提交事务里、Scope 销毁即回滚
- **外部依赖替身**：`ICacheService<>` 以内存实现接管（不依赖 Redis），`ICurrentUser` 以 FakeCurrentUser 接管
- **隔离模型**：每个测试类独享一个宿主（独立 in-memory 库），类内测试共享、用唯一数据标记隔离
- 数据库类测试标注 `[Category("Integration")]`

## 8. 存量迁移

现有测试**不要求立即改造**（命名混用中文段、缺 Category 标记等）。规则：

- 新写、重写的测试一律按本标准
- 顺手改：接触旧测试文件时，将其命名与断言风格改造为标准格式
