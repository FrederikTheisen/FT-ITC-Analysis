using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Interpretation;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

[CollectionDefinition("Local date presentation", DisableParallelization = true)]
public sealed class LocalDatePresentationCollection { }

[Collection("Local date presentation")]
public sealed class LocalDateTimeFormatterTests
{
    static readonly DateTime Date = new(2026, 11, 23, 17, 42, 31, DateTimeKind.Unspecified);

    static void WithCulture(string locale, Action test, string uiCulture = "en-GB")
    {
        var originalLocale = AppSettings.Locale;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            AppSettings.Locale = locale;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(uiCulture);
            test();
        }
        finally
        {
            AppSettings.Locale = originalLocale;
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    static string Normalize(string value) => Regex.Replace(value, @"\s+", " ").Trim();

    [Theory]
    [InlineData("da-DK", 23, 11, 2026, '.', false)]
    [InlineData("en-GB", 23, 11, 2026, '/', false)]
    [InlineData("en-US", 11, 23, 2026, '/', true)]
    [InlineData("sv-SE", 2026, 11, 23, '-', false)]
    public void DatesAndTimesUseRegionalComponentsDespiteUsProcessCulture(
        string locale, int first, int second, int third, char separator, bool twelveHour)
        => WithCulture(locale, () =>
        {
            var date = Normalize(LocalDateTimeFormatter.ShortDate(Date));
            Assert.Equal(new[] { first, second, third }, Regex.Matches(date, @"\d+")
                .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray());
            Assert.Equal(new string(separator, 2), Regex.Replace(date, @"\d|\s", ""));
            var time = Normalize(LocalDateTimeFormatter.ShortTime(Date));
            var numbers = Regex.Matches(time, @"\d+").Select(match => int.Parse(match.Value)).ToArray();
            Assert.Equal(new[] { twelveHour ? 5 : 17, 42 }, numbers);
            Assert.Equal(twelveHour, time.Contains("PM", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain("AM", time, StringComparison.OrdinalIgnoreCase);
            var container = new ITCDataContainer { Date = Date };
            Assert.Equal(date + " " + time, Normalize(container.UIShortDateWithTime));
            Assert.Equal(Date, container.Date); // Display does not change the recorded clock or kind.
        });

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid@locale!")]
    public void InvalidOrEmptyLocaleFallsBackToUiCulture(string locale) => WithCulture(locale, () =>
    {
        Assert.Equal("en-GB", LocalDateTimeFormatter.ResolveCulture().Name);
        Assert.Equal("23/11/2026", Normalize(LocalDateTimeFormatter.ShortDate(Date)));
    });

    [Fact]
    public void InvariantUiCultureIsSafeFallback() => WithCulture("invalid@locale!", () =>
    {
        Assert.Equal(CultureInfo.InvariantCulture, LocalDateTimeFormatter.ResolveCulture());
        Assert.NotEmpty(LocalDateTimeFormatter.LongDateTime(Date));
    }, "");

    [Fact]
    public void LongDateNamesFollowDanishRegionInEnglishUi() => WithCulture("da-DK", () =>
    {
        var text = Normalize(new ITCDataContainer { Date = Date }.UILongDateWithTime).ToLowerInvariant();
        Assert.Contains("mandag", text);
        Assert.Contains("november", text);
        Assert.DoesNotContain("monday", text);
        Assert.Contains("17", text);
        Assert.Contains("42", text);
        Assert.Contains("31", text);
    });

    [Theory]
    [InlineData("da-DK")]
    [InlineData("en-GB")]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    public void AccountDatesShareDisplayFormatterAndReportTimestampRemainsUnchanged(string locale)
        => WithCulture(locale, () =>
        {
            var utc = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
            var account = new InterpretationAccountResponse
            {
                ExpiresAtUtc = utc,
                Usage = new InterpretationAccountUsage { Limited = true, ResetsAtUtc = utc }
            };
            var rows = InterpretationAccessDisplay.PreferenceAccountDetailRows(account);
            var expected = Normalize(LocalDateTimeFormatter.ShortDate(utc.ToLocalTime()));
            Assert.Equal(expected, Normalize(rows.Single(row => row.Label == "Expiry").Value));
            Assert.EndsWith("resets " + expected, Normalize(rows.Single(row => row.Label == "Quota").Value));
            Assert.Equal("23 Nov 2026, 12:00 UTC", Normalize(ReportTimestampFormatter.Format(utc, TimeZoneInfo.Utc)));
            var report = new AnalysisReportDocument { GeneratedAtUtc = utc };
            Assert.Equal(ReportTimestampFormatter.Format(utc), report.ExportDateText);
        });
}
