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
using AnalysisITC.Core.Presentation;
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
            ValidateSignature(buffer, header, format);
            buffer.Position = 0;

            try
            {
                var parseWarnings = new List<string>();
                ITCDataContainer[] containers;
                string formatVersion = null;
                if (format == ViewerFileFormat.Ftxtc)
                {
                    var recovered = await FTXTCReader.ReadWithRecovery(buffer, FtxtcReadPolicy.RecoverUsableContent, interactive: false);
                    containers = recovered.Containers;
                    formatVersion = $"{recovered.SchemaMajor}.{recovered.SchemaMinor}";
                    parseWarnings.AddRange(recovered.Issues.Select(issue => issue.Message));
                }
                else if (format == ViewerFileFormat.Ftitc)
                    containers = await FTITCReader.ReadStream(buffer, interactive: false);
                else
                {
                    if (format == ViewerFileFormat.Nitc)
                        containers = new ITCDataContainer[]
                        {
                            NanoItcReader.ReadStream(buffer, safeName)
                        };
                    else if (format == ViewerFileFormat.Opj)
                        containers = new ITCDataContainer[]
                        {
                            OriginProjectReader.ReadStream(buffer, safeName)
                        };
                    else
                        containers = new ITCDataContainer[]
                        {
                            MicroCalITC200Reader.ReadStream(
                                buffer,
                                safeName,
                                interactive: false,
                                warning: message => parseWarnings.Add(message))
                        };
                }

                cancellationToken.ThrowIfCancellationRequested();
                var document = BuildDocument(containers, safeName, format, buffer.Length, header, formatVersion);
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
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is InvalidDataException || ex is NotSupportedException || ex is IndexOutOfRangeException || ex is ArgumentException || ex is InvalidOperationException || ex is NullReferenceException)
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
            var fallback = format == ViewerFileFormat.Ftxtc
                ? "uploaded.ftxtc"
                : format == ViewerFileFormat.Ftitc ? "uploaded.ftitc"
                    : format == ViewerFileFormat.Nitc ? "uploaded.nitc"
                    : format == ViewerFileFormat.Opj ? "uploaded.opj"
                    : "uploaded.itc";
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

        static void ValidateSignature(MemoryStream stream, string header, ViewerFileFormat format)
        {
            var valid = format == ViewerFileFormat.Ftxtc
                ? HasZipSignature(stream)
                : format == ViewerFileFormat.Ftitc
                    ? header.StartsWith("FTITCVersion:", StringComparison.Ordinal)
                        || header.StartsWith("FILE:Experiment:", StringComparison.Ordinal)
                        || header.StartsWith("FILE:TandemExperiment:", StringComparison.Ordinal)
                        // The first FTITC dialect used tagged sections rather than
                        // the later FILE/LIST grammar. FTITCReader still supports
                        // this format, so the web signature check must allow it too.
                        || header.StartsWith("<Experiment>", StringComparison.Ordinal)
                    : format == ViewerFileFormat.Nitc
                        ? HasGzipSignature(stream)
                        : format == ViewerFileFormat.Opj
                            ? StartsWithToken(header, "CPYA")
                            : header.StartsWith("$ITC", StringComparison.OrdinalIgnoreCase);

            if (!valid)
                throw new ViewerFileException("format_mismatch", "The file contents do not match the selected file extension.");
        }

        static bool HasZipSignature(MemoryStream stream) => HasSignature(stream,
            (first, second, third, fourth) => first == 0x50 && second == 0x4b
                && ((third == 0x03 && fourth == 0x04)
                    || (third == 0x05 && fourth == 0x06)
                    || (third == 0x07 && fourth == 0x08)));

        static bool HasGzipSignature(MemoryStream stream) => HasSignature(stream,
            (first, second, _, _) => first == 0x1f && second == 0x8b);

        static bool HasSignature(MemoryStream stream, Func<int, int, int, int, bool> predicate)
        {
            var position = stream.Position;
            try
            {
                stream.Position = 0;
                var first = stream.ReadByte();
                var second = stream.ReadByte();
                var third = stream.ReadByte();
                var fourth = stream.ReadByte();
                return predicate(first, second, third, fourth);
            }
            finally
            {
                stream.Position = position;
            }
        }

        static bool StartsWithToken(string value, string token) =>
            value.StartsWith(token, StringComparison.Ordinal)
            && (value.Length == token.Length || char.IsWhiteSpace(value[token.Length]));

        static ViewerDocument BuildDocument(
            IEnumerable<ITCDataContainer> containers,
            string displayName,
            ViewerFileFormat format,
            long size,
            string header,
            string formatVersion)
        {
            var all = containers?.Where(item => item != null).ToList() ?? new List<ITCDataContainer>();
            var experiments = all.OfType<ExperimentData>().ToList();
            var results = all.OfType<AnalysisResult>().ToList();
            var document = new ViewerDocument
            {
                DisplayName = displayName,
                Format = format == ViewerFileFormat.Ftxtc ? "ftxtc"
                    : format == ViewerFileFormat.Ftitc ? "ftitc"
                    : format == ViewerFileFormat.Nitc ? "nitc"
                    : format == ViewerFileFormat.Opj ? "opj"
                    : "itc",
                SizeBytes = size,
                FormatVersion = format == ViewerFileFormat.Ftxtc ? formatVersion
                    : format == ViewerFileFormat.Ftitc ? ParseVersion(header)
                    : null,
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
                SequentialSiteCount = SequentialSiteCount(solution?.Model),
                IsGlobal = solution?.Model?.Parameters?.Constraints?.Any(item => item.Value != VariableConstraint.None) == true,
                ExperimentCount = members.Count,
                Loss = solution?.Convergence == null ? (double?)null : FiniteOrNull(solution.Convergence.Loss),
                Solver = BuildSolver(solution),
                Validity = BuildValidity(result),
                TemperatureParameterEvaluation = BuildTemperatureParameterEvaluation(result),
            };
            viewer.AdvancedAnalyses = BuildAdvancedAnalyses(result, viewer.Warnings);

            foreach (var option in solution?.Model?.ModelOptions ?? new Dictionary<AttributeKey, ExperimentAttribute>())
            {
                viewer.ModelOptions.Add(new ViewerSettingDto
                {
                    Key = option.Key.ToString(),
                    Label = string.IsNullOrWhiteSpace(option.Value?.OptionName) ? option.Key.GetEnumDescription() : option.Value.OptionName,
                    Value = FormatModelOption(option.Key, option.Value),
                });
            }

            var constraints = (solution?.Model?.Parameters?.Constraints
                    ?? new Dictionary<ParameterType, VariableConstraint>())
                .Where(constraint => constraint.Value != VariableConstraint.None);
            if (solution?.Model?.ModelType == AnalysisModel.SequentialBindingSites)
            {
                constraints = constraints
                    .GroupBy(constraint => ThermodynamicParameterSlots.TryResolve(
                            constraint.Key,
                            out _,
                            out var family)
                        ? "thermodynamic:" + family
                        : "parameter:" + constraint.Key)
                    .Select(group => group.First());
            }

            foreach (var constraint in constraints)
            {
                var label = constraint.Key.GetProperties()?.Name ?? constraint.Key.ToString();
                if (solution?.Model?.ModelType == AnalysisModel.SequentialBindingSites
                    && ThermodynamicParameterSlots.TryResolve(
                        constraint.Key,
                        out _,
                        out var family))
                {
                    if (family == ThermodynamicParameterFamily.Affinity)
                        label = "Affinity";
                    else if (family == ThermodynamicParameterFamily.Enthalpy)
                        label = "Enthalpy";
                }
                viewer.Constraints.Add(new ViewerSettingDto
                {
                    Key = constraint.Key.ToString(),
                    Label = label,
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

            BuildCorrelationViews(viewer, result, resultKey, experimentKeys);

            return viewer;
        }

        static void BuildCorrelationViews(
            ViewerAnalysisResult viewer,
            AnalysisResult result,
            string resultKey,
            IReadOnlyDictionary<string, string> experimentKeys)
        {
            if (result == null) return;

            var members = result.Solution?.Solutions ?? new List<SolutionInterface>();
            if (members.Count <= 1)
            {
                var single = CreateCorrelationView(
                    result,
                    resultKey + ":correlation-single",
                    "Single experiment",
                    "single",
                    null,
                    null,
                    experimentKeys,
                    () => new BootstrapCorrelationAnalyzer().Analyze(result));
                viewer.CorrelationViews.Add(single);
                return;
            }

            var shared = CreateCorrelationView(
                result,
                resultKey + ":correlation-shared",
                "Shared parameters",
                "shared",
                null,
                null,
                experimentKeys,
                () => new BootstrapCorrelationAnalyzer().Analyze(result));
            viewer.CorrelationViews.Add(shared);

            for (var index = 0; index < members.Count; index++)
            {
                var member = members[index];
                experimentKeys.TryGetValue(member?.Data?.UniqueID ?? string.Empty, out var experimentKey);
                var experimentName = member?.Data?.Name ?? $"Experiment {index + 1}";
                var view = CreateCorrelationView(
                    result,
                    resultKey + ":correlation-member-" + (index + 1).ToString(CultureInfo.InvariantCulture),
                    "Shared + " + experimentName + " local parameters",
                    "member",
                    index,
                    experimentKey,
                    experimentKeys,
                    () => new BootstrapCorrelationAnalyzer().Analyze(result, index));
                viewer.CorrelationViews.Add(view);
            }
        }

        static ViewerCorrelationViewDto CreateCorrelationView(
            AnalysisResult result,
            string key,
            string label,
            string scope,
            int? memberIndex,
            string experimentKey,
            IReadOnlyDictionary<string, string> experimentKeys,
            Func<BootstrapCorrelationResult> calculate)
        {
            var view = new ViewerCorrelationViewDto
            {
                Key = key,
                Label = label,
                Scope = scope,
                MemberIndex = memberIndex,
                ExperimentKey = experimentKey,
                Method = "Residual bootstrap (Pearson)",
                AvailabilityStatus = BootstrapCorrelationAvailabilityStatus.NoBootstrapReplicates.ToString(),
                Reason = "Parameter correlation could not be calculated.",
            };

            try
            {
                var correlation = calculate();
                var availability = correlation?.Availability;
                if (availability == null)
                {
                    view.Warnings.Add("The saved correlation result was unavailable.");
                    return view;
                }

                view.AvailabilityStatus = availability.Status.ToString();
                view.IsAvailable = availability.IsAvailable;
                view.Reason = availability.Reason ?? string.Empty;
                view.UsedReplicateCount = availability.CompleteReplicateCount;
                view.RequiredReplicateCount = availability.RequiredReplicateCount;
                view.VaryingParameterCount = availability.VaryingParameterCount;
                view.OmittedParameterCount = correlation.OmittedParameterCount;
                view.IsRankLimited = correlation.IsRankLimited;

                foreach (var descriptor in correlation.Parameters ?? new List<BootstrapCorrelationParameterDescriptor>())
                {
                    experimentKeys.TryGetValue(descriptor.MemberId ?? string.Empty, out var descriptorExperimentKey);
                    view.Parameters.Add(new ViewerCorrelationParameterDto
                    {
                        Key = CorrelationParameterKey(descriptor),
                        Label = descriptor.Label,
                        Scope = descriptor.Scope.ToString().ToLowerInvariant(),
                        SlotIndex = descriptor.SlotIndex,
                        MemberIndex = descriptor.MemberIndex,
                        ExperimentKey = descriptorExperimentKey,
                        ExperimentName = descriptor.MemberName,
                        OriginallyLocked = descriptor.WasOriginallyLocked,
                        BootstrapUnlocked = descriptor.IncludedBecauseBootstrapUnlock,
                        IsDerivedGlobal = descriptor.IsDerivedGlobalCoordinate,
                    });
                }

                view.HasBootstrapUnlockedParameters = view.Parameters.Any(parameter => parameter.BootstrapUnlocked);
                if (view.HasBootstrapUnlockedParameters)
                    view.Warnings.Add("Some originally locked parameters were included because bootstrap parameters were unlocked.");
                if (view.IsRankLimited)
                    view.Warnings.Add("The number of complete bootstrap replicates limits covariance rank.");

                if (correlation.IsAvailable && correlation.CorrelationMatrix != null)
                    view.CorrelationMatrix = ToJagged(correlation.CorrelationMatrix);
            }
            catch (Exception)
            {
                // A broken saved bootstrap result must not prevent the rest of a project
                // (or other saved results) from being displayed.
                view.AvailabilityStatus = "ProjectionError";
                view.IsAvailable = false;
                view.Reason = "The saved parameter correlation could not be displayed.";
            }

            return view;
        }

        static string CorrelationParameterKey(BootstrapCorrelationParameterDescriptor descriptor)
        {
            var scope = descriptor.Scope.ToString().ToLowerInvariant();
            var member = descriptor.MemberIndex.HasValue
                ? ":member-" + (descriptor.MemberIndex.Value + 1).ToString(CultureInfo.InvariantCulture)
                : string.Empty;
            return scope + member + ":" + descriptor.ParameterType;
        }

        static double[][] ToJagged(double[,] matrix)
        {
            var rows = matrix.GetLength(0);
            var columns = matrix.GetLength(1);
            var result = new double[rows][];
            for (var row = 0; row < rows; row++)
            {
                result[row] = new double[columns];
                for (var column = 0; column < columns; column++)
                    result[row][column] = matrix[row, column];
            }
            return result;
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

            var keys = ThermodynamicParameterSlots.OrderedKeys(
                dependences.Keys,
                ThermodynamicParameterFamily.Enthalpy,
                ThermodynamicParameterFamily.EntropyContribution,
                ThermodynamicParameterFamily.Gibbs);
            foreach (var key in keys)
            {
                if (!dependences.TryGetValue(key, out var dependence)) continue;
                viewer.Dependences.Add(BuildTemperatureDependence(key, dependence));
            }

            return viewer.Dependences.Count == 0 ? null : viewer;
        }

        static ViewerAdvancedAnalysesDto BuildAdvancedAnalyses(AnalysisResult result, List<string> warnings)
        {
            if (result == null) return null;
            var viewer = new ViewerAdvancedAnalysesDto
            {
                SpolarRecordUnavailableReason = result.SpolarRecordAnalysisUnavailableReason,
                ElectrostaticsUnavailableReason = result.ElectrostaticsAnalysisUnavailableReason,
                ProtonationUnavailableReason = result.ProtonationAnalysisUnavailableReason,
            };

            try
            {
                if (result.SpolarRecordAnalysis?.Result != null)
                    viewer.SpolarRecord = BuildSpolarRecord(result);
            }
            catch (Exception ex) when (ex is ArithmeticException || ex is InvalidOperationException || ex is NullReferenceException)
            {
                warnings.Add("The saved Spolar Record analysis could not be displayed.");
            }

            try
            {
                if (result.ElectrostaticsAnalysis?.Calculated == true)
                    viewer.Electrostatics = BuildElectrostatics(result.ElectrostaticsAnalysis);
            }
            catch (Exception ex) when (ex is ArithmeticException || ex is InvalidOperationException || ex is NullReferenceException)
            {
                warnings.Add("The saved electrostatics analysis could not be displayed.");
            }

            try
            {
                if (result.ProtonationAnalysis?.Fit is LinearFitWithError)
                    viewer.Protonation = BuildProtonation(result.ProtonationAnalysis);
            }
            catch (Exception ex) when (ex is ArithmeticException || ex is InvalidOperationException || ex is NullReferenceException)
            {
                warnings.Add("The saved protonation analysis could not be displayed.");
            }

            return viewer;
        }

        static ViewerSpolarRecordDto BuildSpolarRecord(AnalysisResult result)
        {
            const double energyScale = 1.0 / 1000.0;
            var analysis = result.SpolarRecordAnalysis;
            var output = analysis.Result;
            var evaluationTemperature = output.ReferenceTemperature.Value;

            return new ViewerSpolarRecordDto
            {
                Metadata = BuildAdvancedMetadata(analysis),
                FoldedMode = (analysis.CompletedFoldedMode ?? analysis.FoldedMode) switch
                {
                    FTSRMethod.SRFoldedMode.ID => "ID interaction",
                    FTSRMethod.SRFoldedMode.Intermediate => "Intermediate",
                    _ => "Globular",
                },
                TemperatureMode = (analysis.CompletedTempMode ?? analysis.TempMode) switch
                {
                    FTSRMethod.SRTempMode.MeanTemperature => "Mean temperature",
                    FTSRMethod.SRTempMode.ReferenceTemperature => "Reference temperature",
                    _ => "Isoentropic point",
                },
                HydrationContributionKilojoulesPerMole = BuildValueWithError(
                    output.HydrationContribution(evaluationTemperature), energyScale),
                ConformationalContributionKilojoulesPerMole = BuildValueWithError(
                    output.ConformationalContribution(evaluationTemperature), energyScale),
                ResidueEstimate = BuildValueWithError(output.Rvalue, 1),
                ReferenceTemperatureCelsius = BuildValueWithError(output.ReferenceTemperature, 1),
                TemperatureDependencePlot = BuildTemperatureDependencePlot(result),
            };
        }

        static ViewerAdvancedPlotDto BuildTemperatureDependencePlot(AnalysisResult result)
        {
            const double energyScale = 1.0 / 1000.0;
            var plot = new ViewerAdvancedPlotDto
            {
                Key = "temperature-dependence",
                Title = "Temperature dependence",
                XAxisLabel = "Temperature (°C)",
                YAxisLabel = "Thermodynamic parameter (kJ/mol)",
            };
            var solution = result?.Solution;
            var dependences = solution?.TemperatureDependence;
            if (dependences == null || dependences.Count == 0) return plot;

            var temperatures = solution.Solutions
                .Select(member => member?.Data?.MeasuredTemperature ?? double.NaN)
                .Where(IsFinite)
                .ToList();
            var domain = PlotDomain(temperatures, solution.MeanTemperature);
            var xs = Sample(domain.min, domain.max, 81);
            var parameters = ThermodynamicParameterSlots.OrderedKeys(
                dependences.Keys,
                ThermodynamicParameterFamily.Enthalpy,
                ThermodynamicParameterFamily.EntropyContribution,
                ThermodynamicParameterFamily.Gibbs);

            foreach (var parameter in parameters)
            {
                if (!dependences.TryGetValue(parameter, out var fit)) continue;
                var values = solution.Solutions
                    .Where(member => member != null
                        && IsFinite(member.Temp)
                        && member.ReportParameters.TryGetValue(parameter, out var estimate)
                        && IsFinite(estimate.Value))
                    .Select(member => Tuple.Create(member.Temp, member.ReportParameters[parameter]))
                    .OrderBy(point => point.Item1)
                    .ToList();
                if (values.Count == 0) continue;

                var label = parameter.GetProperties()?.Name ?? parameter.ToString();
                var group = parameter.ToString();
                plot.Series.Add(new ViewerAdvancedPlotSeriesDto
                {
                    Label = label,
                    Kind = "points",
                    Group = group,
                    X = values.Select(point => point.Item1).ToArray(),
                    Y = values.Select(point => point.Item2.Value * energyScale).ToArray(),
                    Lower = values.Select(point => FiniteBound(point.Item2.Lower, point.Item2.Value) * energyScale).ToArray(),
                    Upper = values.Select(point => FiniteBound(point.Item2.Upper, point.Item2.Value) * energyScale).ToArray(),
                });

                var bootstrapFits = solution.BootstrapSolutions
                    .Where(bootstrap => bootstrap?.TemperatureDependence?.ContainsKey(parameter) == true)
                    .Select(bootstrap => bootstrap.TemperatureDependence[parameter])
                    .ToList();
                plot.Series.Add(BuildLinearSeries(label, xs, fit, energyScale,
                    bootstrapFits: bootstrapFits, group: group));
            }

            return plot;
        }

        static ViewerElectrostaticsDto BuildElectrostatics(ElectrostaticsAnalysis analysis)
        {
            var fit = analysis.IonicStrengthDependenceFit;
            var viewer = new ViewerElectrostaticsDto
            {
                Metadata = BuildAdvancedMetadata(analysis),
                CounterIonReleaseIterations = analysis.CounterIonReleaseIterations,
                Kd0Micromolar = fit == null ? null : BuildValueWithError(fit.Kd0, 1e6),
                SaltSensitivity = fit == null ? null : BuildValueWithError(fit.SaltSensitivity, 1),
                Curvature = fit == null ? null : BuildValueWithError(fit.Curvature, 1),
                UsesCurvature = fit?.UsesCurvature == true,
                CounterIonRelease = analysis.CounterIonReleaseFit == null ? null : BuildValueWithError(analysis.CounterIonReleaseFit.Slope, 1),
            };

            viewer.Plots.Add(BuildPointPlot(
                "affinity-salt", "Affinity versus salt", "Salt concentration (mM)", "Kd (µM)",
                analysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.AffinityVsSalt), 1, 1e6));

            var debyePoints = analysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.DebyeHuckel)
                .Where(point => point.Item1 >= 0 && point.Item2.Value > 0)
                .Select(point => Tuple.Create(Math.Sqrt(point.Item1), FWEMath.Log10(point.Item2))).ToList();
            var debye = BuildPointPlot("debye-huckel", "Debye–Hückel", "sqrt(Ionic strength / M)", "log10(Kd / M)", debyePoints, 1, 1);
            if (fit != null && debyePoints.Count > 0)
            {
                var domain = PlotDomain(debyePoints.Select(point => point.Item1), 0);
                var xs = Sample(domain.min, domain.max, 81);
                debye.Series.Add(BuildValueSeries("Saved fit", "line", xs, x => fit.Evaluate(x), 1));
            }
            viewer.Plots.Add(debye);

            var counterPoints = analysis.GetDataPoints(ElectrostaticsAnalysis.DissocFitMode.CounterIonRelease);
            var counter = BuildPointPlot("counter-ion-release", "Counter-ion release", "ln(Salt activity)", "ln(Kd / M)", counterPoints, 1, 1);
            if (analysis.CounterIonReleaseFit != null && counterPoints.Count > 0)
            {
                var domain = PlotDomain(counterPoints.Select(point => point.Item1), 0);
                var xs = Sample(domain.min, domain.max, 81);
                counter.Series.Add(BuildLinearSeries("Saved fit", xs, analysis.CounterIonReleaseFit, 1));
            }
            viewer.Plots.Add(counter);
            return viewer;
        }

        static ViewerProtonationDto BuildProtonation(ProtonationAnalysis analysis)
        {
            const double energyScale = 1.0 / 1000.0;
            var fit = analysis.Fit as LinearFitWithError;
            var plot = BuildPointPlot(
                "protonation", "Protonation dependence", "Buffer protonation enthalpy (kJ/mol)",
                "Observed enthalpy (kJ/mol)", analysis.DataPoints, energyScale, energyScale);
            if (fit != null && analysis.DataPoints.Count > 0)
            {
                var domain = PlotDomain(analysis.DataPoints.Select(point => point.Item1 * energyScale), 0);
                var xs = Sample(domain.min, domain.max, 81);
                plot.Series.Add(BuildLinearSeries("Saved fit", xs, fit, energyScale, energyScale));
            }

            return new ViewerProtonationDto
            {
                Metadata = BuildAdvancedMetadata(analysis),
                BindingEnthalpyKilojoulesPerMole = BuildValueWithError(analysis.BindingEnthalpy.FloatWithError, energyScale),
                ProtonationChange = BuildValueWithError(analysis.ProtonationChange, 1),
                Plot = plot,
            };
        }

        static ViewerAdvancedAnalysisMetadataDto BuildAdvancedMetadata(AdvancedAnalysis analysis) => new ViewerAdvancedAnalysisMetadataDto
        {
            CompletedAtUtc = analysis.CompletedAtUtc,
            CompletedIterations = analysis.CompletedIterations,
            ErrorEstimationMethod = analysis.CompletedErrorEstimationMethod?.Description(),
        };

        static ViewerAdvancedPlotDto BuildPointPlot(
            string key,
            string title,
            string xLabel,
            string yLabel,
            IEnumerable<Tuple<double, FloatWithError>> points,
            double xScale,
            double yScale)
        {
            var plot = new ViewerAdvancedPlotDto { Key = key, Title = title, XAxisLabel = xLabel, YAxisLabel = yLabel };
            var values = points
                .Where(point => point != null && IsFinite(point.Item1) && IsFinite(point.Item2.Value))
                .OrderBy(point => point.Item1)
                .ToList();
            plot.Series.Add(new ViewerAdvancedPlotSeriesDto
            {
                Label = "Saved observations",
                Kind = "points",
                X = values.Select(point => point.Item1 * xScale).ToArray(),
                Y = values.Select(point => point.Item2.Value * yScale).ToArray(),
                Lower = values.Select(point => FiniteBound(point.Item2.Lower, point.Item2.Value) * yScale).ToArray(),
                Upper = values.Select(point => FiniteBound(point.Item2.Upper, point.Item2.Value) * yScale).ToArray(),
            });
            return plot;
        }

        static ViewerAdvancedPlotSeriesDto BuildValueSeries(
            string label,
            string kind,
            IEnumerable<double> xs,
            Func<double, FloatWithError> evaluate,
            double scale)
        {
            var points = xs.Select(x => new { X = x, Value = evaluate(x) })
                .Where(point => IsFinite(point.Value.Value)).ToList();
            return new ViewerAdvancedPlotSeriesDto
            {
                Label = label,
                Kind = kind,
                X = points.Select(point => point.X).ToArray(),
                Y = points.Select(point => point.Value.Value * scale).ToArray(),
                Lower = points.Select(point => Math.Min(
                    FiniteBound(point.Value.Lower, point.Value.Value) * scale,
                    FiniteBound(point.Value.Upper, point.Value.Value) * scale)).ToArray(),
                Upper = points.Select(point => Math.Max(
                    FiniteBound(point.Value.Lower, point.Value.Value) * scale,
                    FiniteBound(point.Value.Upper, point.Value.Value) * scale)).ToArray(),
            };
        }

        static ViewerAdvancedPlotSeriesDto BuildLinearSeries(
            string label,
            IEnumerable<double> displayXs,
            LinearFitWithError fit,
            double yScale,
            double xScale = 1,
            IReadOnlyList<LinearFitWithError> bootstrapFits = null,
            string group = null)
        {
            var envelope = LinearFitEnvelopeBuilder.Build(
                fit,
                bootstrapFits,
                displayXs.Where(IsFinite).Select(displayX => displayX / xScale));
            var values = envelope.Select(point => new
            {
                X = point.X * xScale,
                Exact = point.Center * yScale,
                Lower = (point.HasBand ? point.Lower : point.Center) * yScale,
                Upper = (point.HasBand ? point.Upper : point.Center) * yScale,
            }).ToArray();
            return new ViewerAdvancedPlotSeriesDto
            {
                Label = label,
                Kind = "line",
                Group = group,
                X = values.Select(value => value.X).ToArray(),
                Y = values.Select(value => value.Exact).ToArray(),
                Lower = values.Select(value => value.Lower).ToArray(),
                Upper = values.Select(value => value.Upper).ToArray(),
            };
        }

        static double FiniteBound(double value, double fallback) => IsFinite(value) ? value : fallback;

        static (double min, double max) PlotDomain(IEnumerable<double> source, double fallback)
        {
            var values = source.Where(IsFinite).OrderBy(value => value).ToArray();
            if (values.Length == 0) return (fallback - 5, fallback + 5);
            var min = values.First();
            var max = values.Last();
            var span = max - min;
            if (Math.Abs(span) < 1e-12) span = Math.Max(1, Math.Abs(max) * 0.1);
            return (min - span * 0.08, max + span * 0.08);
        }

        static double[] Sample(double min, double max, int count) => Enumerable.Range(0, count)
            .Select(index => min + (max - min) * index / Math.Max(1, count - 1.0)).ToArray();

        static ViewerTemperatureDependenceDto BuildTemperatureDependence(ParameterType key, LinearFitWithError dependence)
        {
            const double energyScale = 1.0 / 1000.0;
            ThermodynamicParameterSlots.TryResolve(key, out var slot, out var family);
            return new ViewerTemperatureDependenceDto
            {
                Key = key.ToString(),
                Family = family.ToString(),
                SlotIndex = slot.Index,
                Label = key.GetProperties().Name,
                Unit = "kJ/mol",
                SlopeUnit = "kJ/(mol·K)",
                ReferenceTemperatureCelsius = dependence.ReferenceT,
                Intercept = BuildValueWithError(dependence.Intercept, energyScale),
                Slope = BuildValueWithError(dependence.Slope, energyScale),
            };
        }

        static int? SequentialSiteCount(GlobalModel model)
        {
            if (model?.ModelType != AnalysisModel.SequentialBindingSites) return null;
            if (model.ModelOptions != null
                && model.ModelOptions.TryGetValue(AttributeKey.SequentialSiteCount, out var option))
                return option.IntValue;
            return null;
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
                var concentrationUnit = ResolveConcentrationUnit(source.Value);
                scale = concentrationUnit.GetMod();
                unit = concentrationUnit.GetName();
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

        static ConcentrationUnit ResolveConcentrationUnit(double value)
        {
            return IsFinite(value) && value != 0
                ? ConcentrationUnitAttribute.GetMagnitudeUnitFromConcentration(Math.Abs(value))
                : ConcentrationUnit.µM;
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
