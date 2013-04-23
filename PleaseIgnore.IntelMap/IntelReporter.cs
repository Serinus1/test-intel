using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Threading;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Provides a component to monitor EVE log files and report new
    ///     entries to the TEST Intel Map for distribution.
    /// </summary>
    [DefaultEvent("IntelReported")]
    public sealed class IntelReporter : Component, ISupportInitialize,
            INotifyPropertyChanged {
        #region Lifecycle
        // Signals that the component is currently initializing
        private bool initializing;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class.
        /// </summary>
        public IntelReporter() {
            Contract.Ensures(this.Container == null);
            this.UpdateDowntime(false);
            this.channels = new IntelChannelCollection(this);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class and adds it to the specified <see cref="Container"/>.
        /// </summary>
        public IntelReporter(IContainer container) {
            Contract.Ensures(this.Container == container);
            if (container != null) {
                container.Add(this);
            }

            this.UpdateDowntime(false);
            this.channels = new IntelChannelCollection(this);
        }

        /// <summary>
        ///     Signals the <see cref="IntelReporter"/> that initialization is
        ///     starting.
        /// </summary>
        /// <remarks>
        ///     Service startup is delayed until after initialization completes.
        ///     This allows events and properties to be safely configured
        ///     without concern for race conditions.
        /// </remarks>
        /// <seealso cref="EndInit"/>
        public void BeginInit() {
            if (this.thread != null) {
                throw new InvalidOperationException(
                    Properties.Resources.InvalidOperation_InitRunning);
            }
            this.initializing = true;
        }

        /// <summary>
        ///     Signals the <see cref="IntelReporter"/> that initialization has
        ///     completed.
        /// </summary>
        /// <remarks>
        ///     Service startup is delayed until after initialization completes.
        ///     This allows events and properties to be safely configured
        ///     without concern for race conditions.
        /// </remarks>
        /// <seealso cref="BeginInit"/>
        public void EndInit() {
            this.initializing = false;
            if (this.running) {
                Start();
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                Stop();
                signal.Dispose();
            }
            base.Dispose(disposing);
        }
        #endregion

        #region Worker Thread
        // Signaling the worker thread to start/stop
        [ContractPublicPropertyName("Enabled")]
        private volatile bool running;
        // Signals the worker thread to leave sleep and process its queues
        private readonly AutoResetEvent signal = new AutoResetEvent(false);
        // Queue of asynchronous actions to execute on the worker thread
        private readonly ConcurrentQueue<Action> actionQueue = new ConcurrentQueue<Action>();
        // Worker thread used for server communications and file monitoring
        private Thread thread;
        // Synchronization object for manipulating the thread state
        private readonly object syncRoot = new object();

        /// <summary>
        ///     Gets or sets a value indicating whether the
        ///     <see cref="IntelReporter"/> actively parsing and reporting
        ///     data from the log files.
        /// </summary>
        /// <seealso cref="Start"/>
        /// <seealso cref="Stop"/>
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
        ///     Starts the worker thread (if not already running) and begins
        ///     reporting log entries.  This is equivalent to setting
        ///     <see cref="Enabled"/> to <param langword="true"/>.
        /// </summary>
        public void Start() {
            Contract.Ensures(this.Enabled == true);

            lock (syncRoot) {
                try {
                    // Attempt to start the thread
                    this.running = true;
                    if ((thread == null) && !this.initializing && !this.DesignMode) {
                        this.thread = new Thread(this.ThreadMain);
                        this.thread.Start();
                        this.OnPropertyChanged("Enabled");
                    }
                } catch {
                    // Make sure we aren't in a half-open state if an exception
                    // occurs
                    this.thread = null;
                    this.running = false;
                    throw;
                }
            }
        }

        /// <summary>
        ///     Stops the worker thread and ceases all activity within
        ///     <see cref="IntelReporter"/>. This is equivalent to setting
        ///     <see cref="Enabled"/> to <param langword="false"/>.
        /// </summary>
        public void Stop() {
            Contract.Ensures(this.Enabled == false);

            lock (syncRoot) {
                this.running = false;
                if (thread != null) {
                    signal.Set();
                    thread.Join();
                    thread = null;
                    this.OnPropertyChanged("Enabled");
                    this.OnPropertyChanged("Status");
                }
            }
        }

        /// <summary>
        ///     Worker thread main loop.
        /// </summary>
        private void ThreadMain() {
            try {
                // EVE may already be running
                this.CreateFileWatcher();
                channels.RescanAll();
                if (channels.Any(x => x.LogFile != null)) {
                    lastIntelReport = DateTime.UtcNow;
                }
                this.OnPropertyChanged("Status");
                
                // Try to log in immediately so we can display an error box
                // quickly
                try {
                    this.GetSession();
                    this.OnPropertyChanged("Status");
                } catch (AuthenticationException) {
                } catch (IntelException) {
                } catch (WebException) {
                }

                // The main loop
                while (this.running) {
                    // Make sure the file watcher exists
                    if (this.fileSystemWatcher == null) {
                        this.CreateFileWatcher();
                    }

                    // Check for downtime changes
                    this.UpdateDowntime(true);

                    // Execute deferred actions
                    Action action;
                    while (actionQueue.TryDequeue(out action)) {
                        action();
                    }

                    // Rummage through the log files
                    channels.Tick();

                    // Maintain Session health
                    if ((this.lastIntelReport + this.channelIdlePeriod < DateTime.UtcNow)
                            || channels.All(x => x.LogFile == null)) {
                        channels.CloseAll();
                        if (this.session != null) {
                            this.CloseSession();
                            this.OnPropertyChanged("Status");
                        }
                    } else {
                        this.KeepAlive();
                    }

                    // Sleep until we have something to do
                    // TODO: What if ChannelScanPeriod is "long" compared to
                    // other timers?
                    signal.WaitOne(this.ChannelScanPeriod);
                }
            } finally {
                this.DestroyFileWatcher();
                channels.CloseAll();
                this.CloseSession();
            }
        }
        #endregion

        #region Authentication
        // The default value for RetryAuthenticationPeriod
        private const string defaultRetryAuthenticationPeriod = "01:00:00";
        // Field backing the Username property
        private string username;
        // Field backing the PasswordHash property
        private string passwordHash;
        // Field backing the RetryAuthenticationPeriod property
        private TimeSpan retryAuthenticationPeriod = TimeSpan.Parse(
            defaultRetryAuthenticationPeriod,
            CultureInfo.InvariantCulture);
        // The last time an authentication failed
        private DateTime? lastAuthenticationFailure;

        /// <summary>
        ///     Gets or sets the TEST Alliance AUTH username.
        /// </summary>
        /// <remarks>
        ///     Changes to the <see cref="Username"/>
        ///     or <see cref="PasswordHash"/> will not be used until the next
        ///     time the client tries to reauthenticate.  To verify that the
        ///     authentication information is correct, use the
        ///     <see cref="Authenticate"/> or <see cref="BeginAuthenticate"/>
        ///     methods.
        /// </remarks>
        [DefaultValue((String)null), Category("Behavior")]
        public string Username {
            get {
                return this.username;
            }
            set {
                if (this.username != value) {
                    this.username = value;
                    this.lastAuthenticationFailure = null;
                    this.OnPropertyChanged("Username");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the <em>hashed</em> services password for the user.
        /// </summary>
        /// <remarks>
        ///     Changes to the <see cref="Username"/>
        ///     or <see cref="PasswordHash"/> will not be used until the next
        ///     time the client tries to reauthenticate.  To verify that the
        ///     authentication information is correct, use the
        ///     <see cref="Authenticate"/> or <see cref="BeginAuthenticate"/>
        ///     methods.
        /// </remarks>
        [DefaultValue((String)null), Category("Behavior")]
        public string PasswordHash {
            get {
                return this.passwordHash;
            }
            set {
                if (this.passwordHash != value) {
                    this.passwordHash = value;
                    this.lastAuthenticationFailure = null;
                    this.OnPropertyChanged("PasswordHash");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time between attempts to authenticate against the
        ///     server.
        /// </summary>
        /// <remarks>
        ///     In some situations, a server may reject a login due to reasons other
        ///     than invalid password (e.g. an EVE API screw-up, database issue,
        ///     etc.).  Instead of permanently rejecting the credentials, it will
        ///     periodically try to log in.
        /// </remarks>
        [DefaultValue(typeof(TimeSpan), defaultRetryAuthenticationPeriod)]
        [Category("Behavior")]
        public TimeSpan RetryAuthenticationPeriod {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.retryAuthenticationPeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(value > TimeSpan.Zero);
                if (this.retryAuthenticationPeriod != value) {
                    this.retryAuthenticationPeriod = value;
                    this.OnPropertyChanged("RetryAuthenticationPeriod");
                }
            }
        }

        /// <summary>
        ///     Requests that we authenticate with the server under new
        ///     credentials, reporting the results asynchronously.
        /// </summary>
        /// <param name="username">
        ///     The TEST auth username.
        /// </param>
        /// <param name="password">
        ///     The plaintext TEST services password.  It will be hashed
        ///     automatically.
        /// </param>
        /// <param name="callback">
        ///     An optional delegate to call upon completion of the authentication
        ///     request.
        /// </param>
        /// <param name="state">
        ///     A user-provided object to provide to <paramref name="callback"/>
        ///     when reporting authentication completion.
        /// </param>
        /// <returns>
        ///     An instance of <see cref="IAsyncResult"/> to provide to
        ///     <see cref="EndAuthenticate"/> to receive the results of this
        ///     operation.
        /// </returns>
        public IAsyncResult BeginAuthenticate(string username, string password,
                AsyncCallback callback, object state) {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(password));
            Contract.Ensures(Contract.Result<IAsyncResult>() != null);

            lock (this.syncRoot) {
                var offline = (this.thread == null);
                var asyncResult = new AuthenticateAsyncResult(this, callback,
                    state, username, password, offline);
                if (offline) {
                    ThreadPool.QueueUserWorkItem(asyncResult.Execute);
                } else {
                    actionQueue.Enqueue(asyncResult.Execute);
                    signal.Set();
                }
                return asyncResult;
            }
        }

        /// <summary>
        ///     Reports the outcome of the authentication process started by
        ///     <see cref="BeginAuthenticate"/>.
        /// </summary>
        /// <param name="asyncResult">
        ///     Instance of <see cref="IAsyncResult"/> returned by a previous
        ///     call to <see cref="BeginAuthenticate"/>.
        /// </param>
        /// <remarks>
        ///     If <see cref="IAsyncResult.IsCompleted"/> is <see langword="false"/>,
        ///     <see cref="EndAuthenticate"/> will block until the operation is
        ///     completed.  In case of an error, the credentials are left unmodified
        ///     and an exception is thrown.
        /// </remarks>
        /// <exception cref="AuthenticationException">
        ///     The credentials provided by the user are incorrect.
        /// </exception>
        public bool EndAuthenticate(IAsyncResult asyncResult) {
            Contract.Requires<ArgumentNullException>(asyncResult != null, "asyncResult");

            var localResult = asyncResult as AuthenticateAsyncResult;
            if (localResult == null) {
                throw new ArgumentException(
                    Properties.Resources.ArgumentException_WrongObject,
                    "asyncResult");
            }

            return localResult.Wait(this, "EndAuthenticate");
        }

        /// <summary>
        ///     Updates the user's credentials and forces the service to
        ///     reauthenticate with the reporting service.
        /// </summary>
        /// <param name="username">
        ///     The TEST auth username.
        /// </param>
        /// <param name="password">
        ///     The plaintext TEST services password.  It will be hashed
        ///     automatically.
        /// </param>
        /// <exception cref="AuthenticationException">
        ///     The credentials provided by the user are incorrect.
        /// </exception>
        public bool Authenticate(string username, string password) {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(password));
            return EndAuthenticate(BeginAuthenticate(username, password, null, null));
        }

        /// <summary>
        ///     Provides the asynchronous implementation of Authenticate()
        /// </summary>
        private class AuthenticateAsyncResult : IntelAsyncResult<IntelReporter, bool> {
            private readonly string username;
            private readonly string password;
            private readonly bool offline;

            internal AuthenticateAsyncResult(IntelReporter owner, AsyncCallback callback,
                    object state, string username, string password, bool offline)
                : base(owner, callback, state) {
                Contract.Requires(owner != null);
                Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(username));
                Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(password));
                this.username = username;
                this.password = IntelSession.HashPassword(password);
                this.offline = offline;
            }

            internal void Execute() {
                if (!this.IsCompleted) {
                    try {
                        var now = DateTime.UtcNow;
                        var session = new IntelSession(this.username, this.password);
                        if (offline) {
                            // Not running in the main thread
                            session.Dispose();
                        } else {
                            // Only save the session if running in the main thread
                            Owner.CloseSession();
                            Owner.session = session;
                            Owner.lastKeepAlive = now;
                            Owner.lastIntelReport = now;
                        }
                        Owner.Username = this.username;
                        Owner.PasswordHash = this.password;
                        this.AsyncComplete(true);
                    } catch (AuthenticationException e) {
                        this.AsyncComplete(e);
                    } catch (IntelException e) {
                        Owner.ReportFailure(e);
                        this.AsyncComplete(e);
                    } catch (WebException e) {
                        Owner.ReportFailure(e);
                        this.AsyncComplete(e);
                    }
                }
            }

            internal void Execute(object obj) {
                this.Execute();
            }
        }
        #endregion

        #region File Monitoring
        // The overridden file directory for EVE logs
        private string logDirectory;
        // Instance of FileSystemWatcher used to monitor changes to logs
        private FileSystemWatcher fileSystemWatcher;
        // The default file directory for EVE logs
        private static readonly string defaultLogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EVE",
            "logs",
            "Chatlogs");

        /// <summary>
        ///     Gets or sets the directory path to find EVE chat logs.
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     The specified directory does not exist.
        /// </exception>
        [AmbientValue((string)null), DefaultValue((string)null), Category("Behavior")]
        public string LogDirectory {
            get {
                if (!String.IsNullOrEmpty(this.logDirectory)) {
                    return this.logDirectory;
                } else if (this.DesignMode) {
                    return null;
                } else {
                    return defaultLogDirectory;
                }
            }
            set {
                if (this.logDirectory != value) {
                    if (!String.IsNullOrEmpty(value) && !Directory.Exists(value)) {
                        throw new ArgumentException(string.Format(
                            CultureInfo.CurrentCulture,
                            Properties.Resources.ArgumentException_DirNotExist,
                            value));
                    }
                    
                    this.logDirectory = value;
                    if (this.thread != null) {
                        actionQueue.Enqueue(OnLogDirectory);
                        signal.Set();
                    }

                    this.OnPropertyChanged("LogDirectory");
                }
            }
        }

        /// <summary>
        ///     Initializes the instance of <see cref="FileSystemWatcher"/>
        ///     used to watch for changes in the file system.
        /// </summary>
        private void CreateFileWatcher() {
            this.DestroyFileWatcher();
            FileSystemWatcher watcher = null;
            try {
                watcher = new FileSystemWatcher();

                watcher.BeginInit();
                watcher.Path = this.LogDirectory;
                watcher.Filter = "*.txt";
                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.NotifyFilter = NotifyFilters.Size;
                watcher.EnableRaisingEvents = true;
                watcher.EndInit();

                this.fileSystemWatcher = watcher;
            } catch (ArgumentException) {
                // Generally, this means LogDirectory does not exist
                // TODO: Record the type of failure
                watcher.Dispose();
                this.CloseSession();
            }
        }

        /// <summary>
        ///     Destroys the instance of <see cref="FileSystemWatcher"/> (if
        ///     any) being used to monitor the file system.
        /// </summary>
        private void DestroyFileWatcher() {
            if (this.fileSystemWatcher != null) {
                this.fileSystemWatcher.Dispose();
                this.fileSystemWatcher = null;
            }
        }

        /// <summary>
        ///     Reconfigures the client for a new log directory.
        /// </summary>
        private void OnLogDirectory() {
            // Recreate the file watcher
            this.CreateFileWatcher();
        }

        /// <summary>
        ///     Queues events from the <see cref="FileSystemWatcher"/> instance.
        /// </summary>
        private void OnFileEvent(object sender, FileSystemEventArgs e) {
            Contract.Requires(e != null);
            channels.OnFileEvent(e);
        }
        #endregion

        #region Channel Management
        // The default value for ChannelIdlePeriod
        private const string defaultChannelIdlePeriod = "00:10:00";
        // The default value for ChannelDownloadPeriod
        private const string defaultChannelDownloadPeriod = "24:00:00";
        // The default value for ChannelScanPeriod
        private const string defaultChannelScanPeriod = "00:00:05";
        // List of active IntelChannels
        private readonly IntelChannelCollection channels;
        // Field backing the ChannelIdlePeriod property
        private TimeSpan channelIdlePeriod = TimeSpan.Parse(
                defaultChannelIdlePeriod,
                CultureInfo.InvariantCulture);
        // Field backing the ChannelDownloadPeriod property
        private TimeSpan channelDownloadPeriod = TimeSpan.Parse(
                defaultChannelDownloadPeriod,
                CultureInfo.InvariantCulture);
        // Field backing the ChannelScanPeriod property
        private TimeSpan channelScanPeriod = TimeSpan.Parse(
                defaultChannelScanPeriod,
                CultureInfo.InvariantCulture);
        // The most recent message read from a log file
        private DateTime lastIntelReport;

        /// <summary>
        ///     Gets or sets the time between log event messages before
        ///     declaring the channel 'idle'.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultChannelIdlePeriod)]
        [Category("Behavior")]
        public TimeSpan ChannelIdlePeriod {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.channelIdlePeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(value > TimeSpan.Zero);
                if (this.channelIdlePeriod != value) {
                    this.channelIdlePeriod = value;
                    this.OnPropertyChanged("ChannelIdlePeriod");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time between downloads of the intel
        ///     channel list.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultChannelDownloadPeriod)]
        [Category("Behavior")]
        public TimeSpan ChannelDownloadPeriod {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.channelDownloadPeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(value > TimeSpan.Zero);
                if (this.channelDownloadPeriod != value) {
                    this.channelDownloadPeriod = value;
                    this.OnPropertyChanged("ChannelDownloadPeriod");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time between scans of the chat logs for new intel.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultChannelScanPeriod)]
        [Category("Behavior")]
        public TimeSpan ChannelScanPeriod {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.channelScanPeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(value > TimeSpan.Zero);
                if (this.channelScanPeriod != value) {
                    this.channelScanPeriod = value;
                    this.OnPropertyChanged("ChannelScanPeriod");
                }
            }
        }

        /// <summary>
        ///     Gets the list of channels currently being monitored by the
        ///     component.
        /// </summary>
        [Browsable(false)]
        public IntelChannelCollection Channels {
            get {
                Contract.Ensures(Contract.Result<IntelChannelCollection>() != null);
                return this.channels;
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
        ///     Raised when a new intel report has been parsed from a log file.
        /// </summary>
        /// <remarks>
        ///     This call will be made from the <see cref="ThreadPool"/>.  The
        ///     consumer will need synchronize to their local threads as
        ///     appropriate.
        /// </remarks>
        public event EventHandler<IntelEventArgs> IntelReported;

        /// <summary>
        ///     Receives Intel reports from the child channel listeners.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> if the intel was sucessfully sent to the
        ///     reporting server; otherwise, <see langword="false"/>.
        /// </returns>
        internal bool OnIntelReported(IntelEventArgs args) {
            Contract.Requires(args != null);
            lastIntelReport = DateTime.UtcNow;

            // Raise the detection event
            this.RaiseEvent(
                new Action<object, IntelEventArgs>(this.IntelReported),
                args);

            if (this.CanSend(false)) {
                // Report the intelligence
                try {
                    bool success = GetSession()
                        .Report(args.Channel.Name, args.Timestamp, args.Message);
                    if (success) {
                        ++this.IntelSent;
                        this.OnPropertyChanged("IntelSent");
                        return true;
                    } else {
                        ++this.IntelDropped;
                        this.OnPropertyChanged("IntelDropped");
                        return false;
                    }
                } catch (AuthenticationException e) {
                    this.ReportFailure(e);
                } catch (IntelException e) {
                    this.ReportFailure(e);
                } catch (WebException e) {
                    this.ReportFailure(e);
                }
            }

            // Failed to report
            ++this.IntelDropped;
            this.OnPropertyChanged("IntelDropped");
            return false;
        }

        /// <summary>
        ///     Notification sent by <see cref="IntelChannel"/> when it's
        ///     openned or closed a log file.
        /// </summary>
        internal void OnChannelChanged(IntelChannel channel) {
            Contract.Requires(channel != null);
            if (channel.LogFile != null) {
                // So that the UI can display 'Connected' or 'Scanning' instead
                // of simple 'Idle'
                try {
                    this.GetSession();
                } catch (AuthenticationException) {
                } catch (IntelException) {
                } catch (WebException) {
                }
            }
        }
        #endregion

        #region Session Management
        // The default value for KeepAlivePeriod
        private const string defaultKeepAlivePeriod = "00:01:00";
        // The default value for RetryPeriod
        private const string defaultRetryPeriod = "00:00:30";
        // Field backing the KeepAlivePeriod property
        [ContractPublicPropertyName("KeepAlivePeriod")]
        private TimeSpan keepAlivePeriod = TimeSpan.Parse(
                defaultKeepAlivePeriod,
                CultureInfo.InvariantCulture);
        // Field backing the RetryPeriod property
        [ContractPublicPropertyName("RetryPeriod")]
        private TimeSpan retryPeriod = TimeSpan.Parse(
                defaultRetryPeriod,
                CultureInfo.InvariantCulture);
        // The current session (if any) for Intel Reporting
        private IntelSession session;
        // The last time we 'pinged' the server to keep our session alive
        private DateTime  lastKeepAlive;
        // The last time we 'failed' to contact the server
        private DateTime? lastFailure;

        /// <summary>
        ///     Gets or sets the time between server pings to maintain our Intel
        ///     Reporting session.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultKeepAlivePeriod)]
        [Category("Behavior")]
        public TimeSpan KeepAlivePeriod {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.keepAlivePeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(value > TimeSpan.Zero, "value");
                if (this.keepAlivePeriod != value) {
                    this.keepAlivePeriod = value;
                    this.OnPropertyChanged("KeepAlivePeriod");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time to back off attempting to contact the server
        ///     again if networking problems prevent communication.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultRetryPeriod)]
        [Category("Behavior")]
        public TimeSpan RetryPeriod {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.retryPeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(value > TimeSpan.Zero, "value");
                if (this.retryPeriod != value) {
                    this.retryPeriod = value;
                    this.OnPropertyChanged("RetryPeriod");
                }
            }
        }


        /// <summary>
        ///     Gets the number of users currently reporting intelligence.
        /// </summary>
        /// <value>
        ///     The number of users reporting intelligence.  The value is
        ///     undefined if we are not currently connected to the server.
        /// </value>
        [Browsable(false)]
        public int Users {
            get {
                Contract.Ensures(Contract.Result<int>() >= 0);
                var session = this.session;
                return (session != null) ? session.Users : 0;
            }
        }

        /// <summary>
        ///     Tests if we can ping the server or if we should wait until
        ///     an error timeout expires.
        /// </summary>
        /// <param name="throwError">
        ///     <see langword="true"/> if we should rethrow the previous
        ///     error if it still applies.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if we should try to contact the server
        ///     again; otherwise, <see langword="false"/>.
        /// </returns>
        internal bool CanSend(bool throwError) {
            Contract.EnsuresOnThrow<IntelException>(throwError);

            if (!lastFailure.HasValue) {
                return true;
            }

            var now = DateTime.UtcNow;
            if (now > this.lastFailure + this.RetryPeriod) {
                return true;
            }

            if (throwError) {
                throw new IntelException();
            } else {
                return false;
            }
        }

        /// <summary>
        ///     Reports a communications error with the server, setting the
        ///     retry timer and notify the client (if required).
        /// </summary>
        /// <param name="e">
        ///     The exception describing the failure.
        /// </param>
        internal void ReportFailure(Exception e) {
            Contract.Requires(e != null);
            this.lastFailure = DateTime.UtcNow;
            this.OnPropertyChanged("Status");
        }

        /// <summary>
        ///     Returns the current session, initiating one if it does not already
        ///     exist.  Can throw any exception normally thrown by
        ///     <see cref="IntelSession.IntelSession"/>.
        /// </summary>
        /// <returns>
        ///     Instance of <see cref="IntelSession"/> to use when contacting the
        ///     server.
        /// </returns>
        private IntelSession GetSession() {
            Contract.Ensures(Contract.Result<IntelSession>() != null);

            // Check network back off time
            CanSend(true);

            // Clear out any expired session
            var users = this.Users;
            if ((this.session != null) && !session.IsConnected) {
                this.session = null;
            }

            // Don't retry on authentication failures
            var now = DateTime.UtcNow;
            if (lastAuthenticationFailure.HasValue
                    && (lastAuthenticationFailure + retryAuthenticationPeriod > now)) {
                throw new AuthenticationException();
            }

            // If the username or password or blank, another no go
            if (String.IsNullOrEmpty(this.username) || String.IsNullOrEmpty(this.passwordHash)) {
                this.lastAuthenticationFailure = now;
                this.OnPropertyChanged("Status");
                throw new AuthenticationException();
            }
            
            // Create the new session
            if (this.session == null) {
                try {
                    this.session = new IntelSession(this.username, this.passwordHash);
                    this.lastKeepAlive = now;
                    this.lastIntelReport = now;
                    this.lastAuthenticationFailure = null;
                    this.OnPropertyChanged("Status");
                } catch (AuthenticationException) {
                    this.lastAuthenticationFailure = now;
                    this.OnPropertyChanged("Status");
                    throw;
                } catch(Exception e) {
                    this.ReportFailure(e);
                    throw;
                }
            }

            return this.session;
        }

        /// <summary>
        ///     Sends a keep alive to the server as necessary.
        /// </summary>
        private void KeepAlive() {
            if ((this.session == null) || (!session.IsConnected)) {
                this.session = null;
                return;
            }

            if (this.CanSend(false)) {
                // Perform the actual keep-alive
                var users = this.Users;
                var now = DateTime.UtcNow;
                if (now > this.lastKeepAlive + this.KeepAlivePeriod) {
                    try {
                        if (this.session.KeepAlive()) {
                            this.lastKeepAlive = now;
                        }
                    } catch (IntelException e) {
                        this.ReportFailure(e);
                    } catch (WebException e) {
                        this.ReportFailure(e);
                    }
                }
                // Check for changes to 'Users'
                if (this.Users != users) {
                    this.OnPropertyChanged("Users");
                }
            }
        }

        /// <summary>
        ///     Closes the currently openned session (if any).
        /// </summary>
        private void CloseSession() {
            if (this.session != null) {
                this.session.Dispose();
                this.session = null;
            }
        }
        #endregion

        #region Notification Support
        // The time each day when Tranquility downtime begins
        private static readonly TimeSpan downtimeStarts = new TimeSpan(11, 0, 0);
        // The instance of ISynchronizeInvoke to use for messaging
        private ISynchronizeInvoke synchronizingObject;

        /// <summary>
        ///     Gets or sets the object used to marshal the event handler
        ///     calls issued as a result of a <see cref="PropertyChanged"/>
        ///     or <see cref="IntelReported"/> event.
        /// </summary>
        [DefaultValue((object)null)]
        public ISynchronizeInvoke SynchronizingObject {
            get { return this.synchronizingObject; }
            set { this.synchronizingObject = value; }
        }

        /// <summary>
        ///     Gets the date and time of the most recent server downtime.
        /// </summary>
        /// <value>
        ///     A <see cref="DateTime"/> instance encoding the start time of
        ///     the most recent downtime.  The value is only valid if the
        ///     service is running.
        /// </value>
        [Browsable(false)]
        public DateTime LastDowntime { get; private set; }

        /// <summary>
        ///     Gets the date and time of the next scheduled server downtime.
        /// </summary>
        /// <value>
        ///     A <see cref="DateTime"/> instance encoding the start time of
        ///     the next scheduled downtime.  The value is only valid if the
        ///     service is running.
        /// </value>
        [Browsable(false)]
        public DateTime NextDowntime { get; private set; }

        /// <summary>
        ///     Gets the current status of the intel reporting component.
        /// </summary>
        [Browsable(false)]
        public IntelStatus Status {
            get {
                if (this.initializing) {
                    return IntelStatus.Initializing;
                } else if (this.thread == null) {
                    return IntelStatus.Stopped;
                } else if (this.fileSystemWatcher == null) {
                    return IntelStatus.MissingDirectory;
                } else if (lastFailure.HasValue) {
                    return IntelStatus.NetworkError;
                } else if (this.session != null) {
                    return IntelStatus.Connected;
                } else if (lastAuthenticationFailure.HasValue) {
                    return IntelStatus.AuthenticationFailure;
                } else {
                    return IntelStatus.Idle;
                }
            }
        }

        /// <summary>
        ///     Occurs when a property value changes.
        /// </summary>
        /// <remarks>
        ///     The <see cref="PropertyChanged"/> event can indicate all properties
        ///     on the object have changed by using either <see langword="null"/> or
        ///     <see cref="String.Empty"/> as the property name in the
        ///     <see cref="PropertyChangedEventArgs"/>.
        /// </remarks>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        ///     Updates the <see cref="LastDowntime"/> and <see cref="NextDowntime"/>
        ///     properties.
        /// </summary>
        /// <param name="signal">
        ///     Raises the appropriate signals.
        /// </param>
        private void UpdateDowntime(bool signal) {
            var now = DateTime.UtcNow;
            var oldtime = this.LastDowntime;
            if (now.TimeOfDay > downtimeStarts) {
                this.LastDowntime = now.Date + downtimeStarts;
                this.NextDowntime = this.LastDowntime + new TimeSpan(24, 0, 0);
            } else {
                this.NextDowntime = now.Date + downtimeStarts;
                this.LastDowntime = this.NextDowntime - new TimeSpan(24, 0, 0);
            }

            if (signal && (oldtime != this.LastDowntime)) {
                OnPropertyChanged("LastDowntime");
                OnPropertyChanged("NextDowntime");
                channels.CloseAll();
                this.CloseSession();
            }
        }

        /// <summary>
        ///     Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        internal void OnPropertyChanged(string propertyName) {
#if DEBUG
            // Verify that propertyName actually exists
            if (!String.IsNullOrEmpty(propertyName)) {
                Debug.Assert(this.GetType().GetProperty(propertyName) != null);
            }
#endif
            // Raise the event (as appropriate)
            this.RaiseEvent(
                new Action<object, PropertyChangedEventArgs>(this.PropertyChanged),
                new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        ///     Raises an event using the <see cref="SynchronizingObject"/>
        ///     as appropriate.
        /// </summary>
        private void RaiseEvent<T>(Action<object, T> handler, T args)
                where T : EventArgs {
            if (handler != null) {
                var invoker = this.synchronizingObject;
                if ((invoker == null) || !invoker.InvokeRequired) {
                    handler(this, args);
                } else {
                    invoker.BeginInvoke(handler, new object[] { this, args });
                }
            }
        }
        #endregion

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(this.channelDownloadPeriod > TimeSpan.Zero);
            Contract.Invariant(this.channelIdlePeriod > TimeSpan.Zero);
            Contract.Invariant(this.channelScanPeriod > TimeSpan.Zero);
            Contract.Invariant(this.keepAlivePeriod > TimeSpan.Zero);
            Contract.Invariant(this.retryPeriod > TimeSpan.Zero);
            Contract.Invariant(this.channels != null);

            Contract.Invariant(this.NextDowntime > this.LastDowntime);
            Contract.Invariant(this.IntelSent >= 0);
            Contract.Invariant(this.IntelDropped >= 0);

            // Unless we are initializating or in DesignMode, thread must be
            // set iff we are running
            Contract.Invariant(((this.thread != null) == this.running)
                ||  (this.initializing || this.DesignMode));
            // Thread cannot be set if we are initializating or in design mode
            //Contract.Invariant((this.thread == null)
            //    || !(this.initializing || this.DesignMode));
        }
    }
}
