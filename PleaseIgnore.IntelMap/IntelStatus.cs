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
        ///     The <see cref="IntelReporter"/> is being initialized.  It will
        ///     return to the <see cref="Stopped"/> or <see cref="Starting"/>
        ///     state once <see cref="IntelReporter.EndInit"/> is called.
        /// </summary>
        Initializing,
        /// <summary>
        ///     The <see cref="IntelReporter"/> has started, but is waiting
        ///     for the EVE client to start logging data.
        /// </summary>
        Idle,
        /// <summary>
        ///     The <see cref="IntelReporter"/> is reporting Intel to the
        ///     server.
        /// </summary>
        Connected,
        /// <summary>
        ///     The <see cref="IntelReporter.Username"/> or
        ///     <see cref="IntelReporter.PasswordHash"/> are incorrect.
        /// </summary>
        AuthenticationFailure,
        /// <summary>
        ///     The last attempt to contact the server failed due to external
        ///     factors.
        /// </summary>
        NetworkError,
        /// <summary>
        ///     An unknown problem forced the service to terminate.
        /// </summary>
        FatalError
    }
}
