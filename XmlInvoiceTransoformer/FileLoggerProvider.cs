using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace XmlInvoiceTransformer
{
    public class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _logFolder;
        private readonly object _lock = new();

        public FileLoggerProvider(string logFolder)
        {
            _logFolder = logFolder;
            Directory.CreateDirectory(_logFolder);
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName, _logFolder, _lock);
        }

        public void Dispose()
        {

        }
    }

    public class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly string _logFolder;
        private readonly object _lock;

        public FileLogger(string categoryName, string logFolder, object lockObj)
        {
            _categoryName = categoryName;
            _logFolder = logFolder;
            _lock = lockObj;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var logFileName = $"InvoiceTransformer_{DateTime.Now:yyyy-MM-dd}.log";
            var logFilePath = Path.Combine(_logFolder, logFileName);

            var logLevelShort = logLevel switch
            {
                LogLevel.Trace => "TRC",
                LogLevel.Debug => "DBG",
                LogLevel.Information => "INF",
                LogLevel.Warning => "WRN",
                LogLevel.Error => "ERR",
                LogLevel.Critical => "CRT",
                _ => "???"
            };

            var message = formatter(state, exception);
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{logLevelShort}] {message}";

            if (exception != null)
            {
                logEntry += Environment.NewLine + $" Exception: {exception.Message}";
                logEntry += Environment.NewLine + $" StackTrace: {exception.StackTrace}";
            }

            lock (_lock)
            {
                File.AppendAllText(logFilePath, logEntry + Environment.NewLine);
            }
        }
    }
}
