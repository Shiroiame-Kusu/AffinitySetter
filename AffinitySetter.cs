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
    
    private static string GetLogPath()
    {
        // 优先使用 /var/log（需要 root 权限）
        string varLogPath = "/var/log";
        if (Directory.Exists(varLogPath))
        {
            try
            {
                string testPath = Path.Combine(varLogPath, $".affinitysetter_test_{Environment.ProcessId}");
                File.WriteAllText(testPath, "test");
                File.Delete(testPath);
                return Path.Combine(varLogPath, $"AffinitySetter-{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }
            catch { }
        }
        
        // 降级到用户目录
        string userLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "log");
        Directory.CreateDirectory(userLogDir);
        return Path.Combine(userLogDir, $"AffinitySetter-{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }
    
    // AffinitySetter.cs (Main loop)
    public static async Task<int> Main(string[] args)
    {   
        // Redirect Console.WriteLine to also log to a file
        var logPath = GetLogPath();
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
                    PrintHelp();
                    return 0;
                case "topology":
                case "--topology":
                case "-t":
                    PrintTopology();
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
        CrashHandler.Setup();
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

            var deletedRules = oldRules.Where(oldRule =>
                !newRules.Any(newRule =>
                    newRule.Type == oldRule.Type &&
                    newRule.Pattern == oldRule.Pattern
                )
            ).ToList();

            if (changedRules.Count == 0 && deletedRules.Count == 0)
            {
                Console.WriteLine("No new, changed, or deleted rules to apply.");
                return;
            }

            foreach (var rule in changedRules)
            {
                int count = threadScanner.ApplyRuleToAllMatchingProcesses(rule);
                Console.WriteLine($"🔁 [RulesChanged] Applied rule '{rule.Pattern}' to {count} threads.");
            }

            foreach (var rule in deletedRules)
            {
                int count = threadScanner.ResetRuleForAllMatchingProcesses(rule);
                Console.WriteLine($"🔁 [RulesChanged] Reset rule '{rule.Pattern}' for {count} threads.");
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

    private static void PrintHelp()
    {
        Console.WriteLine(@"Usage: AffinitySetter [options]

Options:
  --version, -v     Show version
  --help, -h        Show this help
  --topology, -t    Show CPU topology information
  load <file>       Load configuration from specified file
  save <name> <cpu> Save a rule for process name with CPU list

CPU Keywords (for use in config 'cpus' field):
  P, PCore          All P-cores (Performance cores with HT)
  E, ECore          All E-cores (Efficiency cores)
  P-physical        P-cores physical threads only (no HT)
  P-logical, P-HT   P-cores logical threads only (HT siblings)
  physical, no-HT   All physical cores (first thread of each core)
  logical, HT       All logical threads (HT siblings only)
  all               All CPUs

Expressions:
  Use + to combine, - to exclude
  Examples: ""P+E"", ""all-logical"", ""P-P-HT"", ""physical+E""

Examples:
  AffinitySetter                    # Start with default config
  AffinitySetter load my.conf       # Use custom config file
  AffinitySetter save firefox P     # Save rule: firefox -> P-cores
  AffinitySetter save chrome E      # Save rule: chrome -> E-cores
  AffinitySetter topology           # Show CPU topology info
");
    }

    private static void PrintTopology()
    {
        var topology = CpuTopology.Instance;
        Console.WriteLine("\n📊 CPU Topology Summary:");
        Console.WriteLine($"   Total CPUs: {topology.AllCpus.Length}");
        Console.WriteLine($"   Physical Cores: {topology.PhysicalCores.Length}");
        
        if (topology.HasHyperThreading)
        {
            Console.WriteLine($"   Hyper-Threading: Enabled ({topology.LogicalThreads.Length} logical threads)");
        }
        else
        {
            Console.WriteLine("   Hyper-Threading: Disabled");
        }

        if (topology.IsHybridCpu)
        {
            Console.WriteLine("\n🔥 Hybrid CPU Architecture Detected:");
            Console.WriteLine($"   P-Cores (Performance): {topology.PCores.Length} threads");
            Console.WriteLine($"     - Physical: [{string.Join(", ", topology.PCoresPhysical)}]");
            Console.WriteLine($"     - Logical (HT): [{string.Join(", ", topology.PCoresLogical)}]");
            Console.WriteLine($"   E-Cores (Efficiency): {topology.ECores.Length} threads");
            Console.WriteLine($"     - CPUs: [{string.Join(", ", topology.ECores)}]");
        }
        else
        {
            Console.WriteLine("\n   Standard CPU architecture (no P/E core distinction)");
        }

        Console.WriteLine("\n📝 Available CPU Keywords for config:");
        Console.WriteLine("   P, PCore, E, ECore, P-physical, P-logical, P-HT");
        Console.WriteLine("   physical, no-HT, logical, HT, all");
        Console.WriteLine("\n   Use + to combine, - to exclude (e.g., \"all-logical\", \"P+E\")");
    }
}
