namespace Cike.Workflow.Core.Serialization.Internals;

/// <summary>
/// Serializes and deserializes workflow states from and to JSON.
/// </summary>
public class JsonWorkflowStateSerializer : IWorkflowStateSerializer, ISingletonDependency
{
    private readonly ISerializationTypeRegistry _workflowJsonTypeRegistry;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonWorkflowStateSerializer"/> class.
    /// </summary>
    public JsonWorkflowStateSerializer(ISerializationTypeRegistry workflowJsonTypeRegistry, ILoggerFactory loggerFactory)
    {
        _workflowJsonTypeRegistry = workflowJsonTypeRegistry;
        _loggerFactory = loggerFactory;
    }

    public string Serialize(WorkflowState workflowState)
    {
        var options = GetOptions();
        return JsonSerializer.Serialize(workflowState, options);
    }

    public byte[] SerializeToUtfBytes(WorkflowState workflowState)
    {
        var options = GetOptions();
        return JsonSerializer.SerializeToUtf8Bytes(workflowState, options);
    }

    public JsonElement SerializeToElement(WorkflowState workflowState)
    {
        var options = GetOptions();
        return JsonSerializer.SerializeToElement(workflowState, options);
    }

    public string Serialize(object workflowState)
    {
        var options = GetOptions();
        return JsonSerializer.Serialize(workflowState, workflowState.GetType(), options);
    }

    public WorkflowState Deserialize(string serializedState)
    {
        var options = GetOptions();
        return JsonSerializer.Deserialize<WorkflowState>(serializedState, options)!;
    }

    public WorkflowState Deserialize(JsonElement serializedState)
    {
        var options = GetOptions();
        return serializedState.Deserialize<WorkflowState>(options)!;
    }

    public T Deserialize<T>(string serializedState)
    {
        var options = GetOptions();
        return JsonSerializer.Deserialize<T>(serializedState, options)!;
    }

    private JsonSerializerOptions? _options;
    /// <inheritdoc />
    public JsonSerializerOptions GetOptions()
    {
        // 仅缓存转换器集合；每次调用都返回带独立 CrossScopedReferenceHandler 的副本，
        // 保证序列化（写 $id/$values 引用格式）与反序列化（读该格式）使用一致的 options。
        // 此前首次调用返回带 handler 的副本、后续返回不带 handler 的缓存，
        // 导致首次序列化产物（含 $values 格式）在后续反序列化时无法读取。
        if (_options == null)
        {
            var options = JsonHelper.CreateOptionsInternal();
            options.Converters.Add(new TypeJsonConverter(_workflowJsonTypeRegistry));
            options.Converters.Add(new PolymorphicObjectConverterFactory(_workflowJsonTypeRegistry, _loggerFactory.CreateLogger<PolymorphicObjectConverter>()));
            _options = options;
        }

        // 读取端容忍旧版序列化产物中不在对象首位的 $id/$values 引用元数据（如 {"id":...,"$id":"1",...}），
        // 否则加载此类实例时抛 JsonException 并在仓储层回退为默认状态，实例数据丢失。
        // AllowOutOfOrderMetadataProperties 由 JsonHelper.CreateOptionsInternal 提供并随副本继承。
        return new(_options) { ReferenceHandler = new CrossScopedReferenceHandler() };
    }
}
