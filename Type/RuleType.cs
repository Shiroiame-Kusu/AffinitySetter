using System.Text.Json.Serialization;

namespace AffinitySetter.Type;

internal enum RuleType
{
    [JsonPropertyName("name")]
    ProcessName,
    
    [JsonPropertyName("path")]
    ExecutablePath,
    
    [JsonPropertyName("command")]
    CommandLine
}