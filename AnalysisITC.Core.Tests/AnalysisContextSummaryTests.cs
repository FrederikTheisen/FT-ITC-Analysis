using System.Collections.Generic;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public class AnalysisContextSummaryTests
    {
        [Fact]
        public void SingleContextCountsUnlockedVariablesAndIncludedInjections()
        {
            var experiment = CreateExperiment(true, false, true);
            var model = CreateModel(experiment, lockOffset: true);
            var context = new AnalysisContext(AnalysisModel.OneSetOfSites, model);

            Assert.Equal(3, context.FittingVariableCount);
            Assert.Equal(2, context.FittingPointCount);
        }

        [Fact]
        public void GlobalContextCountsSharedAndUnconstrainedVariables()
        {
            var first = CreateModel(
                CreateExperiment(true, false, true),
                lockOffset: false);
            var second = CreateModel(
                CreateExperiment(true, false),
                lockOffset: true);
            var globalModel = new GlobalModel();
            globalModel.AddModel(first);
            globalModel.AddModel(second);

            var globalParameters = new GlobalModelParameters();
            globalParameters.SetConstraintForParameter(
                ParameterType.Nvalue1,
                VariableConstraint.SameForAll);
            globalParameters.SetConstraintForParameter(
                ParameterType.Enthalpy1,
                VariableConstraint.TemperatureDependent);
            globalParameters.AddorUpdateGlobalParameter(ParameterType.Nvalue1, 1);
            globalParameters.AddorUpdateGlobalParameter(ParameterType.Enthalpy1, -1000);
            globalParameters.AddorUpdateGlobalParameter(
                ParameterType.HeatCapacity1,
                -100,
                islocked: true);

            var context = new AnalysisContext(
                AnalysisModel.OneSetOfSites,
                globalModel,
                globalParameters,
                new Dictionary<ParameterType, IReadOnlyList<VariableConstraint>>());

            Assert.Equal(5, context.FittingVariableCount);
            Assert.Equal(3, context.FittingPointCount);
        }

        [Fact]
        public void SingleContextSummaryFormatsSingularCounts()
        {
            var model = CreateModel(
                CreateExperiment(true),
                lockOffset: false);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Enthalpy1,
                model.Parameters.Table[ParameterType.Enthalpy1].Value,
                true);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Affinity1,
                model.Parameters.Table[ParameterType.Affinity1].Value,
                true);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Offset,
                model.Parameters.Table[ParameterType.Offset].Value,
                true);
            var context = new AnalysisContext(AnalysisModel.OneSetOfSites, model);

            Assert.Equal(
                "1 variable • 1 data point",
                AnalysisContextSummaryPresentation.BuildText(context));
        }

        [Fact]
        public void GlobalContextSummaryIncludesScope()
        {
            var first = CreateModel(CreateExperiment(true, true), lockOffset: false);
            var second = CreateModel(CreateExperiment(true), lockOffset: false);
            var globalModel = new GlobalModel();
            globalModel.AddModel(first);
            globalModel.AddModel(second);

            var globalParameters = new GlobalModelParameters();
            globalParameters.SetConstraintForParameter(
                ParameterType.Nvalue1,
                VariableConstraint.SameForAll);
            globalParameters.AddorUpdateGlobalParameter(ParameterType.Nvalue1, 1);

            var context = new AnalysisContext(
                AnalysisModel.OneSetOfSites,
                globalModel,
                globalParameters,
                new Dictionary<ParameterType, IReadOnlyList<VariableConstraint>>());

            Assert.Equal(
                "7 variables • 3 data points • 2 experiments"
                    + System.Environment.NewLine
                    + "Will experiments fit globally",
                AnalysisContextSummaryPresentation.BuildText(context));
        }

        [Fact]
        public void NullContextSummaryReportsNotReady()
        {
            Assert.Equal(
                "No analysis ready",
                AnalysisContextSummaryPresentation.BuildText(null));
        }

        static OneSetOfSites CreateModel(
            ExperimentData experiment,
            bool lockOffset)
        {
            var model = new OneSetOfSites(experiment);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(
                ParameterType.Offset,
                0,
                lockOffset);
            return model;
        }

        static ExperimentData CreateExperiment(params bool[] included)
        {
            var experiment = new ExperimentData("summary-test.itc");

            for (var index = 0; index < included.Length; index++)
            {
                experiment.Injections.Add(new InjectionData(
                    experiment,
                    index,
                    volume: 1,
                    mass: 1,
                    include: included[index]));
            }

            return experiment;
        }
    }
}
