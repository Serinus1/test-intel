using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Threading;

namespace PleaseIgnore.IntelMap {
    [DefaultEvent("IntelReported")]
    public sealed class IntelReporter : Component, ISupportInitialize {
        // Signals that the component is currently initializing
        private bool initializing;
        // Signaling the worker thread to start/stop
        private volatile bool running;
        // Signaling the worker thread to reset the logDirectory
        private volatile bool resetDirectory;
        // Signaling the worker thread to reauthenticate
        private volatile bool authenticate;
        // Signals the worker thread to process a signal
        private AutoResetEvent signal;
        // Synchronization object
        private object syncObject;

        // Thread used for enternal monitoring
        private Thread thread;
        // List of active IntelChannels
        private List<IntelChannel> channels;
        // Last time the channel list was updated
        private DateTime channelTimestamp;
        // File system event queue
        private ConcurrentQueue<FileSystemEventArgs> watcherEvents;
        // The intel reporting server session
        private IntelSession session;
        // The last time we failed to contact the server
        private DateTime lastFailure;

        // The overridden file directory for EVE logs
        private volatile string logDirectory;
        // The AUTH username
        private volatile string username;
        // The hashed services password
        private volatile string password;
        
        // The default file directory for EVE logs
        private static readonly string defaultLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EVE",
            "logs",
            "Chatlogs");
        // The keep-alive period for the intel session
        private static readonly TimeSpan keepalivePeriod = new TimeSpan(0, 1, 0);
        
        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class.
        /// </summary>
        public IntelReporter() {
            this.signal = new AutoResetEvent(false);
            this.watcherEvents = new ConcurrentQueue<FileSystemEventArgs>();
            this.syncObject = new object();
            this.channels = new List<IntelChannel>();

            this.ChannelUpdatePeriod = new TimeSpan(24, 0, 0);
            this.ChannelScanFrequency = new TimeSpan(0, 0, 5);
            this.RetryPeriod = new TimeSpan(0, 10, 0);
            this.username = String.Empty;
            this.password = String.Empty;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class with the specified <see cref="Container"/>.
        /// </summary>
        public IntelReporter(IContainer container) : this() {
            Contract.Ensures(this.Container == container);
            if (container != null) {
                container.Add(this);
            }
        }

        /// <summary>
        ///     Gets or sets a value indicating whether the
        ///     <see cref="IntelReporter"/> actively parsing and reporting
        ///     data from the log files.
        /// </summary>
        [DefaultValue(false), Category("Behavior")]
        public bool Enabled {
            get {
                return this.running;
            }
            set {
                Contract.Ensures(this.Enabled == value);
                if (value) {
                    Start();
                } else {
                    Stop();
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time between updates of the intel channel list.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), "24:00:00"), Category("Behavior")]
        public TimeSpan ChannelUpdatePeriod { get; set; }

        /// <summary>
        ///     Gets or sets the time between scans of the chat logs for new intel.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), "00:00:05"), Category("Behavior")]
        public TimeSpan ChannelScanFrequency { get; set; }

        /// <summary>
        ///     Gets or sets the time to back off attempting to contact the server
        ///     again if networking problems prevent communication.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), "00:10:00"), Category("Behavior")]
        public TimeSpan RetryPeriod { get; set; }

        /// <summary>
        ///     Gets or sets the TEST Alliance AUTH username.
        /// </summary>
        [DefaultValue(""), Category("Behavior")]
        public string Username {
            get {
                return this.username;
            }
            set {
                if (this.username != null) {
                    this.username = value;
                    this.authenticate = true;
                    signal.Set();
                }
            }
        }

        /// <summary>
        ///     Gets or sets the hashed services password for the user.
        /// </summary>
        [DefaultValue(""), Category("Behavior")]
        public string PasswordHash {
            get {
                return this.password;
            }
            set {
                if (this.password != value) {
                    this.password = value;
                    this.authenticate = true;
                    signal.Set();
                }
            }
        }

        /// <summary>
        ///     Sets the hashed password by automatically hashing and storing the
        ///     plaintext password.
        /// </summary>
        [Browsable(false)]
        public string Password {
            set {
                Contract.Requires<ArgumentNullException>(value != null, "value");
                this.PasswordHash = IntelSession.HashPassword(value);
            }
        }

        /// <summary>
        ///     Gets or sets the directory path to find EVE chat logs.
        /// </summary>
        [AmbientValue(""), DefaultValue(""), Category("Behavior")]
        public string LogDirectory {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return String.IsNullOrEmpty(this.logDirectory)
                    ? defaultLogDirectory
                    : this.logDirectory;
            }
            set {
                if (this.logDirectory != value) {
                    this.logDirectory = value;
                    this.resetDirectory = true;
                    signal.Set();
                }
            }
        }

        /// <summary>
        ///     Gets the number of events sent to the intel reporting server.
        /// </summary>
        [Browsable(false)]
        public int IntelSent { get; private set; }

        /// <summary>
        ///     Gets the number of events that were dropped because of network
        ///     or server errors.
        /// </summary>
        [Browsable(false)]
        public int IntelDropped { get; private set; }

        /// <summary>
        ///     Gets the current state of the intel reporting engine.
        /// </summary>
        [Browsable(false)]
        public IntelStatus Status { get; private set;  }

        /// <summary>
        ///     Raised when a new intel report has been parsed from a log file.
        /// </summary>
        /// <remarks>
        ///     This call will be made from the <see cref="ThreadPool"/>.  The
        ///     consumer will need synchronize to their local threads as
        ///     appropriate.
        /// </remarks>
        public event EventHandler<IntelEventArgs> IntelReported;

        public void BeginInit() {
            this.initializing = true;
        }

        public void EndInit() {
            this.initializing = false;
            if (this.running) {
                Start();
            }
        }

        public void Authenticate(string username, string password) {
            this.username = username;
            this.password = IntelSession.HashPassword(password);
            this.authenticate = true;
            signal.Set();
        }

        public void Start() {
            Contract.Ensures(this.Enabled == true);
            lock (this.syncObject) {
                this.running = true;
                if (!this.initializing && !this.DesignMode
                        && (this.thread == null)) {
                    this.thread = new Thread(this.ThreadMain);
                }
            }
        }

        public void Stop() {
            Contract.Ensures(this.Enabled == false);
            lock (this.syncObject) {
                this.running = false;
                if (this.thread != null) {
                    this.thread.Join();
                    this.thread = null;
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString() {
            return base.ToString();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                Stop();
                if (this.signal != null) {
                    this.signal.Dispose();
                    this.signal = null;
                }
            }
            base.Dispose(disposing);
        }

        /// <summary>
        ///     Entry point for the 
        /// </summary>
        private void ThreadMain() {
            try {
                this.Status = IntelStatus.Idle;
                channels.ForEach(x => x.Close());
                signal.Reset();
                using (var container = new Container()) {
                    // Try an initial login so the UI can notify immediately
                    // of bad credentials
                    this.CreateSession();
                    // The age of the session
                    var sessionTimestamp = DateTime.UtcNow;

                    // Configure the File Watcher
                    var watcher = new FileSystemWatcher();
                    watcher.BeginInit();
                    // TODO: Fails if the path doesn't exist
                    watcher.Path = this.LogDirectory;
                    watcher.Filter = "*.txt";
                    watcher.Created += watcher_Created;
                    watcher.Changed += watcher_Changed;
                    watcher.NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.Size;
                    container.Add(watcher);
                    watcher.EnableRaisingEvents = true;
                    watcher.EndInit();

                    // The main loop
                    while (this.running) {
                        // Start scanning from a different log directory
                        if (resetDirectory) {
                            resetDirectory = false;
                            watcher.Path = this.LogDirectory;
                            channels.ForEach(x => x.Close());
                        }

                        // Keep the channel list up to date
                        this.UpdateChannels();

                        // Change the login credentials
                        if (authenticate) {
                            if (this.Status == IntelStatus.AuthenticationFailure) {
                                this.Status = channels.Any(x => x.LogFile != null)
                                    ? IntelStatus.Running
                                    : IntelStatus.Idle;
                            }
                            if (session != null) {
                                session.Dispose();
                                session = null;
                            }
                            // Only clear the authentication flag once we've actually
                            // attempted to reauthenticate
                            if (CreateSession()) {
                                this.authenticate = false;
                                sessionTimestamp = DateTime.UtcNow;
                            } else if (this.Status == IntelStatus.AuthenticationFailure) {
                                this.authenticate = false;
                            }
                        }

                        // Check for directory changes
                        FileSystemEventArgs args;
                        while (watcherEvents.TryDequeue(out args)) {
                            channels.ForEach(x => x.OnFileEvent(args));
                        }

                        // Have all children rescan their log files
                        channels.ForEach(x => x.Tick());

                        // Update Idle/Running status
                        switch (this.Status) {
                        case IntelStatus.NetworkFailure:
                        case IntelStatus.ServerFailure:
                            if (DateTime.UtcNow - lastFailure > this.RetryPeriod) {
                                // Basically a fall through
                                goto case IntelStatus.Idle;
                            }
                            break;
                        case IntelStatus.Running:
                        case IntelStatus.Idle:
                            this.Status = channels.Any(x => x.LogFile != null)
                                ? IntelStatus.Running
                                : IntelStatus.Idle;
                            break;
                        }

                        // Ping the session for keep alive
                        if (this.session != null) {
                            switch (this.Status) {
                            case IntelStatus.Idle:
                                this.session.Dispose();
                                this.session = null;
                                break;
                            case IntelStatus.Running:
                                if (DateTime.UtcNow - sessionTimestamp >= keepalivePeriod) {
                                    try {
                                        if (!this.session.KeepAlive()) {
                                            this.session.Dispose();
                                            this.session = null;
                                        }
                                    } catch (WebException) {
                                        this.Status = IntelStatus.NetworkFailure;
                                        this.lastFailure = DateTime.UtcNow;
                                    } catch (IntelException) {
                                        this.Status = IntelStatus.ServerFailure;
                                        this.lastFailure = DateTime.UtcNow;
                                    }
                                }
                                break;
                            }
                        }

                        // Wait for another event
                        if (this.Status != IntelStatus.Idle) {
                            signal.WaitOne(this.ChannelScanFrequency);
                        } else {
                            signal.WaitOne();
                        }
                    }

                    // Terminate the session before disconnecting
                    if (this.session != null) {
                        session.Dispose();
                        this.session = null;
                    }
                } //using (var container = new Container()) {

                this.Status = IntelStatus.Stopped;
                channels.ForEach(x => x.Close());
            } catch {
                this.Status = IntelStatus.FatalError;
                this.session = null;
            }
        }

        /// <summary>
        ///     Receives Intel reports from the child channel listeners.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> if the intel was sucessfully sent to the
        ///     reporting server; otherwise, <see langword="false"/>.
        /// </returns>
        internal bool OnIntelReported(IntelEventArgs e) {
            Contract.Requires(e != null);

            // Send into the ThreadPool so not to interfere with our processing
            var handler = this.IntelReported;
            if (handler != null) {
                ThreadPool.QueueUserWorkItem(args => handler(this, (IntelEventArgs)args), e);
            }

            // See if it's worth trying again
            switch (this.Status) {
            case IntelStatus.AuthenticationFailure:
            case IntelStatus.NetworkFailure:
            case IntelStatus.ServerFailure:
                ++this.IntelDropped;
                return false;
            }

            // First, try an already existing session
            if (this.session != null) {
                if (this.SendReport(e, false)) {
                    // Success
                    return true;
                } else if (this.session != null) {
                    // If the session object remains, it wasn't a session problem
                    return false;
                }
            }

            // Try again after (re)openning the session
            this.CreateSession();
            return this.SendReport(e, true);
        }

        private void UpdateChannels() {
            // Check for network problems
            switch (this.Status) {
            case IntelStatus.NetworkFailure:
            case IntelStatus.ServerFailure:
                break;
            }

            // Check for expiration
            var now = DateTime.UtcNow;
            if (now - this.channelTimestamp > this.ChannelUpdatePeriod) {
                return;
            }

            // Get the list of channels
            try {
                var list = IntelSession.GetIntelChannels();
                // Look for channels to remove
                channels.RemoveAll(x => !list.Any(y => x.Name == y));
                // Look for channels to add
                channels.AddRange(list
                    .Where(x => !channels.Any(y => y.Name == x))
                    .Select(x => new IntelChannel(this, x))
                    .ToArray());
                // Update timestamp
                this.channelTimestamp = now;
            } catch (WebException) {
                // Failed to download the list
                this.lastFailure = now;
                this.Status = IntelStatus.NetworkFailure;
            }
        }

        private bool CreateSession() {
            Contract.Requires(this.session == null);
            Contract.Ensures(this.session != null);

            // Once authentication fails, don't try again until the user fixes it
            if (this.Status == IntelStatus.AuthenticationFailure) {
                return false;
            }

            // Try openning a new server connection
            try {
                this.session = new IntelSession(this.username, this.password);
                return true;
            } catch (AuthenticationException) {
                // Will not try again until the user corrects this
                this.Status = IntelStatus.AuthenticationFailure;
            } catch (WebException) {
                // Network problems, try again later.
                this.Status = IntelStatus.NetworkFailure;
            } catch (IntelException) {
                // The server did something...odd.
                this.Status = IntelStatus.ServerFailure;
            }

            this.lastFailure = DateTime.UtcNow;
            return false;
        }

        private bool SendReport(IntelEventArgs e, bool lastTry) {
            // Require a session to send the attempt
            if (this.session == null) {
                this.IntelSent += lastTry ? 1 : 0;
                return false;
            }

            // Catch the expected errors and update the status appropriately
            try {
                if (this.session.Report(e.Channel.Name, e.Timestamp, e.Message)) {
                    // Successfully sent
                    ++this.IntelSent;
                    return true;
                } else {
                    // Session expired
                    this.session = null;
                    this.IntelSent += lastTry ? 1 : 0;
                    return false;
                }
            } catch (WebException) {
                // Dropping due to network problems
                this.Status = IntelStatus.NetworkFailure;
            } catch (IntelException) {
                // The server did something...odd.
                this.Status = IntelStatus.ServerFailure;
            }
            
            ++this.IntelDropped;
            this.lastFailure = DateTime.UtcNow;
            return false;
        }

        /// <summary>
        ///     Queues events from the <see cref="FileSystemWatcher"/> instance.
        /// </summary>
        private void watcher_Created(object sender, FileSystemEventArgs e) {
            watcherEvents.Enqueue(e);
            signal.Set();
        }

        /// <summary>
        ///     Queues events from the <see cref="FileSystemWatcher"/> instance.
        /// </summary>
        private void watcher_Changed(object sender, FileSystemEventArgs e) {
            watcherEvents.Enqueue(e);
            signal.Set();
        }

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(this.ChannelUpdatePeriod > TimeSpan.Zero);
            Contract.Invariant(this.ChannelScanFrequency > TimeSpan.Zero);
            Contract.Invariant(this.RetryPeriod > TimeSpan.Zero);
            Contract.Invariant(this.IntelSent >= 0);
            Contract.Invariant(this.IntelDropped >= 0);
        }
    }
}
