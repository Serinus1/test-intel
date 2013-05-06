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
    public sealed class IntelReporter : Component, INotifyPropertyChanged {
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
            get { return this.status; }
            private set {
                Contract.Ensures(Status == value);
                if (this.status != value) {
                    var running = this.IsRunning;
                    this.status = value;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("Status"));
                    if (this.IsRunning != running) {
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IsRunning"));
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
            // TODO: Raise PropertyChanged
            get { return this.synchronizingObject; }
            set { this.synchronizingObject = value; }
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
                    this.lastAuthFailure = null;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("Username"));
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
                    this.lastAuthFailure = null;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("PasswordHash"));
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
                this.keepAlivePeriod = value;
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
                this.sessionTimeout = value;
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
                this.authRetryTimeout = value;
            }
        }

        /// <summary>
        ///     Downloads the channel list and begins the acquisition of log
        ///     entries from the EVE chat logs. This method enables
        ///     <see cref="IntelReported"/> events.
        /// </summary>
        public void Start() {
            Contract.Requires<ObjectDisposedException>(
                    Status != IntelStatus.Disposed,
                    null);
            Contract.Requires<InvalidOperationException>(
                    Status != IntelStatus.FatalError);
            Contract.Ensures(IsRunning);
            this.channels.Start();
        }

        /// <summary>
        ///     Stops the <see cref="IntelChannelChannel"/> from providing
        ///     location data and events.  <see cref="IntelReported"/>
        ///     events will no longer be raised.
        /// </summary>
        public void Stop() {
            Contract.Ensures(!IsRunning);
            this.channels.Stop();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                this.Status = IntelStatus.Disposing;
                this.channels.Dispose();
                this.Status = IntelStatus.Disposed;
            }
            base.Dispose(disposing);
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
            } else if (create && this.lastAuthFailure.HasValue
                    && (this.lastAuthFailure.Value + this.authRetryTimeout > DateTime.UtcNow)) {
                // Wait longer before trying to log in again
                throw new AuthenticationException();
            } else if (create) {
                // Safe to create a new instance of IntelSession
                this.session = new IntelSession(this.username, this.passwordHash);
                this.timerSession.Change(this.keepAlivePeriod, this.keepAlivePeriod);
                this.lastIntel = DateTime.UtcNow;
                this.Status = IntelStatus.Active;
                this.OnPropertyChanged(new PropertyChangedEventArgs("Users"));
                return this.session;
            } else {
                return null;
            }
        }

        /// <summary>
        ///     Raises the <see cref="IntelReported"/> event.
        /// </summary>
        protected virtual void OnIntelReported(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            // Raise the event (as appropriate)
            var handler = this.IntelReported;
            if (handler != null) {
                var sync = this.synchronizingObject;
                WaitCallback callback = (state) => handler(this, e);
                if (sync != null) {
                    sync.BeginInvoke(callback, new object[] { null });
                } else {
                    ThreadPool.QueueUserWorkItem(callback);
                }
            }
            // Report the intel
            try {
                lock (this.syncRoot) {
                    var session = this.GetSession(true);
                    if (session.Report(e)) {
                        ++this.IntelSent;
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IntelSent"));
                    } else {
                        ++this.IntelDropped;
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IntelDropped"));
                    }
                }
            } catch (WebException) {
                // TODO: Change Status
                ++this.IntelDropped;
                this.OnPropertyChanged(new PropertyChangedEventArgs("IntelDropped"));
            } catch (AuthenticationException) {
                ++this.IntelDropped;
                this.OnPropertyChanged(new PropertyChangedEventArgs("IntelDropped"));
            }
        }

        /// <summary>
        ///     Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            Debug.Assert(String.IsNullOrEmpty(e.PropertyName)
                    || (this.GetType().GetProperty(e.PropertyName) != null));
            // Raise the event (as appropriate)
            var handler = this.PropertyChanged;
            if (handler != null) {
                var sync = this.synchronizingObject;
                WaitCallback callback = (state) => handler(this, e);
                if (sync != null) {
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
                    this.OnPropertyChanged(new PropertyChangedEventArgs("Users"));
                } else {
                    // Maintain the session
                    try {
                        var users = session.Users;
                        if (session.KeepAlive()) {
                            this.Status = IntelStatus.Active;
                        }
                        if (users != session.Users) {
                            this.OnPropertyChanged(new PropertyChangedEventArgs("Users"));
                        }
                    } catch (WebException) {
                        // TODO: Record status
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
        }

        /// <summary>
        ///     Periodically sends keep-alives to the session.
        /// </summary>
        private void timer_Callback(object state) {
            lock (this.syncRoot) {
                this.OnKeepAlive();
                if (!IsRunning || !this.GetSession(false).IsConnected) {
                    this.timerSession.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }


        #region Authentication

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
            return null;
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
            return false;
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
            return false;
        }
        #endregion

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
