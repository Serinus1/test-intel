﻿using PleaseIgnore.IntelMap.Properties;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    /// Monitors the log directory for new log entries to a specific
    /// channel.
    /// </summary>
    /// <threadsafety static="true" instance="true"/>
    /// <remarks>
    ///   <para>When running, <see cref="IntelChannel"/> makes use of the
    ///   <see cref="ThreadPool"/> to perform background operations.  The
    /// following sequence of events are processed:</para>
    ///   <list type="number">
    ///   <item>
    ///   <term><see cref="OnStart"/></term>
    ///   <description>Called after the initial call to
    ///   <see cref="Start()"/>.  Responsible for initializing the
    /// internal operations and searching the log directory for
    /// possible active logs.  Will not be called by subsequent
    /// calls to <see cref="Start()"/> unless an intervening call
    /// to <see cref="Stop()"/> has been made.</description>
    ///   </item>
    ///   <item>
    ///   <term><see cref="OnFileCreated"/></term>
    ///   <description>Generated by an internal instance of
    ///   <see cref="FileSystemWatcher"/> to notify us of new files
    /// being created within the log directory, allowing us to
    /// switch to newer log files.</description>
    ///   </item>
    ///   <item>
    ///   <term><see cref="OnFileChanged"/></term>
    ///   <description>Generated by an internal instance of
    ///   <see cref="FileSystemWatcher"/> to notify us of changes
    /// to files in the log directory, allowing us to rescan and
    /// reopen log files appropriately.  Unfortunately, due to
    /// performance optimizations in Windows, change notifications
    /// are often sent when the file is <em>closed</em>, not when
    /// new data has been written to the file.</description>
    ///   </item>
    ///   <item>
    ///   <term><see cref="OnTick"/></term>
    ///   <description>Generated periodically by the
    ///   <see cref="ThreadPool"/> to allow us to rescan the log
    /// files.  Has primary responsibility for generating
    ///   <see cref="IntelReported"/> events.</description>
    ///   </item>
    ///   <item>
    ///   <term><see cref="OnStop"/></term>
    ///   <description>Called after the initial call to
    ///   <see cref="Stop()"/>.  Destroys all internal tracking
    /// structures.  Will be not be called by subsequent calls
    /// to <see cref="Stop()"/> unless an intervening call to
    ///   <see cref="Start()"/> has been made.</description>
    ///   </item>
    ///   </list>
    ///   <note>Internal <see langword="protected"/> and <see langword="virtual"/>
    /// members are provided for the benefit of user testing.  Redefinition may
    /// lead to behavior defects or loss of thread safety.</note>
    /// </remarks>
    [DefaultEvent("IntelReported"), DefaultProperty("Name")]
    public class IntelChannel : Component {
        // Internal members should not be referenced by any other class within
        // PleaseIgnore.IntelMap.  They are made internal purely for the
        // benefit of implementing unit tests.
        
        /// <summary>
        /// Regular Expression used to break apart each entry in the log file.
        /// </summary>
        private static readonly Regex Parser = new Regex(
            "^\uFEFF?" + @"\[\s*(\d{4})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2}):(\d{2})\s*\](.*)$",
            RegexOptions.CultureInvariant);
        /// <summary>
        /// Regular Expression used to extract the timestamp from the filename.
        /// </summary>
        private static readonly Regex FilenameParser = new Regex(
            @"_(\d{4})(\d{2})(\d{2})_(\d{2})(\d{2})(\d{2})\.txt$",
            RegexOptions.CultureInvariant);
        /// <summary>
        /// Default directory to find EVE logs
        /// </summary>
        private static readonly string defaultLogDirectory = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EVE",
            "logs",
            "Chatlogs");
        /// <summary>
        /// The timer period to use when scheduling the timer
        /// </summary>
        private const int timerPeriod = 5000;
        /// <summary>
        /// The default value for expireLog
        /// </summary>
        private const string defaultExpireLog = "00:30:00";

        /// <summary>
        /// Synchronization primitive
        /// </summary>
        internal readonly object syncRoot = new object();
        /// <summary>
        /// Raises the periodic timer for log scanning
        /// </summary>
        private readonly Timer logTimer;
        /// <summary>
        /// The local file system watcher object
        /// </summary>
        private FileSystemWatcher watcher;
        /// <summary>
        /// The channel file name stub
        /// </summary>
        [ContractPublicPropertyName("Name")]
        private string channelFileName;
        /// <summary>
        /// The path to search for EVE chat logs
        /// </summary>
        [ContractPublicPropertyName("Path")]
        private string logDirectory = defaultLogDirectory;
        /// <summary>
        /// The current component status
        /// </summary>
        [ContractPublicPropertyName("Status")]
        private volatile IntelStatus status;
        /// <summary>
        /// The currently processed log file
        /// </summary>
        private StreamReader reader;
        /// <summary>
        /// The last time we parsed a log entry from the current log
        /// </summary>
        private DateTime lastEntry;
        /// <summary>
        /// The time to wait before calling a log file "dead"
        /// </summary>
        [ContractPublicPropertyName("LogExpiration")]
        private TimeSpan expireLog = TimeSpan.Parse(
            defaultExpireLog,
            CultureInfo.InvariantCulture);
        /// <summary>
        /// <see langword="true"/> if the timer is currently running
        /// </summary>
        private bool timerEnabled;

        /// <summary>
        /// Initializes a new instance of the <see cref="IntelChannel" /> class.
        /// </summary>
        public IntelChannel() : this(null, null) {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntelChannel" /> class
        /// with the specified <see cref="Name" />.
        /// </summary>
        /// <param name="name">The initial value for <see cref="Name" />.</param>
        public IntelChannel(string name)
            : this(name, null) {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntelChannel" /> class
        /// with the specified <see cref="Container" />.
        /// </summary>
        /// <param name="container">Optional parent <see cref="Container" />.</param>
        public IntelChannel(IContainer container)
            : this(null, container) {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntelChannel" /> class
        /// with the specified <see cref="Name" /> and <see cref="Container" />.
        /// </summary>
        /// <param name="name">The initial value for <see cref="Name" />.</param>
        /// <param name="container">Optional parent <see cref="Container" />.</param>
        public IntelChannel(string name, IContainer container) {
            this.channelFileName = name;
            this.logTimer = new Timer(this.timer_Callback);

            if (container != null) {
                container.Add(this);
            }
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="IntelChannel"/> class.
        /// </summary>
        ~IntelChannel() {
            this.Dispose(false);
        }

        /// <summary>
        /// Occurs when a new log entry has been read from the chat logs.
        /// </summary>
        public event EventHandler<IntelEventArgs> IntelReported;

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the default directory to search for EVE chat logs.
        /// </summary>
        /// <value>
        /// The default directory to search for EVE chat logs, constructed
        /// from the user's profile directory and other information.
        /// </value>
        public static string DefaultPath {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return defaultLogDirectory;
            }
        }

        /// <summary>
        /// Gets or sets the directory to search for log files.
        /// </summary>
        /// <value>
        /// The directory to search for log files.  If this directory does not
        /// exist, <see cref="Status" /> will be set to
        /// <see cref="IntelStatus.InvalidPath" /> and <see cref="IntelChannel" />
        /// will periodically check if the directory exists.
        /// </value>
        public string Path {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return this.logDirectory;
            }
            set {
                Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(value));
                lock (this.syncRoot) {
                    if (value != this.logDirectory) {
                        this.logDirectory = value;
                        if (this.watcher != null) {
                            try {
                                this.watcher.Path = value;
                                this.ScanFiles();
                            } catch (ArgumentException) {
                                this.watcher.Dispose();
                                this.watcher = null;
                                this.Status = IntelStatus.InvalidPath;
                            }
                        }
                        this.OnPropertyChanged(new PropertyChangedEventArgs("Path"));
                    }
                }
            }
        }

        /// <summary>
        /// Gets or sets the channel name of this <see cref="IntelChannel" />.
        /// </summary>
        /// <value>
        /// The channel name of this <see cref="IntelChannel" />, used as the
        /// basename when searching for log files.
        /// </value>
        /// <exception cref="InvalidOperationException">
        /// Attempt to modify <see cref="Name"/> while <see cref="IsRunning"/>
        /// is <see langword="true"/>.
        /// </exception>
        [DefaultValue((string)null)]
        public string Name {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>())
                        || !this.IsRunning);
                var channelName = this.channelFileName;
                var site = this.Site;
                if (!String.IsNullOrEmpty(channelName)) {
                    return channelName;
                } else if (site != null) {
                    return site.Name;
                } else {
                    return null;
                }
            }
            set {
                lock (this.syncRoot) {
                    if (this.IsRunning) {
                        throw new InvalidOperationException();
                    }
                    this.channelFileName = value;
                }
            }
        }

        /// <summary>
        /// Gets the number of reports that have been made by this
        /// <see cref="IntelChannel" />.
        /// </summary>
        /// <value>
        /// The number of times <see cref="IntelReported"/> has been
        /// raised.
        /// </value>
        public int IntelCount { get; private set; }

        /// <summary>
        /// Gets the log file currently being observed for new intel.
        /// </summary>
        /// <value>
        /// An instance of <see cref="FileInfo"/> describing the log file
        /// being monitoring.
        /// </value>
        public FileInfo LogFile { get; private set; }

        /// <summary>
        /// Gets the current operational status of the <see cref="IntelChannel"/>
        /// component.
        /// </summary>
        /// <value>
        /// A value from the <see cref="IntelStatus"/> enumeration describing
        /// the current operational status of the <see cref="IntelChannel"/>.
        /// </value>
        public virtual IntelStatus Status {
            get {
                return this.status;
            }
            private set {
                Contract.Ensures(Status == value);
                if (this.status != value) {
                    this.status = value;
                    this.UpdateTimer();
                    this.OnPropertyChanged(new PropertyChangedEventArgs("Status"));
                }
            }
        }


        /// <summary>
        /// Gets a value indicating whether the <see cref="IntelChannel" />
        /// is currently running and watching for log entries.
        /// </summary>
        /// <value>
        /// <see langword="true"/> if this instance is running; otherwise,
        /// <see langword="false" />.
        /// </value>
        public bool IsRunning { get { return this.Status.IsRunning(); } }

        /// <summary>
        /// Gets or sets the expiration time on a log file.
        /// </summary>
        /// <value>
        /// A <see cref="TimeSpan"/> representing the maximum time since the
        /// most recent log entry before the log is deemed inactive and closed.
        /// </value>
        [DefaultValue(typeof(TimeSpan), defaultExpireLog)]
        public TimeSpan LogExpiration {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.expireLog;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                    value > TimeSpan.Zero,
                    "value");
                Contract.Ensures(LogExpiration == value);
                lock (this) {
                    if (this.expireLog != value) {
                        this.expireLog = value;
                        this.OnPropertyChanged(new PropertyChangedEventArgs("LogExpiration"));
                    }
                }
            }
        }

        /// <summary>
        /// Initiate the acquisition of log entries from the EVE chat logs. This
        /// method enables <see cref="IntelReported"/> events.
        /// </summary>
        /// <seealso cref="Stop"/>
        public virtual void Start() {
            Contract.Requires<ObjectDisposedException>(
                Status != IntelStatus.Disposed,
                null);
            Contract.Requires<InvalidOperationException>(
                !String.IsNullOrEmpty(Name));
            Contract.Ensures(Status != IntelStatus.Stopped);
            Contract.Ensures(IsRunning);

            lock (this.syncRoot) {
                if (this.status == IntelStatus.Stopped) {
                    this.Status = IntelStatus.Starting;
                    this.channelFileName = this.Name;
                    this.OnStart();
                }
            }
        }

        /// <summary>
        /// Stops the <see cref="IntelChannel"/> from monitoring for new log
        /// entries.
        /// </summary>
        /// <seealso cref="Start"/>
        public virtual void Stop() {
            Contract.Ensures((Status == IntelStatus.Stopped)
                || (Status == IntelStatus.Disposed));
            Contract.Ensures(!IsRunning);

            lock (this.syncRoot) {
                if ((this.status != IntelStatus.Stopped)
                        || (this.status == IntelStatus.Disposed)) {
                    this.Status = IntelStatus.Stopping;
                    this.OnStop();
                    this.UpdateTimer();
                }
            }
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="IntelChannel"/>
        /// and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true"/> to release both managed and unmanaged
        /// resources; <see langword="false"/> to release only unmanaged
        /// resources.
        /// </param>
        protected override void Dispose(bool disposing) {
            Contract.Ensures(Status == IntelStatus.Disposed);
            if (disposing) {
                lock (this.syncRoot) {
                    if (this.status != IntelStatus.Disposed) {
                        // Normal shutdown
                        this.Stop();
                        this.Status = IntelStatus.Disposed;

                        // Dispose child objects
                        this.logTimer.Dispose();

                        // Clear any lingering object references
                        this.IntelReported = null;
                        this.PropertyChanged = null;
                    }
                }
            } else {
                // We really can't touch anything safely
                this.status = IntelStatus.Disposed;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this
        /// instance of <see cref="IntelChannel" />.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance
        /// of <see cref="IntelChannel" />.
        /// </returns>
        public override string ToString() {
            return String.Format(
                CultureInfo.CurrentCulture,
                Resources.IntelChannel_ToString,
                this.GetType().Name,
                this.Name ?? Resources.IntelChannel_NoName,
                this.Status);
        }

        /// <summary>
        /// Creates the instance of <see cref="FileSystemWatcher" /> used
        /// to monitor the file system.
        /// </summary>
        /// <returns>
        /// An instance of <see cref="FileSystemWatcher" /> that will be
        /// used to monitor the directory for the creation/modification
        /// of log files.
        /// </returns>
        /// <remarks>
        /// <see cref="CreateFileSystemWatcher" /> will be called from within
        /// a synchronized context so derived classes should not attempt to
        /// perform any additional synchronization themselves.
        /// </remarks>
        protected virtual FileSystemWatcher CreateFileSystemWatcher() {
            Contract.Ensures(Contract.Result<FileSystemWatcher>() != null);

            var watcher = new FileSystemWatcher();
            watcher.BeginInit();

            watcher.Changed += this.watcher_Changed;
            watcher.Created += this.watcher_Created;
            watcher.EnableRaisingEvents = true;
            watcher.Filter = this.Name + "_*.txt";
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.LastWrite
                | NotifyFilters.DirectoryName | NotifyFilters.FileName;
            watcher.Path = this.Path;

            watcher.EndInit();
            return watcher;
        }

        /// <summary>
        /// Raises the <see cref="IntelReported" /> event.
        /// </summary>
        /// <param name="e">Arguments for the event being raised.</param>
        /// <remarks>
        /// <see cref="OnIntelReported" /> will be called from within
        /// a synchronized context so derived classes should not attempt to
        /// perform any additional synchronization themselves.
        /// </remarks>
        internal protected void OnIntelReported(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");

            ThreadPool.QueueUserWorkItem(delegate(object state) {
                Contract.Requires(state is IntelEventArgs);
                var handler = this.IntelReported;
                if (handler != null) {
                    handler(this, (IntelEventArgs)state);
                }
            }, e);

            this.lastEntry = DateTime.UtcNow;
            ++this.IntelCount;
            this.OnPropertyChanged(new PropertyChangedEventArgs("IntelCount"));
        }

        /// <summary>
        /// Raises the <see cref="PropertyChanged" /> event.
        /// </summary>
        /// <param name="e">Arguments for the event being raised.</param>
        /// <remarks>
        /// <see cref="OnPropertyChanged" /> will be called from within
        /// a synchronized context so derived classes should not attempt to
        /// perform any additional synchronization themselves.
        /// </remarks>
        internal protected void OnPropertyChanged(PropertyChangedEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            Debug.Assert(String.IsNullOrEmpty(e.PropertyName)
                || (GetType().GetProperty(e.PropertyName) != null));

            ThreadPool.QueueUserWorkItem(delegate(object state) {
                Contract.Requires(state is PropertyChangedEventArgs);
                var handler = this.PropertyChanged;
                if (handler != null) {
                    handler(this, (PropertyChangedEventArgs)state);
                }
            }, e);
        }

        /// <summary>
        /// Called after <see cref="Start" /> has been called.
        /// </summary>
        /// <remarks>
        /// <see cref="OnStart" /> will be called from within a synchronized
        /// context so derived classes should not attempt to perform any
        /// additional synchronization themselves.
        /// </remarks>
        protected virtual void OnStart() {
            Contract.Requires<InvalidOperationException>(
                Status == IntelStatus.Starting);
            Contract.Ensures((Status == IntelStatus.Active)
                || (Status == IntelStatus.Waiting)
                || (Status == IntelStatus.InvalidPath));

            // Create the file system watcher object
            try {
                this.watcher = this.CreateFileSystemWatcher();
            } catch(ArgumentException) {
                this.Status = IntelStatus.InvalidPath;
            }

            // Open the log file with the latest timestamp in its filename
            this.ScanFiles();
        }

        /// <summary>
        /// Called when a new log file is created for the channel we
        /// are monitoring.
        /// </summary>
        /// <param name="e">
        /// Instance of <see cref="FileSystemEventArgs" /> describing the
        /// new file.
        /// </param>
        /// <remarks>
        /// <see cref="OnFileCreated" /> will be called from within a
        /// synchronized context so derived classes should not attempt to
        /// perform any additional synchronization themselves.
        /// </remarks>
        protected virtual void OnFileCreated(FileSystemEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");

            // Assume it's going to be a better file....for now
            OpenFile(new FileInfo(e.FullPath));
        }

        /// <summary>
        /// Called when a log file associated with the channel we are
        /// monitoring has been modified.
        /// </summary>
        /// <param name="e">Instance of <see cref="FileSystemEventArgs" />
        /// describing the modified file.
        /// </param>
        /// <remarks>
        /// <see cref="OnFileChanged" /> will be called from within a
        /// synchronized context so derived classes should not attempt to
        /// perform any additional synchronization themselves.
        /// </remarks>
        protected virtual void OnFileChanged(FileSystemEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");

            // Only process this message if we have nothing else to go on
            if (this.reader == null) {
                this.OpenFile(new FileInfo(e.FullPath));
            }
        }

        /// <summary>
        /// Called every couple of seconds to sweep the log file for
        /// new entries.
        /// </summary>
        /// <remarks>
        /// <see cref="OnTick" /> will be called from within a synchronized
        /// context so derived classes should not attempt to perform any
        /// additional synchronization themselves.
        /// </remarks>
        protected virtual void OnTick() {
            if (this.watcher == null) {
                // Try (again) to create the watcher object
                try {
                    this.watcher = this.CreateFileSystemWatcher();
                    this.Status = IntelStatus.Waiting;
                    this.ScanFiles();
                } catch (ArgumentException) {
                    // Still doesn't seem to exist
                }
            }

            if (this.reader != null) {
                // Read new log entries from the current log
                try {
                    string line;
                    while ((line = reader.ReadLine()) != null) {
                        Trace.WriteLine("R " + line, IntelExtensions.WebTraceCategory);
                        var match = Parser.Match(line);
                        if (match.Success) {
                            var e = new IntelEventArgs(
                                this.Name,
                                new DateTime(
                                    match.Groups[1].ToInt32(),
                                    match.Groups[2].ToInt32(),
                                    match.Groups[3].ToInt32(),
                                    match.Groups[4].ToInt32(),
                                    match.Groups[5].ToInt32(),
                                    match.Groups[6].ToInt32(),
                                    DateTimeKind.Utc),
                                match.Groups[7].Value);
                            this.OnIntelReported(e);
                        }
                    }
                } catch (IOException) {
                    this.CloseFile();
                }

                // Close the log if it has been idle for too long
                if (this.lastEntry + this.expireLog < DateTime.UtcNow) {
                    this.CloseFile();
                }
            }
        }

        /// <summary>
        /// Called after <see cref="Stop" /> has been called.
        /// </summary>
        /// <remarks>
        /// <see cref="OnStop" /> will be called from within a synchronized
        /// context so derived classes should not attempt to perform any
        /// additional synchronization themselves.
        /// </remarks>
        protected virtual void OnStop() {
            Contract.Requires<InvalidOperationException>(
                Status == IntelStatus.Stopping);
            Contract.Ensures(Status == IntelStatus.Stopped);

            if (this.reader != null) {
                this.reader.Close();
                this.reader = null;
            }

            if (this.LogFile != null) {
                this.LogFile = null;
                this.OnPropertyChanged(new PropertyChangedEventArgs("LogFile"));
            }

            this.Status = IntelStatus.Stopped;
        }

        /// <summary>
        /// Rescans the active directory looking for valid log files
        /// </summary>
        /// <returns>
        /// <see langword="true" /> if we were able to open a log file;
        /// otherwise, <see langword="false" />.
        /// </returns>
        protected bool ScanFiles() {
            try {
                var downtime = IntelExtensions.LastDowntime;
                var file = new DirectoryInfo(this.Path)
                    .GetFiles(this.Name + "_*.txt", SearchOption.TopDirectoryOnly)
                    .Select(x => new {
                        File = x,
                        Match = FilenameParser.Match(x.Name)
                    })
                    .Where(x => x.Match.Success)
                    .Select(x => new {
                        File = x.File,
                        Timestamp = new DateTime(
                            x.Match.Groups[1].ToInt32(),
                            x.Match.Groups[2].ToInt32(),
                            x.Match.Groups[3].ToInt32(),
                            x.Match.Groups[4].ToInt32(),
                            x.Match.Groups[5].ToInt32(),
                            x.Match.Groups[6].ToInt32(),
                            DateTimeKind.Utc)
                    })
                    .Where(x => x.Timestamp > downtime)
                    .OrderByDescending(x => x.Timestamp)
                    .FirstOrDefault(x => this.OpenFile(x.File));

                if (file == null) {
                    this.CloseFile();
                }

                return file != null;
            } catch (IOException) {
                return false;
            }
        }

        /// <summary>
        /// Closes the existing log file and opens a new log file.
        /// </summary>
        /// <param name="fileInfo">The new log file to track.</param>
        /// <returns>
        /// <see langword="true" /> if we were able to open the file;
        /// otherwise, <see langword="false" />.
        /// </returns>
        internal protected bool OpenFile(FileInfo fileInfo) {
            Contract.Requires<ArgumentNullException>(fileInfo != null, "fileInfo");
            //Contract.Ensures(Status == IntelChannelStatus.Active);
            var oldStatus = this.status;
            var oldFile = this.LogFile;

            // Close the existing file (if any)
            if (this.reader != null) {
                try {
                    this.reader.Close();
                } catch (IOException) {
                } finally {
                    this.reader = null;
                }
            }

            // Clear the status (defer raising PropertyChanged)
            this.LogFile = null;
            this.status = (this.watcher != null)
                ? IntelStatus.Waiting
                : IntelStatus.InvalidPath;
            
            // Try to open the file stream
            FileStream stream = null;
            try {
                stream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(0, SeekOrigin.End);
                // XXX: We rely upon StreamReader's BOM detection.  EVE seems
                // to generate Little Endian UTF-16 log files.  We could hard
                // code that, but we don't know if that would cause other
                // problems.
                this.reader = new StreamReader(stream, true);
                this.status = IntelStatus.Active;
                this.LogFile = fileInfo;
                this.lastEntry = DateTime.UtcNow;
            } catch (IOException) {
                // Don't leak FileStream references
                if (stream != null) {
                    try {
                        stream.Close();
                    } catch (IOException) {
                    }
                }
            }

            // Raise any deferred PropertyChanged events
            if (this.status != oldStatus) {
                this.OnPropertyChanged(new PropertyChangedEventArgs("Status"));
            }
            if (this.LogFile != oldFile) {
                this.OnPropertyChanged(new PropertyChangedEventArgs("LogFile"));
            }

            // Success if we opened a reader
            this.UpdateTimer();
            return (this.reader != null);
        }

        /// <summary>
        /// Closes the log file we are currently monitoring (if any).
        /// </summary>
        protected void CloseFile() {
            Contract.Ensures((Status == IntelStatus.Waiting)
                || (Status == IntelStatus.InvalidPath));

            if (this.reader != null) {
                try {
                    this.reader.Close();
                } catch (IOException) {
                } finally {
                    this.reader = null;
                }
            }
            if (this.LogFile != null) {
                this.LogFile = null;
                this.OnPropertyChanged(new PropertyChangedEventArgs("LogFile"));
            }
            this.Status = (this.watcher != null)
                ? IntelStatus.Waiting
                : IntelStatus.InvalidPath;
        }

        /// <summary>
        /// Updates the timer for <see cref="OnTick" />
        /// </summary>
        private void UpdateTimer() {
            switch (this.status) {
            case IntelStatus.Active:
            case IntelStatus.InvalidPath:
                // Operations that require us to ping the filesystem regularly
                if (!this.timerEnabled) {
                    this.logTimer.Change(timerPeriod, timerPeriod);
                    this.timerEnabled = true;
                }
                break;

            case IntelStatus.Disposed:
                // The timer object is no longer valid
                break;

            default:
                // Operations when we are not actively monitoring the filesystem
                if (this.timerEnabled) {
                    this.logTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    this.timerEnabled = false;
                }
                break;
            }
        }

        /// <summary>
        /// Handler for <see cref="FileSystemWatcher.Created" /> event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FileSystemEventArgs"/> instance
        /// containing the event data.</param>
        private void watcher_Created(object sender, FileSystemEventArgs e) {
            Contract.Requires(e != null);
            ThreadPool.QueueUserWorkItem(delegate(object state) {
                Contract.Requires(state is FileSystemEventArgs);
                lock (this.syncRoot) {
                    if (this.IsRunning) {
                        this.OnFileCreated((FileSystemEventArgs)e);
                    }
                }
            }, e);
        }

        /// <summary>
        /// Handler for <see cref="FileSystemWatcher.Created" /> event.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="FileSystemEventArgs"/> instance
        /// containing the event data.</param>
        private void watcher_Changed(object sender, FileSystemEventArgs e) {
            Contract.Requires(e != null);
            ThreadPool.QueueUserWorkItem(delegate(object state) {
                Contract.Requires(state is FileSystemEventArgs);
                lock (this.syncRoot) {
                    if (this.IsRunning) {
                        this.OnFileChanged((FileSystemEventArgs)e);
                    }
                }
            }, e);
        }

        /// <summary>
        /// Callback for <see cref="logTimer"/>.
        /// </summary>
        /// <param name="state">Ignored</param>
        private void timer_Callback(object state) {
            lock (this.syncRoot) {
                if (this.IsRunning) {
                    this.OnTick();
                }
            }
        }

        /// <summary>Invariant method for Code Contracts.</summary>
        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(!String.IsNullOrEmpty(this.channelFileName)
                    || !this.IsRunning);
            Contract.Invariant(!String.IsNullOrEmpty(this.logDirectory));

            Contract.Invariant(this.IntelCount >= 0);
        }
    }
}
