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
                if (status.Count != 0) return status.OrderBy(sm => sm.Priority).Last();
                else return DefaultStatus;
            }
            set
            {
                status.RemoveAll(o => o.Timed == false && o.Priority < value.Priority);
                status.Add(value);

                StatusUpdated?.Invoke(null, Status.Message);
            }
        }

        static string SecondaryStatus
        {
            get => secondarystatus;
            set
            {
                secondarystatus = value;

                SecondaryStatusUpdated?.Invoke(null, secondarystatus);
            }
        }

        public static double Progress
        {
            private set
            {
                progressstate.Progress = value;

                ProgressUpdate?.Invoke(null, progressstate);
            }
            get
            {
                return progressstate.Progress;
            }
        }

        public static async void SetStatus(string msg, int delay = 10000, int priority = 0)
        {
            abortscroll = true;

            if (delay > 0)
            {
                var sm = new StatusMessage(msg, true, priority);

                Status = sm;

                await Task.Delay(delay);

                StatusExpired(sm);
            }
            else
            {
                Status = new StatusMessage(msg, false, priority);
            }
        }

        private static void StatusExpired(StatusMessage sm)
        {
            if (status.Contains(sm))
            {
                status.Remove(sm);
                StatusUpdated?.Invoke(null, Status.Message);
            }
        }

        public static void ClearAppStatus()
        {
            var priority = Status.Priority;
            status.RemoveAll(s => s.Priority >= priority);
            StopIndeterminateProgress();
            StatusUpdated?.Invoke(null, Status.Message);

            SecondaryStatus = "";
        }

        public static void SetProgress(double progress)
        {
            Progress = progress;
        }

        public static async void SetStatusScrolling(string status, int scrollcount = 2, int scrollspeed = 10)
        {
            var tmp = status;

            abortscroll = false;

            for (int i = 0; i < scrollcount; i++)
            {
                tmp = status;

                Status = new StatusMessage(tmp, false);
                await Task.Delay(2000);

                while (tmp.Length > 35)
                {
                    Status = new StatusMessage(tmp, false);
                    tmp = tmp.Substring(1);

                    await Task.Delay(1000 / scrollspeed);

                    if (abortscroll) break;
                }

                if (abortscroll) break;
                await Task.Delay(2000);
                if (abortscroll) break;
            }

            ClearAppStatus();
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

        public static void StartInderminateProgress()
        {
            Progress = -0.5;
        }

        public static void StopIndeterminateProgress()
        {
            Progress = -1;
        }

        public static void SetSavingFileMessage()
        {
            StartInderminateProgress();

            SetStatus("Saving File...");
        }

        public static void SetFileSaveSuccessfulMessage(string path)
        {
            ClearAppStatus();

            SetStatusScrolling("File Saved: " + path);
        }

        private struct StatusMessage
        {
            public StatusMessage(string msg, bool time, int priority = 0)
            {
                Message = msg;
                Timed = time;
                Priority = priority;
            }

            public string Message { get; private set; }
            public bool Timed { get; private set; }
            public int Priority { get; private set; }
        }
    }

    public struct ProgressIndicatorEventData
    {
        public double Progress { get; set; }

        public bool Indeterminate => this.Progress < 0;
        public bool IsDeterminate => !Indeterminate;
        public bool HideProgressWheel => IsProgressFinished && Indeterminate;
        public bool IsProgressFinished => Math.Abs(Math.Abs(Progress) - 1) < double.Epsilon;
        

        public ProgressIndicatorEventData(double progress)
        {
            Progress = progress;
        }

        public enum Response
        {
            IndeterminateWheel,
            DeterminateWheel,
            IndeterminateFinished,
            DeterminateFinished
        }
    }
}
