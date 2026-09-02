using System;
using System.Collections.Generic;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Presentation;
using AnalysisITC.Platform;

using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class AnalysisResultOverviewColumnWidthPolicyTests
    {
        static readonly IReadOnlyList<AnalysisResultOverviewColumn> Columns =
            new[]
            {
                new AnalysisResultOverviewColumn(
                    "Experiment",
                    "Experiment",
                    AnalysisResultColumnAlignment.Left,
                    170),
                new AnalysisResultOverviewColumn(
                    "Value",
                    "Value",
                    AnalysisResultColumnAlignment.Right,
                    108),
            };

        [Fact]
        public void AutomaticWidthsAreClampedToConfiguredRange()
        {
            var widths = AnalysisResultOverviewColumnWidthPolicy.Calculate(
                Columns,
                new Dictionary<string, double>
                {
                    ["Experiment"] = 20,
                    ["Value"] = 900,
                },
                availableWidth: 0);

            Assert.Equal(90, widths["Experiment"]);
            Assert.Equal(300, widths["Value"]);
        }

        [Fact]
        public void ExperimentReceivesOnlyPositiveViewportSurplus()
        {
            var measurements = new Dictionary<string, double>
            {
                ["Experiment"] = 150,
                ["Value"] = 100,
            };

            var expanded = AnalysisResultOverviewColumnWidthPolicy.Calculate(
                Columns,
                measurements,
                availableWidth: 500);
            var overflowing = AnalysisResultOverviewColumnWidthPolicy.Calculate(
                Columns,
                measurements,
                availableWidth: 200);

            Assert.Equal(400, expanded["Experiment"]);
            Assert.Equal(100, expanded["Value"]);
            Assert.Equal(150, overflowing["Experiment"]);
            Assert.Equal(100, overflowing["Value"]);
        }

        [Fact]
        public void ManualWidthsUseMinimumButNotAutomaticMaximum()
        {
            var widths = AnalysisResultOverviewColumnWidthPolicy.Calculate(
                Columns,
                new Dictionary<string, double>(),
                availableWidth: 0,
                new Dictionary<string, double>
                {
                    ["Experiment"] = 50,
                    ["Value"] = 480,
                });

            Assert.Equal(90, widths["Experiment"]);
            Assert.Equal(480, widths["Value"]);
        }

        [Fact]
        public void SessionWidthSettingSavesLoadsAndResets()
        {
            var originalStore = PlatformServices.SettingsStore;
            var store = new InMemorySettingsStore();
            PlatformServices.RegisterSettingsStore(store);

            try
            {
                AppSettings.Reset();
                AppSettings.RememberResultTableColumnWidthsForSession = true;
                AppSettings.Save();

                AppSettings.RememberResultTableColumnWidthsForSession = false;
                AppSettings.Load();
                Assert.True(AppSettings.RememberResultTableColumnWidthsForSession);

                AppSettings.Reset();
                Assert.False(AppSettings.RememberResultTableColumnWidthsForSession);
            }
            finally
            {
                PlatformServices.RegisterSettingsStore(originalStore);
                AppSettings.RememberResultTableColumnWidthsForSession = false;
            }
        }

        [Fact]
        public void InvalidMeasurementsFallBackToPreferredWidth()
        {
            var widths = AnalysisResultOverviewColumnWidthPolicy.Calculate(
                Columns,
                new Dictionary<string, double>
                {
                    ["Experiment"] = double.NaN,
                    ["Value"] = double.PositiveInfinity,
                },
                availableWidth: 0);

            Assert.Equal(170, widths["Experiment"]);
            Assert.Equal(108, widths["Value"]);
        }
    }
}
