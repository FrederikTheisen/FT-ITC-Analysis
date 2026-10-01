using System;
using AnalysisITC.Platform;
using System.Globalization;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Data
{
    public enum ExperimentDateSource
    {
        Unknown,
        DataFile,
        FileSystem,
        UserModified,
    }

    public class ITCDataContainer
    {
        private string name = "";
        private string comments = "";
        private DateTime date;
        private bool isModified;

        public string UniqueID { get; private set; } = Guid.NewGuid().ToString();
        public string FileName { get; private set; } = "";
        public event EventHandler ModifiedChanged;
        public event EventHandler ContentChanged;

        public string Comments
        {
            get => comments;
            set
            {
                if (comments == value) return;

                comments = value ?? "";
                MarkModified();
            }
        }
        public DateTime Date
        {
            get => date;
            set
            {
                if (date == value) return;

                date = value;
                MarkModified();
            }
        }
        public ExperimentDateSource DateSource { get; set; } = ExperimentDateSource.Unknown;
        public bool IsModified => isModified;

        public string UILongDateWithTime => GetLongDateString();// + " " + Date.ToString("HH:mm:ss");
        public string UIShortDateWithTime => GetShortDateString();
        public string UIDateSourceSuffix => DateSource switch
        {
            ExperimentDateSource.DataFile => " (from data file)",
            ExperimentDateSource.FileSystem => " (from file system)",
            ExperimentDateSource.UserModified => " (user provided)",
            _ => ""
        };

        public void SetID(string id) => UniqueID = id;

        public void SetDate(DateTime date) => this.date = date;

        public void SetFileName(string filename) => FileName = filename;

        public string Name
        {
            get => string.IsNullOrEmpty(name) ? System.IO.Path.GetFileNameWithoutExtension(FileName) : name;
            set
            {
                var next = value ?? "";
                if (name == next) return;

                name = next;
                MarkModified();
            }
        }

        public void MarkModified()
        {
            if (DocumentDirtyTracker.IsRestoringDocument) return;
            ContentChanged?.Invoke(this, EventArgs.Empty);
            if (isModified) return;

            isModified = true;
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }

        public void MarkClean()
        {
            if (!isModified) return;

            isModified = false;
            ModifiedChanged?.Invoke(this, EventArgs.Empty);
        }

        string GetShortDateString()
        {
            string s = Date.ToString("d", CultureInfo.GetCultureInfo(AppSettings.Locale)).Replace("-","/");

            // Add time in local format
            s += " " + Date.ToString("t", CultureInfo.GetCultureInfo(AppSettings.Locale));

            return s;
        }

        string GetLongDateString()
        {
            var s = Date.ToString("D", CultureInfo.CurrentUICulture) + " " + Date.ToString("T", CultureInfo.GetCultureInfo(AppSettings.Locale));

            return s;
        }
    }
}
