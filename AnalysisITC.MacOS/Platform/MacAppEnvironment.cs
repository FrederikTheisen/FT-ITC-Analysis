using System;
using Foundation;
using AnalysisITC.Platform;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Platform.MacOS
{
    public sealed class MacAppEnvironment : IAppEnvironment
    {
        public string LocaleIdentifier => NSLocale.CurrentLocale.CollatorIdentifier;
        public string ShortVersion => NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString();
        public string BuildVersion => NSBundle.MainBundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString();
        public string ApplicationDataDirectory => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        public string GetResourcePath(string name, string extension) => NSBundle.MainBundle.PathForResource(name, extension);
    }
}
