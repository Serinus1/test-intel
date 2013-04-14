using System;
using System.Runtime.Serialization;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Thrown by <see cref="WebMethods"/> when there is a problem contacting
    ///     the remote server.
    /// </summary>
    [Serializable]
    public class IntelException : Exception {
        public IntelException()
            : base(Properties.Resources.IntelException) {
        }

        public IntelException(string message)
            : base(message) {
        }

        public IntelException(string message, Exception innerException)
            : base(message, innerException) {
        }

        protected IntelException(SerializationInfo info, StreamingContext context)
            : base(info, context) {
        }
    }
}
