using System;
using System.Collections.Generic;
using System.Formats.Nrbf;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;

using AnalysisITC.Core.Data;
using AnalysisITC.Core.Numerics;

namespace AnalysisITC.Core.DataReaders
{
    /// <summary>
    /// Safely reads native TA Instruments NanoITC files. The format is a gzip
    /// wrapper around an NRBF-serialized ITCData DataSet. This reader inspects
    /// NRBF records and primitive arrays only; it never instantiates serialized
    /// types and never uses BinaryFormatter.
    /// </summary>
    public static class NanoItcReader
    {
        internal const long DefaultDecompressedSizeLimit = 256L * 1024 * 1024;
        internal const int DefaultTableRowLimit = 2_000_000;

        public static ExperimentData ReadPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new FileNotFoundException("NanoITC file was not found.", path);

            using (var stream = File.OpenRead(path))
            {
                return ReadStream(
                    stream,
                    Path.GetFileName(path),
                    File.GetLastWriteTime(path));
            }
        }

        internal static ExperimentData ReadStream(
            Stream stream,
            string fileName,
            DateTime? fileSystemDate = null,
            long decompressedSizeLimit = DefaultDecompressedSizeLimit,
            int tableRowLimit = DefaultTableRowLimit)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanRead) throw new ArgumentException("The NanoITC stream must be readable.", nameof(stream));
            if (decompressedSizeLimit <= 0) throw new ArgumentOutOfRangeException(nameof(decompressedSizeLimit));
            if (tableRowLimit <= 0) throw new ArgumentOutOfRangeException(nameof(tableRowLimit));

            var displayName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? "experiment.nitc" : fileName);
            var payload = Decompress(stream, decompressedSizeLimit);
            var document = NanoItcNrbfParser.Read(payload, tableRowLimit);
            return NanoItcExperimentMapper.Map(document, displayName, fileSystemDate);
        }

        static MemoryStream Decompress(Stream source, long sizeLimit)
        {
            Stream gzipSource = source;
            MemoryStream bufferedSource = null;
            var startPosition = source.CanSeek ? source.Position : 0;

            try
            {
                var first = source.ReadByte();
                var second = source.ReadByte();
                if (first != 0x1f || second != 0x8b)
                    throw new FormatException("NanoITC gzip stage: the file does not have a gzip signature.");

                if (source.CanSeek)
                {
                    source.Position = startPosition;
                }
                else
                {
                    bufferedSource = new MemoryStream();
                    bufferedSource.WriteByte((byte)first);
                    bufferedSource.WriteByte((byte)second);
                    source.CopyTo(bufferedSource);
                    bufferedSource.Position = 0;
                    gzipSource = bufferedSource;
                }

                var result = new MemoryStream();
                using (var gzip = new GZipStream(gzipSource, CompressionMode.Decompress, leaveOpen: true))
                {
                    var buffer = new byte[81920];
                    while (true)
                    {
                        var read = gzip.Read(buffer, 0, buffer.Length);
                        if (read == 0) break;
                        if (result.Length > sizeLimit - read)
                        {
                            result.Dispose();
                            throw new FormatException($"NanoITC gzip stage: decompressed data exceeds the {sizeLimit} byte limit.");
                        }
                        result.Write(buffer, 0, read);
                    }
                }

                result.Position = 0;
                return result;
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex) when (IsPayloadException(ex))
            {
                throw new FormatException("NanoITC gzip stage: the compressed payload is truncated or malformed.", ex);
            }
            finally
            {
                bufferedSource?.Dispose();
            }
        }

        internal static bool IsPayloadException(Exception ex) =>
            ex is IOException ||
            ex is EndOfStreamException ||
            ex is InvalidDataException ||
            ex is SerializationException ||
            ex is InvalidOperationException ||
            ex is ArgumentException ||
            ex is OverflowException ||
            ex is NotSupportedException;
    }

    internal sealed class NanoItcDocument
    {
        internal string RootType { get; set; }
        internal Dictionary<string, NanoItcTable> Tables { get; } =
            new Dictionary<string, NanoItcTable>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class NanoItcTable
    {
        internal NanoItcTable(
            string name,
            IReadOnlyList<string> columnNames,
            IReadOnlyList<Array> stores,
            IReadOnlyList<NanoItcBitArray> nullBits,
            IReadOnlyList<int> activeRecordIndices)
        {
            Name = name;
            ColumnNames = columnNames;
            Stores = stores;
            NullBits = nullBits;
            ActiveRecordIndices = activeRecordIndices;

            if (stores.Count != columnNames.Count || nullBits.Count != columnNames.Count)
                throw NanoItcNrbfParser.SchemaError($"table '{name}' has inconsistent column storage.");
            foreach (var recordIndex in activeRecordIndices)
            {
                if (recordIndex < 0 || stores.Any(store => recordIndex >= store.Length) || nullBits.Any(mask => recordIndex >= mask.Length))
                    throw NanoItcNrbfParser.DataError($"table '{name}' references a record outside its column storage.");
            }

            ColumnLookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < columnNames.Count; index++)
            {
                if (string.IsNullOrWhiteSpace(columnNames[index]))
                    throw NanoItcNrbfParser.SchemaError($"table '{name}' contains an unnamed column.");
                if (ColumnLookup.ContainsKey(columnNames[index]))
                    throw NanoItcNrbfParser.SchemaError($"table '{name}' contains duplicate column '{columnNames[index]}'.");
                ColumnLookup.Add(columnNames[index], index);
            }
        }

        internal string Name { get; }
        internal int RowCount => ActiveRecordIndices.Count;
        internal IReadOnlyList<string> ColumnNames { get; }
        internal IReadOnlyList<Array> Stores { get; }
        internal IReadOnlyList<NanoItcBitArray> NullBits { get; }
        internal IReadOnlyList<int> ActiveRecordIndices { get; }
        readonly Dictionary<string, int> ColumnLookup;

        internal void RequireColumns(params string[] names)
        {
            foreach (var name in names)
            {
                if (!ColumnLookup.ContainsKey(name))
                    throw NanoItcNrbfParser.SchemaError($"table '{Name}' is missing required column '{name}'.");
            }
        }

        internal object GetRequiredValue(int row, string column)
        {
            if (row < 0 || row >= RowCount)
                throw NanoItcNrbfParser.DataError($"table '{Name}' row {row} is out of range.");
            if (!ColumnLookup.TryGetValue(column, out var columnIndex))
                throw NanoItcNrbfParser.SchemaError($"table '{Name}' is missing required column '{column}'.");

            var recordIndex = ActiveRecordIndices[row];
            if (NullBits[columnIndex].Get(recordIndex))
                throw NanoItcNrbfParser.DataError($"table '{Name}' column '{column}' contains a null at row {row}.");

            var value = Stores[columnIndex].GetValue(recordIndex);
            if (value == null)
                throw NanoItcNrbfParser.DataError($"table '{Name}' column '{column}' contains a null at row {row}.");
            return value;
        }

        internal double GetRequiredNumber(int row, string column)
        {
            var value = GetRequiredValue(row, column);
            double number;
            switch (value)
            {
                case byte v: number = v; break;
                case sbyte v: number = v; break;
                case short v: number = v; break;
                case ushort v: number = v; break;
                case int v: number = v; break;
                case uint v: number = v; break;
                case long v: number = v; break;
                case ulong v: number = v; break;
                case float v: number = v; break;
                case double v: number = v; break;
                case decimal v: number = (double)v; break;
                default:
                    throw NanoItcNrbfParser.DataError(
                        $"table '{Name}' column '{column}' has unsupported numeric value type '{value.GetType().FullName}'.");
            }

            if (double.IsNaN(number) || double.IsInfinity(number))
                throw NanoItcNrbfParser.DataError($"table '{Name}' column '{column}' contains a non-finite value at row {row}.");
            return number;
        }

        internal string GetRequiredString(int row, string column)
        {
            var value = GetRequiredValue(row, column);
            if (value is string text) return text;
            throw NanoItcNrbfParser.DataError(
                $"table '{Name}' column '{column}' has unsupported value type '{value.GetType().FullName}'.");
        }
    }

    internal sealed class NanoItcBitArray
    {
        internal NanoItcBitArray(int[] words, int length)
        {
            if (length < 0 || words == null || words.Length != (length + 31) / 32)
                throw NanoItcNrbfParser.DataError("a serialized bit array has inconsistent storage.");
            Words = words;
            Length = length;
        }

        internal int Length { get; }
        readonly int[] Words;

        internal bool Get(int index)
        {
            if (index < 0 || index >= Length)
                throw NanoItcNrbfParser.DataError("a serialized bit array is shorter than its associated records.");
            return (Words[index / 32] & (1 << (index % 32))) != 0;
        }
    }

    internal static class NanoItcNrbfParser
    {
        const int MaxTables = 1024;
        const int MaxColumns = 512;
        const string ExpectedRootType = "CSC.ITCData.ITCData";

        internal static NanoItcDocument Read(Stream payload, int tableRowLimit)
        {
            ClassRecord root;
            try
            {
                if (!NrbfDecoder.StartsWithPayloadHeader(payload))
                    throw NrbfError("the decompressed payload does not have an NRBF header.");
                payload.Position = 0;
                root = NrbfDecoder.Decode(payload, leaveOpen: true) as ClassRecord;
                if (root == null) throw NrbfError("the NRBF root is not a class record.");

                var rootType = TypeName(root);
                if (!string.Equals(rootType, ExpectedRootType, StringComparison.Ordinal))
                    throw NrbfError($"unsupported root type '{rootType}'.");

                return ReadDataSet(root, rootType, tableRowLimit);
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex) when (NanoItcReader.IsPayloadException(ex))
            {
                throw new FormatException("NanoITC NRBF stage: the payload is truncated or malformed.", ex);
            }
            finally
            {
                payload.Dispose();
            }
        }

        static NanoItcDocument ReadDataSet(ClassRecord root, string rootType, int tableRowLimit)
        {
            try
            {
                var tableCount = root.GetInt32("DataSet.Tables.Count");
                if (tableCount < 0 || tableCount > MaxTables)
                    throw SchemaError($"the DataSet declares an invalid table count ({tableCount}).");

                var document = new NanoItcDocument { RootType = rootType };
                for (var tableIndex = 0; tableIndex < tableCount; tableIndex++)
                {
                    var schema = ReadTableSchema(root, tableIndex);
                    if (!IsRequiredTable(schema.Name)) continue;
                    if (document.Tables.ContainsKey(schema.Name))
                        throw SchemaError($"the DataSet contains duplicate table '{schema.Name}'.");

                    var table = ReadTableData(root, tableIndex, schema, tableRowLimit);
                    document.Tables.Add(schema.Name, table);
                }

                foreach (var required in new[] { "DataPointsTable", "InjectionsTable", "KVP" })
                {
                    if (!document.Tables.ContainsKey(required))
                        throw SchemaError($"the DataSet is missing required table '{required}'.");
                }

                return document;
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex) when (NanoItcReader.IsPayloadException(ex))
            {
                throw new FormatException("NanoITC schema stage: the serialized DataSet schema is malformed.", ex);
            }
        }

        static NanoItcTableSchema ReadTableSchema(ClassRecord root, int tableIndex)
        {
            var member = $"DataSet.Tables_{tableIndex}";
            if (!root.HasMember(member))
                throw SchemaError($"the DataSet is missing schema payload '{member}'.");

            var bytes = MaterializeArray(root.GetArrayRecord(member)) as byte[];
            if (bytes == null) throw SchemaError($"table {tableIndex} has a non-byte schema payload.");

            try
            {
                using (var schemaStream = new MemoryStream(bytes, writable: false))
                {
                    if (!NrbfDecoder.StartsWithPayloadHeader(schemaStream))
                        throw SchemaError($"table {tableIndex} schema does not have an NRBF header.");
                    schemaStream.Position = 0;
                    var schemaRoot = NrbfDecoder.Decode(schemaStream, leaveOpen: false) as ClassRecord;
                    if (schemaRoot == null)
                        throw SchemaError($"table {tableIndex} schema root is not a class record.");

                    var tableName = schemaRoot.GetString("DataTable.TableName");
                    var columnCount = schemaRoot.GetInt32("DataTable.Columns.Count");
                    if (string.IsNullOrWhiteSpace(tableName))
                        throw SchemaError($"table {tableIndex} has no name.");
                    if (columnCount < 0 || columnCount > MaxColumns)
                        throw SchemaError($"table '{tableName}' declares an invalid column count ({columnCount}).");

                    var columns = new List<string>(columnCount);
                    for (var column = 0; column < columnCount; column++)
                        columns.Add(schemaRoot.GetString($"DataTable.DataColumn_{column}.ColumnName"));
                    return new NanoItcTableSchema(tableName, columns);
                }
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex) when (NanoItcReader.IsPayloadException(ex))
            {
                throw new FormatException($"NanoITC schema stage: table {tableIndex} schema is truncated or malformed.", ex);
            }
        }

        static NanoItcTable ReadTableData(
            ClassRecord root,
            int tableIndex,
            NanoItcTableSchema schema,
            int tableRowLimit)
        {
            var prefix = $"DataTable_{tableIndex}";
            var rowCount = root.GetInt32(prefix + ".Rows.Count");
            var recordCount = root.GetInt32(prefix + ".Records.Count");
            if (rowCount < 0 || rowCount > tableRowLimit)
                throw SchemaError($"table '{schema.Name}' declares an invalid or excessive row count ({rowCount}).");
            if (recordCount < 0 || recordCount > checked(tableRowLimit * 3))
                throw SchemaError($"table '{schema.Name}' declares an invalid or excessive record count ({recordCount}).");

            var rowStates = ReadBitArray(root.GetClassRecord(prefix + ".RowStates"), checked(rowCount * 3), schema.Name + " row states");
            var activeRecords = GetActiveRecordIndices(rowStates, rowCount, recordCount, schema.Name);

            var storeRecords = ReadArrayList(root.GetClassRecord(prefix + ".Records"), schema.ColumnNames.Count, schema.Name + " records");
            var nullRecords = ReadArrayList(root.GetClassRecord(prefix + ".NullBits"), schema.ColumnNames.Count, schema.Name + " null masks");
            var stores = new List<Array>(schema.ColumnNames.Count);
            var nullBits = new List<NanoItcBitArray>(schema.ColumnNames.Count);

            for (var column = 0; column < schema.ColumnNames.Count; column++)
            {
                if (!(storeRecords[column] is ArrayRecord arrayRecord))
                    throw SchemaError($"table '{schema.Name}' column '{schema.ColumnNames[column]}' has no primitive array storage.");

                var store = MaterializeArray(arrayRecord);
                if (store.Length != recordCount)
                    throw DataError($"table '{schema.Name}' column '{schema.ColumnNames[column]}' has {store.Length} values but declares {recordCount} records.");
                stores.Add(store);

                if (!(nullRecords[column] is ClassRecord nullMask))
                    throw SchemaError($"table '{schema.Name}' column '{schema.ColumnNames[column]}' has no null mask.");
                nullBits.Add(ReadBitArray(nullMask, recordCount, schema.Name + " null mask"));
            }

            return new NanoItcTable(schema.Name, schema.ColumnNames, stores, nullBits, activeRecords);
        }

        static IReadOnlyList<int> GetActiveRecordIndices(
            NanoItcBitArray states,
            int rowCount,
            int recordCount,
            string tableName)
        {
            var active = new List<int>(rowCount);
            var recordIndex = 0;
            for (var row = 0; row < rowCount; row++)
            {
                var bitIndex = row * 3;
                var first = states.Get(bitIndex);
                var second = states.Get(bitIndex + 1);
                var hasTemporaryRecord = states.Get(bitIndex + 2);

                if (!first && !second) // Unchanged
                {
                    RequireRecord(recordIndex, recordCount, tableName);
                    active.Add(recordIndex++);
                }
                else if (!first && second) // Added
                {
                    RequireRecord(recordIndex, recordCount, tableName);
                    active.Add(recordIndex++);
                }
                else if (first && !second) // Modified: old record followed by current record
                {
                    RequireRecord(recordIndex + 1, recordCount, tableName);
                    active.Add(recordIndex + 1);
                    recordIndex += 2;
                }
                else // Deleted: retain no active row, but consume the old record
                {
                    RequireRecord(recordIndex, recordCount, tableName);
                    recordIndex++;
                }

                if (hasTemporaryRecord)
                {
                    RequireRecord(recordIndex, recordCount, tableName);
                    recordIndex++;
                }
            }

            if (recordIndex != recordCount)
                throw DataError($"table '{tableName}' row states consume {recordIndex} records but {recordCount} were declared.");
            return active;
        }

        static void RequireRecord(int recordIndex, int recordCount, string tableName)
        {
            if (recordIndex < 0 || recordIndex >= recordCount)
                throw DataError($"table '{tableName}' row states reference a missing record.");
        }

        static object[] ReadArrayList(ClassRecord list, int expectedSize, string description)
        {
            var typeName = TypeName(list);
            if (!string.Equals(typeName, "System.Collections.ArrayList", StringComparison.Ordinal))
                throw SchemaError($"{description} use unsupported list type '{typeName}'.");

            var size = list.GetInt32("_size");
            if (size != expectedSize)
                throw SchemaError($"{description} contain {size} columns but {expectedSize} were declared.");
            var items = MaterializeArray(list.GetArrayRecord("_items")) as object[];
            if (items == null || items.Length < size)
                throw SchemaError($"{description} have truncated backing storage.");
            return items.Take(size).ToArray();
        }

        static NanoItcBitArray ReadBitArray(ClassRecord record, int expectedLength, string description)
        {
            var typeName = TypeName(record);
            if (!string.Equals(typeName, "System.Collections.BitArray", StringComparison.Ordinal))
                throw SchemaError($"{description} use unsupported bitmap type '{typeName}'.");

            var length = record.GetInt32("m_length");
            if (length != expectedLength)
                throw DataError($"{description} contain {length} bits but {expectedLength} were expected.");
            var words = MaterializeArray(record.GetArrayRecord("m_array")) as int[];
            var expectedWords = (expectedLength + 31) / 32;
            if (words == null || words.Length != expectedWords)
                throw DataError($"{description} have an inconsistent storage length.");
            return new NanoItcBitArray(words, length);
        }

        static Array MaterializeArray(ArrayRecord record)
        {
            if (record.Rank != 1) throw SchemaError("multidimensional array storage is not supported.");

            var typeName = TypeName(record);
            try
            {
                switch (typeName)
                {
                    case "System.Object[]": return record.GetArray(typeof(object[]), allowNulls: true);
                    case "System.String[]": return record.GetArray(typeof(string[]), allowNulls: true);
                    case "System.Boolean[]": return record.GetArray(typeof(bool[]), allowNulls: false);
                    case "System.Byte[]": return record.GetArray(typeof(byte[]), allowNulls: false);
                    case "System.SByte[]": return record.GetArray(typeof(sbyte[]), allowNulls: false);
                    case "System.Int16[]": return record.GetArray(typeof(short[]), allowNulls: false);
                    case "System.UInt16[]": return record.GetArray(typeof(ushort[]), allowNulls: false);
                    case "System.Int32[]": return record.GetArray(typeof(int[]), allowNulls: false);
                    case "System.UInt32[]": return record.GetArray(typeof(uint[]), allowNulls: false);
                    case "System.Int64[]": return record.GetArray(typeof(long[]), allowNulls: false);
                    case "System.UInt64[]": return record.GetArray(typeof(ulong[]), allowNulls: false);
                    case "System.Single[]": return record.GetArray(typeof(float[]), allowNulls: false);
                    case "System.Double[]": return record.GetArray(typeof(double[]), allowNulls: false);
                    case "System.Decimal[]": return record.GetArray(typeof(decimal[]), allowNulls: false);
                    case "System.Char[]": return record.GetArray(typeof(char[]), allowNulls: false);
                    default: throw SchemaError($"unsupported primitive array type '{typeName}'.");
                }
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex) when (NanoItcReader.IsPayloadException(ex))
            {
                throw new FormatException($"NanoITC data stage: array '{typeName}' is malformed.", ex);
            }
        }

        static string TypeName(SerializationRecord record)
        {
            var name = record.TypeName.AssemblyQualifiedName;
            var separator = name.IndexOf(',');
            return separator < 0 ? name : name.Substring(0, separator);
        }

        static bool IsRequiredTable(string name) =>
            string.Equals(name, "DataPointsTable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "InjectionsTable", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "KVP", StringComparison.OrdinalIgnoreCase);

        internal static FormatException NrbfError(string message) =>
            new FormatException("NanoITC NRBF stage: " + message);

        internal static FormatException SchemaError(string message) =>
            new FormatException("NanoITC schema stage: " + message);

        internal static FormatException DataError(string message) =>
            new FormatException("NanoITC data stage: " + message);

        sealed class NanoItcTableSchema
        {
            internal NanoItcTableSchema(string name, IReadOnlyList<string> columnNames)
            {
                Name = name;
                ColumnNames = columnNames;
            }

            internal string Name { get; }
            internal IReadOnlyList<string> ColumnNames { get; }
        }
    }

    internal static class NanoItcExperimentMapper
    {
        internal static ExperimentData Map(NanoItcDocument document, string fileName, DateTime? fileSystemDate)
        {
            var dataPoints = document.Tables["DataPointsTable"];
            var injections = document.Tables["InjectionsTable"];
            var kvpTable = document.Tables["KVP"];

            dataPoints.RequireColumns("Time", "HeatRate", "Temperture");
            injections.RequireColumns("StartTime", "Size", "InjectionInterval");
            kvpTable.RequireColumns("Key", "Value");
            if (dataPoints.RowCount < 2)
                throw NanoItcNrbfParser.DataError("DataPointsTable must contain at least two active rows.");
            if (injections.RowCount == 0)
                throw NanoItcNrbfParser.DataError("InjectionsTable must contain at least one active row.");

            var metadata = ReadMetadata(kvpTable);
            var cellVolumeMicrolitres = RequiredMetadataNumber(metadata, "cellvolume");
            if (cellVolumeMicrolitres <= 0)
                throw NanoItcNrbfParser.DataError("KVP metadata 'cellvolume' must be positive.");
            var experiment = new ExperimentData(fileName)
            {
                DataSourceFormat = ITCDataFormat.NanoITC,
                CellVolume = cellVolumeMicrolitres * 1e-6,
                CellConcentration = new FloatWithError(OptionalMetadataNumber(metadata, "cellconcentration") * 1e-3),
                SyringeConcentration = new FloatWithError(OptionalMetadataNumber(metadata, "syringeconcentration") * 1e-3),
                StirringSpeed = OptionalMetadataNumber(metadata, "stirrate", -1),
                TargetTemperature = OptionalMetadataNumber(metadata, "tempsetpoint"),
                Comments = BuildComments(metadata),
            };

            if (TryReadEmbeddedDate(metadata, out var embeddedDate))
            {
                experiment.Date = embeddedDate;
                experiment.DateSource = ExperimentDateSource.DataFile;
            }
            else if (fileSystemDate.HasValue)
            {
                experiment.Date = fileSystemDate.Value;
                experiment.DateSource = ExperimentDateSource.FileSystem;
            }

            experiment.DataPoints = new List<DataPoint>(dataPoints.RowCount);
            for (var row = 0; row < dataPoints.RowCount; row++)
            {
                var timeSeconds = dataPoints.GetRequiredNumber(row, "Time");
                var heatRateJoulesPerSecond = dataPoints.GetRequiredNumber(row, "HeatRate") * 1e-6;
                var temperatureCelsius = dataPoints.GetRequiredNumber(row, "Temperture");
                experiment.DataPoints.Add(new DataPoint(
                    ToFiniteFloat(timeSeconds, "DataPointsTable.Time", row),
                    ToFiniteFloat(heatRateJoulesPerSecond, "DataPointsTable.HeatRate", row),
                    ToFiniteFloat(temperatureCelsius, "DataPointsTable.Temperture", row)));
            }

            var injectionRate = OptionalMetadataNumber(metadata, "injrate", double.NaN);
            experiment.Injections = new List<InjectionData>(injections.RowCount);
            for (var row = 0; row < injections.RowCount; row++)
            {
                var startTime = injections.GetRequiredNumber(row, "StartTime");
                var sizeMicrolitres = injections.GetRequiredNumber(row, "Size");
                var interval = injections.GetRequiredNumber(row, "InjectionInterval");
                if (sizeMicrolitres < 0 || interval < 0)
                    throw NanoItcNrbfParser.DataError($"InjectionsTable contains a negative size or interval at row {row}.");

                var duration = injectionRate > 0 && !double.IsInfinity(injectionRate)
                    ? sizeMicrolitres / injectionRate
                    : 0;
                var temperature = NearestTemperature(experiment.DataPoints, startTime);
                var injection = InjectionData.FromNanoItcValues(
                    experiment,
                    row,
                    startTime,
                    sizeMicrolitres * 1e-6,
                    interval,
                    duration,
                    temperature);
                injection.InitializeIntegrationTimes();
                experiment.Injections.Add(injection);
            }

            experiment.InitialDelay = experiment.Injections[0].Time;
            experiment.TargetPowerDiff = experiment.DataPoints[0].Power;
            RawDataReader.ProcessInjections(experiment);
            RawDataReader.ProcessExperiment(experiment);
            return experiment;
        }

        static Dictionary<string, string> ReadMetadata(NanoItcTable table)
        {
            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var row = 0; row < table.RowCount; row++)
            {
                var key = table.GetRequiredString(row, "Key");
                var value = table.GetRequiredString(row, "Value");
                if (string.IsNullOrWhiteSpace(key))
                    throw NanoItcNrbfParser.DataError($"KVP contains an empty key at row {row}.");
                if (metadata.ContainsKey(key))
                    throw NanoItcNrbfParser.DataError($"KVP contains duplicate key '{key}'.");
                metadata.Add(key, value ?? "");
            }
            return metadata;
        }

        static double RequiredMetadataNumber(IReadOnlyDictionary<string, string> metadata, string key)
        {
            if (!metadata.TryGetValue(key, out var text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw NanoItcNrbfParser.DataError($"KVP is missing valid numeric metadata '{key}'.");
            return value;
        }

        static double OptionalMetadataNumber(
            IReadOnlyDictionary<string, string> metadata,
            string key,
            double fallback = 0)
        {
            if (!metadata.TryGetValue(key, out var text) ||
                !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                return fallback;
            return value;
        }

        static bool TryReadEmbeddedDate(IReadOnlyDictionary<string, string> metadata, out DateTime date)
        {
            date = default;
            if (!metadata.TryGetValue("starttime", out var text) ||
                !long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                return false;

            date = new DateTime(ticks, DateTimeKind.Unspecified);
            return true;
        }

        static string BuildComments(IReadOnlyDictionary<string, string> metadata)
        {
            metadata.TryGetValue("comments", out var vendorComment);
            var provenance = new List<string>();
            AddProvenance(provenance, metadata, "FileVersion", "file");
            AddProvenance(provenance, metadata, "serialNumber", "serial");
            AddProvenance(provenance, metadata, "softwareVer", "software");
            AddProvenance(provenance, metadata, "firmwareVer", "firmware");

            var provenanceText = provenance.Count == 0
                ? "NanoITC source"
                : "NanoITC source: " + string.Join("; ", provenance);
            if (string.IsNullOrEmpty(vendorComment)) return provenanceText;
            return vendorComment + Environment.NewLine + provenanceText;
        }

        static void AddProvenance(
            ICollection<string> parts,
            IReadOnlyDictionary<string, string> metadata,
            string key,
            string label)
        {
            if (!metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) return;
            parts.Add(label + " " + NormalizeWhitespace(value));
        }

        static string NormalizeWhitespace(string value) =>
            string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        static double NearestTemperature(IReadOnlyList<DataPoint> points, double time)
        {
            var low = 0;
            var high = points.Count - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                if (points[middle].Time < time) low = middle + 1;
                else if (points[middle].Time > time) high = middle - 1;
                else return points[middle].Temperature;
            }

            if (low <= 0) return points[0].Temperature;
            if (low >= points.Count) return points[points.Count - 1].Temperature;
            var before = points[low - 1];
            var after = points[low];
            return Math.Abs(before.Time - time) <= Math.Abs(after.Time - time)
                ? before.Temperature
                : after.Temperature;
        }

        static float ToFiniteFloat(double value, string field, int row)
        {
            var result = (float)value;
            if (float.IsNaN(result) || float.IsInfinity(result))
                throw NanoItcNrbfParser.DataError($"{field} at row {row} is outside the supported range.");
            return result;
        }
    }
}
