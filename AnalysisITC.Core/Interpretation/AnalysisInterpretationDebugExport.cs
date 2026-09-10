using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
                Write(archive, "model-package.json", prompt.ModelPackageJson);
                Write(archive, "output-instructions.txt", prompt.ResponseFormatInstructions);
                var canonicalBytes = Encoding.UTF8.GetByteCount(prompt.CanonicalPackageJson);
                var modelBytes = Encoding.UTF8.GetByteCount(prompt.ModelPackageJson ?? "");
                Write(archive, "manifest.json", JsonSerializer.Serialize(new
                {
                    exportFormat = "ft-itc-interpretation-debug-1",
                    exportedAtUtc = DateTime.UtcNow,
                    relayRequestSchemaVersion = FtItcInterpretationClient.RequestSchemaVersion,
                    outputInstructionIdentifier = prompt.PromptVersion,
                    outputFormatVersion = prompt.OutputFormatVersion,
                    packageSchemaVersion = package.PackageSchemaVersion,
                    evidenceFingerprintScheme = AnalysisInterpretationPromptBuilder.EvidenceFingerprintScheme,
                    evidenceFingerprint = prompt.EvidenceFingerprint,
                    outputInstructionsFingerprint = prompt.OutputInstructionsFingerprint,
                    packageBytes = canonicalBytes,
                    canonicalPackageBytes = canonicalBytes,
                    modelPackageBytes = modelBytes,
                    modelInputEncoding = prompt.ModelInputEncoding,
                    canonicalPackageSha256 = Sha256(prompt.CanonicalPackageJson),
                    modelPackageSha256 = Sha256(prompt.ModelPackageJson ?? ""),
                    precisionPolicy = "Six significant digits for ordinary scientific values; nine for timing, baseline, power, slopes, drift and thermogram extrema; exact precision for thermogram anchor/bin width/offset, information criteria, likelihoods and parameter bounds, with interval and correlation safeguards.",
                    transportPolicy = "Compact model package is sent; thermogram traces are omitted only when the measured relay envelope exceeds the transport limit.",
                    resultCount = package.Results?.Count ?? 0,
                    supportingExperimentCount = package.SupportingExperiments?.Count ?? 0,
                    stage = "local-before-transport",
                    sentToServer = false,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Write(archive, "README.txt",
                    "FT-ITC interpretation input snapshot\n\n" +
                    "package.json is the complete, readable local evidence package. canonical-package.json is its exact canonical serialization. model-package.json is the exact compact payload prepared before transport.\n" +
                    "model-package.json uses the modelInputEncoding recorded in the manifest; table schemas are declared once in the package. When shared experiment evidence is selected, experimentEvidence records are referenced by result members while fitted evidence remains local to each member. Ordinary scientific values use six significant digits and timing, baseline, power, slopes, drift and thermogram extrema use nine, with protected full-precision values and interval/correlation safeguards. The manifest records separate full/model SHA-256 hashes and byte sizes; the full evidence hash determines freshness.\n" +
                    "output-instructions.txt contains the actual application presentation instructions. The manifest identifies the evidence and output-instruction fingerprints.\n\n" +
                    "This export uses the report selection, question, context and thermogram setting currently shown in the dialog. No network request or additional fit is performed.\n" +
                    "This is a snapshot before transport, not an interception of a past model request. If the measured relay envelope exceeds its limit, the actual sent payload may additionally omit thermogram traces. Server scientific instructions are intentionally not exported and cannot be reconstructed from their fingerprint.\n\n" +
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

        static string Sha256(string value)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? "")).Select(item => item.ToString("x2")));
        }
    }
}
