using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Diagnostics.Contracts;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Support class to <see cref="LogWatcher"/> that watches the log files
    ///     for a specific Intel channel.
    /// </summary>
    /// <remarks>
    ///     <para>An instance of <see cref="IntelChannel"/> is created for each
    ///     tasked Intel channel to manage the parsing and reporting.  This
    ///     includes locating the currently active log file for that channel.</para>
    ///     <para>The following difficulties exist in identifying the active log
    ///     file:</para>
    ///     <list type="bullet">
    ///         <item><description>For performance reasons, Windows will not
    ///         normally update the last-modified date on a file except when
    ///         creating or closing the file handle.  This means that the modified
    ///         timestamp on a log file may be considerably older than the most
    ///         recent log entry within that file.</description></item>
    ///         <item><description><see cref="FileSystemWatcher"/> will not reliably
    ///         raise events for each modification to the file.</description></item>
    ///         <item><description>The user may switch between characters at any
    ///         time and may have multiple running clients. This means there could
    ///         be multiple active log files and that the most recently modified
    ///         one may be due to closing the client.</description></item>
    ///     </list>
    ///     <para>The following file selection logic is used:</para>
    ///     <list type="bullet">
    ///         <item><description>When the <see cref="IntelChannel"/> instance is
    ///         initialized, the log file with the highest timestamp is
    ///         selected.</description></item>
    ///         <item><description>If a <see cref="WatcherChangeTypes.Created"/>
    ///         event is received, the specified file will be used as the active
    ///         file.</description></item>
    ///         <item><description>No file will be monitored if its timestamp
    ///         preceeds the most recent downtime.</description></item>
    ///         <item><description>If a <see cref="WatcherChangeTypes.Changed"/>
    ///         event is received, a timer is started.  Should the currently active
    ///         file not produce a log entry before the timer is exhausted, the
    ///         active file will be switched to the specified
    ///         file.</description></item>
    ///     </list>
    /// </remarks>
    public sealed class IntelChannel {
        // When we receive a FileSystemWatcher.Changed event notification, we
        // hold onto it in case our current log file goes stale
        private FileSystemEventArgs lastEvent;

        // The file we are currently observing.
        private StreamReader activeFile;

        // Regular Expression used to break apart each entry in the log file.
        private static readonly Regex Parser = new Regex(
            @"\[\s*(\d{4})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2}):(\d{2})\s*\](.*)$",
            RegexOptions.CultureInvariant);

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelChannel"/> class.
        /// </summary>
        internal IntelChannel(string name) {
            Contract.Requires<ArgumentException>(!String.IsNullOrWhiteSpace(name));
            this.Name = name;
        }

        /// <summary>
        ///     Gets the channel name of this <see cref="IntelChannel"/>
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        ///     Gets the number of reports that have been made by this
        ///     <see cref="IntelChannel"/>.
        /// </summary>
        public int IntelCount { get; private set; }

        /// <summary>
        ///     Gets the log file currently being observed for new intel.
        /// </summary>
        public FileInfo LogFile { get; private set; }

        /// <summary>
        ///     Occurs when a report is made in this <see cref="IntelChannel"/>.
        /// </summary>
        public event EventHandler<IntelEventArgs> IntelReported;

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(!String.IsNullOrWhiteSpace(this.Name));
            Contract.Invariant(this.IntelCount >= 0);
        }
    }
}
