using System;
using System.Collections.Generic;

namespace AnalysisITC.Utils
{
	public enum Buffers
	{
		Hepes,
		Phosphate,
		Tris,
		BisTris,
		Acetate
	}

	public class BufferAttribute : Attribute
	{
		public static List<Buffers> GetBuffers()
		{
			return new List<Buffers>
			{
				Buffers.Hepes,
				Buffers.Phosphate,
				Buffers.Tris,
				Buffers.BisTris,
				Buffers.Acetate
			};
		}
	}
}

