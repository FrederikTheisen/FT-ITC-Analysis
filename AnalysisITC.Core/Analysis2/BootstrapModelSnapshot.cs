using System;
using System.Collections.Generic;
using System.IO;
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
        public bool ParameterBoundaryHit { get; set; }
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
            if (model.ModelType == AnalysisModel.SequentialBindingSites)
            {
                var count = SequentialPersistenceShape.RequireExplicitSiteCount(
                    model.ModelOptions.Values, "Sequential bootstrap source");
                SequentialPersistenceShape.ValidateFittedParameters(
                    model.Parameters.Table.Values, count, "Sequential bootstrap source");
            }
            var snapshot = new BootstrapModelSnapshot
            {
                ReplicateIndex = replicateIndex,
                ParameterBoundaryHit = solution.ParameterBoundaryHit,
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
            if (model.ModelType != primaryModel.ModelType)
                throw new InvalidDataException(
                    $"Bootstrap restoration constructed '{model.ModelType}' instead of '{primaryModel.ModelType}'.");
            model.ModelCloneOptions = CopyCloneOptions(primaryModel.ModelCloneOptions);
            model.ReuseAttachedSolutionInitialValues = primaryModel.ReuseAttachedSolutionInitialValues;

            if (ModelOptions.GroupBy(option => option.Key).Any(group => group.Count() != 1))
                throw new InvalidDataException("Bootstrap snapshot contains duplicate model options.");
            foreach (var option in ModelOptions)
                model.ModelOptions[option.Key] = option.Copy();

            // Structural options define the dynamic table and must take effect before
            // persisted fitted values are installed.
            model.ApplyModelOptions();

            if (model.ModelType == AnalysisModel.SequentialBindingSites)
            {
                var primaryCount = SequentialPersistenceShape.RequireExplicitSiteCount(
                    primaryModel.ModelOptions.Values, "Primary sequential solution");
                var snapshotCount = SequentialPersistenceShape.RequireExplicitSiteCount(
                    ModelOptions, "Sequential bootstrap snapshot");
                if (snapshotCount != primaryCount)
                    throw new InvalidDataException(
                        $"Sequential bootstrap snapshot declares {snapshotCount} steps; the primary solution declares {primaryCount}.");
                SequentialPersistenceShape.ValidateFittedParameters(
                    Parameters, snapshotCount, "Sequential bootstrap snapshot");
            }

            foreach (var parameter in Parameters)
                model.Parameters.AddOrUpdateParameter(parameter.Copy());

            var solution = SolutionInterface.FromModel(model, null);
            solution.RestoreParameterBoundaryHit(ParameterBoundaryHit);
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

    /// <summary>
    /// Strict shape rules shared by the two persistence formats and bootstrap
    /// snapshots. Sequential payloads are never coerced to another model or inferred
    /// from whatever parameter columns happen to be present.
    /// </summary>
    internal static class SequentialPersistenceShape
    {
        internal static int RequireExplicitSiteCount(
            IEnumerable<ExperimentAttribute> options,
            string context)
        {
            var matches = (options ?? Enumerable.Empty<ExperimentAttribute>())
                .Where(option => option != null && option.Key == AttributeKey.SequentialSiteCount)
                .ToList();
            if (matches.Count != 1)
                throw new InvalidDataException(
                    $"{context} must contain exactly one explicit sequential site count.");

            var count = matches[0].IntValue;
            try
            {
                ThermodynamicParameterSlots.ValidateSequentialCount(count);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidDataException(
                    $"{context} has invalid sequential site count {count}; expected an integer from 2 to 4.", ex);
            }
            return count;
        }

        internal static void ValidateFittedParameters(
            IEnumerable<Parameter> parameters,
            int count,
            string context) => ValidateFittedParameterKeys(
                (parameters ?? Enumerable.Empty<Parameter>()).Select(parameter => parameter.Key),
                count,
                context);

        internal static void ValidateFittedParameterKeys(
            IEnumerable<ParameterType> keys,
            int count,
            string context)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(count);
            var actual = (keys ?? Enumerable.Empty<ParameterType>()).ToList();
            EnsureUnique(actual, context, "fitted parameter");
            var expected = ExpectedFittedKeys(count);
            if (!new HashSet<ParameterType>(actual).SetEquals(expected))
                throw new InvalidDataException(
                    $"{context} has an invalid sequential fitted-parameter shape. "
                    + $"Expected {Describe(expected)}; found {Describe(actual)}.");
        }

        internal static void ValidateReportedParameterKeys(
            IEnumerable<ParameterType> keys,
            int count,
            string context)
        {
            // FTXTC's reportedParameters collection stores uncertainty-bearing
            // values for the fitted coordinates. Kd/G/H/-TDS values remain derived
            // by the concrete solution and therefore share the same active shape.
            var actual = (keys ?? Enumerable.Empty<ParameterType>()).ToList();
            EnsureUnique(actual, context, "reported parameter");
            var expected = ExpectedFittedKeys(count);
            if (!new HashSet<ParameterType>(actual).SetEquals(expected))
                throw new InvalidDataException(
                    $"{context} has an invalid sequential reported-parameter shape. "
                    + $"Expected {Describe(expected)}; found {Describe(actual)}.");
        }

        internal static void ValidateGlobalShape(
            int count,
            IEnumerable<KeyValuePair<ParameterType, VariableConstraint>> constraints,
            IEnumerable<ParameterType> globalKeys,
            string context)
        {
            ThermodynamicParameterSlots.ValidateSequentialCount(count);
            var constraintItems = (constraints
                ?? Enumerable.Empty<KeyValuePair<ParameterType, VariableConstraint>>()).ToList();
            EnsureUnique(constraintItems.Select(item => item.Key).ToList(), context, "constraint");
            var constraintMap = constraintItems.ToDictionary(item => item.Key, item => item.Value);
            var fitted = ExpectedFittedKeys(count);
            if (constraintMap.Keys.Any(key => !fitted.Contains(key)))
                throw new InvalidDataException(
                    $"{context} contains a sequential constraint for an inactive or unsupported parameter.");

            var slots = ThermodynamicParameterSlots.Active(count).ToList();
            var affinityStyles = slots
                .Select(slot => ConstraintOrNone(constraintMap, slot.Affinity)).Distinct().ToList();
            var enthalpyStyles = slots
                .Select(slot => ConstraintOrNone(constraintMap, slot.Enthalpy)).Distinct().ToList();
            if (affinityStyles.Count != 1)
                throw new InvalidDataException(
                    $"{context} has inconsistent active affinity-family constraints.");
            if (enthalpyStyles.Count != 1)
                throw new InvalidDataException(
                    $"{context} has inconsistent active enthalpy-family constraints.");

            var expectedGlobals = new HashSet<ParameterType>();
            foreach (var slot in slots)
            {
                AddCoordinateKeys(expectedGlobals, slot.Affinity, affinityStyles[0]);
                AddCoordinateKeys(expectedGlobals, slot.Enthalpy, enthalpyStyles[0]);
            }
            AddCoordinateKeys(expectedGlobals, ParameterType.Offset,
                ConstraintOrNone(constraintMap, ParameterType.Offset));

            var actualGlobals = (globalKeys ?? Enumerable.Empty<ParameterType>()).ToList();
            EnsureUnique(actualGlobals, context, "global parameter");
            if (!new HashSet<ParameterType>(actualGlobals).SetEquals(expectedGlobals))
                throw new InvalidDataException(
                    $"{context} has an invalid sequential global-coordinate shape. "
                    + $"Expected {Describe(expectedGlobals)}; found {Describe(actualGlobals)}.");
        }

        static HashSet<ParameterType> ExpectedFittedKeys(int count)
        {
            var expected = new HashSet<ParameterType> { ParameterType.Offset };
            foreach (var slot in ThermodynamicParameterSlots.Active(count))
            {
                expected.Add(slot.Affinity);
                expected.Add(slot.Enthalpy);
            }
            return expected;
        }

        static VariableConstraint ConstraintOrNone(
            IReadOnlyDictionary<ParameterType, VariableConstraint> constraints,
            ParameterType key) => constraints.TryGetValue(key, out var value)
                ? value
                : VariableConstraint.None;

        static void AddCoordinateKeys(
            ISet<ParameterType> target,
            ParameterType member,
            VariableConstraint constraint)
        {
            if (member == ParameterType.Offset)
            {
                if (constraint == VariableConstraint.SameForAll) target.Add(member);
                else if (constraint != VariableConstraint.None)
                    throw new InvalidDataException(
                        "Sequential offset constraints may be only None or SameForAll.");
                return;
            }

            if (constraint != VariableConstraint.None
                && constraint != VariableConstraint.SameForAll
                && constraint != VariableConstraint.TemperatureDependent)
                throw new InvalidDataException(
                    $"Unsupported sequential constraint '{constraint}' for '{member}'.");
            foreach (var key in GlobalConstraintSemantics.CoordinateKeys(member, constraint))
                target.Add(key);
        }

        static void EnsureUnique<T>(IReadOnlyCollection<T> values, string context, string kind)
        {
            if (values.Distinct().Count() != values.Count)
                throw new InvalidDataException($"{context} contains duplicate {kind} entries.");
        }

        static string Describe(IEnumerable<ParameterType> keys) =>
            string.Join(", ", keys.OrderBy(key => (int)key));
    }

    internal sealed class BootstrapInjectionSnapshot
    {
        public int ID { get; set; }
        public bool Include { get; set; }
        public double Volume { get; set; }
        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }
        public double Ratio { get; set; } = double.NaN;

        public static BootstrapInjectionSnapshot Capture(InjectionData injection) => new BootstrapInjectionSnapshot
        {
            ID = injection.ID,
            Include = injection.Include,
            Volume = injection.Volume,
            ActualCellConcentration = injection.ActualCellConcentration,
            ActualTitrantConcentration = injection.ActualTitrantConcentration,
            Ratio = injection.Ratio,
        };

        public InjectionData Restore(ExperimentData data)
        {
            var injection = new InjectionData(data, ID, Volume, SyringeMass(data), Include)
            {
                ActualCellConcentration = ActualCellConcentration,
                ActualTitrantConcentration = ActualTitrantConcentration,
                Ratio = double.IsNaN(Ratio)
                    ? (ActualCellConcentration == 0 ? 0 : ActualTitrantConcentration / ActualCellConcentration)
                    : Ratio,
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
