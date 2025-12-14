using System.Runtime.InteropServices;
using AffinitySetter.Config;
using AffinitySetter.Type;

namespace AffinitySetter.Utils;
internal sealed class ThreadScanner
{
    private readonly ConfigManager _configManager;
    private HashSet<int> _processedTids = new();
    private HashSet<int> _currentScanTids = new();
    private readonly Dictionary<int, string> _exePathCache = new();
    private readonly Dictionary<int, string> _cmdLineCache = new();
    private readonly object _cacheLock = new();
    private readonly object _tidLock = new();
    private DateTime _lastCacheClear = DateTime.Now;
    private const int CacheClearIntervalMinutes = 10;
    
    public ThreadScanner(ConfigManager configManager)
    {
        _configManager = configManager;
    }

    public void ScanProcesses()
    {
        var rules = _configManager.GetRules();
        
        foreach (var pidDir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidDir), out int pid)) 
                continue;

            string statusPath = Path.Combine(pidDir, "status");
            if (!File.Exists(statusPath)) 
                continue;

            string? procName = ReadNameFromStatus(statusPath);
            if (procName == null) 
                continue;

            string? exePath = GetExecutablePath(pid);
            string? cmdLine = GetCommandLine(pid);
            string taskDir = Path.Combine(pidDir, "task");
            if (!Directory.Exists(taskDir)) 
                continue;

            ProcessThreads(pid, procName, exePath, cmdLine, taskDir, rules);
        }
        
        ClearCachesIfNeeded();
    }
    // ThreadScanner.cs (Parallel scan)
    public async Task ScanProcessesAsync()
    {
        var rules = _configManager.GetRules();
        var pidDirs = Directory.EnumerateDirectories("/proc")
            .Where(d => int.TryParse(Path.GetFileName(d), out _))
            .ToList();

        var tasks = pidDirs.Select(pidDir => Task.Run(() => ProcessPid(pidDir, rules)));
        await Task.WhenAll(tasks);

        ClearCachesIfNeeded();
    }

    private void ProcessPid(string pidDir, IReadOnlyList<AffinityRule> rules)
    {
        if (!int.TryParse(Path.GetFileName(pidDir), out int pid)) 
            return;

        string statusPath = Path.Combine(pidDir, "status");
        if (!File.Exists(statusPath)) 
            return;

        string? procName = ReadNameFromStatus(statusPath);
        if (procName == null) 
            return;

        string? exePath = GetExecutablePath(pid);
        string? cmdLine = GetCommandLine(pid);
        string taskDir = Path.Combine(pidDir, "task");
        if (!Directory.Exists(taskDir)) 
            return;

        ProcessThreads(pid, procName, exePath, cmdLine, taskDir, rules);
    }

    private void ProcessThreads(int pid, string procName, string? exePath, string? cmdLine, string taskDir, IReadOnlyList<AffinityRule> rules)
    {
        if (!Directory.Exists(taskDir))
            return; // Handle missing directory gracefully

        try
        {
            foreach (var tidDir in Directory.EnumerateDirectories(taskDir))
            {
                var tidName = Path.GetFileName(tidDir);
                if (!int.TryParse(tidName, out int tid)) 
                    continue;

                lock (_tidLock)
                {
                    _currentScanTids.Add(tid); // 记录当前扫描周期中存在的 TID
                    if (!_processedTids.Add(tid))
                        continue; // 已处理过，跳过
                }
                ApplyAffinityRules(tid, pid, procName, exePath, cmdLine, rules);
                
            }
        }
        catch (DirectoryNotFoundException)
        {
            // The task directory disappeared between the check and enumeration; skip this process.
        }
        catch (IOException)
        {
            // IO error (e.g., process exited); skip this process.
        }
    }


    private void ApplyAffinityRules(int tid, int pid, string procName, string? exePath, string? cmdLine, IReadOnlyList<AffinityRule> rules)
    {
        foreach (var rule in rules)
        {
            bool match = false;
        
            switch (rule.Type)
            {
                case RuleType.ProcessName:
                    match = procName.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                    break;
                
                case RuleType.ExecutablePath when exePath != null:
                    match = exePath.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                    break;
                
                case RuleType.CommandLine when cmdLine != null:
                    match = CommandLineUtils.IsMatch(cmdLine, rule.Pattern, rule.IsRegex);
                    break;
            }

            if (match)
            {
                if (rule.Apply(tid))
                {
                    string typeName = rule.Type switch {
                        RuleType.ProcessName => "Name",
                        RuleType.ExecutablePath => "Path",
                        RuleType.CommandLine => "Command",
                        _ => "Unknown"
                    };
                    if (rule.HasIoPriority)
                        rule.ApplyIoPriority(tid);
                    if(rule.HasNicePriority)
                        rule.ApplyNice(tid);
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Set affinity for PID:{pid} TID:{tid} ({typeName}:{rule.Pattern}): CPUs {string.Join(",", rule.Cpus)}");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ❌ ERR:{Marshal.GetLastWin32Error()} Set affinity for PID:{pid} TID:{tid} ({procName}): CPUs {string.Join(",", rule.Cpus)}");
                }
                return; // 只应用第一个匹配的规则
            }
        }
    }

    private string? GetExecutablePath(int pid)
    {
        lock (_cacheLock)
        {
            if (_exePathCache.TryGetValue(pid, out var cachedPath))
                return cachedPath;
        }
        try
        {
            string exeLink = $"/proc/{pid}/exe";
            var target = File.ResolveLinkTarget(exeLink, true);
            if (target != null)
            {
                string fullPath = target.FullName;
                lock (_cacheLock)
                {
                    _exePathCache[pid] = fullPath;
                }
                return fullPath;
            }
        }
        catch
        {
            // 忽略错误
        }
        return null;
    }

    private string? GetCommandLine(int pid)
    {
        lock (_cacheLock)
        {
            if (_cmdLineCache.TryGetValue(pid, out var cachedCmd))
                return cachedCmd;
        }
        string? cmdLine = CommandLineUtils.GetCommandLine(pid);
        if (!string.IsNullOrEmpty(cmdLine))
        {
            string displayCmd = cmdLine.Length > 200 ? 
                cmdLine[..200] + "..." : cmdLine;
            lock (_cacheLock)
            {
                _cmdLineCache[pid] = displayCmd;
            }
            return displayCmd;
        }
        return null;
    }

    private static string? ReadNameFromStatus(string statusPath)
    {
        try
        {
            foreach (var line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("Name:"))
                    return line.Substring(5).Trim();
            }
        }
        catch { }
        return null;
    }

    public void ClearCachesIfNeeded()
    {
        var now = DateTime.Now;
        if ((now - _lastCacheClear).TotalMinutes >= CacheClearIntervalMinutes)
        {
            lock (_cacheLock)
            {
                _exePathCache.Clear();
                _cmdLineCache.Clear();
            }
            // 清理已处理的 TID 集合，只保留当前扫描周期中存在的 TID
            lock (_tidLock)
            {
                _processedTids = _currentScanTids;
                _currentScanTids = new HashSet<int>();
            }
            _lastCacheClear = now;
        }
    }

    public int ApplyRuleToAllMatchingProcesses(AffinityRule rule)
    {
        int appliedCount = 0;
        foreach (var pidDir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidDir), out int pid))
                continue;
            string statusPath = Path.Combine(pidDir, "status");
            if (!File.Exists(statusPath))
                continue;
            string? procName = ReadNameFromStatus(statusPath);
            if (procName == null)
                continue;
            string? exePath = GetExecutablePath(pid);
            string? cmdLine = GetCommandLine(pid);
            string taskDir = Path.Combine(pidDir, "task");
            if (!Directory.Exists(taskDir))
                continue;
            foreach (var tidDir in Directory.EnumerateDirectories(taskDir))
            {
                if (!int.TryParse(Path.GetFileName(tidDir), out int tid))
                    continue;
                bool match = false;
                switch (rule.Type)
                {
                    case RuleType.ProcessName:
                        match = procName.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleType.ExecutablePath when exePath != null:
                        match = exePath.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleType.CommandLine when cmdLine != null:
                        match = CommandLineUtils.IsMatch(cmdLine, rule.Pattern, rule.IsRegex);
                        break;
                }
                if (match)
                {
                    if (rule.Apply(tid))
                    {
                        if (rule.HasIoPriority)
                            rule.ApplyIoPriority(tid);
                        if (rule.HasNicePriority)
                            rule.ApplyNice(tid);
                        appliedCount++;
                    }
                }
            }
        }
        return appliedCount;
    }
    
    public int ResetRuleForAllMatchingProcesses(AffinityRule rule)
    {
        int resetCount = 0;
        foreach (var pidDir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidDir), out int pid))
                continue;
            string statusPath = Path.Combine(pidDir, "status");
            if (!File.Exists(statusPath))
                continue;
            string? procName = ReadNameFromStatus(statusPath);
            if (procName == null)
                continue;
            string? exePath = GetExecutablePath(pid);
            string? cmdLine = GetCommandLine(pid);
            string taskDir = Path.Combine(pidDir, "task");
            if (!Directory.Exists(taskDir))
                continue;
            foreach (var tidDir in Directory.EnumerateDirectories(taskDir))
            {
                if (!int.TryParse(Path.GetFileName(tidDir), out int tid))
                    continue;
                bool match = false;
                switch (rule.Type)
                {
                    case RuleType.ProcessName:
                        match = procName.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleType.ExecutablePath when exePath != null:
                        match = exePath.IndexOf(rule.Pattern, StringComparison.OrdinalIgnoreCase) >= 0;
                        break;
                    case RuleType.CommandLine when cmdLine != null:
                        match = CommandLineUtils.IsMatch(cmdLine, rule.Pattern, rule.IsRegex);
                        break;
                }
                if (match)
                {
                    // Reset affinity to all CPUs (default)
                    var defaultMask = CpuUtils.BuildCpuMask(Enumerable.Range(0, Environment.ProcessorCount).ToArray());
                    CpuUtils.sched_setaffinity(tid, (IntPtr)defaultMask.Length, defaultMask);
                    // Reset nice to 0
                    CpuUtils.setpriority(0, tid, 0);
                    // Optionally reset I/O priority to best-effort (class 2, data 4)
                    CpuUtils.ioprio_set(1, tid, (2 << 13) | 4);
                    resetCount++;
                }
            }
        }
        return resetCount;
    }
}