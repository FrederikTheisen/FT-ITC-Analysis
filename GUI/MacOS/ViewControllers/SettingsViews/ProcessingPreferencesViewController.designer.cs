// WARNING
//
// This file has been generated automatically by Visual Studio to store outlets and
// actions made in the UI designer. If it is removed, they will be lost.
// Manual changes to this file may not be handled correctly.
//
using Foundation;
using System.CodeDom.Compiler;

namespace AnalysisITC
{
    [Register ("ProcessingPreferencesViewController")]
    partial class ProcessingPreferencesViewController
    {
        [Outlet]
        AppKit.NSSegmentedControl BufferSubtractionMethodControl { get; set; }

        [Outlet]
        AppKit.NSSegmentedControl DilutionMathMethodControl { get; set; }

        [Outlet]
        AppKit.NSButton DiscardIntegrationRegionForBaseline { get; set; }

        [Outlet]
        AppKit.NSSegmentedControl PeakFitAlgorithmControl { get; set; }

        [Outlet]
        AppKit.NSButton IntegrationRegionCopyIncludesStartControl { get; set; }

        [Outlet]
        AppKit.NSButton ReprocessIntegratedHeatDataOnLoad { get; set; }

        [Outlet]
        AppKit.NSSegmentedControl SplineHandleModeControl { get; set; }

        [Outlet]
        AppKit.NSSegmentedControl SplinePointDensityControl { get; set; }

        [Outlet]
        AppKit.NSButton SplinePointTimeDraggingDefaultControl { get; set; }

        [Action ("Apply:")]
        partial void Apply (Foundation.NSObject sender);

        [Action ("Close:")]
        partial void Close (Foundation.NSObject sender);

        [Action ("Reset:")]
        partial void Reset (Foundation.NSObject sender);

        void ReleaseDesignerOutlets ()
        {
            if (BufferSubtractionMethodControl != null) {
                BufferSubtractionMethodControl.Dispose ();
                BufferSubtractionMethodControl = null;
            }

            if (DilutionMathMethodControl != null) {
                DilutionMathMethodControl.Dispose ();
                DilutionMathMethodControl = null;
            }

            if (DiscardIntegrationRegionForBaseline != null) {
                DiscardIntegrationRegionForBaseline.Dispose ();
                DiscardIntegrationRegionForBaseline = null;
            }

            if (PeakFitAlgorithmControl != null) {
                PeakFitAlgorithmControl.Dispose ();
                PeakFitAlgorithmControl = null;
            }

            if (IntegrationRegionCopyIncludesStartControl != null) {
                IntegrationRegionCopyIncludesStartControl.Dispose ();
                IntegrationRegionCopyIncludesStartControl = null;
            }

            if (ReprocessIntegratedHeatDataOnLoad != null) {
                ReprocessIntegratedHeatDataOnLoad.Dispose ();
                ReprocessIntegratedHeatDataOnLoad = null;
            }

            if (SplineHandleModeControl != null) {
                SplineHandleModeControl.Dispose ();
                SplineHandleModeControl = null;
            }

            if (SplinePointDensityControl != null) {
                SplinePointDensityControl.Dispose ();
                SplinePointDensityControl = null;
            }

            if (SplinePointTimeDraggingDefaultControl != null) {
                SplinePointTimeDraggingDefaultControl.Dispose ();
                SplinePointTimeDraggingDefaultControl = null;
            }
        }
    }
}
