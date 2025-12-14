namespace AffinitySetter.Utils;

/// <summary>
/// CPU 拓扑信息，用于识别 P 核心、E 核心和超线程
/// </summary>
internal sealed class CpuTopology
{
    private static CpuTopology? _instance;
    private static readonly object _lock = new();

    /// <summary>
    /// 所有在线的 CPU 编号
    /// </summary>
    public int[] AllCpus { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// P 核心（性能核心）的 CPU 编号，包括所有超线程
    /// </summary>
    public int[] PCores { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// E 核心（效率核心）的 CPU 编号
    /// </summary>
    public int[] ECores { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// P 核心的物理核心（每对超线程只取第一个）
    /// </summary>
    public int[] PCoresPhysical { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// P 核心的逻辑线程（每对超线程只取第二个，即超线程）
    /// </summary>
    public int[] PCoresLogical { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// 所有物理核心（不包含超线程的第二个逻辑核心）
    /// </summary>
    public int[] PhysicalCores { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// 所有逻辑线程（超线程的第二个逻辑核心）
    /// </summary>
    public int[] LogicalThreads { get; private set; } = Array.Empty<int>();

    /// <summary>
    /// 是否为混合架构 CPU（同时具有 P 核心和 E 核心）
    /// </summary>
    public bool IsHybridCpu => PCores.Length > 0 && ECores.Length > 0;

    /// <summary>
    /// 是否支持超线程
    /// </summary>
    public bool HasHyperThreading => LogicalThreads.Length > 0;

    /// <summary>
    /// 获取单例实例
    /// </summary>
    public static CpuTopology Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new CpuTopology();
                }
            }
            return _instance;
        }
    }

    private CpuTopology()
    {
        DetectTopology();
    }

    /// <summary>
    /// 重新检测 CPU 拓扑（用于热插拔场景）
    /// </summary>
    public void Refresh()
    {
        DetectTopology();
    }

    private void DetectTopology()
    {
        var allCpus = new List<int>();
        var cpuSiblings = new Dictionary<int, int[]>(); // CPU -> siblings list
        var physicalCores = new List<int>();
        var logicalThreads = new List<int>();
        var processedSiblingGroups = new HashSet<string>();

        // 扫描所有 CPU
        string cpuBasePath = "/sys/devices/system/cpu";
        if (!Directory.Exists(cpuBasePath))
        {
            Console.WriteLine("⚠️ Cannot access CPU sysfs, CPU topology detection unavailable");
            return;
        }

        foreach (var cpuDir in Directory.GetDirectories(cpuBasePath, "cpu*"))
        {
            string cpuName = Path.GetFileName(cpuDir);
            if (!cpuName.StartsWith("cpu") || !int.TryParse(cpuName.Substring(3), out int cpuId))
                continue;

            // 检查 CPU 是否在线
            string onlinePath = Path.Combine(cpuDir, "online");
            if (File.Exists(onlinePath))
            {
                string onlineContent = File.ReadAllText(onlinePath).Trim();
                if (onlineContent == "0")
                    continue; // CPU 离线，跳过
            }

            allCpus.Add(cpuId);

            // 读取超线程兄弟信息
            string siblingsPath = Path.Combine(cpuDir, "topology", "thread_siblings_list");
            if (File.Exists(siblingsPath))
            {
                string siblingsContent = File.ReadAllText(siblingsPath).Trim();
                int[] siblings = ParseCpuRange(siblingsContent);
                cpuSiblings[cpuId] = siblings;

                // 使用兄弟列表作为组标识，避免重复处理
                string groupKey = string.Join(",", siblings.OrderBy(x => x));
                if (!processedSiblingGroups.Contains(groupKey))
                {
                    processedSiblingGroups.Add(groupKey);
                    int minSibling = siblings.Min();

                    if (siblings.Length > 1)
                    {
                        // 有超线程：第一个是物理核心，其余是逻辑线程
                        physicalCores.Add(minSibling);
                        foreach (var sib in siblings)
                        {
                            if (sib != minSibling)
                                logicalThreads.Add(sib);
                        }
                    }
                    else
                    {
                        // 没有超线程：就是物理核心
                        physicalCores.Add(siblings[0]);
                    }
                }
            }
        }

        AllCpus = allCpus.OrderBy(x => x).ToArray();
        PhysicalCores = physicalCores.OrderBy(x => x).ToArray();
        LogicalThreads = logicalThreads.OrderBy(x => x).ToArray();

        // 检测 P 核心和 E 核心
        DetectHybridCores(cpuSiblings);

        PrintTopologyInfo();
    }

    private void DetectHybridCores(Dictionary<int, int[]> cpuSiblings)
    {
        var pCores = new List<int>();
        var eCores = new List<int>();
        var pCoresPhysical = new List<int>();
        var pCoresLogical = new List<int>();

        // 方法1：通过 cpu_capacity 检测（某些内核支持）
        bool useCapacity = TryDetectByCapacity(out var highCapacityCpus, out var lowCapacityCpus);

        if (useCapacity && highCapacityCpus.Count > 0 && lowCapacityCpus.Count > 0)
        {
            pCores.AddRange(highCapacityCpus);
            eCores.AddRange(lowCapacityCpus);
        }
        else
        {
            // 方法2：通过超线程特征检测
            // P 核心通常有超线程（siblings > 1），E 核心没有超线程（siblings == 1）
            var processedGroups = new HashSet<string>();

            foreach (var cpu in AllCpus)
            {
                if (!cpuSiblings.TryGetValue(cpu, out var siblings))
                    continue;

                string groupKey = string.Join(",", siblings.OrderBy(x => x));
                if (processedGroups.Contains(groupKey))
                    continue;
                processedGroups.Add(groupKey);

                if (siblings.Length > 1)
                {
                    // 有超线程，推测为 P 核心
                    pCores.AddRange(siblings);
                    int minSibling = siblings.Min();
                    pCoresPhysical.Add(minSibling);
                    foreach (var sib in siblings)
                    {
                        if (sib != minSibling)
                            pCoresLogical.Add(sib);
                    }
                }
                else
                {
                    // 没有超线程，推测为 E 核心
                    eCores.Add(siblings[0]);
                }
            }
        }

        // 如果所有核心都有或都没有超线程，则不是混合架构
        if (pCores.Count == 0 || eCores.Count == 0)
        {
            // 不是混合架构，清空 P/E 核心分类
            PCores = Array.Empty<int>();
            ECores = Array.Empty<int>();
            PCoresPhysical = Array.Empty<int>();
            PCoresLogical = Array.Empty<int>();
        }
        else
        {
            PCores = pCores.OrderBy(x => x).ToArray();
            ECores = eCores.OrderBy(x => x).ToArray();
            PCoresPhysical = pCoresPhysical.OrderBy(x => x).ToArray();
            PCoresLogical = pCoresLogical.OrderBy(x => x).ToArray();
        }
    }

    private bool TryDetectByCapacity(out List<int> highCapacity, out List<int> lowCapacity)
    {
        highCapacity = new List<int>();
        lowCapacity = new List<int>();
        var capacities = new Dictionary<int, int>();

        foreach (var cpu in AllCpus)
        {
            string capacityPath = $"/sys/devices/system/cpu/cpu{cpu}/cpu_capacity";
            if (File.Exists(capacityPath))
            {
                string content = File.ReadAllText(capacityPath).Trim();
                if (int.TryParse(content, out int capacity))
                {
                    capacities[cpu] = capacity;
                }
            }
        }

        if (capacities.Count == 0)
            return false;

        // 检查是否有不同的容量值
        var uniqueCapacities = capacities.Values.Distinct().OrderByDescending(x => x).ToList();
        if (uniqueCapacities.Count < 2)
            return false; // 所有 CPU 容量相同，不是混合架构

        int maxCapacity = uniqueCapacities[0];
        foreach (var kvp in capacities)
        {
            if (kvp.Value == maxCapacity)
                highCapacity.Add(kvp.Key);
            else
                lowCapacity.Add(kvp.Key);
        }

        return true;
    }

    private static int[] ParseCpuRange(string input)
    {
        var result = new List<int>();
        if (string.IsNullOrWhiteSpace(input))
            return result.ToArray();

        foreach (var part in input.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var range = trimmed.Split('-');
                if (range.Length == 2 &&
                    int.TryParse(range[0], out int start) &&
                    int.TryParse(range[1], out int end) &&
                    start <= end)
                {
                    for (int i = start; i <= end; i++)
                        result.Add(i);
                }
            }
            else if (int.TryParse(trimmed, out int cpu))
            {
                result.Add(cpu);
            }
        }

        return result.Distinct().OrderBy(x => x).ToArray();
    }

    private void PrintTopologyInfo()
    {
        Console.WriteLine("🔍 CPU Topology Detected:");
        Console.WriteLine($"   Total CPUs: {AllCpus.Length} ({string.Join(",", AllCpus)})");
        Console.WriteLine($"   Physical Cores: {PhysicalCores.Length} ({string.Join(",", PhysicalCores)})");
        
        if (HasHyperThreading)
        {
            Console.WriteLine($"   Logical Threads (HT): {LogicalThreads.Length} ({string.Join(",", LogicalThreads)})");
        }

        if (IsHybridCpu)
        {
            Console.WriteLine($"   🚀 P-Cores (Performance): {PCores.Length} ({string.Join(",", PCores)})");
            Console.WriteLine($"      Physical: {PCoresPhysical.Length} ({string.Join(",", PCoresPhysical)})");
            Console.WriteLine($"      Logical (HT): {PCoresLogical.Length} ({string.Join(",", PCoresLogical)})");
            Console.WriteLine($"   🔋 E-Cores (Efficiency): {ECores.Length} ({string.Join(",", ECores)})");
        }
        else
        {
            Console.WriteLine("   ℹ️ Not a hybrid CPU architecture");
        }
    }

    /// <summary>
    /// 解析特殊 CPU 关键字，返回对应的 CPU 列表
    /// </summary>
    /// <param name="keyword">关键字，如 "P", "E", "P-physical", "P-logical", "physical", "logical", "all"</param>
    /// <returns>对应的 CPU 编号数组，如果关键字无效则返回 null</returns>
    public int[]? ResolveCpuKeyword(string keyword)
    {
        return keyword.ToUpperInvariant() switch
        {
            "P" or "PCORE" or "PCORES" or "P-CORE" or "P-CORES" or "PERFORMANCE" => 
                IsHybridCpu ? PCores : null,
            
            "E" or "ECORE" or "ECORES" or "E-CORE" or "E-CORES" or "EFFICIENCY" => 
                IsHybridCpu ? ECores : null,
            
            "P-PHYSICAL" or "PCORE-PHYSICAL" or "PCORES-PHYSICAL" => 
                IsHybridCpu ? PCoresPhysical : null,
            
            "P-LOGICAL" or "P-HT" or "PCORE-LOGICAL" or "PCORE-HT" or "PCORES-LOGICAL" or "PCORES-HT" => 
                IsHybridCpu ? PCoresLogical : null,
            
            "PHYSICAL" or "PHYSICAL-CORES" or "NO-HT" or "NOHT" => 
                PhysicalCores,
            
            "LOGICAL" or "HT" or "HYPERTHREAD" or "HYPERTHREADS" or "SMT" => 
                HasHyperThreading ? LogicalThreads : null,
            
            "ALL" => 
                AllCpus,
            
            _ => null
        };
    }

    /// <summary>
    /// 解析带排除的 CPU 关键字表达式
    /// 支持格式: "P", "E", "P-E" (P核心排除E核心), "all-logical" (所有核心排除逻辑线程)
    /// </summary>
    public int[]? ResolveCpuExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        expression = expression.Trim();

        // 检查是否是简单关键字
        var simple = ResolveCpuKeyword(expression);
        if (simple != null)
            return simple;

        // 检查是否看起来像纯数字表达式（如 "0,1,2" 或 "4-7"）
        // 如果是，返回 null 让调用者使用传统数字解析
        if (LooksLikeNumericCpuList(expression))
            return null;

        // 检查是否是组合表达式 (用 + 表示合并, - 表示排除)
        // 例如: "P+E", "physical+E", "all-logical", "P-P-logical"
        var result = new HashSet<int>();
        var parts = new List<(bool isAdd, string keyword)>();
        
        int i = 0;
        bool nextIsAdd = true;
        int start = 0;
        
        while (i <= expression.Length)
        {
            char c = i < expression.Length ? expression[i] : '\0';
            
            if (c == '+' || c == '-' || c == '\0')
            {
                if (i > start)
                {
                    string keyword = expression.Substring(start, i - start).Trim();
                    if (!string.IsNullOrEmpty(keyword))
                    {
                        parts.Add((nextIsAdd, keyword));
                    }
                }
                nextIsAdd = c == '+' || c == '\0';
                start = i + 1;
            }
            i++;
        }

        foreach (var (isAdd, keyword) in parts)
        {
            var cpus = ResolveCpuKeyword(keyword);
            if (cpus == null)
            {
                // 尝试解析为普通 CPU 列表
                cpus = ParseCpuRange(keyword);
                if (cpus.Length == 0)
                    return null; // 无效的关键字或 CPU 列表
            }

            if (isAdd)
            {
                foreach (var cpu in cpus)
                    result.Add(cpu);
            }
            else
            {
                foreach (var cpu in cpus)
                    result.Remove(cpu);
            }
        }

        return result.OrderBy(x => x).ToArray();
    }

    /// <summary>
    /// 检查字符串是否看起来像纯数字 CPU 列表（如 "0,1,2" 或 "4-7" 或 "0,2,4-6"）
    /// </summary>
    private static bool LooksLikeNumericCpuList(string s)
    {
        // 如果字符串只包含数字、逗号、连字符和空格，则认为是数字 CPU 列表
        foreach (char c in s)
        {
            if (!char.IsDigit(c) && c != ',' && c != '-' && c != ' ')
                return false;
        }
        return true;
    }
}
