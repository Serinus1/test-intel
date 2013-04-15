using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.IO;
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

        // Thread used for enternal monitoring
        private Thread thread;
        // List of active IntelChannels
        private List<IntelChannel> channels;
        // File system event queue
        private ConcurrentQueue<FileSystemEventArgs> watcherEvents;

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
        
        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class.
        /// </summary>
        public IntelReporter() {
            this.signal = new AutoResetEvent(false);
            this.watcherEvents = new ConcurrentQueue<FileSystemEventArgs>();

            this.ChannelUpdatePeriod = new TimeSpan(24, 0, 0);
            this.ChannelScanFrequency = new TimeSpan(0, 0, 5);
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
            lock (this) {
                this.running = true;
                if (!this.initializing && !this.DesignMode
                        && (this.thread == null)) {
                    this.thread = new Thread(this.ThreadMain);
                }
            }
        }

        public void Stop() {
            Contract.Ensures(this.Enabled == false);
            lock (this) {
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
            signal.Reset();
            using (var container = new Container()) {
                // Configure the File Watcher
                IntelSession session = null;
                var watcher = new FileSystemWatcher();
                watcher.BeginInit();
                watcher.Path = this.LogDirectory;
                watcher.Filter = "*.txt";
                watcher.Created += watcher_Created;
                watcher.Changed += watcher_Changed;
                watcher.EnableRaisingEvents = true;
                container.Add(watcher);
                watcher.EndInit();

                while (this.running) {
                    // Start scanning from a different log directory
                    if (resetDirectory) {
                        resetDirectory = false;
                        watcher.Path = this.LogDirectory;
                        // TODO: Reset children
                    }

                    // Change the login credentials
                    if (authenticate) {
                        if (session != null) {
                            session.Dispose();
                            session = null;
                        }
                    }

                    // We've been notified of a directory change
                    FileSystemEventArgs args;
                    while(watcherEvents.TryDequeue(out args)) {
                        // TODO: Notify children
                    }

                    // Have all children rescan their log files

                    // Wait for another event
                    signal.WaitOne(this.ChannelScanFrequency);
                }

                if (session != null) {
                    session.Dispose();
                }
            } //using (var container = new Container()) {
        }

        /// <summary>
        ///     Receives Intel reports from the child channel listeners.
        /// </summary>
        internal void OnIntelReported(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            ++this.IntelSent;

            var handler = this.IntelReported;
            if (handler != null) {
                handler(this, e);
            }
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
            Contract.Invariant(this.IntelSent >= 0);
        }
    }
}
