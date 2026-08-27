using System.Linq;

using AnalysisITC.Core.Analysis;
using AnalysisITC.Core.Analysis.Models;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public class PublicationFigureMetadataTests
    {
        [Fact]
        public void MetadataKeywordsUsePlainParameterLabels()
        {
            var experiment = new ExperimentData("metadata-test.itc");
            var model = new OneSetOfSites(experiment);
            model.Parameters.AddOrUpdateParameter(ParameterType.Nvalue1, 1);
            model.Parameters.AddOrUpdateParameter(ParameterType.Affinity1, 6);
            model.Parameters.AddOrUpdateParameter(ParameterType.Enthalpy1, -1000);
            model.Parameters.AddOrUpdateParameter(ParameterType.Offset, 0);
            var solution = SolutionInterface.FromModel(
                model,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

            var document = PublicationFigureBuilder.Build(
                new PublicationFigureSource(experiment, solution),
                new PublicationFigureOptions
                {
                    ShowThermogram = false,
                    ShowFitParameters = false
                });

            Assert.Contains(document.MetadataKeywords, keyword => keyword.StartsWith("Kd = "));
            Assert.Contains(document.MetadataKeywords, keyword => keyword.StartsWith("ΔH = "));
            Assert.DoesNotContain(document.MetadataKeywords, keyword => keyword.Contains('*') || keyword.Contains('{') || keyword.Contains('}'));
        }

        [Fact]
        public void MetadataKeywordsIncludeFourthSequentialStep()
        {
            var experiment = new ExperimentData("sequential-metadata.itc")
            {
                MeasuredTemperature = 25,
                TargetTemperature = 25,
            };
            var model = new SequentialBindingSites(experiment);
            model.InitializeParameters(experiment);
            model.ModelOptions[AttributeKey.SequentialSiteCount].IntValue = 4;
            model.ApplyModelOptions();
            var solution = SolutionInterface.FromModel(
                model,
                SolverConvergence.FromSnapshot(new SolverConvergenceSnapshot()));

            var document = PublicationFigureBuilder.Build(
                new PublicationFigureSource(experiment, solution),
                new PublicationFigureOptions
                {
                    ShowThermogram = false,
                    ShowFitParameters = false,
                });

            Assert.Contains(document.MetadataKeywords, keyword => keyword.StartsWith("Kd4 = "));
            Assert.Contains(document.MetadataKeywords, keyword => keyword.StartsWith("ΔH4 = "));
            Assert.Contains(document.MetadataKeywords, keyword => keyword.StartsWith("ΔG4 = "));
        }
    }
}
