using System;
using System.Runtime.Serialization;

namespace TestIntelReporter {
    /// <summary>
    ///     Thrown by <see cref="WebMethods"/> when the user credentials are invalid
    /// </summary>
    [Serializable]
    public class IntelAuthorizationException : IntelException {
        public IntelAuthorizationException()
            : base() {
        }

        public IntelAuthorizationException(string message)
            : base(message) {
        }

        public IntelAuthorizationException(string message, Exception innerException)
            : base(message, innerException) {
        }

        protected IntelAuthorizationException(SerializationInfo info, StreamingContext context)
            : base(info, context) {
        }
    }
}
