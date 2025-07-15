
namespace AffinitySetter.Utils;
internal sealed class AffinityRule
{
    public string Name { get; }
    public int[] Cpus { get; }
    public byte[] Mask { get; }

    public AffinityRule(string name, int[] cpus)
    {
        Name = name;
        Cpus = cpus;
        Mask = CpuUtils.BuildCpuMask(cpus);
    }

    public bool Apply(int tid)
    {
        int result = CpuUtils.sched_setaffinity(tid, (IntPtr)Mask.Length, Mask);
        return result == 0;
    }
}