using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AffinitySetter
{
    public static class CrashHandler
    {
        private static readonly object _logLock = new();
        private static string _logPath = "/var/log/AffinitySetter-CrashLogs";
        private static volatile bool _isShuttingDown = false;

        public static void Setup()
        {
            try
            {
                Directory.CreateDirectory(_logPath);
            }
            catch
            {
                Console.Error.WriteLine($"[CrashHandler] Failed to create log directory: {_logPath}");
            }

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (_isShuttingDown) return;

            var ex = e.ExceptionObject as Exception;
            LogCrash(ex, "UNHANDLED EXCEPTION", e.IsTerminating);

            // Prevent process exit: swallow exception and continue
            if (e.IsTerminating)
            {
                _isShuttingDown = false;
                // Optionally, restart main loop or notify user
                Console.Error.WriteLine("[CrashHandler] Exception caught, but process will NOT exit.");
                Thread.Sleep(1000); // Give time for logs to flush
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash(e.Exception, "UNOBSERVED TASK EXCEPTION", false);
            e.SetObserved(); // Prevent process crash
        }

        private static void LogCrash(Exception? ex, string type, bool isFatal)
        {
            lock (_logLock)
            {
                var timestamp = DateTime.Now;
                var crashFile = Path.Combine(_logPath, $"crash-{timestamp:yyyyMMdd}.log");

                try
                {
                    var message = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] {type} - Fatal: {isFatal}\n";
                    if (ex != null)
                    {
                        message += $"Exception: {ex.GetType().Name}\n";
                        message += $"Message: {ex.Message}\n";
                        message += $"Stack Trace:\n{ex.StackTrace}\n";
                        if (ex.InnerException != null)
                        {
                            message += $"\nInner Exception: {ex.InnerException.GetType().Name}\n";
                            message += $"Inner Message: {ex.InnerException.Message}\n";
                            message += $"Inner Stack:\n{ex.InnerException.StackTrace}\n";
                        }
                    }
                    message += new string('-', 80) + "\n\n";
                    File.AppendAllText(crashFile, message);
                    Console.Error.WriteLine(message);
                }
                catch
                {
                    Console.Error.WriteLine($"CRASH: {ex?.Message}");
                }
            }
        }

        public static void Log(string level, string message)
        {
            lock (_logLock)
            {
                var timestamp = DateTime.Now;
                var logMessage = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                Console.WriteLine(logMessage);
                try
                {
                    var logFile = Path.Combine(_logPath, $"affinity-setter-{timestamp:yyyyMMdd}.log");
                    File.AppendAllText(logFile, logMessage + "\n");
                }
                catch { }
            }
        }

        public static bool IsShuttingDown => _isShuttingDown;
    }
}