using System;
using System.Globalization;
using System.IO;

namespace PleaseIgnore.IntelMap.Tests {
    /// <summary>
    ///     Creates a new file that is automatically deleted when this class
    ///     is disposed.
    /// </summary>
    internal sealed class TempFile : IDisposable {
        private readonly string fileName;

        public TempFile() : this(null, null) {
        }

        public TempFile(TempDirectory directory) : this(null, directory) {
        }

        public TempFile(string baseName) : this(baseName, null) {
        }

        public TempFile(string baseName, TempDirectory directory) {
            this.fileName = Path.Combine(
                (directory != null)
                    ? directory.FullName
                    : System.IO.Path.GetTempPath(),
                (baseName ?? "temp") + DateTime.UtcNow.ToString(
                    "'_'yyyyMMdd'_'HHmmss'.txt'",
                    CultureInfo.InvariantCulture));
        }

        public FileInfo FileInfo {
            get { return new FileInfo(this.fileName); }
        }

        public string FullName {
            get { return this.fileName; }
        }

        public string Name {
            get { return Path.GetFileName(this.fileName); }
        }

        public FileStream Open(FileMode mode, FileAccess access, FileShare share) {
            return new FileStream(this.fileName, mode, access, share);
        }

        public void Dispose() {
            try {
                File.Delete(fileName);
            } catch (IOException) {
            }
        }
    }
}
