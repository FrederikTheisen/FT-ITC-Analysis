using System;
using AnalysisITC.Core.Presentation;
using Xunit;

namespace AnalysisITC.Core.Tests;

public sealed class ReportTimestampFormatterTests
{
    [Theory]
    [InlineData("Europe/Helsinki", 2026, 10, 2, 7, 41, "2 Oct 2026, 10:41 EEST")]
    [InlineData("Europe/Helsinki", 2026, 1, 15, 12, 0, "15 Jan 2026, 14:00 EET")]
    [InlineData("Europe/Copenhagen", 2026, 7, 1, 8, 5, "1 Jul 2026, 10:05 CEST")]
    [InlineData("Europe/Copenhagen", 2026, 12, 1, 8, 5, "1 Dec 2026, 09:05 CET")]
    [InlineData("Europe/London", 2026, 7, 1, 8, 5, "1 Jul 2026, 09:05 BST")]
    [InlineData("Europe/London", 2026, 12, 1, 8, 5, "1 Dec 2026, 08:05 GMT")]
    [InlineData("America/New_York", 2026, 1, 15, 12, 0, "15 Jan 2026, 07:00 EST")]
    [InlineData("America/Los_Angeles", 2026, 7, 15, 12, 0, "15 Jul 2026, 05:00 PDT")]
    [InlineData("Asia/Kolkata", 2026, 1, 15, 12, 0, "15 Jan 2026, 17:30 IST")]
    [InlineData("Australia/Sydney", 2026, 1, 15, 0, 0, "15 Jan 2026, 11:00 AEDT")]
    public void KnownZonesUseStandardAbbreviations(string zoneId, int year, int month, int day,
        int hour, int minute, string expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var utc = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

        Assert.Equal(expected, ReportTimestampFormatter.Format(utc, zone));
    }

    [Theory]
    [InlineData(3, 0, "1 Mar 2026, 15:00 UTC+3")]
    [InlineData(-3, -30, "1 Mar 2026, 08:30 UTC-3:30")]
    [InlineData(5, 45, "1 Mar 2026, 17:45 UTC+5:45")]
    [InlineData(0, 0, "1 Mar 2026, 12:00 UTC")]
    public void UnknownZonesFallBackToCompactOffset(int hours, int minutes, string expected)
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("FT-ITC/Test", new TimeSpan(hours, minutes, 0), "Test", "Test");
        var utc = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(expected, ReportTimestampFormatter.Format(utc, zone));
    }
}
