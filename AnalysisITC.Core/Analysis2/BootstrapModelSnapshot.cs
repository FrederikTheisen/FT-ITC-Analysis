using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis
{
    /// <summary>
    /// Evaluation state captured from one completed bootstrap fit.  This type is
    /// deliberately independent of the FTITC text representation.
    /// </summary>
    internal sealed class BootstrapModelSnapshot
    {
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public int ReplicateIndex { get; set; }
        public FloatWithError CellConcentration { get; set; }
        public FloatWithError SyringeConcentration { get; set; }
        public double CellVolume { get; set; }
        public double MeasuredTemperature { get; set; }
        public List<Parameter> Parameters { get; } = new List<Parameter>();
        public List<ExperimentAttribute> ModelOptions { get; } = new List<ExperimentAttribute>();
        public List<BootstrapInjectionSnapshot> Injections { get; } = new List<BootstrapInjectionSnapshot>();
        public List<BootstrapSegmentSnapshot> Segments { get; } = new List<BootstrapSegmentSnapshot>();

        public static BootstrapModelSnapshot Capture(SolutionInterface solution, int replicateIndex)
        {
            if (solution?.Model == null)
                throw new ArgumentNullException(nameof(solution));

            var model = solution.Model;
            var data = model.Data;
            var snapshot = new BootstrapModelSnapshot
            {
                ReplicateIndex = replicateIndex,
                CellConcentration = data.CellConcentration,
                SyringeConcentration = data.SyringeConcentration,
                CellVolume = data.CellVolume,
                MeasuredTemperature = data.MeasuredTemperature,
            };

            snapshot.Parameters.AddRange(model.Parameters.Table.Values.Select(parameter => parameter.Copy()));
            snapshot.ModelOptions.AddRange(model.ModelOptions.Values.Select(option => option.Copy()));
            snapshot.Injections.AddRange(data.Injections.Select(BootstrapInjectionSnapshot.Capture));
            snapshot.Segments.AddRange(data.Segments?.Select(BootstrapSegmentSnapshot.Capture)
                ?? Enumerable.Empty<BootstrapSegmentSnapshot>());

            return snapshot;
        }

        public SolutionInterface Restore(Model primaryModel)
        {
            if (primaryModel == null)
                throw new ArgumentNullException(nameof(primaryModel));
            if (Version != CurrentVersion)
                throw new InvalidOperationException($"Unsupported bootstrap snapshot version {Version}.");
            if (Injections.Count == 0)
                throw new InvalidOperationException("A bootstrap snapshot must contain at least one injection.");

            var data = new ExperimentData(primaryModel.Data.FileName)
            {
                CellConcentration = CellConcentration,
                SyringeConcentration = SyringeConcentration,
                CellVolume = CellVolume,
                MeasuredTemperature = MeasuredTemperature,
            };
            data.SetID(primaryModel.Data.UniqueID);

            foreach (var injectionSnapshot in Injections)
            {
                var injection = injectionSnapshot.Restore(data);
                data.Injections.Add(injection);
            }

            data.ReplaceSegments(Segments.Select(segment => segment.Restore()));

            // Initialize the concrete model normally so model-specific parameter and
            // option tables exist.  Captured options are installed afterwards rather
            // than passed to SetModelOptions: that method intentionally replaces
            // "from experiment" values, which would destroy the sampled option value.
            var factory = new SingleModelFactory(primaryModel.ModelType);
            factory.InitializeModel(data);
            var model = factory.Model;
            model.ModelCloneOptions = CopyCloneOptions(primaryModel.ModelCloneOptions);
            model.ReuseAttachedSolutionInitialValues = primaryModel.ReuseAttachedSolutionInitialValues;

            foreach (var option in ModelOptions)
                model.ModelOptions[option.Key] = option.Copy();

            foreach (var parameter in Parameters)
                model.Parameters.AddOrUpdateParameter(parameter.Copy());

            // Match the last stage of normal model evaluation after fitted values have
            // been installed.  Model-specific option effects are expected to be
            // idempotent here.
            model.ApplyModelOptions();

            var solution = SolutionInterface.FromModel(model, null);
            solution.BootstrapReplicateIndex = ReplicateIndex;
            model.Solution = solution;
            return solution;
        }

        static ModelCloneOptions CopyCloneOptions(ModelCloneOptions source)
        {
            if (source == null) return null;

            return new ModelCloneOptions
            {
                IsGlobalClone = source.IsGlobalClone,
                ErrorEstimationMethod = source.ErrorEstimationMethod,
                IncludeConcentrationErrorsInBootstrap = source.IncludeConcentrationErrorsInBootstrap,
                EnableAutoConcentrationVariance = source.EnableAutoConcentrationVariance,
                AutoConcentrationVariance = source.AutoConcentrationVariance,
                DiscardedDataPoint = source.DiscardedDataPoint,
                UnlockBootstrapParameters = source.UnlockBootstrapParameters,
            };
        }
    }

    internal sealed class BootstrapInjectionSnapshot
    {
        public int ID { get; set; }
        public bool Include { get; set; }
        public double Volume { get; set; }
        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }

        public static BootstrapInjectionSnapshot Capture(InjectionData injection) => new BootstrapInjectionSnapshot
        {
            ID = injection.ID,
            Include = injection.Include,
            Volume = injection.Volume,
            ActualCellConcentration = injection.ActualCellConcentration,
            ActualTitrantConcentration = injection.ActualTitrantConcentration,
        };

        public InjectionData Restore(ExperimentData data)
        {
            var injection = new InjectionData(data, ID, Volume, SyringeMass(data), Include)
            {
                ActualCellConcentration = ActualCellConcentration,
                ActualTitrantConcentration = ActualTitrantConcentration,
                Ratio = ActualCellConcentration == 0 ? 0 : ActualTitrantConcentration / ActualCellConcentration,
            };
            injection.SetPeakArea(new FloatWithError(0));
            return injection;
        }

        double SyringeMass(ExperimentData data) => data.SyringeConcentration * Volume;
    }

    internal sealed class BootstrapSegmentSnapshot
    {
        public int FirstInjectionID { get; set; }
        public double InitialCellConcentration { get; set; }
        public double InitialTitrantConcentration { get; set; }

        public static BootstrapSegmentSnapshot Capture(TandemExperimentSegment segment) => new BootstrapSegmentSnapshot
        {
            FirstInjectionID = segment.FirstInjectionID,
            InitialCellConcentration = segment.SegmentInitialActiveCellConc,
            InitialTitrantConcentration = segment.SegmentInitialActiveTitrantConc,
        };

        public TandemExperimentSegment Restore() => new TandemExperimentSegment(
            FirstInjectionID,
            InitialCellConcentration,
            InitialTitrantConcentration);
    }
}
