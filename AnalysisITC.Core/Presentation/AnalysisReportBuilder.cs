using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Presentation
{
    public static class AnalysisReportBuilder
    {
        const double ContactSheetWidthCentimeters = 18.0;
        const double ContactSheetHeightCentimeters = 15.0;
        const double ExpandedFigureWidthCentimeters = 15.0;
        const double ExpandedFigureHeightCentimeters = 19.5;

        public static AnalysisReportDocument Build(
            AnalysisResult result,
            AnalysisReportOptions options = null)
        {
            options = options ?? new AnalysisReportOptions();
            if (options.EnergyUnitOverride.HasValue)
                EnergyUnitResolver.ValidateOverride(options.EnergyUnitOverride.Value);

            var document = CreateDocument(result, options);
            var validation = Validate(result);
            foreach (var diagnostic in validation.Diagnostics)
                document.AddDiagnostic(diagnostic.Severity, diagnostic.Code, diagnostic.Message);
            if (!validation.IsValid) return document;

            var overview = AnalysisResultOverviewTable.Build(
                result,
                options.EnergyUnitFamily,
                options.EnergyUnitOverride,
                options.UseKelvin,
                options.UncertaintyDisplayStyle);
            var members = result.Solution.Solutions
                .Where(solution => solution?.Data != null)
                .ToList();
            var labels = members
                .Select((_, index) => PublicationFigureCanvasBuilder.PanelLabel(index))
                .ToList();

            BuildCover(document, result, members, labels, options);
            BuildSummary(document, result, overview, options);
            BuildExperimentSections(document, result, members, labels, overview, options);
            BuildAdvancedSections(document, result, options);
            AddResultDiagnostics(document, result);
            BuildAppendix(document, result, members, overview, options);

            return document;
        }

        public static IReadOnlyList<AnalysisReportAdvancedSectionDescriptor> GetAvailableAdvancedSections(
            AnalysisResult result)
        {
            var output = new List<AnalysisReportAdvancedSectionDescriptor>();
            if (result?.Solution?.Solutions == null) return output;

            if (CanBuildTemperaturePlot(result))
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.TemperatureDependence,
                    "Temperature dependence",
                    "Thermodynamic parameters and their saved temperature dependences."));
            }

            if (result.SpolarRecordAnalysis?.Result != null)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.SpolarRecord,
                    "Spolar Record",
                    "Saved hydration, conformational, and residue estimates."));
            }

            if (CanBuildAffinitySaltPlot(result))
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.AffinityVersusSalt,
                    "Affinity versus salt",
                    "Reported affinity as a function of salt concentration."));
            }

            if (result.ElectrostaticsAnalysis?.Calculated == true
                && result.ElectrostaticsAnalysis.IonicStrengthDependenceFit != null)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.DebyeHuckel,
                    "Debye-Huckel dependence",
                    "Saved ionic-strength dependence and fitted curve."));
            }

            if (result.ElectrostaticsAnalysis?.Calculated == true
                && result.ElectrostaticsAnalysis.CounterIonReleaseFit != null)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.CounterIonRelease,
                    "Counter-ion release",
                    "Saved counter-ion release dependence and fitted line."));
            }

            if (result.ProtonationAnalysis?.Fit is LinearFitWithError)
            {
                output.Add(Descriptor(
                    AnalysisReportAdvancedSectionKind.Protonation,
                    "Protonation dependence",
                    "Saved buffer-protonation dependence and fitted line."));
            }

            AddCorrelationDescriptors(output, result);
            return output;
        }

        static AnalysisReportDocument CreateDocument(
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var resultName = result?.Name ?? "";
            return new AnalysisReportDocument
            {
                DocumentLabel = options.DocumentLabel?.Trim() ?? "",
                Title = string.IsNullOrWhiteSpace(options.Title) ? resultName : options.Title.Trim(),
                ResultName = resultName,
                ResultId = result?.UniqueID ?? "",
                ResultDate = result?.Date ?? default(DateTime),
                GeneratedAtUtc = options.GeneratedAtUtc.Kind == DateTimeKind.Utc
                    ? options.GeneratedAtUtc
                    : options.GeneratedAtUtc.ToUniversalTime(),
                Creator = MarkdownStrings.AppName,
                ApplicationVersion = options.ApplicationVersion ?? "",
            };
        }

        public static AnalysisReportValidationResult Validate(AnalysisResult result)
        {
            var diagnostics = new List<AnalysisReportDiagnostic>();
            if (result == null)
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-result", "No saved analysis result was supplied."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            if (result.Solution?.Model == null)
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-solution", "The saved analysis result has no usable fitted model."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            if (result.Solution.Solutions == null
                || !result.Solution.Solutions.Any(solution => solution?.Data != null))
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-members", "The saved analysis result contains no usable experiment fits."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            var reportValues = result.Solution.Solutions
                .Where(solution => solution?.Data != null)
                .SelectMany(solution => solution.ReportParameters?.Values
                    ?? Enumerable.Empty<FloatWithError>())
                .ToList();
            if (reportValues.Count == 0)
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "missing-parameters", "The saved analysis result contains no reportable fitted parameters."));
                return new AnalysisReportValidationResult(diagnostics);
            }
            if (reportValues.Any(value => FloatWithError.IsNaN(value) || !IsFinite(value.Value)))
            {
                diagnostics.Add(new AnalysisReportDiagnostic(AnalysisReportDiagnosticSeverity.Error,
                    "non-finite-parameters", "The saved analysis result contains non-finite reported parameter values."));
                return new AnalysisReportValidationResult(diagnostics);
            }

            return new AnalysisReportValidationResult(diagnostics);
        }

        static void BuildCover(
            AnalysisReportDocument document,
            AnalysisResult result,
            IReadOnlyList<SolutionInterface> members,
            IReadOnlyList<string> labels,
            AnalysisReportOptions options)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.Cover,
                "cover",
                document.Title,
                AnalysisReportLayoutPolicy.KeepTogether
                    | AnalysisReportLayoutPolicy.ShrinkToSinglePage);

            if (!string.IsNullOrWhiteSpace(document.DocumentLabel))
                section.Add(new AnalysisReportHeadingBlock(document.DocumentLabel, 2));

            section.Add(new AnalysisReportKeyValueBlock("Analysis", new[]
            {
                Item("Result", result.Name),
                Item("Date", FormatDate(result.Date)),
                Item("Model", ModelName(result)),
                Item("Experiments", members.Count.ToString(CultureInfo.CurrentCulture)),
                Item("Status", HealthLabel(result.Health)),
            }));

            AddValidityNotice(section, result);
            if (!string.IsNullOrWhiteSpace(result.Comments))
                section.Add(new AnalysisReportTextBlock("Comments", result.Comments,
                    AnalysisReportLayoutPolicy.KeepTogether));

            var grid = ChooseContactGrid(members.Count);
            var figureOptions = ContactFigureOptions(options, grid.columns, grid.rows);
            var cells = new List<AnalysisReportContactSheetCell>();
            for (var index = 0; index < members.Count; index++)
            {
                var source = new PublicationFigureSource(members[index].Data, members[index]);
                var snapshot = PublicationFigureBuilder.Build(source, CloneFigureOptions(figureOptions));
                cells.Add(new AnalysisReportContactSheetCell(
                    index / grid.columns,
                    index % grid.columns,
                    labels[index],
                    members[index].Data.Name,
                    snapshot));
            }
            section.Add(new AnalysisReportContactSheetBlock(
                "Experiment overview", grid.rows, grid.columns, cells));

            document.AddSection(section);
        }

        static void BuildSummary(
            AnalysisReportDocument document,
            AnalysisResult result,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.AnalysisSummary,
                "analysis-summary",
                "Analysis summary",
                AnalysisReportLayoutPolicy.StartOnNewPage
                    | AnalysisReportLayoutPolicy.AllowContinuation);

            section.Add(OverviewTableBlock(overview));

            var evaluationTemperature = AnalysisResultParameterEvaluator
                .DefaultEvaluationTemperatureCelsius(result);
            var evaluation = AnalysisResultParameterEvaluator.Evaluate(
                result,
                evaluationTemperature,
                options.EnergyUnitFamily,
                options.EnergyUnitOverride,
                options.UncertaintyDisplayStyle);
            if (evaluation.IsAvailable)
            {
                section.Add(new AnalysisReportKeyValueBlock(
                    "Reported parameters at " + FormatTemperature(evaluationTemperature, options.UseKelvin),
                    evaluation.Rows.Select(row => Item(row.Label, row.Value))));
            }

            section.Add(new AnalysisReportKeyValueBlock("Model", BuildModelItems(result)));
            section.Add(new AnalysisReportKeyValueBlock("Fit diagnostics", BuildFitDiagnosticItems(result)));
            document.AddSection(section);
        }

        static void BuildExperimentSections(
            AnalysisReportDocument document,
            AnalysisResult result,
            IReadOnlyList<SolutionInterface> members,
            IReadOnlyList<string> labels,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options)
        {
            for (var index = 0; index < members.Count; index++)
            {
                var solution = members[index];
                var data = solution.Data;
                var label = labels[index];
                var section = new AnalysisReportSection(
                    AnalysisReportSectionKind.Experiment,
                    "experiment-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    label + ". " + data.Name,
                    AnalysisReportLayoutPolicy.StartOnNewPage
                        | AnalysisReportLayoutPolicy.AllowContinuation);

                var figure = PublicationFigureBuilder.Build(
                    new PublicationFigureSource(data, solution),
                    ExpandedFigureOptions(options));
                section.Add(new AnalysisReportFigureBlock(
                    "Fit overview", label, figure, AnalysisReportLayoutPolicy.KeepTogether));
                section.Add(new AnalysisReportKeyValueBlock(
                    "Experiment details", BuildExperimentMetadata(data, options)));
                section.Add(new AnalysisReportKeyValueBlock(
                    "Processing and integration", BuildProcessingItems(data)));
                section.Add(BuildParameterTable(
                    "Fitted and derived parameters", solution, overview, options));
                section.Add(new AnalysisReportKeyValueBlock(
                    "Fit details", BuildMemberFitItems(solution)));
                if (!string.IsNullOrWhiteSpace(data.Comments))
                    section.Add(new AnalysisReportTextBlock("Comments", data.Comments,
                        AnalysisReportLayoutPolicy.AllowContinuation));

                document.AddSection(section);
            }
        }

        static void BuildAdvancedSections(
            AnalysisReportDocument document,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var available = GetAvailableAdvancedSections(result)
                .ToDictionary(item => item.Request.Key, item => item);
            var requests = (options.AdvancedSections
                    ?? new List<AnalysisReportAdvancedSectionRequest>())
                .Where(request => request != null)
                .GroupBy(request => request.Key)
                .Select(group => group.First())
                .ToList();
            var temperaturePlotAdded = false;

            foreach (var request in requests)
            {
                if (!available.TryGetValue(request.Key, out var descriptor))
                {
                    var message = UnavailableAdvancedMessage(result, request);
                    document.AddDiagnostic(
                        AnalysisReportDiagnosticSeverity.Warning,
                        "advanced-section-omitted",
                        message);
                    continue;
                }

                var section = new AnalysisReportSection(
                    AnalysisReportSectionKind.AdvancedAnalysis,
                    "advanced-" + NormalizeId(request.Key),
                    descriptor.Title,
                    AnalysisReportLayoutPolicy.StartOnNewPage
                        | AnalysisReportLayoutPolicy.AllowContinuation);

                switch (request.Kind)
                {
                    case AnalysisReportAdvancedSectionKind.TemperatureDependence:
                        if (!temperaturePlotAdded)
                        {
                            section.Add(BuildTemperaturePlot(result, options));
                            temperaturePlotAdded = true;
                            AddTemperatureParameters(section, result, options);
                        }
                        break;
                    case AnalysisReportAdvancedSectionKind.SpolarRecord:
                        AddSpolarRecord(section, result, options);
                        if (!temperaturePlotAdded && CanBuildTemperaturePlot(result))
                        {
                            section.Add(BuildTemperaturePlot(result, options));
                            temperaturePlotAdded = true;
                        }
                        break;
                    case AnalysisReportAdvancedSectionKind.AffinityVersusSalt:
                        section.Add(BuildAffinitySaltPlot(result));
                        break;
                    case AnalysisReportAdvancedSectionKind.DebyeHuckel:
                        section.Add(BuildDebyeHuckelPlot(result));
                        AddElectrostaticsParameters(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.CounterIonRelease:
                        section.Add(BuildCounterIonReleasePlot(result));
                        AddElectrostaticsParameters(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.Protonation:
                        section.Add(BuildProtonationPlot(result, options));
                        AddProtonationParameters(section, result, options);
                        break;
                    case AnalysisReportAdvancedSectionKind.Correlation:
                        AddCorrelation(section, result, request.CorrelationMemberIndex);
                        break;
                }

                if (section.Blocks.Count > 0) document.AddSection(section);
            }
        }

        static void BuildAppendix(
            AnalysisReportDocument document,
            AnalysisResult result,
            IReadOnlyList<SolutionInterface> members,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options)
        {
            var section = new AnalysisReportSection(
                AnalysisReportSectionKind.Appendix,
                "appendix",
                "Appendix",
                AnalysisReportLayoutPolicy.StartOnNewPage
                    | AnalysisReportLayoutPolicy.AllowContinuation);

            section.Add(new AnalysisReportKeyValueBlock(
                "Analysis configuration", BuildConfigurationItems(result)));
            section.Add(BuildProvenanceTable(members, options));

            var notes = new List<string>
            {
                "Reported central parameter values are the best fit to the original data. " +
                "Bootstrap or profile-likelihood calculations determine uncertainty and do not replace the reported estimate.",
                "RMSD values are unweighted display diagnostics. Weighted fitting, when enabled, uses a distinct optimization objective.",
                "Full raw and injection-level numeric data are intentionally excluded from this report and remain available through data export.",
            };
            section.Add(new AnalysisReportTextBlock(
                "Scientific notes",
                string.Join(Environment.NewLine + Environment.NewLine, notes),
                AnalysisReportLayoutPolicy.AllowContinuation));

            var validity = result.ValidityReport;
            if (validity?.Reasons?.Count > 0)
            {
                section.Add(new AnalysisReportTextBlock(
                    "Validity details",
                    string.Join(Environment.NewLine, validity.Reasons),
                    AnalysisReportLayoutPolicy.AllowContinuation));
            }

            if (document.Warnings.Count > 0)
            {
                section.Add(new AnalysisReportNoticeBlock(
                    "Report warnings",
                    string.Join(Environment.NewLine, document.Warnings),
                    AnalysisReportNoticeLevel.Warning));
            }

            section.Add(new AnalysisReportKeyValueBlock("Provenance", new[]
            {
                Item("Created by", document.Creator),
                Item("Application version", document.ApplicationVersion),
                Item("Generated (UTC)", document.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture)),
                Item("Result identifier", document.ResultId),
            }));

            document.AddSection(section);
        }

        static AnalysisReportTableBlock OverviewTableBlock(AnalysisResultOverviewTable overview)
        {
            var columns = overview.Columns.Select(column => new AnalysisReportTableColumn(
                column.Id, column.Title, column.Alignment));
            var rows = overview.Rows.Select(row => new AnalysisReportTableRow(
                overview.Columns.Select(column => row[column.Id])));
            return new AnalysisReportTableBlock(
                "Experiment parameter overview",
                columns,
                rows,
                AnalysisReportLayoutPolicy.KeepTogether
                    | AnalysisReportLayoutPolicy.ShrinkToSinglePage);
        }

        static AnalysisReportTableBlock BuildParameterTable(
            string title,
            SolutionInterface solution,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options)
        {
            var parameters = new Dictionary<ParameterType, FloatWithError>(
                solution?.ReportParameters ?? new Dictionary<ParameterType, FloatWithError>());
            if (solution?.Parameters != null
                && solution.Parameters.TryGetValue(ParameterType.Offset, out var offset))
                parameters[ParameterType.Offset] = offset;

            var columns = new[]
            {
                new AnalysisReportTableColumn("Parameter", "Parameter"),
                new AnalysisReportTableColumn("Type", "Type"),
                new AnalysisReportTableColumn("Value", "Value", AnalysisResultColumnAlignment.Right),
                new AnalysisReportTableColumn("SD", "SD", AnalysisResultColumnAlignment.Right),
                new AnalysisReportTableColumn("Interval", "95% interval", AnalysisResultColumnAlignment.Right),
                new AnalysisReportTableColumn("Unit", "Unit"),
            };
            var rows = parameters
                .OrderBy(item => ParameterOrder(item.Key))
                .Select(item =>
                {
                    var formatted = FormatParameter(item.Key, item.Value, solution, overview, options);
                    return new AnalysisReportTableRow(new[]
                    {
                        ParameterLabel(item.Key),
                        IsDerivedParameter(item.Key) ? "Derived" : "Fitted",
                        formatted.value,
                        formatted.sd,
                        formatted.interval,
                        formatted.unit,
                    });
                });
            return new AnalysisReportTableBlock(
                title, columns, rows, AnalysisReportLayoutPolicy.AllowContinuation);
        }

        static AnalysisReportTableBlock BuildProvenanceTable(
            IReadOnlyList<SolutionInterface> members,
            AnalysisReportOptions options)
        {
            var columns = new[]
            {
                new AnalysisReportTableColumn("Experiment", "Experiment"),
                new AnalysisReportTableColumn("File", "Source file"),
                new AnalysisReportTableColumn("Temperature", options.UseKelvin ? "Temperature (K)" : "Temperature (°C)", AnalysisResultColumnAlignment.Right),
                new AnalysisReportTableColumn("Cell", "Cell concentration", AnalysisResultColumnAlignment.Right),
                new AnalysisReportTableColumn("Syringe", "Syringe concentration", AnalysisResultColumnAlignment.Right),
                new AnalysisReportTableColumn("Injections", "Injections", AnalysisResultColumnAlignment.Right),
            };
            var rows = members.Select(solution => new AnalysisReportTableRow(new[]
            {
                solution.Data.Name,
                solution.Data.FileName,
                FormatTemperature(solution.Data.MeasuredTemperature, options.UseKelvin, includeUnit: false),
                solution.Data.CellConcentration.AsFormattedConcentration(true),
                solution.Data.SyringeConcentration.AsFormattedConcentration(true),
                solution.Data.InjectionCount.ToString(CultureInfo.CurrentCulture),
            }));
            return new AnalysisReportTableBlock(
                "Input provenance", columns, rows, AnalysisReportLayoutPolicy.AllowContinuation);
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildModelItems(AnalysisResult result)
        {
            var properties = result.Model.ModelType.GetProperties();
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Model", properties?.Name ?? result.Model.ModelType.ToString()),
                Item("Description", properties?.Description ?? ""),
                Item("Fitted parameters", result.Model.NumberOfParameters.ToString(CultureInfo.CurrentCulture)),
                Item("Analysis", result.Model.Parameters.RequiresGlobalFitting ? "Global" : "Individual"),
            };
            foreach (var option in result.Model.ModelOptions
                ?? new Dictionary<AttributeKey, ExperimentAttribute>())
            {
                items.Add(Item(
                    "Option: " + (option.Value?.GetDisplayName()
                        ?? option.Key.GetProperties()?.Name
                        ?? option.Key.ToString()),
                    option.Value?.GetDisplayValue() ?? "Unavailable"));
            }
            foreach (var constraint in result.Model.Parameters.Constraints
                .Where(item => item.Value != VariableConstraint.None))
            {
                items.Add(Item(
                    "Constraint: " + ParameterLabel(constraint.Key),
                    constraint.Value.GetEnumDescription()));
            }
            return items;
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildFitDiagnosticItems(AnalysisResult result)
        {
            var solution = result.Solution;
            var convergence = solution.Convergence;
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Fitting", solution.UseWeightedFitting ? "Weighted injection errors" : "Unweighted"),
                Item("Unweighted RMSD", FormatFinite(solution.Loss, "G5") + " µJ"),
            };
            if (solution.MolarRMSD.HasValue)
                items.Add(Item("Molar RMSD", solution.MolarRMSD.Value.ToFormattedString(
                    EnergyUnit.KiloJoule, withunit: true, permole: true)));
            if (convergence != null)
            {
                items.Add(Item("Algorithm", convergence.Algorithm.GetProperties()?.Name ?? convergence.Algorithm.ToString()));
                items.Add(Item("Termination", convergence.Termination.GetEnumDescription()));
                items.Add(Item("Iterations", convergence.Iterations.ToString(CultureInfo.CurrentCulture)));
                items.Add(Item("Elapsed", FormatDuration(convergence.TotalTime)));
                items.Add(Item("Uncertainty method", solution.ErrorEstimationMethod.Description()));
                items.Add(Item("Uncertainty outcome", convergence.ErrorEstimationOutcome.GetEnumDescription()));
                if (!string.IsNullOrWhiteSpace(convergence.ErrorEstimationSummary))
                    items.Add(Item("Uncertainty summary", convergence.ErrorEstimationSummary));
                if (convergence.ErrorEstimationAttemptedRefits.HasValue)
                    items.Add(Item("Uncertainty refits attempted", convergence.ErrorEstimationAttemptedRefits.Value.ToString(CultureInfo.CurrentCulture)));
                if (convergence.ErrorEstimationSucceededRefits.HasValue)
                    items.Add(Item("Uncertainty refits succeeded", convergence.ErrorEstimationSucceededRefits.Value.ToString(CultureInfo.CurrentCulture)));
                if (convergence.ErrorEstimationFailedRefits.HasValue)
                    items.Add(Item("Uncertainty refits failed", convergence.ErrorEstimationFailedRefits.Value.ToString(CultureInfo.CurrentCulture)));
            }

            var criteria = result.InformationCriteria;
            if (criteria != null)
            {
                items.Add(Item("AIC", criteria.IsAicAvailable
                    ? FormatFinite(criteria.Aic.Value, "G6")
                    : criteria.AicUnavailableReason));
                items.Add(Item("AICc", criteria.IsAiccAvailable
                    ? FormatFinite(criteria.Aicc.Value, "G6")
                    : criteria.AiccUnavailableReason));
            }
            return items;
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildExperimentMetadata(
            ExperimentData data,
            AnalysisReportOptions options)
        {
            var instrument = data.Instrument.GetProperties()?.Name;
            return new[]
            {
                Item("Source file", data.FileName),
                Item("Date", FormatDate(data.Date)),
                Item("Instrument", string.IsNullOrWhiteSpace(instrument) ? "Unknown" : instrument),
                Item("Measured temperature", FormatTemperature(data.MeasuredTemperature, options.UseKelvin)),
                Item("Target temperature", FormatTemperature(data.TargetTemperature, options.UseKelvin)),
                Item("Cell concentration", data.CellConcentration.AsFormattedConcentration(true)),
                Item("Syringe concentration", data.SyringeConcentration.AsFormattedConcentration(true)),
                Item("Cell volume", FormatFinite(1_000_000 * data.CellVolume, "G5") + " µL"),
                Item("Injections", data.InjectionCount.ToString(CultureInfo.CurrentCulture)),
            };
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildProcessingItems(ExperimentData data)
        {
            var injections = data.Injections ?? new List<InjectionData>();
            var included = injections.Count(injection => injection.Include);
            var excluded = injections.Where(injection => !injection.Include)
                .Select(injection => (injection.ID + 1).ToString(CultureInfo.CurrentCulture))
                .ToList();
            var integrated = injections.Count(injection => injection.IsIntegrated);
            var range = IntegrationRange(injections);
            return new[]
            {
                Item("Baseline method", data.Processor?.BaselineType.ToString() ?? BaselineInterpolatorTypes.None.ToString()),
                Item("Baseline completed", data.Processor?.BaselineCompleted == true ? "Yes" : "No"),
                Item("Integration mode", data.Processor?.IntegrationLengthMode.ToString() ?? "Unavailable"),
                Item("Integrated injections", integrated + " of " + injections.Count),
                Item("Included injections", included.ToString(CultureInfo.CurrentCulture)),
                Item("Excluded injections", excluded.Count == 0 ? "None" : string.Join(", ", excluded)),
                Item("Integration regions", range),
            };
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildMemberFitItems(SolutionInterface solution)
        {
            var convergence = solution.Convergence;
            var items = new List<AnalysisReportKeyValueItem>
            {
                Item("Status", solution.IsValid ? "Valid" : "Invalid"),
                Item("Fitting", solution.UseWeightedFitting ? "Weighted injection errors" : "Unweighted"),
                Item("Unweighted RMSD", FormatFinite(solution.Loss, "G5") + " µJ"),
                Item("Uncertainty method", solution.ErrorMethod.Description()),
            };
            if (solution.MolarRMSD.HasValue)
                items.Add(Item("Molar RMSD", solution.MolarRMSD.Value.ToFormattedString(
                    EnergyUnit.KiloJoule, withunit: true, permole: true)));
            if (convergence != null)
            {
                items.Add(Item("Termination", convergence.Termination.GetEnumDescription()));
                items.Add(Item("Iterations", convergence.Iterations.ToString(CultureInfo.CurrentCulture)));
                items.Add(Item("Uncertainty outcome", convergence.ErrorEstimationOutcome.GetEnumDescription()));
            }
            return items;
        }

        static IEnumerable<AnalysisReportKeyValueItem> BuildConfigurationItems(AnalysisResult result)
        {
            var output = new List<AnalysisReportKeyValueItem>();
            var options = result.Model.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>();
            foreach (var option in options)
            {
                output.Add(Item(
                    option.Value?.GetDisplayName() ?? option.Key.GetProperties()?.Name ?? option.Key.ToString(),
                    option.Value?.GetDisplayValue() ?? "Unavailable"));
            }

            foreach (var constraint in result.Model.Parameters.Constraints
                .Where(item => item.Value != VariableConstraint.None))
            {
                output.Add(Item(
                    "Constraint: " + ParameterLabel(constraint.Key),
                    constraint.Value.GetEnumDescription()));
            }

            output.AddRange(BuildFitDiagnosticItems(result));
            return output;
        }

        static void AddValidityNotice(AnalysisReportSection section, AnalysisResult result)
        {
            var report = result.ValidityReport;
            var reasons = report?.Reasons == null || report.Reasons.Count == 0
                ? "No validity details were recorded."
                : string.Join(Environment.NewLine, report.Reasons);
            var level = result.Health == AnalysisResultHealth.Valid
                ? AnalysisReportNoticeLevel.Information
                : result.Health == AnalysisResultHealth.Invalid
                    ? AnalysisReportNoticeLevel.Error
                    : AnalysisReportNoticeLevel.Warning;
            section.Add(new AnalysisReportNoticeBlock(
                HealthLabel(result.Health), reasons, level));
        }

        static void AddResultDiagnostics(AnalysisReportDocument document, AnalysisResult result)
        {
            if (result.Health != AnalysisResultHealth.Valid)
            {
                document.AddDiagnostic(
                    AnalysisReportDiagnosticSeverity.Warning,
                    "result-health",
                    "The saved result is reported with status: " + HealthLabel(result.Health) + ".");
            }

            foreach (var solution in result.Solution.Solutions.Where(solution => solution != null))
            {
                if (solution.ParameterBoundaryHit)
                    document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning,
                        "parameter-boundary", (solution.Data?.Name ?? "Experiment") +
                        " has a fitted parameter at a boundary.");
                if (solution.BootstrapParameterBoundaryHit)
                    document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning,
                        "bootstrap-boundary", (solution.Data?.Name ?? "Experiment") +
                        " has bootstrap estimates at a parameter boundary.");
                if (solution.Convergence?.HasErrorEstimationLimitWarnings == true)
                    document.AddDiagnostic(AnalysisReportDiagnosticSeverity.Warning,
                        "uncertainty-limit", (solution.Data?.Name ?? "Experiment") +
                        " has limit-terminated uncertainty refits.");
            }
        }

        static void AddCorrelationDescriptors(
            ICollection<AnalysisReportAdvancedSectionDescriptor> output,
            AnalysisResult result)
        {
            try
            {
                var shared = new BootstrapCorrelationAnalyzer().Analyze(result);
                if (shared?.IsAvailable == true)
                {
                    output.Add(new AnalysisReportAdvancedSectionDescriptor(
                        new AnalysisReportAdvancedSectionRequest(
                            AnalysisReportAdvancedSectionKind.Correlation),
                        result.Solution.Solutions.Count > 1
                            ? "Shared parameter correlation"
                            : "Parameter correlation",
                        "Correlation calculated from saved residual-bootstrap fits."));
                }

                if (result.Solution.Solutions.Count <= 1) return;
                for (var index = 0; index < result.Solution.Solutions.Count; index++)
                {
                    var member = new BootstrapCorrelationAnalyzer().Analyze(result, index);
                    if (member?.IsAvailable != true) continue;
                    var name = result.Solution.Solutions[index]?.Data?.Name ??
                        "Experiment " + (index + 1).ToString(CultureInfo.CurrentCulture);
                    output.Add(new AnalysisReportAdvancedSectionDescriptor(
                        new AnalysisReportAdvancedSectionRequest(
                            AnalysisReportAdvancedSectionKind.Correlation, index),
                        "Correlation: " + name,
                        "Shared and local parameter correlation calculated from saved residual-bootstrap fits."));
                }
            }
            catch
            {
                // Correlation is optional. Damaged legacy bootstrap content must not
                // prevent the rest of the report from being built.
            }
        }

        static void AddCorrelation(
            AnalysisReportSection section,
            AnalysisResult result,
            int? memberIndex)
        {
            var correlation = memberIndex.HasValue
                ? new BootstrapCorrelationAnalyzer().Analyze(result, memberIndex.Value)
                : new BootstrapCorrelationAnalyzer().Analyze(result);
            var labels = correlation.Parameters.Select(parameter =>
            {
                var prefix = parameter.IsShared ? "Global · "
                    : parameter.IsMember ? "Experiment · " : "";
                return prefix + parameter.Label;
            });
            var notes = BootstrapCorrelationDiagnosticFormatter.ReliabilityWarnings(correlation).ToList();
            notes.Insert(0, "Residual bootstrap (Pearson); " +
                correlation.UsedReplicateCount.ToString(CultureInfo.CurrentCulture) +
                " complete replicates.");
            section.Add(new AnalysisReportCorrelationMatrixBlock(
                section.Title, labels, correlation.CorrelationMatrix, notes));
        }

        static AnalysisReportPlotBlock BuildTemperaturePlot(
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var dependences = result.Solution.TemperatureDependence;
            var parameters = ThermodynamicParameterSlots.OrderedKeys(
                dependences.Keys,
                ThermodynamicParameterFamily.Enthalpy,
                ThermodynamicParameterFamily.EntropyContribution,
                ThermodynamicParameterFamily.Gibbs);
            var unit = ResolveMolarEnergyUnit(result, options);
            var scale = Energy.ScaleFactor(unit);
            var series = new List<AnalysisReportPlotSeries>();
            foreach (var parameter in parameters)
            {
                if (!dependences.TryGetValue(parameter, out var fit)) continue;
                var group = parameter.ToString();
                var points = result.Solution.Solutions
                    .Where(solution => solution != null
                        && IsFinite(solution.Temp)
                        && solution.ReportParameters.ContainsKey(parameter)
                        && IsFinite(solution.ReportParameters[parameter].Value))
                    .OrderBy(solution => solution.Temp)
                    .Select(solution => PlotPoint(
                        DisplayTemperature(solution.Temp, options.UseKelvin),
                        solution.ReportParameters[parameter],
                        scale,
                        solution.Data?.Name))
                    .ToList();
                if (points.Count == 0) continue;

                var label = ParameterLabel(parameter);
                series.Add(new AnalysisReportPlotSeries(
                    label, AnalysisReportPlotSeriesKind.Points, points, group));
                var domain = PlotDomain(points.Select(point => point.X));
                var displayXs = Sample(domain.min, domain.max, 81);
                var modelXs = options.UseKelvin
                    ? displayXs.Select(value => value - 273.15).ToArray()
                    : displayXs;
                var bootstrapFits = result.Solution.BootstrapSolutions?
                    .Where(solution => solution?.TemperatureDependence?.ContainsKey(parameter) == true)
                    .Select(solution => solution.TemperatureDependence[parameter])
                    .ToList();
                var envelope = LinearFitEnvelopeBuilder.Build(fit, bootstrapFits, modelXs);
                series.Add(new AnalysisReportPlotSeries(
                    label,
                    AnalysisReportPlotSeriesKind.Line,
                    envelope.Select(point => new AnalysisReportPlotPoint(
                        DisplayTemperature(point.X, options.UseKelvin),
                        point.Center * scale,
                        point.HasBand ? (double?)(point.Lower * scale) : null,
                        point.HasBand ? (double?)(point.Upper * scale) : null)),
                    group));
            }

            return new AnalysisReportPlotBlock(
                "Temperature dependence",
                options.UseKelvin ? "Temperature (K)" : "Temperature (°C)",
                "Thermodynamic parameter (" + unit.GetUnit() + "/mol)",
                series);
        }

        static void AddTemperatureParameters(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var temperature = AnalysisResultParameterEvaluator.DefaultEvaluationTemperatureCelsius(result);
            var evaluation = AnalysisResultParameterEvaluator.Evaluate(
                result, temperature, options.EnergyUnitFamily,
                options.EnergyUnitOverride, options.UncertaintyDisplayStyle);
            if (evaluation.IsAvailable)
                section.Add(new AnalysisReportKeyValueBlock(
                    "Parameters at " + FormatTemperature(temperature, options.UseKelvin),
                    evaluation.Rows.Select(row => Item(row.Label, row.Value))));
        }

        static void AddSpolarRecord(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.SpolarRecordAnalysis;
            var output = analysis.Result;
            var temperature = output.ReferenceTemperature.Value;
            var unit = ResolveMolarEnergyUnit(result, options);
            section.Add(new AnalysisReportKeyValueBlock("Saved result", new[]
            {
                Item("Folded mode", SpolarFoldedMode(analysis)),
                Item("Temperature mode", SpolarTemperatureMode(analysis)),
                Item("Reference temperature", FormatTemperature(temperature, options.UseKelvin)),
                Item("Hydration contribution", new Energy(output.HydrationContribution(temperature))
                    .ToFormattedString(unit, permole: true, style: options.UncertaintyDisplayStyle)),
                Item("Conformational contribution", new Energy(output.ConformationalContribution(temperature))
                    .ToFormattedString(unit, permole: true, style: options.UncertaintyDisplayStyle)),
                Item("Residue estimate", output.Rvalue.ToString("G5", options.UncertaintyDisplayStyle)),
                Item("Iterations", analysis.CompletedIterations.ToString(CultureInfo.CurrentCulture)),
                Item("Completed", FormatNullableDate(analysis.CompletedAtUtc)),
            }));
        }

        static AnalysisReportPlotBlock BuildAffinitySaltPlot(AnalysisResult result)
        {
            var unit = result.AppropriateAffinityUnit;
            var points = result.Solution.Solutions
                .Where(solution => solution?.Data != null
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                .Select(solution =>
                {
                    var salt = solution.Data.Attributes
                        .Find(attribute => attribute.Key == AttributeKey.Salt);
                    return salt == null ? null : PlotPoint(
                        1000 * salt.ParameterValue.Value,
                        solution.ReportParameters[ParameterType.Affinity1],
                        unit.GetMod(), solution.Data.Name);
                })
                .Where(point => point != null)
                .OrderBy(point => point.X)
                .ToList();
            return new AnalysisReportPlotBlock(
                "Affinity versus salt", "Salt concentration (mM)",
                "Kd (" + unit.GetName() + ")",
                new[] { new AnalysisReportPlotSeries(
                    "Saved observations", AnalysisReportPlotSeriesKind.Points, points) });
        }

        static AnalysisReportPlotBlock BuildDebyeHuckelPlot(AnalysisResult result)
        {
            var analysis = result.ElectrostaticsAnalysis;
            var points = result.Solution.Solutions
                .Where(solution => solution?.Data != null
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1)
                    && solution.ReportParameters[ParameterType.Affinity1].Value > 0)
                .Select(solution =>
                {
                    var x = Math.Sqrt(Math.Max(0, BufferAttribute.GetIonicStrength(solution.Data)));
                    var kd = FWEMath.Log10(solution.ReportParameters[ParameterType.Affinity1]);
                    return PlotPoint(x, kd, 1, solution.Data.Name);
                })
                .OrderBy(point => point.X)
                .ToList();
            var series = new List<AnalysisReportPlotSeries>
            {
                new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, points)
            };
            if (points.Count > 0 && analysis.IonicStrengthDependenceFit != null)
            {
                var domain = PlotDomain(points.Select(point => point.X));
                series.Add(new AnalysisReportPlotSeries(
                    "Saved fit", AnalysisReportPlotSeriesKind.Line,
                    Sample(domain.min, domain.max, 81).Select(x =>
                    {
                        var value = FWEMath.Log10(analysis.IonicStrengthDependenceFit.Evaluate(x * x));
                        return PlotPoint(x, value, 1);
                    })));
            }
            return new AnalysisReportPlotBlock(
                "Debye-Huckel dependence", "sqrt(Ionic strength / M)", "log10(Kd / M)", series);
        }

        static AnalysisReportPlotBlock BuildCounterIonReleasePlot(AnalysisResult result)
        {
            var analysis = result.ElectrostaticsAnalysis;
            var points = result.Solution.Solutions
                .Where(solution => solution?.Data != null
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1))
                .Select(solution =>
                {
                    var activity = SaltAttribute.GetIonActivity(solution.Data);
                    var affinity = solution.ReportParameters[ParameterType.Affinity1];
                    return activity > 0 && affinity.Value > 0
                        ? PlotPoint(Math.Log(activity), FWEMath.Log(affinity), 1, solution.Data.Name)
                        : null;
                })
                .Where(point => point != null)
                .OrderBy(point => point.X)
                .ToList();
            var series = new List<AnalysisReportPlotSeries>
            {
                new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, points)
            };
            if (points.Count > 0 && analysis.CounterIonReleaseFit != null)
            {
                var domain = PlotDomain(points.Select(point => point.X));
                series.Add(LinearSeries("Saved fit", analysis.CounterIonReleaseFit,
                    Sample(domain.min, domain.max, 81), 1));
            }
            return new AnalysisReportPlotBlock(
                "Counter-ion release", "ln(Salt activity)", "ln(Kd / M)", series);
        }

        static AnalysisReportPlotBlock BuildProtonationPlot(
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.ProtonationAnalysis;
            var fit = analysis.Fit as LinearFitWithError;
            var unit = ResolveMolarEnergyUnit(result, options);
            var scale = Energy.ScaleFactor(unit);
            var points = analysis.DataPoints
                .Where(point => point != null && IsFinite(point.Item1) && IsFinite(point.Item2.Value))
                .OrderBy(point => point.Item1)
                .Select(point => PlotPoint(point.Item1 * scale, point.Item2, scale))
                .ToList();
            var series = new List<AnalysisReportPlotSeries>
            {
                new AnalysisReportPlotSeries("Saved observations", AnalysisReportPlotSeriesKind.Points, points)
            };
            if (fit != null && points.Count > 0)
            {
                var domain = PlotDomain(points.Select(point => point.X));
                series.Add(LinearSeries("Saved fit", fit,
                    Sample(domain.min, domain.max, 81), scale, xScale: scale));
            }
            return new AnalysisReportPlotBlock(
                "Protonation dependence",
                "Buffer protonation enthalpy (" + unit.GetUnit() + "/mol)",
                "Observed enthalpy (" + unit.GetUnit() + "/mol)",
                series);
        }

        static void AddElectrostaticsParameters(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.ElectrostaticsAnalysis;
            if (analysis == null) return;
            var unit = result.AppropriateAffinityUnit;
            var fit = analysis.IonicStrengthDependenceFit;
            var items = new List<AnalysisReportKeyValueItem>();
            if (fit != null)
            {
                items.Add(Item("Kd at zero ionic strength", fit.Kd0.AsFormattedConcentration(
                    unit, withunit: true, style: options.UncertaintyDisplayStyle)));
                items.Add(Item("Salt sensitivity", fit.SaltSensitivity.ToString("G5", options.UncertaintyDisplayStyle)));
                if (fit.UsesCurvature)
                    items.Add(Item("Curvature", fit.Curvature.ToString("G5", options.UncertaintyDisplayStyle)));
            }
            if (!FloatWithError.IsNaN(analysis.CounterIonRelease))
                items.Add(Item("Counter-ion release", analysis.CounterIonRelease.ToString("G5", options.UncertaintyDisplayStyle)));
            items.Add(Item("Ionic-strength iterations", analysis.CompletedIterations.ToString(CultureInfo.CurrentCulture)));
            items.Add(Item("Counter-ion iterations", analysis.CounterIonReleaseIterations.ToString(CultureInfo.CurrentCulture)));
            items.Add(Item("Completed", FormatNullableDate(analysis.CompletedAtUtc)));
            section.Add(new AnalysisReportKeyValueBlock("Saved result", items));
        }

        static void AddProtonationParameters(
            AnalysisReportSection section,
            AnalysisResult result,
            AnalysisReportOptions options)
        {
            var analysis = result.ProtonationAnalysis;
            var unit = ResolveMolarEnergyUnit(result, options);
            section.Add(new AnalysisReportKeyValueBlock("Saved result", new[]
            {
                Item("Binding enthalpy", analysis.BindingEnthalpy.ToFormattedString(
                    unit, permole: true, style: options.UncertaintyDisplayStyle)),
                Item("Protonation change", analysis.ProtonationChange.ToString("G5", options.UncertaintyDisplayStyle)),
                Item("Iterations", analysis.CompletedIterations.ToString(CultureInfo.CurrentCulture)),
                Item("Completed", FormatNullableDate(analysis.CompletedAtUtc)),
            }));
        }

        static AnalysisReportAdvancedSectionDescriptor Descriptor(
            AnalysisReportAdvancedSectionKind kind,
            string title,
            string description)
        {
            return new AnalysisReportAdvancedSectionDescriptor(
                new AnalysisReportAdvancedSectionRequest(kind), title, description);
        }

        static string UnavailableAdvancedMessage(
            AnalysisResult result,
            AnalysisReportAdvancedSectionRequest request)
        {
            var title = request.Kind.ToString();
            var reason = request.Kind switch
            {
                AnalysisReportAdvancedSectionKind.TemperatureDependence =>
                    "No saved temperature-dependence fit with usable plot data is available.",
                AnalysisReportAdvancedSectionKind.SpolarRecord =>
                    result?.SpolarRecordAnalysisUnavailableReason,
                AnalysisReportAdvancedSectionKind.AffinityVersusSalt =>
                    result?.ElectrostaticsAnalysisUnavailableReason,
                AnalysisReportAdvancedSectionKind.DebyeHuckel =>
                    "No completed saved Debye-Huckel analysis is available.",
                AnalysisReportAdvancedSectionKind.CounterIonRelease =>
                    "No completed saved counter-ion release analysis is available.",
                AnalysisReportAdvancedSectionKind.Protonation =>
                    result?.ProtonationAnalysisUnavailableReason,
                AnalysisReportAdvancedSectionKind.Correlation =>
                    "No usable residual-bootstrap correlation is available for the requested scope.",
                _ => "The requested saved analysis is unavailable.",
            };
            if (string.IsNullOrWhiteSpace(reason)) reason = "The requested saved analysis is unavailable.";
            return title + " was omitted: " + reason;
        }

        static bool CanBuildTemperaturePlot(AnalysisResult result)
        {
            if (result?.IsTemperatureDependenceEnabled != true) return false;
            var dependences = result?.Solution?.TemperatureDependence;
            if (dependences == null || dependences.Count == 0) return false;
            return result.Solution.Solutions.Any(solution => solution?.ReportParameters != null
                && dependences.Keys.Any(key => solution.ReportParameters.ContainsKey(key)));
        }

        static bool CanBuildAffinitySaltPlot(AnalysisResult result)
        {
            return result?.IsElectrostaticsAnalysisDependenceEnabled == true
                && result.Solution.Solutions.Any(solution => solution?.Data?.Attributes != null
                    && solution.Data.Attributes.Any(attribute => attribute.Key == AttributeKey.Salt)
                    && solution.ReportParameters.ContainsKey(ParameterType.Affinity1));
        }

        static PublicationFigureOptions ContactFigureOptions(
            AnalysisReportOptions options,
            int columns,
            int rows)
        {
            var width = ContactSheetWidthCentimeters / Math.Max(1, columns);
            var height = ContactSheetHeightCentimeters / Math.Max(1, rows);
            return ReportFigureOptions(options, width, height, Math.Max(5, Math.Min(9, 22 / Math.Max(columns, rows))));
        }

        static PublicationFigureOptions ExpandedFigureOptions(AnalysisReportOptions options)
        {
            return ReportFigureOptions(
                options,
                ExpandedFigureWidthCentimeters,
                ExpandedFigureHeightCentimeters,
                10);
        }

        static PublicationFigureOptions ReportFigureOptions(
            AnalysisReportOptions options,
            double width,
            double height,
            double fontSize)
        {
            return new PublicationFigureOptions
            {
                PlotWidthCentimeters = width,
                PlotHeightCentimeters = height,
                PointsPerCentimeter = PublicationFigureOptions.DefaultPointsPerCentimeter,
                FontSize = fontSize,
                Font = PublicationFont.LiberationSans,
                EnergyUnitFamily = options.EnergyUnitFamily,
                EnergyUnitOverride = options.EnergyUnitOverride,
                TimeUnit = TimeUnit.Minute,
                ShowThermogram = true,
                ShowResiduals = true,
                ShowErrorBars = true,
                ShowConfidenceBand = true,
                ShowExperimentDetails = false,
                ShowFitParameters = false,
                ShowAxisTitles = true,
                ShowFitLine = true,
                DrawFitOffsetCorrected = true,
                ShowBadData = true,
                ShowBadDataErrorBars = false,
                AutoAxesIgnoresBadData = true,
                IncludeResidualGraphGap = true,
                SanitizeTicks = true,
                DrawBaselineCorrected = true,
                ShowBaseline = false,
                BaselineStyle = PublicationBaselineStyle.Solid,
                BaselineLayer = PublicationBaselineLayer.OverData,
                BaselineWidth = 1,
                ShowIntegrationRegions = false,
                IntegrationRegionStyle = PublicationIntegrationRegionStyle.Fill,
                ShowZeroLine = true,
                DataXTickCount = 5,
                DataYTickCount = 5,
                FitXTickCount = 5,
                FitYTickCount = 5,
                ResidualYTickCount = 3,
                ResidualPanelFraction = 0.2,
                InformationBoxPlacement = PublicationInfoBoxPlacement.Auto,
                SymbolShape = PublicationSymbolShape.Circle,
                SymbolSize = fontSize <= 6 ? 3 : 5,
                FitLineWidth = 1.5,
                FitLineSmoothness = LineSmoothness.Linear,
                PowerAxisTitle = "Differential Power (<unit>)",
                TimeAxisTitle = "Time (<unit>)",
                EnthalpyAxisTitle = "<unit> of injectant",
                XAxisTitle = null,
                DisplayParameters = FinalFigureDisplayParameters.None,
                AttributeOptions = DisplayAttributeOptions.None,
                TextUncertaintyStyle = options.UncertaintyDisplayStyle,
            };
        }

        static PublicationFigureOptions CloneFigureOptions(PublicationFigureOptions source)
        {
            return new PublicationFigureOptions
            {
                PlotWidthCentimeters = source.PlotWidthCentimeters,
                PlotHeightCentimeters = source.PlotHeightCentimeters,
                PointsPerCentimeter = source.PointsPerCentimeter,
                FontSize = source.FontSize,
                Font = source.Font,
                EnergyUnitFamily = source.EnergyUnitFamily,
                EnergyUnitOverride = source.EnergyUnitOverride,
                TimeUnit = source.TimeUnit,
                ShowThermogram = source.ShowThermogram,
                ShowResiduals = source.ShowResiduals,
                ShowErrorBars = source.ShowErrorBars,
                ShowConfidenceBand = source.ShowConfidenceBand,
                ShowExperimentDetails = source.ShowExperimentDetails,
                ShowFitParameters = source.ShowFitParameters,
                ShowAxisTitles = source.ShowAxisTitles,
                ShowFitLine = source.ShowFitLine,
                DrawFitOffsetCorrected = source.DrawFitOffsetCorrected,
                ShowBadData = source.ShowBadData,
                ShowBadDataErrorBars = source.ShowBadDataErrorBars,
                AutoAxesIgnoresBadData = source.AutoAxesIgnoresBadData,
                IncludeResidualGraphGap = source.IncludeResidualGraphGap,
                SanitizeTicks = source.SanitizeTicks,
                DrawBaselineCorrected = source.DrawBaselineCorrected,
                ShowBaseline = source.ShowBaseline,
                BaselineStyle = source.BaselineStyle,
                BaselineLayer = source.BaselineLayer,
                BaselineWidth = source.BaselineWidth,
                ShowIntegrationRegions = source.ShowIntegrationRegions,
                IntegrationRegionStyle = source.IntegrationRegionStyle,
                ShowZeroLine = source.ShowZeroLine,
                DataXTickCount = source.DataXTickCount,
                DataYTickCount = source.DataYTickCount,
                FitXTickCount = source.FitXTickCount,
                FitYTickCount = source.FitYTickCount,
                ResidualYTickCount = source.ResidualYTickCount,
                ResidualPanelFraction = source.ResidualPanelFraction,
                InformationBoxPlacement = source.InformationBoxPlacement,
                SymbolShape = source.SymbolShape,
                SymbolSize = source.SymbolSize,
                FitLineWidth = source.FitLineWidth,
                FitLineSmoothness = source.FitLineSmoothness,
                PowerAxisTitle = source.PowerAxisTitle,
                TimeAxisTitle = source.TimeAxisTitle,
                EnthalpyAxisTitle = source.EnthalpyAxisTitle,
                XAxisTitle = source.XAxisTitle,
                DisplayParameters = source.DisplayParameters,
                AttributeOptions = source.AttributeOptions,
                TextUncertaintyStyle = source.TextUncertaintyStyle,
            };
        }

        static (int columns, int rows) ChooseContactGrid(int count)
        {
            if (count <= 1) return (1, 1);
            var columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(
                count * ContactSheetWidthCentimeters / ContactSheetHeightCentimeters)));
            var rows = (int)Math.Ceiling(count / (double)columns);
            while (columns > 1 && (int)Math.Ceiling(count / (double)(columns - 1)) <= rows)
                columns--;
            return (columns, rows);
        }

        static AnalysisReportKeyValueItem Item(string label, string value)
        {
            return new AnalysisReportKeyValueItem(
                label,
                string.IsNullOrWhiteSpace(value) ? "Unavailable" : value);
        }

        static string ModelName(AnalysisResult result)
        {
            var modelType = result?.Model?.ModelType;
            return modelType?.GetProperties()?.Name ?? modelType?.ToString() ?? "Unavailable";
        }

        static string HealthLabel(AnalysisResultHealth health)
        {
            return health switch
            {
                AnalysisResultHealth.Valid => "Valid",
                AnalysisResultHealth.Warning => "Valid with warnings",
                AnalysisResultHealth.PartialInvalid => "Partially invalid or stale",
                AnalysisResultHealth.Invalid => "Invalid or stale",
                _ => "Validity unknown",
            };
        }

        static string FormatDate(DateTime value)
        {
            return value == default ? "Unavailable" : value.ToString("d", CultureInfo.CurrentCulture);
        }

        static string FormatNullableDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToUniversalTime().ToString("u", CultureInfo.InvariantCulture)
                : "Unavailable";
        }

        static string FormatTemperature(double celsius, bool useKelvin, bool includeUnit = true)
        {
            var value = FormatFinite(DisplayTemperature(celsius, useKelvin), "F2");
            if (!includeUnit || value == "Unavailable") return value;
            return value + (useKelvin ? " K" : " °C");
        }

        static double DisplayTemperature(double celsius, bool useKelvin)
        {
            return useKelvin ? celsius + 273.15 : celsius;
        }

        static string FormatFinite(double value, string format)
        {
            return IsFinite(value)
                ? value.ToString(format, CultureInfo.CurrentCulture)
                : "Unavailable";
        }

        static string FormatDuration(TimeSpan value)
        {
            if (value < TimeSpan.Zero) return "Unavailable";
            return value.TotalHours >= 1
                ? value.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
                : value.ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture);
        }

        static string IntegrationRange(IEnumerable<InjectionData> injections)
        {
            var ranges = (injections ?? Enumerable.Empty<InjectionData>())
                .Where(injection => injection != null && injection.IsIntegrated)
                .Select(injection => new
                {
                    Start = (double)injection.IntegrationStartDelay,
                    End = (double)injection.IntegrationEndOffset,
                })
                .Where(range => IsFinite(range.Start) && IsFinite(range.End))
                .ToList();
            if (ranges.Count == 0) return "Unavailable";

            var minimumStart = ranges.Min(range => range.Start);
            var maximumStart = ranges.Max(range => range.Start);
            var minimumEnd = ranges.Min(range => range.End);
            var maximumEnd = ranges.Max(range => range.End);
            if (Math.Abs(maximumStart - minimumStart) < 1e-9
                && Math.Abs(maximumEnd - minimumEnd) < 1e-9)
                return FormatFinite(minimumStart, "G4") + "–" + FormatFinite(maximumEnd, "G4") + " s after injection";

            return "Start " + FormatFinite(minimumStart, "G4") + "–" + FormatFinite(maximumStart, "G4")
                + " s; end " + FormatFinite(minimumEnd, "G4") + "–" + FormatFinite(maximumEnd, "G4") + " s";
        }

        static (string value, string sd, string interval, string unit) FormatParameter(
            ParameterType parameter,
            FloatWithError value,
            SolutionInterface solution,
            AnalysisResultOverviewTable overview,
            AnalysisReportOptions options)
        {
            var parent = parameter.GetProperties().ParentType;
            double scale;
            string unit;
            if (parent == ParameterType.Affinity1 || parameter == ParameterType.ApparentAffinity)
            {
                var concentrationUnit = ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Math.Abs(value.Value));
                scale = concentrationUnit.GetMod();
                unit = concentrationUnit.GetName();
            }
            else if (parent == ParameterType.Enthalpy1
                || parent == ParameterType.Gibbs1
                || parent == ParameterType.EntropyContribution1
                || parent == ParameterType.Offset
                || parent == ParameterType.HeatCapacity1
                || parent == ParameterType.Entropy1)
            {
                var energyUnit = parent == ParameterType.HeatCapacity1
                    ? overview.ResolvedHeatCapacityUnit
                    : overview.ResolvedEnergyUnit;
                scale = Energy.ScaleFactor(energyUnit);
                unit = energyUnit.GetUnit() + "/mol";
                if (parent == ParameterType.HeatCapacity1 || parent == ParameterType.Entropy1)
                    unit += "·K⁻¹";
            }
            else
            {
                scale = 1;
                unit = "";
            }

            var showSd = options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.Automatic
                || options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.StandardDeviation
                || options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            var showInterval = options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.ConfidenceInterval
                || options.UncertaintyDisplayStyle == UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
            return (
                FormatFinite(value.Value * scale, "G6"),
                showSd && value.HasError ? FormatFinite(value.SD * Math.Abs(scale), "G5") : "",
                showInterval && value.HasError
                    ? FormatFinite(value.Lower * scale, "G5") + " to " + FormatFinite(value.Upper * scale, "G5")
                    : "",
                unit);
        }

        static string ParameterLabel(ParameterType parameter)
        {
            return parameter.GetProperties()?.Name ?? parameter.ToString();
        }

        static int ParameterOrder(ParameterType parameter)
        {
            var values = (ParameterType[])Enum.GetValues(typeof(ParameterType));
            var index = Array.IndexOf(values, parameter);
            return index < 0 ? int.MaxValue : index;
        }

        static bool IsDerivedParameter(ParameterType parameter)
        {
            var parent = parameter.GetProperties().ParentType;
            return parent == ParameterType.Gibbs1
                || parent == ParameterType.Entropy1
                || parent == ParameterType.EntropyContribution1
                || parent == ParameterType.HeatCapacity1;
        }

        static EnergyUnit ResolveMolarEnergyUnit(AnalysisResult result, AnalysisReportOptions options)
        {
            var values = result?.Solution?.Solutions?
                .Where(solution => solution?.ReportParameters != null)
                .SelectMany(solution => solution.ReportParameters)
                .Where(item =>
                {
                    var parent = item.Key.GetProperties().ParentType;
                    return parent == ParameterType.Enthalpy1
                        || parent == ParameterType.Gibbs1
                        || parent == ParameterType.EntropyContribution1
                        || parent == ParameterType.Offset;
                })
                .Select(item => item.Value.Value)
                ?? Enumerable.Empty<double>();
            return EnergyUnitResolver.Resolve(options.EnergyUnitFamily, options.EnergyUnitOverride, values);
        }

        static AnalysisReportPlotPoint PlotPoint(
            double x,
            FloatWithError value,
            double scale,
            string label = "")
        {
            var center = value.Value * scale;
            var lower = value.HasError ? (double?)(value.Lower * scale) : null;
            var upper = value.HasError ? (double?)(value.Upper * scale) : null;
            if (scale < 0)
            {
                var temporary = lower;
                lower = upper;
                upper = temporary;
            }
            return new AnalysisReportPlotPoint(x, center, lower, upper, label);
        }

        static AnalysisReportPlotPoint PlotPoint(
            double x,
            double value,
            double scale,
            string label = "")
        {
            return new AnalysisReportPlotPoint(x, value * scale, label: label);
        }

        static AnalysisReportPlotSeries LinearSeries(
            string label,
            FitWithError fit,
            IEnumerable<double> displayXs,
            double yScale,
            double xScale = 1)
        {
            return new AnalysisReportPlotSeries(
                label,
                AnalysisReportPlotSeriesKind.Line,
                displayXs.Select(x => PlotPoint(x, fit.Evaluate(x / xScale), yScale)));
        }

        static (double min, double max) PlotDomain(IEnumerable<double> values)
        {
            var finite = (values ?? Enumerable.Empty<double>()).Where(IsFinite).ToList();
            if (finite.Count == 0) return (0, 1);
            var min = finite.Min();
            var max = finite.Max();
            if (Math.Abs(max - min) < 1e-12)
            {
                var padding = Math.Max(1, Math.Abs(min) * 0.05);
                return (min - padding, max + padding);
            }
            var extension = 0.05 * (max - min);
            return (min - extension, max + extension);
        }

        static IEnumerable<double> Sample(double min, double max, int count)
        {
            if (count <= 1) return new[] { min };
            return Enumerable.Range(0, count)
                .Select(index => min + (max - min) * index / (count - 1.0));
        }

        static string SpolarFoldedMode(FTSRMethod analysis)
        {
            return (analysis.CompletedFoldedMode ?? analysis.FoldedMode) switch
            {
                FTSRMethod.SRFoldedMode.Glob => "Globular",
                FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
                FTSRMethod.SRFoldedMode.ID => "Intrinsically disordered",
                _ => "Unavailable",
            };
        }

        static string SpolarTemperatureMode(FTSRMethod analysis)
        {
            return (analysis.CompletedTempMode ?? analysis.TempMode) switch
            {
                FTSRMethod.SRTempMode.IsoEntropicPoint => "Iso-entropic point",
                FTSRMethod.SRTempMode.MeanTemperature => "Mean experimental temperature",
                FTSRMethod.SRTempMode.ReferenceTemperature => "Reference temperature",
                _ => "Unavailable",
            };
        }

        static string NormalizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "section";
            return new string(value.Trim().ToLowerInvariant()
                .Select(character => char.IsLetterOrDigit(character) ? character : '-')
                .ToArray()).Trim('-');
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
