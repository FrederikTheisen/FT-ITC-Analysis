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
	[Register ("GraphOptionsPopoverViewController")]
	partial class GraphOptionsPopoverViewController
	{
		[Outlet]
		AppKit.NSSegmentedControl EnergyUnitControl { get; set; }

		[Outlet]
		AppKit.NSTextField HeightLabel { get; set; }

		[Outlet]
		AppKit.NSMenu ParameterDisplayOptionsControl { get; set; }

		[Outlet]
		AppKit.NSButton SanitizeTicks { get; set; }

		[Outlet]
		AppKit.NSTextField WidthLabel { get; set; }

		[Action ("ControlChanged:")]
		partial void ControlChanged (Foundation.NSObject sender);

		[Action ("ParameterOptionAction:")]
		partial void ParameterOptionAction (Foundation.NSObject sender);
		
		void ReleaseDesignerOutlets ()
		{
			if (ParameterDisplayOptionsControl != null) {
				ParameterDisplayOptionsControl.Dispose ();
				ParameterDisplayOptionsControl = null;
			}

			if (EnergyUnitControl != null) {
				EnergyUnitControl.Dispose ();
				EnergyUnitControl = null;
			}

			if (HeightLabel != null) {
				HeightLabel.Dispose ();
				HeightLabel = null;
			}

			if (SanitizeTicks != null) {
				SanitizeTicks.Dispose ();
				SanitizeTicks = null;
			}

			if (WidthLabel != null) {
				WidthLabel.Dispose ();
				WidthLabel = null;
			}
		}
	}
}
