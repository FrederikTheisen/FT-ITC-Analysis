using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using Xunit;

namespace AnalysisITC.Core.Tests;

[Collection("Preferences")]
public sealed class FloatWithErrorConcentrationFormattingTests : IDisposable
{
    readonly NumberPrecision originalPrecision = AppSettings.NumberPrecision;
    readonly UncertaintyDisplayStyle originalStyle = AppSettings.UncertaintyDisplayStyle;
    readonly CultureInfo originalCulture = CultureInfo.CurrentCulture;

    public static IEnumerable<object[]> PrecisionAndCultureCases()
    {
        foreach (var precision in Enum.GetValues<NumberPrecision>())
        foreach (var culture in new[] { "en-US", "fr-FR" })
            yield return new object[] { precision, culture };
    }

    [Theory]
    [MemberData(nameof(PrecisionAndCultureCases))]
    public void ValuesWithoutStandardDeviationKeepTheirExistingDisplay(NumberPrecision precision, string culture)
    {
        Configure(precision);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        var cases = new (FloatWithError Value, string Number, string Unit)[]
        {
            (new FloatWithError(.123456), "123.5", "mM"),
            (new FloatWithError(.0000123456), "12.3", "µM"),
            (new FloatWithError(1.23456e11), "1.23456E+11", "M"),
            (new FloatWithError(.123456, 0, .120, .128), "123.5 [120.0, 128.0]", "mM"),
        };

        foreach (var item in cases)
        {
            var expected = culture == "fr-FR" ? item.Number.Replace('.', ',') : item.Number;
            Assert.Equal(expected, item.Value.AsF1FormattedConcentration(false));
            Assert.Equal(expected + " " + item.Unit, item.Value.AsF1FormattedConcentration(true));
            Assert.Equal(precision, AppSettings.NumberPrecision);
        }

        Assert.Equal(culture == "fr-FR" ? "0,0" : "0.0", new FloatWithError(0).AsF1FormattedConcentration(false));
        Assert.Equal(precision, AppSettings.NumberPrecision);
    }

    [Theory]
    [InlineData(NumberPrecision.Strict, "123 ± 1 [120, 128]")]
    [InlineData(NumberPrecision.Standard, "123.5 ± 1.2 [120.0, 128.0]")]
    [InlineData(NumberPrecision.SingleDecimal, "123.5 ± 1.2 [120.0, 128.0]")]
    [InlineData(NumberPrecision.AllDecimals, "123.46 ± 1.2345 [120, 128]")]
    public void ValuesWithStandardDeviationKeepTheSelectedPrecision(NumberPrecision precision, string expected)
    {
        Configure(precision);
        var value = new FloatWithError(.123456, .0012345, .120, .128);

        Assert.Equal(expected, value.AsF1FormattedConcentration(false));
        Assert.Equal(expected + " mM", value.AsF1FormattedConcentration(true));
        Assert.Equal(precision, AppSettings.NumberPrecision);
    }

    [Theory]
    [InlineData(NumberPrecision.Strict)]
    [InlineData(NumberPrecision.Standard)]
    [InlineData(NumberPrecision.SingleDecimal)]
    [InlineData(NumberPrecision.AllDecimals)]
    public void PrecisionIsUnchangedDuringFormatting(NumberPrecision precision)
    {
        Configure(precision);
        var observedPrecisions = new List<NumberPrecision>();
        CultureInfo.CurrentCulture = new ObservingCulture(() => observedPrecisions.Add(AppSettings.NumberPrecision));

        Assert.Equal("123.5 mM", new FloatWithError(.123456).AsF1FormattedConcentration(true));

        Assert.NotEmpty(observedPrecisions);
        Assert.All(observedPrecisions, observed => Assert.Equal(precision, observed));
        Assert.Equal(precision, AppSettings.NumberPrecision);
    }

    [Theory]
    [InlineData(NumberPrecision.Strict)]
    [InlineData(NumberPrecision.Standard)]
    [InlineData(NumberPrecision.SingleDecimal)]
    [InlineData(NumberPrecision.AllDecimals)]
    public void FormattingFailureLeavesPrecisionUnchanged(NumberPrecision precision)
    {
        Configure(precision);
        NumberPrecision? observedPrecision = null;
        var failure = new FormatException("Injected numeric formatting failure.");
        CultureInfo.CurrentCulture = new ObservingCulture(() =>
        {
            observedPrecision = AppSettings.NumberPrecision;
            throw failure;
        });

        Assert.Same(failure, Assert.Throws<FormatException>(() =>
            new FloatWithError(.123456).AsF1FormattedConcentration(true)));

        Assert.Equal(precision, observedPrecision);
        Assert.Equal(precision, AppSettings.NumberPrecision);
    }

    [Theory]
    [InlineData(NumberPrecision.Strict)]
    [InlineData(NumberPrecision.Standard)]
    [InlineData(NumberPrecision.AllDecimals)]
    public async Task ConcurrentFormattingKeepsTheSelectedPrecision(NumberPrecision precision)
    {
        Configure(precision);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var timeout = TimeSpan.FromSeconds(10);
        var concentration = Task.Run(() =>
        {
            CultureInfo.CurrentCulture = new ObservingCulture(() =>
            {
                entered.Set();
                if (!release.Wait(timeout)) throw new TimeoutException("Formatting was not released.");
            });
            return new FloatWithError(.123456).AsF1FormattedConcentration(true);
        });

        try
        {
            Assert.True(entered.Wait(timeout), "Formatting did not reach numeric conversion.");
            var observedPrecision = AppSettings.NumberPrecision;
            var ordinaryNumber = new FloatWithError(12.345).AsNumber();
            var ordinaryConcentration = new FloatWithError(.123456).AsFormattedConcentration(ConcentrationUnit.mM);
            var secondConcentration = new FloatWithError(.0000123456).AsF1FormattedConcentration(true);
            var precisionAfterSecondCall = AppSettings.NumberPrecision;

            release.Set();
            Assert.Equal("123.5 mM", await concentration.WaitAsync(timeout));
            Assert.Equal(precision, observedPrecision);
            Assert.Equal("12.345", ordinaryNumber);
            Assert.Equal("123.46 mM", ordinaryConcentration);
            Assert.Equal("12.3 µM", secondConcentration);
            Assert.Equal(precision, precisionAfterSecondCall);
            Assert.Equal(precision, AppSettings.NumberPrecision);
        }
        finally
        {
            release.Set();
            await concentration.WaitAsync(timeout);
        }
    }

    void Configure(NumberPrecision precision)
    {
        AppSettings.NumberPrecision = precision;
        AppSettings.UncertaintyDisplayStyle = UncertaintyDisplayStyle.StandardDeviationAndConfidenceInterval;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    // Numeric conversion resolves this provider after the formatter chooses its precision.
    // Observing it detects temporary changes without adding a production test hook.
    sealed class ObservingCulture : CultureInfo
    {
        readonly Action onNumberFormat;

        public ObservingCulture(Action onNumberFormat) : base("en-US")
        {
            this.onNumberFormat = onNumberFormat;
        }

        public override object GetFormat(Type formatType)
        {
            if (formatType == typeof(NumberFormatInfo)) onNumberFormat();
            return base.GetFormat(formatType);
        }
    }

    public void Dispose()
    {
        AppSettings.NumberPrecision = originalPrecision;
        AppSettings.UncertaintyDisplayStyle = originalStyle;
        CultureInfo.CurrentCulture = originalCulture;
    }
}
