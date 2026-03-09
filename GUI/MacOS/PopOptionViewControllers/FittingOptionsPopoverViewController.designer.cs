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
	[Register ("FittingOptionsPopoverViewController")]
	partial class FittingOptionsPopoverViewController
	{
		[Outlet]
		AppKit.NSTextField ErrorIterationLabel { get; set; }

		[Outlet]
		AppKit.NSSlider ErrorIterationsControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl ErrorMethodControl { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl IncludeConcErrorControl { get; set; }

		[Outlet]
		AppKit.NSTextField InitialValuesHeader { get; set; }

		[Outlet]
		AppKit.NSBox InitialValuesLine { get; set; }

		[Outlet]
		AppKit.NSTextField ModelOptionsHeader { get; set; }

		[Outlet]
		AppKit.NSBox ModelOptionsLine { get; set; }

		[Outlet]
		AppKit.NSStackView OptionStackView { get; set; }

		[Outlet]
		AppKit.NSStackView ParameterStackView { get; set; }

		[Outlet]
		AppKit.NSSegmentedControl SolverAlgorithmControl { get; set; }

		[Outlet]
		AppKit.NSStackView StackView { get; set; }

		[Outlet]
		AppKit.NSButton UseWeightedControl { get; set; }

		[Action ("ApplyOptions:")]
		partial void ApplyOptions (Foundation.NSObject sender);

		[Action ("ErrorIterationSliderChanged:")]
		partial void ErrorIterationSliderChanged (AppKit.NSSlider sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ErrorIterationLabel != null) {
				ErrorIterationLabel.Dispose ();
				ErrorIterationLabel = null;
			}

			if (ErrorIterationsControl != null) {
				ErrorIterationsControl.Dispose ();
				ErrorIterationsControl = null;
			}

			if (ErrorMethodControl != null) {
				ErrorMethodControl.Dispose ();
				ErrorMethodControl = null;
			}

			if (IncludeConcErrorControl != null) {
				IncludeConcErrorControl.Dispose ();
				IncludeConcErrorControl = null;
			}

			if (InitialValuesHeader != null) {
				InitialValuesHeader.Dispose ();
				InitialValuesHeader = null;
			}

			if (InitialValuesLine != null) {
				InitialValuesLine.Dispose ();
				InitialValuesLine = null;
			}

			if (ModelOptionsHeader != null) {
				ModelOptionsHeader.Dispose ();
				ModelOptionsHeader = null;
			}

			if (ModelOptionsLine != null) {
				ModelOptionsLine.Dispose ();
				ModelOptionsLine = null;
			}

			if (SolverAlgorithmControl != null) {
				SolverAlgorithmControl.Dispose ();
				SolverAlgorithmControl = null;
			}

			if (StackView != null) {
				StackView.Dispose ();
				StackView = null;
			}

			if (ParameterStackView != null) {
				ParameterStackView.Dispose ();
				ParameterStackView = null;
			}

			if (OptionStackView != null) {
				OptionStackView.Dispose ();
				OptionStackView = null;
			}

			if (UseWeightedControl != null) {
				UseWeightedControl.Dispose ();
				UseWeightedControl = null;
			}
		}
	}
}
