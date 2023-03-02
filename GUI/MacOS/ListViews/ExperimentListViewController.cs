// This file has been autogenerated from a class added in the UI designer.

using System;

using Foundation;
using AppKit;

namespace AnalysisITC
{
	public partial class ExperimentListViewController : NSViewController
	{
		public ExperimentListViewController (IntPtr handle) : base (handle)
		{
		}

        public override void ViewDidAppear()
        {
            base.ViewDidAppear();

            var result = DataManager.DataSourceContent[DataManager.SelectedContentIndex];

            if (result is not AnalysisResult) throw new Exception("Selected item not analysis result");

            var analysisresult = result as AnalysisResult;

            Label.StringValue = "";

            foreach (var mdl in analysisresult.Solution.Model.Models)
            {
                Label.StringValue += mdl.Data.FileName + Environment.NewLine;
            }

            Label.StringValue = Label.StringValue.Trim();
        }
    }
}