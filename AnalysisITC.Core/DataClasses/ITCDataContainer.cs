using System;
using AnalysisITC.Platform;
using AnalysisITC.Core.Presentation;

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
                var next = value ?? "";
                if (comments == next) return;

                comments = next;
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

        public string UILongDateWithTime => LocalDateTimeFormatter.LongDateTime(Date);
        public string UIShortDateWithTime => LocalDateTimeFormatter.ShortDateTime(Date);
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

        // Publish one notification after a complete replacement has been prepared.
        protected void RestoreTextMetadata(string restoredName, string restoredComments)
        {
            name = restoredName ?? "";
            comments = restoredComments ?? "";
        }

        public virtual string Name
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

        public virtual void MarkModified()
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

    }
}
