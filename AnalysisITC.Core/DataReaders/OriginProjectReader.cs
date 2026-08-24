using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using AnalysisITC.Core.Application;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;
using AnalysisITC.Core.Units;

namespace AnalysisITC.Core.DataReaders
{
    /// <summary>
    /// Reads legacy Origin CPYA project files containing ITC worksheets.
    ///
    /// The importer deliberately reads source heats and the embedded thermogram,
    /// but does not attempt to restore Origin's baseline, spline, or fit model.
    /// </summary>
    public static class OriginProjectReader
    {
        public static ExperimentData ReadPath(string path) => ReadFile(path);

        public static ExperimentData ReadFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("Origin project file was not found.", path);

            using (var stream = File.OpenRead(path))
            {
                return ReadStream(stream, Path.GetFileName(path), File.GetCreationTime(path));
            }
        }

        internal static ExperimentData ReadStream(Stream stream, string fileName, DateTime? date = null)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));

            var displayName = Path.GetFileName(fileName ?? "origin.opj");
            try
            {
                var document = OriginProjectParser.Read(stream);
                return OriginExperimentMapper.Map(document, displayName, date);
            }
            catch (Exception ex) when (ex is FormatException || ex is EndOfStreamException || ex is OverflowException)
            {
                throw new FormatException($"Could not read Origin project '{displayName}': {ex.Message}", ex);
            }
        }
    }

    internal sealed class OriginProjectDocument
    {
        internal string Signature { get; set; }
        internal string OriginVersionText { get; set; }
        internal double? OriginVersion { get; set; }
        internal List<OriginColumn> Columns { get; } = new List<OriginColumn>();
        internal Dictionary<string, double> Parameters { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        internal List<OriginNote> Notes { get; } = new List<OriginNote>();
        internal List<string> Warnings { get; } = new List<string>();
    }

    internal sealed class OriginNote
    {
        internal string Name { get; set; }
        internal string Contents { get; set; }
    }

    internal struct OriginValue
    {
        internal double? Number;
        internal string Text;
    }

    internal sealed class OriginColumn
    {
        internal const double EmptyValue = -1.23456789E-300;

        internal string Name { get; set; }
        internal ushort DataType { get; set; }
        internal byte DataTypeU { get; set; }
        internal byte ValueSize { get; set; }
        internal uint TotalRows { get; set; }
        internal uint FirstRow { get; set; }
        internal uint LastRow { get; set; }
        internal List<OriginValue> Values { get; } = new List<OriginValue>();

        internal List<double?> NumericValues()
        {
            var count = (int)Math.Min((uint)Values.Count, LastRow);
            return Values.Take(count).Select(value => value.Number).ToList();
        }
    }

    internal static class OriginProjectParser
    {
        // These limits are intentionally generous for ordinary Origin projects,
        // while preventing corrupt sizes from turning into unbounded allocations.
        const int MaxSignatureBytes = 4096;
        const uint MaxColumnRows = 10_000_000;
        const uint MaxBlockBytes = 512 * 1024 * 1024;
        const int MaxSectionsPerList = 100_000;

        internal static OriginProjectDocument Read(Stream stream)
        {
            var reader = new OriginBinaryReader(stream);
            var document = new OriginProjectDocument();

            var signatureBytes = reader.ReadLine(MaxSignatureBytes);
            var signature = Encoding.ASCII.GetString(signatureBytes);
            var signatureParts = signature.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (signatureParts.Length == 0 || !string.Equals(signatureParts[0], "CPYA", StringComparison.Ordinal))
                throw new FormatException("The file does not have the legacy Origin CPYA signature.");

            // Origin has emitted both comma and period variants and optional W32/W64
            // tokens. Keep the complete line for diagnostics and never gate on it.
            document.Signature = signature;
            document.OriginVersionText = signatureParts.Length > 1 ? signatureParts[1] : "";

            byte[] header = reader.ReadBlock();
            if (header == null) throw new FormatException("The Origin project header is missing.");
            if (header.Length >= 35)
                document.OriginVersion = ReadDoubleLittleEndian(header, 27);
            reader.RequireNullBlock("Origin project header");

            ReadDataList(reader, document);

            // Windows, parameters, and notes are useful provenance but are not
            // required to reconstruct ITC data. Some Origin variants differ in
            // these later sections, so retain data if this optional tail fails.
            try
            {
                ReadWindowList(reader);
                ReadParameters(reader, document);
                ReadNotes(reader, document);
            }
            catch (Exception ex) when (ex is FormatException || ex is EndOfStreamException || ex is IOException)
            {
                document.Warnings.Add("Origin window, parameter, or note metadata could not be fully read: " + ex.Message);
            }

            return document;
        }

        static void ReadDataList(OriginBinaryReader reader, OriginProjectDocument document)
        {
            var count = 0;
            while (true)
            {
                if (++count > MaxSectionsPerList)
                    throw new FormatException("The Origin data list contains too many sections.");

                byte[] header;
                if (!reader.TryReadBlock(out header)) break;

                var content = reader.ReadBlock();
                reader.RequireNullBlock("Origin data column");

                OriginColumn column;
                try
                {
                    column = ReadColumnHeader(header);
                    column.Values.AddRange(ReadColumnValues(column, content));
                    document.Columns.Add(column);
                }
                catch (Exception ex) when (ex is FormatException || ex is OverflowException || ex is ArgumentOutOfRangeException)
                {
                    // Non-ITC Origin files often contain worksheet encodings that
                    // are irrelevant to this reader. Keep the section boundary
                    // intact and let worksheet selection decide whether the file
                    // is a usable ITC project. A malformed DH/INJV column cannot
                    // become compatible because it is not added here.
                    document.Warnings.Add("Skipped an unsupported or malformed Origin data column: " + ex.Message);
                }
            }

        }

        static OriginColumn ReadColumnHeader(byte[] header)
        {
            if (header == null || header.Length < 113)
                throw new FormatException("An Origin data column header is truncated.");

            var column = new OriginColumn
            {
                DataType = ReadUInt16LittleEndian(header, 22),
                TotalRows = ReadUInt32LittleEndian(header, 25),
                FirstRow = ReadUInt32LittleEndian(header, 29),
                LastRow = ReadUInt32LittleEndian(header, 33),
                ValueSize = header[61],
                DataTypeU = header[63],
                Name = ReadFixedString(header, 88, 25),
            };

            if (column.TotalRows > MaxColumnRows)
                throw new FormatException($"Origin column '{column.Name}' declares too many rows.");
            if (column.LastRow > column.TotalRows || column.FirstRow > column.LastRow)
                throw new FormatException($"Origin column '{column.Name}' has invalid row bounds.");
            if (column.ValueSize == 0)
                throw new FormatException($"Origin column '{column.Name}' has a zero value size.");

            return column;
        }

        static IEnumerable<OriginValue> ReadColumnValues(OriginColumn column, byte[] content)
        {
            var expectedLength = checked((ulong)column.TotalRows * column.ValueSize);
            if (expectedLength > MaxBlockBytes)
                throw new FormatException($"Origin column '{column.Name}' is too large.");

            if (column.TotalRows == 0) return Array.Empty<OriginValue>();
            if (content == null || (ulong)content.Length < expectedLength)
                throw new FormatException($"Origin column '{column.Name}' has truncated data.");

            var values = new List<OriginValue>((int)column.TotalRows);
            for (uint row = 0; row < column.TotalRows; row++)
            {
                var offset = checked((int)((ulong)row * column.ValueSize));
                values.Add(ReadValue(column, content, offset));
            }

            return values;
        }

        static OriginValue ReadValue(OriginColumn column, byte[] content, int offset)
        {
            if (column.ValueSize <= 8)
            {
                double value;
                switch (column.ValueSize)
                {
                    case 8: value = ReadDoubleLittleEndian(content, offset); break;
                    case 4:
                        value = (column.DataType & 0x800) != 0
                            ? (column.DataTypeU == 8 ? ReadUInt32LittleEndian(content, offset) : ReadInt32LittleEndian(content, offset))
                            : ReadSingleLittleEndian(content, offset);
                        break;
                    case 2:
                        value = (column.DataType & 0x800) != 0
                            ? (column.DataTypeU == 8 ? ReadUInt16LittleEndian(content, offset) : ReadInt16LittleEndian(content, offset))
                            : ReadInt16LittleEndian(content, offset);
                        break;
                    default:
                        value = (column.DataTypeU == 8 ? content[offset] : (sbyte)content[offset]);
                        break;
                }

                return IsEmpty(value) || double.IsNaN(value) || double.IsInfinity(value)
                    ? new OriginValue()
                    : new OriginValue { Number = value };
            }

            var isTextNumeric = (column.DataType & 0x100) != 0;
            if (isTextNumeric && column.ValueSize >= 10)
            {
                var prefix = content[offset];
                if (prefix == 0)
                {
                    var value = ReadDoubleLittleEndian(content, offset + 2);
                    return IsEmpty(value) || double.IsNaN(value) || double.IsInfinity(value)
                        ? new OriginValue()
                        : new OriginValue { Number = value };
                }

                return new OriginValue { Text = ReadFixedString(content, offset + 2, column.ValueSize - 2) };
            }

            return new OriginValue { Text = ReadFixedString(content, offset, column.ValueSize) };
        }

        static void ReadWindowList(OriginBinaryReader reader)
        {
            ReadList(reader, "Origin window list", windowHeader =>
            {
                RequireBlock(windowHeader, "Origin window header");
                ReadList(reader, "Origin layer list", layerHeader =>
                {
                    RequireBlock(layerHeader, "Origin layer header");
                    ReadFixedBlockList(reader, "Origin sublayer", 4);
                    ReadFixedBlockList(reader, "Origin curve", 2);
                    ReadFixedBlockList(reader, "Origin axis break", 1);
                    ReadFixedBlockList(reader, "Origin axis parameter", 1);
                    ReadFixedBlockList(reader, "Origin axis parameter", 1);
                    ReadFixedBlockList(reader, "Origin axis parameter", 1);
                });
            });
        }

        static void ReadFixedBlockList(OriginBinaryReader reader, string name, int blocksPerSection)
        {
            ReadList(reader, name + " list", _ =>
            {
                // The list reader already consumed the section's first block.
                for (var i = 1; i < blocksPerSection; i++)
                    reader.ReadBlock();
            });
        }

        static void ReadList(OriginBinaryReader reader, string name, Action<byte[]> item)
        {
            var count = 0;
            while (true)
            {
                if (++count > MaxSectionsPerList)
                    throw new FormatException($"The {name} contains too many sections.");

                byte[] block;
                if (!reader.TryReadBlock(out block)) break;
                item(block);
            }
        }

        static void ReadParameters(OriginBinaryReader reader, OriginProjectDocument document)
        {
            var count = 0;
            while (true)
            {
                if (++count > MaxSectionsPerList)
                    throw new FormatException("The Origin parameter list contains too many entries.");

                var nameBytes = reader.ReadLine(MaxSignatureBytes);
                if (nameBytes.Length == 1 && nameBytes[0] == 0) break;

                var name = Encoding.ASCII.GetString(nameBytes).Trim('\0', ' ', '\r');
                if (name.Length == 0)
                    throw new FormatException("The Origin parameter list contains an empty name.");

                var valueBytes = reader.ReadExact(8);
                reader.RequireByte(0x0A, "Origin parameter value terminator");
                var value = ReadDoubleLittleEndian(valueBytes, 0);
                if (!double.IsNaN(value) && !double.IsInfinity(value)) document.Parameters[name] = value;
            }

            reader.RequireNullBlock("Origin parameter list");
        }

        static void ReadNotes(OriginBinaryReader reader, OriginProjectDocument document)
        {
            ReadList(reader, "Origin note list", _ =>
            {
                // The first block is the note section's implementation-specific header.
                var name = reader.ReadBlock();
                var contents = reader.ReadBlock();
                if (name == null || contents == null)
                    throw new FormatException("An Origin note section is truncated.");

                document.Notes.Add(new OriginNote
                {
                    Name = ReadFixedString(name, 0, name.Length),
                    Contents = ReadFixedString(contents, 0, contents.Length),
                });
            });
        }

        static void RequireBlock(byte[] block, string context)
        {
            if (block == null) throw new FormatException(context + " is missing.");
        }

        static bool IsEmpty(double value) => value == OriginColumn.EmptyValue;

        static string ReadFixedString(byte[] bytes, int offset, int length)
        {
            var end = offset;
            var limit = Math.Min(bytes.Length, checked(offset + length));
            while (end < limit && bytes[end] != 0) end++;
            return Encoding.ASCII.GetString(bytes, offset, end - offset).TrimEnd('\r', ' ');
        }

        static short ReadInt16LittleEndian(byte[] bytes, int offset) => unchecked((short)(bytes[offset] | (bytes[offset + 1] << 8)));
        static ushort ReadUInt16LittleEndian(byte[] bytes, int offset) => unchecked((ushort)(bytes[offset] | (bytes[offset + 1] << 8)));
        static int ReadInt32LittleEndian(byte[] bytes, int offset) => unchecked((int)ReadUInt32LittleEndian(bytes, offset));
        static uint ReadUInt32LittleEndian(byte[] bytes, int offset) => unchecked((uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24)));

        static float ReadSingleLittleEndian(byte[] bytes, int offset)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(bytes, offset);
            var copy = new byte[4];
            System.Buffer.BlockCopy(bytes, offset, copy, 0, 4);
            Array.Reverse(copy);
            return BitConverter.ToSingle(copy, 0);
        }

        static double ReadDoubleLittleEndian(byte[] bytes, int offset)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToDouble(bytes, offset);
            var copy = new byte[8];
            System.Buffer.BlockCopy(bytes, offset, copy, 0, 8);
            Array.Reverse(copy);
            return BitConverter.ToDouble(copy, 0);
        }
    }

    internal sealed class OriginBinaryReader
    {
        readonly Stream stream;

        internal OriginBinaryReader(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        internal byte[] ReadLine(int maxBytes)
        {
            var bytes = new List<byte>();
            while (bytes.Count < maxBytes)
            {
                var value = ReadByte();
                if (value == 0x0A) return bytes.ToArray();
                bytes.Add(value);
            }

            throw new FormatException("An Origin line exceeds the supported length.");
        }

        internal byte[] ReadExact(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (stream.CanSeek)
            {
                try
                {
                    if (count > stream.Length - stream.Position)
                        throw new EndOfStreamException("The Origin project ended unexpectedly.");
                }
                catch (NotSupportedException)
                {
                    // Some streams report CanSeek but do not expose Length. The
                    // normal exact-read loop below remains safe for those streams.
                }
            }

            var bytes = new byte[count];
            var read = 0;
            while (read < count)
            {
                var n = stream.Read(bytes, read, count - read);
                if (n <= 0) throw new EndOfStreamException("The Origin project ended unexpectedly.");
                read += n;
            }

            return bytes;
        }

        internal byte ReadByte()
        {
            var value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException("The Origin project ended unexpectedly.");
            return (byte)value;
        }

        internal byte[] ReadBlock()
        {
            byte[] block;
            return TryReadBlock(out block) ? block : null;
        }

        internal bool TryReadBlock(out byte[] block)
        {
            var sizeBytes = ReadExact(4);
            var size = unchecked((uint)(sizeBytes[0] | (sizeBytes[1] << 8) | (sizeBytes[2] << 16) | (sizeBytes[3] << 24)));
            RequireByte(0x0A, "Origin block size terminator");

            if (size == 0)
            {
                block = null;
                return false;
            }
            if (size > 512 * 1024 * 1024)
                throw new FormatException("An Origin block exceeds the supported size limit.");

            block = ReadExact(checked((int)size));
            RequireByte(0x0A, "Origin block terminator");
            return true;
        }

        internal void RequireNullBlock(string context)
        {
            byte[] block;
            if (TryReadBlock(out block))
                throw new FormatException(context + " was expected to end with a null block.");
        }

        internal void RequireByte(byte expected, string context)
        {
            var actual = ReadByte();
            if (actual != expected)
                throw new FormatException($"Invalid {context}: expected 0x{expected:X2}, got 0x{actual:X2}.");
        }
    }

    internal static class OriginExperimentMapper
    {
        static readonly string[] Suffixes =
        {
            "RAW_TIME", "RAW_CP", "INJV", "BEGIN", "RANGE", "NDH", "XMT", "DH", "XT", "MT",
        };

        internal static ExperimentData Map(OriginProjectDocument document, string fileName, DateTime? date)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));

            var datasets = BuildDatasets(document.Columns);
            var selected = datasets.FirstOrDefault(dataset => IsCompatible(dataset));
            if (selected == null)
                throw new FormatException("The Origin project contains no compatible ITC worksheet with DH and INJV columns.");

            var dh = selected.Column("DH").NumericValues();
            var injv = selected.Column("INJV").NumericValues();
            var injectionCount = GetInjectionRowCount(selected);
            if (injectionCount == 0)
                throw new FormatException($"Origin worksheet '{selected.BaseName}' contains no ITC injections.");

            var warnings = new List<string>(document.Warnings);
            for (var i = 0; i < injectionCount; i++)
            {
                if (!Finite(dh[i]) || !Finite(injv[i]) || injv[i] <= 0)
                    throw new FormatException($"Origin worksheet '{selected.BaseName}' has a non-numeric DH or INJV row at index {i}.");
            }

            var cellConcentration = GetMillimolarParameter(document, "CELL_C_" + selected.BaseName);
            if (!Positive(cellConcentration))
            {
                cellConcentration = FirstPositive(selected, "MT") * 1e-3;
                warnings.Add("CELL_C metadata was unavailable; cell concentration was inferred from Mt.");
            }
            if (!Positive(cellConcentration))
            {
                cellConcentration = 1e-3;
                warnings.Add("Cell concentration could not be inferred; using 1 mM.");
            }

            var cellVolume = GetMillilitresParameter(document, "ITC_CELL_VOL");
            if (!Positive(cellVolume))
            {
                cellVolume = InferCellVolume(selected, injectionCount);
                warnings.Add("ITC_CELL_VOL metadata was unavailable; cell volume was inferred from Mt dilution.");
            }
            if (!Positive(cellVolume))
            {
                cellVolume = 1.4e-3;
                warnings.Add("Cell volume could not be inferred; using 1.4 mL.");
            }

            var syringeConcentration = GetMillimolarParameter(document, "SYRNG_C_" + selected.BaseName);
            if (!Positive(syringeConcentration))
            {
                syringeConcentration = InferSyringeConcentration(selected, injectionCount, cellVolume);
                warnings.Add("SYRNG_C metadata was unavailable; syringe concentration was inferred from DH/NDH or Xt.");
            }
            if (!Positive(syringeConcentration))
            {
                syringeConcentration = 1e-3;
                warnings.Add("Syringe concentration could not be inferred; using 1 mM.");
            }

            var temperature = GetParameter(document, "TEMP_" + selected.BaseName);
            if (!Finite(temperature))
            {
                temperature = AppSettings.ReferenceTemperature;
                warnings.Add("TEMP metadata was unavailable; using the application reference temperature.");
            }

            var experiment = new ExperimentData(fileName)
            {
                DataSourceFormat = ITCDataFormat.OriginProject,
                Date = date ?? default(DateTime),
                DateSource = date.HasValue ? ExperimentDateSource.FileSystem : ExperimentDateSource.Unknown,
                CellConcentration = new FloatWithError(cellConcentration),
                SyringeConcentration = new FloatWithError(syringeConcentration),
                CellVolume = cellVolume,
                TargetTemperature = temperature,
                MeasuredTemperature = temperature,
                StirringSpeed = -1,
            };

            SetFeedbackMode(experiment, document, selected.BaseName, warnings);
            SetDataPoints(experiment, selected, temperature, warnings);
            SetInjections(experiment, selected, injectionCount, dh, injv, cellConcentration, syringeConcentration, cellVolume, temperature, warnings);
            experiment.CalculateExperimentHeatDirection();
            ITCInstrumentAttribute.ResolveInstrument(experiment);
            experiment.Comments = BuildComments(document, selected.BaseName, fileName, warnings);

            return experiment;
        }

        static List<OriginDataset> BuildDatasets(IEnumerable<OriginColumn> columns)
        {
            var datasets = new List<OriginDataset>();
            var lookup = new Dictionary<string, OriginDataset>(StringComparer.OrdinalIgnoreCase);

            foreach (var column in columns)
            {
                string baseName;
                string suffix;
                if (!TrySplitColumnName(column.Name, out baseName, out suffix)) continue;

                OriginDataset dataset;
                if (!lookup.TryGetValue(baseName, out dataset))
                {
                    dataset = new OriginDataset(baseName);
                    lookup.Add(baseName, dataset);
                    datasets.Add(dataset);
                }

                if (!dataset.Columns.ContainsKey(suffix)) dataset.Columns.Add(suffix, column);
            }

            return datasets;
        }

        static bool TrySplitColumnName(string name, out string baseName, out string suffix)
        {
            baseName = null;
            suffix = null;
            if (string.IsNullOrWhiteSpace(name)) return false;

            foreach (var candidate in Suffixes)
            {
                if (!name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)) continue;
                var length = name.Length - candidate.Length;
                if (length <= 0) continue;

                baseName = name.Substring(0, length).TrimEnd('_');
                if (baseName.Length == 0) continue;
                suffix = candidate;
                return true;
            }

            return false;
        }

        static bool IsCompatible(OriginDataset dataset)
        {
            if (!dataset.Columns.ContainsKey("DH") || !dataset.Columns.ContainsKey("INJV")) return false;
            return GetInjectionRowCount(dataset) > 0;
        }

        static int GetInjectionRowCount(OriginDataset dataset)
        {
            var dh = dataset.Column("DH").NumericValues();
            var injv = dataset.Column("INJV").NumericValues();
            var sharedLength = Math.Min(dh.Count, injv.Count);
            var count = 0;
            while (count < sharedLength && Finite(dh[count]) && Finite(injv[count]) && injv[count] > 0)
                count++;

            if (count == 0) return 0;

            // Origin commonly declares a trailing empty INJV row. Accept empty
            // tails, but reject a later numeric value or a numeric row present
            // in only one required column rather than silently truncating it.
            if (dh.Skip(count).Any(Finite) || injv.Skip(count).Any(Finite)) return 0;
            return count;
        }

        static double GetParameter(OriginProjectDocument document, string name)
        {
            double value;
            return document.Parameters.TryGetValue(name, out value) ? value : double.NaN;
        }

        static double GetMillimolarParameter(OriginProjectDocument document, string name)
        {
            var value = GetParameter(document, name);
            return Finite(value) ? value * 1e-3 : double.NaN;
        }

        static double GetMillilitresParameter(OriginProjectDocument document, string name)
        {
            var value = GetParameter(document, name);
            return Finite(value) ? value * 1e-3 : double.NaN;
        }

        static double FirstPositive(OriginDataset dataset, string suffix)
        {
            OriginColumn column;
            if (!dataset.Columns.TryGetValue(suffix, out column)) return double.NaN;
            var value = column.NumericValues().FirstOrDefault(item => Finite(item) && item.Value > 0);
            return value.HasValue ? value.Value : double.NaN;
        }

        static double InferCellVolume(OriginDataset dataset, int injectionCount)
        {
            OriginColumn mtColumn;
            if (!dataset.Columns.TryGetValue("MT", out mtColumn)) return double.NaN;
            var mt = mtColumn.NumericValues();
            var injv = dataset.Column("INJV").NumericValues();
            var candidates = new List<double>();

            for (var i = 0; i + 1 < injectionCount && i + 1 < mt.Count && i < injv.Count; i++)
            {
                if (!Finite(mt[i]) || !Finite(mt[i + 1]) || !Finite(injv[i]) || mt[i] <= 0 || mt[i + 1] <= 0 || injv[i] <= 0) continue;
                var ratio = mt[i + 1].Value / mt[i].Value;
                if (ratio <= 0 || ratio >= 1) continue;
                var volume = injv[i].Value * 1e-6 / (1 - ratio);
                if (volume > 50e-6 && volume < 10e-3) candidates.Add(volume);
            }

            return Median(candidates);
        }

        static double InferSyringeConcentration(OriginDataset dataset, int injectionCount, double cellVolume)
        {
            var dh = dataset.Column("DH").NumericValues();
            var injv = dataset.Column("INJV").NumericValues();
            var candidates = new List<double>();
            OriginColumn ndhColumn;
            if (dataset.Columns.TryGetValue("NDH", out ndhColumn))
            {
                var ndh = ndhColumn.NumericValues();
                for (var i = 0; i < injectionCount && i < ndh.Count; i++)
                {
                    if (!Finite(dh[i]) || !Finite(ndh[i]) || !Finite(injv[i]) || Math.Abs(ndh[i].Value) < double.Epsilon || injv[i] <= 0) continue;
                    var concentration = (dh[i].Value * 1e-6 / ndh[i].Value) / (injv[i].Value * 1e-6);
                    if (concentration > 0 && concentration < 50) candidates.Add(concentration);
                }
            }

            if (candidates.Count == 0 && cellVolume > 0)
            {
                OriginColumn xtColumn;
                if (dataset.Columns.TryGetValue("XT", out xtColumn))
                {
                    var xt = xtColumn.NumericValues();
                    var deltaVolume = 0.0;
                    for (var i = 0; i < injectionCount && i + 1 < xt.Count && i < injv.Count; i++)
                    {
                        deltaVolume += injv[i].Value * 1e-6;
                        var fraction = (deltaVolume / cellVolume) * (1 - deltaVolume / (2 * cellVolume));
                        if (fraction <= 0 || !Finite(xt[i + 1])) continue;
                        var concentration = xt[i + 1].Value * 1e-3 / fraction;
                        if (concentration > 0 && concentration < 50) candidates.Add(concentration);
                    }
                }
            }

            return Median(candidates);
        }

        static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return double.NaN;
            values.Sort();
            return values[values.Count / 2];
        }

        static void SetFeedbackMode(ExperimentData experiment, OriginProjectDocument document, string baseName, List<string> warnings)
        {
            var value = GetParameter(document, baseName + "FBMODE");
            if (!Finite(value))
            {
                warnings.Add("Feedback-mode metadata was unavailable.");
                return;
            }

            var mode = (int)Math.Round(value);
            if (mode >= (int)FeedbackMode.None && mode <= (int)FeedbackMode.High)
                experiment.FeedBackMode = (FeedbackMode)mode;
            else
                warnings.Add("Feedback-mode metadata was outside the supported range.");
        }

        static void SetDataPoints(ExperimentData experiment, OriginDataset dataset, double temperature, List<string> warnings)
        {
            OriginColumn timeColumn;
            OriginColumn powerColumn;
            if (!dataset.Columns.TryGetValue("RAW_TIME", out timeColumn) || !dataset.Columns.TryGetValue("RAW_CP", out powerColumn))
            {
                experiment.BaseLineCorrectedDataPoints = new List<DataPoint>();
                warnings.Add("Embedded RAW_time/RAW_cp trace columns were unavailable; imported as source heats only.");
                return;
            }

            var times = timeColumn.NumericValues();
            var powers = powerColumn.NumericValues();
            var count = Math.Min(times.Count, powers.Count);
            var points = new List<DataPoint>(count);
            for (var i = 0; i < count; i++)
            {
                if (!Finite(times[i]) || !Finite(powers[i])) continue;
                points.Add(new DataPoint((float)times[i].Value, (float)Energy.ConvertToJoule(powers[i].Value, EnergyUnit.MicroCal), (float)temperature));
            }

            if (points.Count < 2)
            {
                experiment.DataPoints.Clear();
                experiment.BaseLineCorrectedDataPoints = new List<DataPoint>();
                warnings.Add("Embedded RAW_time/RAW_cp trace columns contained fewer than two valid points; imported as source heats only.");
                return;
            }

            experiment.DataPoints = points;
        }

        static void SetInjections(
            ExperimentData experiment,
            OriginDataset dataset,
            int injectionCount,
            List<double?> dh,
            List<double?> injv,
            double cellConcentration,
            double syringeConcentration,
            double cellVolume,
            double temperature,
            List<string> warnings)
        {
            var xt = dataset.ColumnOrNull("XT")?.NumericValues();
            var mt = dataset.ColumnOrNull("MT")?.NumericValues();
            var xmt = dataset.ColumnOrNull("XMT")?.NumericValues();
            var begin = dataset.ColumnOrNull("BEGIN")?.NumericValues();
            var range = dataset.ColumnOrNull("RANGE")?.NumericValues();
            var hasRangeWarning = false;

            for (var i = 0; i < injectionCount; i++)
            {
                var volume = injv[i].Value * 1e-6;
                var time = TryAt(begin, i);
                if (!Finite(time)) time = i == 0 ? 0 : experiment.Injections.Last().Time + 120;

                var nextTime = TryAt(begin, i + 1);
                var delay = Finite(nextTime) && nextTime.Value > time.Value
                    ? nextTime.Value - time.Value
                    : 120;

                var startDelay = 0.0;
                var endOffset = 0.8 * delay;
                var rangeStart = TryAt(range, 2 * i);
                var rangeEnd = TryAt(range, 2 * i + 1);
                if (Finite(rangeStart) && Finite(rangeEnd) && rangeEnd.Value > rangeStart.Value)
                {
                    startDelay = rangeStart.Value - time.Value;
                    endOffset = rangeEnd.Value - time.Value;
                    if (endOffset <= startDelay || endOffset > delay + 1e-3) { startDelay = 0; endOffset = 0.8 * delay; }
                }
                else if (!hasRangeWarning && range != null)
                {
                    warnings.Add("Some Origin RANGE values were missing or invalid; default integration bounds were used.");
                    hasRangeWarning = true;
                }

                var deltaVolume = experiment.Injections.Sum(injection => injection.Volume) + volume;
                var actualCell = ValueAt(mt, i + 1);
                var actualTitrant = ValueAt(xt, i + 1);
                var ratio = RatioAt(xmt, i);
                if (!Positive(actualCell)) actualCell = cellConcentration * ((1 - deltaVolume / (2 * cellVolume)) / (1 + deltaVolume / (2 * cellVolume)));
                if (!Positive(actualTitrant)) actualTitrant = syringeConcentration * (deltaVolume / cellVolume) * (1 - deltaVolume / (2 * cellVolume));
                if (!Positive(ratio) && Positive(actualCell)) ratio = actualTitrant / actualCell;

                var area = Energy.ConvertToJoule(dh[i].Value, EnergyUnit.MicroCal);
                var injection = new InjectionData(experiment, i, volume, 0, include: i != 0);
                injection.RestoreState(
                    include: i != 0,
                    time: (float)time.Value,
                    volume: volume,
                    delay: (float)delay,
                    duration: (float)(2 * injv[i].Value),
                    filter: 5,
                    temperature: temperature,
                    integrationStartDelay: (float)startDelay,
                    integrationEndOffset: (float)endOffset,
                    actualCellConcentration: actualCell,
                    actualTitrantConcentration: actualTitrant,
                    ratio: ratio,
                    isIntegrated: true,
                    heatDirection: area > 0 ? PeakHeatDirection.Endothermal : area < 0 ? PeakHeatDirection.Exothermal : PeakHeatDirection.Unknown,
                    rawPeakArea: new FloatWithError(area, 0),
                    correctedPeakArea: new FloatWithError(area, 0));
                experiment.Injections.Add(injection);
            }

            experiment.InitialDelay = experiment.Injections.Count > 0 ? experiment.Injections[0].Time : 0;
        }

        static double? TryAt(List<double?> values, int index)
        {
            if (values == null || index < 0 || index >= values.Count) return null;
            return values[index];
        }

        static double ValueAt(List<double?> values, int index)
        {
            var value = TryAt(values, index);
            return Finite(value) ? value.Value * 1e-3 : double.NaN;
        }

        static double RatioAt(List<double?> values, int index)
        {
            var value = TryAt(values, index);
            return Finite(value) ? value.Value : double.NaN;
        }

        static string BuildComments(OriginProjectDocument document, string baseName, string fileName, List<string> warnings)
        {
            var lines = new List<string>
            {
                "Imported from legacy Origin CPYA project: " + fileName,
                "Selected Origin worksheet: " + baseName,
                "Origin signature: " + (document.Signature ?? "CPYA"),
            };
            if (document.OriginVersion.HasValue)
                lines.Add("Origin header version: " + document.OriginVersion.Value.ToString("G17", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(document.OriginVersionText))
                lines.Add("Origin signature version/build: " + document.OriginVersionText);

            lines.Add("Embedded thermogram was restored without Origin baseline/spline processing. Source DH heats are authoritative until the thermogram is reprocessed in FT-ITC Analysis.");
            lines.Add("Origin ResultsLog text was retained as provenance when available. Origin Fit/DY columns, fitted models, and processing state were not imported, and no native solution or result was created.");

            foreach (var warning in warnings.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())
                lines.Add("Import warning: " + warning);

            foreach (var note in document.Notes.Where(note => string.Equals(note.Name, "ResultsLog", StringComparison.OrdinalIgnoreCase)))
            {
                lines.Add("Origin ResultsLog:");
                lines.Add(note.Contents ?? "");
            }

            return string.Join(Environment.NewLine, lines);
        }

        static bool Finite(double? value) => value.HasValue && Finite(value.Value);
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        static bool Positive(double value) => Finite(value) && value > 0;

        internal sealed class OriginDataset
        {
            internal string BaseName { get; }
            internal Dictionary<string, OriginColumn> Columns { get; } = new Dictionary<string, OriginColumn>(StringComparer.OrdinalIgnoreCase);

            internal OriginDataset(string baseName) { BaseName = baseName; }
            internal OriginColumn Column(string suffix) => Columns[suffix];
            internal OriginColumn ColumnOrNull(string suffix)
            {
                OriginColumn column;
                return Columns.TryGetValue(suffix, out column) ? column : null;
            }
        }
    }
}
