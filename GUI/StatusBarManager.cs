using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace AnalysisITC
{
    public static class StatusBarManager
    {
        static readonly StatusMessage DefaultStatus = new StatusMessage("FT ITC-Analysis", false);

        private static readonly List<StatusMessage> status = new();
        private static string secondarystatus = "";
        private static double progress = 0;
        private static bool abortscroll;
        private static ProgressIndicatorEventData progressstate = new ProgressIndicatorEventData(1);

        public static event EventHandler UpdateContextButton;
        public static event EventHandler<string> StatusUpdated;
        public static event EventHandler<string> SecondaryStatusUpdated;
        public static event EventHandler<ProgressIndicatorEventData> ProgressUpdate;

        private static StatusMessage Status
        {
            get
            {
                if (status.Count != 0) return status.Last();
                else return DefaultStatus;
            }
            set
            {
                status.RemoveAll(o => o.Timed == false);
                status.Add(value);

                StatusUpdated?.Invoke(null, Status.Message);
            }
        }

        public static string SecondaryStatus
        {
            get => secondarystatus;
            private set
            {
                secondarystatus = value;

                SecondaryStatusUpdated?.Invoke(null, secondarystatus);
            }
        }

        public static double Progress
        {
            set
            {
                progressstate.Progress = value;

                ProgressUpdate?.Invoke(null, progressstate);
            }
        }

        public static async void SetStatus(string _status, int delay = 10000)
        {
            abortscroll = true;

            if (delay > 0)
            {
                var sm = new StatusMessage(_status, true);

                Status = sm;

                await Task.Delay(delay);

                StatusExpired(sm);
            }
            else
            {
                var sm = new StatusMessage(_status, false);

                Status = sm;
            }
        }

        private static void StatusExpired(StatusMessage _status)
        {
            if (status.Contains(_status))
            {
                status.Remove(_status);
                StatusUpdated?.Invoke(null, Status.Message);
            }
        }

        public static void ClearAppStatus()
        {
            status.Clear();
            StatusUpdated?.Invoke(null, Status.Message);
        }

        public static async void SetStatusScrolling(string status)
        {
            var tmp = status;

            abortscroll = false;

            for (int i = 0; i < 3; i++)
            {
                tmp = status;

                Status = new StatusMessage(tmp, false);
                await Task.Delay(2000);

                while (tmp.Length > 20)
                {
                    Status = new StatusMessage(tmp, false);
                    tmp = tmp.Substring(1);

                    await Task.Delay(100);

                    if (abortscroll) break;
                }

                if (abortscroll) break;
                await Task.Delay(2000);
                if (abortscroll) break;
            }
        }

        public static async void SetSecondaryStatus(string status, int delay = 20000)
        {
            SecondaryStatus = status;

            string c = status;

            if (delay > 0)
            {
                await Task.Delay(delay);

                if (SecondaryStatus == c) SecondaryStatus = "";
            }
        }

        public static void UpdateContext() => UpdateContextButton?.Invoke(null, null);

        public static void StartInderminateProgress()
        {
            Progress = -0.5;
        }

        public static void StopInderminateProgress()
        {
            Progress = -1;
        }

        private struct StatusMessage
        {
            public StatusMessage(string _status, bool _timed)
            {
                Message = _status;
                Timed = _timed;
            }

            public string Message { get; set; }
            public bool Timed { get; set; }
        }
    }

    public struct ProgressIndicatorEventData
    {
        public double Progress { get; set; }

        public bool InDeterminate => this.Progress < 0;
        public bool HideProgressWheel => IsProgressFinished && InDeterminate;
        public bool IsProgressFinished => Math.Abs(Math.Abs(Progress) - 1) < double.Epsilon;
        

        public ProgressIndicatorEventData(double progress)
        {
            Progress = progress;
        }
    }
}
