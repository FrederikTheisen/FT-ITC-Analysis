using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class RawImportIntegrityTests
    {
        [Fact]
        public void MicroCalZeroCellConcentrationIsKeptAndUsesTitrantAxis()
        {
            // Header line "# 0" is the recorded cell concentration.
            var experiment = ReadMicroCal(File.ReadAllText(Fixture("dissociation_data_test.itc")));

            Assert.Equal(0, experiment.CellConcentration.Value);
            Assert.Equal(0.176e-3, experiment.SyringeConcentration.Value, 9);
            Assert.Equal(AnalysisXAxisType.TitrantConcentration, experiment.AxisType);
            Assert.All(experiment.Injections, injection =>
                Assert.Equal(injection.ActualTitrantConcentration, injection.Ratio));
        }

        [Fact]
        public void MicroCalRecordedInjectionTimeIsKeptAcrossThermogramGap()
        {
            // Marker "@1,0.5000,1.0, 150.146000000001"; samples 131–150 s removed.
            var text = RemoveSamples(File.ReadAllText(Fixture("data_1.itc")), time => time > 130 && time < 151);
            var warnings = new List<string>();

            var experiment = ReadMicroCal(text, warnings);

            Assert.Equal(150.146f, experiment.Injections.Single(injection => injection.ID == 0).Time);
            var warning = Assert.Single(warnings);
            Assert.Contains("Injection #1", warning);
        }

        [Fact]
        public void MicroCalInjectionWithoutPrecedingSampleUsesRecordedTime()
        {
            var text = RemoveSamples(File.ReadAllText(Fixture("data_1.itc")), time => time < 151);

            var experiment = ReadMicroCal(text);

            Assert.Equal(150.146f, experiment.Injections.Single(injection => injection.ID == 0).Time);
        }

        [Fact]
        public void MicroCalConcatenatedRunsPlaceLaterMarkersAtPrecedingSample()
        {
            // Markers of later runs restart the run clock ("@14,...,60.048"), while samples keep the file clock.
            var text = File.ReadAllText(Fixture("Lenette112+113+114.itc"));
            var lines = text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
            var markerIndex = lines.FindIndex(line => line.StartsWith("@14,", StringComparison.Ordinal));
            var precedingSampleTime = float.Parse(lines[markerIndex - 1].Split(',')[0], CultureInfo.InvariantCulture);
            var firstMarkerTime = float.Parse(lines.First(line => line.StartsWith("@1,", StringComparison.Ordinal)).Split(',')[3], CultureInfo.InvariantCulture);

            var experiment = ReadMicroCal(text);

            Assert.Equal(firstMarkerTime, experiment.Injections.Single(injection => injection.ID == 0).Time);
            Assert.Equal(precedingSampleTime, experiment.Injections.Single(injection => injection.ID == 13).Time);
        }

        [Fact]
        public void MicroCalMarkerDurationReplacesProtocolDuration()
        {
            var text = File.ReadAllText(Fixture("data_1.itc"))
                .Replace("@2,2.0001,4.0,", "@2,2.0001,8.0,", StringComparison.Ordinal);

            var experiment = ReadMicroCal(text);

            Assert.Equal(8f, experiment.Injections.Single(injection => injection.ID == 1).Duration);
            Assert.Equal(2.0001e-6, experiment.Injections.Single(injection => injection.ID == 1).Volume, 10);
        }

        [Theory]
        [InlineData("power")]
        [InlineData("cell")]
        [InlineData("marker")]
        [InlineData("protocol")]
        public void MicroCalNonFiniteValuesAreRejected(string field)
        {
            var text = File.ReadAllText(Fixture("data_1.itc"));
            text = field switch
            {
                "power" => ReplaceFirstSampleField(text, 1, "NaN"),
                "cell" => ReplaceHashLine(text, 3, "NaN"),
                "marker" => text.Replace("@2,2.0001,4.0,", "@2,NaN,4.0,", StringComparison.Ordinal),
                _ => ReplaceFirstProtocolLine(text, "$ Infinity , 4 , 150 , 5"),
            };

            Assert.Throws<FormatException>(() => ReadMicroCal(text));
        }

        [Fact]
        public void TaZeroCellConcentrationIsKept()
        {
            var text = ReplaceHashLine(File.ReadAllText(Fixture("FileTypeTests", "16102024_Rocu_3mM_Sug_0_3mM_PBS_2.ta")), 3, "0");
            var path = Path.Combine(Path.GetTempPath(), $"zero-cell-{Guid.NewGuid():N}.ta");
            File.WriteAllText(path, text);
            try
            {
                var experiment = TAFileReader.ReadPath(path);

                Assert.Equal(0, experiment.CellConcentration.Value);
                Assert.Equal(3e-3, experiment.SyringeConcentration.Value, 9);
                Assert.Equal(AnalysisXAxisType.TitrantConcentration, experiment.AxisType);
            }
            finally
            {
                File.Delete(path);
            }
        }

        static ExperimentData ReadMicroCal(string text, List<string> warnings = null)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
            return MicroCalITC200Reader.ReadStream(stream, "integrity.itc", warning: message => warnings?.Add(message));
        }

        static string RemoveSamples(string text, Func<float, bool> remove)
        {
            var dataStream = false;
            var kept = new List<string>();
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed == "@0") dataStream = true;
                else if (dataStream && trimmed.Length > 0 && trimmed[0] != '@'
                    && remove(float.Parse(trimmed.Split(',')[0], CultureInfo.InvariantCulture)))
                    continue;
                kept.Add(line);
            }

            return string.Join("\n", kept);
        }

        static string ReplaceFirstSampleField(string text, int field, string value)
        {
            var lines = text.Split('\n');
            var index = Array.FindIndex(lines, line => line.Trim() == "@0") + 1;
            var fields = lines[index].Trim().Split(',');
            fields[field] = value;
            lines[index] = string.Join(",", fields);
            return string.Join("\n", lines);
        }

        static string ReplaceHashLine(string text, int occurrence, string value)
        {
            var lines = text.Split('\n');
            var count = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                if (++count != occurrence) continue;
                lines[i] = "# " + value;
                break;
            }

            return string.Join("\n", lines);
        }

        static string ReplaceFirstProtocolLine(string text, string replacement)
        {
            var lines = text.Split('\n');
            var index = Array.FindIndex(lines, line => line.TrimStart().StartsWith("$ ", StringComparison.Ordinal) && line.Contains(','));
            lines[index] = replacement;
            return string.Join("\n", lines);
        }

        static string Fixture(params string[] parts) =>
            Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());
    }
}
