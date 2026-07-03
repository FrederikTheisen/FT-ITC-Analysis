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
    public static class MacPlatformBootstrapper
    {
        public static void Register()
        {
            PlatformServices.RegisterSettingsStore(new MacUserDefaultsSettingsStore());
            PlatformServices.RegisterMainThreadDispatcher(new MacMainThreadDispatcher());
            PlatformServices.RegisterAppEnvironment(new MacAppEnvironment());
        }
    }
}
