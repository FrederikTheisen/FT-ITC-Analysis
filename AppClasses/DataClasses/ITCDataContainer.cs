using System;
using Foundation;
using System.Globalization;

namespace AnalysisITC
{
    public class ITCDataContainer
    {
        private static readonly CultureInfo UICulture = EnglishWithLocalFormats();

        private string name = "";
        private string comments = "";
        private DateTime date;
        private bool isModified;

        public string UniqueID { get; private set; } = Guid.NewGuid().ToString();
        public string FileName { get; private set; } = "";
        public event EventHandler ModifiedChanged;

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
        public bool IsModified => isModified;

        public string UILongDateWithTime => GetLongDateString();// + " " + Date.ToString("HH:mm:ss");
        public string UIShortDateWithTime => GetShortDateString();

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



        public string Test()
        {
            var s1 = Date.ToLongDateString();
            var s5 = Date.ToShortDateString();

            CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("da");

            var s2 = Date.ToLongDateString();
            var s6 = Date.ToShortDateString();

            CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("en-DK");

            var s7 = Date.ToLongDateString();
            var s8 = Date.ToShortDateString();

            var s3 = Date.ToString(CultureInfo.GetCultureInfo("da"));
            var s4 = Date.ToString(CultureInfo.GetCultureInfo("en"));


            return Date.ToString(CultureInfo.GetCultureInfo("da"));
        }

        private static CultureInfo EnglishWithLocalFormats()
        {
            var local = (CultureInfo)CultureInfo.GetCultureInfo(NSLocale.CurrentLocale.CollatorIdentifier).Clone();
            var culture = (CultureInfo)CultureInfo.GetCultureInfo("en").Clone();

            

            culture.DateTimeFormat.ShortDatePattern = local.DateTimeFormat.ShortDatePattern;
            culture.DateTimeFormat.LongDatePattern = local.DateTimeFormat.LongDatePattern;
            culture.DateTimeFormat.ShortTimePattern = local.DateTimeFormat.ShortTimePattern;
            culture.DateTimeFormat.LongTimePattern = local.DateTimeFormat.LongTimePattern;

            CultureInfo.CurrentCulture = CultureInfo.CreateSpecificCulture("en-DK");
            

            return culture;
        }
    }
}
