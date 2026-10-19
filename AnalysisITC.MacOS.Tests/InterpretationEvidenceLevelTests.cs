using System;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AppKit;
using AnalysisITC;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;

static class InterpretationEvidenceLevelTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    static int Main(string[] args)
    {
        NSApplication.Init();
        var originalCode = AppSettings.InterpretationOperatorCode;
        try
        {
            AppSettings.InterpretationOperatorCode = "native-evidence-level-test";
            var type = typeof(MainWindowController).Assembly.GetType("AnalysisITC.AnalysisInterpretationViewController", true);
            var report = new AnalysisReport();
            report.UpdateInterpretationSettings(new AnalysisInterpretationOptions
            { IncludeProcessingInformation = true, IncludeThermograms = true, InjectionRows = AnalysisInterpretationInjectionRows.IncludedOnly });
            report.MarkClean();
            using (var client = new HttpClient(new LocalHandler()))
            using (var controller = (NSViewController)Activator.CreateInstance(type, new object[]
            {
                report, new Func<string, AnalysisResult>(_ => null), new Func<string, ExperimentData>(_ => null),
                client, new Action(() => { throw new Exception("Capability refresh saved settings"); }),
                new Action<AnalysisInterpretationRecord>(_ => { }),
            }))
            {
                var view = controller.View;
                foreach (var name in new[] { "savePackage", "generate", "cancel" })
                    Check(((NSButton)type.GetField(name, Private).GetValue(controller)).Action != null,
                        name + " must retain its native action");
                var popup = (NSPopUpButton)type.GetField("evidenceLevelPopup", Private).GetValue(controller);
                var heading = (NSTextField)type.GetField("dataInclusionHeading", Private).GetValue(controller);
                var parent = (NSStackView)type.GetField("evidenceLevelOptions", Private).GetValue(controller);
                Action<string> refresh = tier => type.GetMethod("PopulateInterpretationChoices", Private).Invoke(controller,
                    new object[] { new InterpretationOperatorOptionsResponse { AccessTier = tier, Mode = "presets", Quota = tier == "public" ? new InterpretationPublicQuota() : null } });
                type.GetMethod("SetInterpretationAccessFailure", Private).Invoke(controller, new object[] { "Unavailable" });
                Check(parent.Hidden && heading.Hidden && !popup.Enabled, "Unavailable capabilities must hide and disable the selector");
                refresh("standard");
                Check(!parent.Hidden && !heading.Hidden && popup.Enabled, "Standard access must expose its selector and heading");
                Check(popup.TitleOfSelectedItem == "Injection data", "Standard temporarily limits the requested level");
                refresh("advanced");
                Check(popup.TitleOfSelectedItem == "Processing details", "Advanced must restore processing; saved traces need fresh opt-in");
                popup.SelectItem(3); popup.SendAction(popup.Action, popup.Target);
                Check(Snapshot(type, controller).InterpretationSettings.IncludeThermograms, "User selection must reach the request snapshot");
                Check(Snapshot(type, controller).InterpretationSettings.InjectionRows == AnalysisInterpretationInjectionRows.IncludedOnly,
                    "Level selection must preserve IncludedOnly");
                type.GetMethod("SetInterpretationAccessFailure", Private).Invoke(controller, new object[] { "Unavailable" });
                refresh("advanced");
                Check(popup.TitleOfSelectedItem == "Thermograms", "A capability failure must preserve this opening's explicit opt-in");
                popup.SelectItem(0); popup.SendAction(popup.Action, popup.Target);
                refresh("standard"); refresh("advanced");
                Check(popup.TitleOfSelectedItem == "Results", "Capability refresh must honor a user-changed request");
                refresh("public");
                Check(parent.Hidden && heading.Hidden, "Public must hide the redundant selector and heading");
                Check(!report.IsModified && report.InterpretationSettings.IncludeThermograms, "Opening and refreshing must preserve the saved report");
            }
            Console.WriteLine("PASS: native evidence levels, Standard visibility, capability recovery, fresh trace opt-in and IncludedOnly");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { AppSettings.InterpretationOperatorCode = originalCode; }
    }

    static AnalysisReport Snapshot(Type type, object controller) => (AnalysisReport)type.GetMethod("CreateDialogReport", Private).Invoke(controller, null);
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

    sealed class LocalHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline native test"));
    }
}
