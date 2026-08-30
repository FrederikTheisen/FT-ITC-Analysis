using System.Collections.Generic;
using System.Linq;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Core.Units;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class PublicationFigureThermogramScaleTests
    {
        [Theory]
        [InlineData(EnergyUnitFamily.Joules)]
        [InlineData(EnergyUnitFamily.Calories)]
        public void AutomaticThermogramAxisUsesTheSameScaleAsTheSeries(EnergyUnitFamily family)
        {
            var experiment = new ExperimentData("thermogram-scale.itc")
            {
                DataPoints = new List<DataPoint>
                {
                    new DataPoint(0, -15E-6f),
                    new DataPoint(1, 5E-6f),
                },
            };
            var options = new PublicationFigureOptions
            {
                EnergyUnitFamily = family,
                ShowExperimentDetails = false,
                ShowFitParameters = false,
            };

            var document = PublicationFigureBuilder.Build(experiment, options);
            var panel = document.ThermogramPanel;
            var series = panel.Series.Single(item => item.Role == PublicationSeriesRole.Thermogram);
            var scale = ThermogramUnits.DifferentialPowerScale(family);
            var expectedMinimum = experiment.DataPoints.Min(point => point.Power * scale);
            var expectedMaximum = experiment.DataPoints.Max(point => point.Power * scale);
            var expectedSpan = expectedMaximum - expectedMinimum;

            Assert.Equal(expectedMinimum, series.Points.Min(point => point.Y), 8);
            Assert.Equal(expectedMaximum, series.Points.Max(point => point.Y), 8);
            Assert.Equal(expectedMinimum - 0.1 * expectedSpan, panel.YAxis.Minimum, 8);
            Assert.Equal(expectedMaximum + 0.1 * expectedSpan, panel.YAxis.Maximum, 8);
        }
    }
}
