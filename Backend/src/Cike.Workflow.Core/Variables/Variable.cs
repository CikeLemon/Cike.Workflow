namespace Cike.Workflow.Core.Variables;

/// <summary>
/// Represents a variable that references a memory block.
/// </summary>
public class Variable : MemoryBlockReference
{
    private static JsonSerializerOptions? _serializerOptions;

    private static JsonSerializerOptions SerializerOptions =>
        _serializerOptions ??= new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.Preserve,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            // 读取端容忍旧版序列化产物中不在对象首位的 $id/$values 引用元数据（启用了 Preserve，元数据读取生效）。
            AllowOutOfOrderMetadataProperties = true
        }.WithConverters(
            new JsonStringEnumConverter(),
            new ExpandoObjectConverterFactory());

    /// <inheritdoc />
    public Variable()
    {
    }

    /// <inheritdoc />
    public Variable(string name)
    {
        Id = GetIdFromName(name);
        Name = name;
    }

    /// <inheritdoc />
    public Variable(string name, object? value = null) : this(name)
    {
        Value = value;
    }

    public Variable(string name, object? value = null, string? id = null) : this(name, value)
    {
        if (id != null)
        {
            Id = id;
        }
    }

    /// <summary>
    /// The name of the variable.
    /// </summary>
    public string Name { get; set; } = null!;

    public object? Value { get; set; }

    public string? StorageDriverType { get; set; }

    public Type GetVariableType()
    {
        var variableType = GetType();
        return variableType.GenericTypeArguments.Any() ? variableType.GetGenericArguments().First() : typeof(object);
    }

    /// <inheritdoc />
    public override MemoryBlock Declare() => new(Value, new VariableBlockMetadata(this, StorageDriverType, false));

    private string GetIdFromName(string? name) => $"{name?.Camelize() ?? "Unnamed"}{nameof(Variable)}";

    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
    public object? ParseValue(object? value)
    {
        var genericType = GetType();
        return ParseValue(genericType, value);
    }

    [RequiresUnreferencedCode("Calls System.Text.Json.JsonSerializer.Serialize<TValue>(TValue, JsonSerializerOptions)")]
    public bool TryParseValue(object? value, out object? parsedValue)
    {
        try
        {
            parsedValue = this.ParseValue(value);
            return true;
        }
        catch
        {
            parsedValue = null;
            return false;
        }
    }

    public void Set(ActivityExecutionContext context, object? value)
    {
        var parsedValue = ParseValue(value);

        var matchingContext =
            context.ExpressionExecutionContext.FindContextContainingBlock(Id)
            ?? context.FindParentWithVariableContainer()?.ExpressionExecutionContext
            ?? context.WorkflowExecutionContext.ExpressionExecutionContext!;

        ((MemoryBlockReference)this).Set(matchingContext, value);
    }

    public static object? ParseValue(Type type, object? value)
    {
        var genericType = type.GenericTypeArguments.FirstOrDefault();
        var converterOptions = new ObjectConverterOptions(SerializerOptions);
        return genericType == null ? value : value?.ConvertTo(genericType, converterOptions);
    }
}

/// <summary>
/// Represents a variable that references a memory block.
/// </summary>
/// <typeparam name="T">The type of the variable.</typeparam>
public class Variable<T> : Variable
{
    /// <inheritdoc />
    public Variable()
    {
    }

    /// <inheritdoc />
    public Variable(string name, T value) : base(name, value)
    {
    }

    public Variable(string name, T value, string? id = null) : base(name, value, id)
    {
    }

    /// <summary>
    /// Gets the value of the variable.
    /// </summary>
    public T? Get(ActivityExecutionContext context) => Get(context.ExpressionExecutionContext).ConvertTo<T?>();

    /// <summary>
    /// Gets the value of the variable.
    /// </summary>
    public new T? Get(ExpressionExecutionContext context) => base.Get(context).ConvertTo<T?>();

    /// <summary>
    /// Sets the <see cref="Variable.StorageDriverType"/> to the specified type.
    /// </summary>
    public Variable<T> WithStorageDriver<TDriver>() where TDriver : IStorageDriver
    {
        StorageDriverType = typeof(TDriver).Name;
        return this;
    }

    public Variable<T> WithId(string id)
    {
        Id = id;
        return this;
    }

    public Variable<T> WithName(string name)
    {
        Name = name;
        return this;
    }

    public Variable<T> WithValue(T value)
    {
        Value = value;
        return this;
    }
}

/// <summary>
/// Provides metadata about the variable block.
/// </summary>
public record VariableBlockMetadata(Variable Variable, string? StorageDriverType, bool IsInitialized);
