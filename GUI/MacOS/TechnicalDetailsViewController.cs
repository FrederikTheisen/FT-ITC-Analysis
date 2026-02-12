using System;
using System.IO;
using Foundation;
using AppKit;
using System.Collections.Generic;

namespace AnalysisITC
{
	public partial class TechnicalDetailsViewController : AppKit.NSViewController
	{
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

            var text = File.ReadAllText("./ScienceHelpResource.txt");
            var font = NSFont.FromFontName(TextField.Font.DisplayName, 13);

            var processed_text = Utilities.MarkdownProcessor.ProcessWrittenText(text);
            var attstring = Utilities.MacStrings.FromMarkDownString(processed_text, font);

            TextField.TextStorage.SetString(attstring);
            TextField.TextColor = NSColor.Label;
        }
    }
}
