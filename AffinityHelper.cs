using System.Runtime.InteropServices;

namespace AffinitySetter;

public static class AffinityHelper
{
    [DllImport("libc", SetLastError = true)]
    static extern int sched_setaffinity(int pid, IntPtr cpusetsize, byte[] mask);

    public static byte[] BuildMask(int[] cpus)
    {
        var mask = new byte[128];
        foreach (var cpu in cpus)
        {
            if (cpu < 1024)
                mask[cpu / 8] |= (byte)(1 << (cpu % 8));
        }
        return mask;
    }

    public static bool SetAffinity(int tid, byte[] mask)
    {
        return sched_setaffinity(tid, (IntPtr)mask.Length, mask) == 0;
    }
}
