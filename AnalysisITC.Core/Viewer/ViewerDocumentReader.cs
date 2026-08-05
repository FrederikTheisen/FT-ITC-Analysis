using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.Viewer
{
    public sealed class ViewerDocumentReader
    {
        public async Task<ViewerDocument> ReadAsync(
            Stream stream,
            string displayFileName,
            ViewerFileFormat format,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var safeName = SafeDisplayName(displayFileName, format);
            var buffer = new MemoryStream();
            await CopyToAsync(stream, buffer, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (buffer.Length == 0)
                throw new ViewerFileException("empty_file", "The uploaded file is empty.");

            var header = ReadHeader(buffer);
            ValidateSignature(header, format);
            buffer.Position = 0;

            try
            {
                var parseWarnings = new List<string>();
                ITCDataContainer[] containers;
                if (format == ViewerFileFormat.Ftitc)
                    containers = await FTITCReader.ReadStream(buffer, interactive: false);
                else
                    containers = new ITCDataContainer[]
                    {
                        MicroCalITC200Reader.ReadStream(
                            buffer,
                            safeName,
                            interactive: false,
                            warning: message => parseWarnings.Add(message))
                    };

                cancellationToken.ThrowIfCancellationRequested();
                var document = BuildDocument(containers, safeName, format, buffer.Length, header);
                document.Warnings.InsertRange(0, parseWarnings);
                if (document.Experiments.Count == 0)
                    throw new ViewerFileException("no_experiments", "The file did not contain a readable ITC experiment.");

                return document;
            }
            catch (ViewerFileException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is InvalidDataException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is InvalidOperationException || ex is NullReferenceException)
            {
                throw new ViewerFileException("malformed_file", "The file is malformed or incomplete and could not be read.", ex);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        static async Task CopyToAsync(Stream source, Stream destination, CancellationToken cancellationToken)
        {
            var bytes = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(bytes, 0, bytes.Length, cancellationToken)) > 0)
            {
                await destination.WriteAsync(bytes, 0, read, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            destination.Position = 0;
        }

        static string SafeDisplayName(string fileName, ViewerFileFormat format)
        {
            var fallback = format == ViewerFileFormat.Ftitc ? "uploaded.ftitc" : "uploaded.itc";
            var normalized = (fileName ?? fallback).Replace('\\', '/');
            var name = Path.GetFileName(normalized);
            if (string.IsNullOrWhiteSpace(name)) name = fallback;

            var cleaned = new string(name.Where(ch => !char.IsControl(ch)).Take(180).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }

        static string ReadHeader(MemoryStream stream)
        {
            stream.Position = 0;
            var length = (int)Math.Min(stream.Length, 4096);
            var bytes = new byte[length];
            stream.Read(bytes, 0, bytes.Length);
            stream.Position = 0;
            return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF', '\r', '\n', ' ', '\t');
        }

        static void ValidateSignature(string header, ViewerFileFormat format)
        {
            var valid = format == ViewerFileFormat.Ftitc
                ? header.StartsWith("FTITCVersion:", StringComparison.Ordinal) || header.StartsWith("FILE:Experiment:", StringComparison.Ordinal) || header.StartsWith("FILE:TandemExperiment:", StringComparison.Ordinal)
                : header.StartsWith("$ITC", StringComparison.OrdinalIgnoreCase);

            if (!valid)
                throw new ViewerFileException("format_mismatch", "The file contents do not match the selected file extension.");
        }

        static ViewerDocument BuildDocument(
            IEnumerable<ITCDataContainer> containers,
            string displayName,
            ViewerFileFormat format,
            long size,
            string header)
        {
            var all = containers?.Where(item => item != null).ToList() ?? new List<ITCDataContainer>();
            var experiments = all.OfType<ExperimentData>().ToList();
            var results = all.OfType<AnalysisResult>().ToList();
            var document = new ViewerDocument
            {
                DisplayName = displayName,
                Format = format == ViewerFileFormat.Ftitc ? "ftitc" : "itc",
                SizeBytes = size,
                FormatVersion = format == ViewerFileFormat.Ftitc ? ParseVersion(header) : null,
            };

            var resultKeys = results
                .Select((result, index) => new { Result = result, Key = ResultKey(index) })
                .ToDictionary(item => item.Result, item => item.Key);
            var fitsByExperiment = CollectFits(experiments, results, resultKeys);
            var experimentKeys = experiments
                .Select((experiment, index) => new { experiment.UniqueID, Key = ExperimentKey(index) })
                .ToDictionary(item => item.UniqueID, item => item.Key);

            for (var index = 0; index < experiments.Count; index++)
            {
                var experiment = experiments[index];
                fitsByExperiment.TryGetValue(experiment.UniqueID, out var fitSources);
                document.Experiments.Add(BuildExperiment(experiment, index, fitSources ?? new List<FitSource>(), document.Warnings));
            }

            var fitsByKey = document.Experiments
                .SelectMany(experiment => experiment.Fits)
                .GroupBy(fit => fit.Key)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var item in results
                .Select((result, index) => new { Result = result, Index = index })
                .OrderByDescending(item => item.Result.Date == default(DateTime) ? DateTime.MinValue : item.Result.Date)
                .ThenBy(item => item.Index))
            {
                document.AnalysisResults.Add(BuildAnalysisResult(item.Result, resultKeys[item.Result], experimentKeys, fitsByKey));
            }

            return document;
        }

        static string ParseVersion(string header)
        {
            var firstLine = header.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (firstLine == null || !firstLine.StartsWith("FTITCVersion:", StringComparison.Ordinal)) return null;
            return firstLine.Substring("FTITCVersion:".Length).Trim();
        }

        static Dictionary<string, List<FitSource>> CollectFits(
            IEnumerable<ExperimentData> experiments,
            IEnumerable<AnalysisResult> results,
            IReadOnlyDictionary<AnalysisResult, string> resultKeys)
        {
            var map = experiments.ToDictionary(item => item.UniqueID, item => new List<FitSource>());
            foreach (var item in experiments.Select((experiment, index) => new { Experiment = experiment, Index = index }))
            {
                if (item.Experiment.Solution != null)
                    map[item.Experiment.UniqueID].Add(new FitSource(
                        item.Experiment.Solution,
                        "Saved experiment fit",
                        ExperimentKey(item.Index) + ":embedded-fit",
                        resultKey: null));
            }

            foreach (var result in results)
            {
                if (result.Solution?.Solutions == null) continue;
                foreach (var item in result.Solution.Solutions
                    .Select((solution, index) => new { Solution = solution, Index = index })
                    .Where(item => item.Solution?.Data != null))
                {
                    var solution = item.Solution;
                    if (!map.TryGetValue(solution.Data.UniqueID, out var list)) continue;
                    var name = string.IsNullOrWhiteSpace(result.Name) ? result.FileName : result.Name;
                    var resultKey = resultKeys[result];
                    list.Add(new FitSource(solution, name, ResultFitKey(resultKey, item.Index), resultKey));
                }
            }

            foreach (var entry in map)
            {
                var unique = entry.Value
                    .GroupBy(item => item.Key)
                    .Select(group => group.Last())
                    .ToList();
                entry.Value.Clear();
                entry.Value.AddRange(unique);
            }

            return map;
        }

        static ViewerAnalysisResult BuildAnalysisResult(
            AnalysisResult result,
            string resultKey,
            IReadOnlyDictionary<string, string> experimentKeys,
            IReadOnlyDictionary<string, FitDto> fitsByKey)
        {
            var solution = result?.Solution;
            var members = solution?.Solutions ?? new List<SolutionInterface>();
            var viewer = new ViewerAnalysisResult
            {
                Key = resultKey,
                Name = string.IsNullOrWhiteSpace(result?.Name) ? result?.FileName : result.Name,
                Date = result == null || result.Date == default(DateTime) ? (DateTime?)null : result.Date,
                Comments = result?.Comments ?? string.Empty,
                ModelName = solution?.Model?.ModelType.GetProperties()?.Name ?? solution?.Model?.ModelType.ToString() ?? "Unknown model",
                IsGlobal = solution?.Model?.Parameters?.Constraints?.Any(item => item.Value != VariableConstraint.None) == true,
                ExperimentCount = members.Count,
                Loss = solution?.Convergence == null ? (double?)null : FiniteOrNull(solution.Convergence.Loss),
                Solver = BuildSolver(solution),
                Validity = BuildValidity(result),
                TemperatureParameterEvaluation = BuildTemperatureParameterEvaluation(result),
            };

            foreach (var option in solution?.Model?.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>())
            {
                viewer.ModelOptions.Add(new ViewerSettingDto
                {
                    Key = option.Key.ToString(),
                    Label = string.IsNullOrWhiteSpace(option.Value?.OptionName) ? option.Key.GetEnumDescription() : option.Value.OptionName,
                    Value = FormatModelOption(option.Key, option.Value),
                });
            }

            foreach (var constraint in solution?.Model?.Parameters?.Constraints ?? new Dictionary<ParameterType, VariableConstraint>())
            {
                if (constraint.Value == VariableConstraint.None) continue;
                viewer.Constraints.Add(new ViewerSettingDto
                {
                    Key = constraint.Key.ToString(),
                    Label = constraint.Key.GetProperties()?.Name ?? constraint.Key.ToString(),
                    Value = constraint.Value.GetEnumDescription(),
                });
            }

            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                var data = member?.Data;
                var fitKey = member == null ? null : ResultFitKey(resultKey, index);
                experimentKeys.TryGetValue(data?.UniqueID ?? string.Empty, out var experimentKey);
                var fitAvailable = fitKey != null && fitsByKey.ContainsKey(fitKey);
                var availability = string.Empty;

                if (string.IsNullOrWhiteSpace(experimentKey))
                    availability = "The saved result member does not reference an experiment in this project.";
                else if (!fitAvailable)
                    availability = "The saved fit for this experiment could not be displayed.";

                if (!string.IsNullOrWhiteSpace(availability))
                    viewer.Warnings.Add((data?.Name ?? $"Experiment {index + 1}") + ": " + availability);

                viewer.Members.Add(new ViewerAnalysisResultMemberDto
                {
                    ExperimentKey = experimentKey,
                    FitKey = fitAvailable ? fitKey : null,
                    ExperimentName = data?.Name ?? $"Experiment {index + 1}",
                    TemperatureCelsius = data == null ? (double?)null : FiniteOrNull(data.MeasuredTemperature),
                    Loss = member?.Convergence == null ? (double?)null : FiniteOrNull(member.Convergence.Loss),
                    SolutionValid = member?.IsValid == true,
                    AvailabilityMessage = availability,
                });
            }

            return viewer;
        }

        static ViewerTemperatureParameterEvaluationDto BuildTemperatureParameterEvaluation(AnalysisResult result)
        {
            var solution = result?.Solution;
            var dependences = solution?.TemperatureDependence;
            if (dependences == null || dependences.Count == 0) return null;

            var temperatures = solution.Solutions
                .Select(member => FiniteOrNull(member?.Data?.MeasuredTemperature ?? double.NaN))
                .Where(temperature => temperature.HasValue)
                .Select(temperature => temperature.Value)
                .ToArray();
            var viewer = new ViewerTemperatureParameterEvaluationDto
            {
                DefaultTemperatureCelsius = FiniteOrNull(solution.MeanTemperature) ?? 25.0,
                MinimumTemperatureCelsius = temperatures.Length == 0 ? (double?)null : temperatures.Min(),
                MaximumTemperatureCelsius = temperatures.Length == 0 ? (double?)null : temperatures.Max(),
                IsTemperatureDependent = solution.Model?.TemperatureDependenceExposed == true,
            };

            var keys = new[]
            {
                ParameterType.Enthalpy1,
                ParameterType.EntropyContribution1,
                ParameterType.Gibbs1,
                ParameterType.Enthalpy2,
                ParameterType.EntropyContribution2,
                ParameterType.Gibbs2,
            };
            foreach (var key in keys)
            {
                if (!dependences.TryGetValue(key, out var dependence)) continue;
                viewer.Dependences.Add(BuildTemperatureDependence(key, dependence));
            }

            return viewer.Dependences.Count == 0 ? null : viewer;
        }

        static ViewerTemperatureDependenceDto BuildTemperatureDependence(ParameterType key, LinearFitWithError dependence)
        {
            const double energyScale = 1.0 / 1000.0;
            return new ViewerTemperatureDependenceDto
            {
                Key = key.ToString(),
                Label = key.GetProperties().Name,
                Unit = "kJ/mol",
                SlopeUnit = "kJ/(mol·K)",
                ReferenceTemperatureCelsius = dependence.ReferenceT,
                Intercept = BuildValueWithError(dependence.Intercept, energyScale),
                Slope = BuildValueWithError(dependence.Slope, energyScale),
            };
        }

        static ViewerValueWithErrorDto BuildValueWithError(FloatWithError value, double scale)
        {
            return new ViewerValueWithErrorDto
            {
                Value = value.Value * scale,
                Sd = value.SD * scale,
                ConfidenceLower = FiniteOrNull(value.Lower * scale),
                ConfidenceUpper = FiniteOrNull(value.Upper * scale),
            };
        }

        static ViewerSolverDto BuildSolver(GlobalSolution solution)
        {
            var convergence = solution?.Convergence;
            ErrorEstimationMethod? errorMethod = null;
            try
            {
                if (solution?.ModelCloneOptions != null) errorMethod = solution.ErrorEstimationMethod;
            }
            catch
            {
                errorMethod = null;
            }

            return new ViewerSolverDto
            {
                Algorithm = convergence == null ? null : convergence.Algorithm.GetProperties()?.Name ?? convergence.Algorithm.ToString(),
                Termination = convergence?.Termination.GetEnumDescription(),
                Convergence = convergence?.Message,
                Iterations = convergence?.Iterations,
                WeightedFitting = solution?.UseWeightedFitting == true,
                ErrorEstimationMethod = errorMethod?.Description(),
                ErrorEstimationSummary = convergence?.ErrorEstimationSummary,
                BootstrapIterations = solution?.BootstrapSolutions?.Count ?? 0,
                ElapsedSeconds = convergence == null ? (double?)null : FiniteOrNull(convergence.TotalTime.TotalSeconds),
            };
        }

        static ViewerValidityDto BuildValidity(AnalysisResult result)
        {
            var validity = new ViewerValidityDto { Status = "unknown" };
            if (result == null)
            {
                validity.Reasons.Add("No saved analysis result is available.");
                return validity;
            }

            var report = result.ValidityReport;
            validity.Status = report.Status switch
            {
                AnalysisResultValidity.Valid => "valid",
                AnalysisResultValidity.PartialInvalid => "partialInvalid",
                AnalysisResultValidity.Invalid => "invalid",
                _ => "unknown",
            };
            validity.Reasons.AddRange(report.Reasons ?? new List<string>());
            return validity;
        }

        static string FormatModelOption(AttributeKey key, ExperimentAttribute option)
        {
            if (option == null) return "Unavailable";

            switch (key)
            {
                case AttributeKey.PreboundLigandAffinity:
                    var kdMicromolar = 1.0 / Math.Pow(10.0, option.ParameterValue.Value) * 1e6;
                    return IsFinite(kdMicromolar) ? kdMicromolar.ToString("G6", CultureInfo.InvariantCulture) + " µM" : "Unavailable";
                case AttributeKey.PreboundLigandEnthalpy:
                    return (option.ParameterValue.Value / 1000.0).ToString("G6", CultureInfo.InvariantCulture) + " kJ/mol";
                case AttributeKey.PreboundLigandConc when option.BoolValue:
                    return "From experiment attribute";
            }

            if (!string.IsNullOrWhiteSpace(option.StringValue)) return option.StringValue;
            if (option.ParameterValue.HasError || Math.Abs(option.ParameterValue.Value) > double.Epsilon)
            {
                var value = option.ParameterValue.Value.ToString("G6", CultureInfo.InvariantCulture);
                return option.ParameterValue.HasError
                    ? value + " ± " + option.ParameterValue.SD.ToString("G3", CultureInfo.InvariantCulture)
                    : value;
            }
            if (Math.Abs(option.DoubleValue) > double.Epsilon) return option.DoubleValue.ToString("G6", CultureInfo.InvariantCulture);
            if (option.IntValue != 0) return option.IntValue.ToString(CultureInfo.InvariantCulture);
            return option.BoolValue ? "Yes" : "No";
        }

        static string ResultFitKey(string resultKey, int memberIndex)
        {
            if (string.IsNullOrWhiteSpace(resultKey) || memberIndex < 0) return null;
            return resultKey + ":member-" + (memberIndex + 1).ToString(CultureInfo.InvariantCulture);
        }

        static string ResultKey(int index) => "result-" + (index + 1).ToString(CultureInfo.InvariantCulture);
        static string ExperimentKey(int index) => "experiment-" + (index + 1).ToString(CultureInfo.InvariantCulture);

        static ViewerExperiment BuildExperiment(
            ExperimentData experiment,
            int index,
            IEnumerable<FitSource> fitSources,
            List<string> warnings)
        {
            var viewer = new ViewerExperiment
            {
                Key = ExperimentKey(index),
                Name = experiment.Name,
                SourceFileName = Path.GetFileName(experiment.FileName),
                SourceFormat = experiment.DataSourceFormat.GetProperties()?.Name ?? experiment.DataSourceFormat.ToString(),
                Date = experiment.Date == default(DateTime) ? (DateTime?)null : experiment.Date,
                Instrument = experiment.Instrument.GetProperties()?.Name ?? experiment.Instrument.ToString(),
                Comments = experiment.Comments ?? string.Empty,
                TargetTemperatureCelsius = FiniteOrNull(experiment.TargetTemperature),
                MeasuredTemperatureCelsius = FiniteOrNull(experiment.MeasuredTemperature),
                SyringeConcentrationMicromolar = FiniteOrNull(experiment.SyringeConcentration.Value * 1e6),
                CellConcentrationMicromolar = FiniteOrNull(experiment.CellConcentration.Value * 1e6),
                CellVolumeMicroliters = FiniteOrNull(experiment.CellVolume * 1e6),
                StirringSpeedRpm = experiment.StirringSpeed >= 0 ? FiniteOrNull(experiment.StirringSpeed) : null,
                InjectionCount = experiment.Injections?.Count ?? 0,
            };

            AddMetadata(viewer, experiment);
            viewer.Raw = BuildRaw(experiment);
            viewer.Integrated = BuildIntegrated(experiment);
            viewer.Processed = BuildProcessed(experiment);

            if (viewer.Raw != null) viewer.AvailableViews.Add("raw");
            if (viewer.Integrated != null && viewer.Integrated.IsIntegrated.Any(value => value)) viewer.AvailableViews.Add("integrated");
            if (viewer.Processed != null) viewer.AvailableViews.Add("processed");

            foreach (var source in fitSources)
            {
                try
                {
                    var fit = BuildFit(experiment, source);
                    if (fit != null) viewer.Fits.Add(fit);
                }
                catch (Exception ex) when (ex is ArithmeticException || ex is InvalidOperationException || ex is KeyNotFoundException || ex is NullReferenceException)
                {
                    warnings.Add($"A saved fit for {experiment.Name} could not be displayed.");
                }
            }

            if (viewer.Fits.Count > 0)
            {
                viewer.DefaultFitKey = viewer.Fits[viewer.Fits.Count - 1].Key;
                viewer.AvailableViews.Add("fit");
            }
            viewer.AvailableViews.Add("metadata");
            return viewer;
        }

        static void AddMetadata(ViewerExperiment viewer, ExperimentData experiment)
        {
            Add("Filename", viewer.SourceFileName);
            Add("Format", viewer.SourceFormat);
            Add("Instrument", viewer.Instrument);
            Add("Date", viewer.Date?.ToString("O", CultureInfo.InvariantCulture));
            Add("Target temperature", Format(viewer.TargetTemperatureCelsius, " °C"));
            Add("Measured temperature", Format(viewer.MeasuredTemperatureCelsius, " °C"));
            Add("Syringe concentration", Format(viewer.SyringeConcentrationMicromolar, " µM"));
            Add("Cell concentration", Format(viewer.CellConcentrationMicromolar, " µM"));
            Add("Cell volume", Format(viewer.CellVolumeMicroliters, " µL"));
            Add("Stirring speed", Format(viewer.StirringSpeedRpm, " rpm"));
            Add("Injections", viewer.InjectionCount.ToString(CultureInfo.InvariantCulture));

            foreach (var attribute in experiment.Attributes ?? new List<ExperimentAttribute>())
            {
                var name = string.IsNullOrWhiteSpace(attribute.OptionName) ? attribute.Key.ToString() : attribute.OptionName;
                var value = !string.IsNullOrWhiteSpace(attribute.StringValue)
                    ? attribute.StringValue
                    : attribute.ParameterValue.HasError || Math.Abs(attribute.ParameterValue.Value) > double.Epsilon
                        ? attribute.ParameterValue.Value.ToString("G6", CultureInfo.InvariantCulture)
                        : attribute.DoubleValue != 0
                            ? attribute.DoubleValue.ToString("G6", CultureInfo.InvariantCulture)
                            : attribute.IntValue != 0
                                ? attribute.IntValue.ToString(CultureInfo.InvariantCulture)
                                : attribute.BoolValue.ToString();
                Add(name, value);
            }

            void Add(string label, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) viewer.Metadata.Add(new ViewerMetadataItem { Label = label, Value = value });
            }
        }

        static string Format(double? value, string suffix) => value.HasValue
            ? value.Value.ToString("G6", CultureInfo.InvariantCulture) + suffix
            : null;

        static RawTraceDto BuildRaw(ExperimentData experiment)
        {
            var points = experiment.DataPoints?.Where(item => IsFinite(item.Time) && IsFinite(item.Power)).ToArray();
            if (points == null || points.Length < 2) return null;

            var temperatures = points.All(item => IsFinite(item.Temperature))
                ? points.Select(item => (double)item.Temperature).ToArray()
                : null;
            var raw = new RawTraceDto
            {
                TimeSeconds = points.Select(item => (double)item.Time).ToArray(),
                PowerMicrowatts = points.Select(item => item.Power * 1e6).ToArray(),
                TemperatureCelsius = temperatures,
                InjectionNumbers = experiment.Injections.Select(item => item.ID + 1).ToArray(),
                InjectionTimesSeconds = experiment.Injections.Select(item => (double)item.Time).ToArray(),
            };
            if (temperatures == null) raw.UnavailableChannels.Add("Temperature was not recorded for this file.");
            return raw;
        }

        static IntegratedTraceDto BuildIntegrated(ExperimentData experiment)
        {
            var injections = experiment.Injections?.ToArray() ?? Array.Empty<InjectionData>();
            if (injections.Length == 0) return null;
            var axis = Axis(experiment);

            return new IntegratedTraceDto
            {
                InjectionNumbers = injections.Select(item => item.ID + 1).ToArray(),
                Included = injections.Select(item => item.Include).ToArray(),
                IsIntegrated = injections.Select(item => item.IsIntegrated).ToArray(),
                RawHeatMicrojoules = injections.Select(item => item.IsIntegrated ? FiniteOrNull(item.RawPeakArea.Value * 1e6) : null).ToArray(),
                CorrectedHeatMicrojoules = injections.Select(item => item.IsIntegrated ? FiniteOrNull(item.PeakArea.Value * 1e6) : null).ToArray(),
                HeatSdMicrojoules = injections.Select(item => item.IsIntegrated ? FiniteOrNull(item.PeakArea.SD * 1e6) : null).ToArray(),
                MolarHeatKilojoulesPerMole = injections.Select(item => item.IsIntegrated && item.InjectionMass != 0 ? FiniteOrNull(item.Enthalpy / 1000) : null).ToArray(),
                MolarHeatSdKilojoulesPerMole = injections.Select(item => item.IsIntegrated && item.InjectionMass != 0 ? FiniteOrNull(item.SD / 1000) : null).ToArray(),
                InjectionVolumeMicroliters = injections.Select(item => item.Volume * 1e6).ToArray(),
                InjectionTimeSeconds = injections.Select(item => (double)item.Time).ToArray(),
                InjectionTemperatureCelsius = injections.Select(item => item.Temperature).ToArray(),
                CellConcentrationMicromolar = injections.Select(item => item.ActualCellConcentration * 1e6).ToArray(),
                TitrantConcentrationMicromolar = injections.Select(item => item.ActualTitrantConcentration * 1e6).ToArray(),
                AnalysisX = injections.Select(item => AnalysisX(experiment, item)).ToArray(),
                AnalysisXAxisName = axis.Item1,
                AnalysisXAxisUnit = axis.Item2,
            };
        }

        static ProcessedTraceDto BuildProcessed(ExperimentData experiment)
        {
            var raw = experiment.DataPoints;
            var corrected = experiment.BaseLineCorrectedDataPoints;
            var baseline = experiment.Processor?.Interpolator?.Baseline;
            if (raw == null || corrected == null || baseline == null || raw.Count < 2 || corrected.Count != raw.Count || baseline.Count != raw.Count)
                return null;

            var controlTimes = Array.Empty<double>();
            var controlPowers = Array.Empty<double>();
            if (experiment.Processor.Interpolator is SplineInterpolator spline)
            {
                controlTimes = spline.SplinePoints.Select(item => item.Time).ToArray();
                controlPowers = spline.SplinePoints.Select(item => item.Power * 1e6).ToArray();
            }

            return new ProcessedTraceDto
            {
                TimeSeconds = raw.Select(item => (double)item.Time).ToArray(),
                RawPowerMicrowatts = raw.Select(item => item.Power * 1e6).ToArray(),
                BaselinePowerMicrowatts = baseline.Select(item => item.Value * 1e6).ToArray(),
                CorrectedPowerMicrowatts = corrected.Select(item => item.Power * 1e6).ToArray(),
                BaselineMethod = experiment.Processor.BaselineType.ToString(),
                ControlPointTimesSeconds = controlTimes,
                ControlPointPowerMicrowatts = controlPowers,
                IntegrationStartSeconds = experiment.Injections.Select(item => (double)item.IntegrationStartTime).ToArray(),
                IntegrationEndSeconds = experiment.Injections.Select(item => (double)item.IntegrationEndTime).ToArray(),
            };
        }

        static FitDto BuildFit(ExperimentData experiment, FitSource source)
        {
            var solution = source.Solution;
            if (solution?.Model == null || experiment.Injections == null || experiment.Injections.Count == 0) return null;
            var injections = experiment.Injections.ToArray();
            var axis = Axis(experiment);
            var fit = new FitDto
            {
                Key = source.Key,
                ResultKey = source.ResultKey,
                ResultName = source.Name,
                ModelName = solution.ModelType.GetProperties()?.Name ?? solution.ModelType.ToString(),
                IsGlobal = solution.IsGlobalAnalysisSolution,
                AnalysisXAxisName = axis.Item1,
                AnalysisXAxisUnit = axis.Item2,
                X = injections.Select(item => AnalysisX(experiment, item)).ToArray(),
                ObservedKilojoulesPerMole = injections.Select(item => item.IsIntegrated && item.InjectionMass != 0 ? FiniteOrNull(item.Enthalpy / 1000) : null).ToArray(),
                ObservationSdKilojoulesPerMole = injections.Select(item => item.IsIntegrated && item.InjectionMass != 0 ? FiniteOrNull(item.SD / 1000) : null).ToArray(),
                FittedKilojoulesPerMole = injections.Select(item => FiniteOrNull(solution.Model.EvaluateEnthalpy(item.ID, true) / 1000)).ToArray(),
                ResidualKilojoulesPerMole = injections.Select(item => item.InjectionMass != 0 ? FiniteOrNull(solution.Model.Residual(item) / item.InjectionMass / 1000) : null).ToArray(),
                Included = injections.Select(item => item.Include).ToArray(),
                Loss = solution.Convergence == null ? (double?)null : FiniteOrNull(solution.Loss),
                Convergence = solution.Convergence?.Message,
            };

            var lower = new double?[injections.Length];
            var upper = new double?[injections.Length];
            if (solution.BootstrapSolutions != null && solution.BootstrapSolutions.Count > 0)
            {
                for (var i = 0; i < injections.Length; i++)
                {
                    var confidence = solution.Model.EvaluateBootstrap(injections[i].ID, true).DistributionConfidence95;
                    if (confidence != null && confidence.Length == 2)
                    {
                        lower[i] = FiniteOrNull(confidence[0] / 1000);
                        upper[i] = FiniteOrNull(confidence[1] / 1000);
                    }
                }
            }
            fit.ConfidenceLowerKilojoulesPerMole = lower;
            fit.ConfidenceUpperKilojoulesPerMole = upper;

            var reportedParameters = solution.ReportParameters;
            foreach (var parameter in solution.Parameters)
            {
                var displayValue = reportedParameters.TryGetValue(parameter.Key, out var reportedValue)
                    ? reportedValue
                    : parameter.Value;
                var modelParameter = solution.Model.Parameters.Table.TryGetValue(parameter.Key, out var savedParameter)
                    ? savedParameter
                    : null;
                fit.Parameters.Add(BuildParameter(
                    parameter.Key,
                    displayValue,
                    isDerived: false,
                    isLocked: modelParameter?.IsLocked == true,
                    isGloballyDetermined: modelParameter?.IsGloballyDetermined == true));
            }

            foreach (var parameter in reportedParameters.Where(parameter => !solution.Parameters.ContainsKey(parameter.Key)))
                fit.Parameters.Add(BuildParameter(parameter.Key, parameter.Value, isDerived: true));

            return fit;
        }

        static FitParameterDto BuildParameter(
            ParameterType key,
            FloatWithError source,
            bool isDerived = false,
            bool isLocked = false,
            bool isGloballyDetermined = false)
        {
            var parent = key.GetProperties().ParentType;
            var scale = 1.0;
            var unit = string.Empty;
            if (ParameterTypeAttribute.IsEnergyUnitParameter(key))
            {
                scale = 1.0 / 1000.0;
                unit = parent == ParameterType.HeatCapacity1 ? "kJ/(mol·K)" : "kJ/mol";
            }
            else if (parent == ParameterType.Affinity1)
            {
                scale = 1e6;
                unit = "µM";
            }

            return new FitParameterDto
            {
                Key = key.ToString(),
                Label = key.GetProperties().Name,
                IsDerived = isDerived,
                IsLocked = isLocked,
                IsGloballyDetermined = isGloballyDetermined,
                Value = source.Value * scale,
                Sd = source.SD * scale,
                ConfidenceLower = FiniteOrNull(source.Lower * scale),
                ConfidenceUpper = FiniteOrNull(source.Upper * scale),
                Unit = unit,
            };
        }

        static Tuple<string, string> Axis(ExperimentData experiment)
        {
            switch (experiment.AxisType)
            {
                case AnalysisXAxisType.TitrantConcentration: return Tuple.Create("Titrant concentration", "µM");
                case AnalysisXAxisType.ID: return Tuple.Create("Injection number", string.Empty);
                default: return Tuple.Create("Molar ratio", string.Empty);
            }
        }

        static double AnalysisX(ExperimentData experiment, InjectionData injection)
        {
            switch (experiment.AxisType)
            {
                case AnalysisXAxisType.TitrantConcentration: return injection.ActualTitrantConcentration * 1e6;
                case AnalysisXAxisType.ID: return injection.ID + 1;
                default: return injection.Ratio;
            }
        }

        static double? FiniteOrNull(double value) => IsFinite(value) ? value : (double?)null;
        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        sealed class FitSource
        {
            public FitSource(SolutionInterface solution, string name, string key, string resultKey)
            {
                Solution = solution;
                Name = name;
                Key = key;
                ResultKey = resultKey;
            }

            public SolutionInterface Solution { get; }
            public string Name { get; }
            public string Key { get; }
            public string ResultKey { get; }
        }
    }
}
