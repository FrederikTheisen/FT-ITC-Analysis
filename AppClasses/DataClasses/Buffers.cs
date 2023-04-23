using System;
using System.Collections.Generic;
using System.Linq;
using AnalysisITC.AppClasses.AnalysisClasses;

namespace AnalysisITC.AppClasses.AnalysisClasses
{
	public enum Buffer
	{
		Null = -1,
		[Buffer("HEPES", 7.66, -0.014, 0, "HEPES free acid")]
		Hepes,
		[Buffer("Phosphate", new[] { 2.15, 7.2, 12.33 }, new[] { 0.0044,-0.0028, -0.026 }, new[] { 0, -1, -2 }, "Phosphoric acid")]
		Phosphate,
		[Buffer("Tris",  8.06 , -0.028, 1, "Tris base")]
		Tris,
		[Buffer("Maleate", 2.0 , 0, 0, "Maleic acid")]
		Maleate,
		[Buffer("Chloroacetate", 2.88, 0.0023, 0, "Chloroacetic acid")]
		Chloroacetate,
		[Buffer("Citrate", new[] { 3.14, 4.76, 6.39 }, new[] { 0, -0.0016, 0 }, new[] { 0, -1, -2 }, "Citric acid, monohydrate")]
		Citrate,
		[Buffer("Formate", 3.75, 0, 0, "Formic acid")]
		Formate,
		[Buffer("Succinate", new[] { 4.19, 5.57 }, new[] { -0.0018, 0 }, new[] { 0, -1 }, "Succinic Acid")]
		Succinate,
		[Buffer("Benzoate", 4.2, 0.018, 0, "Benzoic acid")]
		Benzoate,
		[Buffer("Acetate", 4.76, -0.0002, 0, "Acetic acid")]
		Acetate,
		[Buffer("Propionate", 4.86, -0.0002, 0, "Propionic acid")]
		Propionate,
		[Buffer("Pyridine", 5.23, -0.014, 1, "Pyridine")]
		Pyridine,
		[Buffer("Piperazine", 5.55, -0.015, 0, "Piperazine")]
		Piperazine,
		[Buffer("MES", 6.21, -0.011, 0, "MES free acid")]
		MES,
		[Buffer("Carbonate", new[] { 6.37, 10.25 }, new[] { -0.0055, -0.0090 }, new[] { 0, -1 }, "Carbonic acid")]
		Carbonate,
		[Buffer("Bis-tris", 6.46, -0.02, 1, "Bis-tris base")]
		BisTris,
		[Buffer("ADA", 6.91, -0.011, -1, "ADA free acid")]
		ADA,
		[Buffer("PIPES", 7.1, -0.0085, -1, "PIPES free acid")]
		PIPES,
		[Buffer("ACES", 6.99, -0.02, 0, "ACES free acid")]
		ACES,
		[Buffer("BES", 7.26, -0.016, 0, "BES free acid")]
		BES,
		[Buffer("MOPS", 7.31, -0.011, 0, "MOPS free acid")]
		MOPS,
		[Buffer("TES", 7.61, -0.02, 0, "TES free acid")]
		TES,
		[Buffer("Tricine", 8.26, -0.021, 0, "Tricine free acid")]
		Tricine,
		[Buffer("Bicine", 8.46, -0.018, 0, "Bicine free acid")]
		Bicine,
		[Buffer("TAPS", 8.51, -0.02, 0, "TAPS free acid")]
		TAPS,
		[Buffer("Ethanolamine", 9.5, -0.029, 1, "Ethanolamine")]
		Ethanolamine,
		[Buffer("CHES", 9.41, -0.018, 0, "CHES free acid")]
		CHES,
		[Buffer("CAPS", 10.51, -0.018, 0, "CAPS free acid")]
		CAPS,
		[Buffer("Methylamine", 10.62, -0.031, 1, "Methylamine")]
		Methylamine,
		[Buffer("Piperidine", 11.12, -0.031, 1, "Piperidine")]
		Piperidine,
	}

	public class BufferAttribute : Attribute
	{
		public string Name { get; private set; }
		public string Description { get; private set; }

		public double[] pKaValues { get; private set; }
		double[] dPKadT { get; set; }
		int[] Charges { get; set; }

		public double ProtonationEnthalpy { get; private set; } = 0;

		public int Transitions => pKaValues.Length;

        public BufferAttribute(string name, double pka, double tc, int za, string description, double dh = 0)
        {
            Name = name;
            Description = description;

            pKaValues = new[] { pka };
            dPKadT = new[] { tc };
            Charges = new[] { za };

            ProtonationEnthalpy = dh;
        }

        public BufferAttribute(string name, double[] pkas, double[] tcs, int[] za, string description, double dh = 0)
		{
			Name = name;
			Description = description;

			pKaValues = pkas;
			dPKadT = tcs;
			Charges = za;

			ProtonationEnthalpy = dh;
		}

		Tuple<double,double,int> GetStateProperties(double pH)
		{
			if (Transitions == 1) return new Tuple<double, double, int>(pKaValues[0], dPKadT[0], Charges[0]);
			else
			{
				var dist = 14.0;

                for (int i = 0; i < Transitions; i++)
				{
                    double pka = pKaValues[i];

					if (Math.Abs(pka - pH) > dist) return new Tuple<double, double, int>(pKaValues[i - 1], dPKadT[i - 1], Charges[i - 1]);
					else dist = Math.Abs(pka - pH);
                }

				return new Tuple<double, double, int>(pKaValues[Transitions - 1], dPKadT[Transitions - 1], Charges[Transitions - 1]);
            }
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
            var I1 = (R / (1 + R)) * conc * (Math.Pow((z - 1), 2));    // basic species
            var I2 = (1 / (1 + R)) * conc * (Math.Pow(z, 2));        // acidic species
            var I3 = (R / (1 + R)) * conc * (Math.Abs(z - 1));        // counterion, basic species
            var I4 = (1 / (1 + R)) * conc * (Math.Abs(z));          // counterion, acidic species
            return (I1 + I2 + I3 + I4) / 2;
        }

		public static double GetIonicStrength(ExperimentData data)
		{
			//Check if ionicstrength is specifically stated
			if (data.ExperimentOptions.Exists(opt => opt.Key == ModelOptionKey.IonicStrength)) return data.ExperimentOptions.First(opt => opt.Key == ModelOptionKey.IonicStrength).ParameterValue.Value;

			var salts = data.ExperimentOptions.Where(opt => opt.Key == ModelOptionKey.Salt);
			var buffers = data.ExperimentOptions.Where(opt => opt.Key == ModelOptionKey.Buffer);

			var i = 0.0;

			foreach (var salt in salts)
			{
				i += ((Salt)salt.IntValue).GetProperties().IonicStrength * salt.ParameterValue;
			}

			if (AppSettings.IonicStrengthIncludesBuffer) foreach (var buffer in buffers)
				{
					i += ((Buffer)buffer.IntValue).GetProperties().GetBufferIonicStrength(buffer.DoubleValue, buffer.ParameterValue, data.MeasuredTemperature);
				}

			return i;
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
                Buffer.Hepes,
                Buffer.Phosphate,
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
            return (from Buffer buffer in Enum.GetValues(typeof(Buffer)) select buffer).Where(b => (int)b >= 0).ToList();
        }
	}
}

