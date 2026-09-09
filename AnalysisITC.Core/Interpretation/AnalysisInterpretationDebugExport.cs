using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace AnalysisITC.Core.Interpretation
{
    public static class AnalysisInterpretationDebugExport
    {
        public static byte[] CreateArchive(AnalysisInterpretationPackage package)
        {
            var id = Guid.NewGuid().ToString("N");
            var prompt = AnalysisInterpretationPromptBuilder.Build(package, id);
            using var output = new MemoryStream();
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                Write(archive, "canonical-package.json", prompt.CanonicalPackageJson);
                using var json = JsonDocument.Parse(prompt.CanonicalPackageJson);
                Write(archive, "package.json", JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = true }));
                Write(archive, "system-instructions.txt", prompt.SystemInstructions);
                Write(archive, "user-message.txt", prompt.UserMessage);
                Write(archive, "output-format.txt", prompt.ResponseFormatInstructions);
                Write(archive, "manifest.json", JsonSerializer.Serialize(new
                {
                    exportFormat = "ft-itc-interpretation-debug-1",
                    exportedAtUtc = DateTime.UtcNow,
                    promptVersion = prompt.PromptVersion,
                    outputFormatVersion = prompt.OutputFormatVersion,
                    packageSchemaVersion = package.PackageSchemaVersion,
                    inputFingerprint = prompt.InputFingerprint,
                    packageBytes = Encoding.UTF8.GetByteCount(prompt.CanonicalPackageJson),
                    resultCount = package.Results?.Count ?? 0,
                    supportingExperimentCount = package.SupportingExperiments?.Count ?? 0,
                    stage = "local-before-transport",
                    sentToServer = false,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Write(archive, "README.txt",
                    "FT-ITC interpretation input snapshot\n\n" +
                    "package.json is the complete, readable local evidence package. canonical-package.json is its exact compact serialization.\n" +
                    "The text files contain the locally built prompt and output instructions. The manifest identifies their versions and input fingerprint.\n\n" +
                    "This export uses the report selection, question, context and thermogram setting currently shown in the dialog. No network request or additional fit is performed.\n" +
                    "This is a snapshot before transport, not an interception of a past model request. Sending a large report can omit thermograms to meet the transport limit; the server rebuilds its prompt and may apply context/retrieval fallbacks. Server instructions and retrieved literature are not captured here.\n\n" +
                    "The archive contains the supplied scientific data, names, comments and context. It contains no API credentials. Review it before sharing.\n");
            }
            var bytes = output.ToArray();
            AnalysisInterpretationLog.Write("debug-export-ready", id, $"archiveBytes={bytes.Length} fingerprint={prompt.InputFingerprint}");
            return bytes;
        }

        static void Write(ZipArchive archive, string name, string text)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }
}
