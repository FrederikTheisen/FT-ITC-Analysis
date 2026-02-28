using System;
using System.Collections.Generic;
using System.Linq;
using AppKit;
using DataReaders;

namespace AnalysisITC
{
    static class ImportValidator
    {
        const int AlertFirst = 1000;
        const int AlertSecond = 1001;
        const int AlertThird = 1002;

        sealed class ValidationIssue
        {
            public string Message { get; }
            public DataFixProtocol FixProtocol { get; }
            public bool Fixable => FixProtocol != DataFixProtocol.None;

            public ValidationIssue(string message, DataFixProtocol fixProtocol = DataFixProtocol.None)
            {
                Message = message ?? "";
                FixProtocol = fixProtocol;
            }
        }

        public static bool ValidateData(ExperimentData data)
        {
            if (data == null) return false;

            // Integrated heats: allow import, but keep the logic here if you later decide
            // to do minimal sanity checks specific to this format.
            if (data.DataSourceFormat == ITCDataFormat.IntegratedHeats) return true;

            while (true)
            {
                var issue = GetFirstIssue(data);
                if (issue == null) return true;

                using var alert = new NSAlert
                {
                    AlertStyle = NSAlertStyle.Warning,
                    MessageText = "Potential Error Detected: " + data.FileName,
                    InformativeText = issue.Message
                };

                // Button order matters because RunModal returns 1000/1001/1002...
                if (issue.Fixable) alert.AddButton("Attempt Fix");
                alert.AddButton("Discard");
                alert.AddButton("Keep");

                var response = (int)alert.RunModal();

                if (issue.Fixable)
                {
                    switch (response)
                    {
                        case AlertFirst:
                            var fixedData = AttemptDataFix(data, issue.FixProtocol);
                            if (fixedData == null) return false; // fix failed -> discard
                            data = fixedData;
                            continue; // re-validate after fix
                        case AlertSecond:
                            return false;
                        case AlertThird:
                        default:
                            return true;
                    }
                }
                else
                {
                    // Only Discard/Keep are relevant; they are buttons 1000/1001 in this branch,
                    // but we still added 2 or 3 buttons consistently, so map to label semantics.
                    // Since we always add Discard then Keep (and no Attempt Fix here), we can:
                    switch (response)
                    {
                        case AlertFirst:  // Discard
                            return false;
                        case AlertSecond: // Keep
                        default:
                            return true;
                    }
                }
            }
        }

        static ValidationIssue GetFirstIssue(ExperimentData data)
        {
            // Defensive null checks (importers can leave these null).
            var dps = data.DataPoints;
            var injs = data.Injections;

            if (dps == null || dps.Count < 10)
            {
                var n = dps?.Count ?? 0;
                return new ValidationIssue($"Only {n} data points were found (expected > 10).");
            }

            // Avoid flagging self if re-validating an already-added dataset.
            var existingSameName = DataManager.Data
                .FirstOrDefault(d => d.UniqueID != data.UniqueID && d.FileName == data.FileName);

            if (existingSameName != null)
            {
                return new ValidationIssue(
                    $"An experiment with the same file name already exists: \"{existingSameName.FileName}\".\n" +
                    "Attempt fix can rename the incoming dataset to a unique name.",
                    DataFixProtocol.FileExists);
            }

            // This “identical experiment” heuristic is weak if based only on temperature;
            // keep it if it’s useful, but make the warning explicit.
            var existingSameTemp = DataManager.Data
                .FirstOrDefault(d => d.UniqueID != data.UniqueID && d.MeasuredTemperature == data.MeasuredTemperature);

            if (existingSameTemp != null)
            {
                return new ValidationIssue(
                    $"Another dataset has the same measured temperature ({data.MeasuredTemperature:G4} °C): \"{existingSameTemp.FileName}\".\n" +
                    "This may be a duplicate import; verify before keeping.");
            }

            if (injs == null || injs.Count == 0)
            {
                return new ValidationIssue("No injections were found in the file.");
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

            var deltaT = Math.Abs(data.MeasuredTemperature - data.TargetTemperature);
            if (deltaT > 0.5)
            {
                return new ValidationIssue(
                    $"Measured temperature deviates from target by {deltaT:F2} °C.\n" +
                    $"Target: {data.TargetTemperature:G4} °C. Measured: {data.MeasuredTemperature:G4} °C.");
            }

            return null;
        }

        static ExperimentData AttemptDataFix(ExperimentData data, DataFixProtocol fix)
        {
            switch (fix)
            {
                case DataFixProtocol.FileExists: data.IterateFileName(); break;
                case DataFixProtocol.InvalidInjection:
                    var injectiondata = new List<InjectionData>();
                    foreach (var inj in data.Injections)
                    {
                        if (inj.Time > 0)
                            injectiondata.Add(inj);
                    }
                    data.Injections = injectiondata;
                    break;
                case DataFixProtocol.Concentrations: break;
            }

            return data;
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

