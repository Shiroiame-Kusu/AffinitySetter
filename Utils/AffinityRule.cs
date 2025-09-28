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
    // Accepts CPU list or range string, e.g. "0,1,2" or "0-2" or "0,2,4-6" or array [0,1,2]
    [JsonPropertyName("cpus")] public object CpusRaw { get; set; } = "";
    [JsonIgnore] public int[] Cpus { get; set; } = Array.Empty<int>();
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
        Cpus = ParseCpuList(CpusRaw);
        Mask = CpuUtils.BuildCpuMask(Cpus);
        // 检查是否为正则表达式（以/开头和结尾）
        IsRegex = Pattern.StartsWith("/") && Pattern.EndsWith("/");
    }

    // Accepts string ("0,1,2", "0-2", "0,2,4-6") or int[] ([0,1,2])
    private static int[] ParseCpuList(object input)
    {
        if (input == null) return Array.Empty<int>();
        if (input is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<int>();
            var cpus = new List<int>();
            foreach (var part in s.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    var range = trimmed.Split('-');
                    if (range.Length == 2 && int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end) && start <= end)
                    {
                        for (int i = start; i <= end; i++) cpus.Add(i);
                    }
                }
                else if (int.TryParse(trimmed, out int cpu))
                {
                    cpus.Add(cpu);
                }
            }
            return cpus.Distinct().OrderBy(x => x).ToArray();
        }
        else if (input is System.Text.Json.JsonElement elem && elem.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var cpus = new List<int>();
            foreach (var item in elem.EnumerateArray())
            {
                if (item.TryGetInt32(out int cpu)) cpus.Add(cpu);
            }
            return cpus.Distinct().OrderBy(x => x).ToArray();
        }
        return Array.Empty<int>();
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