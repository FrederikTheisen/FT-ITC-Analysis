using System;
using System.Globalization;
using System.IO;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.DataReaders;
using AnalysisITC.Core.Utilities;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Published model reproduction")]
public sealed class ScientificImportCultureTests
{
    [Theory]
    [InlineData("en-US")]
    [InlineData("da-DK")]
    [InlineData("fr-FR")]
    public void InstrumentDecimalPointsHaveTheSameMeaningAcrossUserLocales(string locale)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(locale);
            // Instrument files use decimal points and comma-separated fields;
            // a dot must never become a thousands separator in a Danish UI.
            Assert.Equal(new[] { 1.25f, -2.5f, 25.125f }, StringParsers.ParseLine("1.25,-2.5,25.125"));
            // Preserve the reader's existing tolerance of trailing protocol
            // extension fields while parsing the four defined numeric fields.
            var injection = new InjectionData(new ExperimentData("protocol reference"), "$2.5,2.25,80.5,1.5,extension", 0);
            Assert.InRange(Math.Abs(injection.Volume - 2.5e-6), 0, 1e-12);
            Assert.Equal(2.25f, injection.Duration);
            Assert.Equal(80.5f, injection.Delay);
            Assert.Equal(1.5f, injection.Filter);

            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
                "Fixtures", "ScientificValidation", "RawPipeline", "analytic-one-site.itc"));
            var experiment = MicroCalITC200Reader.ReadStream(stream, "analytic-one-site.itc");
            // The independent analytic raw file starts at 3 µW, then has a
            // 1 nW/s drift. These are absolute physical targets in watts.
            Assert.InRange(Math.Abs(experiment.DataPoints[0].Power - 3e-6), 0, 5e-13);
            Assert.InRange(Math.Abs(experiment.DataPoints[1].Power - 3.001e-6), 0, 5e-13);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
