// The storyboard retains the controller registration; its content is built in code.
using Foundation;
namespace AnalysisITC
{
    [Register("ExperimentDetailsPopoverController")]
    partial class ExperimentDetailsPopoverController
    {
        [Action("AddAttribute:")]
        partial void AddAttribute(NSObject sender);
        [Action("Apply:")]
        partial void Apply(NSObject sender);
        [Action("Cancel:")]
        partial void Cancel(NSObject sender);
    }
}
