using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Series of helper methods to assist with the construction of
    ///     classes within PleaseIgnore.IntelMap.
    /// </summary>
    /// <threadsafety static="true" instance="false"/>
    internal static class IntelExtensions {
        // The Unix time epoc
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // The 'unreserved' characters from RFC 3986
        private const string Unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~";
        // Convert a number 0...15 to hex
        private const string HexUpperString = "0123456789ABCDEF";
        private const string HexLowerString = "0123456789abcdef";
        // Category for Network Tracing
        public const string WebTraceCategory = "PleaseIgnore.IntelMap";
        // Downtime in ticks from beginning of day
        private const Int64 DowntimeTicks = 11L * TimeSpan.TicksPerHour;
        // Priority list of IntelChannelStatus
        private static readonly IntelStatus[] StatusPriority = new IntelStatus[] {
            IntelStatus.FatalError,
            IntelStatus.AuthenticationError,
            IntelStatus.NetworkError,
            IntelStatus.InvalidPath,
            IntelStatus.Active,
            IntelStatus.Waiting
        };

        // The base URL for requests on the intel map server
        public readonly static Uri BaseUrl = new Uri("http://map.pleaseignore.com/");
        // The URL for quering the channel list
        public readonly static Uri ChannelsUrl = new Uri(BaseUrl, "intelchannels.pl");
        // The URL for reporting intel
        public readonly static Uri ReportUrl = new Uri(BaseUrl, "report.pl");

        /// <summary>
        ///     Gets the time and date of the most recent scheduled Tranquility
        ///     downtime.
        /// </summary>
        public static DateTime LastDowntime {
            get {
                var nowTicks = DateTime.UtcNow.Ticks;
                var eveTicks = nowTicks - DowntimeTicks;
                return new DateTime(
                    eveTicks - eveTicks % TimeSpan.TicksPerDay + DowntimeTicks,
                    DateTimeKind.Utc);
            }
        }

        /// <summary>
        ///     Gets the time and date of the next scheduled Tranquility
        ///     downtime.
        /// </summary>
        public static DateTime NextDowntime {
            get {
                return LastDowntime + new TimeSpan(TimeSpan.TicksPerDay);
            }
        }

        /// <summary>
        ///     Runs an action against each member of a collection.
        /// </summary>
        /// <typeparam name="T">
        ///     The type of the elements of <paramref name="source"/>.
        /// </typeparam>
        /// <param name="source">
        ///     An <see cref="IEnumerable{T}"/> to process.
        /// </param>
        /// <param name="action">
        ///     A function to execute on each member of <paramref name="source"/>.
        /// </param>
        /// <returns>
        ///     The collection <paramref name="source"/> after <paramref name="action"/>
        ///     has been executed on each member.
        /// </returns>
        public static IEnumerable<T> ForEach<T>(this IEnumerable<T> source, Action<T> action) {
            Contract.Requires<ArgumentNullException>(source != null, "source");
            Contract.Requires<ArgumentNullException>(action != null, "action");
            foreach (var item in source) {
                action(item);
            }
            return source;
        }

        /// <summary>
        ///     Converts the value of the current <see cref="DateTime"/>
        ///     object to a standard Unix timestamp.
        /// </summary>
        /// <param name="timestamp">
        ///     The <see cref="DateTime"/> object to convert
        /// </param>
        /// <returns>
        ///     The representation of <paramref name="timestamp"/> as a Unix
        ///     timestamp, specifically the number of seconds elapsed since
        ///     midnight, 1 Jan 1970 GMT.
        /// </returns>
        [Pure]
        public static double ToUnixTime(this DateTime timestamp) {
            Contract.Ensures(!double.IsInfinity(Contract.Result<double>()));
            Contract.Ensures(!double.IsNaN(Contract.Result<double>()));
            return Math.Floor((timestamp.ToUniversalTime() - Epoch).TotalSeconds);
        }

        /// <summary>
        ///     Picks the highest priority status out of an array of
        ///     <see cref="IntelStatus"/>.
        /// </summary>
        /// <param name="array">
        ///     An array of <see cref="IntelStatus"/> values.
        /// </param>
        /// <returns>
        ///     The highest priority status from <paramref name="array"/>.
        /// </returns>
        [Pure]
        public static IntelStatus Combine(params IntelStatus[] array) {
            Contract.Requires(array != null);
            Contract.Requires(array.Length > 0);
            foreach (var status in StatusPriority) {
                if (array.Any(x => x == status)) {
                    return status;
                }
            }
            return array[0];
        }

        /// <summary>
        ///     Tests if a value of the <see cref="IntelStatus"/>
        ///     enumeration refers to a "running" state.
        /// </summary>
        /// <param name="status">
        ///     Value of <see cref="IntelStatus"/> to test.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if the value of <paramref name="status"/>
        ///     refers to a normal operating state; otherwise, 
        ///     <see langword="false"/> if it's in a stopped state.
        /// </returns>
        [Pure]
        public static bool IsRunning(this IntelStatus status) {
            switch (status) {
            case IntelStatus.Active:
            case IntelStatus.InvalidPath:
            case IntelStatus.Starting:
            case IntelStatus.Waiting:
            case IntelStatus.NetworkError:
            case IntelStatus.AuthenticationError:
                return true;
            default:
                return false;
            }
        }

        /// <summary>
        ///     Tests if a value of the <see cref="IntelStatus"/>
        ///     enumeration refers to an "error" state.
        /// </summary>
        /// <param name="status">
        ///     Value of <see cref="IntelStatus"/> to test.
        /// </param>
        /// <returns>
        ///     <see langword="true"/> if the value of <paramref name="status"/>
        ///     refers to an error state; otherwise, 
        ///     <see langword="false"/> if it's in a stopped state.
        /// </returns>
        [Pure]
        public static bool IsError(this IntelStatus status) {
            switch (status) {
            case IntelStatus.NetworkError:
            case IntelStatus.AuthenticationError:
            case IntelStatus.FatalError:
            case IntelStatus.InvalidPath:
                return true;
            default:
                return false;
            }
        }

        /// <summary>
        ///     Submits a standard POST to an HTTP(S) server.
        /// </summary>
        /// <param name="webRequest">
        ///     The instance of <see cref="WebRequest"/> to use when submitting
        ///     the HTTP POST.
        /// </param>
        /// <param name="payload">
        ///     The "application/x-www-form-urlencoded" encoded payload to
        ///     send as the POST content.
        /// </param>
        /// <returns>
        ///     The instance of <see cref="WebResponse"/> providing the server's
        ///     response to the POST.
        /// </returns>
        public static WebResponse Post(this WebRequest webRequest, byte[] payload) {
            Contract.Requires<ArgumentNullException>(webRequest != null, "webRequest");
            Contract.Requires<ArgumentNullException>(payload != null, "payload");
            Contract.Ensures(Contract.Result<WebResponse>() != null);
            return Post(webRequest, payload, 0, payload.Length);
        }

        /// <summary>
        ///     Submits a standard POST to an HTTP(S) server.
        /// </summary>
        /// <param name="webRequest">
        ///     The instance of <see cref="WebRequest"/> to use when submitting
        ///     the HTTP POST.
        /// </param>
        /// <param name="payload">
        ///     The "application/x-www-form-urlencoded" encoded payload to
        ///     send as the POST content.
        /// </param>
        /// <param name="offset">
        ///     The zero-based byte offset in <paramref name="payload"/> at
        ///     which to begin copying bytes to the server. 
        /// </param>
        /// <param name="count">
        ///     The number of bytes to be sent to the server.
        /// </param>
        /// <returns>
        ///     The instance of <see cref="WebResponse"/> providing the server's
        ///     response to the POST.
        /// </returns>
        public static WebResponse Post(this WebRequest webRequest, byte[] payload, int offset, int count) {
            Contract.Requires<ArgumentNullException>(webRequest != null, "webRequest");
            Contract.Requires<ArgumentNullException>(payload != null, "payload");
            Contract.Requires<ArgumentOutOfRangeException>(offset >= 0, "offset");
            Contract.Requires<ArgumentOutOfRangeException>(count >= 0, "count");
            Contract.Requires<ArgumentException>(offset + count <= payload.Length);
            Contract.Ensures(Contract.Result<WebResponse>() != null);

            try {
                Trace.WriteLine("<< " + Encoding.UTF8.GetString(payload, offset, count),
                    WebTraceCategory);

                webRequest.Method = "POST";
                webRequest.ContentLength = count;
                webRequest.ContentType = "application/x-www-form-urlencoded";

                using (var stream = webRequest.GetRequestStream()) {
                    stream.Write(payload, offset, count);
                }

                return webRequest.GetResponse();
            } catch (Exception e) {
                Trace.WriteLine("!! " + e.Message, WebTraceCategory);
                throw;
            }
        }

        /// <summary>
        ///     Submits a standard POST to an HTTP(S) server after encoding
        ///     the name-value pairs.
        /// </summary>
        /// <param name="webRequest">
        ///     The instance of <see cref="WebRequest"/> to use when submitting
        ///     the HTTP POST.
        /// </param>
        /// <param name="variables">
        ///     A list of name-value pairs to send to the server.
        /// </param>
        /// <returns>
        ///     The instance of <see cref="WebResponse"/> providing the server's
        ///     response to the POST.
        /// </returns>
        /// <remarks>
        ///     The name-value pairs provided by <paramref name="variables"/>
        ///     will be encoded as per the method described in the HTML
        ///     specification (part 17.13.4) after being converted to
        ///     strings by calling <see cref="Object.ToString()"/>.
        /// </remarks>
        public static WebResponse Post(this WebRequest webRequest,
                IEnumerable<KeyValuePair<string, string>> variables) {
            Contract.Requires<ArgumentNullException>(webRequest != null, "webRequest");
            Contract.Requires<ArgumentNullException>(variables != null, "variables");
            Contract.Requires<ArgumentException>(Contract.ForAll(variables,
                x => !String.IsNullOrEmpty(x.Key)));
            Contract.Ensures(Contract.Result<WebResponse>() != null);

            // Compute an upper bounds on the payload length
            var maxLength = variables.Sum(x => 2 + 9 * x.Key.Length
                + 9 * (x.Value ?? String.Empty).Length);
            // Build up the POST payload
            using (var stream = new MemoryStream(maxLength)) {
                bool first = true;
                foreach (var keypair in variables) {
                    // Write a '&' between each variable
                    if (!first) {
                        stream.WriteByte((byte)'&');
                    } else {
                        first = false;
                    }
                    // Write the variable name
                    WriteUriEncoded(stream, keypair.Key);
                    // Write the '=' between the name and value
                    stream.WriteByte((byte)'=');
                    // Write the variable value
                    WriteUriEncoded(stream, keypair.Value);
                }

                return Post(webRequest, stream.ToArray());
            }
        }

        /// <summary>
        ///     Reads the entirety of the response body of a
        ///     <see cref="WebResponse"/> and then disposes the instances.
        /// </summary>
        /// <param name="webResponse">
        ///     The instance of <see cref="WebResponse"/> to read out.
        /// </param>
        /// <returns>
        ///     The response payload parsed as a string.
        /// </returns>
        public static string ReadContent(this WebResponse webResponse) {
            Contract.Requires<ArgumentNullException>(webResponse != null, "webResponse");
            Contract.Ensures(Contract.Result<string>() != null);
            try {
                using (var stream = webResponse.GetResponseStream()) {
                    using (var reader = new StreamReader(stream)) {
                        var responseData = reader.ReadToEnd();
                        Trace.WriteLine(">> " + responseData, WebTraceCategory);
                        return responseData;
                    }
                }
            } catch (Exception e) {
                Trace.WriteLine("!! " + e.Message, WebTraceCategory);
                throw;
            } finally {
                webResponse.Close();
            }
        }

        /// <summary>
        ///     Writes the HTML form url encoded form a string to a
        ///     <see cref="Stream"/>.
        /// </summary>
        /// <param name="stream">
        ///     The instance of <see cref="Stream"/> to write the data
        ///     string to.
        /// </param>
        /// <param name="dataString">
        ///     The <see cref="String"/> to be encoded.
        /// </param>
        public static void WriteUriEncoded(this Stream stream, string dataString) {
            Contract.Requires<ArgumentNullException>(stream != null, "stream");
            if (!String.IsNullOrEmpty(dataString)) {
                var bytes = Encoding.UTF8.GetBytes(dataString);
                foreach (var current in bytes) {
                    if (current == ' ') {
                        // Escape space as '+'
                        stream.WriteByte((byte)'+');
                    } else if (Unreserved.Contains((char)current)) {
                        // Characters that should not be escaped
                        stream.WriteByte(current);
                    } else {
                        // Everything else
                        stream.WriteByte((byte)'%');
                        stream.WriteByte((byte)HexUpperString[current / 16]);
                        stream.WriteByte((byte)HexUpperString[current % 16]);
                    }
                }
            }
        }

        /// <summary>
        ///     Converts a byte array into a hex string using lower-case
        ///     characters for A-F.
        /// </summary>
        /// <param name="array">
        ///     Byte array to convert to a hex string.
        /// </param>
        /// <returns>
        ///     The hex string representation of <paramref name="array"/>.
        /// </returns>
        [Pure]
        public static string ToLowerHexString(this byte[] array) {
            Contract.Requires<ArgumentNullException>(array != null, "array");
            Contract.Ensures(Contract.Result<string>() != null);
            Contract.Ensures(Contract.Result<string>().Length == array.Length * 2);

            if (array.Length == 0) {
                return String.Empty;
            } else {
                StringBuilder builder = new StringBuilder(array.Length * 2);
                foreach (var current in array) {
                    builder.Append(HexLowerString[current / 16]);
                    builder.Append(HexLowerString[current % 16]);
                }
                return builder.ToString();
            }
        }

        /// <summary>
        ///     Parse an integer found in a Regular Expression match.
        /// </summary>
        /// <param name="capture">
        ///     Regular expression <see cref="Capture"/> to parse.
        /// </param>
        /// <returns>
        ///     The integer represented by the string matched by
        ///     <paramref name="capture"/>.
        /// </returns>
        /// <remarks>
        ///     <see cref="ToInt32"/> decodes <paramref name="capture"/>
        ///     according to the invariant culture.
        /// </remarks>
        [Pure]
        public static int ToInt32(this Capture capture) {
            Contract.Requires<ArgumentNullException>(capture != null, "capture");
            return int.Parse(capture.Value, CultureInfo.InvariantCulture);
        }
    }
}
