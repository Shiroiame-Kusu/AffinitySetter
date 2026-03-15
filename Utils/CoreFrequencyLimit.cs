using System.Text.Json.Serialization;

namespace AffinitySetter.Utils;

internal sealed class CoreFrequencyLimit
{
    [JsonPropertyName("cpus")]
    public object CpusRaw { get; set; } = "";

    [JsonIgnore]
    public int[] Cpus { get; private set; } = Array.Empty<int>();

    [JsonPropertyName("minfreq")]
    public long? MinFrequencyKHz { get; set; }

    [JsonPropertyName("maxfreq")]
    public long? MaxFrequencyKHz { get; set; }

    [JsonIgnore]
    public bool HasLimits => MinFrequencyKHz.HasValue || MaxFrequencyKHz.HasValue;

    public void Initialize()
    {
        Cpus = CpuSelectionParser.Parse(CpusRaw);
    }
}