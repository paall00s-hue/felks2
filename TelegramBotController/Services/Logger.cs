using System;
using System.IO;

namespace TelegramBotController.Services
{
    public static class Logger
    {
        // Use the explicit root path as requested by the user
        private static readonly string RootPath = @"C:\Users\saud\Desktop\البوت كامل من صتعي";
        private static readonly string ErrorLogPath = Path.Combine(RootPath, "bot_errors.log");
        private static readonly string EventLogPath = Path.Combine(RootPath, "bot_events.log");

        static Logger()
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(RootPath))
                {
                    Directory.CreateDirectory(RootPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL] Failed to initialize logger path: {ex.Message}");
            }
        }

        public static void LogError(string message, Exception? ex = null)
        {
            try
            {
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [ERROR] {message}";
                if (ex != null)
                {
                    logContent += $"\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
                }
                logContent += "\n--------------------------------------------------\n";
                
                File.AppendAllText(ErrorLogPath, logContent);
                
                // Also write to console
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {message}");
                if (ex != null) Console.WriteLine($"Exception: {ex.Message}");
                Console.ForegroundColor = originalColor;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write to error log: {e.Message}");
            }
        }

        public static void LogEvent(string message)
        {
            try
            {
                string logContent = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [EVENT] {message}\n";
                File.AppendAllText(EventLogPath, logContent);
                
                // Optional: Console output for events if needed, but keeping it clean for now
                // Console.WriteLine($"[EVENT] {message}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to write to event log: {e.Message}");
            }
        }
    }
}
