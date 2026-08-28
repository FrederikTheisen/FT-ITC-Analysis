using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AnalysisITC.Platform;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.DataReaders
{
    internal sealed class AutomaticImportActionReport
    {
        public AutomaticImportActionReport(string experimentName, int discardedOrphanInjectionCount)
        {
            ExperimentName = experimentName ?? "";
            DiscardedOrphanInjectionCount = Math.Max(0, discardedOrphanInjectionCount);
        }

        public string ExperimentName { get; }
        public int DiscardedOrphanInjectionCount { get; }
    }

    public static class ImportValidator
    {
        sealed class ValidationIssue
        {
            public string Message { get; }
            public DataFixProtocol FixProtocol { get; }
            public bool Fixable => FixProtocol != DataFixProtocol.None;
            public bool RequiresInput => FixProtocol == DataFixProtocol.CellVolume
                || FixProtocol == DataFixProtocol.CellConcentration
                || FixProtocol == DataFixProtocol.SyringeConcentration;
            public bool IsOrphanInjectionIssue => FixProtocol == DataFixProtocol.OrphanInjection;

            public ValidationIssue(string message, DataFixProtocol fixProtocol = DataFixProtocol.None)
            {
                Message = message ?? "";
                FixProtocol = fixProtocol;
            }
        }

        public static bool ValidateData(ExperimentData data) =>
            ValidateData(data, allowAutomaticActions: false, automaticActionReports: null);

        internal static bool ValidateData(
            ExperimentData data,
            bool allowAutomaticActions,
            ICollection<AutomaticImportActionReport> automaticActionReports)
        {
            if (data == null) return false;

            while (true)
            {
                var issue = GetFirstIssue(data);
                if (issue == null) return true;

                if (issue.IsOrphanInjectionIssue
                    && allowAutomaticActions
                    && !data.IsTandemExperiment
                    && AppSettings.AutomaticallyDiscardOrphanInjectionsOnLoad)
                {
                    var discardedCount = DiscardOrphanInjections(data);
                    if (discardedCount > 0)
                    {
                        RawDataReader.ProcessInjections(data);
                        automaticActionReports?.Add(new AutomaticImportActionReport(data.Name, discardedCount));
                        AppEventHandler.PrintAndLog(
                            $"Automatically discarded {discardedCount} orphan injection(s) while loading {data.Name}.");
                        continue;
                    }
                }

                var response = PlatformServices.DataValidationPromptService.AskValidationIssue(
                    "Potential Error Detected: " + data.Name,
                    issue.Message,
                    issue.Fixable,
                    issue.RequiresInput);

                if (issue.Fixable)
                {
                    switch (response.Action)
                    {
                        case DataValidationPromptAction.AttemptFix:
                            var fixedData = AttemptDataFix(data, issue.FixProtocol, response.Input);
                            if (fixedData == null) return false; // fix failed -> discard
                            data = fixedData;
                            continue; // re-validate after fix
                        case DataValidationPromptAction.Discard:
                            return false;
                        case DataValidationPromptAction.Keep:
                        default:
                            return true;
                    }
                }
                else
                {
                    switch (response.Action)
                    {
                        case DataValidationPromptAction.Discard:
                            return false;
                        case DataValidationPromptAction.Keep:
                        default:
                            return true;
                    }
                }
            }
        }

        static ValidationIssue GetFirstIssue(ExperimentData data)
        {
            var injs = data.Injections;

            // Defensive null checks (importers can leave these null).
            var originWithoutTrace = data.DataSourceFormat == ITCDataFormat.OriginProject
                && (data.DataPoints == null || data.DataPoints.Count < 10);
            if (data.DataSourceFormat != ITCDataFormat.IntegratedHeats && !originWithoutTrace)
            {
                var dps = data.DataPoints;

                if (dps == null || dps.Count < 10)
                {
                    var n = dps?.Count ?? 0;
                    return new ValidationIssue($"Only {n} data points were found (expected > 10).");
                }

                if (injs != null && injs.Count > 0)
                {
                    var minDataTime = dps.Min(dp => dp.Time);
                    var maxDataTime = dps.Max(dp => dp.Time);
                    var orphanCount = injs.Count(inj => IsOrphanInjection(inj, minDataTime, maxDataTime));

                    if (orphanCount > 0)
                    {
                        return new ValidationIssue(
                            $"{orphanCount} injection marker(s) occur outside the recorded thermogram range " +
                            $"({minDataTime:G4}–{maxDataTime:G4} s).\n" +
                            "Attempt fix can remove the orphan injection markers.",
                            DataFixProtocol.OrphanInjection);
                    }

                    // Optimize the original O(Ninj * Ndps) check to O(Ninj).
                    if (injs.All(inj => (inj.Time + 10) >= maxDataTime))
                    {
                        var firstInj = injs.Min(i => i.Time);
                        var lastInj = injs.Max(i => i.Time);
                        return new ValidationIssue(
                            "All injections appear to occur at the very end of the recorded data range.\n" +
                            $"Last data point: {maxDataTime:G4} s. Injection time range: {firstInj:G4}–{lastInj:G4} s.\n" +
                            "Attempt fix can remove problematic injections.",
                            DataFixProtocol.InvalidInjection);
                    }
                }
            }

            if (injs == null || injs.Count == 0)
            {
                return new ValidationIssue("No injections were found in the file.");
            }

            // Avoid flagging self if re-validating an already-added dataset.
            var existingSameName = DataManager.Data
                .FirstOrDefault(d => d.UniqueID != data.UniqueID && d.Name == data.Name);

            if (existingSameName != null)
            {
                return new ValidationIssue(
                    $"An experiment with the same name already exists: \"{existingSameName.Name}\".\n" +
                    "Attempt fix can rename the incoming dataset to a unique name.",
                    DataFixProtocol.FileExists);
            }

            var existingSameFile = DataManager.Data
                .FirstOrDefault(d => d.UniqueID != data.UniqueID && d.FileName == data.FileName && d.Name == data.Name);

            if (existingSameFile != null)
            {
                return new ValidationIssue(
                    $"An experiment with the same file name and name already exists: \"{existingSameFile.FileName}\".\n" +
                    "Attempt fix can rename the incoming dataset to a unique name.",
                    DataFixProtocol.FileExists);
            }

            var negative = injs.Where(inj => inj.Time < 0).ToList();
            if (negative.Count > 0)
            {
                var example = negative[0];
                return new ValidationIssue(
                    $"{negative.Count} injection(s) have negative time (example: #{example.ID + 1} at {example.Time:G4} s).\n" +
                    "This usually indicates an injection table that is not aligned to the recorded data.\n" +
                    "Attempt fix can remove invalid injections.",
                    DataFixProtocol.InvalidInjection);
            }

            var deltaT = Math.Abs(data.MeasuredTemperature - data.TargetTemperature);
            if (deltaT > AppSettings.MinimumTemperatureSpanForFitting) // Probably 2C if not changed by user
            {
                return new ValidationIssue(
                    $"Measured temperature deviates from target by {deltaT:F2} °C.\n" +
                    $"Target: {data.TargetTemperature:G4} °C. Measured: {data.MeasuredTemperature:G4} °C.");
            }

            if (data.DataSourceFormat == ITCDataFormat.IntegratedHeats)
            {
                if (!FWEMath.IsFinite(data.CellVolume) || data.CellVolume <= 0)
                {
                    return new ValidationIssue(
                        "The cell volume could not be inferred from the imported Mt trajectory.\n" +
                        "The heat and injection-volume data can still be used after the missing metadata is supplied.\n\n" +
                        "Provide the cell volume here (accepted units: L, mL, µL; unitless values are interpreted as µL):",
                        DataFixProtocol.CellVolume);
                }

                if (!FWEMath.IsFinite(data.CellConcentration.Value) || data.CellConcentration.Value < 0)
                {
                    return new ValidationIssue(
                        "The initial cell concentration could not be read from the imported Mt trajectory.\n" +
                        "The heat and injection-volume data can still be used after the missing metadata is supplied.\n\n" +
                        $"Provide the cell concentration here (default unit: {AppSettings.DefaultConcentrationUnit}):",
                        DataFixProtocol.CellConcentration);
                }

                if (!FWEMath.IsFinite(data.SyringeConcentration.Value) || data.SyringeConcentration.Value < 0)
                {
                    return new ValidationIssue(
                        "The syringe concentration could not be inferred from the imported Xt trajectory.\n" +
                        "The heat and injection-volume data can still be used after the missing metadata is supplied.\n\n" +
                        $"Provide the syringe concentration here (default unit: {AppSettings.DefaultConcentrationUnit}):",
                        DataFixProtocol.SyringeConcentration);
                }
            }

            if (data.CellConcentration > data.SyringeConcentration)
            {
                return new ValidationIssue(
                    $"The syringe concentration ({data.SyringeConcentration.AsConcentration(ConcentrationUnit.µM, true)}) appears to be lower than the cell concentration ({data.CellConcentration.AsConcentration(ConcentrationUnit.µM, true)}).\n" +
                    "There may be an error in the concentrations.\n\n" +
                    $"Provide an updated syringe concentration (default unit: {AppSettings.DefaultConcentrationUnit}) here:",
                    DataFixProtocol.SyringeConcentration);
            }

            return null;
        }

        static ExperimentData AttemptDataFix(ExperimentData data, DataFixProtocol fix, string inputValue)
        {
            try
            {
                switch (fix)
                {
                    case DataFixProtocol.FileExists: data.IterateCopyName(); break;
                    case DataFixProtocol.OrphanInjection:
                        DiscardOrphanInjections(data);
                        RawDataReader.ProcessInjections(data);
                        break;
                    case DataFixProtocol.InvalidInjection:
                        var injectiondata = new List<InjectionData>();
                        foreach (var inj in data.Injections)
                        {
                            if (inj.Time > 0)
                                injectiondata.Add(inj);
                        }
                        data.Injections = injectiondata;
                        break;
                    case DataFixProtocol.CellVolume:
                        if (TryParseCellVolumeLiters(inputValue, out var cellVolume))
                            data.CellVolume = cellVolume;
                        ReprocessIfResolved(data);
                        break;
                    case DataFixProtocol.CellConcentration:
                        if (TryParseConcentration(inputValue, out var cellConcentration))
                            data.CellConcentration = cellConcentration;
                        ReprocessIfResolved(data);
                        break;
                    case DataFixProtocol.SyringeConcentration:
                        if (TryParseConcentration(inputValue, out var syringeConcentration))
                            data.SyringeConcentration = syringeConcentration;
                        ReprocessIfResolved(data);
                        break;
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        static bool TryParseConcentration(string inputValue, out FloatWithError concentration)
        {
            if (!ConcentrationParser.TryParseMolarConcentration(inputValue ?? "", out concentration))
                return false;

            return FWEMath.IsFinite(concentration.Value) && concentration.Value >= 0;
        }

        static bool TryParseCellVolumeLiters(string inputValue, out double liters)
        {
            liters = double.NaN;
            if (string.IsNullOrWhiteSpace(inputValue)) return false;

            var normalized = inputValue.Trim().Replace('µ', 'u').Replace('μ', 'u').ToLowerInvariant();
            var match = Regex.Match(
                normalized,
                @"^([-+]?(?:\d+(?:[.,]\d+)?|[.,]\d+)(?:e[-+]?\d+)?)\s*([a-z]*)$");
            if (!match.Success) return false;
            if (!double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return false;

            var scale = match.Groups[2].Value switch
            {
                "l" => 1.0,
                "ml" => 1e-3,
                "ul" => 1e-6,
                "" => 1e-6,
                _ => double.NaN,
            };
            liters = value * scale;
            return FWEMath.IsFinite(liters) && liters > 0;
        }

        static void ReprocessIfResolved(ExperimentData data)
        {
            if (IntegratedHeatReader.HasResolvedConcentrationMetadata(data))
                RawDataReader.ProcessInjections(data);
        }

        static int DiscardOrphanInjections(ExperimentData data)
        {
            if (data?.Injections == null || data.DataPoints == null || data.DataPoints.Count == 0)
                return 0;

            var minDataTime = data.DataPoints.Min(point => point.Time);
            var maxDataTime = data.DataPoints.Max(point => point.Time);
            return data.Injections.RemoveAll(injection => IsOrphanInjection(injection, minDataTime, maxDataTime));
        }

        static bool IsOrphanInjection(InjectionData injection, float minDataTime, float maxDataTime)
        {
            return injection != null && (injection.Time < minDataTime || injection.Time > maxDataTime);
        }

        enum DataFixProtocol
        {
            None,
            FileExists,
            OrphanInjection,
            InvalidInjection,
            CellVolume,
            CellConcentration,
            SyringeConcentration,
        }
    }
}
