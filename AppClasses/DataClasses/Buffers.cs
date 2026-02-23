using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AnalysisITC.AppClasses.AnalysisClasses;
using AnalysisITC.Utilities;

namespace AnalysisITC
{
	public enum Buffer
	{
		Null = -1,
		[Buffer("Hepes", 7.66, -0.014, 0, "N-2-hydroxyethylpiperazine-N'-2-ethanesulfonic acid", new[] { 20400.0, 47 } )]
		Hepes,
		//pKa1: ∆H = -8000, -141, pKa3: 16000, -242
		[Buffer(new[] { "Sodium Phosphate", "NaHPO{4}", "NaPO4", "NaPO", "NaPi" }, new[] { 2.15, 7.2, 12.33 }, new[] { 0.0044, -0.0028, -0.026 }, new[] { 0, -1, -2 }, "Sodium phosphoric acid (NaPO4)", new[] { 5120, -187.0 })]
		SodiumPhosphate,
        [Buffer(new[] { "Potassium Phosphate", "KHPO{4}", "KPO4", "KPO" }, new[] { 2.15, 7.2, 12.33 }, new[] { 0.0044, -0.0028, -0.026 }, new[] { 0, -1, -2 }, "Potasium phosphoric acid (KPO4)", new[] { 5120, -187.0 })]
        PotassiumPhosphate,
        [Buffer("Tris", 8.06, -0.028, 1, "tris(hydroxymethyl)aminomethane", new[] { 47450, -59.0 })]
		Tris,
		[Buffer("Maleate", 2.0, 0, 0, "Maleic acid")]
		Maleate,
		[Buffer("Chloroacetate", 2.88, 0.0023, 0, "Chloroacetic acid")]
		Chloroacetate,
		//pKa1: 4070, -131; pKa2: 2230, -178
		[Buffer("Citrate", new[] { 3.14, 4.76, 6.39 }, new[] { 0, -0.0016, 0 }, new[] { 0, -1, -2 }, "Citric acid, monohydrate", new[] { -3380, -254.0 })]
		Citrate,
		[Buffer("Formate", 3.75, 0, 0, "Na/K/NH4 etc. HCOO")]
		Formate,
		[Buffer("Succinate", new[] { 4.19, 5.57 }, new[] { -0.0018, 0 }, new[] { 0, -1 }, "Succinic Acid")]
		Succinate,
		[Buffer("Benzoate", 4.2, 0.018, 0, "Benzoic acid")]
		Benzoate,
		[Buffer("Acetate", 4.76, -0.0002, 0, "NaCH3COOH", new[] { -410, -142.0 })]
		Acetate,
		[Buffer("Propionate", 4.86, -0.0002, 0, "Propionic acid")]
		Propionate,
		[Buffer("Pyridine", 5.23, -0.014, 1, "Pyridine")]
		Pyridine,
		[Buffer("Piperazine", 5.55, -0.015, 0, "Piperazine")]
		Piperazine,
		[Buffer("MES", 6.21, -0.011, 0, "2-(N-morpholino)ethanesulfonic acid", new[] { 14800, 5.0 })]
		MES,
		[Buffer("Carbonate", new[] { 6.37, 10.25 }, new[] { -0.0055, -0.0090 }, new[] { 0, -1 }, "Carbonic acid", new[] { 9150, -249.0 })]
		Carbonate,
		[Buffer("Bis-Tris", 6.46, -0.02, 1, "Bis-Tris base", new[] { 28400, 27.0 })]
		BisTris,
		[Buffer("ADA", 6.91, -0.011, -1, "N-(2-acetamido)iminodiacetic acid", new[] { 12230, -144.0 })]
		ADA,
		[Buffer("PIPES", 7.1, -0.0085, -1, "piperazine-N,N′-bis(2-ethanesulfonic acid)", new[] { 11200, 22.0 })]
		PIPES,
		[Buffer("ACES", 6.99, -0.02, 0, "N-(2-Acetamido)-2-aminoethanesulfonic acid", new[] { 30430, -49.0 })]
		ACES,
		[Buffer("BES", 7.26, -0.016, 0, "N,N-Bis(2-hydroxyethyl)-2-aminoethanesulfonic acid", new[] { 24250, -2.0 })]
		BES,
		[Buffer("MOPS", 7.31, -0.011, 0, "3-(N-morpholino)propanesulfonic acid", new[] { 22200, 25.0 })]
		MOPS,
		[Buffer("TES", 7.61, -0.02, 0, "2-{[1,3-Dihydroxy-2-(hydroxymethyl)propan-2-yl]amino}ethane-1-sulfonic acid", new[] { 32130, 0.0 })]
		TES,
		[Buffer("Tricine", 8.26, -0.021, 0, "Tricine free acid", new[] { 31370, -53.0 })]
		Tricine,
		[Buffer("Bicine", 8.46, -0.018, 0, "Bicine free acid", new[] { 26340, 0.0 })]
		Bicine,
		[Buffer("TAPS", 8.51, -0.02, 0, "[tris(hydroxymethyl)methylamino]propanesulfonic acid", new[] { 40400, 15.0 })]
		TAPS,
		[Buffer("Ethanolamine", 9.5, -0.029, 1, "Ethanolamine")]
		Ethanolamine,
		[Buffer("CHES", 9.41, -0.018, 0, "N-cyclohexyl-2-aminoethanesulfonic acid", new[] { 39550, 9.0 })]
		CHES,
		[Buffer("CAPS", 10.51, -0.018, 0, "N-cyclohexyl-3-aminopropanesulfonic acid", new[] { 48100, 57.0 })]
		CAPS,
		[Buffer("Methylamine", 10.62, -0.031, 1, "Methylamine")]
		Methylamine,
		[Buffer("Piperidine", 11.12, -0.031, 1, "Piperidine")]
		Piperidine,
        [Buffer("TAPSO", 7.635, 0, 1, "3-[[1,3-dihydroxy-2-(hydroxymethyl)propan-2-yl]amino]-2-hydroxypropane-1-sulfonic acid", new[] { 39090, -16.0 })] //FIXME check charge and pka temp dependence
        TAPSO,
		[Buffer("PBS", 7.2, -0.0028, 1, "Phosphate-buffered saline [NaPO4, KPO4, pH 7.4, NaCl, KCl]")]
		PBS,
        [Buffer("TBS", 8.06, -0.028, 1, "Tris-buffered saline [Tris-HCl, pH 7.4, NaCl, KCl]")]
        TBS,
        //dpKa/T ref: doi.org/10.1016/j.chroma.2006.09.084
        [Buffer(new[] { "L-Histidine", "Histidine", "His" }, 6.07, -0.02, 1, "Histidine Buffer", new[] { 29500, 176.0 })]
		Histidine,
    }

    public static partial class Extensions
    {
        public static BufferAttribute GetProperties(this Buffer value)
        {
            var fieldInfo = value.GetType().GetField(value.ToString());
            var attribute = fieldInfo.GetCustomAttributes(typeof(BufferAttribute), false).FirstOrDefault() as BufferAttribute;

            return attribute;
        }

		public static string GetTooltip(this Buffer value)
		{
			if (value == Buffer.Null) return "";
			return BufferAttribute.Tooltips[value];
        }

        public static double GetProtonationEnthalpy(this Buffer value, double temperature)
        {
			if (value == Buffer.Null) return 0;
            return value.GetProperties().ProtonationEnthalpy.Evaluate(temperature);
        }
    }

    public class BufferAttribute : Attribute
	{
		/// <summary>
		/// Nice looking name string
		/// </summary>
		public string ListName { get; private set; }

		/// <summary>
		/// Should typically be a chemical formula or the same as the ListName
		/// </summary>
		public string AttributedName { get; set; }
		private List<string> aliases { get; set; }
		public string Description { get; private set; }

		public double[] pKaValues { get; private set; }
		public double[] dPKadT { get; private set; }
		int[] AcidCharges { get; set; }

		public LinearFit ProtonationEnthalpy { get; private set; } = new(0, 0, 25);

		public int Transitions => pKaValues.Length;

		static double avgdPKadT = 0.01240; //some default just to be sure
		static double vardPKadT = 0.00998;

		/// <summary>
		/// All possible name of the buffer
		/// </summary>
		public List<string> Aliases
		{
			get
			{
				var list = new List<string> { ListName, AttributedName.Replace("{", "").Replace("}", "") };

				if (aliases != null) list.AddRange(aliases);

				return list;
			}
		}

		public static Dictionary<Buffer, string> Tooltips { get; private set; } = new Dictionary<Buffer, string>();

		public static void Init()
		{
            var tcvalues = new List<double>();

            var buffers = BufferAttribute.GetBuffers();

            foreach (var buffer in buffers)
            {
                foreach (var tc in buffer.GetProperties().dPKadT)
                {
                    tcvalues.Add(Math.Abs(tc));
                }
            }

            var value = new FloatWithError(tcvalues.Where(v => v > 0.00001));

			avgdPKadT = value.Value;
			vardPKadT = value.SD;

			foreach (var buffer in GetBuffers())
			{
				Tooltips[buffer] = buffer.GetProperties().GetBufferTooltip();
            }
        }

        public BufferAttribute(string name, double pka, double tc, int za, string description, double[] dh)
        {
            ListName = name;
			AttributedName = name;
            Description = description;

            pKaValues = new[] { pka };
            dPKadT = new[] { tc };
            AcidCharges = new[] { za };

            if (dh != null) ProtonationEnthalpy = new LinearFit(dh[1], dh[0], 25);
        }

        public BufferAttribute(string[] names, double pka, double tc, int za, string description, double[] dh)
        {
            ListName = names[0];
            AttributedName = names[1];
            Description = description;

            pKaValues = new[] { pka };
            dPKadT = new[] { tc };
            AcidCharges = new[] { za };

            if (dh != null) ProtonationEnthalpy = new LinearFit(dh[1], dh[0], 25);

            AddAliases(names);
        }

        public BufferAttribute(string name, double pka, double tc, int za, string description, double dh = 0)
        {
            ListName = name;
			AttributedName = name;
            Description = description;

            pKaValues = new[] { pka };
            dPKadT = new[] { tc };
            AcidCharges = new[] { za };

            ProtonationEnthalpy = new(0, dh, 25);
        }

        public BufferAttribute(string name, double[] pkas, double[] tcs, int[] za, string description, double[] dh = null)
		{
			ListName = name;
			AttributedName = name;
			Description = description;

			pKaValues = pkas;
			dPKadT = tcs;
			AcidCharges = za;

			if (dh != null) ProtonationEnthalpy = new(dh[1], dh[0], 25);
        }

        public BufferAttribute(string[] names, double[] pkas, double[] tcs, int[] za, string description, double[] dh = null)
        {
            ListName = names[0];
			AttributedName = names[1];
            Description = description;

            pKaValues = pkas;
            dPKadT = tcs;
            AcidCharges = za;

            if (dh != null) ProtonationEnthalpy = new(dh[1], dh[0], 25);

			AddAliases(names);
        }

		void AddAliases(string[] names)
        {
            aliases = new List<string>();
            for (int i = 2; i < names.Length; i++)
                aliases.Add(names[i].Replace("{", "").Replace("}", ""));
        }

        string GetBufferTooltip()
		{
			var tooltip = ListName + Environment.NewLine + Description + Environment.NewLine;
			tooltip += MarkdownStrings.pKa + (Transitions > 1 ? "s: " : ": ");
			foreach (var pka in pKaValues)
				tooltip += pka.ToString("F2") + ", ";
			tooltip = tooltip.Substring(0,tooltip.Length - 2) + Environment.NewLine;

			tooltip += "Thermal stability: ";

			foreach (var tc in dPKadT)
			{
				string s = "";
				if (Math.Abs(tc) > avgdPKadT + vardPKadT) s = "bad, ";
				else if (Math.Abs(tc) > avgdPKadT) s = "poor, ";
				else if (Math.Abs(tc) > avgdPKadT - vardPKadT) s = "ok, ";
				else if (Math.Abs(tc) > 0.000001) s = "good, ";
				else s = "unknown, ";

				tooltip += s;
			}
			tooltip = tooltip.Substring(0, tooltip.Length - 2);

            if (ProtonationEnthalpy.Intercept != 0) tooltip += Environment.NewLine + MarkdownStrings.ProtonationEnthalpy +  "@25°C = " + new Energy(ProtonationEnthalpy.Evaluate(25)).ToFormattedString(AppSettings.EnergyUnit.IsSI() ? EnergyUnit.KiloJoule : EnergyUnit.KCal, withunit: true, permole: true);

			return tooltip;
        }

        public static void SetupSpecialBuffer(List<ExperimentAttribute> tmpoptions, Buffer buffer)
        {
            tmpoptions.RemoveAll(att => att.Key == AttributeKey.Buffer);
            tmpoptions.RemoveAll(att => att.Key == AttributeKey.Salt);

			switch (buffer)
			{
				case Buffer.TBS:
					{
						var tris = ExperimentAttribute.FromKey(AttributeKey.Buffer);
						tris.IntValue = (int)Buffer.Tris;
						tris.ParameterValue = new(0.025);
						tris.DoubleValue = 7.4;
                        tmpoptions.Add(tris);

						var nacl = ExperimentAttribute.FromKey(AttributeKey.Salt);
						nacl.IntValue = (int)Salt.NaCl;
						nacl.ParameterValue = new(0.137);
                        tmpoptions.Add(nacl);

						var kcl = ExperimentAttribute.FromKey(AttributeKey.Salt);
						kcl.IntValue = (int)Salt.KCl;
						kcl.ParameterValue = new(0.0027);

                        tmpoptions.Add(kcl);

					}
                    break;
				case Buffer.PBS:
                    {
                        var napo4 = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                        napo4.IntValue = (int)Buffer.SodiumPhosphate;
                        napo4.ParameterValue = new(0.010);
                        napo4.DoubleValue = 7.4;
                        tmpoptions.Add(napo4);

                        var kpo4 = ExperimentAttribute.FromKey(AttributeKey.Buffer);
                        kpo4.IntValue = (int)Buffer.PotassiumPhosphate;
                        kpo4.ParameterValue = new(0.0018);
                        kpo4.DoubleValue = 7.4;
                        tmpoptions.Add(kpo4);

                        var nacl = ExperimentAttribute.FromKey(AttributeKey.Salt);
                        nacl.IntValue = (int)Salt.NaCl;
                        nacl.ParameterValue = new(0.137);
                        tmpoptions.Add(nacl);

                        var kcl = ExperimentAttribute.FromKey(AttributeKey.Salt);
                        kcl.IntValue = (int)Salt.KCl;
                        kcl.ParameterValue = new(0.0027);

                        tmpoptions.Add(kcl);
                    }
                    break;
			}
        }

        #region Ionic strength methods

        Tuple<double, double, int> GetStateProperties(double pH)
        {
            int best = 0;
            double bestDist = double.MaxValue;

            for (int i = 0; i < Transitions; i++)
            {
                double d = Math.Abs(pKaValues[i] - pH);
                if (d < bestDist) { bestDist = d; best = i; }
            }

            return Tuple.Create(pKaValues[best], dPKadT[best], AcidCharges[best]);
        }

        /// <summary>
        /// Makes temperature correction to pKa
        /// </summary>
        /// <param name="pKa">pKa</param>
        /// <param name="dpKadT"></param>
        /// <param name="temp">Temperature in Celcius</param>
        /// <returns></returns>
        double tempcomp(double pKa, double dpKadT, double temp)
		{
			return (pKa + (dpKadT * (temp - 25)));
		}
		double DebyeHuckel(double temp)
		{
			return (0.4918 + 0.0006614 * temp + 0.000004975 * (temp * temp));
		}
		double newpKa(double pK, double I, double T, int z)
		{
			var A = DebyeHuckel(T);
			var corr = (2 * z - 1) * ((A * Math.Sqrt(I)) / (1 + Math.Sqrt(I)) - (0.1 * I));
			var P = pK + corr;

			return P;
		}
		double optpKa(double pKa, double T, int z, double C, double pH)
		{
			var pKtemp1 = pKa;
			var pKtemp2 = 0.0;
			var d = 10.0;
			var Itemp = 0.0;
			while (d > 0.0001)
			{
				Itemp = CalcIS(pH, pKtemp1, z, C);
				pKtemp2 = newpKa(pKa, Itemp, T, z);
				d = Math.Abs(pKtemp1 - pKtemp2);
				pKtemp1 = pKtemp2;
			}
			return pKtemp2;
		}
        double GetBufferIonicStrength(double pH, double conc, double temperature)
        {
			var properties = GetStateProperties(pH);
			var pka = properties.Item1;
			var tc = properties.Item2;
			var z = properties.Item3;

            var pKaPrime = tempcomp(pka, tc, temperature);
            pKaPrime = optpKa(pKaPrime, temperature, z, conc, pH);
            var I = CalcIS(pH, pKaPrime, z, conc);

			return I;
        }
        double CalcIS(double pH, double pK, int z, double conc)
        {
            var R = Math.Pow(10, (pH - pK));
            var c_acid = conc / (1.0 + R);
            var c_base = conc - c_acid;

            // buffer species
            var I_species =
                c_base * Math.Pow(z - 1, 2) +
                c_acid * Math.Pow(z, 2);

            // monovalent counterions from NaOH / HCl
            var I_counter =
                c_base * Math.Abs(z - 1) +
                c_acid * Math.Abs(z);

            return 0.5 * (I_species + I_counter);
        }
        public static double GetIonicStrength(ExperimentData data)
		{
			//Check if ionicstrength is specifically stated
			if (data.Attributes.Exists(opt => opt.Key == AttributeKey.IonicStrength)) return data.Attributes.First(opt => opt.Key == AttributeKey.IonicStrength).ParameterValue.Value;

			var salts = data.Attributes.Where(opt => opt.Key == AttributeKey.Salt);
			var buffers = data.Attributes.Where(opt => opt.Key == AttributeKey.Buffer);

			var i = 0.0;

			foreach (var salt in salts)
			{
				i += ((Salt)salt.IntValue).GetProperties().IonicStrength * salt.ParameterValue;
			}

			if (AppSettings.IncludeBufferInIonicStrengthCalc) foreach (var buffer in buffers)
				{
					i += ((Buffer)buffer.IntValue).GetProperties().GetBufferIonicStrength(buffer.DoubleValue, buffer.ParameterValue, data.MeasuredTemperature);
				}

			return i;
		}

        #endregion

		public static Energy GetProtonationEnthalpy(ExperimentData data)
		{
			var buffer = (Buffer)data.Attributes.Find(att => att.Key == AttributeKey.Buffer).IntValue;

			return new (buffer.GetProtonationEnthalpy(data.MeasuredTemperature));
        }

        public static List<Buffer> GetUIBuffers()
		{
			var list = GetCommonBuffers();
			list.Add(Buffer.Null);
			list.AddRange(GetOtherBuffers());
			return list;
        }

        public static List<Buffer> GetCommonBuffers()
        {
            return new List<Buffer>
            {
				Buffer.PBS,
				Buffer.TBS,
				Buffer.Null,
                Buffer.Hepes,
                Buffer.SodiumPhosphate,
				Buffer.Citrate,
                Buffer.Tris,
				Buffer.MOPS,
				Buffer.MES,
            };
        }

		public static List<Buffer> GetOtherBuffers()
		{
			var common = GetCommonBuffers();

            return GetBuffers().Where(b => !common.Contains(b)).ToList();
		}

        public static List<Buffer> GetBuffers()
		{
            return (from Buffer buffer in Enum.GetValues(typeof(Buffer)) select buffer).Where(enumidx => (int)enumidx >= 0).ToList();
        }

        public static bool TryExtractPH(string text, string name, out double pH)
        {
            pH = 0;

            var esc = Regex.Escape(name);
            var mName = Regex.Match(text, $@"(?<![A-Za-z0-9]){esc}(?![A-Za-z0-9])", RegexOptions.IgnoreCase);
            if (!mName.Success) return false;

            // Search a local window around the buffer name
            int start = Math.Max(0, mName.Index - 40);
            int len = Math.Min(text.Length - start, 140);
            var window = text.Substring(start, len);

            var m = Regex.Match(window, @"\bpH\s*[:=]?\s*([+-]?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(window, @"([+-]?\d+(?:[.,]\d+)?)\s*pH\b", RegexOptions.IgnoreCase);

            if (!m.Success) return false;

            return TryParseNumber(m.Groups[1].Value, out pH);

            static bool TryParseNumber(string s, out double v)
            {
                v = 0;
                if (string.IsNullOrWhiteSpace(s)) return false;

                // allow both "7.4" and "7,4"
                var t = s.Trim();
                if (t.Count(c => c == ',') == 1 && !t.Contains('.')) t = t.Replace(',', '.');

                return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            }
        }
    }
}

