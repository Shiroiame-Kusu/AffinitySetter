using System.Text.Json;
using AffinitySetter.Type;
using AffinitySetter.Utils;

#pragma warning disable CS8618
namespace AffinitySetter.Config;

internal sealed class ConfigManager : IDisposable
{
    private readonly string _configFilePath;
    private AppConfig _config = new();
    private FileSystemWatcher? _configWatcher;
    private Timer? _reloadTimer;
    private readonly object _lock = new();
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
                var newConfig = DeserializeConfig(json);
                newConfig.Initialize();

                if (!newConfig.HasContent)
                {
                    Console.WriteLine("❌ No valid rules or frequency limits in configuration");
                    return false;
                }

                _config = newConfig;
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
            var defaultConfig = new AppConfig
            {
                Rules = new List<AffinityRule>
                {
                    new()
                    {
                        Type = RuleType.ProcessName,
                        Pattern = "example",
                        CpusRaw = new[] { 0, 1 }
                    }
                }
            };

            SaveConfig(defaultConfig);
            Console.WriteLine($"✅ Created default config at: {_configFilePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Failed to create default config: {ex.Message}");
        }
    }

    private static AppConfig DeserializeConfig(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind switch
        {
            JsonValueKind.Array => new AppConfig
            {
                Rules = JsonSerializer.Deserialize(json, ConfigJsonContext.Default.ListAffinityRule) ?? new List<AffinityRule>()
            },
            JsonValueKind.Object => JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AppConfig) ?? new AppConfig(),
            _ => new AppConfig()
        };
    }

    public void SaveConfig(AppConfig config)
    {
        lock (_lock)
        {
            string json = JsonSerializer.Serialize(config, ConfigJsonContext.Default.AppConfig);
            File.WriteAllText(_configFilePath, json);
        }
    }

    public void SaveRules(IReadOnlyList<AffinityRule> rules)
    {
        lock (_lock)
        {
            var config = new AppConfig
            {
                Rules = rules.Select(CloneRule).ToList(),
                FrequencyLimits = _config.FrequencyLimits.Select(CloneFrequencyLimit).ToList()
            };
            SaveConfig(config);
        }
    }

    public IReadOnlyList<AffinityRule> GetRules()
    {
        lock (_lock)
        {
            return _config.Rules.Select(CloneRule).ToList();
        }
    }

    public IReadOnlyList<CoreFrequencyLimit> GetFrequencyLimits()
    {
        lock (_lock)
        {
            return _config.FrequencyLimits.Select(CloneFrequencyLimit).ToList();
        }
    }

    private void PrintConfig()
    {
        Console.WriteLine("Loaded configuration:");
        foreach (var rule in _config.Rules)
        {
            string typeName = rule.Type switch
            {
                RuleType.ProcessName => "Name",
                RuleType.ExecutablePath => "Path",
                RuleType.CommandLine => "Command",
                _ => "Unknown"
            };

            string patternType = rule.IsRegex ? "Regex" : "Text";
            Console.WriteLine($"  {typeName} ({patternType}): {rule.Pattern} -> CPUs {string.Join(",", rule.Cpus)}");
        }

        foreach (var limit in _config.FrequencyLimits)
        {
            string minText = limit.MinFrequencyKHz?.ToString() ?? "default";
            string maxText = limit.MaxFrequencyKHz?.ToString() ?? "default";
            Console.WriteLine($"  Freq: CPUs {string.Join(",", limit.Cpus)} -> min {minText} max {maxText} kHz");
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
        if (_reloadTimer == null)
        {
            _reloadTimer = new Timer(_ => ReloadConfig(), null, ReloadDelay, Timeout.Infinite);
        }
        else
        {
            _reloadTimer.Change(ReloadDelay, Timeout.Infinite);
        }
    }

    private void ReloadConfig()
    {
        try
        {
            Console.WriteLine("\n🔄 Reloading configuration...");
            List<AffinityRule> oldRules;
            List<CoreFrequencyLimit> oldFrequencyLimits;
            lock (_lock)
            {
                oldRules = _config.Rules.Select(CloneRule).ToList();
                oldFrequencyLimits = _config.FrequencyLimits.Select(CloneFrequencyLimit).ToList();
            }

            if (LoadConfig())
            {
                Console.WriteLine("✅ Configuration reloaded successfully");
                RulesChanged?.Invoke(oldRules, GetRules());
                FrequencyLimitsChanged?.Invoke(oldFrequencyLimits, GetFrequencyLimits());
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

    public event Action<IReadOnlyList<AffinityRule>, IReadOnlyList<AffinityRule>>? RulesChanged;
    public event Action<IReadOnlyList<CoreFrequencyLimit>, IReadOnlyList<CoreFrequencyLimit>>? FrequencyLimitsChanged;

    private static AffinityRule CloneRule(AffinityRule rule)
    {
        var clone = new AffinityRule
        {
            Type = rule.Type,
            Pattern = rule.Pattern,
            CpusRaw = rule.CpusRaw,
            IoPriorityClass = rule.IoPriorityClass,
            IoPriorityData = rule.IoPriorityData,
            Nice = rule.Nice
        };
        clone.Initialize();
        return clone;
    }

    private static CoreFrequencyLimit CloneFrequencyLimit(CoreFrequencyLimit limit)
    {
        var clone = new CoreFrequencyLimit
        {
            CpusRaw = limit.CpusRaw,
            MinFrequencyKHz = limit.MinFrequencyKHz,
            MaxFrequencyKHz = limit.MaxFrequencyKHz
        };
        clone.Initialize();
        return clone;
    }

    public void Dispose()
    {
        _configWatcher?.Dispose();
        _reloadTimer?.Dispose();
    }
}