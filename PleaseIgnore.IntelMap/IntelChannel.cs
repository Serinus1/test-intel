using System;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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
        // Timestamp of lastEvent
        private DateTime lastEventTime;
        // The file we are currently observing.
        private StreamReader activeFile;

        // Regular Expression used to break apart each entry in the log file.
        private static readonly Regex Parser = new Regex(
            @"\[\s*(\d{4})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2}):(\d{2})\s*\](.*)$",
            RegexOptions.CultureInvariant);
        // Regular Expression used to extract the timestamp from the filename.
        private static readonly Regex FilenameParser = new Regex(
            @"_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})\.txt$",
            RegexOptions.CultureInvariant);

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelChannel"/> class.
        /// </summary>
        internal IntelChannel(IntelReporter reporter, string name) {
            Contract.Requires<ArgumentNullException>(reporter != null, "reporter");
            Contract.Requires<ArgumentException>(!String.IsNullOrWhiteSpace(name));
            this.Name = name;
            this.IntelReporter = reporter;
        }

        /// <summary>
        ///     Gets the instance of <see cref="IntelReporter"/> that is managing
        ///     and reporting the events from this <see cref="IntelChannel"/>.
        /// </summary>
        public IntelReporter IntelReporter { get; private set; }

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
        ///     Called by <see cref="IntelReporter"/> to notify us of changes to
        ///     the filesystem.
        /// </summary>
        internal void OnFileEvent(FileSystemEventArgs e) {
            Contract.Requires(e != null);
            if (Matches(e.Name)) {
                this.lastEvent = e;
                this.lastEventTime = DateTime.UtcNow;
            }
        }

        /// <summary>
        ///     Call this periodically to rescan the log file for new log
        ///     entries.
        /// </summary>
        /// <returns>
        ///     Number of intel reports parsed from the log file.
        /// </returns>
        internal int Tick() {
            Contract.Ensures(Contract.Result<int>() >= 0);

            // Parse the currently selected file, looking for fresh intel
            int linesRead = 0;
            int intelRead = 0;
            if (activeFile != null) {
                try {
                    string line;
                    while ((line = this.activeFile.ReadLine()) != null) {
                        ++linesRead;
                        var match = Parser.Match(line);
                        if (match.Success) {
                            var timestamp = new DateTime(
                                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                                int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                                int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture),
                                DateTimeKind.Utc);
                            var args = new IntelEventArgs(this, timestamp, match.Groups[7].Value);
                            ++intelRead;
                            ++this.IntelCount;
                            this.IntelReporter.OnIntelReported(args);
                        }
                    }
                } catch (IOException) {
                    this.Close();
                }
            }

            // Do we need to switch files?
            if (this.lastEvent != null) {
                if (linesRead != 0) {
                    this.lastEvent = null;
                } else if ((this.LogFile == null)
                        || (this.lastEventTime + IntelReporter.ChannelScanPeriod < DateTime.UtcNow)) {
                    SwitchTo(new FileInfo(this.lastEvent.FullPath));
                    this.lastEvent = null;
                }
            }

            return intelRead;
        }

        /// <summary>
        ///     Called by <see cref="IntelReporter"/> to force us to close our
        ///     log file.
        /// </summary>
        internal void Close() {
            if (activeFile != null) {
                try {
                    activeFile.Close();
                } catch (IOException) {
                } finally {
                    this.activeFile = null;
                    this.LogFile = null;
                }
                IntelReporter.OnChannelChanged(this);
            }
        }

        /// <summary>
        ///     Called by <see cref="IntelReporter"/> to force us to scan the
        ///     log directory for new log files.
        /// </summary>
        internal void Rescan() {
            try {
                var logDir = this.IntelReporter.LogDirectory;
                if (logDir == null) {
                    return;
                }

                var infoDir = new DirectoryInfo(logDir);
                var infoLog = infoDir.GetFiles(this.Name + "_*.txt")
                    .Select(x => new { File = x, Timestamp = ParseTimeStamp(x) })
                    .Where(x => x.Timestamp > this.IntelReporter.LastDowntime)
                    .OrderByDescending(x => x.Timestamp)
                    .Select(x => x.File)
                    .FirstOrDefault();
                if (infoLog != null) {
                    SwitchTo(infoLog);
                }
            } catch (IOException) {
            }
        }

        /// <summary>
        ///     Extracts the timestamp from a log's filename.
        /// </summary>
        [Pure]
        private static DateTime ParseTimeStamp(FileInfo file) {
            Contract.Requires(file != null);
            var match = FilenameParser.Match(file.Name);
            if (match.Success) {
                return new DateTime(
                    int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[6].Value, CultureInfo.InvariantCulture),
                    DateTimeKind.Utc);
            } else {
                return DateTime.MinValue;
            }
        }

        /// <summary>
        ///     Tests the filename of a log file to see if it corresponds to
        ///     the channel we are watching.
        /// </summary>
        [Pure]
        private bool Matches(string filename) {
            return !String.IsNullOrEmpty(filename)
                && filename.StartsWith(this.Name + '_',
                    StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>
        ///     Switches the actively scanned log to the specified log file.
        /// </summary>
        private void SwitchTo(FileInfo file) {
            Contract.Ensures(this.LogFile == file);
            
            // Drop any sort of reopen-on-tick
            this.Close();
            this.lastEvent = null;

            // Open the new file
            if ((file != null) && file.Exists) {
                FileStream stream = null;
                StreamReader reader = null;
                try {
                    stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    reader = new StreamReader(stream, true);
                    stream.Seek(0, SeekOrigin.End);
                    reader.DiscardBufferedData();
                    
                    this.LogFile = file;
                    this.activeFile = reader;
                } catch (IOException) {
                    // Failed to open the log file, just clean up and abort
                    try {
                        if (reader != null) {
                            reader.Close();
                        } else if (stream != null) {
                            stream.Close();
                        }
                    } catch (IOException) {
                    }
                } catch (UnauthorizedAccessException) {
                }
                IntelReporter.OnChannelChanged(this);
            }
        }

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(!String.IsNullOrWhiteSpace(this.Name));
            Contract.Invariant(this.IntelCount >= 0);
            Contract.Invariant(this.IntelReporter != null);
        }
    }
}
