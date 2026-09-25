using Cike.Workflow.Expressions.LiteralExpressions;
using System.Runtime.CompilerServices;

namespace Cike.Workflow.Core.Models;

public abstract class Input : Argument
{
    public Input(MemoryBlockReference memoryBlockReference, Type type) : base(memoryBlockReference)
    {
        Type = type;
    }

    public Input(Expression? expression, MemoryBlockReference memoryBlockReference, Type type) : base(memoryBlockReference)
    {
        Expression = expression;
        Type = type;
    }

    public Expression? Expression { get; }

    [JsonIgnore]
    public Type Type { get; }
}

public class Input<T> : Input
{
    public Input(MemoryBlockReference memoryBlockReference) : base(memoryBlockReference, typeof(T))
    {
    }

    public Input(T literal, string? id = null) : this(new Literal<T>(literal, id))
    {
    }

    public Input(Variable variable) : base(new("Variable", variable), variable, typeof(T))
    {
    }

    public Input(Output output) : base(new("Output", output), output.MemoryBlockReference, typeof(T))
    {
    }

    public Input(Literal<T> literal) : base(Expression.LiteralExpression(literal.Value), literal, typeof(T))
    {
    }

    public Input(Literal literal) : base(Expression.LiteralExpression(literal.Value), literal, typeof(T))
    {
    }

    /// <summary>JSON 反序列化专用构造：Expression 为只读属性，须经构造器绑定画布里的表达式与内存块引用。</summary>
    [JsonConstructor]
    public Input(Expression expression, MemoryBlockReference memoryBlockReference) : base(expression, memoryBlockReference, typeof(T))
    {
    }

    public Input(Expression expression) : this(expression, new())
    {
    }

    public Input() : base(new(), typeof(T))
    {

    }

    public T? GetOrDefault(ActivityExecutionContext context, Func<T>? defaultValue = default)
    {
        var value = context.Get(this);
        return value != null ? value : defaultValue != null ? defaultValue.Invoke() : default;
    }

    public T Get(ActivityExecutionContext context, [CallerArgumentExpression("input")] string? inputName = default)
    {
        return context.Get(this) ?? throw new Exception($"{inputName} is required.");
    }
}
