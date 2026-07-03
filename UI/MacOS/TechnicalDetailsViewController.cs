using System;
using Foundation;
using AppKit;
using System.Collections.Generic;

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
