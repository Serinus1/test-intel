using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Net;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Manages the list of <see cref="IntelChannel"/> watched by an instance
    ///     of <see cref="IntelReporter"/>.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(IntelChannelCollection.DebugView))]
    public sealed class IntelChannelCollection : ICollection<IntelChannel>,
            ICollection {
        // The parent instance of IntelReporter
        private readonly IntelReporter parent;
        // The actual collection of IntelChannel objects
        private readonly List<IntelChannel> list = new List<IntelChannel>(16);
        // Used to protect client reads from our modifications
        private readonly object syncRoot = new object();
        // The last time we attempted to download the channel list
        private DateTime? lastDownload;
        
        internal IntelChannelCollection(IntelReporter reporter) {
            Contract.Requires(reporter != null);
            this.parent = reporter;
        }

        /// <summary>
        ///     Gets the number of channels currently being tracked by the
        ///     <see cref="IntelChannelCollection"/>.
        /// </summary>
        public int Count { get { return list.Count; } }

        bool ICollection<IntelChannel>.IsReadOnly { get { return true; } }

        bool ICollection.IsSynchronized { get { return true; } }

        object ICollection.SyncRoot { get { return this.syncRoot; } }

        void ICollection<IntelChannel>.Add(IntelChannel item) {
            throw new NotSupportedException();
        }

        void ICollection<IntelChannel>.Clear() {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Determines whether the <see cref="IntelChannelCollection"/> is
        ///     tracking a specific <see cref="IntelChannel"/>.
        /// </summary>
        /// <param name="item">
        ///     The object to locate in the <see cref="IntelChannelCollection"/>.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if <paramref name="item"/> is found in the
        ///     <see cref="IntelChannelCollection"/>; otherwise,
        ///     <see langword="false"/>. 
        /// </returns>
        public bool Contains(IntelChannel item) {
            lock (this.syncRoot) {
                return list.Contains(item);
            }
        }

        /// <summary>
        ///     Copies the elements of the <see cref="IntelChannelCollection"/>
        ///     to an <see cref="Array"/>, starting at a particular
        ///     <see cref="Array"/> index.
        /// </summary>
        /// <param name="array"></param>
        /// <param name="arrayIndex"></param>
        public void CopyTo(IntelChannel[] array, int arrayIndex) {
            lock (this.syncRoot) {
                list.CopyTo(array, arrayIndex);
            }
        }

        void ICollection.CopyTo(Array array, int index) {
            lock (this.syncRoot) {
                ((ICollection)list).CopyTo(array, index);
            }
        }

        bool ICollection<IntelChannel>.Remove(IntelChannel item) {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Returns an enumerator that iterates through the
        ///     <see cref="IntelChannelCollection"/>.
        /// </summary>
        /// <returns>
        ///     A <see cref="IEnumerator{T}"/> that can be used to iterate
        ///     through the <see cref="IntelChannelCollection"/>.
        /// </returns>
        /// <remarks>
        ///     The <see cref="IEnumerator{T}"/> returned by
        ///     <see cref="GetEnumerator()"/> enumerates against a snapshot
        ///     of the <see cref="IntelChannelCollection"/>.  This makes the
        ///     <see cref="IEnumerator{T}"/> thread safe, but will not reflect
        ///     updates to the channel list.
        /// </remarks>
        public IEnumerator<IntelChannel> GetEnumerator() {
            lock (this.syncRoot) {
                ICollection<IntelChannel> clone = list.ToArray();
                return clone.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return this.GetEnumerator();
        }

        /// <summary>
        ///     Updates the channel list (if appropriate) and then pings each channel
        ///     to perform a log scan.
        /// </summary>
        internal void Tick() {
            var now = DateTime.UtcNow;
            var period = parent.ChannelDownloadPeriod;
            if (!lastDownload.HasValue || now > lastDownload + period) {
                // Get the updated channel list
                // TODO: Dial it back on network failures
                string[] channels = null;
                try {
                    channels = IntelSession.GetIntelChannels();
                } catch (WebException) {
                } catch (IntelException) {
                }

                // Remove stale channels
                if ((channels != null) && (channels.Length > 0)) {
                    lastDownload = now;
                    lock (this.syncRoot) {
                        // TODO: Should we keep lock while dealing with files?
                        // TODO: Notify clients on changes?
                        foreach (var channel in list
                                .Where(x => !channels.Contains(x.Name))
                                .ToList()) {
                            channel.Close();
                            list.Remove(channel);
                        }

                        foreach (var name in channels
                                .Where(x => !list.Any(y => y.Name == x))
                                .ToList()) {
                            var channel = new IntelChannel(parent, name);
                            channel.Rescan();
                            list.Add(channel);
                        }
                    }
                }
            }

            list.ForEach(x => x.Tick());
        }

        /// <summary>
        ///     Notifies all channels of a change in the file system.
        /// </summary>
        internal void OnFileEvent(FileSystemEventArgs e) {
            Contract.Requires(e != null);
            list.ForEach(x => x.OnFileEvent(e));
        }

        /// <summary>
        ///     Forces all channels to reopen their log files.
        /// </summary>
        internal void RescanAll() {
            list.ForEach(x => x.Rescan());
        }

        /// <summary>
        ///     Forces all channels to release their log files.
        /// </summary>
        internal void CloseAll() {
            list.ForEach(x => x.Close());
        }

        /// <summary>
        ///     Debug Proxy Viewer for <see cref="IntelChannelCollection"/>.
        /// </summary>
        private class DebugView {
            private readonly IntelChannelCollection collection;

            public DebugView(IntelChannelCollection collection) {
                this.collection = collection;
            }

            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public IntelChannel[] Values {
                get {
                    return (collection != null)
                        ? collection.list.ToArray()
                        : new IntelChannel[0];
                }
            }
        }
    }
}
