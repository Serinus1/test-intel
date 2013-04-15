using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PleaseIgnore.IntelMap {
    public enum IntelStatus {
        /// <summary>
        ///     The <see cref="IntelReporter"/> is not currently running.
        /// </summary>
        Stopped,
        /// <summary>
        ///     The <see cref="IntelReporter"/> is waiting for EVE to start
        ///     logging intel.
        /// </summary>
        Idle,
        /// <summary>
        ///     The <see cref="IntelReporter"/> is reporting Intel to the
        ///     server.
        /// </summary>
        Running,
        /// <summary>
        ///     The <see cref="IntelReporter.Username"/> or
        ///     <see cref="IntelReporter.PasswordHash"/> are incorrect.
        /// </summary>
        AuthenticationFailure,
        /// <summary>
        ///     The last attempt to contact the server failed due to network
        ///     problems.
        /// </summary>
        NetworkFailure,
        /// <summary>
        ///     The last attempt to contact the server failed due to an expected
        ///     server response.
        /// </summary>
        ServerFailure,
        /// <summary>
        ///     An unknown problem forced the service to terminate.
        /// </summary>
        FatalError
    }
}
