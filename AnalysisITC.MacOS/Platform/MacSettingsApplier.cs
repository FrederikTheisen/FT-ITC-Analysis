using System;

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

namespace AnalysisITC.UI.MacOS
{
    public static class MacSettingsApplier
    {
        static bool registered;

        public static void Register()
        {
            if (registered) return;

            AppSettings.SettingsApplied += Apply;
            registered = true;
        }

        static void Apply(object sender, EventArgs e)
        {
            FinalFigureGraphView.Width = (float)AppSettings.FinalFigureDimensions[0];
            FinalFigureGraphView.Height = (float)AppSettings.FinalFigureDimensions[1];
            FinalFigureGraphView.DrawExpDetails = AppSettings.FinalFigureShowParameterBoxAsDefault;
            FinalFigureGraphView.DrawModelInfo = AppSettings.FinalFigureShowModelInfoAsDefault;
            FinalFigureGraphView.TextUncertaintyStyle = AppSettings.UncertaintyDisplayStyle;
            FinalFigureGraphView.UpdateParameterBoxVisibility();
            FinalFigureGraphView.ShowResiduals = AppSettings.ShowResidualGraph;
            FinalFigureGraphView.GapResidualGraph = AppSettings.ShowResidualGraphGap;
            FinalFigureGraphView.AutoAxesIgnoresBadData = AppSettings.AutoAxesIgnoresBadData;
            SplineInterpolator.DefaultPointDensity = AppSettings.DefaultSplinePointDensity;
            SplineInterpolator.DefaultHandleMode = AppSettings.DefaultSplineHandleMode;
            SplineInterpolator.DefaultAllowPointTimeDragging = AppSettings.DefaultSplinePointTimeDragging;
            AnalysisGraphView.AnalysisDisplayParameters = AppSettings.AnalysisParameterDisplay;
        }
    }
}
