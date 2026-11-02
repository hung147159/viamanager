using System;
using System.Text.RegularExpressions;

namespace ViaManager.Utilities
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public record LogEntry(
        string Scope,
        LogLevel Level,
        string Message,
        DateTime Timestamp
    );

    public static class AppLogger
    {
        private static readonly object LockObj = new();
        public static event Action<LogEntry>? LogEmitted;

        public static void Log(string scope, LogLevel level, string message)
        {
            string sanitized = Sanitize(message);
            var entry = new LogEntry(scope, level, sanitized, DateTime.Now);

            lock (LockObj)
            {
                var color = level switch
                {
                    LogLevel.Error => ConsoleColor.Red,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Debug => ConsoleColor.DarkGray,
                    _ => ConsoleColor.Cyan
                };

                var orig = Console.ForegroundColor;
                try
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"[{entry.Timestamp:HH:mm:ss}] ");
                    Console.ForegroundColor = color;
                    Console.Write($"[{entry.Level.ToString().ToUpperInvariant()}] ");
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write($"[{entry.Scope}] ");
                    Console.ForegroundColor = color;
                    Console.WriteLine(entry.Message);
                }
                finally
                {
                    Console.ForegroundColor = orig;
                }
            }

            try
            {
                LogEmitted?.Invoke(entry);
            }
            catch { }
        }

        public static void Info(string scope, string message) => Log(scope, LogLevel.Info, message);
        public static void Warn(string scope, string message) => Log(scope, LogLevel.Warning, message);
        public static void Error(string scope, string message, Exception? ex = null)
        {
            string fullMsg = ex == null ? message : $"{message} | Ex: {ex.GetType().Name}: {ex.Message}";
            Log(scope, LogLevel.Error, fullMsg);
        }
        public static void Debug(string scope, string message) => Log(scope, LogLevel.Debug, message);

        /// <summary>
        /// Sanitizes message to prevent accidental logging of passwords, 2FA secrets, or cookie session tokens.
        /// </summary>
        private static string Sanitize(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            // Mask xs cookie tokens
            message = Regex.Replace(message, @"xs=[^;\s]+", "xs=***MASKED***");
            // Mask 2FA / code patterns
            message = Regex.Replace(message, @"(pass(?:word)?\s*[:=]\s*)([^\s,;]+)", "$1***", RegexOptions.IgnoreCase);
            message = Regex.Replace(message, @"(ma2fa|secret)\s*[:=]\s*([^\s,;]+)", "$1=***", RegexOptions.IgnoreCase);

            return message;
        }
    }
}
