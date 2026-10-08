using Cike.Workflow.Common.Serialization.Converters;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;

namespace Cike.Workflow.Common.Serialization;

public static class JsonHelper
{
    static JsonHelper()
    {
        DefaultSerializerOptions = CreateOptionsInternal();
    }

    public static JsonSerializerOptions DefaultSerializerOptions;

    public static JsonSerializerOptions CreateOptionsInternal()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            // 读取端容忍旧版序列化产物中不在对象首位的 $id/$values 引用元数据；
            // 对未启用 ReferenceHandler 的 options 是空操作，对启用的则避免乱序元数据抛 JsonException。
            AllowOutOfOrderMetadataProperties = true
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(JsonMetadataServices.TimeSpanConverter);
        options.Converters.Add(new IntegerJsonConverter());
        options.Converters.Add(new BigIntegerJsonConverter());
        options.Converters.Add(new DecimalJsonConverter());
        options.Converters.Add(new ExpandoObjectConverterFactory());

        return options;
    }

    public static T Deserialize<T>(string json, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, options ?? DefaultSerializerOptions)!;
    }

    public static string Serialize<T>(T value, JsonSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(value, options ?? DefaultSerializerOptions);
    }
}
