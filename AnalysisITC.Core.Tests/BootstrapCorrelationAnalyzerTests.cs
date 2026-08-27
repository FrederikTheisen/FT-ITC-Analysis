using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class BootstrapCorrelationAnalyzerTests
    {
        [Fact]
        public void UsesFittedCoordinatesIncludesUnlockedOriginallyLockedAndDropsFixedParameters()
        {
            var solution = CreateSingleSolution(35);

            var result = new BootstrapCorrelationAnalyzer().Analyze(solution);

            Assert.True(result.IsAvailable);
            Assert.Equal(35, result.CompleteReplicateCount);
            Assert.Equal(new[] { ParameterType.Nvalue1, ParameterType.Enthalpy1, ParameterType.Affinity1 },
                result.Parameters.Select(parameter => parameter.ParameterType));
            Assert.Contains(result.Parameters, parameter => parameter.ParameterType == ParameterType.Enthalpy1
                && parameter.WasOriginallyLocked && parameter.IncludedBecauseBootstrapUnlock);
            Assert.Contains(result.OmittedParameters, parameter => parameter.ParameterType == ParameterType.Offset);
            Assert.Equal(1, result.OmittedParameterCount);
            Assert.Equal(1.0, result.CorrelationMatrix[0, 2], 12);
            Assert.False(result.IsRankLimited);
        }

        [Fact]
        public void RequiresThirtyCompleteReplicates()
        {
            var result = new BootstrapCorrelationAnalyzer().Analyze(CreateSingleSolution(29));

            Assert.False(result.IsAvailable);
            Assert.Equal(BootstrapCorrelationAvailabilityStatus.TooFewCompleteReplicates, result.Availability.Status);
            Assert.Equal(29, result.Availability.CompleteReplicateCount);
        }

        [Fact]
        public void ExactlyThirtyReplicatesProducesNegativeCorrelationAndUnitDiagonal()
        {
            var result = new BootstrapCorrelationAnalyzer().Analyze(CreateSingleSolution(30, positiveAffinitySlope: false));

            Assert.True(result.IsAvailable);
            Assert.Equal(30, result.CompleteReplicateCount);
            Assert.Equal(1.0, result.CorrelationMatrix[0, 0], 12);
            Assert.Equal(-1.0, result.CorrelationMatrix[0, 2], 12);
            Assert.Equal(1.0, result.CorrelationMatrix[2, 2], 12);
        }

        [Fact]
        public void DropsReplicatesWithAnyNonFiniteCoordinateListwise()
        {
            var solution = CreateSingleSolution(31);
            var rows = solution.BootstrapSolutions.ToList();
            rows[0].Model.Parameters.Table[ParameterType.Affinity1].Update(double.NaN);
            SetSingleBootstrapRows(solution, rows);

            var result = new BootstrapCorrelationAnalyzer().Analyze(solution);

            Assert.True(result.IsAvailable);
            Assert.Equal(30, result.CompleteReplicateCount);
        }

        [Fact]
        public void SingleMemberAnalysisResultUsesItsLocalFittedCoordinates()
        {
            var member = CreateSingleSolution(30);
            var globalModel = new GlobalModel(new List<Model> { member.Model })
            {
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
            };
            globalModel.Parameters.AddIndivdualParameter(member.Model.Parameters);
            var global = new GlobalSolution(
                new GlobalSolver { Model = globalModel, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                CreateConvergence());
            globalModel.Solution = global;
            var wrappedMember = global.Solutions[0];
            wrappedMember.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            wrappedMember.SetBootstrapSolutions(member.BootstrapSolutions);

            var result = new BootstrapCorrelationAnalyzer().Analyze(new AnalysisResult(global));

            Assert.True(result.IsAvailable);
            Assert.Contains(result.Parameters, parameter => parameter.ParameterType == ParameterType.Affinity1);
        }

        [Fact]
        public void TwoSetsPreservesSlotsAndCrossSiteCorrelation()
        {
            var solution = CreateTwoSetSolution(30);
            var result = new BootstrapCorrelationAnalyzer().Analyze(solution);

            Assert.True(result.IsAvailable);
            Assert.Equal(
                new[] { ParameterType.Nvalue1, ParameterType.Enthalpy1, ParameterType.Affinity1,
                    ParameterType.Nvalue2, ParameterType.Enthalpy2, ParameterType.Affinity2 },
                result.Parameters.Select(parameter => parameter.ParameterType));
            Assert.Equal(new[] { 1, 1, 1, 2, 2, 2 }, result.Parameters.Select(parameter => parameter.SlotIndex));
            Assert.Equal(-1.0, result.CorrelationMatrix[0, 5], 12);
        }

        [Fact]
        public void FourStepSequentialUsesAllStepCoordinatesAndOmitsStoichiometry()
        {
            var result = new BootstrapCorrelationAnalyzer().Analyze(CreateSequentialSolution(4, 30));

            Assert.True(result.IsAvailable);
            Assert.DoesNotContain(result.Parameters, parameter =>
                parameter.ParameterType == ParameterType.Nvalue1 || parameter.ParameterType == ParameterType.Nvalue2);
            Assert.Contains(result.Parameters, parameter => parameter.ParameterType == ParameterType.Affinity4);
            Assert.Contains(result.Parameters, parameter => parameter.ParameterType == ParameterType.Enthalpy4);
            Assert.Equal(
                new[] { "log10 Ka1", "log10 Ka2", "log10 Ka3", "log10 Ka4" },
                result.Parameters.Where(parameter => parameter.ParameterType.GetProperties().ParentType == ParameterType.Affinity1)
                    .Select(parameter => parameter.Label));
            Assert.Equal(new[] { 1, 2, 3, 4 },
                result.Parameters.Where(parameter => parameter.ParameterType.GetProperties().ParentType == ParameterType.Affinity1)
                    .Select(parameter => parameter.SlotIndex));
        }

        [Fact]
        public void GlobalScopeUsesSharedOnlyOrSharedPlusSelectedLocal()
        {
            var primary = CreateGlobalSolution(
                new[] { 20.0, 30.0 },
                (model, index) =>
                {
                    model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1 + index * .01);
                    model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000 - index);
                    model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6 + index * .01);
                    model.Parameters.AddOrUpdateParameter(ParameterType.Offset, index);
                },
                global =>
                {
                    global.Parameters.SetConstraintForParameter(ParameterType.Nvalue1, VariableConstraint.SameForAll);
                    global.Parameters.AddorUpdateGlobalParameter(ParameterType.Nvalue1, 1);
                });

            var shared = new BootstrapCorrelationAnalyzer().Analyze(primary);
            var selected = new BootstrapCorrelationAnalyzer().Analyze(primary, 0);

            Assert.All(shared.Parameters, parameter => Assert.True(parameter.IsShared));
            Assert.Contains(shared.Parameters, parameter => parameter.ParameterType == ParameterType.Nvalue1);
            Assert.Equal(1, selected.Parameters.Count(parameter => parameter.IsShared));
            Assert.All(selected.Parameters.Where(parameter => parameter.IsMember),
                parameter => Assert.Equal(0, parameter.MemberIndex));
            Assert.DoesNotContain(selected.Parameters, parameter => parameter.MemberIndex == 1);
        }

        [Fact]
        public void ReconstructsMissingGlobalGibbsAndHeatCapacityFromMemberSnapshots()
        {
            var primary = CreateGlobalSolution(
                new[] { 20.0, 30.0 },
                (model, index) => AddThermodynamicParameters(model, index, -15000, -1000, -100),
                global =>
                {
                    global.Parameters.SetConstraintForParameter(ParameterType.Affinity1, VariableConstraint.TemperatureDependent);
                    global.Parameters.SetConstraintForParameter(ParameterType.Enthalpy1, VariableConstraint.TemperatureDependent);
                    global.Parameters.AddorUpdateGlobalParameter(ParameterType.Gibbs1, -15000);
                    global.Parameters.AddorUpdateGlobalParameter(ParameterType.HeatCapacity1, -100);
                    global.Parameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -1000);
                },
                bootstrapCount: 30,
                bootstrapBuilder: (model, index) =>
                {
                    var dG = -15000 + index * 5;
                    var dCp = -100 - index * .1;
                    AddThermodynamicParameters(model, index, dG, -1000 + index * 2, dCp);
                });

            var result = new BootstrapCorrelationAnalyzer().Analyze(primary);

            Assert.True(result.IsAvailable);
            Assert.Contains(result.Parameters, parameter => parameter.ParameterType == ParameterType.Gibbs1);
            Assert.Contains(result.Parameters, parameter => parameter.ParameterType == ParameterType.HeatCapacity1);
            var gibbs = result.Parameters.ToList().FindIndex(parameter => parameter.ParameterType == ParameterType.Gibbs1);
            var heatCapacity = result.Parameters.ToList().FindIndex(parameter => parameter.ParameterType == ParameterType.HeatCapacity1);
            Assert.Equal(1.0, result.CorrelationMatrix[gibbs, gibbs], 12);
            Assert.Equal(1.0, result.CorrelationMatrix[heatCapacity, heatCapacity], 12);
            Assert.Equal(-15000, result.Coordinates[0][gibbs], 8);
            Assert.Equal(-100, result.Coordinates[0][heatCapacity], 8);
        }

        static SolutionInterface CreateSingleSolution(int replicateCount, bool positiveAffinitySlope = true)
        {
            var data = new ExperimentData("correlation.itc");
            var model = CreateModel(data, 1, -1000, 6, 0, unlock: true, lockEnthalpy: true);
            var solution = SolutionInterface.FromModel(model, null);
            solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            model.Solution = solution;

            var replicates = new List<SolutionInterface>();
            for (var i = 0; i < replicateCount; i++)
            {
                var replicateModel = CreateModel(new ExperimentData("correlation-bootstrap.itc"),
                    1 + i * 0.01, -1000 + i, 6 + (positiveAffinitySlope ? 1 : -1) * i * 0.01, 0,
                    unlock: true, lockEnthalpy: false);
                var replicate = SolutionInterface.FromModel(replicateModel, null);
                replicate.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
                replicateModel.Solution = replicate;
                replicates.Add(replicate);
            }
            solution.SetBootstrapSolutions(replicates);
            return solution;
        }

        static SolutionInterface CreateTwoSetSolution(int replicateCount)
        {
            var primaryModel = CreateTwoSetModel(new ExperimentData("two-sites.itc"), 0);
            var primary = SolutionInterface.FromModel(primaryModel, null);
            primary.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            primaryModel.Solution = primary;
            var replicates = new List<SolutionInterface>();
            for (var i = 0; i < replicateCount; i++)
            {
                var model = CreateTwoSetModel(new ExperimentData("two-sites-bootstrap.itc"), i);
                var solution = SolutionInterface.FromModel(model, null);
                solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
                model.Solution = solution;
                replicates.Add(solution);
            }
            primary.SetBootstrapSolutions(replicates);
            return primary;
        }

        static SolutionInterface CreateSequentialSolution(int stepCount, int replicateCount)
        {
            SequentialBindingSites Create(int index)
            {
                var data = new ExperimentData("sequential-correlation-" + index + ".itc")
                {
                    MeasuredTemperature = 25,
                    TargetTemperature = 25,
                };
                var model = new SequentialBindingSites(data)
                {
                    ModelCloneOptions = new ModelCloneOptions
                    {
                        ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    },
                };
                model.InitializeParameters(data);
                model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = stepCount;
                model.ApplyModelOptions();
                foreach (var slot in ThermodynamicParameterSlots.Active(stepCount))
                {
                    model.Parameters.Table[slot.Affinity].Update(8 - slot.Index * .5 + index * .01);
                    model.Parameters.Table[slot.Enthalpy].Update(-1000 * slot.Index + index);
                }
                model.Parameters.Table[ParameterType.Offset].Update(index * .01);
                return model;
            }

            var primaryModel = Create(0);
            var primary = SolutionInterface.FromModel(primaryModel, null);
            primary.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
            primaryModel.Solution = primary;
            var replicates = Enumerable.Range(1, replicateCount).Select(index =>
            {
                var model = Create(index);
                var solution = SolutionInterface.FromModel(model, null);
                solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
                model.Solution = solution;
                return solution;
            }).ToList();
            primary.SetBootstrapSolutions(replicates);
            return primary;
        }

        static TwoSetsOfSites CreateTwoSetModel(ExperimentData data, int index)
        {
            var model = new TwoSetsOfSites(data)
            {
                ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
            };
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1 + index * .01);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000 + index);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6 + index * .01);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue2, .8 + index * .01);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy2, -2000 - index);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity2, 5 - index * .02);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            return model;
        }

        static GlobalSolution CreateGlobalSolution(
            double[] temperatures,
            Action<Model, int> primaryBuilder,
            Action<GlobalModel> globalBuilder,
            int bootstrapCount = 30,
            Action<Model, int> bootstrapBuilder = null)
        {
            var models = temperatures.Select((temperature, index) =>
            {
                var data = new ExperimentData("global-" + index + ".itc") { MeasuredTemperature = temperature };
                var model = new OneSetOfSites(data)
                {
                    ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                };
                primaryBuilder(model, index);
                return model;
            }).ToList();
            var global = new GlobalModel(models.Cast<Model>().ToList()) { ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals } };
            foreach (var model in models) global.Parameters.AddIndivdualParameter(model.Parameters);
            globalBuilder(global);
            var solver = new GlobalSolver { Model = global, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals };
            var primary = new GlobalSolution(solver, CreateConvergence());
            for (var replicateIndex = 0; replicateIndex < bootstrapCount; replicateIndex++)
            {
                var replicateModels = temperatures.Select((temperature, memberIndex) =>
                {
                    var data = new ExperimentData("global-bootstrap-" + replicateIndex + "-" + memberIndex + ".itc")
                    {
                        MeasuredTemperature = temperature,
                    };
                    var model = new OneSetOfSites(data)
                    {
                        ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                    };
                    (bootstrapBuilder ?? primaryBuilder)(model, replicateIndex);
                    return model;
                }).ToList();
                var replicateGlobal = new GlobalModel(replicateModels.Cast<Model>().ToList())
                {
                    // Deliberately leave GlobalTable empty: this is the persisted
                    // global snapshot shape that the analyzer must reconstruct.
                    ModelCloneOptions = new ModelCloneOptions { ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                };
                var replicateSolutions = replicateModels.Select(model =>
                {
                    var solution = SolutionInterface.FromModel(model, null);
                    solution.ErrorMethod = ErrorEstimationMethod.BootstrapResiduals;
                    model.Solution = solution;
                    return solution;
                }).ToList();
                var replicate = new GlobalSolution(
                    new GlobalSolver { Model = replicateGlobal, ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals },
                    replicateSolutions,
                    null);
                SetGlobalBootstrapRows(primary, replicateIndex, replicate);
            }
            return primary;
        }

        static void SetGlobalBootstrapRows(GlobalSolution primary, int index, GlobalSolution row)
        {
            var property = typeof(GlobalSolution).GetProperty(nameof(GlobalSolution.BootstrapSolutions), BindingFlags.Instance | BindingFlags.Public);
            var rows = (List<GlobalSolution>)property.GetValue(primary);
            if (index == 0) rows.Clear();
            rows.Add(row);
        }

        static void SetSingleBootstrapRows(SolutionInterface primary, List<SolutionInterface> rows)
        {
            var property = typeof(SolutionInterface).GetProperty(nameof(SolutionInterface.BootstrapSolutions), BindingFlags.Instance | BindingFlags.Public);
            property.SetValue(primary, rows);
        }

        static SolverConvergence CreateConvergence()
        {
            return (SolverConvergence)typeof(SolverConvergence)
                .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                .Invoke(null);
        }

        static void AddThermodynamicParameters(Model model, int index, double dG, double referenceEnthalpy, double dCp)
        {
            var temperature = model.Data.MeasuredTemperatureKelvin;
            var referenceTemperature = 298.15;
            var enthalpy = referenceEnthalpy + (temperature - referenceTemperature) * dCp;
            var logKa = dG / (-AnalysisITC.Core.Units.Energy.R * temperature * Math.Log(10));
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1 + index * .005);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, logKa);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, index * .01);
        }

        static OneSetOfSites CreateModel(
            ExperimentData data,
            double n,
            double enthalpy,
            double affinity,
            double offset,
            bool unlock,
            bool lockEnthalpy)
        {
            var model = new OneSetOfSites(data)
            {
                ModelCloneOptions = new ModelCloneOptions
                {
                    ErrorEstimationMethod = ErrorEstimationMethod.BootstrapResiduals,
                    UnlockBootstrapParameters = unlock,
                },
            };
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, n);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, enthalpy, lockEnthalpy);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, affinity);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, offset);
            return model;
        }
    }
}
