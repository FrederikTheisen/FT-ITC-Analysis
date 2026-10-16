using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace AnalysisITC.Core.Export
{
    /// <summary>Writes a batch of files through sibling temporary files and protected commits.</summary>
    public static class ExportBatchWriter
    {
        public static ISet<string> CaptureExistingPaths(IEnumerable<string> paths)
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (paths == null) return existing;
            foreach (var path in paths)
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) existing.Add(path);
            return existing;
        }

        public static ExportOutcome Write(IReadOnlyList<string> paths, ISet<string> approvedReplacementPaths, Action<int, Stream> write, Action prepare = null) =>
            Write(paths, approvedReplacementPaths, write, prepare, PhysicalExportFileSystem.Instance);

        internal static ExportOutcome Write(IReadOnlyList<string> paths, ISet<string> approvedReplacementPaths, Action<int, Stream> write, Action prepare, IExportFileSystem fileSystem)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (write == null) throw new ArgumentNullException(nameof(write));
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            var approved = approvedReplacementPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var completed = 0;
            var replaced = 0;
            string currentPath = null;
            string temporaryPath = null;
            try
            {
                prepare?.Invoke();
                for (var i = 0; i < paths.Count; i++)
                {
                    currentPath = paths[i] ?? throw new ArgumentException("An export destination cannot be null.", nameof(paths));
                    temporaryPath = fileSystem.CreateTemporarySibling(currentPath);
                    WriteAndClose(fileSystem.OpenTemporary(temporaryPath), stream => write(i, stream));
                    var didReplace = Commit(fileSystem, temporaryPath, currentPath, approved.Contains(currentPath));
                    temporaryPath = null;
                    completed++;
                    if (didReplace) replaced++;
                }
                return new ExportOutcome(paths.Count, completed, replaced, null, null);
            }
            catch (Exception error)
            {
                return ExportOutcome.Failure(paths.Count, error, currentPath, completed, replaced);
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { fileSystem.DeleteTemporary(temporaryPath); }
                    catch { /* Preserve the failure that stopped the export. */ }
                }
            }
        }

        public static Task<ExportOutcome> WriteAsync(IReadOnlyList<string> paths, ISet<string> approvedReplacementPaths, Func<int, Stream, Task> write, Action prepare = null) =>
            WriteAsync(paths, approvedReplacementPaths, write, prepare, PhysicalExportFileSystem.Instance);

        internal static async Task<ExportOutcome> WriteAsync(IReadOnlyList<string> paths, ISet<string> approvedReplacementPaths, Func<int, Stream, Task> write, Action prepare, IExportFileSystem fileSystem)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (write == null) throw new ArgumentNullException(nameof(write));
            if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
            var approved = approvedReplacementPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var completed = 0;
            var replaced = 0;
            string currentPath = null;
            string temporaryPath = null;
            try
            {
                prepare?.Invoke();
                for (var i = 0; i < paths.Count; i++)
                {
                    currentPath = paths[i] ?? throw new ArgumentException("An export destination cannot be null.", nameof(paths));
                    temporaryPath = fileSystem.CreateTemporarySibling(currentPath);
                    await WriteAndCloseAsync(fileSystem.OpenTemporary(temporaryPath), stream => write(i, stream)).ConfigureAwait(false);
                    var didReplace = Commit(fileSystem, temporaryPath, currentPath, approved.Contains(currentPath));
                    temporaryPath = null;
                    completed++;
                    if (didReplace) replaced++;
                }
                return new ExportOutcome(paths.Count, completed, replaced, null, null);
            }
            catch (Exception error)
            {
                return ExportOutcome.Failure(paths.Count, error, currentPath, completed, replaced);
            }
            finally
            {
                if (temporaryPath != null)
                {
                    try { fileSystem.DeleteTemporary(temporaryPath); }
                    catch { /* Preserve the failure that stopped the export. */ }
                }
            }
        }

        static bool Commit(IExportFileSystem fileSystem, string temporaryPath, string destination, bool approved)
        {
            if (!approved)
            {
                MoveWithoutReplacement(fileSystem, temporaryPath, destination);
                return false;
            }

            try
            {
                fileSystem.Replace(temporaryPath, destination);
                return true;
            }
            catch (Exception) when (!fileSystem.Exists(destination))
            {
                // An approved destination may have been removed after confirmation. The
                // no-overwrite move still protects a file that appears in the meantime.
                MoveWithoutReplacement(fileSystem, temporaryPath, destination);
                return false;
            }
        }

        static void MoveWithoutReplacement(IExportFileSystem fileSystem, string temporaryPath, string destination)
        {
            try { fileSystem.MoveNoOverwrite(temporaryPath, destination); }
            catch (IOException error) when (fileSystem.Exists(destination))
            {
                throw new IOException("A file appeared at this destination after confirmation. It was kept because replacement was not approved. " + error.Message, error);
            }
        }

        static void WriteAndClose(Stream stream, Action<Stream> write)
        {
            Exception failure = null;
            try
            {
                write(stream);
                stream.Flush();
            }
            catch (Exception error) { failure = error; }
            try { stream.Dispose(); }
            catch (Exception disposeError) { if (failure == null) failure = disposeError; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        static async Task WriteAndCloseAsync(Stream stream, Func<Stream, Task> write)
        {
            Exception failure = null;
            try
            {
                await write(stream).ConfigureAwait(false);
                stream.Flush();
            }
            catch (Exception error) { failure = error; }
            try { stream.Dispose(); }
            catch (Exception disposeError) { if (failure == null) failure = disposeError; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    internal interface IExportFileSystem
    {
        bool Exists(string path);
        string CreateTemporarySibling(string destination);
        Stream OpenTemporary(string path);
        void Replace(string source, string destination);
        void MoveNoOverwrite(string source, string destination);
        void DeleteTemporary(string path);
    }

    internal sealed class PhysicalExportFileSystem : IExportFileSystem
    {
        public static readonly PhysicalExportFileSystem Instance = new PhysicalExportFileSystem();
        PhysicalExportFileSystem() { }

        public bool Exists(string path) => File.Exists(path);

        public string CreateTemporarySibling(string destination)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(destination));
            var name = Path.GetFileName(destination);
            for (var attempt = 0; attempt < 32; attempt++)
            {
                var candidate = Path.Combine(directory, "." + name + "." + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
                    return candidate;
                }
                catch (IOException) when (File.Exists(candidate)) { }
            }
            throw new IOException("Could not reserve a temporary export file beside " + destination);
        }

        public Stream OpenTemporary(string path) => new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        public void Replace(string source, string destination) => File.Replace(source, destination, null);
        public void MoveNoOverwrite(string source, string destination) => File.Move(source, destination);
        public void DeleteTemporary(string path) { if (File.Exists(path)) File.Delete(path); }
    }
}
