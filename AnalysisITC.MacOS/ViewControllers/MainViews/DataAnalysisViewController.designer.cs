// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;

namespace AnalysisITC
{
    [Register("DataAnalysisViewController")]
    partial class DataAnalysisViewController
    {
        [Outlet]
        AppKit.NSSegmentedControl AnalysisInspectorTabControl { get; set; }

        [Outlet]
        AppKit.NSTabView AnalysisInspectorTabView { get; set; }

        [Outlet]
        AppKit.NSSegmentedControl AnalysisModeControl { get; set; }

        [Outlet]
        AppKit.NSStackView ConstraintStackView { get; set; }

        [Outlet]
        AppKit.NSTextField ConstraintsHeader { get; set; }

        [Outlet]
        AppKit.NSBox ConstraintsLine { get; set; }

        [Outlet]
        AppKit.NSButton CreateAnalysisResultControl { get; set; }

        [Outlet]
        AppKit.NSTextField DataAnalysisSummaryLabel { get; set; }

        [Outlet]
        AppKit.NSTextField ErrorIterationLabel { get; set; }

        [Outlet]
        AppKit.NSSlider ErrorIterationsControl { get; set; }

        [Outlet]
        AppKit.NSPopUpButton ErrorMethodControl { get; set; }

        [Outlet]
        AppKit.NSButton FitSimplexButton { get; set; }

        [Outlet]
        AnalysisITC.UI.MacOS.CustomViews.AnalysisFitSummaryView FitSummaryView { get; set; }

        [Outlet]
        AnalysisITC.AnalysisGraphView GraphView { get; set; }

        [Outlet]
        AppKit.NSButton IncludeConcErrorControl { get; set; }

        [Outlet]
        AppKit.NSTextField ModelOptionsEmptyLabel { get; set; }

        [Outlet]
        AppKit.NSStackView ModelOptionsStackView { get; set; }

        [Outlet]
        AppKit.NSPopUpButton ModelTypeControl { get; set; }

        [Outlet]
        AppKit.NSStackView ParameterStackView { get; set; }

        [Outlet]
        AppKit.NSTextField ParametersEmptyLabel { get; set; }

        [Outlet]
        AppKit.NSButton PeakInfoScopeButton { get; set; }

        [Outlet]
        AppKit.NSButton ScaleToValidButton { get; set; }

        [Outlet]
        AppKit.NSButton ShowResidualGraphButton { get; set; }

        [Outlet]
        AppKit.NSPopUpButton SolverAlgorithmControl { get; set; }

        [Outlet]
        AppKit.NSButton UnlockParametersForErrorEstimationControl { get; set; }

        [Outlet]
        AppKit.NSButton UseWeightedControl { get; set; }

        [Action("AnalysisInspectorTabChanged:")]
        partial void AnalysisInspectorTabChanged(Foundation.NSObject sender);

        [Action("AnalysisModeClicked:")]
        partial void AnalysisModeClicked(AppKit.NSSegmentedControl sender);

        [Action("AnalysisModelClicked:")]
        partial void AnalysisModelClicked(AppKit.NSPopUpButton sender);

        [Action("CreateAnalysisResultChanged:")]
        partial void CreateAnalysisResultChanged(AppKit.NSButton sender);

        [Action("ErrorIterationSliderChanged:")]
        partial void ErrorIterationSliderChanged(AppKit.NSSlider sender);

        [Action("FitSimplex:")]
        partial void FitSimplex(Foundation.NSObject sender);

        [Action("ScopeButtonClicked:")]
        partial void ScopeButtonClicked(AppKit.NSButton sender);

        void ReleaseDesignerOutlets()
        {
            AnalysisInspectorTabControl?.Dispose();
            AnalysisInspectorTabControl = null;
            AnalysisInspectorTabView?.Dispose();
            AnalysisInspectorTabView = null;
            AnalysisModeControl?.Dispose();
            AnalysisModeControl = null;
            ConstraintStackView?.Dispose();
            ConstraintStackView = null;
            ConstraintsHeader?.Dispose();
            ConstraintsHeader = null;
            ConstraintsLine?.Dispose();
            ConstraintsLine = null;
            CreateAnalysisResultControl?.Dispose();
            CreateAnalysisResultControl = null;
            DataAnalysisSummaryLabel?.Dispose();
            DataAnalysisSummaryLabel = null;
            ErrorIterationLabel?.Dispose();
            ErrorIterationLabel = null;
            ErrorIterationsControl?.Dispose();
            ErrorIterationsControl = null;
            ErrorMethodControl?.Dispose();
            ErrorMethodControl = null;
            FitSimplexButton?.Dispose();
            FitSimplexButton = null;
            FitSummaryView?.Dispose();
            FitSummaryView = null;
            GraphView?.Dispose();
            GraphView = null;
            IncludeConcErrorControl?.Dispose();
            IncludeConcErrorControl = null;
            ModelOptionsEmptyLabel?.Dispose();
            ModelOptionsEmptyLabel = null;
            ModelOptionsStackView?.Dispose();
            ModelOptionsStackView = null;
            ModelTypeControl?.Dispose();
            ModelTypeControl = null;
            ParameterStackView?.Dispose();
            ParameterStackView = null;
            ParametersEmptyLabel?.Dispose();
            ParametersEmptyLabel = null;
            PeakInfoScopeButton?.Dispose();
            PeakInfoScopeButton = null;
            ScaleToValidButton?.Dispose();
            ScaleToValidButton = null;
            ShowResidualGraphButton?.Dispose();
            ShowResidualGraphButton = null;
            SolverAlgorithmControl?.Dispose();
            SolverAlgorithmControl = null;
            UnlockParametersForErrorEstimationControl?.Dispose();
            UnlockParametersForErrorEstimationControl = null;
            UseWeightedControl?.Dispose();
            UseWeightedControl = null;
        }
    }
}
