using System.Runtime.InteropServices;

namespace AffinitySetter.Utils;
internal static class CpuUtils
{
    public static byte[] BuildCpuMask(int[] cpus)
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


    [DllImport("libc", SetLastError = true)]
    public static extern int sched_setaffinity(int pid, IntPtr cpusetsize, byte[] mask);
    // In Utils/CpuUtils.cs
    [DllImport("libc", SetLastError = true)]
    public static extern int ioprio_set(int which, int who, int ioprio);
    // Utils/CpuUtils.cs
    [DllImport("libc", SetLastError = true)]
    public static extern int setpriority(int which, int who, int prio);
}
