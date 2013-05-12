using PleaseIgnore.IntelMap.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    /// Provides low-level access to the reporting features of the Test
    /// Alliance Intel Map.
    /// </summary>
    /// <threadsafety static="true" instance="false" />
    public class IntelSession : IDisposable {
        /// <summary><see cref="Regex"/> to parse an error response</summary>
        private static readonly Regex ErrorResponse  = new Regex(@"^(50\d) ERROR (.*)");
        /// <summary><see cref="Regex"/> to parse an auth response</summary>
        private static readonly Regex AuthResponse = new Regex(@"^200 AUTH ([^\s]+) (\d+)");
        /// <summary><see cref="Regex"/> to parse a report response</summary>
        private static readonly Regex IntelResponse = new Regex(@"^202 INTEL .*");
        /// <summary><see cref="Regex"/> to parse a keep alive
        /// response</summary>
        private static readonly Regex AliveResponse = new Regex(@"^203 ALIVE OK (\d+)");
        /// <summary>Number of server errors before dropping the
        /// connection</summary>
        private const int maxServerErrors = 3;

        /// <summary>The username for this login (required when logging out)</summary>
        private readonly string username;
        /// <summary>The server-provided session id</summary>
        private readonly string session;
        /// <summary>The service access Uri</summary>
        private readonly Uri serviceUri;
        /// <summary>Count of consecutive server errors</summary>
        private int serverErrors;

        /// <summary>
        /// Creates a new instance of the <see cref="IntelSession" /> class
        /// and authenticates with the map server.
        /// </summary>
        /// <param name="username">The TEST user name.</param>
        /// <param name="passwordHash">An SHA1 hash of the user's
        /// TEST services password.</param>
        /// <seealso cref="HashPassword" />
        /// <exception cref="AuthenticationException">The username/password
        /// combination were rejected.</exception>
        /// <exception cref="WebException">Error in contacting the web
        /// server.</exception>
        public IntelSession(string username, string passwordHash)
            : this(username, passwordHash, IntelExtensions.ReportUrl) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(passwordHash));
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IntelSession" /> class
        /// and authenticates with the map server using a specified service
        /// <see cref="Uri" />.
        /// </summary>
        /// <param name="username">The TEST user name.</param>
        /// <param name="passwordHash">An SHA1 hash of the user's
        /// TEST services password.</param>
        /// <param name="serviceUri"><see cref="Uri" /> to use when contacting
        /// the Intel Map reporting service.</param>
        /// <seealso cref="HashPassword" />
        /// <exception cref="AuthenticationException">The username/password
        /// combination were rejected.</exception>
        /// <exception cref="WebException">Error in contacting the web
        /// server.</exception>
        /// <exception cref="NotSupportedException"><paramref name="serviceUri" />
        /// uses a URI scheme not registered with <see cref="WebRequest" />.</exception>
        public IntelSession(string username, string passwordHash, string serviceUri)
                : this (username, passwordHash, new Uri(serviceUri)) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(passwordHash));
            Contract.Requires<ArgumentNullException>(serviceUri != null, "serviceUri");
            Contract.Requires<ArgumentException>(Uri.IsWellFormedUriString(serviceUri, UriKind.Absolute));
        }

        /// <summary>
        /// Creates a new instance of the <see cref="IntelSession" /> class
        /// and authenticates with the map server using a specified service
        /// <see cref="Uri" />.
        /// </summary>
        /// <param name="username">The TEST user name.</param>
        /// <param name="passwordHash">An SHA1 hash of the user's
        /// TEST services password.</param>
        /// <param name="serviceUri"><see cref="Uri" /> to use when contacting
        /// the Intel Map reporting service.</param>
        /// <seealso cref="HashPassword" />
        /// <exception cref="AuthenticationException">The username/password
        /// combination were rejected.</exception>
        /// <exception cref="WebException">Error in contacting the web
        /// server.</exception>
        /// <exception cref="NotSupportedException"><paramref name="serviceUri" />
        /// uses a URI scheme not registered with <see cref="WebRequest" />.</exception>
        public IntelSession(string username, string passwordHash, Uri serviceUri) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(passwordHash));
            Contract.Requires<ArgumentNullException>(serviceUri != null, "serviceUri");
            Contract.Requires<ArgumentException>(serviceUri.IsAbsoluteUri);

            this.username = username;
            this.serviceUri = serviceUri;

            var request = WebRequest.Create(this.serviceUri);
            var response = request.Post(new Dictionary<string, string>() {
                { "username", username },
                { "password", passwordHash },
                { "action", "AUTH" },
                { "version", "2.2.0" }
            });
            var responseBody = response.ReadContent();

            Match match;
            if ((match = AuthResponse.Match(responseBody)).Success) {
                // Successfully authenticated
                this.session  = match.Groups[1].Value;
                this.Users = match.Groups[2].ToInt32();
                this.IsConnected = true;
            } else if ((match = ErrorResponse.Match(responseBody)).Success) {
                // Authentication failed
                throw new AuthenticationException(match.Groups[2].Value);
            } else {
                // The server responded with something unexpected
                throw new WebException(Resources.IntelException,
                    WebExceptionStatus.ProtocolError);
            }
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="IntelSession"/> class.
        /// </summary>
        ~IntelSession() {
            this.Dispose(false);
        }

        /// <summary>
        /// Gets a flag indicating whether our session with the intel
        /// reporting server is still valid.
        /// </summary>
        /// <value>
        /// <see langword="true" /> if this instance is connected; otherwise,
        /// <see langword="false" />.
        /// </value>
        /// <seealso cref="Closed"/>
        /// <remarks>
        /// An instance of <see cref="IntelSession" /> becomes disconnected
        /// through an explicit call to <see cref="Dispose()" />, two many
        /// consecutive errors, or the server reported an invalid session in
        /// a call to <see cref="KeepAlive" /> or
        /// <see cref="Report(string,DateTime,string)" />.
        /// </remarks>
        public bool IsConnected { get; private set; }

        /// <summary>Gets the number of users currently connected to the
        /// server.</summary>
        /// <value>
        /// The number of users reported by the intel server during
        /// our most recent <see cref="KeepAlive"/>.  This value is undefined
        /// once <see cref="IsConnected"/> is <see langword="false"/>.
        /// </value>
        public int Users { get; private set; }

        /// <summary>Gets the number of intel reports sent to the
        /// server.</summary>
        /// <value>The number of successful calls to
        /// <see cref="Report(string,DateTime,string)"/>.</value>
        public int ReportsSent { get; private set; }

        /// <summary>
        /// Occurs when this session with the server is closed, either
        /// through a call to <see cref="Dispose()" />, too many consecutive
        /// errors, or timing out.
        /// </summary>
        /// <seealso cref="IsConnected"/>
        public event EventHandler Closed;

        /// <summary>
        /// Sends a keep-alive to the intel reporting server, preserving
        /// our session.
        /// </summary>
        /// <returns>
        /// <see langword="true" /> if our session is still valid;
        /// otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="WebException">Failed to contact the web
        /// server.</exception>
        public virtual bool KeepAlive() {
            Contract.Ensures(Contract.Result<bool>() == this.IsConnected);
            if (!this.IsConnected)
                return false;

            try {
                var request = WebRequest.Create(this.serviceUri);
                var response = request.Post(new Dictionary<string, string>() {
                    { "session", this.session },
                    { "action", "ALIVE" },
                });
                var responseBody = response.ReadContent();

                Match match;
                if ((match = AliveResponse.Match(responseBody)).Success) {
                    // Successful ping of the server
                    this.Users = match.Groups[1].ToInt32();
                    return true;
                } else if ((match = ErrorResponse.Match(responseBody)).Success) {
                    if (match.Groups[1].Value == "502") {
                        // Our session has expired
                        this.OnClosed();
                        return false;
                    } else {
                        // The server responded with something unexpected
                        throw new WebException(Resources.IntelException,
                            WebExceptionStatus.ProtocolError);
                    }
                } else {
                    // The server responded with something unexpected
                    throw new WebException(Resources.IntelException,
                        WebExceptionStatus.ProtocolError);
                }
            } catch {
                this.OnError();
                throw;
            }
        }

        /// <summary>Sends a log entry to the intel reporting server.</summary>
        /// <param name="channel">The channel were the intel was
        /// reported.</param>
        /// <param name="timestamp">The time and date the intel was
        /// reported.</param>
        /// <param name="message">The entire message entered into the
        /// log file (including the <var>username &gt; </var>).</param>
        /// <returns>
        /// <see langword="true" /> if our session is still valid;
        /// otherwise, <see langword="false" />.
        /// </returns>
        /// <exception cref="WebException">Failed to contact the web
        /// server.</exception>
        public virtual bool Report(string channel, DateTime timestamp, string message) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(channel));
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(message));
            Contract.Ensures(Contract.Result<bool>() == this.IsConnected);
            if (!this.IsConnected)
                return false;

            try {
                var request = WebRequest.Create(this.serviceUri);
                var response = request.Post(new Dictionary<string, string>() {
                    { "session", session },
                    { "inteltime", timestamp.ToUnixTime()
                        .ToString("F0", CultureInfo.InvariantCulture) },
                    { "action", "INTEL" },
                    { "region", channel },
                    // XXX: The \r is to make our report match the perl version EXACTLY
                    { "intel", message + '\r' }
                });
                var responseBody = response.ReadContent();

                Match match;
                if ((match = IntelResponse.Match(responseBody)).Success) {
                    // Successfully reported intel
                    ++this.ReportsSent;
                    this.serverErrors = 0;
                    return true;
                } else if ((match = ErrorResponse.Match(responseBody)).Success) {
                    if (match.Groups[1].Value == "502") {
                        // Our session has expired
                        this.OnClosed();
                        return false;
                    } else {
                        // The server responded with something unexpected
                        throw new WebException(Resources.IntelException,
                            WebExceptionStatus.ProtocolError);
                    }
                } else {
                    // The server responded with something unexpected
                    throw new WebException(Resources.IntelException,
                        WebExceptionStatus.ProtocolError);
                }
            } catch {
                this.OnError();
                throw;
            }
        }

        /// <summary>Sends a log entry to the intel reporting server.</summary>
        /// <param name="e">An instance of <see cref="IntelEventArgs" />
        /// containing the information to report.</param>
        /// <returns>
        /// <see langword="true" /> if our session is still valid;
        /// otherwise, <see langword="false" />.
        /// </returns>
        public bool Report(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            return this.Report(e.Channel, e.Timestamp, e.Message);
        }

        /// <summary>Closes this session with the intel reporting
        /// server.</summary>
        public void Dispose() {
            Contract.Ensures(this.IsConnected == false);
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Closes this session with the intel reporting
        /// server.</summary>
        /// <param name="disposing"><see langword="true" /> to release both
        /// managed and unmanaged resources; <see langword="false" /> to
        /// release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing) {
            Contract.Ensures(!this.IsConnected);
            if (disposing) {
                if (!this.IsConnected)
                    return;
                try {
                    var request = WebRequest.Create(this.serviceUri);
                    var response = request.Post(new Dictionary<string, string>() {
                    { "username", this.username },
                    { "session", this.session },
                    { "action", "LOGOFF" },
                });
                    // ReadContent() handles tracing the response
                    var responseBody = response.ReadContent();
                } catch (WebException) {
                    // We don't actually care...
                } finally {
                    this.OnClosed();
                }
            } else {
                this.IsConnected = false;
            }
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this
        /// instance of <see cref="IntelSession" />.
        /// </summary>
        /// <returns>
        /// A <see cref="System.String" /> that represents this instance
        /// of <see cref="IntelSession" />.
        /// </returns>
        public override string ToString() {
            return String.Format(
                CultureInfo.CurrentCulture,
                this.IsConnected
                    ? Properties.Resources.IntelSession_Connected
                    : Properties.Resources.IntelSession_Disposed,
                this.GetType().Name,
                this.Users,
                this.ReportsSent);
        }

        /// <summary>Signals that an error has occured contacting the
        /// server.</summary>
        private void OnError() {
            if (++this.serverErrors == maxServerErrors) {
                this.OnClosed();
            }
        }

        /// <summary>
        /// Raises the <see cref="Closed" /> event when the session is
        /// closed.
        /// </summary>
        private void OnClosed() {
            Contract.Ensures(!this.IsConnected);
            this.IsConnected = false;
            this.Users = 0;

            var handler = this.Closed;
            this.Closed = null;
            if (handler != null) {
                handler(this, EventArgs.Empty);
            }
        }

        /// <summary>Invariant method for Code Contracts.</summary>
        [ContractInvariantMethod]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        private void ObjectInvariant() {
            Contract.Invariant(this.Users >= 0);
            Contract.Invariant(this.ReportsSent >= 0);
            Contract.Invariant(!this.IsConnected || (this.serverErrors < maxServerErrors));
        }

        /// <summary>
        /// Hashes a user's AUTH password in the manner required by
        /// authentication with the intel map reporting server.
        /// </summary>
        /// <param name="password">The plain text password to be hashed.</param>
        /// <returns>The hashed representation of <paramref name="password" />.</returns>
        [Pure]
        public static string HashPassword(string password) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(password));
            Contract.Ensures(Contract.Result<String>() != null);
            Contract.Ensures(Contract.Result<String>().Length == 40);
            using (var sha = SHA1.Create()) {
                return sha.ComputeHash(Encoding.UTF8.GetBytes(password)).ToLowerHexString();
            }
        }
    }
}
