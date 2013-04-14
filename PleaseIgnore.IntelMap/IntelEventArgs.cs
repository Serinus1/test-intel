using System;
using System.Diagnostics.Contracts;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Provides data for the intel reporting events.
    /// </summary>
    public class IntelEventArgs : EventArgs {
        /// <summary>
        ///     Initializes a new instance of <see cref="IntelEventArgs"/> class.
        /// </summary>
        /// <param name="channel">
        ///     The <see cref="IntelChannel"/> that provided this log entry.
        /// </param>
        /// <param name="timestamp">
        ///     The date and time this log entry was generated.
        /// </param>
        /// <param name="message">
        ///     The content of the log entry.
        /// </param>
        public IntelEventArgs(IntelChannel channel, DateTime timestamp,
                string message) {
            Contract.Requires<ArgumentNullException>(channel != null, "channel");
            Contract.Requires<ArgumentException>(timestamp.Kind == DateTimeKind.Utc);
            Contract.Requires<ArgumentException>(!String.IsNullOrWhiteSpace(message));

            this.Channel = channel;
            this.Timestamp = timestamp;
            this.Message = message;
        }

        /// <summary>
        ///     Gets the log file that generated this event.
        /// </summary>
        /// <value>
        ///     The instance of <see cref="IntelChannel"/> that parsed and reported
        ///     this log entry.
        /// </value>
        public IntelChannel Channel { get; private set; }

        /// <summary>
        ///     Gets the timestamp of the log entry.
        /// </summary>
        /// <value>
        ///     A value of <see cref="DateTime"/> that encodes the log entry's time
        ///     stamp in the UTC time zone.
        /// </value>
        public DateTime Timestamp { get; private set; }

        /// <summary>
        ///     Gets the content of the log entry.
        /// </summary>
        public string Message { get; private set; }

        /// <inheritdoc/>
        public override string ToString() {
            return string.Format("{0}: [{1} - {2:u}] {3}",
                GetType().Name,
                this.Channel.Name,
                this.Timestamp,
                this.Message);
        }

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(this.Channel != null);
            Contract.Invariant(!String.IsNullOrWhiteSpace(this.Message));
        }
    }
}
