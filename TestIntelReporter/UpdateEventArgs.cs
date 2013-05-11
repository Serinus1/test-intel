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
        private readonly string uri;
        private readonly string version;

        public UpdateEventArgs(string version, string uri) {
            this.uri = uri;
            this.version = version;
        }

        public string NewVersion { get { return this.version; } }

        public string UpdateUri { get { return this.uri; } }
    }
}
