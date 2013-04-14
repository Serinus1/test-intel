using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Security.Authentication;
using System.Text.RegularExpressions;
using System.Globalization;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Provides low-level access to the reporting features of the Test
    ///     Alliance Intel Map.
    /// </summary>
    public sealed class IntelSession : IDisposable {
        // The Unix time epoc
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // The base URL for requests on the intel map server
        private static readonly Uri BaseUrl = new Uri("http://map.pleaseignore.com/");
        // The URL for quering the channel list
        private static readonly Uri ChannelsUrl = new Uri(BaseUrl, "intelchannels.pl");
        // The URL for reporting intel
        private static readonly Uri ReportUrl = new Uri(BaseUrl, "report.pl");

        // Field separators for the channel list
        private static readonly char[] ChannelSeparators = new char[] { ',' };
        // Field separators for the server response
        private static readonly char[] FieldSeparators = new char[] { ' ' };

        // API response parsers
        private static readonly Regex ErrorResponse  = new Regex(@"^(50?) ERROR (.*)");
        private static readonly Regex AuthResponse   = new Regex(@"^200 AUTH ([\s]+) (\d+)");
        private static readonly Regex LogOffResponse = new Regex(@"^201 AUTH .*");
        private static readonly Regex IntelResponse  = new Regex(@"^202 INTEL .*");
        private static readonly Regex AliveResponse  = new Regex(@"^203 ALIVE OK (\d+)");

        // The username for this login (required when logging out)
        private readonly string username;
        // The session id
        private readonly string session;
        // Set to true once we are disposed
        private bool disposed;

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
        public IntelSession(string username, string passwordHash) {
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(username));
            Contract.Requires<ArgumentException>(!String.IsNullOrEmpty(passwordHash));

            var response = SendRequest(new Dictionary<string, string>() {
                { "username", username },
                { "password", passwordHash },
                { "action", "AUTH" },
                { "version", "2.2.0" }
            });

            Match match;
            if ((match = AuthResponse.Match(response)).Success) {
                // Successfully authenticated
                this.username = username;
                this.session  = match.Groups[3].Value;
                Contract.Assume(match.Groups[4].Value.Length > 0);
                Contract.Assume(Contract.ForAll(match.Groups[4].Value, x => Char.IsDigit(x)));
                this.Users = int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);
            } else if ((match = ErrorResponse.Match(response)).Success) {
                // Authentication failed
                throw new AuthenticationException(match.Groups[2].Value);
            } else {
                // The server responded with something unexpected
                throw new IntelException();
            }
        }

        /// <summary>
        ///     Gets a flag indicating whether our session with the intel
        ///     reporting server is still valid.
        /// </summary>
        public bool IsConnected { get { return !this.disposed; } }

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
        public EventHandler Closed;

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
            if (disposed)
                return false;

            var response = SendRequest(new Dictionary<string, string>() {
                { "session", this.session },
                { "action", "ALIVE" },
            });

            Match match;
            if ((match = AliveResponse.Match(response)).Success) {
                // Successful ping of the server
                this.Users = int.Parse(match.Groups[1].Value);
                return true;
            } else if ((match = ErrorResponse.Match(response)).Success) {
                if (match.Groups[1].Value == "502") {
                    // Our session has expired
                    this.OnClosed();
                    return false;
                } else {
                    // The server responded with something unexpected
                    throw new IntelException();
                }
            } else {
                // The server responded with something unexpected
                throw new IntelException();
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
            if (disposed)
                return false;

            var response = SendRequest(new Dictionary<string, string>() {
                { "session", session },
                { "inteltime", ToUnixTime(timestamp).ToString("F0") },
                { "action", "INTEL" },
                { "region", channel },
                // XXX: The \r is to make our report match the perl version EXACTLY
                { "intel", message + '\r' }
            });

            Match match;
            if ((match = IntelResponse.Match(response)).Success) {
                // Successful ping of the server
                ++this.ReportsSent;
                return true;
            } else if ((match = ErrorResponse.Match(response)).Success) {
                if (match.Groups[1].Value == "502") {
                    // Our session has expired
                    this.OnClosed();
                    return false;
                } else {
                    // The server responded with something unexpected
                    throw new IntelException();
                }
            } else {
                // The server responded with something unexpected
                throw new IntelException();
            }
        }

        /// <summary>
        ///     Closes this session with the intel reporting server.
        /// </summary>
        public void Dispose() {
            Contract.Ensures(this.IsConnected == false);
            if (disposed)
                return;

            try {
                SendRequest(new Dictionary<string, string>() {
                    { "username", this.username },
                    { "session", this.session },
                    { "action", "LOGOFF" },
                });
            } catch (WebException) {
                // We don't actually care...
            } finally {
                this.OnClosed();
            }
        }

        /// <inheritdoc/>
        public override string ToString() {
            return String.Format(
                this.disposed
                    ? Properties.Resources.IntelSession_Disposed
                    : Properties.Resources.IntelSession_Connected,
                this.GetType().Name,
                this.Users,
                this.ReportsSent);
        }

        /// <summary>
        ///     Raises the <see cref="Closed"/> event when the session is
        ///     closed.
        /// </summary>
        private void OnClosed() {
            Contract.Ensures(this.IsConnected == false);
            this.disposed = true;
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
        }

        /// <summary>
        ///     Gets the current list of intel channels from the intel
        ///     map reporting server.
        /// </summary>
        /// <returns>
        ///     An <see cref="Array"/> of <see cref="String"/> identifying
        ///     the desired intel channels for monitoring.
        /// </returns>
        /// <exception cref="WebException">
        ///     Could not access the intel server.
        /// </exception>
        public static string[] GetIntelChannels() {
            // TODO: Throw an exception if the response from the server is "wrong"
            Contract.Ensures(Contract.Result<string[]>() != null);

            var request = WebRequest.Create(ChannelsUrl);
            using (var response = request.GetResponse()) {
                using (var reader = new StreamReader(response.GetResponseStream())) {
                    string line;
                    var list = new List<string>();

                    while ((line = reader.ReadLine()) != null) {
                        var fields = line.Split(ChannelSeparators, 2);
                        if (!string.IsNullOrEmpty(fields[0])) {
                            list.Add(fields[0]);
                        }
                    }

                    return list.ToArray();
                }
            }
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

            using (var sha = SHA1.Create()) {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(password));
                return BitConverter.ToString(hash).ToLowerInvariant().Replace("-", "");
            }
        }

        /// <summary>
        ///     Internal function to combine the POST variables and execute them on
        ///     the server.
        /// </summary>
        /// <param name="parameters">
        ///     Dictionary of post parameters.  The value will be URL encoded and each
        ///     key/value pair will be joined with '&amp;'.
        /// </param>
        /// <returns>
        ///     The content body returned from the server.
        /// </returns>
        private static string SendRequest(IDictionary<string, string> parameters) {
            Contract.Requires<ArgumentNullException>(parameters != null);
            Contract.Requires<ArgumentException>(Contract.ForAll(parameters, x => !String.IsNullOrEmpty(x.Key)));
            Contract.Requires<ArgumentException>(Contract.ForAll(parameters, x => !String.IsNullOrEmpty(x.Value)));
            Contract.Ensures(Contract.Result<string>() != null);

            try {
                var requestText = String.Join("&",
                    parameters.Select(x => x.Key + '=' + UrlEscape(x.Value)));
                Trace.WriteLine("<< " + requestText, typeof(IntelSession).FullName);
                var requestBody = Encoding.UTF8.GetBytes(requestText);

                var request = WebRequest.Create(ReportUrl);
                request.Method = "POST";
                request.ContentType = "application/x-www-form-urlencoded";
                request.ContentLength = requestBody.Length;

                using (var stream = request.GetRequestStream()) {
                    stream.Write(requestBody, 0, requestBody.Length);
                }

                using (var response = request.GetResponse()) {
                    using (var reader = new StreamReader(response.GetResponseStream())) {
                        var responseBody = reader.ReadLine();
                        Trace.WriteLine(">> " + responseBody, typeof(IntelSession).FullName);
                        return responseBody;
                    }
                }
            } catch (WebException e) {
                Trace.WriteLine("!! " + e.Message, typeof(IntelSession).FullName);
                throw;
            }
        }

        /// <summary>
        ///     Variant of <see cref="Uri.EscapeDataString"/> that converts
        ///     spaces into '+' instead of '%20'.
        /// </summary>
        [Pure]
        private static string UrlEscape(string stringToEscape) {
            Contract.Requires(stringToEscape != null);
            return Uri.EscapeDataString(stringToEscape).Replace("%20", "+");
        }

        /// <summary>
        ///     Converts a <see cref="DateTime"/> into "unix time" (seconds
        ///     elapsed since midnight UTC 1970-1-1, ignoring leap seconds).
        /// </summary>
        [Pure]
        private static double ToUnixTime(DateTime timestamp) {
            return Math.Floor((timestamp - Epoch).TotalSeconds);
        }
    }
}
