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
    /// Manages the list of <see cref="IntelChannel" /> watched by an instance
    /// of <see cref="IntelReporter" />.
    /// </summary>
    /// <threadsafety static="true" instance="true" />
    public class IntelChannelContainer : INestedContainer, INotifyPropertyChanged {
        /// <summary>Default value of the <see cref="ChannelUpdateInterval"/>
        /// property</summary>
        internal const string defaultUpdateInterval = "24:00:00";
        /// <summary>Default value of the <see cref="RetryInterval"/>
        /// property</summary>
        internal const string defaultRetryInterval = "00:15:00";
        
        /// <summary>Thread Synchronization object</summary>
        private readonly object syncRoot = new object();
        /// <summary>List of managed <see cref="IntelChannel"/> objects</summary>
        private readonly List<IntelChannel> channels = new List<IntelChannel>();
        /// <summary>Timer object used to fetch updated channel lists</summary>
        private readonly Timer updateTimer;
        /// <summary>The current channel processing state</summary>
        [ContractPublicPropertyName("Status")]
        private volatile IntelStatus status;
        /// <summary>The component which owns this
        /// <see cref="IntelChannelContainer"/></summary>
        [ContractPublicPropertyName("Owner")]
        private readonly IComponent owner;
        /// <summary>The update period for the channel list</summary>
        [ContractPublicPropertyName("ChannelUpdateInterval")]
        private TimeSpan? updateInterval = TimeSpan.Parse(
                defaultUpdateInterval,
                CultureInfo.InvariantCulture);
        /// <summary>The network retry period for the channel list</summary>
        [ContractPublicPropertyName("RetryInterval")]
        private TimeSpan retryInterval = TimeSpan.Parse(
                defaultRetryInterval,
                CultureInfo.InvariantCulture);
        /// <summary>The intel upload count</summary>
        [ContractPublicPropertyName("IntelCount")]
        private int uploadCount;
        /// <summary>URI to use when fetching the channel list</summary>
        [ContractPublicPropertyName("ChannelListUri")]
        private Uri channelListUri = IntelExtensions.ChannelsUrl;
        /// <summary>Directory to use when overriding the IntelChannel's
        /// <see cref="IntelChannel.Path"/></summary>
        [ContractPublicPropertyName("Path")]
        private string logDirectory;
        /// <summary>The contents of the channel list the last time we fetched
        /// it</summary>
        private string[] channelList;

        /// <summary>
        /// Initializes a new instance of the <see cref="IntelChannelContainer" />
        /// class.
        /// </summary>
        public IntelChannelContainer() : this(null) {
            Contract.Ensures(Status == IntelStatus.Stopped);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="IntelChannelContainer" />
        /// class with the specified owning <see cref="Component"/>.
        /// </summary>
        /// <param name="owner">The instance of <see cref="Component"/> which
        /// owns this <see cref="IntelChannelContainer"/>.</param>
        public IntelChannelContainer(IComponent owner) {
            Contract.Ensures(Status == IntelStatus.Stopped);
            this.updateTimer = new Timer(this.timer_Callback);
            this.owner = owner;
        }

        /// <summary>Occurs when a new log entry has been read from the chat logs.</summary>
        public event EventHandler<IntelEventArgs> IntelReported;

        /// <summary>Occurs when a property value changes.</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Gets the current operational status of the
        /// <see cref="IntelChannelContainer" /> object and its children.
        /// </summary>
        /// <value>The status.</value>
        public IntelStatus Status {
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
        /// Gets a value indicating whether the <see cref="IntelChannelContainer" />
        /// is currently running and watching for log entries.
        /// </summary>
        /// <value>
        /// <see langword="true" /> if this instance is running; otherwise, <see langword="false" />.
        /// </value>
        public bool IsRunning { get { return this.status.IsRunning(); } }

        /// <summary>Gets an instance of <see cref="IntelChannelCollection" /></summary>
        /// <value>The channels.</value>
        /// <remarks>
        /// Calling <see cref="IntelChannel.Dispose" /> on an
        /// <see cref="IntelChannel" /> will remove it from
        /// <see cref="Channels" />.  It may be readded when the
        /// channel list is redownloaded.
        /// </remarks>
        public IntelChannelCollection Channels {
            get {
                Contract.Ensures(Contract.Result<IntelChannelCollection>() != null);
                lock (this.syncRoot) {
                    return new IntelChannelCollection(this.channels);
                }
            }
        }

        /// <summary>
        /// Gets or sets the time between downloads of the intel
        /// channel list.
        /// </summary>
        /// <value>
        /// An instance of <see cref="TimeSpan"/> describing the time to wait
        /// between periodic updates of the intel channel list or
        /// <see langword="null" /> to disable periodic downloads of the
        /// channel list.
        /// </value>
        public TimeSpan? ChannelUpdateInterval {
            get {
                Contract.Ensures(!Contract.Result<TimeSpan?>().HasValue
                        || (Contract.Result<TimeSpan?>() > TimeSpan.Zero));
                return this.updateInterval;
            }
            set {
                Contract.Requires<InvalidOperationException>(!this.IsRunning);
                Contract.Requires<ArgumentOutOfRangeException>(
                        !value.HasValue || (value > TimeSpan.Zero),
                        "value");
                Contract.Ensures(ChannelUpdateInterval == value);
                if (this.updateInterval != value) {
                    this.updateInterval = value;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("ChannelUpdatePeriod"));
                }
            }
        }

        /// <summary>
        /// Gets or sets the time to wait after a failed attempt to
        /// download the channel list before trying again.
        /// </summary>
        /// <value>
        /// An instance of <see cref="TimeSpan"/> describing the time to wait
        /// after an error before attempting to download the channel list
        /// again.
        /// </value>
        public TimeSpan RetryInterval {
            get {
                Contract.Ensures(Contract.Result<TimeSpan>() > TimeSpan.Zero);
                return this.retryInterval;
            }
            set {
                Contract.Requires<InvalidOperationException>(!this.IsRunning);
                Contract.Requires<ArgumentOutOfRangeException>(
                        value > TimeSpan.Zero,
                        "value");
                Contract.Ensures(RetryInterval == value);
                if (this.retryInterval != value) {
                    this.retryInterval = value;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("RetryInterval"));
                }
            }
        }

        /// <summary>
        /// Gets or sets the <see cref="Uri" /> to use when downloading
        /// the channel list.
        /// </summary>
        /// <value>The channel list URI.</value>
        /// <exception cref="System.ArgumentException"></exception>
        [AmbientValue((string)null)]
        public string ChannelListUri {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return (this.channelListUri ?? IntelExtensions.ChannelsUrl).OriginalString;
            }
            set {
                Contract.Requires<InvalidOperationException>(!this.IsRunning);
                var uri = (value != null) ? new Uri(value) : IntelExtensions.ChannelsUrl;
                if (!uri.IsAbsoluteUri) {
                    // TODO: Proper exception
                    throw new ArgumentException();
                }
                if (this.channelListUri != uri) {
                    this.channelListUri = uri;
                    this.OnPropertyChanged(new PropertyChangedEventArgs("ChannelListUri"));
                }
            }
        }

        /// <summary>Gets or sets the directory to search for log files.</summary>
        /// <value>The path.</value>
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

        /// <summary>Gets the owning component for the
        /// <see cref="IntelChannelContainer"/>.</summary>
        /// <value>
        /// A reference to the <see cref="IComponent"/> instance that owns
        /// this container or <see langword="null"/> if no owner was assigned.
        /// </value>
        public IComponent Owner { get { return this.owner; } }

        /// <summary>
        /// Gets an object that can be used for synchronization of the
        /// <see cref="IntelChannelContainer"/> state.
        /// </summary>
        /// <value>
        /// An instance of <see cref="Object" /> that can be used by classes
        /// derived from <see cref="IntelChannelContainer" /> to synchronize
        /// their own internal state.
        /// </value>
        protected object SyncRoot {
            get {
                Contract.Ensures(Contract.Result<object>() != null);
                return this.syncRoot;
            }
        }

        /// <summary>
        /// Gets the number of reports that have been made by this
        /// <see cref="IntelChannel" />.
        /// </summary>
        /// <value>The number of times the <see cref="IntelReported"/>
        /// event has been raised.</value>
        public int IntelCount { get { return this.uploadCount; } }

        /// <summary>
        /// Downloads the channel list and begins the acquisition of log
        /// entries from the EVE chat logs. This method enables
        /// <see cref="IntelReported" /> events.
        /// </summary>
        public void Start() {
            Contract.Requires<ObjectDisposedException>(
                    Status != IntelStatus.Disposed,
                    null);
            Contract.Requires<InvalidOperationException>(
                    Status != IntelStatus.FatalError);
            Contract.Ensures(IsRunning);

            lock (this.syncRoot) {
                if (this.status == IntelStatus.Stopped) {
                    try {
                        this.Status = IntelStatus.Starting;
                        this.OnStart();
                        this.Status = IntelStatus.Waiting;
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IsRunning"));
                    } catch {
                        this.Status = IntelStatus.FatalError;
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Stops the <see cref="IntelChannelContainer"/> from providing
        /// location data and events.  <see cref="IntelReported"/> events will
        /// no longer be raised nor will the channel list be updated.
        /// </summary>
        public void Stop() {
            Contract.Ensures(!IsRunning);
            lock (this.syncRoot) {
                if (this.IsRunning) {
                    try {
                        this.Status = IntelStatus.Stopping;
                        this.OnStop();
                        this.Status = IntelStatus.Stopped;
                    } catch {
                        this.Status = IntelStatus.FatalError;
                        throw;
                    } finally {
                        this.OnPropertyChanged(new PropertyChangedEventArgs("IsRunning"));
                    }
                }
            }
        }

        /// <summary>Releases the managed and unmanaged resources used by the
        /// <see cref="IntelChannelContainer" />.</summary>
        public void Dispose() {
            Contract.Ensures(this.Status == IntelStatus.Disposed);
            Contract.Ensures(!this.IsRunning);
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the
        /// <see cref="IntelChannelContainer"/> and optionally releases
        /// the managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <see langword="true"/> to release both managed and unmanaged
        /// resources; <see langword="false"/> to release only unmanaged
        /// resources.
        /// </param>
        protected virtual void Dispose(bool disposing) {
            Contract.Ensures(Status == IntelStatus.Disposed);
            Contract.Ensures(!IsRunning);

            if (disposing) {
                lock (this.syncRoot) {
                    if (this.status != IntelStatus.Disposed) {
                        try {
                            this.Status = IntelStatus.Disposing;
                            this.updateTimer.Dispose();
                            channels.ForEach(x => x.Dispose());
                        } catch {
                            // Ignore any exceptions during disposal
                        } finally {
                            channels.Clear();
                        }
                    }
                }
                this.IntelReported = null;
                this.PropertyChanged = null;
            }
            this.status = IntelStatus.Disposed;
        }

        /// <summary>
        /// Creates an instance of <see cref="IntelChannel" /> to manage
        /// the monitoring of an intel channel log file.
        /// </summary>
        /// <param name="channelName">The base file name of the intel channel to monitor.</param>
        /// <returns>
        /// An instance of <see cref="IntelChannel" /> to use when
        /// monitoring the log file.
        /// </returns>
        /// <remarks>
        /// Classes derived from <see cref="IntelChannelContainer" /> are
        /// free to override <see cref="CreateChannel" /> and completely
        /// replace the logic without calling the base implementation.
        /// In this case, the derivative class must register handlers to
        /// call <see cref="OnUpdateStatus" /> and <see cref="OnIntelReported" />
        /// under the appropriate circumstances.  The
        /// <see cref="IComponent.Site" /> must be initialzied to a proper
        /// linking instance of <see cref="ISite" />.
        /// </remarks>
        protected virtual IntelChannel CreateChannel(string channelName) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(channelName));
            Contract.Ensures(Contract.Result<IntelChannel>() != null);
            Contract.Ensures(Contract.Result<IntelChannel>().Site != null);
            Contract.Ensures(Contract.Result<IntelChannel>().Site.Container == this);
            Contract.Ensures(Contract.Result<IntelChannel>().Name == channelName);

            var channel = new IntelChannel();
            channel.Site = new ChannelSite(this, channel, channelName);
            if (this.logDirectory != null) {
                channel.Path = this.logDirectory;
            }
            channel.IntelReported += channel_IntelReported;
            channel.PropertyChanged += channel_PropertyChanged;
            return channel;
        }

        /// <summary>Raises the <see cref="IntelReported" /> event.</summary>
        /// <param name="e">Arguments of the event being raised.</param>
        /// <remarks>
        /// <see cref="OnIntelReported" /> makes no changes to the internal
        /// object state and can be safely called at any time.  If the
        /// logic within <see cref="CreateChannel" /> is replaced, the
        /// <see cref="IntelChannel.IntelReported" /> event needs to be
        /// forwarded to <see cref="OnIntelReported" />.
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

        /// <summary>Raises the <see cref="PropertyChanged" /> event.</summary>
        /// <param name="e">Arguments of the event being raised.</param>
        /// <remarks>
        /// <see cref="OnIntelReported" /> makes no changes to the internal
        /// object state and can be safely called at any time.  The
        /// <see cref="PropertyChanged" /> event is scheduled for asynchronous
        /// handling by the <see cref="ThreadPool" />.
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
        /// Called after <see cref="Start" /> has been called.
        /// </summary>
        /// <remarks>
        /// <see cref="OnStart" /> will be called from within a synchronized
        /// context so derived classes should not attempt to perform any
        /// additional synchronization themselves.
        /// </remarks>
        protected virtual void OnStart() {
            this.channels.ForEach(x => x.Start());
            this.updateTimer.Change(0, Timeout.Infinite);
        }

        /// <summary>
        /// Called periodically to download the intel channel list and
        /// update the list of <see cref="IntelChannel" /> components.
        /// </summary>
        /// <remarks>
        /// As <see cref="OnUpdateList" /> downloads data from a remote
        /// server, locks should not be maintained on object state during
        /// the call to <see cref="OnUpdateList" /> as this may lead to
        /// significantly impairments of the UI.
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
        /// Updates the <see cref="Status" /> property to reflect the aggregate
        /// state of those components in <see cref="Channels" />.
        /// </summary>
        /// <remarks>
        /// If a derived class replaces <see cref="CreateChannel" />, it
        /// must ensure that <see cref="OnUpdateStatus" /> is called
        /// when there is a change to the <see cref="IntelChannel.Status" />
        /// property by monitoring the <see cref="IntelChannel.PropertyChanged" />
        /// event.
        /// </remarks>
        protected virtual void OnUpdateStatus() {
            lock (this.syncRoot) {
                if (this.IsRunning) {
                    this.Status = this.channels.Aggregate(
                        IntelStatus.Waiting,
                        (sum, x) => IntelExtensions.Combine(sum, x.Status));
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
            this.channels.ForEach(x => x.Stop());
            this.updateTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>Calls <see cref="OnIntelReported" />.</summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="IntelEventArgs"/> instance
        /// containing the event data.</param>
        private void channel_IntelReported(object sender, IntelEventArgs e) {
            Contract.Requires(e != null);
            this.OnIntelReported(e);
        }

        /// <summary>Calls <see cref="OnUpdateStatus" />.</summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="PropertyChangedEventArgs"/>
        /// instance containing the event data.</param>
        private void channel_PropertyChanged(object sender, PropertyChangedEventArgs e) {
            Contract.Requires(e != null);
            if (String.IsNullOrEmpty(e.PropertyName) || (e.PropertyName == "Status")) {
                this.OnUpdateStatus();
            }
        }

        /// <summary>Calls <see cref="OnUpdateList" />, trapping any
        /// exceptions.</summary>
        /// <param name="state">Ignored</param>
        private void timer_Callback(object state) {
            try {
                this.OnUpdateList();
            } catch {
                // Fail on error
                lock (this.syncRoot) {
                    if (this.status != IntelStatus.Disposed) {
                        this.updateTimer.Dispose();
                        this.Status = IntelStatus.FatalError;
                    }
                }
                throw;
            }
        }

        /// <summary>Invariant method for Code Contracts.</summary>
        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(!this.updateInterval.HasValue
                || (this.updateInterval > TimeSpan.Zero));
            Contract.Invariant(this.retryInterval > TimeSpan.Zero);
            Contract.Invariant(this.channelListUri != null);
            Contract.Invariant(this.uploadCount >= 0);
            Contract.Invariant(Contract.ForAll(this.channels, x => x != null));
        }

        /// <summary>Gets all the components in the
        /// <see cref="IContainer" />.</summary>
        ComponentCollection IContainer.Components {
            get {
                lock (this.syncRoot) {
                    return new ComponentCollection(channels.ToArray());
                }
            }
        }

        /// <summary>
        /// Adds the specified <see cref="IComponent" /> to the
        /// <see cref="IContainer" /> at the end of the list, and assigns a
        /// name to the component.
        /// </summary>
        /// <param name="component">The <see cref="IComponent" /> to add.</param>
        /// <param name="name">
        /// The unique, case-insensitive name to assign to the component
        /// <see langword="null"/> null that leaves the component unnamed.
        /// </param>
        /// <exception cref="System.NotSupportedException">
        /// External modification of <see cref="IntelChannelContainer"/> is
        /// not supported.
        /// </exception>
        void IContainer.Add(IComponent component, string name) {
            throw new NotSupportedException(Resources.IntelChannelCollection_ReadOnly);
        }

        /// <summary>
        /// Adds the specified <see cref="IComponent" /> to the
        /// <see cref="IContainer" /> at the end of the list.
        /// </summary>
        /// <param name="component">The <see cref="IComponent" /> to add.</param>
        /// <exception cref="System.NotSupportedException">
        /// External modification of <see cref="IntelChannelContainer"/> is
        /// not supported.
        /// </exception>
        void IContainer.Add(IComponent component) {
            throw new NotSupportedException(Resources.IntelChannelCollection_ReadOnly);
        }

        /// <summary>
        /// Removes a component from the <see cref="IContainer" />.
        /// </summary>
        /// <param name="component">The <see cref="IComponent" /> to remove.</param>
        void IContainer.Remove(IComponent component) {
            // Called when the channel is being disposed
            if (this.status != IntelStatus.Disposing) {
                lock (this.syncRoot) {
                    var count = this.channels.RemoveAll(x => x == component);
                    if (count > 0) {
                        this.OnPropertyChanged(new PropertyChangedEventArgs("Channels"));
                    }
                }
            }
        }

        /// <summary>
        /// Downloads the list of channels to monitor from the Test
        /// Alliance Intel Map server.
        /// </summary>
        /// <returns>
        /// A <see cref="String" /> <see cref="Array" /> of channel
        /// filenames.
        /// </returns>
        /// <exception cref="WebException">There was a problem contacting the
        /// server or the server response was invalid.</exception>
        public static string[] GetChannelList() {
            Contract.Ensures(Contract.Result<string[]>() != null);
            Contract.Ensures(Contract.Result<string[]>().Length > 0);
            return GetChannelList(IntelExtensions.ChannelsUrl);
        }

        /// <summary>
        /// Downloads the list of channels to monitor from a specific
        /// reporting server.
        /// </summary>
        /// <param name="serviceUri">The server <see cref="Uri"/> to download
        /// the channel list from.</param>
        /// <returns>A <see cref="String" /> <see cref="Array" /> of channel
        /// filenames.</returns>
        /// <exception cref="WebException">There was a problem contacting
        /// the server or the server response was invalid.</exception>
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
                throw new WebException(Resources.IntelException,
                    WebExceptionStatus.ProtocolError);
            } else {
                return channels;
            }
        }

        /// <summary>
        /// Implementation of <see cref="ISite" /> for linking an instance of
        /// <see cref="IntelChannel" /> to its parent
        /// <see cref="IntelChannelContainer" />.
        /// </summary>
        private class ChannelSite : INestedSite {
            /// <summary>The container</summary>
            [ContractPublicPropertyName("Container")]
            private readonly IntelChannelContainer container;
            /// <summary>The component</summary>
            [ContractPublicPropertyName("Component")]
            private readonly IntelChannel component;
            /// <summary>The name</summary>
            [ContractPublicPropertyName("Name")]
            private readonly string name;

            /// <summary>
            /// Initializes a new instance of the <see cref="ChannelSite"/> class.
            /// </summary>
            /// <param name="container">The container.</param>
            /// <param name="component">The component.</param>
            /// <param name="name">The name.</param>
            internal ChannelSite(IntelChannelContainer container,
                    IntelChannel component, string name) {
                Contract.Requires(container != null);
                Contract.Requires(component != null);
                Contract.Requires(!String.IsNullOrEmpty(name));
                this.container = container;
                this.component = component;
                this.name = name;
            }

            /// <summary>Gets the component associated with the
            /// <see cref="ISite" />.</summary>
            /// <returns>The <see cref="IComponent" /> instance associated
            /// with the <see cref="ISite" />.</returns>
            public IComponent Component {
                get { return this.component; }
            }

            /// <summary>Gets the <see cref="IContainer" /> associated with the
            /// <see cref="ISite" />.</summary>
            /// <returns>The <see cref="IContainer" /> instance associated with
            /// the <see cref="ISite" />.</returns>
            public IContainer Container {
                get { return this.container; }
            }

            /// <summary>Determines whether the component is in design
            /// mode.</summary>
            /// <returns><see langword="true"/> if the component is in design
            /// mode; otherwise, <see langword="false"/>.</returns>
            public bool DesignMode {
                get {
                    var owner = this.container.owner;
                    var site = (owner != null) ? owner.Site : null;
                    return (site != null) && site.DesignMode;
                }
            }

            /// <summary>Gets or sets the name of the component associated
            /// with the <see cref="ISite" />.</summary>
            /// <returns>The name of the component associated with the
            /// <see cref="ISite" />; or null, if no name is assigned to the
            /// component.</returns>
            /// <exception cref="System.NotSupportedException">
            /// Attempt to modify <see cref="Name"/>.  <see cref="Name"/> is
            /// assigned by the server and treated as immutable.
            /// </exception>
            public string Name {
                get { return this.name; }
                set { throw new NotSupportedException(); }
            }

            /// <summary>Gets the full name of the component in this site.</summary>
            /// <returns>The full name of the component in this site.</returns>
            public string FullName {
                get {
                    var owner = this.container.owner;
                    var site = (owner != null) ? owner.Site : null;
                    var siteName = (site != null) ? site.Name : null;
                    if (!String.IsNullOrEmpty(siteName)) {
                        return siteName + '.' + this.name;
                    } else {
                        return this.name;
                    }
                }
            }

            /// <summary>Gets the service object of the specified type.</summary>
            /// <param name="serviceType">An object that specifies the type
            /// of service object to get.</param>
            /// <returns>
            /// A service object of type <paramref name="serviceType" /> or
            /// <see langword="null" /> if there is no service object of type
            /// <paramref name="serviceType" />.
            /// </returns>
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
                    var owner = this.container.owner;
                    var site = (owner != null) ? owner.Site : null;
                    return (site != null) ? site.GetService(serviceType) : null;
                }
            }
        }
    }
}
