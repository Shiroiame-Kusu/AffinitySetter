using System;
using System.Threading;
using AffinitySetter.Config;
using AffinitySetter.Utils;
namespace AffinitySetter;
internal class AffinitySetter
{
    static bool running = true;
    
    public static int Main(string[] args)
    {
        Console.CancelKeyPress += (sender, e) => 
        {
            running = false;
            e.Cancel = true;
            Console.WriteLine("\nExiting...");
        };
        
        Console.WriteLine("🌀 AffinitySetter Starting...");
        
        // 初始化配置管理器
        var configManager = new ConfigManager("/etc/AffinitySetter.conf");
        if (!configManager.LoadConfig())
        {
            Console.WriteLine("❌ No valid rules found. Exiting.");
            return 1;
        }
        
        // 初始化线程扫描器
        var threadScanner = new ThreadScanner(configManager);
        
        // 主循环
        while (running)
        {
            threadScanner.ScanProcesses();
            Thread.Sleep(1000);
        }
        
        return 0;
    }
}


