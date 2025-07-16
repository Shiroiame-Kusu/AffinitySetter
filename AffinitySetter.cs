using System;
using System.Threading;
using AffinitySetter.Config;
using AffinitySetter.Utils;
using AffinitySetter.Type;
namespace AffinitySetter;
internal class AffinitySetter
{
    static bool running = true;

    private static string ConfigPath = "/etc/AffinitySetter.conf";
    // AffinitySetter.cs (Main loop)
    public static async Task<int> Main(string[] args)
    {   
        // Redirect Console.WriteLine to also log to a file
        var logPath = Path.Combine("/var/log", $"AffinitySetter-{DateTime.Now:yyyyMMdd_HHmmss}.log");
        Console.SetOut(new LogWriter(logPath));

        Console.WriteLine($"AffinitySetter {Global.Version}");
        if (args.Length >= 1)
        {
            switch (args[0])
            {
                case "--version":
                case "-v":
                case "version":
                    Console.WriteLine($"AffinitySetter {Global.Version}");
                    return 0;
                case "--help":
                case "-h":
                case "help":
                    Console.WriteLine("Usage: AffinitySetter [options]\nOptions:\n  --version, -v   Show version\n  --help, -h      Show this help");
                    return 0;
                case "load":
                    if (args.Length == 2)
                    {
                        ConfigPath = args[1];
                    }
                    else
                    {
                        Console.WriteLine("Usage: AffinitySetter load [FileName]");
                        return 1;
                    }
                    break;
                case "save":
                    if (args.Length == 3)
                    {
                        string processName = args[1];
                        string cpuListStr = args[2];

                        // Parse CPU list (e.g., "0-3,6")
                        List<int> cpuList = new();
                        foreach (var part in cpuListStr.Split(','))
                        {
                            if (part.Contains('-'))
                            {
                                var range = part.Split('-');
                                if (int.TryParse(range[0], out int start) && int.TryParse(range[1], out int end))
                                {
                                    for (int i = start; i <= end; i++)
                                        cpuList.Add(i);
                                }
                            }
                            else if (int.TryParse(part, out int cpu))
                            {
                                cpuList.Add(cpu);
                            }
                        }

                        var configManager = new ConfigManager(ConfigPath);
                        configManager.LoadConfig();
                        var rules = configManager.GetRules().ToList();

                        // Update or add rule
                        var existing = rules.FirstOrDefault(r => r.Type == RuleType.ProcessName && r.Pattern == processName);
                        if (existing != null)
                        {
                            existing.Cpus = cpuList.ToArray();
                        }
                        else
                        {
                            rules.Add(new AffinityRule
                            {
                                Type = RuleType.ProcessName,
                                Pattern = processName,
                                Cpus = cpuList.ToArray()
                            });
                        }

                        // Save updated config
                        typeof(ConfigManager)
                            .GetMethod("SaveConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                            ?.Invoke(configManager, new object[] { rules });

                        Console.WriteLine($"✅ Saved rule for '{processName}' with CPUs: {string.Join(",", cpuList)}");
                        return 0;
                    }
                    Console.WriteLine("Usage: AffinitySetter save [ProcessName] [CpuList]\nExample: AffinitySetter save firefox 0-3,6");
                    return 1;
                default:
                    Console.WriteLine($"Unknown option: {args[0]}");
                    return 1;
            }
        }
        Console.CancelKeyPress += (sender, e) => { running = false; e.Cancel = true; Console.WriteLine("\nExiting..."); };
        Console.WriteLine("🌀 AffinitySetter Starting...");

        var _configManager = new ConfigManager(ConfigPath);
        var threadScanner = new ThreadScanner(_configManager);
        // Remove ConfigReloaded event handler, only use RulesChanged for selective re-application
        // _configManager.ConfigReloaded += async () =>
        // {
        //     Console.WriteLine("\n🔁 Applying new rules to running processes...");
        //     threadScanner.ClearProcessed();
        //     await threadScanner.ScanProcessesAsync();
        // };
        // Subscribe to RulesChanged event to only apply new/changed rules
        _configManager.RulesChanged += (oldRules, newRules) =>
        {
            var changedRules = newRules.Where(newRule =>
                !oldRules.Any(oldRule =>
                    oldRule.Type == newRule.Type &&
                    oldRule.Pattern == newRule.Pattern &&
                    oldRule.Cpus.SequenceEqual(newRule.Cpus) &&
                    oldRule.Nice == newRule.Nice &&
                    oldRule.IoPriorityClass == newRule.IoPriorityClass &&
                    oldRule.IoPriorityData == newRule.IoPriorityData
                )
            ).ToList();
            if (changedRules.Count == 0)
            {
                Console.WriteLine("No new or changed rules to apply.");
                return;
            }
            foreach (var rule in changedRules)
            {
                int count = threadScanner.ApplyRuleToAllMatchingProcesses(rule);
                Console.WriteLine($"[RulesChanged] Applied rule '{rule.Pattern}' to {count} threads.");
            }
        };
        if (!_configManager.LoadConfig())
        {
            Console.WriteLine("❌ No valid rules found. Exiting.");
            return 1;
        }

        while (running)
        {
            await threadScanner.ScanProcessesAsync();
            await Task.Delay(1000);
        }

        return 0;
    }
}
