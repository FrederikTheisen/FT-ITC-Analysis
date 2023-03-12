using System;
using System.Collections.Generic;
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

		public static void Out(string msg)
		{
			Console.WriteLine(msg);

			AddLog(msg);
        }

        public static void DisplayHandledException(Exception ex)
		{
			AddLog(ex);

			var stacktrace = ex.StackTrace.Split(Environment.NewLine);

			foreach (var line in stacktrace) Console.WriteLine(line);

			switch (ex)
			{
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

	public class HandledException
	{
		public Severity Level { get; private set; } = Severity.Message;
		public string Title { get; private set; } = "Error";
		public string Message { get; private set; } = "An error occured";

		public HandledException(Exception ex)
		{
			Level = Severity.Error;

			Title = ex.GetType().ToString();
			Message = ex.Message;
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

	}
}