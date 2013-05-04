using PleaseIgnore.IntelMap.Properties;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Manages the list of <see cref="IntelChannel"/> watched by an instance
    ///     of <see cref="IntelReporter"/>.
    /// </summary>
    /// <threadsafety static="true" instance="true"/>
    public class IntelChannelContainer : Component, IContainer, INestedContainer,
            INotifyPropertyChanged {
        // Default value of the ChannelUpdateInterval property
        private const string defaultUpdateInterval = "24:00:00";
        // Default value of the RetryInterval property
        private const string defaultRetryInterval = "00:15:00";
        
        // Thread Synchronization object
        private readonly object syncRoot = new object();
        // List of active IntelChannel objects
        private readonly List<IIntelChannel> channels
            = new List<IIntelChannel>();
        // Timer object used to fetch updated channel lists
        private readonly Timer updateTimer;
        // The current channel processing state
        [ContractPublicPropertyName("Status")]
        private volatile IntelChannelStatus status;
        // The update period for the channel list
        [ContractPublicPropertyName("ChannelUpdateInterval")]
        private TimeSpan? updateInterval = TimeSpan.Parse(defaultUpdateInterval, CultureInfo.InvariantCulture);
        // The network retry period for the channel list
        [ContractPublicPropertyName("RetryInterval")]
        private TimeSpan retryInterval = TimeSpan.Parse(defaultRetryInterval, CultureInfo.InvariantCulture);
        // The intel upload count
        [ContractPublicPropertyName("IntelCount")]
        private int uploadCount;
        // URI to use when fetching the channel list
        [ContractPublicPropertyName("ChannelListUri")]
        private Uri channelListUri = IntelExtensions.ChannelsUrl;
        // Directory to use when overriding the IntelChannel's Path
        [ContractPublicPropertyName("Path")]
        public string logDirectory;
        // The last time we downloaded the channel list
        private DateTime? lastDownload;
        // The contents of the channel list the last time we fetched it
        private string[] channelList;

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelChannelContainer"/>
        ///     class.
        /// </summary>
        public IntelChannelContainer() : this(null) {
            Contract.Ensures(Status == IntelChannelStatus.Stopped);
            Contract.Ensures(Container == null);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelChannelContainer"/>
        ///     class and adds it to the specified container.
        /// </summary>
        public IntelChannelContainer(IContainer container) {
            Contract.Ensures(Status == IntelChannelStatus.Stopped);
            Contract.Ensures(Container == container);
            this.updateTimer = new Timer(this.timer_Callback);
            if (container != null) {
                container.Add(this);
            }
        }

        /// <summary>
        ///     Occurs when a new log entry has been read from the chat logs.
        /// </summary>
        public event EventHandler<IntelEventArgs> IntelReported;

        /// <summary>
        ///     Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        ///     Gets the current operational status of the
        ///     <see cref="IntelChannelContainer"/> object and its children.
        /// </summary>
        public IntelChannelStatus Status {
            get { return this.status; }
            private set {
                Contract.Ensures(Status == value);
                if (this.status != value) {
                    this.status = value;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("Status"));
                }
            }
        }

        /// <summary>
        ///     Gets a value indicating whether the <see cref="IntelChannelContainer"/>
        ///     is currently running and watching for log entries.
        /// </summary>
        public bool IsRunning {
            get {
                var status = this.status;
                return (status == IntelChannelStatus.Active)
                    || (status == IntelChannelStatus.InvalidPath)
                    || (status == IntelChannelStatus.Starting)
                    || (status == IntelChannelStatus.Waiting);
            }
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
            get {
                lock (this.syncRoot) {
                    return new IntelChannelCollection(this.channels.ToArray());
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time between downloads of the intel
        ///     channel list.
        /// </summary>
        /// <value>
        ///     The time between downloads of the intel channel list or
        ///     <see langword="null"/> to disable periodic downloads of
        ///     the channel list.
        /// </value>
        [DefaultValue(typeof(TimeSpan), defaultUpdateInterval)]
        public TimeSpan? ChannelUpdateInterval {
            get {
                Contract.Ensures(!Contract.Result<TimeSpan?>().HasValue
                        || (Contract.Result<TimeSpan?>() > TimeSpan.Zero));
                return this.updateInterval;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                        !value.HasValue || (value > TimeSpan.Zero),
                        "value");
                Contract.Ensures(ChannelUpdateInterval == value);
                lock (this.syncRoot) {
                    if (this.updateInterval != value) {
                        var hadValue = this.updateInterval.HasValue;
                        var hasValue = value.HasValue;
                        this.updateInterval = value;
                        if (!hasValue && (this.channelList != null)) {
                            // Don't download again
                            this.updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
                        } else if (this.IsRunning && hasValue && !hadValue) {
                            // We'll need to download the channel list
                            if (lastDownload.HasValue) {
                                // TODO: Avoid triggering OnUpdateList multiple times
                                var diff = DateTime.UtcNow - this.lastDownload.Value;
                                if (diff > TimeSpan.Zero) {
                                    this.updateTimer.Change(diff, TimeSpan.Zero);
                                } else {
                                    this.updateTimer.Change(0, Timeout.Infinite);
                                }
                            }
                        }
                        this.OnPropertyChanged(new PropertyChangedEventArgs("ChannelUpdatePeriod"));
                    }
                }
            }
        }

        /// <summary>
        ///     Gets or sets the time to wait after a failed attempt to
        ///     download the channel list before trying again.
        /// </summary>
        [DefaultValue(typeof(TimeSpan), defaultRetryInterval)]
        public TimeSpan RetryInterval {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.retryInterval;
            }
            set {
                Contract.Requires<ArgumentOutOfRangeException>(
                        value > TimeSpan.Zero,
                        "value");
                Contract.Ensures(RetryInterval == value);
                lock (this.syncRoot) {
                    // TODO: Update the timer to reflect new retry period
                    if (this.retryInterval != value) {
                        this.retryInterval = value;
                        this.OnPropertyChanged(new PropertyChangedEventArgs("RetryInterval"));
                    }
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
                return (this.channelListUri ?? IntelExtensions.ChannelsUrl).OriginalString;
            }
            set {
                var uri = (value != null) ? new Uri(value) : IntelExtensions.ChannelsUrl;
                if (!uri.IsAbsoluteUri) {
                    // TODO: Proper exception
                    throw new ArgumentException();
                }

                lock (this.syncRoot) {
                    if (this.channelListUri != uri) {
                        this.channelListUri = uri;
                        if (this.IsRunning) {
                            // TODO: Avoid triggering OnUpdateList multiple times
                            this.updateTimer.Change(0, Timeout.Infinite);
                        }
                        this.OnPropertyChanged(new PropertyChangedEventArgs("ChannelListUri"));
                    }
                }
            }
        }

        /// <summary>
        ///     Gets or sets the directory to search for log files.
        /// </summary>
        [DefaultValue((string)null)]
        public string Path {
            get { return this.logDirectory; }
            set {
                lock (this.syncRoot) {
                    if (value != this.logDirectory) {
                        this.logDirectory = value;
                        this.channels.ForEach(x => x.Path = (value ?? IntelChannel.DefaultPath));
                        this.OnPropertyChanged(new PropertyChangedEventArgs("Path"));
                    }
                }
            }
        }

        /// <summary>
        ///     Gets an object that can be used for synchronization of
        ///     the component state.
        /// </summary>
        /// <value>
        ///     An <see cref="Object"/> that can be used by classes
        ///     derived from <see cref="IntelChannelContainer"/> to
        ///     synchronize their own internal state.
        /// </value>
        protected object SyncRoot {
            get {
                Contract.Ensures(Contract.Result<object>() != null);
                return this.syncRoot;
            }
        }

        /// <summary>
        ///     Gets the number of reports that have been made by this
        ///     <see cref="IntelChannel"/>.
        /// </summary>
        public int IntelCount { get { return this.uploadCount; } }

        /// <summary>
        ///     Downloads the channel list and begins the acquisition of log
        ///     entries from the EVE chat logs. This method enables
        ///     <see cref="IntelReported"/> events.
        /// </summary>
        public void Start() {
            Contract.Requires<ObjectDisposedException>(
                    Status != IntelChannelStatus.Disposed,
                    null);
            Contract.Requires<InvalidOperationException>(
                    Status != IntelChannelStatus.FatalError);
            Contract.Ensures(IsRunning);

            lock (this.syncRoot) {
                if (this.status == IntelChannelStatus.Stopped) {
                    try {
                        this.Status = IntelChannelStatus.Starting;
                        this.OnStart();
                        this.Status = IntelChannelStatus.Waiting;
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IsRunning"));
                    } catch {
                        this.Status = IntelChannelStatus.FatalError;
                        throw;
                    }
                }
            }
        }

        /// <summary>
        ///     Stops the <see cref="IntelChannelChannel"/> from providing
        ///     location data and events.  <see cref="IntelReported"/>
        ///     events will no longer be raised.
        /// </summary>
        public void Stop() {
            Contract.Ensures(!IsRunning);
            lock (this.syncRoot) {
                if (this.IsRunning) {
                    try {
                        this.Status = IntelChannelStatus.Stopping;
                        this.OnStop();
                        this.Status = IntelChannelStatus.Stopped;
                    } catch {
                        this.Status = IntelChannelStatus.FatalError;
                        throw;
                    } finally {
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IsRunning"));
                    }
                }
            }
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            Contract.Ensures(Status == IntelChannelStatus.Disposed);
            Contract.Ensures(!IsRunning);

            if (disposing) {
                lock (this.syncRoot) {
                    if (this.status != IntelChannelStatus.Disposed) {
                        try {
                            this.Status = IntelChannelStatus.Disposing;
                            this.updateTimer.Dispose();
                            channels.ForEach(x => x.Dispose());
                        } catch {
                            // Ignore any exceptions during disposal
                        } finally {
                            channels.Clear();
                            this.Status = IntelChannelStatus.Disposed;
                        }
                    }
                }
                this.IntelReported = null;
                this.PropertyChanged = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        ///     Creates an instance of <see cref="IntelChannel"/> to manage
        ///     the monitoring of an intel channel log file.
        /// </summary>
        /// <param name="channelName">
        ///     The base file name of the intel channel to monitor.
        /// </param>
        /// <returns>
        ///     An instance of <see cref="IntelChannel"/> to use when
        ///     monitoring the log file.
        /// </returns>
        /// <remarks>
        ///     Classes derived from <see cref="IntelChannelContainer"/> are
        ///     free to override <see cref="CreateChannel"/> and completely
        ///     replace the logic without calling the base implementation.
        ///     In this case, the derivative class must register handlers to
        ///     call <see cref="OnUpdateStatus"/> and <see cref="OnIntelReported"/>
        ///     under the appropriate circumstances.  The
        ///     <see cref="IntelChannel.Site"/> must be initialzied to a proper
        ///     linking instance of <see cref="ISite"/>.
        /// </remarks>
        protected virtual IIntelChannel CreateChannel(string channelName) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(channelName));
            Contract.Ensures(Contract.Result<IIntelChannel>() != null);
            Contract.Ensures(Contract.Result<IIntelChannel>().Site != null);
            Contract.Ensures(Contract.Result<IIntelChannel>().Site.Container == this);
            Contract.Ensures(Contract.Result<IIntelChannel>().Name == channelName);

            var channel = new IntelChannel();
            channel.Site = new ChannelSite(this, channel, channelName);
            if (this.logDirectory != null) {
                channel.Path = this.logDirectory;
            }
            channel.IntelReported += channel_IntelReported;
            channel.PropertyChanged += channel_PropertyChanged;
            return channel;
        }

        /// <summary>
        ///     Raises the <see cref="IntelReported"/> event.
        /// </summary>
        /// <param name="e">
        ///     Arguments of the event being raised.
        /// </param>
        /// <remarks>
        ///     <see cref="OnIntelReported"/> makes no changes to the internal
        ///     object state and can be safely called at any time.  If the
        ///     logic within <see cref="CreateChannel"/> is replaced, the
        ///     <see cref="IntelChannel.IntelReported"/> event needs to be
        ///     forwarded to <see cref="OnIntelReported"/>.
        /// </remarks>
        protected virtual void OnIntelReported(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");

            Interlocked.Increment(ref this.uploadCount);
            this.OnPropertyChanged(new PropertyChangedEventArgs("IntelCount"));

            var handler = this.IntelReported;
            if (handler != null) {
                handler(this, e);
            }
        }

        /// <summary>
        ///     Raises the <see cref="PropertyChanged"/> event.
        /// </summary>
        /// <param name="e">
        ///     Arguments of the event being raised.
        /// </param>
        /// <remarks>
        ///     <see cref="OnIntelReported"/> makes no changes to the internal
        ///     object state and can be safely called at any time.  The
        ///     <see cref="PropertyChanged"/> event is scheduled for asynchronous
        ///     handling by the <see cref="ThreadPool"/>.
        /// </remarks>
        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            Debug.Assert(String.IsNullOrEmpty(e.PropertyName)
                    || (this.GetType().GetProperty(e.PropertyName) != null));

            var handler = this.PropertyChanged;
            if (handler != null) {
                ThreadPool.QueueUserWorkItem(delegate(object state) {
                    handler(this, e);
                });
            }
        }

        /// <summary>
        ///     Called after <see cref="Start()"/> has been called.
        /// </summary>
        /// <remarks>
        ///     <see cref="OnFileCreated"/> will be called with synchronized
        ///     access to the object state.
        /// </remarks>
        protected virtual void OnStart() {
            this.channels.ForEach(x => x.Start());
            this.updateTimer.Change(0, Timeout.Infinite);
        }

        /// <summary>
        ///     Called periodically to download the intel channel list and
        ///     update the list of <see cref="IntelChannel"/> components.
        /// </summary>
        /// <remarks>
        ///     As <see cref="OnUpdateList"/> downloads data from a remote
        ///     server, locks should not be maintained on object state during
        ///     the call to <see cref="OnUpdateList"/> as this may lead to
        ///     significantly impairments of the UI.
        /// </remarks>
        protected virtual void OnUpdateList() {
            // A lot may have happened...
            if (!this.IsRunning) {
                return;
            }

            string[] list;
            try {
                // Download the new list outside the lock
                list = GetChannelList(this.channelListUri);
            } catch (WebException) {
                // Retry in a few minutes
                lock (this.syncRoot) {
                    if (this.IsRunning) {
                        this.updateTimer.Change(this.retryInterval, TimeSpan.Zero);
                    }
                }
                return;
            }

            // Alter program state within the lock
            lock (this.syncRoot) {
                if (!this.IsRunning) {
                    return;
                }
                if (this.channelList == null) {
                    // Initializing the channel list
                    this.channels.AddRange(list.Select(x => this.CreateChannel(x)));
                    this.channelList = list;
                    this.lastDownload = DateTime.UtcNow;
                    this.channels.ForEach(x => x.Start());
                    this.OnPropertyChanged(new PropertyChangedEventArgs("Channels"));
                } else {
                    // Patching the existing channel list
                    var toAdd = list
                        .Except(this.channelList, StringComparer.OrdinalIgnoreCase)
                        .Select(x => this.CreateChannel(x))
                        .ToList();
                    var toRemove = this.channels
                        .Where(x => !list.Contains(x.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                    this.channels.AddRange(toAdd);
                    this.channelList = list;
                    this.lastDownload = DateTime.UtcNow;
                    toRemove.ForEach(x => x.Dispose());
                    toAdd.ForEach(x => x.Start());
                    if ((toAdd.Count > 0) || (toRemove.Count > 0)) {
                        this.OnPropertyChanged(new PropertyChangedEventArgs("Channels"));
                    }
                }
                // Schedule the next update
                this.OnUpdateStatus();
                if (this.updateInterval.HasValue) {
                    this.updateTimer.Change(this.updateInterval.Value, TimeSpan.Zero);
                }
            }
        }

        /// <summary>
        ///     Updates the <see cref="Status"/> property to reflect the aggregate
        ///     state of those components in <see cref="Channels"/>.
        /// </summary>
        /// <remarks>
        ///     If a derived class replaces <see cref="CreateChannel"/>, it
        ///     must ensure that <see cref="OnUpdateStatus"/> is called
        ///     when there is a change to the <see cref="IntelChannel.Status"/>
        ///     property by monitoring the <see cref="IntelChannel.PropertyChanged"/>
        ///     event.
        /// </remarks>
        protected virtual void OnUpdateStatus() {
            lock (this.syncRoot) {
                if (this.IsRunning) {
                    this.Status = this.channels.Aggregate(
                        IntelChannelStatus.Waiting,
                        (sum, x) => IntelExtensions.Combine(sum, x.Status));
                }
            }
        }

        /// <summary>
        ///     Called after <see cref="Stop()"/> has been called.
        /// </summary>
        /// <remarks>
        ///     <see cref="OnStop()"/> will be called with synchronized
        ///     access to the object state.
        /// </remarks>
        protected virtual void OnStop() {
            this.channels.ForEach(x => x.Stop());
            this.updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        ///     Calls <see cref="OnIntelReported"/>.
        /// </summary>
        private void channel_IntelReported(object sender, IntelEventArgs e) {
            Contract.Requires(e != null);
            this.OnIntelReported(e);
        }

        /// <summary>
        ///     Calls <see cref="OnUpdateStatus"/>, trapping any exceptions.
        /// </summary>
        private void channel_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            Contract.Requires(e != null);
            if (String.IsNullOrEmpty(e.PropertyName) || (e.PropertyName == "Status")) {
                this.OnUpdateStatus();
            }
        }

        /// <summary>
        ///     Calls <see cref="OnTimer"/>, trapping any exceptions.
        /// </summary>
        private void timer_Callback(object state) {
            try {
                this.OnUpdateList();
            } catch {
                // Fail on error
                lock (this.syncRoot) {
                    if (this.status != IntelChannelStatus.Disposed) {
                        this.updateTimer.Dispose();
                        this.Status = IntelChannelStatus.FatalError;
                    }
                }
                throw;
            }
        }

        /// <summary>
        ///     Code Contracts class invariants.
        /// </summary>
        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(!this.updateInterval.HasValue
                || (this.updateInterval > TimeSpan.Zero));
            Contract.Invariant(this.retryInterval > TimeSpan.Zero);
            Contract.Invariant(this.channelListUri != null);
            Contract.Invariant(this.uploadCount >= 0);
            Contract.Invariant(Contract.ForAll(this.channels, x => x != null));
        }

        /// <inheritdoc/>
        ComponentCollection IContainer.Components {
            get {
                lock (this.syncRoot) {
                    return new ComponentCollection(channels.ToArray());
                }
            }
        }

        /// <inheritdoc/>
        void IContainer.Add(IComponent component, string name) {
            throw new NotSupportedException(Resources.IntelChannelCollection_ReadOnly);
        }

        /// <inheritdoc/>
        void IContainer.Add(IComponent component) {
            throw new NotSupportedException(Resources.IntelChannelCollection_ReadOnly);
        }

        /// <inheritdoc/>
        void IContainer.Remove(IComponent component) {
            // Called when the channel is being disposed
            if (this.status != IntelChannelStatus.Disposing) {
                lock (this.syncRoot) {
                    var count = this.channels.RemoveAll(x => x == component);
                    if (count > 0) {
                        this.OnPropertyChanged(new PropertyChangedEventArgs("Channels"));
                    }
                }
            }
        }

        /// <inheritdoc/>
        IComponent INestedContainer.Owner { get { return this; } }

        /// <summary>
        ///     Downloads the list of channels to monitor from the Test
        ///     Alliance Intel Map server.
        /// </summary>
        /// <returns>
        ///     A <see cref="String"/> <see cref="Array"/> of channel
        ///     filenames.
        /// </returns>
        /// <exception cref="WebException">
        ///     There was a problem contacting the server or the server
        ///     response was invalid.
        /// </exception>
        public static string[] GetChannelList() {
            Contract.Ensures(Contract.Result<string[]>() != null);
            Contract.Ensures(Contract.Result<string[]>().Length > 0);
            return GetChannelList(IntelExtensions.ChannelsUrl);
        }

        /// <summary>
        ///     Downloads the list of channels to monitor from a specific
        ///     reporting server.
        /// </summary>
        /// <param name="serviceUri">
        ///     The server URI to download maps from.
        /// </param>
        /// <returns>
        ///     A <see cref="String"/> <see cref="Array"/> of channel
        ///     filenames.
        /// </returns>
        /// <exception cref="WebException">
        ///     There was a problem contacting the server or the server
        ///     response was invalid.
        /// </exception>
        public static string[] GetChannelList(Uri serviceUri) {
            Contract.Requires<ArgumentNullException>(serviceUri != null, "serviceUri");
            Contract.Requires<ArgumentException>(serviceUri.IsAbsoluteUri);
            Contract.Ensures(Contract.Result<string[]>() != null);
            Contract.Ensures(Contract.Result<string[]>().Length > 0);

            // TODO: More thorough sanity check of the server response
            var channels = WebRequest
                .Create(serviceUri)
                .GetResponse()
                .ReadContent()
                .Split('\n', '\r')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Select(x => x.Split(',')[0])
                .ToArray();

            if (channels.Length == 0) {
                throw new WebException(Resources.IntelException);
            } else {
                return channels;
            }
        }

        /// <summary>
        ///     Implementation of <see cref="ISite"/> for linking an instance of
        ///     <see cref="IntelChannel"/> to its parent
        ///     <see cref="IntelChannelContainer"/>.
        /// </summary>
        private class ChannelSite : INestedSite {
            [ContractPublicPropertyName("Container")]
            private readonly IntelChannelContainer container;
            [ContractPublicPropertyName("Component")]
            private readonly IntelChannel component;
            [ContractPublicPropertyName("Name")]
            private readonly string name;

            internal ChannelSite(IntelChannelContainer container,
                    IntelChannel component, string name) {
                Contract.Requires(container != null);
                Contract.Requires(component != null);
                Contract.Requires(!String.IsNullOrEmpty(name));
                this.container = container;
                this.component = component;
                this.name = name;
            }

            public IComponent Component {
                get { return this.component; }
            }

            public IContainer Container {
                get { return this.container; }
            }

            public bool DesignMode {
                get { return this.container.DesignMode; }
            }

            public string Name {
                get { return this.name; }
                set { throw new NotSupportedException(); }
            }

            public string FullName {
                get {
                    var site = this.container.Site;
                    var siteName = (site != null) ? site.Name : null;
                    if (!String.IsNullOrEmpty(siteName)) {
                        return siteName + '.' + this.name;
                    } else {
                        return this.name;
                    }
                }
            }

            public object GetService(Type serviceType) {
                if (serviceType == typeof(ISite)) {
                    return this;
                } else if(serviceType == typeof(INestedSite)) {
                    return this;
                } else if (serviceType == typeof(IContainer)) {
                    return this.container;
                } else if (serviceType == typeof(INestedContainer)) {
                    return this.container;
                } else {
                    return this.container.GetService(serviceType);
                }
            }
        }
    }
}
