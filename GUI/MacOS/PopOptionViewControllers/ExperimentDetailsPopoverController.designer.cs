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
	[Register ("ExperimentDetailsPopoverController")]
	partial class ExperimentDetailsPopoverController
	{
		[Outlet]
		AppKit.NSTextField CellConcentrationErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField CellConcentrationField { get; set; }

		[Outlet]
		AppKit.NSTextField ExperimentNameField { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcentrationErrorField { get; set; }

		[Outlet]
		AppKit.NSTextField SyringeConcentrationField { get; set; }

		[Outlet]
		AppKit.NSTextField TemperatureField { get; set; }

		[Action ("Apply:")]
		partial void Apply (Foundation.NSObject sender);

		[Action ("Cancel:")]
		partial void Cancel (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (CellConcentrationErrorField != null) {
				CellConcentrationErrorField.Dispose ();
				CellConcentrationErrorField = null;
			}

			if (CellConcentrationField != null) {
				CellConcentrationField.Dispose ();
				CellConcentrationField = null;
			}

			if (SyringeConcentrationErrorField != null) {
				SyringeConcentrationErrorField.Dispose ();
				SyringeConcentrationErrorField = null;
			}

			if (SyringeConcentrationField != null) {
				SyringeConcentrationField.Dispose ();
				SyringeConcentrationField = null;
			}

			if (TemperatureField != null) {
				TemperatureField.Dispose ();
				TemperatureField = null;
			}

			if (ExperimentNameField != null) {
				ExperimentNameField.Dispose ();
				ExperimentNameField = null;
			}
		}
	}
}
