using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TestIntelReporter {
    /// <summary>
    ///     Notifies the client process that a new version of the
    ///     monitored software is available.
    /// </summary>
    [Serializable]
    public class UpdateEventArgs : EventArgs {
        private readonly Version oldVersion;
        private readonly Version newVersion;
        private readonly string updateUri;

        public UpdateEventArgs(Version oldVersion, Version newVersion,
                string updateUri) {
            this.oldVersion = oldVersion;
            this.newVersion = newVersion;
            this.updateUri = updateUri;
        }

        public Version OldVersion { get { return this.oldVersion; } }

        public Version NewVersion { get { return this.newVersion; } }

        public string UpdateUri { get { return this.updateUri; } }
    }
}
