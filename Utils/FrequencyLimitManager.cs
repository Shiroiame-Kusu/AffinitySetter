namespace AffinitySetter.Utils;

internal sealed class FrequencyLimitManager
{
    private readonly Dictionary<int, CpuFrequencyState> _defaults = new();
    private readonly HashSet<int> _unsupportedCpus = new();
    private readonly object _lock = new();
    private bool _loggedMissingCpufreq;

    public void ApplyLimits(IReadOnlyList<CoreFrequencyLimit> limits)
    {
        lock (_lock)
        {
            EnsureDefaultStates();
            if (_defaults.Count == 0)
            {
                if (!_loggedMissingCpufreq)
                {
                    Console.WriteLine("ℹ️ No cpufreq sysfs entries available, skipping frequency limits.");
                    _loggedMissingCpufreq = true;
                }
                return;
            }

            var targets = _defaults.ToDictionary(
                pair => pair.Key,
                pair => new FrequencyTarget(pair.Value.DefaultMinKHz, pair.Value.DefaultMaxKHz));

            foreach (var limit in limits)
            {
                if (!limit.HasLimits)
                {
                    continue;
                }

                foreach (var cpu in limit.Cpus)
                {
                    if (!_defaults.TryGetValue(cpu, out var state))
                    {
                        if (_unsupportedCpus.Add(cpu))
                        {
                            Console.WriteLine($"⚠️ CPU{cpu} does not expose cpufreq controls, skipping frequency limit.");
                        }
                        continue;
                    }

                    var currentTarget = targets[cpu];
                    long targetMin = limit.MinFrequencyKHz ?? currentTarget.MinKHz;
                    long targetMax = limit.MaxFrequencyKHz ?? currentTarget.MaxKHz;

                    if (targetMin < state.HardwareMinKHz || targetMax > state.HardwareMaxKHz)
                    {
                        Console.WriteLine($"⚠️ CPU{cpu} frequency limit [{targetMin}, {targetMax}] is outside supported range [{state.HardwareMinKHz}, {state.HardwareMaxKHz}] kHz, skipping.");
                        continue;
                    }

                    if (targetMin > targetMax)
                    {
                        Console.WriteLine($"⚠️ CPU{cpu} minfreq {targetMin} is greater than maxfreq {targetMax}, skipping.");
                        continue;
                    }

                    targets[cpu] = new FrequencyTarget(targetMin, targetMax);
                }
            }

            foreach (var pair in targets.OrderBy(pair => pair.Key))
            {
                ApplyTarget(pair.Key, pair.Value);
            }
        }
    }

    private void EnsureDefaultStates()
    {
        if (_defaults.Count > 0)
        {
            return;
        }

        foreach (var cpu in GetCandidateCpus())
        {
            if (TryReadCpuFrequencyState(cpu, out var state))
            {
                _defaults[cpu] = state;
            }
        }
    }

    private static IEnumerable<int> GetCandidateCpus()
    {
        var topologyCpus = CpuTopology.Instance.AllCpus;
        if (topologyCpus.Length > 0)
        {
            return topologyCpus;
        }

        return Enumerable.Range(0, Environment.ProcessorCount);
    }

    private static bool TryReadCpuFrequencyState(int cpu, out CpuFrequencyState state)
    {
        string basePath = $"/sys/devices/system/cpu/cpu{cpu}/cpufreq";
        if (!Directory.Exists(basePath))
        {
            state = default;
            return false;
        }

        string scalingMinPath = Path.Combine(basePath, "scaling_min_freq");
        string scalingMaxPath = Path.Combine(basePath, "scaling_max_freq");
        if (!TryReadLong(scalingMinPath, out long defaultMin) || !TryReadLong(scalingMaxPath, out long defaultMax))
        {
            state = default;
            return false;
        }

        string cpuInfoMinPath = Path.Combine(basePath, "cpuinfo_min_freq");
        string cpuInfoMaxPath = Path.Combine(basePath, "cpuinfo_max_freq");
        long hardwareMin = TryReadLong(cpuInfoMinPath, out long minValue) ? minValue : defaultMin;
        long hardwareMax = TryReadLong(cpuInfoMaxPath, out long maxValue) ? maxValue : defaultMax;

        state = new CpuFrequencyState(
            scalingMinPath,
            scalingMaxPath,
            hardwareMin,
            hardwareMax,
            defaultMin,
            defaultMax);
        return true;
    }

    private void ApplyTarget(int cpu, FrequencyTarget target)
    {
        if (!_defaults.TryGetValue(cpu, out var state))
        {
            return;
        }

        if (!TryReadLong(state.ScalingMinPath, out long currentMin) || !TryReadLong(state.ScalingMaxPath, out long currentMax))
        {
            Console.WriteLine($"⚠️ Unable to read current cpufreq state for CPU{cpu}, skipping.");
            return;
        }

        if (currentMin == target.MinKHz && currentMax == target.MaxKHz)
        {
            return;
        }

        try
        {
            if (currentMin > target.MaxKHz)
            {
                WriteFrequency(state.ScalingMinPath, target.MinKHz);
                WriteFrequency(state.ScalingMaxPath, target.MaxKHz);
            }
            else if (currentMax < target.MinKHz)
            {
                WriteFrequency(state.ScalingMaxPath, target.MaxKHz);
                WriteFrequency(state.ScalingMinPath, target.MinKHz);
            }
            else
            {
                if (currentMax != target.MaxKHz)
                {
                    WriteFrequency(state.ScalingMaxPath, target.MaxKHz);
                }

                if (currentMin != target.MinKHz)
                {
                    WriteFrequency(state.ScalingMinPath, target.MinKHz);
                }
            }

            Console.WriteLine($"🎛️ CPU{cpu} frequency limit applied: min={target.MinKHz} max={target.MaxKHz} kHz");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Failed to apply CPU{cpu} frequency limit: {ex.Message}");
        }
    }

    private static bool TryReadLong(string path, out long value)
    {
        try
        {
            string content = File.ReadAllText(path).Trim();
            return long.TryParse(content, out value);
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static void WriteFrequency(string path, long value)
    {
        File.WriteAllText(path, value.ToString());
    }

    private readonly record struct CpuFrequencyState(
        string ScalingMinPath,
        string ScalingMaxPath,
        long HardwareMinKHz,
        long HardwareMaxKHz,
        long DefaultMinKHz,
        long DefaultMaxKHz);

    private readonly record struct FrequencyTarget(long MinKHz, long MaxKHz);
}