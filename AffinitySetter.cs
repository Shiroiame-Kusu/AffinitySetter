using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

internal class AffinitySetter
{
    static Dictionary<string, Rule> rules = new(StringComparer.OrdinalIgnoreCase);
    static HashSet<int> processedTids = new();
    static bool running = true;
    static readonly string ConfigFilePath = "/etc/AffinitySetter.conf";
    static FileSystemWatcher configWatcher;
    static readonly object configLock = new(); // 用于配置访问的锁
    static Timer reloadTimer; // 用于延迟重载的计时器
    const int ReloadDelay = 500; // 延迟时间（毫秒）

    public static int Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) => {
            running = false;
            e.Cancel = true;
            Console.WriteLine("\nExiting...");
        };
        Console.WriteLine("🌀 AffinitySetter Starting...");
        if (!LoadConfig(ConfigFilePath))
        {   
            Console.WriteLine("❌ No valid rules found. Exiting.");
            Console.WriteLine("❌ Create a config file at /etc/AffinitySetter.conf First.");
            if (File.Exists(ConfigFilePath))
            {
                File.Delete(ConfigFilePath);
            }

            File.Create(ConfigFilePath);
            return 1;
        }
            
        Console.WriteLine("Loaded configuration:");
        foreach (var rule in rules)
            Console.WriteLine($"  {rule.Key}: {string.Join(",", rule.Value.Cpus)}");

        // 初始化文件监视器
        SetupConfigWatcher();
        
        while (running)
        {
            ScanProcesses();
            Thread.Sleep(1000);
        }

        // 清理资源
        configWatcher?.Dispose();
        reloadTimer?.Dispose();
        return 0;
    }

    static void SetupConfigWatcher()
    {
        string configDir = Path.GetDirectoryName(ConfigFilePath);
        string configFile = Path.GetFileName(ConfigFilePath);
        
        configWatcher = new FileSystemWatcher(configDir)
        {
            Filter = configFile,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        
        configWatcher.Changed += OnConfigChanged;
        Console.WriteLine($"🔭 Watching config: {ConfigFilePath}");
    }

    static void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        // 延迟触发重载，避免多次事件
        reloadTimer?.Dispose(); // 取消之前的计时器
        reloadTimer = new Timer(ReloadConfig, null, ReloadDelay, Timeout.Infinite);
    }

    static void ReloadConfig(object state)
    {
        lock (configLock)
        {
            try
            {
                Console.WriteLine("\n🔄 Reloading configuration...");
                var newRules = new Dictionary<string, Rule>(StringComparer.OrdinalIgnoreCase);
                if (LoadConfigInto(ConfigFilePath, newRules))
                {
                    rules = newRules;
                    processedTids.Clear(); // 清除已处理线程记录
                    Console.WriteLine("✅ Configuration reloaded successfully.");
                    foreach (var rule in rules)
                        Console.WriteLine($"  {rule.Key}: {string.Join(",", rule.Value.Cpus)}");
                }
                else
                {
                    Console.WriteLine("❌ Configuration reload failed. Keeping old rules.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Reload error: {ex.Message}");
            }
        }
    }

    static bool LoadConfig(string filePath)
    {
        return LoadConfigInto(filePath, rules);
    }

    static bool LoadConfigInto(string filePath, Dictionary<string, Rule> target)
    {
        try
        {
            target.Clear();
            foreach (var line in File.ReadLines(filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split(':', 2);
                if (parts.Length != 2)
                {
                    Console.WriteLine($"Invalid config line: {line}");
                    continue;
                }

                var name = parts[0].Trim();
                var cpus = ParseCpuList(parts[1].Trim());
                target[name] = new Rule(name, cpus, BuildCpuMask(cpus));
            }
            return target.Count > 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error loading config: {ex.Message}");
            return false;
        }
    }

    static void ScanProcesses()
    {
        Dictionary<string, Rule> currentRules;
        lock (configLock)
        {
            // 复制当前规则集（线程安全）
            currentRules = new Dictionary<string, Rule>(rules, StringComparer.OrdinalIgnoreCase);
        }

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

            foreach (var tidDir in Directory.EnumerateDirectories(taskDir))
            {
                if (!int.TryParse(Path.GetFileName(tidDir), out int tid)) 
                    continue;

                if (processedTids.Contains(tid)) 
                    continue;

                processedTids.Add(tid);
                ApplyAffinityRules(tid, pid, procName, currentRules);
            }
        }
    }

    static void ApplyAffinityRules(int tid, int pid, string procName, Dictionary<string, Rule> ruleSet)
    {
        foreach (var rule in ruleSet)
        {
            if (procName.IndexOf(rule.Key, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (SetAffinity(tid, rule.Value.Mask))
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

    static string? ReadNameFromStatus(string statusPath)
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

    static byte[] BuildCpuMask(int[] cpus)
    {
        var mask = new byte[128]; // 1024 bits
        foreach (var cpu in cpus)
        {
            if (cpu >= 0 && cpu < 1024)
            {
                int byteIndex = cpu / 8;
                int bitOffset = cpu % 8;
                mask[byteIndex] |= (byte)(1 << bitOffset);
            }
        }
        return mask;
    }

    static bool SetAffinity(int tid, byte[] mask)
    {
        int result = sched_setaffinity(tid, (IntPtr)mask.Length, mask);
        return result == 0;
    }

    static int[] ParseCpuList(string input)
    {
        var result = new List<int>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var bounds = part.Split('-');
                if (bounds.Length == 2 && int.TryParse(bounds[0], out int start) && int.TryParse(bounds[1], out int end))
                    for (int i = start; i <= end; i++) 
                        result.Add(i);
            }
            else if (int.TryParse(part, out int cpu))
            {
                result.Add(cpu);
            }
        }
        return result.ToArray();
    }

    [DllImport("libc", SetLastError = true)]
    static extern int sched_setaffinity(int pid, IntPtr cpusetsize, byte[] mask);

    class Rule
    {
        public string Name { get; }
        public int[] Cpus { get; }
        public byte[] Mask { get; }

        public Rule(string name, int[] cpus, byte[] mask)
        {
            Name = name;
            Cpus = cpus;
            Mask = mask;
        }
    }
}