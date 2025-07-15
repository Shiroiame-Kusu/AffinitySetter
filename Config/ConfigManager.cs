using System.Text.Json;
using AffinitySetter.Type;
using AffinitySetter.Utils;

namespace AffinitySetter.Config;
internal sealed class ConfigManager : IDisposable
{
    private readonly string _configFilePath;
    private List<AffinityRule> _rules = new();
    private FileSystemWatcher _configWatcher;
    private Timer _reloadTimer;
    private readonly object _lock = new object();
    private const int ReloadDelay = 500;

    public ConfigManager(string configFilePath)
    {
        _configFilePath = configFilePath;
        SetupConfigWatcher();
    }

    public bool LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    Console.WriteLine($"❌ Config file not found: {_configFilePath}");
                    CreateDefaultConfig();
                    return false;
                }
                
                Console.WriteLine($"📖 Loading config: {_configFilePath}");
                string json = File.ReadAllText(_configFilePath);
                var newRules = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ListAffinityRule) ?? new List<AffinityRule>();
                // 初始化规则
                foreach (var rule in newRules)
                {
                    rule.Initialize();
                }
                
                if (newRules.Count == 0)
                {
                    Console.WriteLine("❌ No valid rules in configuration");
                    return false;
                }
                
                _rules = newRules;
                PrintConfig();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error loading config: {ex.Message}");
                return false;
            }
        }
    }

    private void CreateDefaultConfig()
    {
        try
        {
            Console.WriteLine("🛠️ Creating default configuration...");
            var defaultRules = new List<AffinityRule>
            {
                new AffinityRule
                {
                    Type = RuleType.ProcessName,
                    Pattern = "example",
                    Cpus = new[] {0, 1}
                }
            };
            
            SaveConfig(defaultRules);
            Console.WriteLine($"✅ Created default config at: {_configFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create default config: {ex.Message}");
        }
    }

    private void SaveConfig(List<AffinityRule> rules)
    {
        string json = JsonSerializer.Serialize(rules, ConfigJsonContext.Default.ListAffinityRule);
        File.WriteAllText(_configFilePath, json);
    }

    public IReadOnlyList<AffinityRule> GetRules()
    {
        lock (_lock)
        {
            return new List<AffinityRule>(_rules);
        }
    }

    private void PrintConfig()
    {
        Console.WriteLine("Loaded configuration:");
        foreach (var rule in _rules)
        {
            string typeName = rule.Type switch {
                RuleType.ProcessName => "Name",
                RuleType.ExecutablePath => "Path",
                RuleType.CommandLine => "Command",
                _ => "Unknown"
            };
        
            string patternType = rule.IsRegex ? "Regex" : "Text";
            Console.WriteLine($"  {typeName} ({patternType}): {rule.Pattern} -> CPUs {string.Join(",", rule.Cpus)}");
        }
    }

    private void SetupConfigWatcher()
    {
        try
        {
            string configDir = Path.GetDirectoryName(_configFilePath) ?? "/etc";
            string configFile = Path.GetFileName(_configFilePath);
            
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
                Console.WriteLine($"📁 Created config directory: {configDir}");
            }
            
            _configWatcher = new FileSystemWatcher(configDir)
            {
                Filter = configFile,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            
            _configWatcher.Changed += OnConfigChanged;
            Console.WriteLine($"🔭 Watching config: {_configFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to setup config watcher: {ex.Message}");
        }
    }

    private void OnConfigChanged(object sender, FileSystemEventArgs e)
    {
        _reloadTimer?.Dispose();
        _reloadTimer = new Timer(_ => ReloadConfig(), null, ReloadDelay, Timeout.Infinite);
    }

    private void ReloadConfig()
    {
        try
        {
            Console.WriteLine("\n🔄 Reloading configuration...");
            if (LoadConfig())
            {
                Console.WriteLine("✅ Configuration reloaded successfully");
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

    public void Dispose()
    {
        _configWatcher?.Dispose();
        _reloadTimer?.Dispose();
    }
}