using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AnalysisITC.Core.Data;
using AnalysisITC.Core.Export;
using Xunit;

namespace AnalysisITC.Core.Tests
{
    public sealed class ExportBatchWriterTests
    {
        [Theory]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("  ..  ")]
        public void FigureFolderNamesCannotReferToTheParentOrCurrentDirectory(string name)
        {
            Assert.Equal("Untitled Figure", FigureExportPlanner.SanitizeFileName(name));
        }

        [Fact]
        public void FigurePlannerRetainsDotsInExperimentNames()
        {
            var experiment = new ExperimentData("source.itc") { Name = "Protein.1" };
            var target = Assert.Single(FigureExportPlanner.CreateTargets(new[] { experiment }, "figures"));
            Assert.Equal("Protein.1.pdf", Path.GetFileName(target.Path));
        }

        [Fact]
        public void NewlyCreatedUnapprovedDestinationStopsWithoutReplacingIt()
        {
            var fs = new MemoryExportFileSystem();
            fs.Files["first"] = Bytes("old-first");
            fs.OnMove = destination =>
            {
                if (destination == "second") fs.Files[destination] = Bytes("racing-file");
            };

            var outcome = ExportBatchWriter.Write(new[] { "first", "second", "third" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "first" },
                (index, stream) => Write(stream, "new-" + index), null, fs);

            Assert.False(outcome.IsSuccess);
            Assert.Equal(3, outcome.PlannedCount);
            Assert.Equal(1, outcome.CompletedCount);
            Assert.Equal(1, outcome.ReplacedCount);
            Assert.Equal("second", outcome.FailedPath);
            Assert.Contains("replacement was not approved", outcome.Error.Message);
            Assert.Equal("racing-file", Text(fs.Files["second"]));
            Assert.False(fs.Files.ContainsKey("third"));
        }

        [Fact]
        public void FailureAfterTwoReplacementsKeepsThirdAndLaterOldFiles()
        {
            var fs = new MemoryExportFileSystem { FailReplaceFor = "third" };
            foreach (var path in new[] { "first", "second", "third", "fourth" }) fs.Files[path] = Bytes("old-" + path);
            var approved = new HashSet<string>(new[] { "first", "second", "third", "fourth" }, StringComparer.OrdinalIgnoreCase);

            var outcome = ExportBatchWriter.Write(new[] { "first", "second", "third", "fourth" }, approved,
                (index, stream) => Write(stream, "new-" + index), null, fs);

            Assert.Equal(2, outcome.CompletedCount);
            Assert.Equal(2, outcome.ReplacedCount);
            Assert.Equal("third", outcome.FailedPath);
            Assert.Equal("new-0", Text(fs.Files["first"]));
            Assert.Equal("new-1", Text(fs.Files["second"]));
            Assert.Equal("old-third", Text(fs.Files["third"]));
            Assert.Equal("old-fourth", Text(fs.Files["fourth"]));
            Assert.Contains("2 of 4 files", ExportFailurePresentation.FormatMessage("PDF export", outcome));
            Assert.Contains("folder now holds a mix", ExportFailurePresentation.FormatMessage("PDF export", outcome));
            Assert.Contains("previous export", ExportFailurePresentation.FormatMessage("PDF export", outcome));
            Assert.Contains("absent", ExportFailurePresentation.FormatMessage("PDF export", outcome));
        }

        [Fact]
        public void ApprovedDestinationRemovedBeforeCommitUsesProtectedMoveAndIsNotCountedAsReplacement()
        {
            var fs = new MemoryExportFileSystem { RemoveBeforeReplaceFor = "target" };
            fs.Files["target"] = Bytes("old");

            var outcome = ExportBatchWriter.Write(new[] { "target" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "target" },
                (_, stream) => Write(stream, "new"), null, fs);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(1, outcome.CompletedCount);
            Assert.Equal(0, outcome.ReplacedCount);
            Assert.Equal("new", Text(fs.Files["target"]));
        }

        [Fact]
        public void CleanupFailureDoesNotMaskWriteFailureAndFlushFailureIsReported()
        {
            var writeFailure = new InvalidOperationException("render failed");
            var fs = new MemoryExportFileSystem { FailDelete = true };
            var outcome = ExportBatchWriter.Write(new[] { "target" }, null, (_, _) => throw writeFailure, null, fs);
            Assert.Same(writeFailure, outcome.Error);
            Assert.Equal("target", outcome.FailedPath);

            fs = new MemoryExportFileSystem { FailFlush = true };
            outcome = ExportBatchWriter.Write(new[] { "target" }, null, (_, stream) => Write(stream, "partial"), null, fs);
            Assert.False(outcome.IsSuccess);
            Assert.Contains("flush", outcome.Error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(fs.Files.ContainsKey("target"));
        }

        [Fact]
        public void DisposeFailureDoesNotReplaceEarlierCallbackFailure()
        {
            var callbackFailure = new InvalidOperationException("format failed");
            var fs = new MemoryExportFileSystem { FailDispose = true };
            var outcome = ExportBatchWriter.Write(new[] { "target" }, null, (_, _) => throw callbackFailure, null, fs);

            Assert.Same(callbackFailure, outcome.Error);
        }

        [Fact]
        public async Task AsyncWriterFlushesAndCommitsAfterCallbackCompletes()
        {
            var fs = new MemoryExportFileSystem();
            var outcome = await ExportBatchWriter.WriteAsync(new[] { "a", "b" }, null,
                async (index, stream) =>
                {
                    var bytes = Bytes("value-" + index);
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                }, null, fs);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, outcome.CompletedCount);
            Assert.Equal("value-0", Text(fs.Files["a"]));
            Assert.Equal("value-1", Text(fs.Files["b"]));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task TemporaryAcquisitionFailureDoesNotCallWriterOrCleanup(bool useAsync)
        {
            var fs = new MemoryExportFileSystem { FailCreate = true };
            var writeCalled = false;

            var outcome = useAsync
                ? await ExportBatchWriter.WriteAsync(new[] { "target" }, null,
                    (_, _) =>
                    {
                        writeCalled = true;
                        return Task.CompletedTask;
                    }, null, fs)
                : ExportBatchWriter.Write(new[] { "target" }, null,
                    (_, _) => writeCalled = true, null, fs);

            Assert.False(outcome.IsSuccess);
            Assert.Equal("target", outcome.FailedPath);
            Assert.Equal(0, outcome.CompletedCount);
            Assert.False(writeCalled);
            Assert.Equal(0, fs.DeleteTemporaryCalls);
        }

        [Fact]
        public void FigurePlannerReservesNaturalSuffixNamesAndUsesCaseInsensitiveCollisions()
        {
            var experiments = new[] { "sample", "SAMPLE", "sample (2)", " ", "" }
                .Select(name => new ExperimentData(name)).ToList();

            var paths = FigureExportPlanner.CreateTargets(experiments, "figures").Select(target => Path.GetFileName(target.Path)).ToArray();

            Assert.Equal(new[] { "sample.pdf", "SAMPLE (3).pdf", "sample (2).pdf", "Untitled Figure.pdf", "Untitled Figure (2).pdf" }, paths);
            Assert.Equal("20261016", FigureExportPlanner.GetProjectFolderName(null, new DateTime(2026, 10, 16)));
            Assert.Equal("Project", FigureExportPlanner.GetProjectFolderName("/tmp/Project.ftxtc", DateTime.MinValue));
        }

        static void Write(Stream stream, string text)
        {
            var bytes = Bytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }

        static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
        static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

        sealed class MemoryExportFileSystem : IExportFileSystem
        {
            readonly Dictionary<string, byte[]> temporary = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            int tempId;
            public readonly Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            public string FailReplaceFor;
            public string RemoveBeforeReplaceFor;
            public bool FailDelete;
            public bool FailFlush;
            public bool FailDispose;
            public bool FailCreate;
            public int DeleteTemporaryCalls;
            public Action<string> OnMove;

            public bool Exists(string path) => Files.ContainsKey(path);
            public (string Path, Stream Stream) CreateTemporarySibling(string destination)
            {
                if (FailCreate) throw new IOException("temporary creation failed");
                var path = "tmp:" + (++tempId).ToString();
                temporary[path] = Array.Empty<byte>();
                return (path, new TempStream(value => temporary[path] = value, FailFlush, FailDispose));
            }
            public void Replace(string source, string destination)
            {
                if (destination == RemoveBeforeReplaceFor) Files.Remove(destination);
                if (destination == FailReplaceFor) throw new IOException("replace failed");
                if (!Files.ContainsKey(destination)) throw new FileNotFoundException("destination disappeared");
                Files[destination] = temporary[source];
                temporary.Remove(source);
            }
            public void MoveNoOverwrite(string source, string destination)
            {
                OnMove?.Invoke(destination);
                if (Files.ContainsKey(destination)) throw new IOException("destination already exists");
                Files[destination] = temporary[source];
                temporary.Remove(source);
            }
            public void DeleteTemporary(string path)
            {
                DeleteTemporaryCalls++;
                if (FailDelete) throw new IOException("cleanup failed");
                temporary.Remove(path);
            }

            sealed class TempStream : MemoryStream
            {
                readonly Action<byte[]> save;
                readonly bool failFlush;
                readonly bool failDispose;
                public TempStream(Action<byte[]> save, bool failFlush, bool failDispose) { this.save = save; this.failFlush = failFlush; this.failDispose = failDispose; }
                public override void Flush()
                {
                    if (failFlush) throw new IOException("flush failed");
                    base.Flush();
                }
                protected override void Dispose(bool disposing)
                {
                    if (disposing) save(ToArray());
                    if (disposing && failDispose) throw new IOException("dispose failed");
                    base.Dispose(disposing);
                }
            }
        }
    }
}
