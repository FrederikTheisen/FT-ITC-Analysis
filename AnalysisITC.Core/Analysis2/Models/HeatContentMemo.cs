using System;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.Analysis.Models
{
    /// <summary>
    /// Three recent equilibrium heat contents within a single objective evaluation.
    /// MicroCal requests the current endpoint before the previous endpoint, so two
    /// entries would evict the shared endpoint before the next injection uses it.
    /// </summary>
    internal struct HeatContentMemo
    {
        Entry first;
        Entry second;
        Entry third;
        int count;

        internal bool TryGet(double cell, double ligand, out double heat)
        {
            var cellBits = BitConverter.DoubleToInt64Bits(cell);
            var ligandBits = BitConverter.DoubleToInt64Bits(ligand);
            if (count > 0 && first.Matches(cellBits, ligandBits))
            {
                heat = first.Heat;
                return true;
            }
            if (count > 1 && second.Matches(cellBits, ligandBits))
            {
                var found = second;
                second = first;
                first = found;
                heat = found.Heat;
                return true;
            }
            if (count > 2 && third.Matches(cellBits, ligandBits))
            {
                var found = third;
                third = second;
                second = first;
                first = found;
                heat = found.Heat;
                return true;
            }
            heat = 0;
            return false;
        }

        internal void Store(double cell, double ligand, double heat)
        {
            if (!FWEMath.IsFinite(cell) || !FWEMath.IsFinite(ligand) || !FWEMath.IsFinite(heat)) return;
            third = second;
            second = first;
            first = new Entry(BitConverter.DoubleToInt64Bits(cell), BitConverter.DoubleToInt64Bits(ligand), heat);
            if (count < 3) count++;
        }

        readonly struct Entry
        {
            internal Entry(long cellBits, long ligandBits, double heat)
            {
                CellBits = cellBits;
                LigandBits = ligandBits;
                Heat = heat;
            }

            long CellBits { get; }
            long LigandBits { get; }
            internal double Heat { get; }
            internal bool Matches(long cellBits, long ligandBits) => CellBits == cellBits && LigandBits == ligandBits;
        }
    }
}
