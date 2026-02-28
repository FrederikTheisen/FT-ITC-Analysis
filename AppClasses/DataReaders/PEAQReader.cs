using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AnalysisITC;
using Utilities;

namespace DataReaders
{
    /// <summary>
    /// Reader for MicroCal PEAQ-ITC Analysis (.apj) files.
    ///
    /// PEAQ analysis exports the entire experiment – raw data, injection table
    /// and fitted results – as an XML document with the extension .apj.
    /// Although rich in content, the ITC analysis program cares primarily
    /// about injection volumes, integrated heats and post‑injection
    /// concentrations.  This reader extracts only the information needed to
    /// build an <see cref="ExperimentData"/> object suitable for FT‑ITC
    /// Analysis.  The thermogram itself is discarded; PEAQ exports provide
    /// normalized heat and DH values which are sufficient for integrated
    /// analysis.
    ///
    /// If additional fields (such as baseline corrected data points or
    /// fitting results) are required in future, extend this class
    /// accordingly.
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
        /// Read a PEAQ .apj file and return an ExperimentData object.
        /// </summary>
        /// <param name="path">Path to the .apj file.</param>
        /// <returns>A populated ExperimentData ready for analysis.</returns>
        public static ExperimentData ReadFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("File not found", path);

            // Load the XML document
            XDocument doc = XDocument.Load(path);
            var analysis = doc.Root;
            if (analysis == null || analysis.Name != "Analysis")
                throw new FormatException("Invalid APJ file: missing <Analysis> root.");

            // For simplicity, we handle only the first experiment in the file.
            // PEAQ exports may contain multiple experiments, but FT‑ITC Analysis
            // currently imports one at a time.
            var experimentElement = analysis.Element("Experiments")?.Element("Experiment");
            if (experimentElement == null) throw new FormatException("No <Experiment> element found in APJ file.");

            // Parse basic metadata
            double cellVolume = ParseDouble(experimentElement.Element("CellVolume")?.Value);
            double cellConcentration = ParseDouble(experimentElement.Element("CellConcentration")?.Value);
            double syringeConcentration = ParseDouble(experimentElement.Element("SyringeConcentration")?.Value);
            double averageTemperature = ParseDouble(experimentElement.Element("AverageTemperature")?.Value);
            double filter = ParseDouble(experimentElement.Element("FilterPeriod")?.Value);
            var instrument = ITCInstrumentAttribute.GetInstrument(experimentElement.Element("CellID")?.Value);
            string comment = experimentElement.Element("Comment")?.Value;
            bool parseddate = DateTime.TryParse(experimentElement.Element("ModifiedDate").Value, out var date);
            double temperature = averageTemperature;
            double initialDelay = 0;
            double stirringSpeed = 0;
            var feedback = FeedbackMode.None;
            double refPower = 5;

            var plannedInjectionScheme = new List<InjectionData>();
            var methodElement = experimentElement.Element("Method");

            if (methodElement != null)
            {
                temperature = ParseDouble(methodElement.Element("Temperature")?.Value);
                initialDelay = ParseDouble(methodElement.Element("InitialDelay")?.Value);
                stirringSpeed = ParseDouble(methodElement.Element("StirSpeed")?.Value);
                feedback = methodElement.Element("Feedback")?.Value.ToLower() switch
                {
                    "high" => FeedbackMode.High,
                    "low" => FeedbackMode.Low,
                    "none" => FeedbackMode.None,
                    _ => FeedbackMode.None,
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
                DataSourceFormat = ITCDataFormat.IntegratedHeats,
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
                    var dp = new DataPoint((float)tSample, powerJ, temp: (float)dp_temp, shieldt: (float)experiment.TargetTemperature);
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
                    .OrderBy(elem => ParseInt(elem.Element("Index")?.Value));
                foreach (var injElem in injElements)
                {
                    int id = ParseInt(injElem.Element("Index")?.Value);
                    double vol = ParseDouble(injElem.Element("Volume")?.Value);
                    double startTime = ParseDouble(injElem.Element("StartTime")?.Value);
                    double duration = ParseDouble(injElem.Element("Duration")?.Value);
                    bool include = ParseBool(injElem.Element("IsValid")?.Value);

                    var plannedInj = plannedInjectionScheme[id];

                    // Build CSV string for InjectionData constructor.
                    // Format: id,Include,Time,Volume,Delay,Duration,Temperature,IntegrationStartDelay,IntegrationLength
                    // Use StartTime for both the Time and Delay fields.  Temperature is the average experiment temperature.
                    string csv = string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5},{6},0,0",
                        id,
                        include ? "1" : "0",
                        startTime,
                        vol,
                        plannedInj.Delay,
                        duration,
                        experiment.DataPoints.First(dp => dp.Time > startTime).Temperature);

                    var inj = new InjectionData(experiment, csv);
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
            return double.Parse(s.Trim(), CultureInfo.InvariantCulture);
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return int.Parse(s.Trim(), CultureInfo.InvariantCulture);
        }

        private static bool ParseBool(string s, bool fallback = true)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            return s.ToLower() == "true";
        }
    }
}