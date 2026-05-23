using System;
using Foundation;
using AppKit;
using System.Collections.Generic;

namespace AnalysisITC
{
	public partial class TechnicalDetailsViewController : AppKit.NSViewController
	{
        HelpDocumentView helpDocumentView;

		#region Constructors

		// Called when created from unmanaged code
		public TechnicalDetailsViewController (IntPtr handle) : base (handle)
		{
			Initialize ();
		}

		// Called when created directly from a XIB file
		[Export ("initWithCoder:")]
		public TechnicalDetailsViewController (NSCoder coder) : base (coder)
		{
			Initialize ();
		}

		// Call to load from the XIB/NIB file
		public TechnicalDetailsViewController () : base ("TechnicalDetailsView", NSBundle.MainBundle)
		{
			Initialize ();
		}

		// Shared initialization code
		void Initialize ()
		{
		}

        #endregion

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            helpDocumentView = new HelpDocumentView(this, TextField, "./ScienceHelpResource.txt");
            helpDocumentView.Install();
        }
    }
}
