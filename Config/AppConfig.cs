using System.Text.Json.Serialization;
using AffinitySetter.Utils;

namespace AffinitySetter.Config;

internal sealed class AppConfig
{
    [JsonPropertyName("rules")]
    public List<AffinityRule> Rules { get; set; } = new();

    [JsonPropertyName("frequencyLimits")]
    public List<CoreFrequencyLimit> FrequencyLimits { get; set; } = new();

    [JsonIgnore]
    public bool HasContent => Rules.Count > 0 || FrequencyLimits.Count > 0;

    public void Initialize()
    {
        foreach (var rule in Rules)
        {
            rule.Initialize();
            rule.Nice ??= 0;
        }

        foreach (var limit in FrequencyLimits)
        {
            limit.Initialize();
        }
    }
}