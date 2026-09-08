using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class FitInformationCriteriaTests
    {
        [Fact]
        public void UnweightedCriteriaIncludeEstimatedVarianceInK()
        {
            var solution = CreateSolution(10, 2, weighted: false, residual: 1e-6);
            var criteria = FitInformationCriteriaCalculator.Calculate(solution);
            var rss = 10e-12;
            var minusTwoLogLikelihood = 10 * (Math.Log(2 * Math.PI * rss / 10) + 1);
            var aic = minusTwoLogLikelihood + 2 * 3;

            Assert.Equal(10, criteria.ObservationCount);
            Assert.Equal(2, criteria.FittedParameterCount);
            Assert.Equal(3, criteria.LikelihoodParameterCount);
            Assert.Equal(GaussianLikelihoodMode.EstimatedCommonVariance, criteria.LikelihoodMode);
            Assert.False(criteria.UsesKnownObservationSigmas);
            Assert.Equal(minusTwoLogLikelihood, criteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(aic, criteria.Aic.Value, 12);
            Assert.Equal(aic + 2 * 3 * 4.0 / 6, criteria.Aicc.Value, 12);
        }

        [Fact]
        public void WeightedCriteriaEstimateVarianceAndCountItInK()
        {
            var solution = CreateSolution(10, 2, weighted: true, residual: 2e-6);
            var criteria = FitInformationCriteriaCalculator.Calculate(solution);
            var member = Assert.Single(solution.Solutions);
            member.UseWeightedFitting = true;
            var memberCriteria = FitInformationCriteriaCalculator.Calculate(member);
            // Equal input sigmas reduce to a common fitted SD of 2 µJ.
            var density = Math.Exp(-0.5) / Math.Sqrt(2 * Math.PI * 4e-12);
            var minusTwoLogLikelihood = -20 * Math.Log(density);

            Assert.Equal(3, criteria.LikelihoodParameterCount);
            Assert.Equal(GaussianLikelihoodMode.EstimatedWeightedVariance, criteria.LikelihoodMode);
            Assert.False(criteria.UsesKnownObservationSigmas);
            Assert.Equal(minusTwoLogLikelihood, criteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(minusTwoLogLikelihood + 6, criteria.Aic.Value, 12);
            Assert.Equal(minusTwoLogLikelihood + 6 + 24.0 / 6, criteria.Aicc.Value, 12);
            Assert.Equal(criteria.Aic, memberCriteria.Aic);
            Assert.Equal(criteria.Aicc, memberCriteria.Aicc);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FixedParametersAreExcludedAndAicRemainsAvailableWhenAiccDoesNot(bool weighted)
        {
            var solution = CreateSolution(4, 2, weighted, residual: 1e-6);
            var criteria = FitInformationCriteriaCalculator.Calculate(solution);

            Assert.Equal(2, criteria.FittedParameterCount);
            Assert.Equal(3, criteria.LikelihoodParameterCount);
            Assert.True(criteria.IsAicAvailable);
            Assert.False(criteria.IsAiccAvailable);
            Assert.Equal(FitInformationCriteriaCalculator.AiccSampleSizeReason, criteria.AiccUnavailableReason);
            Assert.NotNull(criteria.Aic);
            Assert.Null(criteria.Aicc);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LikelihoodFailureReasonIsSharedByAicAndAicc(bool weighted)
        {
            var criteria = FitInformationCriteriaCalculator.Calculate(
                CreateSolution(5, 0, weighted, residual: 0));

            Assert.False(criteria.IsAicAvailable);
            Assert.False(criteria.IsAiccAvailable);
            Assert.Null(criteria.MinusTwoLogLikelihood);
            Assert.Null(criteria.Aic);
            Assert.Null(criteria.Aicc);
            Assert.Equal(GaussianLikelihoodEvaluator.ZeroResidualVarianceReason, criteria.AicUnavailableReason);
            Assert.Equal(criteria.AicUnavailableReason, criteria.AiccUnavailableReason);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SharedCoordinatesCountOnceAndMemberCoordinatesCountPerMember(bool weighted)
        {
            var first = CreateSolution(4, 1, weighted, residual: 1e-6).Model.Models[0];
            var second = CreateSolution(4, 1, weighted, residual: 2e-6).Model.Models[0];
            var global = new GlobalModel(new List<Model> { first, second });
            global.Parameters.AddIndivdualParameter(first.Parameters);
            global.Parameters.AddIndivdualParameter(second.Parameters);
            global.Parameters.AddorUpdateGlobalParameter(
                ParameterType.Affinity1,
                6,
                islocked: false);
            var solution = new GlobalSolution(
                new GlobalSolver { Model = global, UseErrorWeightedFitting = weighted },
                Convergence());

            var criteria = FitInformationCriteriaCalculator.Calculate(solution);

            Assert.Equal(3, criteria.FittedParameterCount);
            Assert.Equal(4, criteria.LikelihoodParameterCount);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(10.0)]
        public void WeightedSharedVersusIndividualMeansMatchAnalyticAiccDifference(double sigmaMultiplier)
        {
            var individual = CreateMeanComparison(shared: false, sigmaMultiplier);
            var shared = CreateMeanComparison(shared: true, sigmaMultiplier);

            // Analytic weighted means are 3.9, 4.0, 4.1 µJ separately, or 4.0 µJ
            // shared. Q is 48 versus 68 at the original sigmas; their ratio is
            // invariant to a common change in sigma. Shared fitting removes 2 means.
            var expectedDifference = 12 * Math.Log(68.0 / 48) + (4 - 8) + (12.0 / 9 - 40.0 / 7);
            Assert.Equal(3, individual.InformationCriteria.FittedParameterCount);
            Assert.Equal(4, individual.InformationCriteria.LikelihoodParameterCount);
            Assert.Equal(1, shared.InformationCriteria.FittedParameterCount);
            Assert.Equal(2, shared.InformationCriteria.LikelihoodParameterCount);
            Assert.Equal(expectedDifference,
                shared.InformationCriteria.Aicc.Value - individual.InformationCriteria.Aicc.Value, 10);
            Assert.True(shared.InformationCriteria.Aicc < individual.InformationCriteria.Aicc);
            Assert.All(shared.Solution.Solutions, member => Assert.Null(member.InformationCriteria));
        }

        [Fact]
        public void WeightedIndependentMembersEstimateLocalVariancesAndAnalysisEstimatesOnePooledMultiplier()
        {
            var result = CreateIndependentResult(true, (4, 1e-6), (10, 6e-6));
            foreach (var injection in result.Solution.Solutions[1].Data.Injections)
                injection.SetPeakArea(new FloatWithError(6e-6, 2e-6));
            result.UpdateSolution(result.Solution);
            var members = result.Solution.Solutions;

            var pooledMinusTwoLogLikelihood = 14 * (Math.Log(2 * Math.PI * (94.0 / 14)) + 1)
                + 4 * Math.Log(1e-12) + 10 * Math.Log(4e-12);
            Assert.Equal(3, result.InformationCriteria.LikelihoodParameterCount);
            Assert.All(members, member => Assert.Equal(2, member.InformationCriteria.LikelihoodParameterCount));
            Assert.Equal(4 * (Math.Log(2 * Math.PI * 1e-12) + 1),
                members[0].InformationCriteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(10 * (Math.Log(2 * Math.PI * 36e-12) + 1),
                members[1].InformationCriteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(pooledMinusTwoLogLikelihood, result.InformationCriteria.MinusTwoLogLikelihood.Value, 12);
            Assert.NotEqual(members.Sum(member => member.InformationCriteria.Aic.Value), result.InformationCriteria.Aic.Value);
        }

        [Fact]
        public void AnalysisResultSnapshotIsStableUntilSolutionIsReplaced()
        {
            var initial = CreateSolution(5, 0, weighted: false, residual: 1e-6);
            var result = new AnalysisResult(initial);
            var initialAic = result.InformationCriteria.Aic;
            var initialAicc = result.InformationCriteria.Aicc;

            initial.Model.Models[0].Data.Injections[0].SetPeakArea(new FloatWithError(100e-6, 1e-6));
            Assert.Equal(initialAic, result.InformationCriteria.Aic);
            Assert.Equal(initialAicc, result.InformationCriteria.Aicc);

            var replacement = CreateSolution(5, 0, weighted: false, residual: 2e-6);
            result.UpdateSolution(replacement);
            Assert.NotEqual(initialAic, result.InformationCriteria.Aic);
        }

        [Fact]
        public void IndependentMembersUseSeparateLikelihoodsWhileAnalysisUsesPooledLikelihood()
        {
            var result = CreateIndependentResult(
                (4, 1e-6),
                (10, 3e-6));
            var members = result.Solution.Solutions;
            var pooled = FitInformationCriteriaCalculator.Calculate(result.Solution);

            Assert.Equal(4, members[0].InformationCriteria.ObservationCount);
            Assert.Equal(10, members[1].InformationCriteria.ObservationCount);
            Assert.Equal(14, result.InformationCriteria.ObservationCount);
            Assert.Equal(pooled.Aic, result.InformationCriteria.Aic);
            Assert.Equal(pooled.Aicc, result.InformationCriteria.Aicc);

            var firstMinusTwoLogLikelihood = 4 * (Math.Log(2 * Math.PI * 1e-12) + 1);
            var secondMinusTwoLogLikelihood = 10 * (Math.Log(2 * Math.PI * 9e-12) + 1);
            var pooledMinusTwoLogLikelihood = 14 * (Math.Log(2 * Math.PI * (94e-12 / 14)) + 1);
            Assert.Equal(firstMinusTwoLogLikelihood, members[0].InformationCriteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(secondMinusTwoLogLikelihood, members[1].InformationCriteria.MinusTwoLogLikelihood.Value, 12);
            Assert.Equal(pooledMinusTwoLogLikelihood, result.InformationCriteria.MinusTwoLogLikelihood.Value, 12);
            Assert.NotEqual(result.InformationCriteria.Aic, members[0].InformationCriteria.Aic);
            Assert.NotEqual(result.InformationCriteria.Aic, members[1].InformationCriteria.Aic);
        }

        [Fact]
        public void MemberCriteriaRemainStableUntilResultSolutionIsReplaced()
        {
            var initial = new AnalysisResult(CreateSolution(10, 1, weighted: false, residual: 1e-6));
            var initialMember = Assert.Single(initial.Solution.Solutions);
            var initialAic = initialMember.InformationCriteria.Aic;
            var initialAicc = initialMember.InformationCriteria.Aicc;

            initialMember.Data.Injections[0].SetPeakArea(new FloatWithError(100e-6, 1e-6));
            Assert.Equal(initialAic, initialMember.InformationCriteria.Aic);
            Assert.Equal(initialAicc, initialMember.InformationCriteria.Aicc);

            var replacement = CreateSolution(10, 1, weighted: false, residual: 2e-6);
            initial.UpdateSolution(replacement);
            var replacementMember = Assert.Single(initial.Solution.Solutions);
            Assert.NotEqual(initialAic, replacementMember.InformationCriteria.Aic);
            Assert.Equal(replacementMember.InformationCriteria.Aic, initial.InformationCriteria.Aic);
        }

        [Fact]
        public void IndependentlyFittedMemberReceivesItsOwnCriteriaSnapshot()
        {
            var solution = CreateSolution(10, 2, weighted: false, residual: 1e-6);
            var result = new AnalysisResult(solution);
            var member = Assert.Single(solution.Solutions);
            var expected = FitInformationCriteriaCalculator.Calculate(member);

            Assert.NotNull(member.InformationCriteria);
            Assert.Equal(expected.ObservationCount, member.InformationCriteria.ObservationCount);
            Assert.Equal(expected.FittedParameterCount, member.InformationCriteria.FittedParameterCount);
            Assert.Equal(expected.LikelihoodParameterCount, member.InformationCriteria.LikelihoodParameterCount);
            Assert.Equal(expected.Aic, member.InformationCriteria.Aic);
            Assert.Equal(expected.Aicc, member.InformationCriteria.Aicc);
            Assert.Equal(result.InformationCriteria.Aic, member.InformationCriteria.Aic);
            Assert.Equal(result.InformationCriteria.Aicc, member.InformationCriteria.Aicc);
        }

        [Fact]
        public void GloballyFittedMembersDoNotReceiveIndividualCriteria()
        {
            var first = CreateSolution(10, 1, weighted: false, residual: 1e-6).Model.Models[0];
            var second = CreateSolution(10, 1, weighted: false, residual: 2e-6).Model.Models[0];
            var global = new GlobalModel(new List<Model> { first, second });
            global.Parameters.AddIndivdualParameter(first.Parameters);
            global.Parameters.AddIndivdualParameter(second.Parameters);
            global.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 6, islocked: false);
            var solution = new GlobalSolution(
                new GlobalSolver { Model = global },
                Convergence());
            global.Solution = solution;

            var result = new AnalysisResult(solution);

            Assert.False(result.Model.ShouldFitIndividually);
            Assert.NotNull(result.InformationCriteria);
            Assert.All(solution.Solutions, member => Assert.Null(member.InformationCriteria));
            var table = AnalysisResultOverviewTable.Build(result, EnergyUnit.KiloJoule, useKelvin: false);
            Assert.DoesNotContain(table.Columns, column => column.Id == "InformationCriteria");

            var report = AnalysisReportBuilder.Build(result);
            var reportOverview = report.Sections
                .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
                .Blocks.OfType<AnalysisReportTableBlock>().Single();
            Assert.DoesNotContain(reportOverview.Columns, column => column.Id == "InformationCriteria");
        }

        [Fact]
        public void OverviewTableShowsMemberAiccAndAicFallback()
        {
            var result = new AnalysisResult(CreateSolution(4, 2, weighted: false, residual: 1e-6));
            var table = AnalysisResultOverviewTable.Build(result, EnergyUnit.KiloJoule, useKelvin: false);
            var column = Assert.Single(table.Columns, item => item.Id == "InformationCriteria");

            Assert.Equal("AICc / AIC", column.Title);
            Assert.StartsWith("AIC ", table.Rows.Single()[column.Id], StringComparison.Ordinal);

            var report = AnalysisReportBuilder.Build(result);
            var reportOverview = report.Sections
                .Single(section => section.Kind == AnalysisReportSectionKind.AnalysisSummary)
                .Blocks.OfType<AnalysisReportTableBlock>().Single();
            Assert.Contains(reportOverview.Columns, item => item.Id == "InformationCriteria");
        }

        [Fact]
        public void OverviewTableShowsUnavailableForMemberLikelihoodFailure()
        {
            var result = new AnalysisResult(CreateSolution(5, 0, weighted: false, residual: 0));
            var table = AnalysisResultOverviewTable.Build(result, EnergyUnit.KiloJoule, useKelvin: false);

            Assert.Equal("Unavailable", table.Rows.Single()["InformationCriteria"]);
        }

        [Fact]
        public void SummaryUsesAiccAndPutsAicAndFitDetailsInOneTooltip()
        {
            var result = new AnalysisResult(CreateSolution(10, 2, weighted: false, residual: 1e-6));
            var summary = InformationCriteriaSummaryPresentation.For(result, CultureInfo.InvariantCulture);

            Assert.Equal("AICc", summary.CriterionLabel);
            Assert.Equal(result.InformationCriteria.Aicc.Value.ToString("G6", CultureInfo.InvariantCulture), summary.CriterionValue);
            Assert.Contains("AICc shown; AIC = ", summary.Tooltip);
            Assert.Contains("n = included injections.", summary.Tooltip);
            Assert.Contains("K = fitted parameters + 1 estimated common residual variance.", summary.Tooltip);
            Assert.EndsWith("Compare only like-for-like fits.", summary.Tooltip, StringComparison.Ordinal);
            Assert.Equal("Compare only like-for-like fits.", summary.Footer);
        }

        [Fact]
        public void SummaryUsesAicFallbackAndExplainsWhyAiccIsUnavailable()
        {
            var result = new AnalysisResult(CreateSolution(4, 2, weighted: false, residual: 1e-6));
            var summary = InformationCriteriaSummaryPresentation.For(result, CultureInfo.InvariantCulture);

            Assert.Equal("AIC", summary.CriterionLabel);
            Assert.Equal(result.InformationCriteria.Aic.Value.ToString("G6", CultureInfo.InvariantCulture), summary.CriterionValue);
            Assert.Contains("AIC shown; AICc unavailable (n ≤ K + 1).", summary.Tooltip);
            Assert.Contains("K = fitted parameters + 1 estimated common residual variance.", summary.Tooltip);
        }

        [Fact]
        public void SummaryExplainsWeightedSigmasAndUnavailableLikelihood()
        {
            var weighted = new AnalysisResult(CreateSolution(10, 2, weighted: true, residual: 2e-6));
            var weightedSummary = InformationCriteriaSummaryPresentation.For(weighted, CultureInfo.InvariantCulture);
            Assert.Contains("K = fitted parameters + 1 estimated variance multiplier; injection errors supply relative uncertainties.", weightedSummary.Tooltip);

            var unavailable = new AnalysisResult(CreateSolution(5, 0, weighted: false, residual: 0));
            var unavailableSummary = InformationCriteriaSummaryPresentation.For(unavailable, CultureInfo.InvariantCulture);
            Assert.Contains("AIC unavailable (", unavailableSummary.Tooltip);
            Assert.Contains(GaussianLikelihoodEvaluator.ZeroResidualVarianceReason, unavailableSummary.Tooltip);
            Assert.Equal(unavailable.InformationCriteria.AicUnavailableReason, unavailableSummary.CriterionValue);
        }

        [Fact]
        public void SummaryFooterExplainsIndependentAndSharedScopes()
        {
            var independentUnweighted = CreateIndependentResult(
                false,
                (10, 1e-6),
                (10, 2e-6));
            Assert.Equal(
                "Pooled with one common residual variance; neither AIC nor AICc is a sum of member values.",
                InformationCriteriaSummaryPresentation.For(independentUnweighted).Footer);

            var independentWeighted = CreateIndependentResult(
                true,
                (10, 1e-6),
                (10, 2e-6));
            Assert.Equal(
                "Pooled with one common variance multiplier for the injection errors; neither AIC nor AICc is a sum of member values.",
                InformationCriteriaSummaryPresentation.For(independentWeighted).Footer);

            var first = CreateSolution(10, 1, weighted: false, residual: 1e-6).Model.Models[0];
            var second = CreateSolution(10, 1, weighted: false, residual: 2e-6).Model.Models[0];
            var globalModel = new GlobalModel(new List<Model> { first, second });
            globalModel.Parameters.AddIndivdualParameter(first.Parameters);
            globalModel.Parameters.AddIndivdualParameter(second.Parameters);
            globalModel.Parameters.AddorUpdateGlobalParameter(ParameterType.Affinity1, 6, islocked: false);
            var global = new GlobalSolution(new GlobalSolver { Model = globalModel }, Convergence());
            globalModel.Solution = global;
            var shared = new AnalysisResult(global);
            Assert.Equal(
                "Combined across all members using shared fitted parameters.",
                InformationCriteriaSummaryPresentation.For(shared).Footer);
        }

        static GlobalSolution CreateSolution(int observations, int fittedParameters, bool weighted, double residual)
        {
            var data = new ExperimentData("aic.itc")
            {
                CellConcentration = new FloatWithError(10e-6),
                SyringeConcentration = new FloatWithError(100e-6),
                CellVolume = 1.4e-3,
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var predictions = new Dictionary<int, double>();
            for (var index = 0; index < observations; index++)
            {
                var injection = new InjectionData(data, index, 2e-6, 2e-10, true)
                {
                    ActualCellConcentration = 10e-6,
                    ActualTitrantConcentration = index * 2e-6,
                };
                injection.SetPeakArea(new FloatWithError(residual, 1e-6));
                data.Injections.Add(injection);
                predictions[index] = 0;
            }

            var model = new ProbeModel(data, predictions);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, islocked: fittedParameters < 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, islocked: fittedParameters < 2);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, islocked: true);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0, islocked: true);
            data.Model = model;

            var global = new GlobalModel(new List<Model> { model });
            global.Parameters.AddIndivdualParameter(model.Parameters);
            var solver = new GlobalSolver
            {
                Model = global,
                UseErrorWeightedFitting = weighted,
            };
            var solution = new GlobalSolution(solver, Convergence());
            foreach (var member in solution.Solutions)
                member.UseWeightedFitting = weighted;
            global.Solution = solution;
            return solution;
        }

        static AnalysisResult CreateMeanComparison(bool shared, double sigmaMultiplier)
        {
            var models = new List<Model>();
            foreach (var mean in new[] { 3.9e-6, 4.0e-6, 4.1e-6 })
            {
                var data = CreateSolution(4, 0, weighted: true, residual: 0).Model.Models[0].Data;
                var offsets = new[] { -0.1e-6, 0.1e-6, -0.2e-6, 0.2e-6 };
                var sigmas = new[] { 0.05e-6, 0.05e-6, 0.1e-6, 0.1e-6 };
                for (var i = 0; i < 4; i++)
                    data.Injections[i].SetPeakArea(new FloatWithError(mean + offsets[i], sigmas[i] * sigmaMultiplier));
                var model = new ConstantMeanModel(data);
                model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1, islocked: true);
                model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000, islocked: true);
                model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6, islocked: true);
                model.Parameters.AddOrUpdateParameter(ParameterType.Offset, shared ? 4e-6 : mean);
                data.Model = model;
                models.Add(model);
            }
            var global = new GlobalModel(models);
            foreach (var model in models)
                global.Parameters.AddIndivdualParameter(model.Parameters);
            if (shared)
            {
                global.Parameters.AddorUpdateGlobalParameter(ParameterType.Offset, 4e-6);
                global.Parameters.SetConstraintForParameter(ParameterType.Offset, VariableConstraint.SameForAll);
                global.Parameters.SetIndividualFromGlobal();
            }
            var solution = new GlobalSolution(new GlobalSolver { Model = global, UseErrorWeightedFitting = true }, Convergence());
            foreach (var member in solution.Solutions)
                member.UseWeightedFitting = true;
            global.Solution = solution;
            return new AnalysisResult(solution);
        }

        static AnalysisResult CreateIndependentResult(params (int Observations, double Residual)[] members)
            => CreateIndependentResult(weighted: false, members);

        static AnalysisResult CreateIndependentResult(
            bool weighted,
            params (int Observations, double Residual)[] members)
        {
            var sourceSolutions = members
                .Select(member => CreateSolution(member.Observations, 1, weighted, residual: member.Residual))
                .ToList();
            var models = sourceSolutions.Select(solution => solution.Model.Models[0]).ToList();
            var memberSolutions = sourceSolutions.Select(solution => solution.Solutions[0]).ToList();
            var globalModel = new GlobalModel(models)
            {
                Parameters = new GlobalModelParameters(),
            };
            foreach (var model in models)
                globalModel.Parameters.AddIndivdualParameter(model.Parameters);

            var global = new GlobalSolution(
                new GlobalSolver { Model = globalModel, UseErrorWeightedFitting = weighted },
                memberSolutions,
                Convergence());
            globalModel.Solution = global;
            return new AnalysisResult(global);
        }

        static SolverConvergence Convergence()
        {
            return SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot
            {
                Algorithm = SolverAlgorithm.LevenbergMarquardt,
                Termination = SolverTermination.Converged,
            });
        }

        sealed class ConstantMeanModel : Model
        {
            public ConstantMeanModel(ExperimentData data) : base(data) { }
            public override double Evaluate(int injectionindex, bool withoffset = true)
                => Parameters.Table[ParameterType.Offset].Value;
        }

        sealed class ProbeModel : Model
        {
            readonly IReadOnlyDictionary<int, double> predictions;

            public ProbeModel(ExperimentData data, IReadOnlyDictionary<int, double> predictions)
                : base(data) => this.predictions = predictions;

            public override double Evaluate(int injectionindex, bool withoffset = true) => predictions[injectionindex];
        }
    }
}
