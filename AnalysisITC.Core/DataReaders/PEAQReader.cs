using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AnalysisITC.Core.Utilities;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
{
    /// <summary>
    /// Reader for MicroCal PEAQ-ITC Analysis (.apj) files.
    ///
    /// PEAQ analysis exports the entire experiment – raw data, injection table
    /// and fitted results – as an XML document with the extension .apj.
    /// This reader restores the raw thermogram and injection information needed
    /// to build an <see cref="ExperimentData"/> object suitable for FT‑ITC
    /// Analysis. PEAQ fitting results and integrated heats are not imported.
    ///
    /// The units in the .apj export are:
    /// * CellVolume: litres (e.g. 0.0002136 for 213.6 µL)
    /// * Volume (injection): litres
    /// * DH: joules (integrated heat per injection)
    /// * NDH: joules per mole (normalized injection heat)
    /// * Concentrations: molar (M)
    /// </summary>
    public static class PEAQReader
    {
        /// <summary>
        /// Read a PEAQ .apj file and return one ExperimentData object per experiment.
        /// </summary>
        /// <param name="path">Path to the .apj file.</param>
        /// <returns>Populated experiments in file order, ready for analysis.</returns>
        public static ExperimentData[] ReadFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            // Load the XML document
            XDocument doc = XDocument.Load(path);
            var analysis = doc.Root;
            if (analysis == null || analysis.Name != "Analysis")
                throw new FormatException("Invalid APJ file: missing <Analysis> root.");

            var experimentElements = analysis.Element("Experiments")?.Elements("Experiment").ToList();
            if (experimentElements == null || experimentElements.Count == 0)
                throw new FormatException("No <Experiment> element found in APJ file.");

            // A single-experiment file keeps the file name; multiple experiments use their PEAQ names.
            if (experimentElements.Count == 1)
                return new[] { ReadExperiment(experimentElements[0], path) };

            // Keep every readable experiment; log the ones that fail.
            var experiments = new List<ExperimentData>();
            for (int i = 0; i < experimentElements.Count; i++)
            {
                var name = experimentElements[i].Element("Name")?.Value;
                if (string.IsNullOrWhiteSpace(name))
                    name = $"{Path.GetFileNameWithoutExtension(path)} {i + 1}";

                try
                {
                    var experiment = ReadExperiment(experimentElements[i], path);
                    experiment.Name = name.Trim();
                    experiments.Add(experiment);
                }
                catch (Exception ex)
                {
                    AppEventHandler.PrintAndLog($"Skipped APJ experiment {i + 1} (\"{name.Trim()}\"): {ex.Message}");
                }
            }

            if (experiments.Count == 0)
                throw new FormatException("No experiment in the APJ file could be read.");

            return experiments.ToArray();
        }

        static ExperimentData ReadExperiment(XElement experimentElement, string path)
        {
            // Parse basic metadata
            double cellVolume = ParseDouble(experimentElement.Element("CellVolume")?.Value);
            double cellConcentration = ParseDouble(experimentElement.Element("CellConcentration")?.Value);
            double syringeConcentration = ParseDouble(experimentElement.Element("SyringeConcentration")?.Value);
            double averageTemperature = ParseDouble(experimentElement.Element("AverageTemperature")?.Value);
            double filter = ParseDouble(experimentElement.Element("FilterPeriod")?.Value);
            var instrument = ITCInstrumentAttribute.TryResolveMicroCalInstrument(experimentElement.Element("CellID")?.Value);
            string comment = experimentElement.Element("Comment")?.Value;
            bool parseddate = DateTime.TryParse(
                experimentElement.Element("ModifiedDate")?.Value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var date);
            double temperature = averageTemperature;
            double initialDelay = 0;
            double stirringSpeed = 0;
            var feedback = FeedbackMode.Null;
            double refPower = 5;

            var plannedInjectionScheme = new List<InjectionData>();
            var methodElement = experimentElement.Element("Method");

            if (methodElement != null)
            {
                temperature = ParseDouble(methodElement.Element("Temperature")?.Value);
                initialDelay = ParseDouble(methodElement.Element("InitialDelay")?.Value);
                stirringSpeed = ParseDouble(methodElement.Element("StirSpeed")?.Value);
                feedback = methodElement.Element("Feedback")?.Value.ToLowerInvariant() switch
                {
                    "high" => FeedbackMode.High,
                    "low" => FeedbackMode.Low,
                    "none" => FeedbackMode.None,
                    _ => FeedbackMode.Null,
                };
                refPower = ParseDouble(methodElement.Element("ReferencePower")?.Value);

                // Initialize injections
                var elements = methodElement.Elements("Injections");
                foreach (var injElem in elements.Elements("Injection"))
                {
                    plannedInjectionScheme.Add(new InjectionData(
                        null,
                        volume: (float)ParseDouble(injElem.Element("Volume")?.Value),
                        delay: (float)ParseDouble(injElem.Element("Spacing")?.Value),
                        filter: (float)ParseDouble(injElem.Element("FilterPeriod")?.Value),
                        duration: (float)ParseDouble(injElem.Element("Duration")?.Value)
                        ));
                }
            }

            // Create the ExperimentData object and populate basic fields
            var experiment = new ExperimentData(Path.GetFileName(path))
            {
                Date = parseddate ? date : File.GetCreationTime(path),
                DateSource = parseddate ? ExperimentDateSource.DataFile : ExperimentDateSource.FileSystem,
                DataSourceFormat = ITCDataFormat.PEAQITCProject,
                CellVolume = cellVolume,
                CellConcentration = new FloatWithError((float)cellConcentration),
                SyringeConcentration = new FloatWithError((float)syringeConcentration),
                TargetTemperature = (float)temperature,
                Comments = comment,
                Instrument = instrument,
                FeedBackMode = feedback,
                InitialDelay = initialDelay,
                StirringSpeed = stirringSpeed,
                TargetPowerDiff = refPower
            };

            // Parse raw data points if present.  PEAQ exports store the thermogram under <DataPoints> with
            // child <P> elements containing attributes T (sample index), DP (power, µcal/s) and Tmp (temperature).
            var dpList = new List<DataPoint>();
            var dpElement = experimentElement.Element("DataPoints");
            if (dpElement != null)
            {
                foreach (var p in dpElement.Elements("P"))
                {
                    var time = p.Attribute("T")?.Value;
                    var power = p.Attribute("DP")?.Value;
                    var temp = p.Attribute("Tmp")?.Value;
                    if (time == null || power == null) continue;
                    double tSample = ParseDouble(time);
                    double dpValue = ParseDouble(power);
                    double dp_temp = !string.IsNullOrWhiteSpace(temp) ? ParseDouble(temp) : averageTemperature;

                    // Convert the power value to joules using the Energy.ConvertToJoule utility.
                    float powerJ = (float)Energy.ConvertToJoule(dpValue, EnergyUnit.Cal);

                    // Construct DataPoint with named parameter 'temp' for temperature
                    var dp = new DataPoint((float)tSample, powerJ, temp: (float)dp_temp);
                    dpList.Add(dp);
                }
            }

            experiment.DataPoints = dpList;

            // Build injection list
            var injections = new List<InjectionData>();
            var injectionsElement = experimentElement.Element("Injections");
            if (injectionsElement != null)
            {
                // Sort injections by index to ensure correct order
                var injElements = injectionsElement.Elements("Injection")
                    .OrderBy(elem => ParseInt(elem.Element("Index")?.Value))
                    .ToList();
                for (int i = 0; i < injElements.Count; i++)
                {
                    var injElem = injElements[i];
                    int id = ParseInt(injElem.Element("Index")?.Value);
                    double vol = ParseDouble(injElem.Element("Volume")?.Value);
                    double startTime = ParseDouble(injElem.Element("StartTime")?.Value);
                    double duration = ParseDouble(injElem.Element("Duration")?.Value);
                    bool include = ParseBool(injElem.Element("IsValid")?.Value);

                    // Use the planned spacing; without it, use the time until the next recorded injection or the trace end.
                    double delay = id >= 0 && id < plannedInjectionScheme.Count ? plannedInjectionScheme[id].Delay : 0;
                    if (delay <= 0)
                    {
                        double nextTime = i + 1 < injElements.Count
                            ? ParseDouble(injElements[i + 1].Element("StartTime")?.Value)
                            : dpList.Count > 0 ? dpList[dpList.Count - 1].Time : startTime;
                        delay = nextTime - startTime;
                    }

                    // Injections beyond the trace are left for import validation to report.
                    var temperatureIndex = experiment.DataPoints.FindIndex(dp => dp.Time > startTime);
                    var injectionTemperature = temperatureIndex >= 0
                        ? experiment.DataPoints[temperatureIndex].Temperature
                        : experiment.DataPoints.Count > 0 ? experiment.DataPoints[experiment.DataPoints.Count - 1].Temperature : averageTemperature;
                    var inj = InjectionData.FromPEAQFile(
                        experiment,
                        id,
                        include,
                        startTime,
                        vol,
                        delay,
                        duration,
                        injectionTemperature);

                    inj.InitializeIntegrationTimes();

                    injections.Add(inj);
                }
            }

            experiment.Injections = injections;

            // Post‑process injections and data.  This computes integration windows and derived concentrations,
            // and also uses the raw data points to determine measured temperature and baseline.
            RawDataReader.ProcessInjections(experiment);
            RawDataReader.ProcessExperiment(experiment);

            return experiment;
        }

        private static double ParseDouble(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0.0;
            var value = double.Parse(s.Trim(), CultureInfo.InvariantCulture);
            if (!FWEMath.IsFinite(value))
                throw new FormatException($"The file contains a value that is not a finite number: '{s.Trim()}'.");
            return value;
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return int.Parse(s.Trim(), CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string s, bool fallback = true)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return s.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }
}
