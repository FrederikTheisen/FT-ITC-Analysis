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
        public const string PackageSchemaVersion = "1.0";

        public static AnalysisInterpretationPackage Build(
            AnalysisReport report,
            AnalysisResult result,
            AnalysisInterpretationOptions options = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (report.ResultIds.Count != 1)
                throw new InvalidOperationException("Interpretation package version 1 requires exactly one report result ID.");
            if (!string.Equals(report.ResultIds[0], result.UniqueID, StringComparison.Ordinal))
                throw new InvalidOperationException("The supplied result does not match the report result ID.");
            if (result.Solution?.Model == null)
                throw new InvalidOperationException("The analysis result has no fitted model.");

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
            package.Result = BuildResult(package, result, requested);
            return package;
        }

        static InterpretationResultEvidence BuildResult(
            AnalysisInterpretationPackage package,
            AnalysisResult result,
            AnalysisInterpretationOptions options)
        {
            const string resultEvidenceId = "result-1";
            AddEvidence(package, resultEvidenceId, "result", result.Name, "report-1");
            var validity = result.ValidityReport;
            var global = result.Solution;
            var convergence = global.Convergence;
            var value = new InterpretationResultEvidence
            {
                EvidenceId = resultEvidenceId,
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
                    UnweightedRmsdMicrojoules = Finite(global.Loss),
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
                InformationCriteria = InformationCriteria(result.InformationCriteria),
            };

            var members = global.Solutions.Where(member => member?.Data != null).ToList();
            for (var index = 0; index < members.Count; index++)
                value.Experiments.Add(BuildExperiment(package, members[index], index + 1, global, options));

            foreach (var dependency in global.TemperatureDependence.OrderBy(item => FtxtcWireIds.Parameter(item.Key), StringComparer.Ordinal))
            {
                value.TemperatureDependence.Add(new InterpretationTemperatureDependenceEvidence
                {
                    ParameterId = QuantityId(dependency.Key),
                    SiUnit = Unit(dependency.Key),
                    ReferenceTemperatureKelvin = Finite(dependency.Value.ReferenceT + 273.15),
                    InterceptSi = Finite(dependency.Value.Intercept.Value),
                    SlopeSiPerKelvin = Finite(dependency.Value.Slope.Value),
                });
            }

            AddAdvancedAnalyses(value.AdvancedAnalyses, result);

            for (var index = 0; index < members.Count; index++)
                value.BootstrapCorrelations.Add(Correlation(global, index, $"result-1/experiment-{index + 1}"));
            value.BootstrapCorrelation = value.BootstrapCorrelations.FirstOrDefault();
            return value;
        }

        static InterpretationExperimentEvidence BuildExperiment(
            AnalysisInterpretationPackage package,
            SolutionInterface solution,
            int ordinal,
            GlobalSolution global,
            AnalysisInterpretationOptions options)
        {
            var data = solution.Data;
            var evidenceId = $"result-1/experiment-{ordinal}";
            AddEvidence(package, evidenceId, "experiment", data.Name, "result-1");
            var output = new InterpretationExperimentEvidence
            {
                EvidenceId = evidenceId, ExperimentId = data.UniqueID, Name = data.Name,
                SourceFileBasename = Path.GetFileName(data.FileName ?? ""), DateUtc = Utc(data.Date),
                Comments = data.Comments, Instrument = data.Instrument.ToString(),
                TargetTemperatureKelvin = Finite(data.TargetTemperature + 273.15),
                MeasuredTemperatureKelvin = Finite(data.MeasuredTemperatureKelvin),
                CellConcentrationMolar = Finite(data.CellConcentration.Value),
                CellConcentrationSdMolar = Finite(data.CellConcentration.SD),
                SyringeConcentrationMolar = Finite(data.SyringeConcentration.Value),
                SyringeConcentrationSdMolar = Finite(data.SyringeConcentration.SD),
                CellVolumeLitres = Finite(data.CellVolume), StirringSpeedRpm = Finite(data.StirringSpeed),
                FeedbackMode = data.FeedBackMode.ToString(), AnalysisAxis = data.AxisType.ToString(),
                BaselineCompleted = data.Processor?.BaselineCompleted == true,
                IntegrationCompleted = data.Processor?.IntegrationCompleted == true,
                BaselineProcessor = data.Processor?.BaselineType.ToString(),
                ProcessorLocked = data.Processor?.IsLocked == true,
                DiscardsIntegratedPointsForBaseline = data.Processor?.DiscardIntegratedPoints == true,
                IntegrationLengthMode = data.Processor?.IntegrationLengthMode.ToString(),
                IntegrationLengthFactor = Finite(data.Processor?.IntegrationLengthFactor),
                InitialDelaySeconds = Finite(data.InitialDelay),
                Attributes = NamedValues(data.Attributes),
                ModelOptions = NamedValues(solution.ModelOptions),
            };

            foreach (var item in solution.ReportParameters.OrderBy(item => QuantityId(item.Key), StringComparer.Ordinal))
            {
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
                    var fitted = Safe(() => solution.Model.EvaluateEnthalpy(injection.ID, true));
                    var residual = Safe(() => solution.Model.Residual(injection) / injection.InjectionMass);
                    double? lower = null, upper = null;
                    if (global.ErrorEstimationMethod == ErrorEstimationMethod.BootstrapResiduals
                        && solution.BootstrapSolutions?.Count > 0)
                    {
                        var band = SafeFwe(() => solution.Model.EvaluateBootstrap(injection.ID, true));
                        lower = band.HasValue ? Finite(band.Value.Lower) : null;
                        upper = band.HasValue ? Finite(band.Value.Upper) : null;
                    }
                    output.Injections.Add(new InterpretationInjectionEvidence
                    {
                        EvidenceId = id, InjectionId = injection.ID + 1, Included = injection.Include,
                        VolumeLitres = Finite(injection.Volume),
                        ActualCellConcentrationMolar = Finite(injection.ActualCellConcentration),
                        ActualTitrantConcentrationMolar = Finite(injection.ActualTitrantConcentration),
                        AnalysisAxisKind = data.AxisType.ToString(),
                        AnalysisAxisValue = AxisValue(data.AxisType, injection),
                        ObservedHeatJoulesPerMole = Finite(injection.Enthalpy),
                        ObservedUncertaintyJoulesPerMole = Finite(injection.SD),
                        FittedHeatJoulesPerMole = fitted,
                        ResidualJoulesPerMole = residual,
                        Confidence95LowerJoulesPerMole = lower,
                        Confidence95UpperJoulesPerMole = upper,
                    });
                    AddEvidence(package, id, "injection", $"Injection {injection.ID + 1}", evidenceId);
                }
            }
            return output;
        }

        static InterpretationParameterEvidence BuildParameter(
            SolutionInterface solution, GlobalSolution global, ParameterType key,
            AnalysisITC.Core.Numerics.FloatWithError reported, string experimentEvidenceId)
        {
            var quantityId = QuantityId(key);
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
                Name = key.GetProperties().Name,
                SiUnit = Unit(key),
                BestFitValue = Finite(reported.Value), StandardDeviation = Finite(reported.SD),
                Confidence95Lower = Finite(reported.Lower), Confidence95Upper = Finite(reported.Upper),
                IsFittedCoordinate = isDirect && coordinate.IsFitted,
                IsDerived = !isDirect,
                IsLocked = coordinate?.IsLocked == true,
                Constraint = global.Model.Parameters.GetConstraintForParameter(key).ToString(),
                BoundaryWarning = contacts.Any(contact => contact.Parameter == key),
                FittedLowerBound = coordinate?.Limits?.Length >= 2 ? Finite(coordinate.Limits[0]) : null,
                FittedUpperBound = coordinate?.Limits?.Length >= 2 ? Finite(coordinate.Limits[1]) : null,
                UncertaintyMethod = global.ErrorEstimationMethod.ToString(),
            };
        }

        static InterpretationInformationCriteriaEvidence InformationCriteria(FitInformationCriteria value)
        {
            if (value == null) return null;
            return new InterpretationInformationCriteriaEvidence
            {
                ObservationCount = value.ObservationCount, FittedParameterCount = value.FittedParameterCount,
                LikelihoodParameterCount = value.LikelihoodParameterCount,
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

        static List<InterpretationNamedValue> NamedValues(IDictionary<AttributeKey, ExperimentAttribute> values) =>
            NamedValues(values?.Values);

        static List<InterpretationNamedValue> NamedValues(IEnumerable<ExperimentAttribute> values)
        {
            return (values ?? Enumerable.Empty<ExperimentAttribute>())
                .OrderBy(value => FtxtcWireIds.Attribute(value.Key), StringComparer.Ordinal)
                .ThenBy(value => value.OptionName, StringComparer.Ordinal)
                .Select(value => new InterpretationNamedValue
                {
                    Name = FtxtcWireIds.Attribute(value.Key), Type = value.Key.GetProperties().Type.ToString(),
                    TextValue = value.StringValue ?? value.OptionName,
                    NumericValue = AttributeNumber(value), BooleanValue = AttributeBoolean(value),
                }).ToList();
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
