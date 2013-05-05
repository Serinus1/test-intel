using PleaseIgnore.IntelMap.Properties;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Provides low-level access to the reporting features of the Test
    ///     Alliance Intel Map.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    public sealed class IntelSession : IDisposable {
        // The Unix time epoc
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Field separators for the channel list
        private static readonly char[] ChannelSeparators = new char[] { ',' };

        // API response parsers
        private static readonly Regex ErrorResponse  = new Regex(@"^(50\d) ERROR (.*)");
        private static readonly Regex AuthResponse   = new Regex(@"^200 AUTH ([^\s]+) (\d+)");
        private static readonly Regex IntelResponse  = new Regex(@"^202 INTEL .*");
        private static readonly Regex AliveResponse  = new Regex(@"^203 ALIVE OK (\d+)");
        // Number of server errors before dropping the connection
        private const int maxServerErrors = 3;

        // The username for this login (required when logging out)
        private readonly string username;
        // The session id
        private readonly string session;
        // The service access Uri
        private readonly Uri serviceUri;
        // Number of consecutive server errors
        private int serverErrors;

        /// <summary>
        ///     Creates a new instance of the <see cref="IntelSession"/> class
        ///     and authenticates with the map server.
        /// </summary>
        /// <param name="username">
        ///     The user's AUTH name.
        /// </param>
        /// <param name="passwordHash">
        ///     An SHA1 hash of the user's password.
        /// </param>
        /// <exception cref="AuthenticationException">
        ///     The authentication failed.
        /// </exception>
        /// <exception cref="IntelException">
        ///     Unexpected response returned from the server.
        /// </exception>
        /// <exception cref="WebException">
        ///     Failed to contact the web server.
        /// </exception>
        /// <seealso cref="HashPassword"/>
        public IntelSession(string username, string passwordHash)
            : this(username, passwordHash, null) {
        }

        /// <summary>
        ///     Creates a new instance of the <see cref="IntelSession"/> class
        ///     and authenticates with the map server using a specified service
        ///     <see cref="Uri"/>.
        /// </summary>
        /// <param name="username">
        ///     The user's AUTH name.
        /// </param>
        /// <param name="passwordHash">
        ///     An SHA1 hash of the user's password.
        /// </param>
        /// <param name="serviceUri">
        ///     <see cref="Uri"/> to use when contacting the Intel Map reporting
        ///     service.
        /// </param>
        /// <remarks>
        ///     <see cref="IntelSession(string,string,Uri)"/> is primarily intended
        ///     for use with unit testing.  Normal users will make use of the
        ///     <see cref="IntelSession(string,string)"/> implementation.
        /// </remarks>
        /// <exception cref="AuthenticationException">
        ///     The authentication failed.
        /// </exception>
        /// <exception cref="IntelException">
        ///     Unexpected response returned from the server.
        /// </exception>
        /// <exception cref="WebException">
        ///     Failed to contact the web server.
        /// </exception>
        /// <exception cref="NotSupportedException">
        ///     <paramref name="serviceUri"/> uses a URI scheme not supported
        ///     by <see cref="WebRequest"/>.
        /// </exception>
        /// <seealso cref="HashPassword"/>
        public IntelSession(string username, string passwordHash, Uri serviceUri) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(passwordHash));
            Contract.Requires<ArgumentException>((serviceUri == null) || serviceUri.IsAbsoluteUri);

            this.username = username;
            this.serviceUri = serviceUri ?? IntelExtensions.ReportUrl;

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
        ///     Gets a flag indicating whether our session with the intel
        ///     reporting server is still valid.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        ///     Gets the number of users currently connected to the server.
        /// </summary>
        public int Users { get; private set; }

        /// <summary>
        ///     Gets the number of intel reports sent to the server.
        /// </summary>
        public int ReportsSent { get; private set; }

        /// <summary>
        ///     Occurs when this session with the server is closed, either
        ///     through a call to <see cref="Close"/> or timing out.
        /// </summary>
        public event EventHandler Closed;

        /// <summary>
        ///     Sends a keep-alive to the intel reporting server, preserving
        ///     our session.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> if our session is still valid;
        ///     otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="IntelException">
        ///     Unexpected response returned from the server.
        /// </exception>
        /// <exception cref="WebException">
        ///     Failed to contact the web server.
        /// </exception>
        public bool KeepAlive() {
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

        /// <summary>
        ///     Sends a log entry to the intel reporting server.
        /// </summary>
        /// <returns>
        ///     <see langword="true"/> if our session is still valid;
        ///     otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="IntelException">
        ///     Unexpected response returned from the server.
        /// </exception>
        /// <exception cref="WebException">
        ///     Failed to contact the web server.
        /// </exception>
        public bool Report(string channel, DateTime timestamp, string message) {
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

        /// <summary>
        ///     Sends a log entry to the intel reporting server.
        /// </summary>
        /// <param name="e">
        ///     An instance of <see cref="IntelEventArgs"/> containing the
        ///     information to report.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if our session is still valid;
        ///     otherwise, <see langword="false"/>.
        /// </returns>
        public bool Report(IntelEventArgs e) {
            Contract.Requires<ArgumentNullException>(e != null, "e");
            return this.Report(e.Channel, e.Timestamp, e.Message);
        }

        /// <summary>
        ///     Closes this session with the intel reporting server.
        /// </summary>
        public void Dispose() {
            Contract.Ensures(this.IsConnected == false);
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
        }

        /// <inheritdoc/>
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

        /// <summary>
        ///     Signals that an error has occured contacting the server.
        /// </summary>
        /// <remarks>
        ///     
        /// </remarks>
        private void OnError() {
            if (++this.serverErrors == maxServerErrors) {
                this.OnClosed();
            }
        }

        /// <summary>
        ///     Raises the <see cref="Closed"/> event when the session is
        ///     closed.
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

        [ContractInvariantMethod]
        private void ObjectInvariant() {
            Contract.Invariant(this.Users >= 0);
            Contract.Invariant(this.ReportsSent >= 0);
            Contract.Invariant(!this.IsConnected || (this.serverErrors < maxServerErrors));
        }

        /// <summary>
        ///     Hashes a user's AUTH password in the manner required by
        ///     authentication with the intel map reporting server.
        /// </summary>
        /// <param name="password">
        ///     The plain text password to be hashed.
        /// </param>
        /// <returns>
        ///     The hashed representation of <paramref name="password"/>.
        /// </returns>
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
