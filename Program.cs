using AffinitySetter;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length > 0)
        {
            switch (args[0].ToLower())
            {
                case "save":
                    if (args.Length == 3)
                    {
                        ConfigLoader.SaveOrUpdateRule(args[1], args[2]);
                        Console.WriteLine($"✅ Rule saved: {args[2]}:{args[1]}");
                    }
                    else
                    {
                        Console.WriteLine("Usage: AffinitySetter save <cpus> <match>");
                    }
                    return;

                case "remove":
                    if (args.Length == 2)
                    {
                        ConfigLoader.RemoveRule(args[1]);
                    }
                    else
                    {
                        Console.WriteLine("Usage: AffinitySetter remove <match>");
                    }
                    return;

                case "list":
                    ConfigLoader.ListRules();
                    return;

                default:
                    Console.WriteLine("Unknown command.");
                    return;
            }
        }

        // 正常服务运行
        Console.WriteLine("🌀 AffinitySetter Starting...");
        var rules = ConfigLoader.Load();

        if (rules.Count == 0)
        {
            Console.WriteLine("❌ No valid rules found. Exiting.");
            Environment.Exit(1);
        }

        WorkerPool.Start(workerCount: Environment.ProcessorCount / 2);
        InitialScanAndBind(rules);
        InotifyWatcher.Watch(rules);

        Console.WriteLine("✅ Running. Ctrl+C to exit.");
        Thread.Sleep(Timeout.Infinite);
    }


    private static void InitialScanAndBind(List<TargetRule> rules)
    {
        foreach (var pidDir in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidDir), out int pid)) continue;

            string statusPath = Path.Combine(pidDir, "status");
            if (!File.Exists(statusPath)) continue;

            string? name = ReadNameFromStatus(statusPath);
            if (name == null) continue;

            foreach (var rule in rules)
            {
                if (name.Contains(rule.NamePattern, StringComparison.OrdinalIgnoreCase))
                {
                    string taskPath = Path.Combine(pidDir, "task");
                    foreach (var tidDir in Directory.EnumerateDirectories(taskPath))
                    {
                        if (int.TryParse(Path.GetFileName(tidDir), out int tid))
                            WorkerPool.Enqueue(tid, rule.CpuMask);
                    }
                }
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

    private static void WatchInotify(List<TargetRule> rules)
    {
        // 可选：实现inotify监听 /proc/*/task/* 创建线程时触发 WorkerPool.Enqueue
        Console.WriteLine("[!] Inotify monitoring not yet wired in here — optional");
    }
}
