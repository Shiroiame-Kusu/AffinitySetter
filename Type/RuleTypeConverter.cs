using System.Text.Json;
using System.Text.Json.Serialization;

namespace AffinitySetter.Type;
internal class RuleTypeConverter : JsonConverter<RuleType>
{
    public override RuleType Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "name" => RuleType.ProcessName,
            "path" => RuleType.ExecutablePath,
            "command" => RuleType.CommandLine,
            _ => throw new JsonException($"Unknown RuleType: {value}")
        };
    }

    public override void Write(Utf8JsonWriter writer, RuleType value, JsonSerializerOptions options)
    {
        var str = value switch
        {
            RuleType.ProcessName => "name",
            RuleType.ExecutablePath => "path",
            RuleType.CommandLine => "command",
            _ => throw new JsonException($"Unknown RuleType: {value}")
        };
        writer.WriteStringValue(str);
    }
}