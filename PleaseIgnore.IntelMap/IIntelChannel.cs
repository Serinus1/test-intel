using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Diagnostics.Contracts;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Provides an abstract interface to <see cref="IntelChannel"/>.
    /// </summary>
    [ContractClass(typeof(IIntelChannelContract))]
    public interface IIntelChannel : IComponent, INotifyPropertyChanged {
        /// <summary>
        ///     Occurs when a new log entry has been read from the chat logs.
        /// </summary>
        event EventHandler<IntelEventArgs> IntelReported;

        /// <summary>
        ///     Gets the current operational status of the
        ///     <see cref="IntelChannel"/> object.
        /// </summary>
        IntelChannelStatus Status { get; }

        /// <summary>
        ///     Gets the channel name of this <see cref="IntelChannel"/>
        /// </summary>
        string Name { get; }

        /// <summary>
        ///     Gets or sets the directory to search for log files.
        /// </summary>
        string Path { get; set; }

        /// <summary>
        ///     Gets a value indicating whether the <see cref="IntelChannel"/>
        ///     is currently running and watching for log entries.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        ///     Initiate the acquisition of log entries from the EVE chat logs. This
        ///     method enables <see cref="IntelReported"/> events.
        /// </summary>
        void Start();

        /// <summary>
        ///     Stops the <see cref="IntelChannel"/> from providing location data and events.
        /// </summary>
        void Stop();
    }

    [ContractClassFor(typeof(IIntelChannel))]
    internal abstract class IIntelChannelContract : IIntelChannel {
        public event EventHandler<IntelEventArgs> IntelReported;
        public event EventHandler Disposed;
        public event PropertyChangedEventHandler PropertyChanged;

        public IntelChannelStatus Status {
            get { return default(IntelChannelStatus); }
        }

        public string Name {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>())
                        || !this.IsRunning);
                return default(string);
            }
        }

        public string Path {
            get {
                Contract.Ensures(!String.IsNullOrEmpty(Contract.Result<string>()));
                return default(string);
            }
            set {
                Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(value));
            }
        }

        public bool IsRunning {
            get { return default(bool); }
        }

        public void Start() {
            Contract.Requires<ObjectDisposedException>(
                Status != IntelChannelStatus.Disposed,
                null);
            Contract.Requires<InvalidOperationException>(
                !String.IsNullOrEmpty(Name));
            Contract.Ensures(Status != IntelChannelStatus.Stopped);
            Contract.Ensures(IsRunning);
        }

        public void Stop() {
            Contract.Ensures((Status == IntelChannelStatus.Stopped)
                || (Status == IntelChannelStatus.Disposed));
            Contract.Ensures(!IsRunning);
        }

        public ISite Site { get; set; }

        public void Dispose() {
        }
    }
}
