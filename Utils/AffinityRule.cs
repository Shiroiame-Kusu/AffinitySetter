using System.Text.Json.Serialization;
namespace AffinitySetter.Utils;

internal sealed class AffinityRule
{
    [JsonPropertyName("type")]
    public string Type { get; set; }
    
    [JsonPropertyName("pattern")]
    public string Pattern { get; set; } = "";
    
    [JsonPropertyName("cpus")]
    public int[] Cpus { get; set; } = Array.Empty<int>();
    
    [JsonIgnore]
    public byte[] Mask { get; private set; } = Array.Empty<byte>();

    public void Initialize()
    {
        Mask = CpuUtils.BuildCpuMask(Cpus);
    }

    public bool Apply(int tid)
    {
        if (Mask.Length == 0) return false;
        int result = CpuUtils.sched_setaffinity(tid, (IntPtr)Mask.Length, Mask);
        return result == 0;
    }
}