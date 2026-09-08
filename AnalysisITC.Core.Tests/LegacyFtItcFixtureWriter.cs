using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;
using AnalysisITC.Platform;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Analysis;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Processing;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Export;

namespace AnalysisITC.Core.Tests
{
    // Test-only serializer for exercising imports from the retired text format.
    internal sealed class LegacyFtItcFixtureWriter : FTITCFormat
    {
        internal static async Task WriteStream(
            Stream stream,
            IEnumerable<ExperimentData> experiments,
            IEnumerable<AnalysisResult> results = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true);
            await writer.WriteLineAsync(Variable(FTITCVersion, AppVersion.FullVersionString));

            foreach (var experiment in experiments ?? Enumerable.Empty<ExperimentData>())
                await WriteExperimentDataToFile(experiment, writer);
            foreach (var result in results ?? Enumerable.Empty<AnalysisResult>())
                await WriteAnalysisResultToFile(result, writer);

            await writer.FlushAsync();
        }

        static async Task WriteExperimentDataToFile(ExperimentData data, StreamWriter stream)
        {
            var file = new List<string>
            {
                FileHeader(data.IsTandemExperiment ? TandemExperimentHeader : ExperimentHeader, data.FileName),
                Variable(AssignedName, EncodeText(data.Name)),
                Variable(ID, data.UniqueID),
                Variable(Date, data.Date.ToString("O", CultureInfo.InvariantCulture)),
                Variable(DateSource, (int)data.DateSource),
                Variable(SourceFormat, (int)data.DataSourceFormat),
                Variable(Comments, EncodeText(data.Comments)),
                Variable(Include, data.Include),
                Variable(SyringeConcentration, data.SyringeConcentration),
                Variable(CellConcentration, data.CellConcentration),
                Variable(StirringSpeed, data.StirringSpeed),
                Variable(TargetTemperature, data.TargetTemperature),
                Variable(MeasuredTemperature, data.MeasuredTemperature),
                Variable(InitialDelay, data.InitialDelay),
                Variable(TargetPowerDiff, data.TargetPowerDiff),
                //Variable(UseIntegrationFactorLength, (int)data.IntegrationLengthMode),
                //Variable(IntegrationLengthFactor, data.IntegrationLengthFactor),
                Variable(FeedBackMode, (int)data.FeedBackMode),
                Variable(CellVolume, data.CellVolume),
                Variable(Instrument, (int)data.Instrument)
            };

            if (data.Attributes.Count > 0)
            {
                file.Add(ListHeader(ExperimentAttributes));
                foreach (var att in data.Attributes) file.Add(Attribute(att));
                file.Add(EndListHeader);
            }

            if (data.Solution != null)
            {
                file.Add(Variable(SolutionGUID, data.Solution.Guid));
                file.Add(ObjectHeader(ExperimentSolutionHeader));
                file.AddRange(GetSolutionLines(data.Solution));
                file.Add(EndObjectHeader);
            }

            file.Add(ListHeader(InjectionList));
            foreach (var inj in data.Injections)
            {
                file.Add(string.Join(",",
                    FormatInt(inj.ID),
                    inj.Include ? "1" : "0",
                    FormatFloat(inj.Time),
                    FormatDouble(inj.Volume),
                    FormatFloat(inj.Delay),
                    FormatFloat(inj.Duration),
                    FormatDouble(inj.Temperature),
                    FormatFloat(inj.IntegrationStartDelay),
                    FormatFloat(inj.IntegrationEndOffset),
                    FormatDouble(inj.ActualCellConcentration),
                    FormatDouble(inj.ActualTitrantConcentration),
                    FormatDouble(inj.RawPeakArea.Value),
                    FormatDouble(inj.RawPeakArea.SD)));
            }
            file.Add(EndListHeader);
            if (data.Segments != null)
            {
                file.Add(ListHeader(SegmentList));
                foreach (var seg in data.Segments)
                {
                    file.Add(string.Join(",",
                        FormatInt(seg.FirstInjectionID),
                        FormatDouble(seg.SegmentInitialActiveCellConc),
                        FormatDouble(seg.SegmentInitialActiveTitrantConc)));
                }
            }
            file.Add(EndListHeader);

            file.Add(ListHeader(DataPointList));
            foreach (var dp in data.DataPoints)
            {
                file.Add(string.Join(",",
                    FormatFloat(dp.Time),
                    FormatFloat(dp.Power),
                    FormatFloat(dp.Temperature)));
            }
            file.Add(EndListHeader);

            if (data.Processor != null)
            {
                file.Add(ObjectHeader(Processor));
                file.Add(Variable(ProcessorType, (int)data.Processor.BaselineType));

                switch (data.Processor.BaselineType)
                {
                    case BaselineInterpolatorTypes.Polynomial:
                        file.Add(Variable(PolynomiumDegree, (data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).Degree));
                        file.Add(Variable(PolynomiumLimit, (data.Processor.Interpolator as PolynomialLeastSquaresInterpolator).ZLimit));
                        break;
                    case BaselineInterpolatorTypes.Segmented:
                        file.Add(Variable(SegmentedBaselineDegree, (data.Processor.Interpolator as SegmentedBaselineInterpolator).Degree));
                        break;
                    case BaselineInterpolatorTypes.Spline:
                        var spinterpolator = (data.Processor.Interpolator as SplineInterpolator);
                        file.Add(Variable(SplineAlgorithm, (int)spinterpolator.Algorithm));
                        file.Add(Variable(SplineShowHandles, spinterpolator.ShowHandles));
                        file.Add(Variable(SplineAllowPointTimeDragging, spinterpolator.AllowPointTimeDragging));
                        file.Add(Variable(SplinePointDensity, (int)spinterpolator.PointDensity));
                        file.Add(Variable(SplineHandleMode, (int)spinterpolator.HandleMode));
                        file.Add(Variable(SplinePointsPerInjection, FormatInt(spinterpolator.PointsPerInjection)));
                        file.Add(Variable(SplineLocked, spinterpolator.IsLocked));
                        file.Add(ListHeader(SplinePointList));
                        foreach (var sp in spinterpolator.SplinePoints)
                        {
                            file.Add(string.Join(",",
                                FormatDouble(sp.Time),
                                FormatDouble(sp.Power),
                                FormatInt(sp.ID),
                                FormatDouble(sp.Slope),
                                FormatInt(sp.Locked ? 1 : 0),
                                FormatInt(sp.UserDefined ? 1 : 0),
                                FormatInt(sp.SlopeLocked ? 1 : 0),
                                FormatInt(sp.Linear ? 1 : 0)));
                        }
                        file.Add(EndListHeader);
                        break;
                    default:
                    case BaselineInterpolatorTypes.ASL:
                    case BaselineInterpolatorTypes.None:
                        break;
                }

                file.Add(EndObjectHeader);
            }

            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static List<string> GetSolutionLines(SolutionInterface solution)
        {
            if (solution.ModelType == AnalysisModel.SequentialBindingSites)
            {
                var count = SequentialPersistenceShape.RequireExplicitSiteCount(
                    solution.ModelOptions.Values, "Sequential FTITC solution");
                SequentialPersistenceShape.ValidateFittedParameters(
                    solution.Model.Parameters.Table.Values, count, "Sequential FTITC solution");
                SequentialPersistenceShape.ValidateReportedParameterKeys(
                    solution.Parameters.Keys, count, "Sequential FTITC solution");
            }

            var file = new List<string>();
            file.Add(FileHeader(SolutionHeader, solution.Guid));
            file.Add(Variable(DataRef, solution.Data.UniqueID));
            file.Add(Variable(SolModel, (int)solution.ModelType));
            file.Add(Variable(SolWeightedError, solution.UseWeightedFitting));
            if (solution.ParentSolution != null) file.Add(Variable(SolParent, solution.ParentSolution.UniqueID));
            if (solution.ErrorMethod != ErrorEstimationMethod.None) file.Add(Variable(SolErrorMethod, (int)solution.ErrorMethod));
            //AddConvergenceLine(solution.Convergence, file);
            AddConvergenceSnapshot(solution.Convergence, file);

            file.Add(ListHeader(SolParams));
            foreach (var par in solution.Model.Parameters.Table)
            {
                file.Add(ParameterLine(par.Key, par.Value));
            }
            file.Add(EndListHeader);

            file.Add(ListHeader(MdlOptions));
            foreach (var opt in solution.Model.ModelOptions.ToList())
            {
                file.Add(Attribute(opt.Value));
            }
            file.Add(EndListHeader);


            if (solution.BootstrapSolutions.Count > 0)
            {
                file.Add(ListHeader(SolBootstrapSnapshots));
                for (int index = 0; index < solution.BootstrapSolutions.Count; index++)
                {
                    var bootstrap = solution.BootstrapSolutions[index];
                    var replicateIndex = bootstrap.BootstrapReplicateIndex ?? index;
                    var snapshot = BootstrapModelSnapshot.Capture(bootstrap, replicateIndex);
                    file.AddRange(GetBootstrapSnapshotLines(snapshot));
                }
                file.Add(EndListHeader);
            }

            file.Add(EndFileHeader);

            return file;
        }

        private static List<string> GetBootstrapSnapshotLines(BootstrapModelSnapshot snapshot)
        {
            var lines = new List<string>
            {
                ObjectHeader(BootSnapshot),
                Variable(BootSnapshotVersion, snapshot.Version),
                Variable(BootReplicateIndex, snapshot.ReplicateIndex),
                Variable(BootCellConcentration, snapshot.CellConcentration),
                Variable(BootSyringeConcentration, snapshot.SyringeConcentration),
                Variable(BootCellVolume, snapshot.CellVolume),
                Variable(BootMeasuredTemperature, snapshot.MeasuredTemperature),
                ListHeader(BootSnapshotParameters),
            };

            foreach (var parameter in snapshot.Parameters)
                lines.Add(ParameterLine(parameter.Key, parameter));
            lines.Add(EndListHeader);

            lines.Add(ListHeader(BootSnapshotModelOptions));
            foreach (var option in snapshot.ModelOptions)
                lines.Add(Attribute(option));
            lines.Add(EndListHeader);

            lines.Add(ListHeader(BootSnapshotInjections));
            foreach (var injection in snapshot.Injections)
            {
                lines.Add(string.Join(",",
                    FormatInt(injection.ID),
                    injection.Include ? "1" : "0",
                    FormatDouble(injection.Volume),
                    FormatDouble(injection.ActualCellConcentration),
                    FormatDouble(injection.ActualTitrantConcentration)));
            }
            lines.Add(EndListHeader);

            lines.Add(ListHeader(BootSnapshotSegments));
            foreach (var segment in snapshot.Segments)
            {
                lines.Add(string.Join(",",
                    FormatInt(segment.FirstInjectionID),
                    FormatDouble(segment.InitialCellConcentration),
                    FormatDouble(segment.InitialTitrantConcentration)));
            }
            lines.Add(EndListHeader);
            lines.Add(EndObjectHeader);

            return lines;
        }

        private static void AddConvergenceLine(SolverConvergence convergence, List<string> file)
        {
            if (convergence == null) return;

            file.Add(ObjectHeader(SolConvergence));
            var conv = "";
            conv += Variable(SolIterations, convergence.Iterations) + ";";
            conv += Variable(SolConvMsg, EncodeText(convergence.Message)) + ";";
            conv += Variable(SolConvTime, convergence.Time.TotalSeconds) + ";";
            conv += Variable(SolConvBootstrapTime, convergence.ErrorEstimationTime.TotalSeconds) + ";";
            conv += Variable(SolLoss, convergence.Loss) + ";";
            conv += Variable(SolConvFailed, convergence.Failed) + ";";
            conv += Variable(SolConvAlgorithm, (int)convergence.Algorithm);
            file.Add(conv);
            file.Add(EndObjectHeader);
        }

        private static void AddConvergenceSnapshot(SolverConvergence convergence, List<string> file)
        {
            if (convergence == null) return;

            var snapshot = convergence.ToSnapshot();

            file.Add(ObjectHeader(SolConvergenceSnapshot));
            file.Add(Variable(SolConvSchemaVersion, snapshot.SchemaVersion));
            file.Add(Variable(SolIterations, snapshot.Iterations));
            file.Add(Variable(SolLoss, snapshot.Loss));
            file.Add(Variable(SolConvTime, snapshot.TimeSeconds));
            file.Add(Variable(SolConvBootstrapTime, snapshot.ErrorEstimationTimeSeconds));
            file.Add(Variable(SolConvAlgorithm, (int)snapshot.Algorithm));
            file.Add(Variable(SolConvTermination, (int)snapshot.Termination));
            file.Add(Variable(SolConvErrorOutcome, (int)snapshot.ErrorEstimationOutcome));
            file.Add(Variable(SolConvFailureReason, EncodeText(snapshot.FailureReason)));
            file.Add(Variable(SolConvErrorSummary, EncodeText(snapshot.ErrorEstimationSummary)));
            file.Add(Variable(SolConvErrorLimitTerminations, snapshot.ErrorEstimationLimitTerminations));
            file.Add(EndObjectHeader);
        }

        static async Task WriteAnalysisResultToFile(AnalysisResult result, StreamWriter stream)
        {
            var file = new List<string>()
            {
                FileHeader(AnalysisResultHeader, new[] { result.UniqueID, result.FileName }),
                Variable(Comments, EncodeText(result.Comments)),
                Variable(Date, result.Date.ToString("O", CultureInfo.InvariantCulture)),
                Variable(AssignedName, EncodeText(result.Name)),
            };
            if (result.ValiditySnapshot != null)
                file.Add(Variable(AnalysisResultValiditySnapshotData, EncodeText(result.ValiditySnapshot.ToJson())));

            file.AddRange(GetGlobalSolutionLines(result.Solution));
            file.Add(EndFileHeader);
            foreach (var line in file) await stream.WriteLineAsync(line);
        }

        static List<string> GetGlobalSolutionLines(GlobalSolution solution)
        {
            var constraints = solution.Model.Parameters.Constraints.ToList();
            if (solution.Model.ModelType == AnalysisModel.SequentialBindingSites)
            {
                var counts = solution.Model.Models.Select(model =>
                    SequentialPersistenceShape.RequireExplicitSiteCount(
                        model.ModelOptions.Values, "Sequential FTITC global member")).Distinct().ToList();
                if (counts.Count != 1)
                    throw new InvalidDataException(
                        "Sequential FTITC global members must declare the same site count.");
                constraints = SequentialPersistenceShape.ActiveConstraints(
                    counts[0], constraints);
                SequentialPersistenceShape.ValidateGlobalShape(
                    counts[0], constraints,
                    solution.Model.Parameters.GlobalTable.Keys, "Sequential FTITC global solution");
            }

            var file = new List<string>();
            file.Add(FileHeader(GlobalSolutionHeader, ""));
            file.Add(Variable(SolModel, (int)solution.Model.ModelType));
            file.Add(Variable(SolWeightedError, solution.UseWeightedFitting));

            //DataRefs
            file.Add(ListHeader(DataRef));
            foreach (var mdl in solution.Model.Models)
            {
                file.Add(mdl.Data.UniqueID);
            }
            file.Add(EndListHeader);

            //Parameter Constraints
            file.Add(ListHeader(SolConstraints));
            foreach (var par in constraints)
            {
                file.Add(Variable(par.Key.ToString() + ":" + ((int)par.Key).ToString(), (int)par.Value));
            }
            file.Add(EndListHeader);

            //Parameter Table
            file.Add(ListHeader(SolParams));
            foreach (var par in solution.Model.Parameters.GlobalTable)
            {
                file.Add(ParameterLine(par.Key, par.Value));
            }
            file.Add(EndListHeader);

            //ModelCloneOptions
            file.Add(ObjectHeader(MdlCloneOptions));
            file.Add(Variable(SolErrorMethod, (int)solution.Model.ModelCloneOptions.ErrorEstimationMethod));
            file.Add(Variable(SolCloneIsGlobal, solution.Model.ModelCloneOptions.IsGlobalClone));
            file.Add(Variable(SolCloneConcentrationVariance, solution.Model.ModelCloneOptions.IncludeConcentrationErrorsInBootstrap));
            file.Add(Variable(SolCloneAutoVariance, solution.Model.ModelCloneOptions.EnableAutoConcentrationVariance));
            file.Add(Variable(SolCloneAutoVarianceValue, solution.Model.ModelCloneOptions.AutoConcentrationVariance));
            file.Add(Variable(SolCloneUnlockParameters, solution.Model.ModelCloneOptions.UnlockBootstrapParameters));
            file.Add(EndObjectHeader);

            //Convergence
            //AddConvergenceLine(solution.Convergence, file);
            AddConvergenceSnapshot(solution.Convergence, file);

            file.Add(ListHeader(SolutionList));
            foreach (var sol in solution.Solutions)
            {
                file.AddRange(GetSolutionLines(sol));
            }
            file.Add(EndListHeader);

            file.Add(EndFileHeader);

            return file;
        }

        static string ParameterLine(ParameterType key, Parameter parameter)
        {
            return Variable(key.ToString() + ":" + ((int)key).ToString(), parameter.Value)
                + ":" + FormatInt(parameter.IsLocked ? 1 : 0);
        }
    }
}
