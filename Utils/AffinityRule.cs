using System.Text.Json.Serialization;
using AffinitySetter.Type;
namespace AffinitySetter.Utils;

internal sealed class AffinityRule
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(RuleTypeConverter))]
    public RuleType Type { get; set; }
    
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";
    
    [JsonPropertyName("cpus")]
    public int[] Cpus { get; set; } = Array.Empty<int>();
    
    [JsonIgnore]
    public byte[] Mask { get; private set; } = Array.Empty<byte>();
    
    [JsonIgnore]
    public bool IsRegex { get; private set; }

    public void Initialize()
    {
        Mask = CpuUtils.BuildCpuMask(Cpus);
        // 检查是否为正则表达式（以/开头和结尾）
        IsRegex = Pattern.StartsWith("/") && Pattern.EndsWith("/");
    }

    public bool Apply(int tid)
    {
        if (Mask.Length == 0) return false;
        int result = CpuUtils.sched_setaffinity(tid, (IntPtr)Mask.Length, Mask);
        return result == 0;
    }
}