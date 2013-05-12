using System;
using System.Diagnostics.Contracts;
using System.Globalization;

namespace PleaseIgnore.IntelMap {
    /// <summary>Provides data for the intel reporting events.</summary>
    /// <threadsafety static="true" instance="true" />
    [Serializable]
    public class IntelEventArgs : EventArgs {
        /// <summary>
        /// Initializes a new instance of <see cref="IntelEventArgs" /> class.
        /// </summary>
        /// <param name="channel">The base name of the <see cref="IntelChannel"/>
        /// that initially reported this log entry.</param>
        /// <param name="timestamp">The date and time this log entry was
        /// generated.</param>
        /// <param name="message">The content of the log entry.</param>
        public IntelEventArgs(string channel, DateTime timestamp,
                string message) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(channel));
            Contract.Requires<ArgumentException>(timestamp.Kind == DateTimeKind.Utc);
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(message));

            this.Channel = channel;
            this.Timestamp = timestamp;
            this.Message = message;
        }

        /// <summary>Gets the log file that generated this event.</summary>
        /// <value>
        /// The base name of the <see cref="IntelChannel"/> that reported this
        /// log entry.
        /// </value>
        public string Channel { get; private set; }

        /// <summary>Gets the timestamp of the log entry.</summary>
        /// <value>
        /// A value of <see cref="DateTime" /> that encodes the log entry's time
        /// stamp in the UTC time zone.
        /// </value>
        public DateTime Timestamp { get; private set; }

        /// <summary>Gets the content of the log entry.</summary>
        /// <value>The payload of the log entry.</value>
        public string Message { get; private set; }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this
        /// instance of <see cref="IntelChannel" />.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance
        /// of <see cref="IntelChannel" />.
        /// </returns>
        public override string ToString() {
            return string.Format(
                CultureInfo.CurrentCulture,
                "{0}: [{1} - {2:u}] {3}",
                GetType().Name,
                this.Channel,
                this.Timestamp,
                this.Message);
        }

        /// <summary>
        /// Invariant method for Code Contracts.
        /// </summary>
        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(!String.IsNullOrEmpty(this.Channel));
            Contract.Invariant(Timestamp.Kind == DateTimeKind.Utc);
            Contract.Invariant(!String.IsNullOrEmpty(this.Message));
        }
    }
}
