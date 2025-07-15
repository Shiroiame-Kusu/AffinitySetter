using AffinitySetter.Utils;

namespace AffinitySetter.Config;
internal sealed class ConfigManager : IDisposable
    {
        private readonly string _configFilePath;
        private Dictionary<string, AffinityRule> _rules = new(StringComparer.OrdinalIgnoreCase);
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
                    var newRules = new Dictionary<string, AffinityRule>(StringComparer.OrdinalIgnoreCase);
                    
                    if (!File.Exists(_configFilePath))
                    {
                        Console.WriteLine($"❌ Config file not found: {_configFilePath}");
                        return false;
                    }
                    
                    foreach (var line in File.ReadLines(_configFilePath))
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
                        var cpus = CpuUtils.ParseCpuList(parts[1].Trim());
                        newRules[name] = new AffinityRule(name, cpus);
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

        public IReadOnlyDictionary<string, AffinityRule> GetRules()
        {
            lock (_lock)
            {
                return new Dictionary<string, AffinityRule>(_rules);
            }
        }

        private void PrintConfig()
        {
            Console.WriteLine("Loaded configuration:");
            foreach (var rule in _rules)
                Console.WriteLine($"  {rule.Key}: {string.Join(",", rule.Value.Cpus)}");
        }

        private void SetupConfigWatcher()
        {
            try
            {
                string configDir = Path.GetDirectoryName(_configFilePath);
                string configFile = Path.GetFileName(_configFilePath);
                
                if (!Directory.Exists(configDir))
                {
                    Console.WriteLine($"⚠️ Config directory not found: {configDir}");
                    return;
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
        
    

