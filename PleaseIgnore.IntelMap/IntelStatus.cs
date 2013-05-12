using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Represents the current condition of a <see cref="IntelChannel"/>
    ///     or <see cref="IntelReporter"/>.
    /// </summary>
    public enum IntelStatus {
        /// <summary>
        ///     <see cref="IntelReporter.Start"/> has not yet been called
        /// </summary>
        Stopped = 0,
        /// <summary>
        ///     <see cref="IntelReporter.Start"/> has been called and the
        ///     component is current initializing
        /// </summary>
        Starting = 1,
        /// <summary>
        ///     The <see cref="IntelReporter"/> is running, but EVE is either
        ///     not running or the channels are not open.
        /// </summary>
        Waiting = 2,
        /// <summary>
        ///     The <see cref="IntelReporter"/> is actively parsing chat logs
        ///     and reporting intel.
        /// </summary>
        Active = 3,
        /// <summary>
        ///     <see cref="IntelReporter.Stop"/> has been called and the
        ///     component is currently shutting down.
        /// </summary>
        Stopping = 4,
        /// <summary>
        ///     <see cref="IDisposable.Dispose"/> has been called and the
        ///     component is currently shutting down.
        /// </summary>
        Disposing = 5,
        /// <summary>
        ///     The call to <see cref="IDisposable.Dispose"/> has been
        ///     completed.
        /// </summary>
        Disposed = 6,
        /// <summary>
        ///     The <see cref="IntelChannel"/> is disabled due to an illegal
        ///     or non-existant <see cref="IntelChannel.Path"/>
        /// </summary>
        InvalidPath = 7,
        /// <summary>
        ///     An internal fatal error has occured and the component can no
        ///     longer operate.
        /// </summary>
        FatalError = 8,
        /// <summary>
        ///     A network or server error is preventing communications with
        ///     the intel server
        /// </summary>
        NetworkError = 9,
        /// <summary>
        ///     The intel server rejected our authentication request.  Will
        ///     retry periodically.
        /// </summary>
        AuthenticationError = 10
    }
}
