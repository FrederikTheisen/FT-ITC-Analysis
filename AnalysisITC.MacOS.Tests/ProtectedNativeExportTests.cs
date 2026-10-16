using System;
using System.IO;
using System.Reflection;
using AppKit;
using AnalysisITC;
using AnalysisITC.Core.Export;

static class ProtectedNativeExportTests
{
    const BindingFlags InternalStatic = BindingFlags.Static | BindingFlags.NonPublic;

    static int Main(string[] args)
    {
        NSApplication.Init();
        var directory = Path.Combine(Path.GetTempPath(), "native-protected-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ResultTable(directory);
            SupportingFigure(directory);
            Console.WriteLine("Native protected result table and Supporting Figure writes passed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally { Directory.Delete(directory, true); }
    }

    static void ResultTable(string directory)
    {
        var path = Path.Combine(directory, "results.csv");
        var write = typeof(AnalysisResultExporterViewController).GetMethod("WriteResultTable", InternalStatic);
        Check(write != null, "Native result table did not expose its protected write boundary");
        var first = (ExportOutcome)write.Invoke(null, new object[] { path, "old,table", ExportBatchWriter.CaptureExistingPaths(new[] { path }) });
        Check(first.IsSuccess && File.ReadAllText(path) == "old,table", "Native result table content changed");
        var approved = ExportBatchWriter.CaptureExistingPaths(new[] { path });
        var replaced = (ExportOutcome)write.Invoke(null, new object[] { path, "new,table", approved });
        Check(replaced.IsSuccess && replaced.ReplacedCount == 1 && File.ReadAllText(path) == "new,table",
            "Native result table did not replace the approved destination");

        var blockedPath = Path.Combine(directory, "missing", "results.csv");
        var failed = (ExportOutcome)write.Invoke(null, new object[] { blockedPath, "unwritten", null });
        Check(!failed.IsSuccess && failed.CompletedCount == 0 && !File.Exists(blockedPath),
            "Native result table reported success for an unavailable destination");
    }

    static void SupportingFigure(string directory)
    {
        var controller = typeof(FinalFigureGraphView).Assembly.GetType("AnalysisITC.SupportingFigureCanvasWindowController", true);
        var write = controller.GetMethod("WriteSupportingFigurePdf", InternalStatic);
        Check(write != null, "Supporting Figure did not expose its protected write boundary");
        var path = Path.Combine(directory, "supporting.pdf");
        var firstBytes = new byte[] { 37, 80, 68, 70, 45, 49 };
        var first = (ExportOutcome)write.Invoke(null, new object[] { path, firstBytes, ExportBatchWriter.CaptureExistingPaths(new[] { path }) });
        Check(first.IsSuccess && Equal(firstBytes, File.ReadAllBytes(path)), "Supporting Figure write changed the PDF bytes");
        var nextBytes = new byte[] { 37, 80, 68, 70, 45, 50 };
        var approved = ExportBatchWriter.CaptureExistingPaths(new[] { path });
        var replaced = (ExportOutcome)write.Invoke(null, new object[] { path, nextBytes, approved });
        Check(replaced.IsSuccess && replaced.ReplacedCount == 1 && Equal(nextBytes, File.ReadAllBytes(path)),
            "Supporting Figure did not replace the approved destination");
    }

    static bool Equal(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        for (var index = 0; index < first.Length; index++)
            if (first[index] != second[index]) return false;
        return true;
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
