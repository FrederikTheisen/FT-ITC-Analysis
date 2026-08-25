#r "../AnalysisITC.Core/bin/Debug/netstandard2.0/AnalysisITC.Core.dll"

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

var parserType = Type.GetType("AnalysisITC.Core.DataReaders.OriginProjectParser, AnalysisITC.Core")
    ?? throw new InvalidOperationException("OriginProjectParser was not found. Build AnalysisITC.Core first.");
var readMethod = parserType.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
    ?? throw new InvalidOperationException("OriginProjectParser.Read was not found.");

if (Args.Count < 2 || ((Args.Count - 0) % 2) != 0)
    throw new ArgumentException("Usage: csi scripts/extract-elife-opj-dh.csx source.OPJ output.DH [source.OPJ output.DH ...]");

for (var i = 0; i < Args.Count; i += 2)
{
    var sourcePath = Path.GetFullPath(Args[i]);
    var outputPath = Path.GetFullPath(Args[i + 1]);
    Extract(sourcePath, outputPath);
}

void Extract(string sourcePath, string outputPath)
{
    var document = ReadDocument(sourcePath);
    var columnsProperty = document.GetType().GetProperty("Columns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Origin document has no Columns property.");
    var columns = ((IEnumerable)columnsProperty.GetValue(document)!).Cast<object>().ToList();

    var columnByName = columns.ToDictionary(
        c => (string)c.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(c)!,
        StringComparer.OrdinalIgnoreCase);

    var selected = columns
        .Select(c => new
        {
            Column = c,
            Name = (string)c.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(c)!
        })
        .Where(x => x.Name.EndsWith("_DH", StringComparison.OrdinalIgnoreCase))
        .Select(x => new
        {
            x.Column,
            x.Name,
            Prefix = x.Name[..^3]
        })
        .FirstOrDefault(x =>
        {
            if (!columnByName.TryGetValue(x.Prefix + "_INJV", out var injvColumn)) return false;
            var dh = NumericValues(x.Column).ToArray();
            var injv = NumericValues(injvColumn).ToArray();
            return dh.Length >= 3 && injv.Length >= dh.Length && dh.All(IsFinite) && injv.Take(dh.Length).All(IsFinite);
        })
        ?? throw new InvalidOperationException($"No complete direct DH/INJV worksheet found in {sourcePath}.");

    var dhValues = NumericValues(selected.Column).ToArray();
    var injvValues = NumericValues(columnByName[selected.Prefix + "_INJV"]).Take(dhValues.Length).ToArray();
    if (dhValues.Length != injvValues.Length)
        throw new InvalidOperationException($"DH/INJV length mismatch in {sourcePath}: {dhValues.Length} vs {injvValues.Length}.");

    var parametersProperty = document.GetType().GetProperty("Parameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Origin document has no Parameters property.");
    var parameters = (IDictionary)parametersProperty.GetValue(document)!;
    var normalizedPrefix = Normalize(selected.Prefix);
    var temperature = Parameter(parameters, p => p == "TEMP" + normalizedPrefix);
    var cellConcentration = Parameter(parameters, p => p == "CELLC" + normalizedPrefix);
    var syringeConcentration = Parameter(parameters, p => p == "SYRNGC" + normalizedPrefix);
    var cellVolume = Parameter(parameters, p => p == "ITCCELLVOL");

    var lines = new List<string>
    {
        dhValues.Length.ToString(CultureInfo.InvariantCulture),
        $"0,{dhValues.Length.ToString(CultureInfo.InvariantCulture)},0,0,0",
        string.Join(",", new[] { temperature, cellConcentration, syringeConcentration, cellVolume, 0.0 }
            .Select(v => v.ToString("G17", CultureInfo.InvariantCulture))),
        "0",
        "0"
    };

    for (var i = 0; i < dhValues.Length; i++)
        lines.Add($"{injvValues[i].ToString("G17", CultureInfo.InvariantCulture)},{dhValues[i].ToString("G17", CultureInfo.InvariantCulture)}");

    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllLines(outputPath, lines);
    Console.WriteLine($"{Path.GetFileName(sourcePath)} -> {Path.GetFileName(outputPath)}: {selected.Name}, {dhValues.Length} injections");
}

object ReadDocument(string sourcePath)
{
    using var stream = File.OpenRead(sourcePath);
    return readMethod.Invoke(null, new object[] { stream })!;
}

IEnumerable<double> NumericValues(object column)
{
    var method = column.GetType().GetMethod("NumericValues", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Origin column has no NumericValues method.");
    return ((IEnumerable)method.Invoke(column, null)!).Cast<object>().Select(Convert.ToDouble);
}

double Parameter(IDictionary parameters, Func<string, bool> predicate)
{
    foreach (DictionaryEntry entry in parameters)
    {
        var key = Normalize(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty);
        if (predicate(key)) return Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture);
    }

    throw new InvalidOperationException("Required Origin metadata parameter was not found.");
}

string Normalize(string value) => value.Replace("_", string.Empty).ToUpperInvariant();
bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
