// Run with run-analysis-null-graph-tests.sh; exercises the real AppKit report picker and renderer.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AppKit;
using CoreGraphics;
using Foundation;
using AnalysisITC;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Processing;
using AnalysisITC.UI.MacOS.Drawing;

static class AnalysisReportReferencesTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type ReportControllerType = typeof(MainWindowController).Assembly.GetType("AnalysisITC.AnalysisReportViewController");

    static int Main(string[] args)
    {
        NSApplication.Init();
        var original = PreferencesState.FromSettings();
        try
        {
            CheckPicker(false);
            CheckPicker(true);
            CheckCoverSignOff();
            CheckMixedAssessmentReport();
            CheckDataAvailabilityReport();
            CheckExperimentHeaders();
            CheckConditionsSubtitles();
            CheckSourceMetadata();
            Console.WriteLine("PASS: native tandem source preselection, shared picker roles/tooltips, optional removal, experiment headers and conditions subtitles, source metadata wrapping and PDF rendering");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL: " + error);
            return 1;
        }
        finally
        {
            DataManager.Clear(DataClearMode.ResetSession);
            typeof(PreferencesState).GetMethod("ApplyToSettings", PrivateInstance).Invoke(original, null);
        }
    }

    static void CheckExperimentHeaders()
    {
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        var measurer = (IAnalysisReportTextMeasurer)rendererType.GetField("measurer", PrivateInstance).GetValue(renderer);
        foreach (var scenario in new[] { "short", "long-experiment", "long-result" })
        {
            var name = scenario == "short" ? "Experiment name" : new string('W', 200);
            var data = Source(name);
            data.Comments = string.Join("\n", Enumerable.Repeat("Header continuation context", 180));
            var result = Result(data);
            result.Name = scenario == "long-result" ? new string('W', 200) : "Result name";
            var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions
            {
                IncludeInjectionTables = true,
                GeneratedAtUtc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
            });
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
            var experimentPages = plan.Pages.Where(page => page.ExperimentLabel.Length > 0).ToList();
            Check(experimentPages.Count > 1, "Native header fixture needs experiment continuation pages");
            foreach (var page in experimentPages)
            {
                Check(page.ExperimentLabel == "1A" && page.ExperimentName == name,
                    "Native experiment header context changed on continuation");
                var header = AnalysisReportHeaderLayout.Create(document, page, plan.MarginLeft,
                    page.Width - plan.MarginRight, measurer);
                Check(header.ContextText.Contains(" · 1A."), "Native running header lost the experiment label");
                if (scenario == "long-result") Check(header.ContextText.EndsWith(" · 1A."),
                    "Native header did not give the result name priority");
                if (scenario == "long-experiment") Check(header.ContextText.EndsWith("…"),
                    "Native long experiment header was not truncated");
                Check(plan.MarginLeft + measurer.Measure(header.ContextText, header.ContextStyle).Width
                    <= header.ExportDateX - 8, "Native header overlaps the export date");
                Check(header.ExportDateText == document.ExportDateText, "Native header changed the export date");
            }
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "Native header QA PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_REPORT_HEADER_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                    Path.Combine(output, "macos-" + scenario + ".pdf") });
            }
        }
    }

    static void CheckConditionsSubtitles()
    {
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        var measurer = (IAnalysisReportTextMeasurer)Field(renderer, "measurer");
        foreach (var scenario in new[] { "short", "wrapped", "supporting" })
        {
            var data = Source("Conditions example");
            data.Name = scenario == "wrapped"
                ? string.Join(" ", Enumerable.Repeat("Long experiment name", 12)) : "Experiment name";
            data.CellConcentration = new FloatWithError(10e-6, 1e-6);
            data.SyringeConcentration = new FloatWithError(100e-6, 10e-6);
            data.MeasuredTemperature = 24.85;
            var buffer = ExperimentAttribute.Parameter(AttributeKey.Buffer, "", new FloatWithError(.050));
            buffer.IntValue = (int)AnalysisITC.Core.Data.Buffer.Hepes;
            buffer.DoubleValue = 7.4;
            data.Attributes.Add(buffer);
            var salt = ExperimentAttribute.Parameter(AttributeKey.Salt, "", new FloatWithError(.150));
            salt.IntValue = (int)Salt.NaCl;
            data.Attributes.Add(salt);
            if (scenario == "wrapped")
                for (var index = 0; index < 12; index++) data.Attributes.Add(buffer.Copy());
            var result = Result(data);
            var report = new AnalysisReport();
            report.SetResultIds(new[] { result.UniqueID });
            if (scenario == "supporting") report.SetSupportingExperimentIds(new[] { "supporting-conditions" });
            var supporting = Source("supporting-conditions");
            supporting.CopyAttributesFrom(data.Attributes, notify: false);
            supporting.Name = "Supporting blank";
            var document = AnalysisReportBuilder.Build(report, id => id == result.UniqueID ? result : null,
                id => id == supporting.UniqueID ? supporting : null);
            var chapter = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment);
            Check(chapter.Subtitle.StartsWith("100 µM ▸ 10 µM | 25 °C | 50 mM Hepes pH 7.4 | "),
                "Native conditions must show syringe then cell with pipes between conditions");
            Check(chapter.Subtitle.Contains("25 °C") && !chapter.Subtitle.Contains("24.85")
                && !chapter.Subtitle.Contains("±"), "Native conditions must show target temperature and nominal concentrations");
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
            var headings = plan.Pages.SelectMany(page => page.Fragments)
                .Where(fragment => fragment.HeadingLayout != null).ToList();
            Check(headings.Count == (scenario == "supporting" ? 2 : 1), "Native report lost a conditions subtitle");
            foreach (var fragment in headings)
            {
                var layout = fragment.HeadingLayout;
                Check(layout.Height == fragment.Bounds.Height, "Native conditions heading was clipped");
                Check(layout.SubtitleBounds.Y - layout.TitleBounds.Bottom == 3, "Native subtitle gap changed");
                Check(layout.SubtitleLines.All(line => measurer.Measure(line,
                    AnalysisReportHeadingLayout.SubtitleStyle).Width <= fragment.Bounds.Width), "Native subtitle exceeds page width");
                if (scenario == "wrapped") Check(layout.TitleLines.Count > 1 && layout.SubtitleLines.Count > 1,
                    "Native long-name fixture must wrap title and conditions");
            }
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "Native conditions PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_REPORT_CONDITIONS_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                    Path.Combine(output, "macos-conditions-" + scenario + ".pdf") });
            }
        }
    }

    static void CheckSourceMetadata()
    {
        var data = Source("Source metadata QA");
        var fileName = string.Concat(Enumerable.Repeat("long-source-name-", 12)) + ".itc";
        data.SetFileName(fileName);
        data.Name = "Source metadata QA experiment";
        data.DataSourceFormat = ITCDataFormat.ITC200;
        var result = Result(data);
        result.Name = "Source metadata QA";
        var document = AnalysisReportBuilder.Build(result);
        var metadata = document.Sections.Single(section => section.Kind == AnalysisReportSectionKind.Experiment)
            .Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Experiment details");
        var source = metadata.Items.Single(item => item.Label == "Source file");
        Check(source.Value == fileName + " (MicroCal ITC Data File)", "Native source metadata differs from saved provenance");
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
        var fragment = plan.Pages.SelectMany(page => page.Fragments).Single(item => ReferenceEquals(item.Block, metadata)
            && metadata.Items.Skip(item.FirstItem).Take(item.ItemCount).Contains(source));
        var lines = (List<string>)rendererType.GetMethod("Wrap", PrivateInstance).Invoke(renderer,
            new object[] { source.Value, fragment.Bounds.Width * .70 - 6, 9d, false });
        Check(lines.Count > 1, "Native source QA fixture did not wrap");
        Check(string.Concat(lines).Replace(" ", "") == source.Value.Replace(" ", ""), "Native wrapping lost source text");
        Check(fragment.Bounds.Height >= lines.Count * 12 + 6, "Native layout did not reserve wrapped source height");
        using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
            Check(pdf.Length > 0, "Native source QA PDF is empty");
        var output = Environment.GetEnvironmentVariable("FTITC_REPORT_SOURCE_QA_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                Path.Combine(output, "macos-source.pdf") });
        }
    }

    static void CheckMixedAssessmentReport()
    {
        var parts = new[] { "Binder", "Inconclusive", "No binding" }.Select(name => Result(Source(name))).ToList();
        var members = parts.Select(part => part.Solution.Solutions.Single()).ToList();
        members[0].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 0, -27000, -24000);
        members[1].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 1500, -29000, -23000);
        members[2].Parameters[ParameterType.Enthalpy1] = new FloatWithError(-25000, 0, -1e11, 1e11);
        var model = new GlobalModel(members.Select(member => member.Model).ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var member in members) model.Parameters.AddIndivdualParameter(member.Model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = model }, members, members[0].Convergence,
            reconstructBootstrap: false);
        model.Solution = global;
        var result = new AnalysisResult(global) { Name = "Mixed assessment report" };
        result.SetValiditySnapshot(AnalysisResultValiditySnapshot.Capture(global));
        var restoreState = typeof(BindingAssessmentState).GetMethod("Restore", BindingFlags.Static | BindingFlags.NonPublic);
        var restoreMember = typeof(AnalysisResult).GetMethod("RestoreMemberAssessment", PrivateInstance);
        var outcomes = new[] { BindingAssessmentOutcome.BindingDetected, BindingAssessmentOutcome.Inconclusive,
            BindingAssessmentOutcome.NoBindingDetected };
        for (var index = 0; index < members.Count; index++)
            restoreMember.Invoke(result, new[] { (object)members[index].Guid,
                restoreState.Invoke(null, new object[] { outcomes[index], BindingAssessmentState.CurrentRuleId, null }) });
        result.SetMemberBindingAssessmentOverride(members[2].Guid, BindingAssessmentOutcome.NoBindingDetected);
        Check(result.Health == AnalysisResultHealth.Warning, "Inconclusive member did not produce a native report warning");
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        {
            var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
            Check(document.IsValid, "Mixed-assessment native report is invalid");
            var summary = document.Sections.Single(section => section.Id == "result-1-analysis-summary");
            if (purpose == ResultOutputPurpose.Diagnostic)
            {
                var assessments = summary.Blocks.OfType<AnalysisReportKeyValueBlock>()
                    .Single(block => block.Title == "Binding assessment and null comparison");
                Check(assessments.Items.Any(item => item.Label == "Member assessments" && item.Value == "Mixed assessments (manual)"),
                    "Native mixed collection lost its manual assessment marker");
                foreach (var outcome in new[] { "Binding detected", "Inconclusive", "No binding detected" })
                    Check(assessments.Items.Any(item => item.Label == outcome && item.Value == "1"),
                        "Native mixed collection lost assessment counts");
            }
            var modelAndFit = summary.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Model and fit details");
            Check(modelAndFit.Items.Any(item => item.Label == "Model")
                    && modelAndFit.Items.Any(item => item.Label.StartsWith("RMSD", StringComparison.Ordinal)),
                "Mixed-assessment native summary lost model or fit information");
            var overview = summary.Blocks.OfType<AnalysisReportTableBlock>()
                .Single(table => table.Title == "Experiment parameter overview");
            Check(overview.Rows.Count == 3, "Native report overview did not retain all members");
            Check(overview.Rows[2].Cells[0].EndsWith("\nNo binding detected (manual)", StringComparison.Ordinal),
                "Native report overview lost its manual assessment marker");
            var plot = summary.Blocks.OfType<AnalysisReportThermodynamicSummaryBlock>().Single();
            Check(plot.Series.Select(series => series.Label).SequenceEqual(new[] { "1A", "1B" }),
                "Native thermodynamic summary must omit no-binding members in both output modes");
            Check(plot.Series.SelectMany(series => series.Bars).All(bar =>
                    (!bar.ConfidenceLower.HasValue || Math.Abs(bar.ConfidenceLower.Value) < 100)
                    && (!bar.ConfidenceUpper.HasValue || Math.Abs(bar.ConfidenceUpper.Value) < 100)),
                "No-binding intervals changed the native thermodynamic summary extent");
            Check(!document.Sections.Any(section => section.Blocks.Count == 0), "Native report contains an empty section");
            Check(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportNoticeBlock>()
                .Any(block => block.Message.Contains("inconclusive")), "Native report lacks its health reason");
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
            Check(plan.Pages.All(page => page.Fragments.Count > 0), "Native report has an empty rendered page");
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "Mixed-assessment native PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_CONSISTENT_REPORT_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                    Path.Combine(output, "macos-mixed-" + purpose.ToString().ToLowerInvariant() + ".pdf") });
            }
        }
        foreach (var member in members)
            result.SetMemberBindingAssessmentOverride(member.Guid, BindingAssessmentOutcome.NoBindingDetected);
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        {
            var document = AnalysisReportBuilder.Build(result, new AnalysisReportOptions { OutputPurpose = purpose });
            Check(!document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportThermodynamicSummaryBlock>().Any(),
                "All-no-binding native report must omit the thermodynamic summary");
            if (purpose == ResultOutputPurpose.Diagnostic)
                Check(document.Sections.SelectMany(section => section.Blocks).OfType<AnalysisReportKeyValueBlock>()
                    .Single(block => block.Title == "Binding assessment and null comparison").Items
                    .Any(item => item.Label == "Member assessments" && item.Value == "No binding detected (manual)"),
                    "Native uniform collection should identify its manual verdict");
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "All-no-binding native PDF is empty");
        }
    }

    static void CheckDataAvailabilityReport()
    {
        var raw = Source("Native raw thermogram");
        raw.Name = "Native raw thermogram";
        raw.DataSourceFormat = ITCDataFormat.ITC200;
        var heatOnly = Source("Native heat-only fitted");
        heatOnly.Name = "Native heat-only fitted";
        heatOnly.DataPoints.Clear();
        heatOnly.DataSourceFormat = ITCDataFormat.OriginProject;
        var support = Source("Native heat-only supporting");
        support.Name = "Native heat-only supporting";
        support.DataPoints.Clear();
        support.DataSourceFormat = ITCDataFormat.Unknown;

        var memberParts = new[] { Result(raw), Result(heatOnly) };
        heatOnly.Injections[1].Include = false;
        heatOnly.Injections[2].IsIntegrated = false;
        var members = memberParts.SelectMany(part => part.Solution.Solutions).ToList();
        var convergence = members[0].Convergence;
        var globalModel = new GlobalModel(members.Select(member => member.Model).ToList())
        {
            Parameters = new GlobalModelParameters(),
            ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None },
        };
        foreach (var member in members) globalModel.Parameters.AddIndivdualParameter(member.Model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, members, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        var result = new AnalysisResult(global) { Name = "Native mixed availability" };

        var savedReport = new AnalysisITC.Core.Data.AnalysisReport { Name = "Native data availability QA" };
        savedReport.SetResultIds(new[] { result.UniqueID });
        savedReport.SetSupportingExperimentIds(new[] { support.UniqueID });
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        foreach (var purpose in new[] { ResultOutputPurpose.Standard, ResultOutputPurpose.Diagnostic })
        foreach (var includeInjectionTables in new[] { true, false })
        {
            var document = AnalysisReportBuilder.Build(savedReport,
                id => id == result.UniqueID ? result : null,
                id => id == support.UniqueID ? support : null,
                new AnalysisReportOptions
                {
                    OutputPurpose = purpose,
                    IncludeInjectionTables = includeInjectionTables,
                    GeneratedAtUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
                });
            var heatSection = document.Sections.Single(section => section.Id == "result-1-experiment-2");
            var availability = heatSection.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Single(block => block.Title == "Data availability");
            Check(availability.Items.Any(item => item.Label == "Input heats" && item.Value == "Imported integrated heats"),
                "Native heat-only member lost its imported-heats availability row");
            Check(availability.Items.Any(item => item.Label == "Injection use" && item.Value.Contains("excluded: 2")),
                "Native heat-only member lost its excluded-injection row");
            Check(availability.Items.Any(item => item.Label == "Available heats" && item.Value == "2 of 3 injections"),
                "Native heat-only member lost its available-heat count");
            Check(!heatSection.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Any(block => block.Title == "Processing and integration"),
                "Native heat-only member was described as processed in FT-ITC");
            var rawSection = document.Sections.Single(section => section.Id == "result-1-experiment-1");
            Check(rawSection.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Any(block => block.Title == "Processing and integration"),
                "Native raw thermogram lost its processing details");
            var supportSection = document.Sections.Single(section => section.Id == "supporting-experiments");
            Check(supportSection.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Any(block => block.Title == "Data availability"),
                "Native supporting heat-only experiment lost its availability block");
            Check(!supportSection.Blocks.OfType<AnalysisReportKeyValueBlock>()
                .Any(block => block.Title == "Processing and integration"),
                "Native supporting heat-only experiment was described as processed in FT-ITC");
            CheckAvailabilityFollowsDetails(heatSection);
            CheckAvailabilityFollowsDetails(supportSection);
            Check(heatSection.Blocks.OfType<AnalysisReportTableBlock>().Any(block => block.Title == "Injection table")
                == includeInjectionTables, "Native injection-table option did not affect heat-only report rendering");

            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan")
                .Invoke(renderer, new object[] { document });
            Check(plan.Pages.All(page => page.Fragments.Count > 0), "Native availability report has an empty page");
            foreach (var availabilityBlock in new[] { availability,
                supportSection.Blocks.OfType<AnalysisReportKeyValueBlock>().Single(block => block.Title == "Data availability") })
            {
                var fragments = plan.Pages.SelectMany(page => page.Fragments)
                    .Where(fragment => ReferenceEquals(fragment.Block, availabilityBlock)).ToList();
                Check(fragments.Count > 0 && fragments.Sum(fragment => fragment.ItemCount) == availabilityBlock.Items.Count,
                    "Native layout omitted availability rows");
            }
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData")
                .Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "Native availability report PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_DATA_AVAILABILITY_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
            {
                Directory.CreateDirectory(output);
                rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                    Path.Combine(output, "macos-availability-" + purpose.ToString().ToLowerInvariant()
                        + (includeInjectionTables ? "-tables.pdf" : "-no-tables.pdf")) });
            }
        }
    }

    static void CheckAvailabilityFollowsDetails(AnalysisReportSection section)
    {
        var blocks = section.Blocks.ToList();
        var details = blocks.FindIndex(block => block is AnalysisReportKeyValueBlock keyValue
            && keyValue.Title.StartsWith("Experiment details", StringComparison.Ordinal));
        var availability = blocks.FindIndex(block => block is AnalysisReportKeyValueBlock keyValue
            && keyValue.Title == "Data availability");
        Check(details >= 0 && availability == details + 1,
            "Native data availability must immediately follow experiment details");
    }

    static void CheckCoverSignOff()
    {
        var rendererType = typeof(MainWindowController).Assembly.GetType(
            "AnalysisITC.UI.MacOS.Drawing.CoreGraphicsAnalysisReportRenderer");
        var renderer = Activator.CreateInstance(rendererType, true);
        var results = new[] { Result(Source("signoff-first")), Result(Source("signoff-second")) };
        // Traceability Mode is global; restore it so later checks run with the original setting.
        var previousTraceability = AppSettings.TraceabilityModeEnabled;
        try
        {
        foreach (var resultCount in new[] { 1, 2 })
        foreach (var traceability in new[] { false, true })
        {
            AppSettings.TraceabilityModeEnabled = traceability;
            var document = AnalysisReportBuilder.Build(results.Take(resultCount).ToList(), new AnalysisReportOptions
            {
                Title = "A long custom report title for cover layout inspection",
                DocumentLabel = "Subtitle for visual inspection with Unicode λ β and wrapped content",
                Author = "Research collaborator Zoë with a deliberately long display name",
                ReportId = "Study-β-" + new string('0', 120),
                ExtraTraceability = traceability,
            });
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { document });
            var signOffs = plan.Pages.SelectMany(page => page.Fragments)
                .Where(fragment => fragment.Kind == AnalysisReportFragmentKind.SignOff).ToList();
            Check(signOffs.Count == (traceability ? 1 : 0), "Native signing block ignored traceability or was repeated");
            if (traceability)
            {
                var signOff = signOffs.Single();
                Check(plan.Pages[0].Fragments.Contains(signOff), "Native signing block moved off the front page");
                Check(Math.Abs(signOff.Bounds.Bottom - (plan.PageHeight - plan.MarginBottom - 18)) < 1e-6,
                    "Native signing block is not anchored above the footer");
                Check(plan.Pages[0].Fragments.Where(fragment => fragment != signOff)
                    .All(fragment => fragment.Bounds.Bottom <= signOff.Bounds.Y - 9 + 1e-6),
                    "Native cover content overlaps the signing block");
            }
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { document, plan }))
                Check(pdf.Length > 0, "Native signing block PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_SIGNOFF_QA_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(output))
                rendererType.GetMethod("WritePdf").Invoke(renderer, new object[] { document, plan,
                    Path.Combine(output, "macos-" + resultCount + (traceability ? "-on.pdf" : "-off.pdf")) });
        }
        }
        finally
        {
            AppSettings.TraceabilityModeEnabled = previousTraceability;
        }
    }

    static void CheckPicker(bool automatic)
    {
        DataManager.Clear(DataClearMode.ResetSession);
        AppSettings.AutoSelectReportReferenceExperiments = automatic;
        AppSettings.DilutionCalculationMethod = DilutionMethod.MicroCal;
        var first = Source("first"); var second = Source("second");
        var tandem = TandemConcatenation.ConcatTandem(new List<ExperimentData> { first, second });
        tandem.SetBufferSubtraction(first, BufferSubtractionMethod.MatchedInjection, notify: false);
        var result = Result(tandem);
        DataManager.AddData(new ITCDataContainer[] { second, first, tandem, result });
        using (var controller = (NSViewController)Activator.CreateInstance(ReportControllerType, new object[] { result }))
        using (var window = new NSWindow(new CGRect(0, 0, 1120, 720), NSWindowStyle.Titled, NSBackingStore.Buffered, false))
        {
            window.ContentView = controller.View;
            var supporting = (List<ExperimentData>)Field(controller, "selectedSupportingExperiments");
            Check(supporting.Select(data => data.UniqueID).SequenceEqual(automatic ? new[] { "second", "first" } : new string[0]),
                "Native initial source selection ignored the preference or project order");
            ReportControllerType.GetMethod("OpenResultPopover", PrivateInstance).Invoke(controller, null);
            var rows = (IDictionary)Field(controller, "resultPickerButtons");
            var sourceButton = Row(rows, first);
            var coveredButton = Row(rows, tandem);
            Check(sourceButton.ToolTip == "Buffer and tandem source for selected results"
                && sourceButton.Title.Contains("Buffer reference; Tandem source"), "Native source row role/tooltip differs from Core");
            Check(!coveredButton.Enabled && coveredButton.Title.Contains("Included through Result 1")
                && coveredButton.ToolTip == "Already reported with a selected result", "Native covered source row is selectable");
            var resultButton = Row(rows, result);
            Check(resultButton.ToolTip == "Reports this fit and its experiments", "Native result row tooltip differs from Core");
            resultButton.PerformClick(controller);
            Check(sourceButton.ToolTip == "Adds saved data as a supporting experiment"
                && !sourceButton.Title.Contains("Tandem source"),
                "Native role text did not update with the checked results");
            sourceButton.State = NSCellStateValue.Off;
            Row(rows, second).State = NSCellStateValue.Off;
            resultButton.PerformClick(controller);
            Check((sourceButton.State == NSCellStateValue.On) == automatic, "Native result check did not honor reference preselection");
            sourceButton.State = NSCellStateValue.Off;
            var popover = (NSPopover)Field(controller, "resultPopover");
            Buttons(popover.ContentViewController.View).Single(button => button.Title == "Apply").PerformClick(controller);
            Check(!supporting.Contains(first), "Native Apply added a removed tandem source back");
            var report = (AnalysisReport)Field(controller, "report");
            var document = AnalysisReportBuilder.Build(report, _ => result,
                id => DataManager.Data.FirstOrDefault(data => data.UniqueID == id));
            Check(!document.SupportingExperiments.Any(item => item.Id == "first"), "Renderer added an unchecked source");
            Check(document.IsValid, "Report failed after removing an available tandem source");
            // A separate complete selection renders the actual appendix role and missing-source note.
            var pdfReport = new AnalysisReport();
            pdfReport.SetResultIds(new[] { result.UniqueID });
            pdfReport.SetSupportingExperimentIds(new[] { first.UniqueID });
            var pdfDocument = AnalysisReportBuilder.Build(pdfReport, _ => result, id => id == "first" ? first : null);
            Check(pdfDocument.IsValid, "Missing recorded source prevented native report generation");
            var renderer = Field(controller, "renderer");
            var rendererType = renderer.GetType();
            var plan = (AnalysisReportLayoutPlan)rendererType.GetMethod("CreatePlan").Invoke(renderer, new object[] { pdfDocument });
            using (var pdf = (NSData)rendererType.GetMethod("CreatePdfData").Invoke(renderer, new object[] { pdfDocument, plan }))
                Check(pdf.Length > 0, "Native tandem report PDF is empty");
            var output = Environment.GetEnvironmentVariable("FTITC_TANDEM_REPORT_QA_DIRECTORY");
            if (automatic && !string.IsNullOrEmpty(output)) rendererType.GetMethod("WritePdf").Invoke(renderer,
                new object[] { pdfDocument, plan, Path.Combine(output, "macos.pdf") });
            window.Close();
        }
    }

    static object Field(object instance, string name) => instance.GetType().GetField(name, PrivateInstance).GetValue(instance);
    static NSButton Row(IDictionary rows, object data) => rows.Keys.Cast<NSButton>().Single(button => ReferenceEquals(rows[button], data));
    static IEnumerable<NSButton> Buttons(NSView view)
    {
        if (view is NSButton) yield return (NSButton)view;
        foreach (var child in view.Subviews)
            foreach (var button in Buttons(child)) yield return button;
    }

    static ExperimentData Source(string id)
    {
        var data = new ExperimentData(id + ".itc")
            { CellConcentration = new FloatWithError(10e-6), SyringeConcentration = new FloatWithError(100e-6), CellVolume = 200e-6, TargetTemperature = 25 };
        data.SetID(id);
        data.DataPoints.Add(new DataPoint(0, 0, 25)); data.DataPoints.Add(new DataPoint(120, 0, 25));
        for (var index = 0; index < 3; index++)
        {
            var injection = new InjectionData(data, index, 2e-6, 2e-10, true) { Time = 30 + index * 30 };
            injection.SetPeakArea(new FloatWithError(-2e-6, 1e-8)); data.Injections.Add(injection);
        }
        return data;
    }

    static AnalysisResult Result(ExperimentData data)
    {
        var model = new OneSetOfSites(data);
        model.InitializeParameters(data);
        model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
        model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
        model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -25_000);
        model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
        var convergence = SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            { Algorithm = SolverAlgorithm.LevenbergMarquardt, Termination = SolverTermination.Converged, Loss = 1 });
        model.Solution = SolutionInterface.FromModel(model, convergence);
        var globalModel = new GlobalModel(new List<Model> { model })
            { Parameters = new GlobalModelParameters(), ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.None } };
        globalModel.Parameters.AddIndivdualParameter(model.Parameters);
        var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, new List<SolutionInterface> { model.Solution }, convergence,
            reconstructBootstrap: false);
        globalModel.Solution = global;
        return new AnalysisResult(global);
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
