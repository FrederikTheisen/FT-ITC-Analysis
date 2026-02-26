using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;

namespace AnalysisITC
{
	public static class AppEventHandler
	{
		public static EventHandler<HandledException> ShowAppMessage;

		static List<LogEntry> Log { get; } = new List<LogEntry>();

		public static void AddLog(string msg)
		{
			Log.Add(new LogEntry(msg));
		}

        public static void AddLog(Exception ex)
        {
            Log.Add(new LogEntry(ex));
        }

		public static void PrintAndLog(string msg)
		{
			Console.WriteLine(msg);

			AddLog(msg);
        }

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