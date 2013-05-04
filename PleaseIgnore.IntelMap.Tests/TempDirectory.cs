using System;
using System.Globalization;
using System.IO;

namespace PleaseIgnore.IntelMap.Tests {
    /// <summary>
    ///     Creates a new directory that is automatically removed when this
    ///     class is disposed.
    /// </summary>
    internal sealed class TempDirectory : IDisposable {
        private readonly string directoryName;
        
        public TempDirectory() {
            this.directoryName = Path.Combine(
                System.IO.Path.GetTempPath(),
                "tmp-" + DateTime.UtcNow.ToString(
                    "yyyyMMdd-HHmmss",
                    CultureInfo.InvariantCulture
                ));
            Directory.CreateDirectory(this.directoryName);
        }

        public DirectoryInfo DirectoryInfo {
            get { return new DirectoryInfo(this.directoryName); }
        }

        public string FullName {
            get { return this.directoryName; }
        }

        public string Name {
            get { return Path.GetFileName(directoryName); }
        }

        public void Dispose() {
            try {
                Directory.Delete(this.directoryName, true);
            } catch (IOException) {
            }
        }
    }
}
