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
using AnalysisITC.UI.MacOS.Drawing;

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
            var changes = PreferencesChangeSet.FromEventArgs(e);
            var figureChanged = false;
            if (changes.Contains(nameof(PreferencesState.FinalFigureWidthCentimeters)))
                FinalFigureGraphView.Width = (float)AppSettings.FinalFigureDimensions[0];
            if (changes.Contains(nameof(PreferencesState.FinalFigureHeightCentimeters)))
                FinalFigureGraphView.Height = (float)AppSettings.FinalFigureDimensions[1];
            if (changes.Contains(nameof(PreferencesState.FinalFigureShowDetailsAsDefault)))
                FinalFigureGraphView.DrawExpDetails = AppSettings.FinalFigureShowDetailsAsDefault;
            if (changes.Contains(nameof(PreferencesState.FinalFigureShowModelInfoAsDefault)))
                FinalFigureGraphView.DrawModelInfo = AppSettings.FinalFigureShowModelInfoAsDefault;
            if (changes.Contains(nameof(PreferencesState.FinalFigureShowParameterBoxAsDefault)))
            {
                FinalFigureGraphView.DrawFitParameterValues = AppSettings.FinalFigureShowParameterBoxAsDefault;
                FinalFigureGraphView.UpdateParameterBoxVisibility();
            }
            if (changes.Contains(nameof(PreferencesState.UncertaintyDisplayStyle)))
                FinalFigureGraphView.TextUncertaintyStyle = AppSettings.UncertaintyDisplayStyle;
            if (changes.Contains(nameof(PreferencesState.ShowResidualGraph)))
                FinalFigureGraphView.ShowResiduals = AppSettings.ShowResidualGraph;
            if (changes.Contains(nameof(PreferencesState.ShowResidualGraphGap)))
                FinalFigureGraphView.GapResidualGraph = AppSettings.ShowResidualGraphGap;
            if (changes.Contains(nameof(PreferencesState.AutoAxesIgnoresBadData)))
                FinalFigureGraphView.AutoAxesIgnoresBadData = AppSettings.AutoAxesIgnoresBadData;
            if (changes.Contains(nameof(PreferencesState.FitLineSmoothness)))
                FinalFigureGraphView.FitLineSmoothness = (GraphBase.LineSmoothness)(int)AppSettings.FitLineSmoothness;
            if (changes.Contains(nameof(PreferencesState.DefaultSplinePointDensity)))
                SplineInterpolator.DefaultPointDensity = AppSettings.DefaultSplinePointDensity;
            if (changes.Contains(nameof(PreferencesState.DefaultSplineHandleMode)))
                SplineInterpolator.DefaultHandleMode = AppSettings.DefaultSplineHandleMode;
            if (changes.Contains(nameof(PreferencesState.DefaultSplinePointTimeDragging)))
                SplineInterpolator.DefaultAllowPointTimeDragging = AppSettings.DefaultSplinePointTimeDragging;
            if (changes.Contains(nameof(PreferencesState.AnalysisParameterDisplay)))
                AnalysisGraphView.AnalysisDisplayParameters = AppSettings.AnalysisParameterDisplay;
            figureChanged = changes.ContainsAny(
                nameof(PreferencesState.FinalFigureWidthCentimeters),
                nameof(PreferencesState.FinalFigureHeightCentimeters),
                nameof(PreferencesState.FinalFigureShowDetailsAsDefault),
                nameof(PreferencesState.FinalFigureShowModelInfoAsDefault),
                nameof(PreferencesState.FinalFigureShowParameterBoxAsDefault),
                nameof(PreferencesState.UncertaintyDisplayStyle),
                nameof(PreferencesState.ShowResidualGraph),
                nameof(PreferencesState.ShowResidualGraphGap),
                nameof(PreferencesState.AutoAxesIgnoresBadData),
                nameof(PreferencesState.FitLineSmoothness),
                nameof(PreferencesState.DefaultSplinePointDensity),
                nameof(PreferencesState.DefaultSplineHandleMode),
                nameof(PreferencesState.DefaultSplinePointTimeDragging),
                nameof(PreferencesState.AnalysisParameterDisplay));
            if (figureChanged) FinalFigureGraphView.Invalidate();
        }
    }
}
