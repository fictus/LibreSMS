using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreSMS.Models;

namespace LibreSMS.Services
{
    public class GatewayLogService
    {
        private static GatewayLogService? _instance;
        public static GatewayLogService Instance => _instance ??= new GatewayLogService();

        public ObservableCollection<LogEntry> Logs { get; } = new();
        public event EventHandler<LogEntry>? LogAdded;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private const int MaxLogs = 500;

        public void Log(string message, string level = "INFO")
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (Logs.Count >= MaxLogs)
                    Logs.RemoveAt(Logs.Count - 1);
                Logs.Insert(0, entry);
                LogAdded?.Invoke(this, entry);
            });

            System.Diagnostics.Debug.WriteLine($"[{level}] {message}");
        }

        public void Info(string msg) => Log(msg, "INFO");
        public void Error(string msg) => Log(msg, "ERROR");
        public void Warning(string msg) => Log(msg, "WARN");
        public void Success(string msg) => Log(msg, "OK");

        public void Clear()
        {
            MainThread.BeginInvokeOnMainThread(() => Logs.Clear());
        }
    }
}
