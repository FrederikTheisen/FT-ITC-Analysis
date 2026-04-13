using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AppKit;

namespace AnalysisITC
{
	public static class AppEventHandler
	{
		public static EventHandler<HandledException> ShowAppMessage;

		static readonly object LogLock = new object();
		static List<LogEntry> Log { get; } = new List<LogEntry>();

		public static void AddLog(string msg)
		{
			lock (LogLock)
			{
				Log.Add(new LogEntry(msg));

				if (Log.Count > 1023) Log.RemoveAt(0);
			}
		}

        public static void AddLog(Exception ex)
        {
			lock (LogLock)
			{
				Log.Add(new LogEntry(ex));

				if (Log.Count > 10000) Log.RemoveAt(0);
			}
        }

		public static void PrintAndLog(string msg, int indentation = 0, string code = null)
		{
            Print(msg, indentation);

			if (code == null) AddLog(msg);
			else AddLog($"[{code}] {msg}");
        }

		public static void Print(string msg, int indentation = 0) => Console.WriteLine($"{new string(' ', 2*indentation)}{msg}");

		public static void DisplayHandledException(Exception ex)
		{
			AddLog(ex);

			var stacktrace = ex.StackTrace?.Split(Environment.NewLine) ?? new[] { "" };

            Console.WriteLine(ex.Message);
            foreach (var line in stacktrace) Console.WriteLine(line);

			switch (ex)
			{
				case HandledException:
					NSApplication.SharedApplication.InvokeOnMainThread(() =>
						{
							ShowAppMessage?.Invoke(null, ex as HandledException);
							StatusBarManager.ClearAppStatus();
						});
					break;
                case OptimizerStopException: break;
                case AggregateException: NSApplication.SharedApplication.InvokeOnMainThread(() => { ShowAppMessage?.Invoke(null, new(ex.InnerException)); StatusBarManager.ClearAppStatus(); }); break;
				default: NSApplication.SharedApplication.InvokeOnMainThread(() => { ShowAppMessage?.Invoke(null, new(ex)); StatusBarManager.ClearAppStatus(); }); break;
            }
        }

		public static string GetLogReport()
		{
			lock (LogLock)
			{
				var builder = new StringBuilder();

				foreach (var entry in Log)
				{
					builder.Append('[');
					builder.Append(entry.DateTime.ToString("yyyy-MM-dd HH:mm:ss"));
					builder.Append("] ");
					builder.AppendLine(entry.Message);

					if (entry.Exception == null) continue;

					builder.Append("Exception: ");
					builder.AppendLine(entry.Exception.GetType().FullName);

					if (!string.IsNullOrWhiteSpace(entry.Exception.StackTrace))
					{
						builder.AppendLine(entry.Exception.StackTrace);
					}

					builder.AppendLine();
				}

				if (builder.Length == 0)
				{
					builder.AppendLine("No log entries recorded.");
				}

				return builder.ToString();
			}
		}

		public static string GetRecentLogSummary(int maxEntries = 12, int maxMessageLength = 160)
		{
			lock (LogLock)
			{
				var builder = new StringBuilder();
				var entries = Log.TakeLast(Math.Max(0, maxEntries));

				foreach (var entry in entries)
				{
					builder.Append("- ");
					builder.Append(entry.DateTime.ToString("HH:mm:ss"));
					builder.Append("  ");
					builder.AppendLine(Compact(entry.Message, maxMessageLength));
				}

				if (builder.Length == 0)
				{
					builder.AppendLine("- No recent log entries.");
				}

				return builder.ToString();
			}
		}

		static string Compact(string value, int maxLength)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return "(empty)";
			}

			var compact = string.Join(" ", value
				.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
				.Trim();

			if (compact.Length <= maxLength)
			{
				return compact;
			}

			return compact.Substring(0, Math.Max(0, maxLength - 3)) + "...";
		}
	}

	public class LogEntry
	{
		public string Message { get; private set; }
		public DateTime DateTime { get; private set; }
		public Exception Exception { get; private set; }

		public LogEntry(string msg)
		{
			Message = msg;
			DateTime = DateTime.Now;
		}

        public LogEntry(Exception ex)
        {
            Message = ex.Message;
            DateTime = DateTime.Now;
			Exception = ex;
        }
    }

	public class HandledException : Exception
	{
		public Severity Level { get; private set; } = Severity.Message;
		public string Title { get; private set; } = "Error";

		public HandledException(Exception ex) : base(ex.Message)
		{
			Level = Severity.Error;

			Title = ex.GetType().ToString().Split('.').Last();
        }

		public HandledException(Severity level, string title, string message) : base(message)
		{
			Level = level;
			Title = title;
		}

		public enum Severity
		{
			Error,
			Warning,
			Message,
		}
	}

	public class OptimizerStopException : Exception
    {
        public HandledException.Severity Level { get; } = HandledException.Severity.Warning;
        public string Title { get; } = "Optimizer Was Stopped";

        public OptimizerStopException() : base("The optimization was stopped by the user")
		{

        }
	}
}
