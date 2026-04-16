using System;
using System.Linq;

namespace AnalysisITC
{
    public class InjectionData
    {
        public ExperimentData Experiment { get; private set; }
        public int ID { get; private set; }

        float time = -1;

        public float Time { get => time; set { time = value; InitializeIntegrationTimes(); } }
        public double Volume { get; private set; }
        public float Duration { get; private set; }
        public float Delay { get; private set; }
        public float Filter { get; private set; } = 5;
        public double Temperature { get; set; }
        public double InjectionMass => Experiment.SyringeConcentration * this.Volume; // { get; set; }

        public double ActualCellConcentration { get; set; }
        public double ActualTitrantConcentration { get; set; }

        /// <summary>
        /// The X axis variable to plot
        /// </summary>
        public double Ratio { get; set; }

        bool include = true;
        public bool Include
        {
            get => include;
            set
            {
                if (include == value) return;

                include = value;
                Experiment?.MarkModified();
            }
        }
        public float IntegrationStartDelay { get; private set; } = 0;
        public float IntegrationEndOffset { get; private set; } = 90;
        public float IntegrationStartTime => Time + IntegrationStartDelay;
        public float IntegrationEndTime => Time + IntegrationEndOffset;
        public float IntegrationLength => IntegrationEndOffset - IntegrationStartDelay;
        float MinimumIntegrationTime => 2 * (float)Experiment.TimeStep;

        public PeakHeatDirection HeatDirection { get; set; } = PeakHeatDirection.Unknown;

        public bool IsIntegrated { get; set; } = false;
        public FloatWithError RawPeakArea { get; private set; } = new();
        public FloatWithError PeakArea { get; private set; } = new();
        public Energy Enthalpy2 => new(PeakArea / InjectionMass);
        public double Enthalpy => PeakArea / InjectionMass;
        public double SD => PeakArea.SD / InjectionMass;

        static bool IsValidReferenceInjection(InjectionData inj) => inj != null && inj.Include && inj.IsIntegrated;

        public double OffsetEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return Enthalpy;
                //else return Enthalpy - Experiment.Solution.Offset; //A1 pattern
                else return Enthalpy - Experiment.Solution.Offset;
            }
        }

        public double ResidualEnthalpy
        {
            get
            {
                if (Experiment.Solution == null) return 0;
                if (InjectionMass == 0) return 0;

                return Experiment.Model.Residual(this) / InjectionMass;
            }
        }

        private InjectionData() { }

        public static InjectionData FromTAFileLine(ExperimentData experiment, int id, double v, DataPoint dp, InjectionData prev)
        {
            float delay = 0.0f;
            if (prev != null)
            {
                delay = dp.Time - prev.Time;
                prev.Delay = delay;
            }


            return new InjectionData()
            {
                Experiment = experiment,
                ID = id,
                Include = id != 0,
                Volume = v,
                Time = dp.Time,
                Temperature = dp.Temperature,
                Delay = delay, // Delay guess until next injection is processed
                Duration = 2 * (float)v, // Not known
                Filter = 0.0f, // Not known, not used
            };
        }

        public static InjectionData FromFTITCLine(ExperimentData experiment, string line)
        {
            var parameters = line.Split(',');

            var inj = new InjectionData()
            {
                Experiment = experiment,
                ID = FTITCFormat.IParse(parameters[0]),
                Include = parameters[1] == "1",
                Time = FTITCFormat.FParse(parameters[2]),
                Volume = FTITCFormat.DParse(parameters[3]),
                Delay = FTITCFormat.FParse(parameters[4]),
                Duration = FTITCFormat.FParse(parameters[5]),
                Temperature = FTITCFormat.DParse(parameters[6]),
                IntegrationStartDelay = FTITCFormat.FParse(parameters[7]),
                IntegrationEndOffset = FTITCFormat.FParse(parameters[8]),
            };

            // Newer files contain additional information for the injections to handle tandem experiment data
            if (parameters.Count() >= 11)
            {
                inj.ActualCellConcentration = FTITCFormat.DParse(parameters[9]);
                inj.ActualTitrantConcentration = FTITCFormat.DParse(parameters[10]);
                inj.Ratio = inj.ActualTitrantConcentration / inj.ActualCellConcentration;
            }

            // Newer files contain additional information
            if (parameters.Count() >= 13)
            {
                var peakarea = FTITCFormat.DParse(parameters[11]);
                var peaksd = FTITCFormat.DParse(parameters[12]);

                inj.SetPeakArea(new FloatWithError(peakarea, peaksd));
            }

            return inj;
        }

        public static InjectionData FromPEAQFile(ExperimentData experiment, int id, bool include, double time, double volume, double delay, double duration, double temperature)
        {
            return new InjectionData()
            {
                Experiment = experiment,
                ID = id,
                Include = include,
                Time = (float)time,
                Volume = volume,
                Delay = (float)delay,
                Duration = (float)duration,
                Temperature = temperature
            };
        }

        public InjectionData(ExperimentData experiment, double volume)
        {
            Experiment = experiment;

            ID = experiment.InjectionCount;
            Volume = volume;
            Time = ID; // No meaningful time
        }

        public InjectionData(ExperimentData experiment, int id, double volume, double mass, bool include)
        {
            Experiment = experiment;
            ID = id;
            Volume = volume;
            //InjectionMass = mass;
            Include = include;
        }

        public InjectionData(ExperimentData experiment, string line, int id)
        {
            Experiment = experiment;

            ID = id;

            var data = line.Substring(1).Split(',');

            Volume = float.Parse(data[0]) / 1000000f;
            Duration = float.Parse(data[1]);
            Delay = float.Parse(data[2]);
            Filter = float.Parse(data[3]);
            Include = ID > 0;
        }

        public InjectionData(ExperimentData data, float volume, float delay, float filter, float duration)
        {
            Experiment = data;
            Volume = volume;
            Delay = delay;
            Filter = filter;
            Duration = duration;
        }

        private InjectionData(ExperimentData data, int id, float time, double volume, float delay, float duration, double temp)
        {
            Experiment = data;
            ID = id;
            Time = time;
            Volume = volume;
            Duration = duration;
            Temperature = temp;
            Delay = delay;

            InitializeIntegrationTimes();
        }

        public void InitializeIntegrationTimes()
        {
            IntegrationStartDelay = 0;
            IntegrationEndOffset = 0.8f * Delay;
        }

        public void SetIntegrationStartTime(float delay)
        {
            // start delay cannot be more negative than this number under any circumstances
            // -Delay should be start of previous injection
            // DataPoints first because there might not be datapoinst from the first few seconds
            var absoluteminimum = Math.Max(-Delay, Experiment.DataPoints.First().Time - Time + MinimumIntegrationTime);

            IntegrationStartDelay = Math.Clamp(delay, absoluteminimum, IntegrationEndOffset - MinimumIntegrationTime);
            Experiment?.MarkModified();
        }

        public void SetIntegrationLengthByPeakFitting()
        {
            SetIntegrationLengthByFactor(2.5f);
        }

        public void SetIntegrationLengthByFactor(float factor)
        {
            const double tau = 8.0;

            const double returnFrac = 0.02;
            const double noiseFactor = 3.0;

            // Time-based parameters (seconds)
            const double smoothWindowSeconds = 5.0; // ~current behavior at 1 s sampling
            const double tailWindowSeconds = 20.0;  // as per comment
            const int minTailSamples = 10;          // keep some robustness

            try
            {
                var dps = Experiment.BaseLineCorrectedDataPoints
                    .Where(dp => dp.Time > Time && dp.Time < Time + Delay)
                    .ToList();
                if (dps.Count < 10) return;

                int n = dps.Count;
                double dt = Experiment.TimeStep;
                if (!(dt > 0)) return;

                // Convert time windows -> sample windows
                int smoothHalfWin = Math.Max(1, (int)Math.Round((smoothWindowSeconds / dt) / 2.0));
                int tailN = (int)Math.Round(tailWindowSeconds / dt);
                tailN = Math.Clamp(tailN, minTailSamples, n);

                // Smooth |power|
                double[] sm = new double[n];
                for (int i = 0; i < n; i++)
                {
                    double s = 0; int c = 0;
                    int a = Math.Max(0, i - smoothHalfWin);
                    int b = Math.Min(n - 1, i + smoothHalfWin);
                    for (int j = a; j <= b; j++) { s += Math.Abs(dps[j].Power); c++; }
                    sm[i] = s / c;
                }

                // Baseline + sigma from tail (last tailWindowSeconds)
                var tail = sm.Skip(n - tailN).OrderBy(v => v).ToArray();
                double baseline = tail[tail.Length / 2];

                double sigma = Math.Sqrt(sm.Skip(n - tailN)
                    .Average(v => (v - baseline) * (v - baseline)));

                // Apex: turning point using the same time-scale neighborhood
                // (keep your logic but with window derived from dt)
                int apex = -1;
                int k = Math.Max(2, smoothHalfWin); // ensure enough points for pattern
                for (int i = k; i < n - k; i++)
                {
                    // require non-decreasing up to i and non-increasing after i over k steps
                    bool up = true, down = true;
                    for (int u = i - k; u < i; u++) if (sm[u] > sm[u + 1]) { up = false; break; }
                    if (up)
                    {
                        for (int d = i; d < i + k; d++) if (sm[d] < sm[d + 1]) { down = false; break; }
                        if (down) { apex = i; break; }
                    }
                }
                if (apex < 0) return;

                double A0 = sm[apex] - baseline;
                if (A0 <= 0) return;

                double thr = Math.Max(returnFrac * A0, noiseFactor * sigma);

                double tReturn = (thr < A0) ? (tau * Math.Log(A0 / thr)) : 0.0;

                double apexOffset = dps[apex].Time - Time;
                double endOffset = apexOffset + factor * tReturn;

                IntegrationEndOffset = Math.Clamp((float)endOffset,
                    IntegrationStartDelay + MinimumIntegrationTime,
                    Delay);
                Experiment?.MarkModified();
            }
            catch (Exception ex)
            {
                AppEventHandler.DisplayHandledException(ex);
            }
        }

        public void SetIntegrationLengthByTime(float time)
        {
            // Keep between the start delay and the injection scope
            IntegrationEndOffset = Math.Clamp(time, IntegrationStartDelay + MinimumIntegrationTime, Delay);
            Experiment?.MarkModified();
        }

        public void ToggleDataPointActive()
        {
            Include = !Include;

            // Heat direction depends only on included peaks to avoid artifacts
            Experiment.CalculateExperimentHeatDirection();
        }

        public void Integrate()
        {
            var data = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime).ToList();
            var area = 0.0;
            var t = IntegrationStartTime;

            foreach (var dp in data)
            {
                var dt = dp.Time - t;
                area += dp.Power * dt;

                t = dp.Time;
            }

            var sd = EstimateError2();
            var peakarea = new FloatWithError(area, sd);

            // Set the peak area and perform buffer subtraction if relevant
            SetPeakArea(peakarea);
        }

        /// <summary>
        /// Set the integrated area and set the IsIntegrated flag. Also performs buffer referencing.
        /// </summary>
        public void SetPeakArea(FloatWithError area)
        {
            RawPeakArea = area;
            UpdateCorrectedPeakArea();

            // Set heat direction based on the area and error
            HeatDirection =
                PeakArea > 3 * PeakArea.SD ? PeakHeatDirection.Endothermal :
                PeakArea < -3 * PeakArea.SD ? PeakHeatDirection.Exothermal :
                PeakHeatDirection.Unknown;

            IsIntegrated = true;
        }

        public void UpdateCorrectedPeakArea()
        {
            var area = RawPeakArea;

            var reference = Experiment?.ReferenceExperiment;
            if (reference != null && TryGetReferencePeakArea(reference, out var refHeat))
            {
                var newHeat = area.Value - refHeat.Value;
                var newSd = Math.Sqrt(area.SD * area.SD + refHeat.SD * refHeat.SD);
                area = new FloatWithError(newHeat, newSd);
            }

            PeakArea = area;
        }

        bool TryGetReferencePeakArea(ExperimentData reference, out FloatWithError area)
        {
            area = default;

            if (reference.InjectionCount == 0) return false;

            // Prefer same injection if valid
            var idx = Math.Clamp(ID, 0, reference.InjectionCount - 1);
            var same = reference.Injections[idx];
            if (IsValidReferenceInjection(same))
            {
                area = same.RawPeakArea;
                return true;
            }

            // Look for alternatives
            InjectionData prev = null;
            for (int i = idx - 1; i >= 0; i--)
            {
                var inj = reference.Injections[i];
                if (IsValidReferenceInjection(inj))
                {
                    prev = inj;
                    break;
                }
            }

            InjectionData next = null;
            for (int i = idx + 1; i < reference.InjectionCount; i++)
            {
                var inj = reference.Injections[i];
                if (IsValidReferenceInjection(inj))
                {
                    next = inj;
                    break;
                }
            }

            // Prefer average over previous over next
            if (prev != null && next != null) { area = FWEMath.Average(prev.RawPeakArea, next.RawPeakArea); return true; }
            else if (prev != null) { area = prev.RawPeakArea; return true; }
            else if (next != null) { area = next.RawPeakArea; return true; }

            return false;
        }

        private double EstimateError2()
        {
            // Set baseline fitting time points to non-integrated data points surrounding current peak.
            float baseline_start_time = ID == 0 ? 0 : Experiment.Injections[ID - 1].IntegrationEndTime;
            float baseline_end_time = Time + Delay;
            float baseline_exclude_start_time = IntegrationStartTime;

            // Start at previous inj integration end time, but take at least 10s
            baseline_start_time = Math.Min(baseline_start_time, baseline_exclude_start_time - 10);
            // Exclude integration but keep at least the last 10s of the injection scope
            float baseline_exclude_end_time = Math.Min(IntegrationEndTime, baseline_end_time - 10);

            // In case of strange values or very short injection scopes
            if (baseline_exclude_end_time <= baseline_exclude_start_time)
                baseline_exclude_end_time = baseline_exclude_start_time;

            // Collect the baseline points (excluding integrated points)
            var bl = Experiment.BaseLineCorrectedDataPoints.Where(dp =>
                dp.Time >= baseline_start_time
                && dp.Time <= baseline_end_time
                && !(dp.Time > baseline_exclude_start_time && dp.Time < baseline_exclude_end_time)).ToList();

            var med = Statistics.Median(bl.Select(dp => dp.Power).ToList());
            var mad = Statistics.Median(bl.Select(dp => Math.Abs(dp.Power - med)).ToList());
            var sigma0 = 1.4826f * mad;
            if (sigma0 <= 0) sigma0 = 1e-12f;
            var k = 6.0f;
            var cap = k * sigma0;

            var blpoints = bl.Count;

            if (blpoints < 2) return 0;

            // Calculate the RMSD
            double ss = 0;
            int n1 = 0;
            int n2 = 0;
            foreach (var dp in bl)
            {
                var p = dp.Power;

                if (Math.Abs(p) > cap) p = cap;

                ss += p * p;

                if (dp.Time <= baseline_exclude_start_time) n1++;
                else n2++;
            }

            var sigma_p = Math.Sqrt(ss / (blpoints - 1));

            var intpoints = Experiment.BaseLineCorrectedDataPoints.Where(dp => dp.Time > IntegrationStartTime && dp.Time < IntegrationEndTime).ToList();
            int n_samples_integration = intpoints.Count;

            if (n_samples_integration < 2) return 0;

            float dt = (float)Experiment.TimeStep;
            var r1 = Statistics.EstimateAutoCorrelation(bl, 2 * dt);

            var sumVarInt = Statistics.Ar1SumVarFactor(n_samples_integration, r1);
            var sigma_q = sigma_p * dt * Math.Sqrt(sumVarInt);

            var sumVar = Statistics.Ar1SumVarFactor(n1, r1) + Statistics.Ar1SumVarFactor(n2, r1);
            var sigma_q_bl = (sigma_p * Math.Sqrt(sumVar) / blpoints) * IntegrationLength;

            return Math.Sqrt(sigma_q * sigma_q + sigma_q_bl * sigma_q_bl);
        }

        public InjectionData Copy(ExperimentData data)
        {
            var inj = new InjectionData(data, ID, Time, Volume, Delay, Duration, Temperature)
            {
                //InjectionMass = Volume * data.SyringeConcentration,
                ActualCellConcentration = ActualCellConcentration,
                ActualTitrantConcentration = ActualTitrantConcentration,
                Ratio = Ratio,
                Include = Include
            };

            inj.SetPeakArea(RawPeakArea);

            return inj;
        }

        public InjectionData CopyWithNewID(ExperimentData data, int id, float timeshift)
        {
            var inj = new InjectionData()
            {
                Experiment = data,
                ID = id,
                Include = this.Include,
                Time = this.Time + timeshift,
                Volume = this.Volume,
                Delay = this.Delay,
                Duration = this.Duration,
                Temperature = this.Temperature,
                IntegrationStartDelay = this.IntegrationStartDelay,
                IntegrationEndOffset = this.IntegrationEndOffset,
            };

            return inj;
        }

        public enum IntegrationLengthMode
        {
            Time,
            Factor,
            Fit
        }

        public override string ToString()
        {
            return $"{ID} : {Ratio:F2} : {Enthalpy2.ToFormattedString(EnergyUnit.KiloJoule)}";
        }
    }
}
