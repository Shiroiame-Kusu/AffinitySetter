namespace AffinitySetter;
public record TargetRule(string NamePattern, byte[] CpuMask);

public static class ConfigLoader
{
    public static string ConfigPath
    {
        get
        {
            string userPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "AffinitySetter.conf");

            if (File.Exists(userPath)) return userPath;
            return "/etc/AffinitySetter.conf";
        }
    }

    public static List<TargetRule> Load()
    {
        var rules = new List<TargetRule>();
        if (!File.Exists(ConfigPath))
        {
            WriteDefault(ConfigPath);
            Console.WriteLine($"[INFO] Default config created at: {ConfigPath}");
            Console.WriteLine("[INFO] Please edit the file or use CLI commands.");
            Environment.Exit(0);
        }

        foreach (var line in File.ReadAllLines(ConfigPath))
        {
            var clean = line.Trim();
            if (string.IsNullOrEmpty(clean) || clean.StartsWith('#')) continue;

            var parts = clean.Split(':', 2);
            if (parts.Length != 2) continue;

            string name = parts[0].Trim();
            var cpus = ParseCpuList(parts[1]);
            if (cpus.Length > 0)
                rules.Add(new TargetRule(name, AffinityHelper.BuildMask(cpus)));
        }

        return rules;
    }

    public static void SaveOrUpdateRule(string cpuList, string match)
    {
        string path = ConfigPath;
        string newLine = $"{match}:{cpuList}";

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, newLine + Environment.NewLine);
            return;
        }

        var lines = File.ReadAllLines(path).ToList();
        bool replaced = false;

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;

            var parts = line.Split(':', 2);
            if (parts.Length == 2 && parts[0].Trim().Equals(match, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = newLine;
                replaced = true;
                break;
            }
        }

        if (!replaced) lines.Add(newLine);
        File.WriteAllLines(path, lines);
    }

    public static void RemoveRule(string match)
    {
        if (!File.Exists(ConfigPath))
        {
            Console.WriteLine("No config found.");
            return;
        }

        var lines = File.ReadAllLines(ConfigPath).ToList();
        int removed = lines.RemoveAll(line =>
            !string.IsNullOrWhiteSpace(line) &&
            !line.StartsWith('#') &&
            line.Split(':', 2)[0].Trim().Equals(match, StringComparison.OrdinalIgnoreCase));

        File.WriteAllLines(ConfigPath, lines);
        Console.WriteLine(removed > 0 ? $"Removed {removed} rule(s)." : "No matching rule found.");
    }

    public static void ListRules()
    {
        if (!File.Exists(ConfigPath))
        {
            Console.WriteLine("No config found.");
            return;
        }

        Console.WriteLine($"📄 Current Rules ({ConfigPath}):");
        foreach (var line in File.ReadLines(ConfigPath))
        {
            var clean = line.Trim();
            if (string.IsNullOrEmpty(clean)) continue;

            Console.WriteLine(clean);
        }
    }

    private static void WriteDefault(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, """
# AffinitySetter configuration
# Format: process_substring:cpu_list
# Example:
# msedge:8-15
# CrRenderer:4-7
# steamwebhelper:0-3
""");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ERROR] Failed to write config: {ex.Message}");
            Environment.Exit(1);
        }
    }

    private static int[] ParseCpuList(string input)
    {
        var result = new List<int>();
        var parts = input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                var bounds = part.Split('-');
                if (bounds.Length == 2 &&
                    int.TryParse(bounds[0], out int start) &&
                    int.TryParse(bounds[1], out int end))
                {
                    for (int i = start; i <= end; i++)
                        result.Add(i);
                }
            }
            else if (int.TryParse(part, out int cpu))
            {
                result.Add(cpu);
            }
        }
        return result.ToArray();
    }
}
