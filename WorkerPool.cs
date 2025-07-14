using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace AffinitySetter;

public static class WorkerPool
{
    private static readonly Channel<(int Tid, byte[] Mask)> _queue = Channel.CreateUnbounded<(int, byte[])>();

    public static void Start(int workerCount = 4)
    {
        for (int i = 0; i < workerCount; i++)
        {
            _ = Task.Run(async () =>
            {
                await foreach (var (tid, mask) in _queue.Reader.ReadAllAsync())
                {
                    if (AffinityHelper.SetAffinity(tid, mask))
                        Console.WriteLine($"[OK] TID {tid} bound.");
                    else
                        Console.WriteLine($"[FAIL] TID {tid} errno={Marshal.GetLastWin32Error()}");
                }
            });
        }
    }

    public static void Enqueue(int tid, byte[] mask) => _queue.Writer.TryWrite((tid, mask));
}
