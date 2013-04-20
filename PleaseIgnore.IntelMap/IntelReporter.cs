using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
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

            this.ChannelDownloadPeriod = TimeSpan.Parse(
                defaultChannelDownloadPeriod,
                CultureInfo.InvariantCulture);
            this.KeepAlivePeriod = TimeSpan.Parse(
                defaultKeepAlivePeriod,
                CultureInfo.InvariantCulture);
            this.ChannelScanPeriod = TimeSpan.Parse(
                defaultChannelScanPeriod,
                CultureInfo.InvariantCulture);
            this.RetryPeriod = TimeSpan.Parse(
                defaultRetryPeriod,
                CultureInfo.InvariantCulture);
            this.UpdateDowntime(false);

            this.channels = new IntelChannelCollection(this);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class and adds it to the specified <see cref="Container"/>.
        /// </summary>
        public IntelReporter(IContainer container) : this() {
            Contract.Ensures(this.Container == container);
            if (container != null) {
                container.Add(this);
            }
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
                this.running = true;
                if ((thread == null) && !this.initializing && !this.DesignMode) {
                    this.thread = new Thread(this.ThreadMain);
                    this.thread.Start();
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
                }
            }
        }

        /// <summary>
        ///     Worker thread main loop.
        /// </summary>
        private void ThreadMain() {
            try {
                // Initialize I/O
                channels.RescanAll();
                this.CreateFileWatcher();

                while (this.running) {
                    // Check for downtime changes
                    this.UpdateDowntime(true);

                    // Execute deferred actions
                    Action action;
                    while (actionQueue.TryDequeue(out action)) {
                        action();
                    }

                    // Rummage through the log files
                    channels.Tick();

                    // Sleep until we have something to do
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
        /// <summary>
        ///     Gets or sets the TEST Alliance AUTH username.
        /// </summary>
        /// <remarks>
        ///     Changes to the <see cref="Username"/>, <see cref="Password"/>,
        ///     or <see cref="PasswordHash"/> will not be used until the next
        ///     time the client tries to reauthenticate.  To verify that the
        ///     authentication information is correct, use the
        ///     <see cref="Authenticate"/> or <see cref="BeginAuthenticate"/>
        ///     methods.
        /// </remarks>
        [DefaultValue((String)null), Category("Behavior")]
        public string Username { get; set; }

        /// <summary>
        ///     Gets or sets the <em>hashed</em> services password for the user.
        /// </summary>
        /// <remarks>
        ///     Changes to the <see cref="Username"/>, <see cref="Password"/>,
        ///     or <see cref="PasswordHash"/> will not be used until the next
        ///     time the client tries to reauthenticate.  To verify that the
        ///     authentication information is correct, use the
        ///     <see cref="Authenticate"/> or <see cref="BeginAuthenticate"/>
        ///     methods.
        /// </remarks>
        [DefaultValue((String)null), Category("Behavior")]
        public string PasswordHash { get; set; }

        /// <summary>
        ///     Sets the hashed password by automatically hashing and storing the
        ///     plaintext password.
        /// </summary>
        /// <remarks>
        ///     Changes to the <see cref="Username"/>, <see cref="Password"/>,
        ///     or <see cref="PasswordHash"/> will not be used until the next
        ///     time the client tries to reauthenticate.  To verify that the
        ///     authentication information is correct, use the
        ///     <see cref="Authenticate"/> or <see cref="BeginAuthenticate"/>
        ///     methods.
        /// </remarks>
        [Browsable(false)]
        public string Password {
            set {
                this.PasswordHash = !String.IsNullOrEmpty(value)
                    ? IntelSession.HashPassword(value)
                    : null;
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

            // TODO: Handle case when worker isn't running
            var asyncResult = new AuthenticateAsyncResult(this, callback,
                state, username, password);
            actionQueue.Enqueue(asyncResult.Execute);
            signal.Set();
            return asyncResult;
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
        ///     completed.  Authentication errors will be reported with an exception.
        /// </remarks>
        /// <exception cref="AuthenticationException">
        ///     The credentials provided by the user are incorrect.
        /// </exception>
        public bool EndAuthenticate(IAsyncResult asyncResult) {
            Contract.Requires<ArgumentNullException>(asyncResult != null, "asyncResult");

            var localResult = asyncResult as AuthenticateAsyncResult;
            if (localResult == null) {
                throw new ArgumentException(Properties.Resources.ArgumentException_WrongObject);
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
            return EndAuthenticate(BeginAuthenticate(username, password, null, null));
        }

        /// <summary>
        ///     Provides the asynchronous implementation of Authenticate()
        /// </summary>
        private class AuthenticateAsyncResult : IntelAsyncResult<IntelReporter, bool> {
            private readonly string username;
            private readonly string password;

            public AuthenticateAsyncResult(IntelReporter owner, AsyncCallback callback,
                    object state, string username, string password)
                : base(owner, callback, state) {
                Contract.Requires(username != null);
                Contract.Requires(password != null);
                this.username = username;
                this.password = IntelSession.HashPassword(password);
            }

            public void Cancel() {
                // TODO: Implement
            }

            public void Execute() {
                // TODO: Implement
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
                            Properties.Resources.ArgumentException_DirNotExist,
                            value));
                    }
                    
                    this.logDirectory = value;
                    if (this.thread != null) {
                        actionQueue.Enqueue(OnLogDirectory);
                        signal.Set();
                    }
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
                watcher.NotifyFilter = NotifyFilters.LastWrite
                    | NotifyFilters.Size;
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
        // List of active IntelChannels
        private readonly IntelChannelCollection channels;
        // The default value for ChannelDownloadPeriod
        private const string defaultChannelDownloadPeriod = "24:00:00";
        // The default value for ChannelScanPeriod
        private const string defaultChannelScanPeriod = "00:00:05";

        /// <summary>
        ///     Gets or sets the time between downloads of the intel
        ///     channel list.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultChannelDownloadPeriod)]
        [Category("Behavior")]
        public TimeSpan ChannelDownloadPeriod { get; set; }

        /// <summary>
        ///     Gets or sets the time between scans of the chat logs for new intel.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultChannelScanPeriod)]
        [Category("Behavior")]
        public TimeSpan ChannelScanPeriod { get; set; }

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

            // Send into the ThreadPool so not to interfere with our processing
            ThreadPool.QueueUserWorkItem(this.IntelReportedWorkItem, args);

            if (this.CanSend(false)) {
                // Report the intelligence
                try {
                    bool success = GetSession()
                        .Report(args.Channel.Name, args.Timestamp, args.Message);
                    if (success) {
                        ++this.IntelSent;
                        return true;
                    } else {
                        ++this.IntelDropped;
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
            return false;
        }

        /// <summary>
        ///     Raises <see cref="IntelReported"/> in the appropriate thread.
        /// </summary>
        /// <param name="state">
        ///     Instance of <see cref="IntelEventArgs"/> to include.
        /// </param>
        private void IntelReportedWorkItem(object state) {
            var handler = this.IntelReported;
            if (handler != null) {
                handler(this, (IntelEventArgs)state);
            }
        }
        #endregion

        #region Session Management
        // The current session (if any) for Intel Reporting
        private IntelSession session;
        // The last time we 'pinged' the server to keep our session alive
        private DateTime  lastKeepAlive;
        // The last time we 'failed' to contact the server
        private DateTime? lastFailure;
        // The default value for KeepAlivePeriod
        private const string defaultKeepAlivePeriod = "00:01:00";
        // The default value for RetryPeriod
        private const string defaultRetryPeriod = "00:15:00";

        /// <summary>
        ///     Gets or sets the time between server pings to maintain our Intel
        ///     Reporting session.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultKeepAlivePeriod)]
        [Category("Behavior")]
        public TimeSpan KeepAlivePeriod { get; set; }

        /// <summary>
        ///     Gets or sets the time to back off attempting to contact the server
        ///     again if networking problems prevent communication.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultRetryPeriod)]
        [Category("Behavior")]
        public TimeSpan RetryPeriod { get; set; }


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
            // TODO: Record manner of failure
            this.lastFailure = DateTime.UtcNow;
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
            if ((this.session != null) && !session.IsConnected) {
                this.session = null;
            }
            
            // Create the new session
            if (this.session == null) {
                try {
                    this.session = new IntelSession(this.Username, this.PasswordHash);
                    this.lastKeepAlive = DateTime.UtcNow;
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

        /// <summary>
        ///     Gets the date and time of the most recent server downtime.
        /// </summary>
        public DateTime LastDowntime { get; private set; }

        /// <summary>
        ///     Gets the date and time of the next scheduled server downtime.
        /// </summary>
        public DateTime NextDowntime { get; private set; }

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
            var now = DateTime.Now;
            var oldtime = this.LastDowntime;
            if (now.TimeOfDay > downtimeStarts) {
                this.LastDowntime = now.Date + downtimeStarts;
                this.NextDowntime = this.LastDowntime + new TimeSpan(1, 0, 0);
            } else {
                this.NextDowntime = now.Date + downtimeStarts;
                this.LastDowntime = this.NextDowntime - new TimeSpan(1, 0, 0);
            }

            if (signal && (oldtime != this.LastDowntime)) {
                OnPropertyChanged("LastDowntime");
                OnPropertyChanged("NextDowntime");
                channels.CloseAll();
            }
        }

        /// <summary>
        ///     Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        private void OnPropertyChanged(string propertyName) {
#if DEBUG
            // Verify that propertyName actually exists
            if (!String.IsNullOrEmpty(propertyName)) {
                Debug.Assert(this.GetType().GetProperty(propertyName) != null);
            }
#endif
            ThreadPool.QueueUserWorkItem(
                this.PropertyChangedWorkItem,
                propertyName);
        }

        /// <summary>
        ///     Calls the <see cref="PropertyChanged"/> handler in the appropriate
        ///     thread.
        /// </summary>
        /// <param name="state">
        ///     Name of the property whose value has changed.
        /// </param>
        private void PropertyChangedWorkItem(object state) {
            var handler = this.PropertyChanged;
            if (handler != null) {
                handler(this, new PropertyChangedEventArgs((string)state));
            }
        }
        #endregion

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(this.ChannelDownloadPeriod > TimeSpan.Zero);
            Contract.Invariant(this.ChannelScanPeriod > TimeSpan.Zero);
            Contract.Invariant(this.KeepAlivePeriod > TimeSpan.Zero);
            Contract.Invariant(this.RetryPeriod > TimeSpan.Zero);

            Contract.Invariant(this.NextDowntime > this.LastDowntime);
            Contract.Invariant(this.IntelSent >= 0);
            Contract.Invariant(this.IntelDropped >= 0);
        }
    }
}
