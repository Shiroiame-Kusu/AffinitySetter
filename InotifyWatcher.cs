using System.Runtime.InteropServices;
using System.Text;

namespace AffinitySetter;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public static class InotifyWatcher
{
    [DllImport("libc", SetLastError = true)] static extern int inotify_init1(int flags);
    [DllImport("libc", SetLastError = true)] static extern int inotify_add_watch(int fd, string pathname, uint mask);
    [DllImport("libc", SetLastError = true)] static extern int read(int fd, byte[] buffer, int count);

    const uint IN_CREATE = 0x00000100;
    static Dictionary<int, TargetRule> pidToRule = new();

    public static void Watch(List<TargetRule> rules)
    {
        int fd = inotify_init1(0);
        if (fd < 0)
        {
            Console.WriteLine("❌ inotify_init1 failed");
            return;
        }

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
                    if (Directory.Exists(taskPath))
                    {
                        inotify_add_watch(fd, taskPath, IN_CREATE);
                        pidToRule[pid] = rule;
                    }
                }
            }
        }

        // 监听 loop
        Task.Run(() =>
        {
            var buffer = new byte[4096];
            while (true)
            {
                int len = read(fd, buffer, buffer.Length);
                if (len < 0) continue;

                int offset = 0;
                while (offset + 16 <= len)
                {
                    int nameLen = BitConverter.ToInt32(buffer, offset + 16);
                    string name = Encoding.UTF8.GetString(buffer, offset + 20, nameLen).TrimEnd('\0');

                    int tidStart = offset + 16 + 4;
                    if (int.TryParse(name, out int tid))
                    {
                        string tidPath = $"/proc/{tid}";
                        if (Directory.Exists(tidPath))
                        {
                            int pid = GetParentPid(tid);
                            if (pidToRule.TryGetValue(pid, out var rule))
                            {
                                WorkerPool.Enqueue(tid, rule.CpuMask);
                            }
                        }
                    }
                    offset += 16 + nameLen + 4;
                }
            }
        });
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

    private static int GetParentPid(int tid)
    {
        try
        {
            string path = $"/proc/{tid}/status";
            foreach (var line in File.ReadLines(path))
                if (line.StartsWith("Tgid:")) return int.Parse(line.Substring(5).Trim());
        }
        catch { }
        return tid;
    }
}

