using System;
using System.Runtime.Serialization;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Thrown by <see cref="WebMethods"/> when the session has expired.
    /// </summary>
    [Serializable]
    public class IntelSessionException : IntelException {
        public IntelSessionException()
            : base() {
        }

        public IntelSessionException(string message)
            : base(message) {
        }

        public IntelSessionException(string message, Exception innerException)
            : base(message, innerException) {
        }

        protected IntelSessionException(SerializationInfo info, StreamingContext context)
            : base(info, context) {
        }
    }
}
