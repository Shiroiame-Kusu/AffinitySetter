using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

internal class AffinitySetter
{
    static byte[]? CpuMaskCache;

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Usage: AffinitySetter <cpu_list> <process_name_list>");
            return 1;
        }

        var cpus = ParseCpuList(args[0]);
        var targets = NormalizeTargets(args[1]);
        CpuMaskCache = BuildCpuMask(cpus);

        int success = 0, fail = 0;
        var failList = new List<(int Tid, int Pid, string Name)>();

        foreach (var pidStr in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(pidStr), out int pid)) continue;

            string statusPath = Path.Combine(pidStr, "status");
            if (!File.Exists(statusPath)) continue;

            string? processName = ReadNameFromStatus(statusPath);
            if (processName == null || !MatchesTarget(processName, targets)) continue;

            string taskPath = Path.Combine(pidStr, "task");
            if (!Directory.Exists(taskPath)) continue;

            foreach (var tidDir in Directory.EnumerateDirectories(taskPath))
            {
                if (!int.TryParse(Path.GetFileName(tidDir), out int tid)) continue;
                if (SetAffinity(tid))
                {
                    Console.WriteLine($"[SUCCESS] TID {tid} (PID {pid}, Name \"{processName}\") -> CPUs {string.Join(",", cpus)}");
                    success++;
                }
                else
                {
                    failList.Add((tid, pid, processName));
                    fail++;
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine($"[SUMMARY] Success: {success}, Failed: {fail}");

        if (fail > 0)
        {
            Console.WriteLine("[FAILED THREADS]");
            foreach (var (tid, pid, name) in failList)
                Console.WriteLine($"  TID {tid} (PID {pid}, Name \"{name}\")");
            return 1;
        }

        return 0;
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

    static HashSet<string> NormalizeTargets(string input)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(name);
        return set;
    }

    static bool MatchesTarget(string name, HashSet<string> targets)
    {
        foreach (var target in targets)
            if (name.Contains(target, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
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

    static bool SetAffinity(int tid)
    {
        int result = sched_setaffinity(tid, (IntPtr)CpuMaskCache!.Length, CpuMaskCache);
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
                    for (int i = start; i <= end; i++) result.Add(i);
            }
            else if (int.TryParse(part, out int cpu))
                result.Add(cpu);
        }
        return result.ToArray();
    }

    [DllImport("libc", SetLastError = true)]
    static extern int sched_setaffinity(int pid, IntPtr cpusetsize, byte[] mask);
}
