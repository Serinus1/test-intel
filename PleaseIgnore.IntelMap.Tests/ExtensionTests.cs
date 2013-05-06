using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PleaseIgnore.IntelMap.Tests {
    /// <summary>
    ///     Tests of the <see cref="IntelExtensions"/> members
    /// </summary>
    [TestClass]
    public class ExtensionTests {
        const string testString = "ABC αβγ";
        const string testStringEncoded = "ABC+%CE%B1%CE%B2%CE%B3";

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.LastDowntime"/> member.
        /// </summary>
        [TestMethod]
        public void LastDowntime() {
            var now = DateTime.UtcNow;
            var lastDowntime = IntelExtensions.LastDowntime;

            Assert.IsTrue(lastDowntime <= now);
            Assert.IsTrue(now < lastDowntime + new TimeSpan(24, 0, 0));
            Assert.AreEqual(new TimeSpan(11, 0, 0), lastDowntime - lastDowntime.Date);
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.NextDowntime"/> member.
        /// </summary>
        [TestMethod]
        public void NextDowntime() {
            var now = DateTime.UtcNow;
            var nextDowntime = IntelExtensions.NextDowntime;

            Assert.IsTrue(now < nextDowntime);
            Assert.IsTrue(nextDowntime <= now + new TimeSpan(24, 0, 0));
            Assert.AreEqual(new TimeSpan(11, 0, 0), nextDowntime - nextDowntime.Date);
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.ToUnixTime()"/> member.
        /// </summary>
        [TestMethod]
        public void ToUnixTime() {
            // Example from Wikipedia
            Assert.AreEqual(1095357343, new DateTime(2004, 9, 16, 17, 55, 43, DateTimeKind.Utc).ToUnixTime());
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.Combine()"/> member.
        /// </summary>
        [TestMethod]
        public void Combine() {
            Assert.AreEqual(IntelStatus.Active,
                IntelExtensions.Combine(
                    IntelStatus.Waiting,
                    IntelStatus.Active));
            Assert.AreEqual(IntelStatus.Active,
                IntelExtensions.Combine(
                    IntelStatus.Active,
                    IntelStatus.Waiting));
            Assert.AreEqual(IntelStatus.FatalError,
                IntelExtensions.Combine(
                    IntelStatus.FatalError,
                    IntelStatus.Active));
            Assert.AreEqual(IntelStatus.FatalError,
                IntelExtensions.Combine(
                    IntelStatus.Active,
                    IntelStatus.FatalError));
            Assert.AreEqual(IntelStatus.InvalidPath,
                IntelExtensions.Combine(
                    IntelStatus.InvalidPath,
                    IntelStatus.Active));
            Assert.AreEqual(IntelStatus.InvalidPath,
                IntelExtensions.Combine(
                    IntelStatus.Active,
                    IntelStatus.InvalidPath));
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.ToInt32()"/> member.
        /// </summary>
        [TestMethod]
        public void ToInt32() {
            var match = new Regex(@"(\d*)").Match("12345");
            Assert.AreEqual(12345, match.Groups[1].ToInt32());
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.ToLowerHexString()"/> member.
        /// </summary>
        [TestMethod]
        public void ToLowerHexString() {
            var bytes = new byte[] { 0x12, 0x34, 0x56, 0xFE, 0xDC, 0xBA };
            Assert.AreEqual("123456fedcba", bytes.ToLowerHexString());
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.WriteUriEncoded()"/> member.
        /// </summary>
        [TestMethod]
        public void WriteUriEncoded() {
            using (var stream = new MemoryStream()) {
                stream.WriteUriEncoded(testString);
                // Answer came from doing a Google search in Chrome
                Assert.AreEqual(testStringEncoded, Encoding.UTF8.GetString(stream.ToArray()));
            }
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.Post(WebRequest, byte[])"/> member.
        /// </summary>
        [TestMethod]
        public void Post() {
            var data = Encoding.UTF8.GetBytes(testString);

            // The stream should receive data and only data
            var mockStream = new Mock<Stream>(MockBehavior.Strict);
            mockStream.Setup(x => x.Write(data, 0, data.Length));
            mockStream.Setup(x => x.Close());

            // No calls should be made on WebResponse
            var mockResponse = new Mock<WebResponse>(MockBehavior.Strict);
            var response = mockResponse.Object;

            // The operation should set some properties, write the query, and give us the response
            var mockRequest = new Mock<WebRequest>(MockBehavior.Strict);
            mockRequest.SetupProperty(x => x.ContentLength);
            mockRequest.SetupProperty(x => x.ContentType);
            mockRequest.SetupProperty(x => x.Method);
            mockRequest.Setup(x => x.GetRequestStream())
                .Returns(mockStream.Object);
            mockRequest.Setup(x => x.GetResponse())
                .Returns(response);

            // Perform the actual operation
            var request = mockRequest.Object;
            Assert.AreEqual(response, request.Post(data));

            // Make sure the properties were set correctly
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual(data.Length, request.ContentLength);
            Assert.AreEqual("application/x-www-form-urlencoded", request.ContentType);

            // Make sure certain methods were called appropriately
            mockRequest.Verify(x => x.GetRequestStream(), Times.Once());
            mockStream.Verify(x => x.Write(data, 0, data.Length), Times.Once());
            mockStream.Verify(x => x.Close(), Times.Once());
            mockRequest.Verify(x => x.GetResponse(), Times.Once());
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.Post(WebRequest, IDictionary{string, string})"/> member.
        /// </summary>
        [TestMethod]
        public void PostVariables() {
            // For the request stream, we need to reconstruct the string written to the server
            StringBuilder builder = new StringBuilder();
            var byteCount = 0;
            var mockStream = new Mock<Stream>(MockBehavior.Strict);
            mockStream.Setup(x => x.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback((byte[] array, int offset, int count) => {
                    builder.Append(Encoding.UTF8.GetString(array, offset, count));
                    byteCount += count;
                });
            mockStream.Setup(x => x.WriteByte(It.IsAny<byte>()))
                .Callback((byte ch) => {
                    builder.Append((char)ch);
                    ++byteCount;
                });
            mockStream.Setup(x => x.Close());

            // No operations should be performed on the WebResponse
            var mockResponse = new Mock<WebResponse>(MockBehavior.Strict);
            var response = mockResponse.Object;

            // The operation should set some properties, write the query, and give us the response
            var mockRequest = new Mock<WebRequest>(MockBehavior.Strict);
            mockRequest.SetupProperty(x => x.ContentLength);
            mockRequest.SetupProperty(x => x.ContentType);
            mockRequest.SetupProperty(x => x.Method);
            mockRequest.Setup(x => x.GetRequestStream())
                .Returns(mockStream.Object);
            mockRequest.Setup(x => x.GetResponse())
                .Returns(response);

            // Perform the actual operation
            var request = mockRequest.Object;
            Assert.AreEqual(response, request.Post(new Dictionary<string, string> {
                { "Key 1", testString },
                { "Key 2", "DEF GHI" }
            }));

            // Make sure the properties were set correctly
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual(byteCount, request.ContentLength);
            Assert.AreEqual("application/x-www-form-urlencoded", request.ContentType);
            Assert.AreEqual("Key+1=" + testStringEncoded + "&Key+2=DEF+GHI", builder.ToString());

            // Make sure certain methods were called appropriately
            mockRequest.Verify(x => x.GetRequestStream(), Times.Once());
            mockStream.Verify(x => x.Close(), Times.Once());
            mockRequest.Verify(x => x.GetResponse(), Times.Once());
        }

        /// <summary>
        ///     Tests the <see cref="IntelExtensions.ReadContent"/> member.
        /// </summary>
        [TestMethod]
        public void ReadContent() {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(testString), false);

            var mockResponse = new Mock<WebResponse>(MockBehavior.Strict);
            mockResponse.Setup(x => x.GetResponseStream())
                .Returns(stream);
            mockResponse.Setup(x => x.Close());

            // Perform the actual operation
            var response = mockResponse.Object;
            Assert.AreEqual(testString, response.ReadContent());

            // Make sure certain methods were called appropriately
            mockResponse.Verify(x => x.GetResponseStream(), Times.Once());
            mockResponse.Verify(x => x.Close(), Times.Once());
        }
    }
}
