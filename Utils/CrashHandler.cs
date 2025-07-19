namespace AffinitySetter
{
    public static class CrashHandler
    {
        private static readonly object _logLock = new();
        private static string _logPath = "/var/log/AffinitySetter-CrashLogs";
        private static volatile bool _isShuttingDown = false;
        
        public static void Setup()
        {
            // Create log directory
            Console.WriteLine("[CrashHandler] Setting up crash handler...");
            try
            {
                Directory.CreateDirectory(_logPath);
            }
            catch
            {
                Console.Error.WriteLine($"[CrashHandler] Failed to create the fucking log directory: {_logPath}");
                Console.Error.WriteLine("Now CrashHandler is fucked, use this fucking program at your own risk!");
            }
            
            // Handle all unhandled exceptions
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            
            // Handle unobserved task exceptions
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            
            // Handle Ctrl+C gracefully
            //Console.CancelKeyPress += OnCancelKeyPress;
            
            // Handle process exit
            //AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (_isShuttingDown) return;
            
            var ex = e.ExceptionObject as Exception;
            LogCrash(ex, "FATAL CRASH", e.IsTerminating);
            
            if (e.IsTerminating)
            {
                // Give time for logs to flush
                Thread.Sleep(1000);
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogCrash(e.Exception, "UNOBSERVED TASK EXCEPTION", false);
            e.SetObserved(); // Prevent crash
        }

        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            _isShuttingDown = true;
            Log("INFO", "Shutdown requested (Ctrl+C)");
            e.Cancel = true; // Prevent immediate termination
        }

        private static void OnProcessExit(object? sender, EventArgs e)
        {
            _isShuttingDown = true;
            Log("INFO", "Process exiting");
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
                    
                    // Write to file
                    File.AppendAllText(crashFile, message);
                    
                    // Also write to stderr for systemd journal
                    Console.Error.WriteLine(message);
                }
                catch
                {
                    // Last resort - just print to console
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