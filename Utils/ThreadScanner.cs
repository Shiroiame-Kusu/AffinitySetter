using System.Runtime.InteropServices;
using AffinitySetter.Config;

namespace AffinitySetter.Utils;
internal sealed class ThreadScanner
{
    private readonly ConfigManager _configManager;
    private readonly HashSet<int> _processedTids = new();
    
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

            string taskDir = Path.Combine(pidDir, "task");
            if (!Directory.Exists(taskDir)) 
                continue;

            ProcessThreads(pid, procName, taskDir, rules);
        }
    }

    private void ProcessThreads(int pid, string procName, string taskDir, IReadOnlyDictionary<string, AffinityRule> rules)
    {
        foreach (var tidDir in Directory.EnumerateDirectories(taskDir))
        {
            if (!int.TryParse(Path.GetFileName(tidDir), out int tid)) 
                continue;

            if (_processedTids.Contains(tid)) 
                continue;

            _processedTids.Add(tid);
            ApplyAffinityRules(tid, pid, procName, rules);
        }
    }

    private void ApplyAffinityRules(int tid, int pid, string procName, IReadOnlyDictionary<string, AffinityRule> rules)
    {
        foreach (var rule in rules)
        {
            if (procName.IndexOf(rule.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (rule.Value.Apply(tid))
                {
                    Console.WriteLine($"[{DateTime.Now}] Set affinity for PID:{pid} TID:{tid} ({procName}): CPUs {string.Join(",", rule.Value.Cpus)}");
                }
                else
                {
                    Console.WriteLine($"[{DateTime.Now}] ❌ ERR:{Marshal.GetLastWin32Error()} Set affinity for PID:{pid} TID:{tid} ({procName}): CPUs {string.Join(",", rule.Value.Cpus)}");
                }
                return; // 只应用第一个匹配的规则
            }
        }
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