using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;

namespace PleaseIgnore.IntelMap {
    /// <summary>
    ///     Series of helper methods to assist with the construction of
    ///     classes within PleaseIgnore.IntelMap.
    /// </summary>
    internal static class IntelExtensions {
        // The Unix time epoc
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // The 'unreserved' characters from RFC 3986
        private const string Unreserved = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_.~";
        // Convert a number 0...15 to hex
        private const string HexString = "0123456789ABCDEF";
        // Category for Network Tracing
        private const string WebTraceCategory = "PleaseIgnore.IntelMap";

        /// <summary>
        ///     Converts the value of the current <see cref="DateTime"/>
        ///     object to a standard Unix timestamp.
        /// </summary>
        /// <param name="timestamp">
        ///     The <see cref="DateTime"/> object to convert
        /// </param>
        /// <returns>
        ///     The representation of <paramref name="DateTime"/> as a Unix
        ///     timestamp, specifically the number of seconds elapsed since
        ///     midnight, 1 Jan 1970 GMT.
        /// </returns>
        public static double ToUnixTime(this DateTime timestamp) {
            return Math.Floor((timestamp.ToUniversalTime() - Epoch).TotalSeconds);
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
        /// <param name="webRequest">
        ///     The instance of <see cref="WebResponse"/> to use when reading
        ///     the response payload.
        /// </param>
        /// <returns>
        ///     The response payload parsed as a string.
        /// </returns>
        public static string ReadContent(this WebResponse webResponse) {
            Contract.Requires<ArgumentNullException>(webResponse != null, "webResponse");
            try {
                using (var stream = webResponse.GetResponseStream()) {
                    using (var reader = new StreamReader(stream)) {
                        try {
                            var responseData = reader.ReadToEnd();
                            Trace.WriteLine(">> " + responseData, WebTraceCategory);
                            return responseData;
                        } catch (Exception e) {
                            Trace.WriteLine("!! " + e.Message, WebTraceCategory);
                            throw;
                        }
                    }
                }
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
                        stream.WriteByte((byte)HexString[current / 16]);
                        stream.WriteByte((byte)HexString[current % 16]);
                    }
                }
            }
        }
    }
}
