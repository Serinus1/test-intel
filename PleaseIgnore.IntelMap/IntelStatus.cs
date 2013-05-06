using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Represents the current condition of a <see cref="IntelChannel"/>.
    /// </summary>
    public enum IntelStatus {
        /// <summary>
        ///     <see cref="IntelChannel.Start"/> has not yet been called
        /// </summary>
        Stopped = 0,
        /// <summary>
        ///     <see cref="IntelChannel.Start"/> has been called and the
        ///     component is current initializing
        /// </summary>
        Starting = 1,
        /// <summary>
        ///     The <see cref="IntelChannel"/> is running, but EVE is either
        ///     not running or the channel is not open.
        /// </summary>
        Waiting = 2,
        /// <summary>
        ///     The <see cref="IntelChannel"/> is actively parsing chat logs
        ///     and reporting intel.
        /// </summary>
        Active = 3,
        /// <summary>
        ///     <see cref="IntelChannel.Stop"/> has been called and the
        ///     component is currently shutting down.
        /// </summary>
        Stopping = 4,
        /// <summary>
        ///     <see cref="IntelChannel.Dispose"/> has been called and the
        ///     component is currently shutting down.
        /// </summary>
        Disposing = 5,
        /// <summary>
        ///     The call to <see cref="IntelChannel.Dispose()"/> has been
        ///     completed.
        /// </summary>
        Disposed = 6,
        /// <summary>
        ///     The <see cref="IntelChannel"/> is disabled due to an illegal
        ///     or non-existant <see cref="IntelChannel.Path"/>
        /// </summary>
        InvalidPath = 7,
        /// <summary>
        ///     A fatal error has occured and the <see cref="IntelChannel"/>
        ///     can no longer operate.
        /// </summary>
        FatalError = 8
    }
}
