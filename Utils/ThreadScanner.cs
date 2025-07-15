
using System.Runtime.InteropServices;
using AffinitySetter.Config;
using AffinitySetter.Type;

namespace AffinitySetter.Utils;
internal sealed class ThreadScanner
{
    private readonly ConfigManager _configManager;
    private readonly HashSet<int> _processedTids = new();
    private readonly Dictionary<int, string> _exePathCache = new();
    private readonly Dictionary<int, string> _cmdLineCache = new();
    
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
        
        // 定期清理缓存（每10分钟）
        if (DateTime.Now.Minute % 10 == 0)
        {
            _exePathCache.Clear();
            _cmdLineCache.Clear();
        }
    }

    private void ProcessThreads(int pid, string procName, string? exePath, string? cmdLine, 
                                 string taskDir, IReadOnlyList<AffinityRule> rules)
    {
        foreach (var tidDir in Directory.EnumerateDirectories(taskDir))
        {
            if (!int.TryParse(Path.GetFileName(tidDir), out int tid)) 
                continue;

            if (_processedTids.Contains(tid)) 
                continue;

            _processedTids.Add(tid);
            ApplyAffinityRules(tid, pid, procName, exePath, cmdLine, rules);
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
        if (_exePathCache.TryGetValue(pid, out var cachedPath))
            return cachedPath;
        
        try
        {
            string exeLink = $"/proc/{pid}/exe";
            var target = File.ResolveLinkTarget(exeLink, true);
            if (target != null)
            {
                string fullPath = target.FullName;
                _exePathCache[pid] = fullPath;
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
        if (_cmdLineCache.TryGetValue(pid, out var cachedCmd))
            return cachedCmd;
        
        string? cmdLine = CommandLineUtils.GetCommandLine(pid);
        if (!string.IsNullOrEmpty(cmdLine))
        {
            // 缩短长命令行（显示前200字符）
            string displayCmd = cmdLine.Length > 200 ? 
                cmdLine[..200] + "..." : cmdLine;
                
            _cmdLineCache[pid] = displayCmd;
            return displayCmd;
        }
        
        return null;
    }

    private static string? ReadNameFromStatus(string statusPath)
    {
        try
        {
            foreach (var line in File.ReadLines(statusPath))
                if (line.StartsWith("Name:"))
                    return line.Substring(5).Trim();
        }
        catch { }
        return null;
    }
}