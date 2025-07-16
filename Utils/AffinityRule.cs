using System.Text.Json.Serialization;
using AffinitySetter.Type;
#pragma warning disable CS8629 // Nullable value type may be null.
namespace AffinitySetter.Utils;

internal sealed class AffinityRule
{
    [JsonPropertyName("type")]
    [JsonConverter(typeof(RuleTypeConverter))]
    public RuleType Type { get; set; }
    [JsonPropertyName("pattern")] public string Pattern { get; set; } = "";
    [JsonPropertyName("cpus")] public int[] Cpus { get; set; } = Array.Empty<int>();
    [JsonPropertyName("iopriorityclass")] public int? IoPriorityClass { get; set; } // 1: realtime, 2: best-effort, 3: idle
    [JsonPropertyName("ioprioritydata")] public int? IoPriorityData { get; set; }  // 0-7
    [JsonPropertyName("nice")] public int? Nice { get; set; }
    
    [JsonIgnore]
    public byte[] Mask { get; private set; } = Array.Empty<byte>();
    
    [JsonIgnore]
    public bool IsRegex { get; private set; }
    [JsonIgnore]
    public bool HasIoPriority => IoPriorityClass.HasValue && IoPriorityData.HasValue;

    [JsonIgnore] 
    public bool HasNicePriority => Nice.HasValue;
   
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
    public void ApplyIoPriority(int tid)
    {
        if (!HasIoPriority) return;
        int ioprio = ((IoPriorityClass.Value & 0x7) << 13) | (IoPriorityData.Value & 0x1fff);
        CpuUtils.ioprio_set(1, tid, ioprio); // 1: process/thread
    }
    
    public void ApplyNice(int tid)
    {
        if (!HasNicePriority) return;
        CpuUtils.setpriority(0, tid, Nice.Value); // 0: PRIO_PROCESS
    }
}