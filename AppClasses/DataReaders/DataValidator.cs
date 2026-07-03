using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.Platform;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;
using AnalysisITC.Core.Utilities;

namespace AnalysisITC.Core.DataReaders
{
    public static class ImportValidator
    {
        sealed class ValidationIssue
        {
            public string Message { get; }
            public DataFixProtocol FixProtocol { get; }
            public bool Fixable => FixProtocol != DataFixProtocol.None;
            public bool RequiresInput => FixProtocol == DataFixProtocol.Concentrations;

            public ValidationIssue(string message, DataFixProtocol fixProtocol = DataFixProtocol.None)
            {
                Message = message ?? "";
                FixProtocol = fixProtocol;
            }
        }

        public static bool ValidateData(ExperimentData data)
        {
            if (data == null) return false;

            while (true)
            {
                var issue = GetFirstIssue(data);
                if (issue == null) return true;

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
            if (data.DataSourceFormat != ITCDataFormat.IntegratedHeats)
            {
                var dps = data.DataPoints;
                

                if (dps == null || dps.Count < 10)
                {
                    var n = dps?.Count ?? 0;
                    return new ValidationIssue($"Only {n} data points were found (expected > 10).");
                }

                // Optimize the original O(Ninj * Ndps) check to O(Ninj).
                var maxDataTime = dps.Max(dp => dp.Time);
                if (injs.All(inj => (inj.Time + 10) >= maxDataTime))
                {
                    var firstInj = injs.Min(i => i.Time);
                    var lastInj = injs.Max(i => i.Time);
                    return new ValidationIssue(
                        "All injections appear to occur outside (or at the very end of) the recorded data range.\n" +
                        $"Last data point: {maxDataTime:G4} s. Injection time range: {firstInj:G4}–{lastInj:G4} s.\n" +
                        "Attempt fix can remove problematic injections.",
                        DataFixProtocol.InvalidInjection);
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

            if (data.CellConcentration > data.SyringeConcentration)
            {
                return new ValidationIssue(
                    $"The syringe concentration ({data.SyringeConcentration.AsConcentration(ConcentrationUnit.µM, true)}) appears to be lower than the cell concentration ({data.CellConcentration.AsConcentration(ConcentrationUnit.µM, true)}).\n" +
                    "There may be an error in the concentrations.\n\n" +
                    $"Provide an updated syringe concentration (default unit: {AppSettings.DefaultConcentrationUnit}) here:",
                    DataFixProtocol.Concentrations);
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
                    case DataFixProtocol.InvalidInjection:
                        var injectiondata = new List<InjectionData>();
                        foreach (var inj in data.Injections)
                        {
                            if (inj.Time > 0)
                                injectiondata.Add(inj);
                        }
                        data.Injections = injectiondata;
                        break;
                    case DataFixProtocol.Concentrations:
                        FloatWithError conc;
                        var b = ConcentrationParser.TryParseMolarConcentration(inputValue ?? "", out conc);

                        if (b)
                        {
                            AppEventHandler.Print(conc.ToString());
                            data.SyringeConcentration = conc;

                            // We need to recalculate concentrations
                            RawDataReader.ProcessInjections(data);
                        }
                        break;
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        enum DataFixProtocol
        {
            None,
            FileExists,
            InvalidInjection,
            Concentrations,
        }
    }
}
