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
using System.Threading.Tasks;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Provides a component to monitor EVE log files and report new
    ///     entries to the TEST Intel Map for distribution.
    /// </summary>
    [DefaultEvent("IntelReported")]
    public class IntelReporter : Component, INotifyPropertyChanged {
        // The default value for RetryAuthenticationPeriod
        private const string defaultRetryAuthenticationPeriod = "01:00:00";
        // The default value for ChannelIdlePeriod
        private const string defaultSessionTimeout = "00:10:00";
        // The default value for KeepAlivePeriod
        private const string defaultKeepAlivePeriod = "00:01:00";
        // The default value for RetryPeriod
        private const string defaultRetryPeriod = "00:00:30";

        // Protect internal state
        private readonly object syncRoot = new object();
        // List of active IntelChannel objects
        private readonly IntelChannelContainer channels;
        // Timer to use when maintaining the session keepalive
        private readonly Timer timerSession;
        // The instance of ISynchronizeInvoke to use for messaging
        [ContractPublicPropertyName("SynchronizingObject")]
        private ISynchronizeInvoke synchronizingObject;
        // The current channel processing state
        [ContractPublicPropertyName("Status")]
        private IntelStatus status;
        // The time to wait between sending keep alives
        [ContractPublicPropertyName("KeepAliveInterval")]
        private TimeSpan keepAlivePeriod = TimeSpan.Parse(
            defaultKeepAlivePeriod,
            CultureInfo.InvariantCulture);
        // Field backing the ChannelIdlePeriod property
        [ContractPublicPropertyName("SessionTimeout")]
        private TimeSpan sessionTimeout = TimeSpan.Parse(
                defaultSessionTimeout,
                CultureInfo.InvariantCulture);
        // Field backing the RetryAuthenticationPeriod property
        [ContractPublicPropertyName("AuthenticationRetryTimeout")]
        private TimeSpan authRetryTimeout = TimeSpan.Parse(
            defaultRetryAuthenticationPeriod,
            CultureInfo.InvariantCulture);
        // Field backing the ServiceUri property
        [ContractPublicPropertyName("ServiceUri")]
        private Uri serviceUri = IntelExtensions.ReportUrl;
        // The session used to contact the intel server
        private IntelSession session;
        // Field backing the Username property
        private string username;
        // Field backing the PasswordHash property
        private string passwordHash;
        // Date and time the last intel was reported to the server
        private DateTime lastIntel;
        // The last time an authentication attempt failed
        private DateTime? lastAuthFailure;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class.
        /// </summary>
        public IntelReporter() : this(null) {
            Contract.Ensures(Container == null);
            Contract.Ensures(Status == IntelStatus.Stopped);
            Contract.Ensures(!IsRunning);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelReporter"/>
        ///     class and adds it to the specified <see cref="Container"/>.
        /// </summary>
        public IntelReporter(IContainer container) {
            Contract.Ensures(this.Container == container);
            Contract.Ensures(Status == IntelStatus.Stopped);
            Contract.Ensures(!IsRunning);
            this.channels = new IntelChannelContainer();
            this.channels.IntelReported += channels_IntelReported;
            this.channels.PropertyChanged += channels_PropertyChanged;
            this.timerSession = new Timer(this.timer_Callback);

            if (container != null) {
                container.Add(this);
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
        ///     Raised when a new intel report has been parsed from a log file.
        /// </summary>
        /// <remarks>
        ///     This call will be made from the <see cref="ThreadPool"/>.  The
        ///     consumer will need synchronize to their local threads as
        ///     appropriate.
        /// </remarks>
        public event EventHandler<IntelEventArgs> IntelReported;

        /// <summary>
        ///     Gets a value indicating the operating state of this
        ///     <see cref="IntelReporter"/>.
        /// </summary>
        public IntelStatus Status {
            get {
                return IntelExtensions.Combine(this.status, this.channels.Status);
            }
            private set {
                if (this.status != value) {
                    var running = this.IsRunning;
                    this.status = value;
                    this.OnPropertyChanged("Status");
                    if (this.IsRunning != running) {
                        this.OnPropertyChanged("IsRunning");
                    }
                }
            }
        }

        /// <summary>
        ///     Gets a value indicating whether this <see cref="IntelReporter"/>
        ///     is a normal operating state.
        /// </summary>
        public bool IsRunning {
            get { return this.status.IsRunning(); }
        }

        /// <summary>
        ///     Gets an instance of <see cref="IntelChannelCollection"/>
        /// </summary>
        /// <remarks>
        ///     Calling <see cref="IntelChannel.Dispose"/> on an
        ///     <see cref="IntelChannel"/> will remove it from
        ///     <see cref="Channels"/>.  It may be readded when the
        ///     channel list is redownloaded.
        /// </remarks>
        public IntelChannelCollection Channels {
            get { return this.channels.Channels; }
        }

        /// <summary>
        ///     Gets or sets the object used to marshal the event handler
        ///     calls issued as a result of a <see cref="PropertyChanged"/>
        ///     or <see cref="IntelReported"/> event.
        /// </summary>
        [DefaultValue((object)null)]
        public ISynchronizeInvoke SynchronizingObject {
            get { return this.synchronizingObject; }
            set {
                Contract.Requires<InvalidOperationException>(!this.IsRunning);
                if (this.synchronizingObject != value) {
                    this.synchronizingObject = value;
                    this.OnPropertyChanged("SynchronizingObject");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the directory path to find EVE chat logs.
        /// </summary>
        /// <exception cref="ArgumentException">
        ///     The specified directory does not exist.
        /// </exception>
        [AmbientValue((string)null), Category("Behavior")]
        public string Path {
            get { return this.channels.Path; }
            set { this.channels.Path = value; }
        }

        /// <summary>
        ///     Gets or sets the TEST Alliance AUTH username.
        /// </summary>
        /// <remarks>
        ///     Changes to the <see cref="Username"/>
        ///     or <see cref="PasswordHash"/> will not be used until the next
        ///     time the client tries to reauthenticate.  To verify that the
        ///     authentication information is correct, call
        ///     <see cref="Authenticate"/> instead of setting <see cref="Username"/>
        ///     or <see cref="PasswordHash"/> directly.
        /// </remarks>
        [DefaultValue((String)null), Category("Behavior")]
        public string Username {
            get {
                return this.username;
            }
            set {
                if (this.username != value) {
                    this.username = value;
                    this.lastAuthFailure = null;
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
        ///     authentication information is correct, call
        ///     <see cref="Authenticate"/> instead of setting <see cref="Username"/>
        ///     or <see cref="PasswordHash"/> directly.
        /// </remarks>
        [DefaultValue((String)null), Category("Behavior")]
        public string PasswordHash {
            get {
                return this.passwordHash;
            }
            set {
                if (this.passwordHash != value) {
                    this.passwordHash = value;
                    this.lastAuthFailure = null;
                    this.OnPropertyChanged("PasswordHash");
                }
            }
        }

        /// <summary>
        ///     Gets the number of events sent to the intel reporting server.
        /// </summary>
        public int IntelSent { get; private set; }

        /// <summary>
        ///     Gets the number of events that were dropped due to network
        ///     or server errors.
        /// </summary>
        public int IntelDropped { get; private set; }

        /// <summary>
        ///     Gets the number of users currently connected to the server.
        /// </summary>
        public int Users {
            get {
                Contract.Ensures(Contract.Result<int>() >= 0);
                var session = this.session;
                return (session != null) ? session.Users : 0;
            }
        }

        /// <summary>
        ///     Gets or sets the time between downloads of the intel
        ///     channel list.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), IntelChannelContainer.defaultUpdateInterval)]
        [Category("Behavior")]
        public TimeSpan? ChannelUpdateInterval {
            get {
                Contract.Ensures(!Contract.Result<TimeSpan?>().HasValue 
                    || (Contract.Result<TimeSpan?>().Value > TimeSpan.Zero));
                return this.channels.ChannelUpdateInterval;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                        !value.HasValue || (value.Value > TimeSpan.Zero),
                        "value");
                Contract.Requires<InvalidOperationException>(!IsRunning);
                this.channels.ChannelUpdateInterval = value;
            }
        }

        /// <summary>
        ///     Gets or sets the time between server keep alives sent
        ///     to the server while a session is open.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultKeepAlivePeriod)]
        [Category("Behavior")]
        public TimeSpan KeepAliveInterval {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.keepAlivePeriod;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                    value > TimeSpan.Zero,
                    "value");
                Contract.Requires<InvalidOperationException>(!IsRunning);
                if (this.keepAlivePeriod != value) {
                    this.keepAlivePeriod = value;
                    this.OnPropertyChanged("KeepAliveInterval");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time to hold the <see cref="IntelSession"/>
        ///     open when not reporting fresh intel.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultSessionTimeout)]
        [Category("Behavior")]
        public TimeSpan SessionTimeout {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.sessionTimeout;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                    value > TimeSpan.Zero,
                    "value");
                Contract.Requires<InvalidOperationException>(!IsRunning);
                if (this.sessionTimeout != value) {
                    this.sessionTimeout = value;
                    this.OnPropertyChanged("SessionTimeout");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time to wait after authentication fails
        ///     before trying again.
        /// </summary>
        /// <remarks>
        ///     In some situations, a server may reject a login due to reasons other
        ///     than invalid password (e.g. an EVE API screw-up, database issue,
        ///     etc.).  Instead of permanently rejecting the credentials, it will
        ///     periodically try to log in.
        /// </remarks>
        [DefaultValue(typeof(TimeSpan), defaultRetryAuthenticationPeriod)]
        [Category("Behavior")]
        public TimeSpan AuthenticationRetryTimeout {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.authRetryTimeout;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                    value > TimeSpan.Zero,
                    "value");
                Contract.Requires<InvalidOperationException>(!IsRunning);
                Contract.Ensures(this.AuthenticationRetryTimeout == value);
                if (this.authRetryTimeout != value) {
                    this.authRetryTimeout = value;
                    this.OnPropertyChanged("AuthenticationRetryTimeout");
                }
            }
        }

        /// <summary>
        ///     Gets or sets the <see cref="Uri"/> to use when downloading
        ///     the channel list.
        /// </summary>
        [AmbientValue((string)null)]
        public string ChannelListUri {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return this.channels.ChannelListUri;
            }
            set {
                Contract.Requires<InvalidOperationException>(!IsRunning);
                this.channels.ChannelListUri = value;
            }
        }

        /// <summary>
        ///     Gets or sets the <see cref="Uri"/> to use when accessing the
        ///     intel reporting service.
        /// </summary>
        [AmbientValue((string)null)]
        public string ServiceUri {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return (this.serviceUri ?? IntelExtensions.ChannelsUrl).OriginalString;
            }
            set {
                Contract.Requires<InvalidOperationException>(!IsRunning);
                var uri = (value != null) ? new Uri(value) : IntelExtensions.ChannelsUrl;
                if (!uri.IsAbsoluteUri) {
                    // TODO: Proper exception
                    throw new ArgumentException();
                }
                if (this.serviceUri != uri) {
                    this.serviceUri = uri;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("ServiceUri"));
                }
            }
        }

        /// <summary>
        ///     Downloads the channel list and begins the acquisition of log
        ///     entries from the EVE chat logs. This method enables
        ///     <see cref="IntelReported"/> events.
        /// </summary>
        public void Start() {
            Contract.Requires<ObjectDisposedException>(
                    this.Status != IntelStatus.Disposed,
                    null);
            Contract.Requires<InvalidOperationException>(
                    this.Status != IntelStatus.FatalError);
            Contract.Ensures(this.IsRunning);

            lock (this.syncRoot) {
                try {
                    if (this.Status == IntelStatus.Stopped) {
                        this.Status = IntelStatus.Starting;
                        this.channels.Start();
                        try {
                            this.GetSession(true);
                        } catch (WebException) {
                            this.Status = IntelStatus.NetworkError;
                        } catch (AuthenticationException) {
                            this.Status = IntelStatus.AuthenticationError;
                        }
                    }
                } catch {
                    this.Status = IntelStatus.FatalError;
                    throw;
                }
            }
        }

        /// <summary>
        ///     Stops the <see cref="IntelReporter"/> from providing
        ///     location data and events.  <see cref="IntelReported"/>
        ///     events will no longer be raised.
        /// </summary>
        public void Stop() {
            Contract.Ensures(!this.IsRunning);

            lock (this.syncRoot) {
                try {
                    if (this.IsRunning) {
                        this.Status = IntelStatus.Stopping;
                        if (this.session != null) {
                            this.session.Dispose();
                            this.session = null;
                        }
                        this.channels.Stop();
                        this.Status = IntelStatus.Stopped;
                    }
                } catch {
                    this.Status = IntelStatus.FatalError;
                    throw;
                }
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                // Clean up
                lock (this.syncRoot) {
                    try {
                        if (this.Status != IntelStatus.Disposed) {
                            this.Status = IntelStatus.Disposing;
                            if (this.session != null) {
                                this.session.Dispose();
                                this.session = null;
                            }
                            this.channels.Dispose();
                        }
                    } finally {
                        this.Status = IntelStatus.Disposed;
                        base.Dispose(disposing);
                    }
                }
            } else {
                // Cannot safely clean up
                this.status = IntelStatus.Disposed;
                base.Dispose(disposing);
            }
        }

        /// <summary>
        ///     Gets the instance of <see cref="IntelSession"/> to use when
        ///     contacting the intel server.
        /// </summary>
        /// <param name="create">
        ///     <see langword="true"/> to create a new instance of
        ///     <see cref="IntelSession"/> if there is currently not an active
        ///     session; othwerise, return <see langword="null"/> if no session
        ///     is currently active.
        /// </param>
        protected virtual IntelSession GetSession(bool create) {
            Contract.Requires(IsRunning);
            Contract.Ensures((Contract.Result<IntelSession>() != null) || !create);
            var session = this.session;
            if ((session != null) && session.IsConnected) {
                // Existing session is still valid
                return session;
            } else if (create && (String.IsNullOrEmpty(this.username)
                    || String.IsNullOrEmpty(this.passwordHash))) {
                // Can't login without a username/password
                throw new AuthenticationException();
            } else if (create && this.lastAuthFailure.HasValue
                    && (this.lastAuthFailure + this.authRetryTimeout > DateTime.UtcNow)) {
                // Wait longer before trying to log in again
                throw new AuthenticationException();
            } else if (create) {
                // Safe to create a new instance of IntelSession
                this.session = new IntelSession(this.username, this.passwordHash, this.serviceUri);
                this.timerSession.Change(this.keepAlivePeriod, this.keepAlivePeriod);
                this.lastIntel = DateTime.UtcNow;
                this.Status = IntelStatus.Active;
                this.OnPropertyChanged("Users");
                return this.session;
            } else if (session != null) {
                // Session has expired
                this.session = null;
                timerSession.Change(Timeout.Infinite, Timeout.Infinite);
                return null;
            } else {
                // No session open
                return null;
            }
        }

        /// <summary>
        ///     Raises the <see cref="IntelReported"/> event.
        /// </summary>
        internal protected virtual void OnIntelReported(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            // Raise the event (as appropriate)
            this.lastIntel = DateTime.UtcNow;
            var handler = this.IntelReported;
            if (handler != null) {
                var sync = this.synchronizingObject;
                WaitCallback callback = (state) => handler(this, e);
                if ((sync != null) && sync.InvokeRequired) {
                    sync.BeginInvoke(callback, new object[] { null });
                } else {
                    ThreadPool.QueueUserWorkItem(callback);
                }
            }
            // Report the intel
            try {
                lock (this.syncRoot) {
                    // Race condition avoidance
                    if (!this.IsRunning) {
                        return;
                    }
                    // Report as necessary
                    var session = this.GetSession(true);
                    if (session.Report(e)) {
                        this.Status = IntelStatus.Active;
                        ++this.IntelSent;
                        this.OnPropertyChanged("IntelSent");
                    } else {
                        ++this.IntelDropped;
                        this.OnPropertyChanged("IntelDropped");
                    }
                }
            } catch (WebException) {
                this.Status = IntelStatus.NetworkError;
                ++this.IntelDropped;
                this.OnPropertyChanged("IntelDropped");
            } catch (AuthenticationException) {
                this.Status = IntelStatus.AuthenticationError;
                ++this.IntelDropped;
                this.OnPropertyChanged("IntelDropped");
            }
        }

        /// <summary>
        ///     Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        internal protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            Debug.Assert(String.IsNullOrEmpty(e.PropertyName)
                    || (this.GetType().GetProperty(e.PropertyName) != null));
            // Raise the event (as appropriate)
            var handler = this.PropertyChanged;
            if (handler != null) {
                var sync = this.synchronizingObject;
                WaitCallback callback = (state) => handler(this, e);
                if ((sync != null) && sync.InvokeRequired) {
                    sync.BeginInvoke(callback, new object[] { null });
                } else {
                    ThreadPool.QueueUserWorkItem(callback);
                }
            }
        }

        /// <summary>
        ///     Called every <see cref="KeepAliveInterval"/> while the server
        ///     connection is open.
        /// </summary>
        protected virtual void OnKeepAlive() {
            var session = this.GetSession(false);
            if (session != null) {
                if (this.lastIntel + this.sessionTimeout < DateTime.UtcNow) {
                    // Session has expired
                    session.Dispose();
                    this.Status = IntelStatus.Waiting;
                    this.OnPropertyChanged("Users");
                } else {
                    // Maintain the session
                    try {
                        var users = session.Users;
                        if (session.KeepAlive()) {
                            this.Status = IntelStatus.Active;
                        }
                        if (users != session.Users) {
                            this.OnPropertyChanged("Users");
                        }
                    } catch (WebException) {
                        this.Status = IntelStatus.NetworkError;
                    }
                }
            }
        }

        /// <summary>
        ///     Event handler for <see cref="IntelChannelContainer.IntelReported"/>.
        /// </summary>
        private void channels_IntelReported(object sender, IntelEventArgs e) {
            Contract.Requires(e != null);
            this.OnIntelReported(e);
        }

        /// <summary>
        ///     Event handler for <see cref="IntelChannelContainer.PropertyChanged"/>.
        /// </summary>
        private void channels_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            Contract.Requires(e != null);
            if ((e.PropertyName == "ChannelUpdateInterval") || (e.PropertyName == "Channels")
                    || (e.PropertyName == "Status") || (e.PropertyName == "Path")
                    || (e.PropertyName == "ChannelListUri")) {
                // Property we forward
                this.OnPropertyChanged(e);
            }
        }

        /// <summary>
        ///     Periodically sends keep-alives to the session.
        /// </summary>
        private void timer_Callback(object state) {
            lock (this.syncRoot) {
                if (this.IsRunning) {
                    this.OnKeepAlive();
                }
                if (!this.IsRunning || (this.GetSession(false) == null)) {
                    this.timerSession.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        /// <summary>
        ///     Helper function to automatically construct a <see cref="PropertyChangedEventArgs"/>
        ///     for <see cref="OnPropertyChanged(PropertyChangedEventArgs)"/>.
        /// </summary>
        private void OnPropertyChanged(string propertyName) {
            this.OnPropertyChanged(new PropertyChangedEventArgs(propertyName));
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
        public void Authenticate(string username, string password) {
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!string.IsNullOrEmpty(password));
            Contract.Requires<ObjectDisposedException>(this.Status != IntelStatus.Disposed);
            Contract.Requires<InvalidOperationException>(this.Status != IntelStatus.FatalError);

            // Verify the password is correct before saving anything
            var passwordHash = IntelSession.HashPassword(password);
            IntelSession testSession;
            try {
                testSession = new IntelSession(username, passwordHash, this.serviceUri);
            } catch (WebException) {
                lock (this.syncRoot) {
                    this.Status = IntelStatus.NetworkError;
                    throw;
                }
            }

            // Update the program state
            lock (this.syncRoot) {
                this.username = username;
                this.passwordHash = passwordHash;
                if (this.session != null) {
                    this.session.Dispose();
                }
                this.session = testSession;
                this.lastIntel = DateTime.UtcNow;
                this.lastAuthFailure = null;
                this.Status = IntelStatus.Active;
                this.timerSession.Change(this.keepAlivePeriod, this.keepAlivePeriod);
                this.OnPropertyChanged("Username");
                this.OnPropertyChanged("PasswordHash");
                this.OnPropertyChanged("Users");
            }
        }

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(this.sessionTimeout > TimeSpan.Zero);
            Contract.Invariant(this.keepAlivePeriod > TimeSpan.Zero);
            Contract.Invariant(this.channels != null);

            Contract.Invariant(this.IntelSent >= 0);
            Contract.Invariant(this.IntelDropped >= 0);
        }
    }
}
