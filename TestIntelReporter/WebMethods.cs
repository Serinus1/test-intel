using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace TestIntelReporter {
    /// <summary>
    ///     Basic static methods to interface with the API on map.pleaseignore.com
    /// </summary>
    static class WebMethods {
#if DEBUG
        private const string BaseUri = "http://obsidian/inteltest.php";
#else
        private const string BaseUri = "http://map.pleaseignore.com";
#endif
        private const string ReportUri = BaseUri + "/report.pl";
        private const string ChannelsUri = BaseUri + "/intelchannels.pl";
        private const string Version = "2.2.0";
        private const string MethodPost = "POST";
        private const string ContentType = "application/x-www-form-urlencoded";

        private static readonly char[] Whitespace = new char[] { ' ' };
        private static readonly char[] FieldSeparators = new char[] { ',' };
        private static readonly char[] Newlines = new char[] { '\n' };
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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
            try {
                // TODO: Escapes ' ' as "%20" instead of '+'.   Don't know how the server
                // will interpret that.
                var requestText = String.Join("&",
                    parameters
                        .Select(x => x.Key + '=' + Uri.EscapeDataString(x.Value).Replace("%20", "+")));
                Trace.Write("<<" + requestText + '\n');
                var requestBody = Encoding.UTF8.GetBytes(requestText);

                var request = WebRequest.Create(ReportUri);
                request.Method = MethodPost;
                request.ContentType = ContentType;
                request.ContentLength = requestBody.Length;

                using (var stream = request.GetRequestStream()) {
                    stream.Write(requestBody, 0, requestBody.Length);
                }

                using (var response = request.GetResponse()) {
                    using (var reader = new StreamReader(response.GetResponseStream())) {
                        var responseBody = reader.ReadToEnd();
                        Trace.Write(">>" + responseBody + '\n');
                        return responseBody;
                    }
                }
            } catch(WebException e) {
                throw new IntelException(e.Message, e);
            }
        }

        /// <summary>
        ///     Returns the list of chat channels the client should observe.
        /// </summary>
        /// <returns>
        ///     Array of channel names.
        /// </returns>
        public static string[] GetChannelList() {
            try {
                var request = WebRequest.Create(ChannelsUri);
                using (var response = request.GetResponse()) {
                    using (var reader = new StreamReader(response.GetResponseStream())) {
                        return reader
                            .ReadToEnd()
                            .Split(Newlines, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => x.Split(FieldSeparators, 2).First())
                            .ToArray();
                    }
                }
            } catch (WebException e) {
                throw new IntelException(e.Message, e);
            }
        }

        /// <summary>
        ///     Opens a new session with map.pleaseignore.com for reporting intel.
        /// </summary>
        /// <param name="username">
        ///     The TEST member's auth account username.
        /// </param>
        /// <param name="passwordHash">
        ///     The SHA1 hash of the TEST member's services password.
        /// </param>
        /// <returns>
        ///     A tuple providing the session id in the first position and the current
        ///     number of users reporting intel in the second.
        /// </returns>
        public static Tuple<string, int> Authenticate(string username, string passwordHash) {
            var payload = SendRequest(new Dictionary<string, string>() {
                { "username", username },
                { "password", passwordHash },
                { "action", "AUTH" },
                { "version", Version }
            });
            var decode = payload.Split(Whitespace, 4);

            if (decode[0] != "200") {
                throw new IntelAuthorizationException(payload);
            }

            try {
                return new Tuple<string, int>(decode[2], int.Parse(decode[3]));
            } catch (FormatException e) {
                throw new IntelException(e.Message, e);
            }
        }

        /// <summary>
        ///     Terminates the session previously openned by
        ///     <see cref="Authenticate"/>,
        /// </summary>
        /// <param name="username">
        ///     The TEST member's auth account username.
        /// </param>
        /// <param name="session">
        ///     The session identifier returned from <see cref="Authenticate"/>.
        /// </param>
        public static void Logoff(string username, string session) {
            var payload = SendRequest(new Dictionary<string, string>() {
                { "username", username },
                { "session", session },
                { "action", "LOGOFF" },
            });

            var decode = payload.Split(Whitespace, 3);
            if (decode[0] != "201") {
                throw new IntelSessionException(payload);
            }
        }

        /// <summary>
        ///     Sends a keep alive to prevent the session from expiring.  The
        ///     perl client sends this once per minute.
        /// </summary>
        /// <param name="session">
        ///     The session identifier returned from <see cref="Authenticate"/>.
        /// </param>
        /// <returns>
        ///     The number of users currently reporting intel.
        /// </returns>
        public static int KeepAlive(string session) {
            var payload = SendRequest(new Dictionary<string, string>() {
                { "session", session },
                { "action", "ALIVE" },
            });

            var decode = payload.Split(Whitespace, 4);
            if (decode[0] != "203") {
                throw new IntelSessionException(payload);
            }

            try {
                return int.Parse(decode[3]);
            } catch (IntelException e) {
                throw new IntelException(e.Message, e);
            }
        }

        /// <summary>
        ///     Reports intelligence to the server.
        /// </summary>
        /// <param name="session">
        ///     The session identifier returned from <see cref="Authenticate"/>.
        /// </param>
        /// <param name="intelTime">
        ///     The date and time parsed from the chat log entry.
        /// </param>
        /// <param name="region">
        ///     The chat log this intel was read from.
        /// </param>
        /// <param name="intel">
        ///     The item to report to the server.
        /// </param>
        public static void Report(string session, DateTime intelTime, string region, string intel) {
            var reportTime = Math.Floor((intelTime - Epoch).TotalSeconds);
            var payload = SendRequest(new Dictionary<string, string>() {
                { "session", session },
                { "inteltime", reportTime.ToString("F0") },
                { "action", "INTEL" },
                { "region", region },
                // XXX: The \r is to make our report match the perl version EXACTLY
                { "intel", intel + '\r' }
            });

            var decode = payload.Split(Whitespace, 3);
            if (decode[0] != "202") {
                throw new IntelSessionException(payload);
            }
        }
    }
}
