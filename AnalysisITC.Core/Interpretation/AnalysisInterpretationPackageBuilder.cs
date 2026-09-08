using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Interpretation
{
    public static class AnalysisInterpretationPackageBuilder
    {
        public const string PackageSchemaVersion = "2.0";

        public static AnalysisInterpretationPackage Build(
            AnalysisReport report,
            AnalysisResult result,
            AnalysisInterpretationOptions options = null)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return Build(report, id => id == result.UniqueID ? result : null, _ => null, options);
        }

        public static AnalysisInterpretationPackage Build(
            AnalysisReport report, Func<string, AnalysisResult> resultResolver,
            Func<string, ExperimentData> experimentResolver, AnalysisInterpretationOptions options = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (resultResolver == null) throw new ArgumentNullException(nameof(resultResolver));
            if (experimentResolver == null) throw new ArgumentNullException(nameof(experimentResolver));
            if (report.ResultIds.Count == 0) throw new InvalidOperationException("Select at least one analysis result.");
            if (report.ResultIds.Distinct(StringComparer.Ordinal).Count() != report.ResultIds.Count
                || report.SupportingExperimentIds.Distinct(StringComparer.Ordinal).Count() != report.SupportingExperimentIds.Count)
                throw new InvalidOperationException("A report reference occurs more than once.");
            var results = report.ResultIds.Select(id => resultResolver(id)
                ?? throw new InvalidOperationException("Analysis result is missing or unresolved: " + id)).ToList();
            if (results.Any(result => result.Solution?.Model == null))
                throw new InvalidOperationException("An analysis result has no stored fitted model.");
            var supporting = report.SupportingExperimentIds.Select(id => experimentResolver(id)
                ?? throw new InvalidOperationException("Supporting experiment is missing or unresolved: " + id)).ToList();
            var requested = (options ?? report.InterpretationSettings ?? AnalysisInterpretationOptions.Default()).Copy();
            var package = new AnalysisInterpretationPackage
            {
                Report = new InterpretationReportEvidence
                {
                    EvidenceId = "report-1", ReportId = report.UniqueID, Name = report.Name,
                    DateUtc = Utc(report.Date), AuthorComments = report.Comments,
                    ResultIds = report.ResultIds.ToList(),
                },
                StudyContext = report.StudyContext.Copy(),
                RequestedInterpretation = requested,
            };
            AddEvidence(package, "report-1", "report", report.Name, null);
            AddContextEvidence(package, package.StudyContext);
            for (var index = 0; index < results.Count; index++)
                package.Results.Add(BuildResult(package, results[index], requested, index));
            var memberIds = new HashSet<string>(package.Results.SelectMany(result => result.Experiments).Select(item => item.ExperimentId), StringComparer.Ordinal);
            foreach (var data in supporting.Where(data => !memberIds.Contains(data.UniqueID)))
                package.SupportingExperiments.Add(BuildExperiment(package, data, null,
                    package.SupportingExperiments.Count + 1, null, requested, -1, false));
            foreach (var result in package.Results)
            {
                package.Report.References.Add(new InterpretationReportReference { ReportReference = result.ReportReference,
                    Kind = "result", Id = result.ResultId, Name = result.Name });
                foreach (var member in result.Experiments)
                    package.Report.References.Add(new InterpretationReportReference { ReportReference = member.ReportReference,
                        Kind = "result-experiment", Id = member.ExperimentId, Name = member.Name, ParentReference = result.ReportReference });
            }
            foreach (var data in package.SupportingExperiments)
                package.Report.References.Add(new InterpretationReportReference { ReportReference = data.ReportReference,
                    Kind = "supporting-experiment", Id = data.ExperimentId, Name = data.Name });
            AnalysisInterpretationThermograms.UpdateBoundary(package);
            if (!requested.IncludeThermograms) package.Omissions.Add("Thermograms and sampled baselines omitted by user choice.");
            return package;
        }

        static InterpretationResultEvidence BuildResult(
            AnalysisInterpretationPackage package,
            AnalysisResult result,
            AnalysisInterpretationOptions options, int resultIndex)
        {
            var resultEvidenceId = $"result-{resultIndex + 1}";
            AddEvidence(package, resultEvidenceId, "result", result.Name, "report-1");
            var validity = result.ValidityReport;
            var global = result.Solution;
            var convergence = global.Convergence;
            var value = new InterpretationResultEvidence
            {
                EvidenceId = resultEvidenceId,
                ReportReference = AnalysisReportReferenceLabels.Result(resultIndex),
                ResultId = result.UniqueID,
                Name = result.Name,
                DateUtc = Utc(result.Date),
                Comments = result.Comments,
                Health = result.Health.ToString(),
                ValidityStatus = validity.Status.ToString(),
                ValidityReasons = validity.Reasons?.ToList() ?? new List<string>(),
                Model = new InterpretationModelEvidence
                {
                    Type = FtxtcWireIds.Model(global.Model.ModelType),
                    IsGlobal = global.Model.Parameters?.RequiresGlobalFitting == true,
                    UsesWeightedFitting = global.UseWeightedFitting,
                    Options = NamedValues(global.Model.ModelOptions),
                    Constraints = (global.Model.Parameters?.Constraints ?? new Dictionary<ParameterType, VariableConstraint>())
                        .OrderBy(item => FtxtcWireIds.Parameter(item.Key), StringComparer.Ordinal)
                        .Select(item => new InterpretationConstraintEvidence
                        { FittedCoordinateId = FtxtcWireIds.Parameter(item.Key), Constraint = item.Value.ToString() }).ToList(),
                },
                Solver = new InterpretationSolverEvidence
                {
                    Algorithm = convergence?.Algorithm.ToString(), Termination = convergence?.Termination.ToString(),
                    FailureReason = convergence?.FailureReason, Iterations = convergence?.Iterations ?? 0,
                    UsesWeightedObjective = global.UseWeightedFitting,
                    UnweightedRmsdMicrojoules = Finite(convergence?.Loss),
                    UnweightedMolarRmsdJoulesPerMole = Finite(global.MolarRMSD?.Value),
                    ErrorEstimationMethod = global.ErrorEstimationMethod.ToString(),
                    ErrorEstimationOutcome = convergence?.ErrorEstimationOutcome.ToString(),
                    ErrorEstimationSummary = convergence?.ErrorEstimationSummary,
                    BootstrapIterationCount = global.BootstrapIterations,
                    AttemptedUncertaintyRefits = convergence?.ErrorEstimationAttemptedRefits,
                    SuccessfulUncertaintyRefits = convergence?.ErrorEstimationSucceededRefits,
                    FailedUncertaintyRefits = convergence?.ErrorEstimationFailedRefits,
                    ExcludedLimitTerminations = convergence?.ErrorEstimationLimitTerminations ?? 0,
                    ProfileLikelihood = Profile(global.ProfileLikelihood),
                },
                InformationCriteria = validity.IsValid ? InformationCriteria(result.InformationCriteria) : null,
                MatchedFitDiagnosticsUnavailableReason = validity.IsValid ? null : "Matching original fit inputs cannot be established; current observations are separate from stored estimates.",
                HistoricalFitInputs = validity.IsValid ? new List<System.Text.Json.JsonElement>() :
                    (result.ValiditySnapshot?.Experiments ?? new List<ExperimentFitInputSnapshot>()).Select(snapshot =>
                        HistoricalInput(snapshot)).ToList(),
            };

            var members = global.Solutions.Where(member => member?.Data != null).ToList();
            for (var index = 0; index < members.Count; index++)
                value.Experiments.Add(BuildExperiment(package, members[index].Data, members[index], index + 1, global, options, resultIndex, validity.IsValid));

            foreach (var dependency in global.TemperatureDependence.OrderBy(item => FtxtcWireIds.Parameter(item.Key), StringComparer.Ordinal))
            {
                value.TemperatureDependence.Add(new InterpretationTemperatureDependenceEvidence
                {
                    ParameterId = QuantityId(dependency.Key),
                    SiUnit = Unit(dependency.Key),
                    ReferenceTemperatureKelvin = Finite(dependency.Value.ReferenceT + 273.15),
                    ReferenceTemperatureCelsius = Celsius(dependency.Value.ReferenceT + 273.15),
                    InterceptSi = Finite(dependency.Value.Intercept.Value),
                    SlopeSiPerKelvin = Finite(dependency.Value.Slope.Value),
                });
            }

            AddAdvancedAnalyses(value.AdvancedAnalyses, result);

            for (var index = 0; index < members.Count; index++)
                value.BootstrapCorrelations.Add(Correlation(global, index, $"{resultEvidenceId}/experiment-{index + 1}"));
            value.BootstrapCorrelation = value.BootstrapCorrelations.FirstOrDefault();
            return value;
        }

        static InterpretationExperimentEvidence BuildExperiment(
            AnalysisInterpretationPackage package,
            ExperimentData data,
            SolutionInterface solution,
            int ordinal,
            GlobalSolution global,
            AnalysisInterpretationOptions options, int resultIndex, bool matchedFit)
        {
            var parentId = resultIndex < 0 ? "report-1" : $"result-{resultIndex + 1}";
            var evidenceId = resultIndex < 0 ? $"supporting-{ordinal}" : $"{parentId}/experiment-{ordinal}";
            AddEvidence(package, evidenceId, "experiment", data.Name, parentId);
            var output = new InterpretationExperimentEvidence
            {
                EvidenceId = evidenceId,
                ReportReference = resultIndex < 0 ? AnalysisReportReferenceLabels.SupportingExperiment(ordinal - 1) : AnalysisReportReferenceLabels.Experiment(resultIndex, ordinal - 1),
                ExperimentId = data.UniqueID, Name = data.Name,
                SourceFileBasename = Path.GetFileName(data.FileName ?? ""), DateUtc = Utc(data.Date),
                Comments = data.Comments, Instrument = Instrument(data), Solver = MemberSolver(solution), DateProvenance = data.DateSource.ToString(),
                InformationCriteria = matchedFit && global?.Model?.ShouldFitIndividually == true ? InformationCriteria(solution?.InformationCriteria) : null,
                UnavailableDerivedParameterReason = matchedFit || solution == null ? null : "Temperature/concentration-dependent derived values are omitted; historical snapshots do not record a verified measurement temperature.",
                MatchedFitDiagnosticsUnavailableReason = matchedFit ? null : solution == null ? "Supporting experiment has no report fit." : "Current inputs do not have a verified match to the historical fit.",
                Thermogram = options.IncludeThermograms ? AnalysisInterpretationThermograms.Compress(data) : null,
                SourceStateFingerprint = AnalysisInterpretationThermograms.SourceFingerprint(data),
                BlankReferenceExperimentId = data.BufferSubtractionSettings?.ReferenceExperimentId,
                BlankSubtractionMethod = data.BufferSubtractionSettings?.MethodDisplayName,
                TargetTemperatureKelvin = Finite(data.TargetTemperature + 273.15),
                MeasuredTemperatureKelvin = Finite(data.MeasuredTemperatureKelvin),
                TargetTemperatureCelsius = Celsius(data.TargetTemperature + 273.15),
                MeasuredTemperatureCelsius = Celsius(data.MeasuredTemperatureKelvin),
                CellConcentrationMolar = Finite(data.CellConcentration.Value),
                CellConcentrationSdMolar = Finite(data.CellConcentration.SD),
                SyringeConcentrationMolar = Finite(data.SyringeConcentration.Value),
                SyringeConcentrationSdMolar = Finite(data.SyringeConcentration.SD),
                AnalysisAxis = data.AxisType.ToString(),
                BaselineCompleted = data.Processor?.BaselineCompleted == true,
                IntegrationCompleted = data.Processor?.IntegrationCompleted == true,
                BaselineProcessor = data.Processor?.BaselineType.ToString(),
                ProcessorLocked = data.Processor?.IsLocked == true,
                DiscardsIntegratedPointsForBaseline = data.Processor?.DiscardIntegratedPoints == true,
                IntegrationLengthMode = data.Processor?.IntegrationLengthMode.ToString(),
                IntegrationLengthFactor = Finite(data.Processor?.IntegrationLengthFactor),
                InitialDelaySeconds = Finite(data.InitialDelay),
                Attributes = NamedValues(data.Attributes, data),
                ModelOptions = NamedValues(solution?.ModelOptions, data),
            };
            output.Baseline = AnalysisInterpretationDiagnostics.Baseline(data, evidenceId);
            output.ResidualDiagnostics = matchedFit ? AnalysisInterpretationDiagnostics.Residuals(data, solution, evidenceId, AxisValue)
                : new InterpretationResidualDiagnosticsEvidence { EvidenceId = evidenceId + "/residual-diagnostics", IsAvailable = false,
                    UnavailableReason = output.MatchedFitDiagnosticsUnavailableReason };
            var segments = (data.Segments ?? new List<TandemExperimentSegment>()).OrderBy(item => item.FirstInjectionID).ToList();
            for (var segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                output.TandemSegments.Add(new InterpretationTandemSegmentEvidence
                {
                    FirstInjectionId = segment.FirstInjectionID + 1,
                    StartTimeSeconds = data.HasThermogram ? data.Injections.FirstOrDefault(item => item.ID == segment.FirstInjectionID)?.Time : null,
                    EndTimeSeconds = !data.HasThermogram ? null : segmentIndex + 1 < segments.Count
                        ? data.Injections.FirstOrDefault(item => item.ID == segments[segmentIndex + 1].FirstInjectionID)?.Time
                        : data.DataPoints.Last().Time,
                    InitialActiveCellConcentrationMolar = Finite(segment.SegmentInitialActiveCellConc),
                    InitialActiveTitrantConcentrationMolar = Finite(segment.SegmentInitialActiveTitrantConc),
                });
            }
            AddEvidence(package, output.Baseline.EvidenceId, "baseline-summary", "Baseline summary and controls", evidenceId);
            AddEvidence(package, output.ResidualDiagnostics.EvidenceId, "residual-diagnostics", "Residual diagnostics", evidenceId);

            foreach (var item in ReportedParameters(solution, global, matchedFit).OrderBy(item => QuantityId(item.Key), StringComparer.Ordinal))
            {
                var family = item.Key.GetProperties().ParentType;
                if (!matchedFit && (item.Key == ParameterType.ApparentAffinity || family == ParameterType.Gibbs1
                    || family == ParameterType.Entropy1 || family == ParameterType.EntropyContribution1))
                {
                    output.UnavailableDerivedParameterReason = "Derived values depending on current temperature or concentration are omitted because the original fit-input basis is unverified.";
                    continue;
                }
                var parameter = BuildParameter(solution, global, item.Key, item.Value, evidenceId);
                output.Parameters.Add(parameter);
                AddEvidence(package, parameter.EvidenceId, "parameter", parameter.Name, evidenceId);
            }

            if (options.InjectionRows != AnalysisInterpretationInjectionRows.None)
            {
                foreach (var injection in data.Injections.OrderBy(item => item.ID))
                {
                    if (options.InjectionRows == AnalysisInterpretationInjectionRows.IncludedOnly && !injection.Include) continue;
                    var id = $"{evidenceId}/injection-{injection.ID + 1}";
                    var fitted = matchedFit ? Safe(() => solution.Model.EvaluateEnthalpy(injection.ID, true)) : null;
                    var residual = matchedFit ? Safe(() => solution.Model.Residual(injection) / injection.InjectionMass) : null;
                    double? lower = null, upper = null;
                    if (matchedFit && global.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
                        && solution.BootstrapSolutions?.Count > 0)
                    {
                        var band = SafeFwe(() => solution.Model.EvaluateBootstrap(injection.ID, true));
                        lower = band.HasValue ? Finite(band.Value.Lower) : null;
                        upper = band.HasValue ? Finite(band.Value.Upper) : null;
                    }
                    var injectionEvidence = new InterpretationInjectionEvidence
                    {
                        EvidenceId = id, InjectionId = injection.ID + 1, Included = injection.Include,
                        TimeSeconds = data.HasThermogram ? Finite(injection.Time) : null,
                        DurationSeconds = data.HasThermogram ? Positive(injection.Duration) : null,
                        IntegrationStartTimeSeconds = data.HasThermogram && injection.IsIntegrated ? Finite(injection.IntegrationStartTime) : null,
                        IntegrationEndTimeSeconds = data.HasThermogram && injection.IsIntegrated ? Finite(injection.IntegrationEndTime) : null,
                        NonIntegratedIntervalBeforeNextInjectionSeconds = data.HasThermogram && injection.IsIntegrated
                            ? NonNegative(data.Injections.Where(next => next.Time > injection.Time).OrderBy(next => next.Time).FirstOrDefault()?.Time - injection.IntegrationEndTime) : null,
                        IsIntegrated = injection.IsIntegrated,
                        IntegratedHeatBeforeSubtractionJoules = injection.IsIntegrated ? Finite(injection.RawPeakArea.Value) : null,
                        IntegratedHeatBeforeSubtractionErrorJoules = injection.IsIntegrated ? Finite(injection.RawPeakArea.SD) : null,
                        VolumeLitres = Finite(injection.Volume),
                        ActiveCellConcentrationMolar = Finite(injection.ActualCellConcentration),
                        ActiveTitrantConcentrationMolar = Finite(injection.ActualTitrantConcentration),
                        InjectionDelaySeconds = data.HasThermogram ? Finite(injection.Delay) : null,
                        FilterPeriodSeconds = Positive(injection.Filter),
                        IntegrationStartDelaySeconds = data.HasThermogram ? Finite(injection.IntegrationStartDelay) : null,
                        IntegrationEndOffsetSeconds = data.HasThermogram ? Finite(injection.IntegrationEndOffset) : null,
                        IntegrationLengthSeconds = data.HasThermogram ? Finite(injection.IntegrationLength) : null,
                        IntegrationLengthFractionOfInjectionDelay = data.HasThermogram ? PositiveRatio(injection.IntegrationLength, injection.Delay) : null,
                        AnalysisAxisKind = data.AxisType.ToString(),
                        AnalysisAxisValue = AxisValue(data.AxisType, injection),
                        IntegratedHeatJoules = injection.IsIntegrated ? Finite(injection.PeakArea.Value) : null,
                        IntegratedHeatErrorJoules = injection.IsIntegrated ? Finite(injection.PeakArea.SD) : null,
                        ObservedHeatJoulesPerMole = injection.IsIntegrated ? Finite(injection.Enthalpy) : null,
                        ObservedHeatErrorJoulesPerMole = injection.IsIntegrated ? Finite(injection.SD) : null,
                        FittedHeatJoulesPerMole = fitted,
                        ResidualJoulesPerMole = residual,
                        Confidence95LowerJoulesPerMole = lower,
                        Confidence95UpperJoulesPerMole = upper,
                    };
                    if (data.HasThermogram && injection.IsIntegrated) AnalysisInterpretationDiagnostics.AddInjectionBaseline(injectionEvidence, data, injection);
                    output.Injections.Add(injectionEvidence);
                    AddEvidence(package, id, "injection", $"Injection {injection.ID + 1}", evidenceId);
                }
            }
            return output;
        }

        static InterpretationSolverEvidence MemberSolver(SolutionInterface solution)
        {
            if (solution == null) return null;
            var convergence = solution.Convergence;
            return new InterpretationSolverEvidence
            {
                Algorithm = convergence?.Algorithm.ToString(), Termination = convergence?.Termination.ToString(),
                FailureReason = convergence?.FailureReason, Iterations = convergence?.Iterations ?? 0,
                UsesWeightedObjective = solution.UseWeightedFitting,
                UnweightedRmsdMicrojoules = Finite(convergence?.Loss), UnweightedMolarRmsdJoulesPerMole = Finite(solution.MolarRMSD?.Value),
                ErrorEstimationMethod = solution.ErrorMethod.ToString(), ErrorEstimationOutcome = convergence?.ErrorEstimationOutcome.ToString(),
                ErrorEstimationSummary = convergence?.ErrorEstimationSummary, BootstrapIterationCount = solution.BootstrapSolutions?.Count ?? 0,
                AttemptedUncertaintyRefits = convergence?.ErrorEstimationAttemptedRefits, SuccessfulUncertaintyRefits = convergence?.ErrorEstimationSucceededRefits,
                FailedUncertaintyRefits = convergence?.ErrorEstimationFailedRefits, ExcludedLimitTerminations = convergence?.ErrorEstimationLimitTerminations ?? 0,
                ProfileLikelihood = Profile(solution.ProfileLikelihood),
            };
        }

        static Dictionary<ParameterType, AnalysisITC.Core.Numerics.FloatWithError> ReportedParameters(
            SolutionInterface solution, GlobalSolution global, bool matchedFit)
        {
            if (solution == null) return new Dictionary<ParameterType, AnalysisITC.Core.Numerics.FloatWithError>();
            if (matchedFit) return solution.ReportParameters;
            // Reading ReportParameters eagerly can evaluate apparent affinity against changed concentrations.
            // Only stored coordinates and their concentration/temperature-independent transformations are usable here.
            var output = new Dictionary<ParameterType, AnalysisITC.Core.Numerics.FloatWithError>();
            foreach (var item in solution.Parameters)
            {
                if (item.Key == ParameterType.Nvalue2 && solution.ModelOptions.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringeOption) && syringeOption.BoolValue) continue;
                var family = item.Key.GetProperties().ParentType;
                if (item.Key == ParameterType.ApparentAffinity || family == ParameterType.Gibbs1
                    || family == ParameterType.Entropy1 || family == ParameterType.EntropyContribution1) continue;
                if (family != ParameterType.Affinity1) { output[item.Key] = item.Value; continue; }
                var kd = 1.0 / AnalysisITC.Core.Numerics.FWEMath.Pow(10.0, item.Value);
                var coordinates = (solution.ProfileLikelihood?.Coordinates ?? Array.Empty<ProfileCoordinateResult>())
                    .Concat(global?.ProfileLikelihood?.Coordinates ?? Array.Empty<ProfileCoordinateResult>());
                var coordinate = coordinates.FirstOrDefault(value => value.Id.Parameter == item.Key
                    && (value.Id.Scope == ParameterBoundaryScope.Shared || value.Id.ExperimentIdentity == solution.Data.UniqueID));
                if (coordinate?.HasCompleteInterval == true)
                {
                    var transformed = coordinate.Transform(value => 1.0 / Math.Pow(10.0, value));
                    kd = new AnalysisITC.Core.Numerics.FloatWithError(kd.Value, transformed.SD, transformed.Lower, transformed.Upper);
                }
                output[item.Key] = kd;
            }
            return output;
        }

        static System.Text.Json.JsonElement HistoricalInput(ExperimentFitInputSnapshot snapshot)
        {
            var options = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
            options.Converters.Add(new FiniteHistoricalNumberConverter());
            return System.Text.Json.JsonSerializer.SerializeToElement(snapshot, options);
        }

        sealed class FiniteHistoricalNumberConverter : System.Text.Json.Serialization.JsonConverter<double>
        {
            public override double Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options) =>
                reader.TokenType == System.Text.Json.JsonTokenType.Null ? double.NaN : reader.GetDouble();
            public override void Write(System.Text.Json.Utf8JsonWriter writer, double value, System.Text.Json.JsonSerializerOptions options)
            { if (double.IsNaN(value) || double.IsInfinity(value)) writer.WriteNullValue(); else writer.WriteNumberValue(value); }
        }

        static InterpretationParameterEvidence BuildParameter(
            SolutionInterface solution, GlobalSolution global, ParameterType key,
            AnalysisITC.Core.Numerics.FloatWithError reported, string experimentEvidenceId)
        {
            var syringeFraction = key.GetProperties().ParentType == ParameterType.Nvalue1
                && solution.ModelOptions.TryGetValue(AttributeKey.UseSyringeActiveFraction, out var syringeOption) && syringeOption.BoolValue;
            var quantityId = syringeFraction ? "syringe-active-fraction-" + (key == ParameterType.Nvalue2 ? "2" : "1") : QuantityId(key);
            var coordinate = solution.Model.Parameters.Table.TryGetValue(key, out var fitted) ? fitted : null;
            var contacts = solution.Convergence?.BoundaryContacts ?? Array.Empty<ParameterBoundaryContact>();
            var isAffinity = key == ParameterType.ApparentAffinity || key.GetProperties().ParentType == ParameterType.Affinity1;
            var isDirect = coordinate != null && !isAffinity
                && key.GetProperties().ParentType != ParameterType.Gibbs1
                && key.GetProperties().ParentType != ParameterType.Entropy1
                && key.GetProperties().ParentType != ParameterType.EntropyContribution1;
            return new InterpretationParameterEvidence
            {
                EvidenceId = $"{experimentEvidenceId}/parameter/{quantityId}",
                QuantityId = quantityId,
                FittedCoordinateId = coordinate == null ? null : FtxtcWireIds.Parameter(key),
                Name = syringeFraction ? "Syringe active fraction (alpha_syringe; fixed N in model options)" : key.GetProperties().Name,
                SiUnit = Unit(key),
                BestFitValue = Finite(reported.Value), StandardDeviation = Finite(reported.SD),
                Confidence95Lower = Confidence95(reported, 0), Confidence95Upper = Confidence95(reported, 1),
                IsFittedCoordinate = isDirect && coordinate.IsFitted,
                IsDerived = !isDirect,
                IsLocked = coordinate?.IsLocked == true,
                Constraint = global.Model.Parameters.GetConstraintForParameter(key).ToString(),
                BoundaryWarning = contacts.Any(contact => contact.Parameter == key),
                FittedLowerBound = coordinate?.Limits?.Length >= 2 ? Finite(coordinate.Limits[0]) : null,
                FittedUpperBound = coordinate?.Limits?.Length >= 2 ? Finite(coordinate.Limits[1]) : null,
                UncertaintyMethod = global.ErrorEstimationMethod.ToString(),
                Confidence95Available = Confidence95(reported, 0).HasValue && Confidence95(reported, 1).HasValue,
                Confidence95UnavailableReason = Confidence95(reported, 0).HasValue && Confidence95(reported, 1).HasValue
                    ? ""
                    : "No finite 95% confidence interval was stored for this reported parameter.",
            };
        }

        static InterpretationInformationCriteriaEvidence InformationCriteria(FitInformationCriteria value)
        {
            if (value == null) return null;
            return new InterpretationInformationCriteriaEvidence
            {
                ObservationCount = value.ObservationCount, FittedParameterCount = value.FittedParameterCount,
                LikelihoodParameterCount = value.LikelihoodParameterCount,
                LikelihoodMode = value.LikelihoodMode,
                UsesKnownObservationSigmas = value.UsesKnownObservationSigmas,
                MinusTwoLogLikelihood = Available(value.MinusTwoLogLikelihood, value.MinusTwoLogLikelihood.HasValue, value.AicUnavailableReason),
                Aic = Available(value.Aic, value.IsAicAvailable, value.AicUnavailableReason),
                Aicc = Available(value.Aicc, value.IsAiccAvailable, value.AiccUnavailableReason),
            };
        }

        static InterpretationAvailableNumber Available(double? value, bool available, string reason) => new InterpretationAvailableNumber
        { IsAvailable = available && Finite(value).HasValue, Value = available ? Finite(value) : null, UnavailableReason = available ? "" : reason ?? "Unavailable" };

        static InterpretationCorrelationEvidence Correlation(GlobalSolution solution, int memberIndex, string scopeEvidenceId)
        {
            BootstrapCorrelationResult result;
            try { result = new BootstrapCorrelationAnalyzer().Analyze(solution, memberIndex); }
            catch (Exception ex)
            {
                return new InterpretationCorrelationEvidence { ScopeEvidenceId = scopeEvidenceId, Availability = "Unavailable", Reason = ex.Message };
            }
            var output = new InterpretationCorrelationEvidence
            {
                ScopeEvidenceId = scopeEvidenceId,
                Availability = result.Availability.Status.ToString(), Reason = result.Availability.Reason,
                CompleteReplicateCount = result.CompleteReplicateCount, RankLimited = result.IsRankLimited,
                CoarseMonteCarloPrecision = result.Reliability?.HasCoarseMonteCarloPrecision == true,
                FrequentFailures = result.Reliability?.HasFrequentFailures == true,
                UncertainSignPairCount = result.Reliability?.UncertainSignPairCount ?? 0,
                CoordinateLabels = result.Parameters.Select(item => item.Label).ToList(),
            };
            if (result.CorrelationMatrix != null)
                for (var row = 0; row < result.CorrelationMatrix.GetLength(0); row++)
                {
                    var values = new List<double?>();
                    for (var column = 0; column < result.CorrelationMatrix.GetLength(1); column++)
                        values.Add(Finite(result.CorrelationMatrix[row, column]));
                    output.PearsonMatrix.Add(values);
                }
            return output;
        }

        static InterpretationProfileLikelihoodEvidence Profile(ProfileLikelihoodRunResult profile)
        {
            if (profile == null) return null;
            return new InterpretationProfileLikelihoodEvidence
            {
                Coordinates = profile.Coordinates.Select(item => new InterpretationProfileCoordinateEvidence
                {
                    FittedCoordinateId = FtxtcWireIds.Parameter(item.Id.Parameter), Scope = item.Id.Scope.ToString(),
                    ExperimentId = item.Id.ExperimentIdentity, BestFitValue = Finite(item.BestValue),
                    LowerBound = Finite(item.LowerBound), UpperBound = Finite(item.UpperBound), Warnings = item.ShapeWarnings.ToList(),
                    Lower = new InterpretationProfileSideEvidence { Outcome = item.Lower.Outcome.ToString(), Endpoint = Finite(item.Lower.Endpoint), Warnings = item.Lower.Warnings.ToList() },
                    Upper = new InterpretationProfileSideEvidence { Outcome = item.Upper.Outcome.ToString(), Endpoint = Finite(item.Upper.Endpoint), Warnings = item.Upper.Warnings.ToList() },
                }).ToList(),
                Calibration = profile.Calibration.ToString(), Outcome = profile.Outcome.ToString(),
                ConfidenceLevel = Finite(profile.ConfidenceLevel), ObservationCount = profile.ObservationCount,
                ParameterCount = profile.ParameterCount, DegreesOfFreedom = profile.DegreesOfFreedom,
                AttemptedSolverCalls = profile.AttemptedSolverCalls,
                CoordinateCount = profile.Coordinates.Count,
                CompleteIntervalCount = profile.Coordinates.Count(item => item.HasCompleteInterval),
                Diagnostics = profile.Coordinates.SelectMany(item => item.ShapeWarnings)
                    .Concat(profile.Coordinates.SelectMany(item => item.Lower.Warnings))
                    .Concat(profile.Coordinates.SelectMany(item => item.Upper.Warnings))
                    .Where(item => !string.IsNullOrWhiteSpace(item)).Distinct(StringComparer.Ordinal).ToList(),
            };
        }

        static void AddAdvancedAnalyses(List<InterpretationAdvancedAnalysisEvidence> output, AnalysisResult result)
        {
            if (result.SpolarRecordAnalysis?.Result != null)
            {
                var analysis = result.SpolarRecordAnalysis;
                var value = new InterpretationAdvancedAnalysisEvidence
                {
                    Type = "spolar-record", Status = "completed", CompletedIterations = analysis.CompletedIterations,
                    CompletedAtUtc = analysis.CompletedAtUtc.HasValue ? Utc(analysis.CompletedAtUtc.Value) : null,
                    UncertaintyMethod = analysis.CompletedErrorEstimationMethod?.ToString(),
                };
                AddAdvancedValue(value, "hydration-entropy", "J/(mol*K)", analysis.Result.HydrationEntropy);
                AddAdvancedValue(value, "conformational-entropy", "J/(mol*K)", analysis.Result.ConformationalEntropy);
                AddAdvancedValue(value, "residue-estimate", "1", analysis.Result.Rvalue);
                var referenceTemperature = analysis.Result.ReferenceTemperature;
                AddAdvancedValue(value, "reference-temperature", "K", new AnalysisITC.Core.Numerics.FloatWithError(
                    referenceTemperature.Value + 273.15,
                    referenceTemperature.SD,
                    referenceTemperature.Lower + 273.15,
                    referenceTemperature.Upper + 273.15));
                output.Add(value);
            }
            if (result.ElectrostaticsAnalysis?.Calculated == true)
            {
                var analysis = result.ElectrostaticsAnalysis;
                var value = new InterpretationAdvancedAnalysisEvidence
                {
                    Type = "electrostatics", Status = "completed", CompletedIterations = analysis.CompletedIterations,
                    CompletedAtUtc = analysis.CompletedAtUtc.HasValue ? Utc(analysis.CompletedAtUtc.Value) : null,
                    UncertaintyMethod = analysis.CompletedErrorEstimationMethod?.ToString(),
                };
                if (analysis.IonicStrengthDependenceFit != null)
                {
                    AddAdvancedValue(value, "kd-zero-ionic-strength", "mol/L", analysis.IonicStrengthDependenceFit.Kd0);
                    AddAdvancedValue(value, "salt-sensitivity", "(mol/L)^-0.5", analysis.IonicStrengthDependenceFit.SaltSensitivity);
                    AddAdvancedValue(value, "curvature", "(mol/L)^-1", analysis.IonicStrengthDependenceFit.Curvature);
                }
                if (!AnalysisITC.Core.Numerics.FloatWithError.IsNaN(analysis.CounterIonRelease))
                    AddAdvancedValue(value, "counter-ion-release", "1", analysis.CounterIonRelease);
                output.Add(value);
            }
            if (result.ProtonationAnalysis?.Fit is AnalysisITC.Core.Numerics.LinearFitWithError)
            {
                var analysis = result.ProtonationAnalysis;
                var value = new InterpretationAdvancedAnalysisEvidence
                {
                    Type = "protonation", Status = "completed", CompletedIterations = analysis.CompletedIterations,
                    CompletedAtUtc = analysis.CompletedAtUtc.HasValue ? Utc(analysis.CompletedAtUtc.Value) : null,
                    UncertaintyMethod = analysis.CompletedErrorEstimationMethod?.ToString(),
                };
                AddAdvancedValue(value, "binding-enthalpy", "J/mol", analysis.BindingEnthalpy.FloatWithError);
                AddAdvancedValue(value, "protonation-change", "1", analysis.ProtonationChange);
                output.Add(value);
            }
        }

        static void AddAdvancedValue(
            InterpretationAdvancedAnalysisEvidence analysis,
            string id,
            string unit,
            AnalysisITC.Core.Numerics.FloatWithError value)
        {
            analysis.Values.Add(new InterpretationAdvancedValue
            {
                Id = id, SiUnit = unit, Value = Finite(value.Value), StandardDeviation = Finite(value.SD),
                Confidence95Lower = Finite(value.Lower), Confidence95Upper = Finite(value.Upper),
            });
        }

        static List<InterpretationNamedValue> NamedValues(
            IDictionary<AttributeKey, ExperimentAttribute> values,
            ExperimentData experiment = null) =>
            NamedValues(values?.Values, experiment);

        static List<InterpretationNamedValue> NamedValues(
            IEnumerable<ExperimentAttribute> values,
            ExperimentData experiment = null)
        {
            return (values ?? Enumerable.Empty<ExperimentAttribute>())
                .OrderBy(value => FtxtcWireIds.Attribute(value.Key), StringComparer.Ordinal)
                .ThenBy(value => value.OptionName, StringComparer.Ordinal)
                .Select(value => new InterpretationNamedValue
                {
                    Name = FtxtcWireIds.Attribute(value.Key), DisplayName = value.GetDisplayName(),
                    Type = value.Key.GetProperties().Type.ToString(),
                    TextValue = value.StringValue ?? value.OptionName,
                    DisplayValue = AttributeDisplayValue(value, experiment),
                    NumericValue = AttributeNumber(value), BooleanValue = AttributeBoolean(value),
                }).ToList();
        }

        static string AttributeDisplayValue(ExperimentAttribute value, ExperimentData experiment)
        {
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            switch (value.Key)
            {
                case AttributeKey.Buffer:
                    return (1000 * value.ParameterValue.Value).ToString("G5", invariant) + " mM "
                        + ((AnalysisITC.Core.Data.Buffer)value.IntValue).GetProperties().AttributedName
                        + " pH " + value.DoubleValue.ToString("G4", invariant);
                case AttributeKey.Salt:
                    return (1000 * value.ParameterValue.Value).ToString("G5", invariant) + " mM "
                        + ((Salt)value.IntValue).GetProperties().AttributedName;
                case AttributeKey.IonicStrength:
                    return (1000 * value.ParameterValue.Value).ToString("G5", invariant) + " mM";
                case AttributeKey.BufferSubtraction:
                    var subtraction = BufferSubtractionSettings.FromAttribute(value);
                    var referenceName = experiment?.ReferenceExperiment?.Name ?? "Missing reference experiment";
                    return subtraction == null ? referenceName : referenceName + " (" + subtraction.MethodDisplayName + ")";
                case AttributeKey.NumberOfSites1:
                case AttributeKey.NumberOfSites2:
                    return StoichiometryInvariant(value.DoubleValue);
                case AttributeKey.SequentialSiteCount:
                    return value.IntValue.ToString(invariant) + " binding sites";
                case AttributeKey.PreboundLigandConc:
                    return value.ParameterValue.Value.ToString("G17", invariant) + " mol/L";
                case AttributeKey.Species:
                    return string.IsNullOrWhiteSpace(value.StringValue)
                        ? ""
                        : ExperimentAttribute.GetSpeciesLocationDisplayName(value.IntValue) + ": " + value.StringValue;
            }

            switch (value.Key.GetProperties().Type)
            {
                case ExperimentAttribute.AttributeType.Bool: return value.BoolValue ? "Yes" : "No";
                case ExperimentAttribute.AttributeType.Enum:
                case ExperimentAttribute.AttributeType.Int: return value.IntValue.ToString(invariant);
                case ExperimentAttribute.AttributeType.Double: return value.DoubleValue.ToString("G17", invariant);
                case ExperimentAttribute.AttributeType.ParameterAffinity:
                case ExperimentAttribute.AttributeType.ParameterConcentration:
                case ExperimentAttribute.AttributeType.Parameter: return value.ParameterValue.Value.ToString("G17", invariant);
                case ExperimentAttribute.AttributeType.ReferenceExperiment:
                    return experiment?.ReferenceExperiment?.Name ?? "Missing reference experiment";
                case ExperimentAttribute.AttributeType.String: return value.StringValue ?? "";
                default: return value.StringValue ?? "";
            }
        }

        static string StoichiometryInvariant(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return "";
            const double tolerance = 1e-4;
            var rounded = Math.Round(value);
            if (Math.Abs(value - rounded) < tolerance)
                return rounded.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ":1";
            if (value > 0 && value < 1)
            {
                var reciprocal = Math.Round(1.0 / value);
                if (Math.Abs(value - (1.0 / reciprocal)) < tolerance)
                    return "1:" + reciprocal.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
            }
            return value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture);
        }

        static double? Confidence95(AnalysisITC.Core.Numerics.FloatWithError value, int index)
        {
            if (value.DistributionConfidence95 == null || value.DistributionConfidence95.Length <= index) return null;
            return Finite(value.DistributionConfidence95[index]);
        }

        static double? PositiveRatio(double numerator, double denominator)
        {
            if (double.IsNaN(numerator) || double.IsInfinity(numerator)
                || double.IsNaN(denominator) || double.IsInfinity(denominator)
                || denominator <= 0) return null;
            return Finite(numerator / denominator);
        }

        static InterpretationInstrumentEvidence Instrument(ExperimentData data)
        {
            var filters = (data.Injections ?? new List<InjectionData>())
                .Select(injection => Positive(injection.Filter)).Where(value => value.HasValue)
                .Select(value => value.Value).ToList();
            var distinct = filters.Distinct().OrderBy(value => value).ToList();
            return new InterpretationInstrumentEvidence
            {
                ModelId = data.Instrument == ITCInstrument.Unknown ? null : InstrumentId(data.Instrument),
                ModelName = data.Instrument == ITCInstrument.Unknown ? null : data.Instrument.GetProperties().Name,
                CellVolumeLitres = Positive(data.CellVolume),
                FeedbackMode = data.FeedBackMode == FeedbackMode.Null ? null : data.FeedBackMode.GetProperties().Name,
                StirringSpeedRpm = NonNegative(data.StirringSpeed),
                FilterPeriodSeconds = distinct.Count == 1 ? distinct[0] : (double?)null,
                FilterPeriodVariesByInjection = distinct.Count > 1,
                MinimumFilterPeriodSeconds = distinct.Count > 1 ? distinct.First() : (double?)null,
                MaximumFilterPeriodSeconds = distinct.Count > 1 ? distinct.Last() : (double?)null,
            };
        }

        static string InstrumentId(ITCInstrument value) => value switch
        {
            ITCInstrument.MicroCalITC200 => "microcal-itc200",
            ITCInstrument.MalvernITC200 => "microcal-peaq-itc",
            ITCInstrument.MicroCalVPITC => "microcal-vp-itc",
            ITCInstrument.TAInstrumentsITCStandard => "ta-itc-standard",
            ITCInstrument.TAInstrumentsITCLowVolume => "ta-itc-low-volume",
            _ => null,
        };

        static double? Positive(double? value)
        {
            var finite = Finite(value);
            return finite.HasValue && finite.Value > 0 ? finite : null;
        }

        static double? NonNegative(double? value)
        {
            var finite = Finite(value);
            return finite.HasValue && finite.Value >= 0 ? finite : null;
        }

        static double? AttributeNumber(ExperimentAttribute value)
        {
            switch (value.Key.GetProperties().Type)
            {
                case ExperimentAttribute.AttributeType.Double: return Finite(value.DoubleValue);
                case ExperimentAttribute.AttributeType.Int:
                case ExperimentAttribute.AttributeType.Enum: return value.IntValue;
                case ExperimentAttribute.AttributeType.Parameter:
                case ExperimentAttribute.AttributeType.ParameterAffinity:
                case ExperimentAttribute.AttributeType.ParameterConcentration: return Finite(value.ParameterValue.Value);
                default: return null;
            }
        }

        static bool? AttributeBoolean(ExperimentAttribute value) =>
            value.Key.GetProperties().Type == ExperimentAttribute.AttributeType.Bool ? value.BoolValue : (bool?)null;

        static double? AxisValue(AnalysisXAxisType type, InjectionData injection) => type switch
        {
            AnalysisXAxisType.TitrantConcentration => Finite(injection.ActualTitrantConcentration),
            AnalysisXAxisType.ID => injection.ID + 1,
            _ => Finite(injection.Ratio),
        };

        internal static string QuantityId(ParameterType key)
        {
            if (key == ParameterType.ApparentAffinity) return "apparent-kd";
            var wire = FtxtcWireIds.Parameter(key);
            return wire.StartsWith("affinity-log10-", StringComparison.Ordinal)
                ? "kd-" + wire.Substring("affinity-log10-".Length)
                : wire;
        }

        static string Unit(ParameterType key)
        {
            switch (key.GetProperties().ParentType)
            {
                case ParameterType.Affinity1: return "mol/L";
                case ParameterType.Enthalpy1:
                case ParameterType.Gibbs1:
                case ParameterType.EntropyContribution1:
                case ParameterType.Offset: return "J/mol";
                case ParameterType.HeatCapacity1:
                case ParameterType.Entropy1: return "J/(mol*K)";
                case ParameterType.IsomerizationRate: return "1/s";
                default: return "1";
            }
        }

        static void AddContextEvidence(AnalysisInterpretationPackage package, AnalysisStudyContext context)
        {
            AddContext(package, "scientific-question", context.ScientificQuestion);
            AddContext(package, "system-description", context.SystemDescription);
            AddContext(package, "component-types", context.ComponentTypes);
            AddContext(package, "interaction-considerations", context.InteractionConsiderations);
            AddContext(package, "expected-outcome", context.ExpectedOutcome);
            AddContext(package, "cell-contents", context.CellContentsAndRole);
            AddContext(package, "syringe-contents", context.SyringeContentsAndRole);
            AddContext(package, "related-systems", context.RelatedSystemsOrConstructs);
            AddContext(package, "previous-results-controls", context.PreviousResultsAndControls);
            AddContext(package, "buffer-considerations", context.BufferConsiderations);
            AddContext(package, "temperature-considerations", context.TemperatureConsiderations);
            AddContext(package, "additional-notes", context.AdditionalNotes);
            for (var i = 0; i < (context.References?.Count ?? 0); i++)
                AddEvidence(package, $"context/reference-{i + 1}", "user-reference", context.References[i].Label, null);
            for (var i = 0; i < (context.Experiments?.Count ?? 0); i++)
                AddEvidence(package, $"context/experiment-{i + 1}", "experiment-context",
                    context.Experiments[i].ExperimentId, null);
        }

        static void AddContext(AnalysisInterpretationPackage package, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) AddEvidence(package, "context/" + key, "context", key, null);
        }

        static void AddEvidence(AnalysisInterpretationPackage package, string id, string kind, string label, string parentId) =>
            package.EvidenceCatalog.Add(new InterpretationEvidenceCatalogEntry { Id = id, Kind = kind, Label = label ?? "", ParentId = parentId });

        static double? Safe(Func<double> read)
        {
            try { return Finite(read()); } catch { return null; }
        }

        static AnalysisITC.Core.Numerics.FloatWithError? SafeFwe(Func<AnalysisITC.Core.Numerics.FloatWithError> read)
        {
            try { return read(); } catch { return null; }
        }

        static double? Finite(double? value) => value.HasValue && !double.IsNaN(value.Value) && !double.IsInfinity(value.Value) ? value : null;
        static double? Celsius(double? kelvin)
        {
            var value = Finite(kelvin);
            return value.HasValue ? Math.Round(value.Value - 273.15, 1, MidpointRounding.AwayFromZero) : (double?)null;
        }
        static string Utc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc
                ? value
                : value.Kind == DateTimeKind.Local
                    ? value.ToUniversalTime()
                    : DateTime.SpecifyKind(value, DateTimeKind.Utc);
            return utc.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
