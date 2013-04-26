using System;
using System.Runtime.Serialization;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Thrown by <see cref="IntelSession"/> when there is a problem
    ///     communicating with the remote server.
    /// </summary>
    [Serializable]
    public class IntelException : Exception {
        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelException"/>
        ///     class.
        /// </summary>
        public IntelException()
            : base(Properties.Resources.IntelException) {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelException"/>
        ///     class with a specified error message and a reference to the
        ///     inner exception that is the cause of this exception.
        /// </summary>
        public IntelException(string message)
            : base(message) {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelException"/>
        ///     class with the specified error message.
        /// </summary>
        public IntelException(string message, Exception innerException)
            : base(message, innerException) {
        }


        /// <summary>
        ///     Initializes a new instance of the <see cref="IntelException"/>
        ///     class with serialized data.
        /// </summary>
        /// <param name="info">
        ///     The <see cref="SerializationInfo"/> that holds the serialized
        ///     object data about the exception being thrown. 
        /// </param>
        /// <param name="context">
        ///     The <see cref="StreamingContext"/> that contains contextual
        ///     information about the source or destination. 
        /// </param>
        protected IntelException(SerializationInfo info, StreamingContext context)
            : base(info, context) {
        }
    }
}
