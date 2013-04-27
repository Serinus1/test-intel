using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.IO;
using System.Net;
using System.Security.Authentication;
using System.Text;

namespace PleaseIgnore.IntelMap.Tests {
    /// <summary>
    ///     Tests of the <see cref="IntelSession"/> class.
    /// </summary>
    [TestClass]
    public class IntelSessionTests {
        const string testPassword = "ABC αβγ";
        const string testPasswordHash = "a79e978f450304a9a7660803fb3aff7ec631f641";
        const string testScheme = "session-test";
        const string testUsername = "test-user";
        const string testSession = "01234567890abcdef";
        const string intelChannel = "random-intel";
        const string intelString = "this-is-some-hot-intel";
        // Combination tested in ExtensionTests.ToUnixTime()
        const string intelTimeString = "1095357343";
        private readonly static DateTime intelTimestamp = new DateTime(2004, 9, 16, 17, 55, 43, DateTimeKind.Utc);
        // For routing into our Mock WebRequest
        private readonly static Uri sessionUri = new Uri(testScheme + "://blah-blah-blah");

        /// <summary>
        ///     The currently assigned Mock for creating instances of
        ///     <see cref="WebRequest"/>.
        /// </summary>
        private static IWebRequestCreate requestProxy;

        /// <summary>
        ///     We use a session-test://whatever URI to connect to our mock
        /// </summary>
        private class WebRequestCreateProxy : IWebRequestCreate {
            public WebRequest Create(Uri uri) {
                return requestProxy.Create(uri);
            }
        }

        [ClassInitialize]
        public static void InitScheme(TestContext context) {
            WebRequest.RegisterPrefix(testScheme, new WebRequestCreateProxy());
        }

        [TestCleanup]
        public void TestCleanup() {
            requestProxy = null;
        }

        /// <summary>
        ///     Tests the <see cref="IntelSession.HashPassword"/> member.
        /// </summary>
        [TestMethod]
        public void HashPassword() {
            // Should be equivalent to the SHA1 has of the password
            Assert.AreEqual(testPasswordHash, IntelSession.HashPassword(testPassword));
        }

        /// <summary>
        ///     Creates an instance of <see cref="IntelSession"/> and logs
        ///     it in.
        /// </summary>
        /// <returns>
        ///     Initialized instance of <see cref="IntelSession"/>.
        /// </returns>
        private IntelSession Login() {
            var responseBody = Encoding.UTF8.GetBytes("200 AUTH " + testSession + " 5");
            var responseStream = new MemoryStream(responseBody, false);
            var responseMock = new Mock<WebResponse>();
            responseMock.Setup(x => x.GetResponseStream())
                .Returns(responseStream);
            var response = responseMock.Object;

            var requestBody = new StringBuilder();
            var requestStreamMock = new Mock<Stream>();
            requestStreamMock.Setup(x => x.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback((byte[] bytes, int index, int count)
                    => requestBody.Append(Encoding.UTF8.GetString(bytes, index, count)));
            var requestStream = requestStreamMock.Object;

            var requestMock = new Mock<WebRequest>();
            requestMock.Setup(x => x.GetRequestStream())
                .Returns(requestStream);
            requestMock.Setup(x => x.GetResponse())
                .Returns(response);
            var request = requestMock.Object;

            var proxyMock = new Mock<IWebRequestCreate>(MockBehavior.Strict);
            proxyMock.Setup(x => x.Create(sessionUri))
                .Returns(request);
            requestProxy = proxyMock.Object;

            var session = new IntelSession(testUsername, testPasswordHash, sessionUri);
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(5, session.Users);

            Assert.AreEqual(
                "username=" + testUsername + "&"
                    + "password=" + testPasswordHash
                    + "&action=AUTH&version=2.2.0",
                requestBody.ToString());
            proxyMock.Verify(x => x.Create(sessionUri), Times.Once());
            return session;
        }

        /// <summary>
        ///     Sets up a proxy of <see cref="IWebRequestCreate"/> that will
        ///     respond with a specific string as its payload.
        /// </summary>
        /// <returns>
        ///     Instance of <see cref="StringBuilder"/> that will receive the
        ///     request payload.
        /// </returns>
        private StringBuilder CreateRequestMock(string responseText) {
            var responseBody = Encoding.UTF8.GetBytes(responseText);
            var responseStream = new MemoryStream(responseBody, false);
            var responseMock = new Mock<WebResponse>();
            responseMock.Setup(x => x.GetResponseStream())
                .Returns(responseStream);
            var response = responseMock.Object;

            var requestBody = new StringBuilder();
            var requestStreamMock = new Mock<Stream>();
            requestStreamMock.Setup(x => x.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback((byte[] bytes, int index, int count)
                    => requestBody.Append(Encoding.UTF8.GetString(bytes, index, count)));
            var requestStream = requestStreamMock.Object;

            var requestMock = new Mock<WebRequest>();
            requestMock.Setup(x => x.GetRequestStream())
                .Returns(requestStream);
            requestMock.Setup(x => x.GetResponse())
                .Returns(response);
            var request = requestMock.Object;

            var proxyMock = new Mock<IWebRequestCreate>(MockBehavior.Strict);
            proxyMock.Setup(x => x.Create(sessionUri))
                .Returns(requestMock.Object);
            requestProxy = proxyMock.Object;

            return requestBody;
        }


        /// <summary>
        ///     Sets up a proxy of <see cref="IWebRequestCreate"/> that will
        ///     throw <see cref="WebException"/> when generating the response.
        /// </summary>
        /// <returns>
        ///     Instance of <see cref="StringBuilder"/> that will receive the
        ///     request payload.
        /// </returns>
        private StringBuilder CreateRequestError() {
            var requestBody = new StringBuilder();
            var requestStreamMock = new Mock<Stream>();
            requestStreamMock.Setup(x => x.Write(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>()))
                .Callback((byte[] bytes, int index, int count)
                    => requestBody.Append(Encoding.UTF8.GetString(bytes, index, count)));
            var requestStream = requestStreamMock.Object;

            var requestMock = new Mock<WebRequest>();
            requestMock.Setup(x => x.GetRequestStream())
                .Returns(requestStream);
            requestMock.Setup(x => x.GetResponse())
                .Throws<WebException>();
            var request = requestMock.Object;

            var proxyMock = new Mock<IWebRequestCreate>(MockBehavior.Strict);
            proxyMock.Setup(x => x.Create(sessionUri))
                .Returns(requestMock.Object);
            requestProxy = proxyMock.Object;

            return requestBody;
        }

        /// <summary>
        ///     Tests the <see cref="IntelSession.IntelSession"/> constructor
        ///     with a successful login.
        /// </summary>
        [TestMethod]
        public void LoginSuccess() {
            Login();
        }

        /// <summary>
        ///     Tests the <see cref="IntelSession.IntelSession"/> constructor
        ///     with a simulated network error.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(WebException))]
        public void LoginError() {
            var requestMock = new Mock<WebRequest>();
            requestMock.Setup(x => x.GetRequestStream())
                .Returns(new MemoryStream());
            requestMock.Setup(x => x.GetResponse())
                .Throws<WebException>();

            var proxyMock = new Mock<IWebRequestCreate>(MockBehavior.Strict);
            proxyMock.Setup(x => x.Create(sessionUri))
                .Returns(requestMock.Object);
            requestProxy = proxyMock.Object;

            var session = new IntelSession(testUsername, testPasswordHash, sessionUri);
        }

        /// <summary>
        ///     Tests the <see cref="IntelSession.IntelSession"/> constructor
        ///     with a rejected login.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(AuthenticationException))]
        public void LoginRejected() {
            CreateRequestMock("500 ERROR AUTH");
            var session = new IntelSession(testUsername, testPasswordHash, sessionUri);
        }

        /// <summary>
        ///     Tests that <see cref="IntelSession.Dispose"/> properly logs
        ///     us out.
        /// </summary>
        [TestMethod]
        public void Dispose() {
            var session = Login();

            var closedCount = 0;
            session.Closed += (sender, e) => ++closedCount;

            var requestBody = CreateRequestMock("201 AUTH Logged Off");
            session.Dispose();

            Assert.AreEqual(
                "username=" + testUsername
                    + "&session=" + testSession
                    + "&action=LOGOFF",
                requestBody.ToString());
            Assert.IsFalse(session.IsConnected);
            Assert.AreEqual(1, closedCount);
        }

        /// <summary>
        ///     Tests that <see cref="IntelSession.KeepAlive"/> properly pings
        ///     the server.
        /// </summary>
        [TestMethod]
        public void KeepAlive() {
            var session = Login();
            session.Closed += (sender, e) => Assert.Fail("IntelSession.Closed raised inappropriately");

            var requestBody = CreateRequestMock("203 ALIVE OK 15");
            session.KeepAlive();

            Assert.AreEqual("session=" + testSession + "&action=ALIVE", requestBody.ToString());
            Assert.AreEqual(15, session.Users);
            Assert.IsTrue(session.IsConnected);
        }

        /// <summary>
        ///     Tests that <see cref="IntelSession.KeepAlive"/> throws an error
        ///     for an invalid response.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(WebException))]
        public void KeepAliveInvalid() {
            var session = Login();
            session.Closed += (sender, e) => Assert.Fail("IntelSession.Closed raised inappropriately");

            try {
                var requestBody = CreateRequestMock("SOMETHING IS NOT RIGHT");
                session.KeepAlive();
            } catch {
                Assert.IsTrue(session.IsConnected);
                throw;
            }
        }

        /// <summary>
        ///     Tests that <see cref="IntelSession.KeepAlive"/> disconnects after
        ///     hitting the error limit.
        /// </summary>
        [TestMethod]
        public void KeepAliveErrorLimit() {
            var session = Login();

            var closedCount = 0;
            session.Closed += (sender, e) => ++closedCount;

            try {
                var requestBody = CreateRequestMock("SOMETHING IS NOT RIGHT");
                session.KeepAlive();
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = CreateRequestError();
                session.KeepAlive();
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = CreateRequestMock("SOMETHING IS NOT RIGHT");
                session.KeepAlive();
            } catch (WebException) {
            }

            Assert.IsFalse(session.IsConnected);
            Assert.AreEqual(1, closedCount);
        }

        /// <summary>
        ///     Tests that <see cref="IntelSession.Report"/> properly pings
        ///     the server.
        /// </summary>
        [TestMethod]
        public void Report() {
            var session = Login();
            session.Closed += (sender, e) => Assert.Fail("IntelSession.Closed raised inappropriately");

            var requestBody = CreateRequestMock("202 INTEL Accepted");
            session.Report(intelChannel, intelTimestamp, intelString);

            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(
                "session=" + testSession
                    + "&inteltime=" + intelTimeString
                    + "&action=INTEL"
                    + "&region=" + intelChannel
                    + "&intel=" + intelString + "%0D",
                requestBody.ToString());
        }


        /// <summary>
        ///     Tests that <see cref="IntelSession.Report"/> throws an error
        ///     for an invalid response.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(WebException))]
        public void ReportInvalid() {
            var session = Login();
            session.Closed += (sender, e) => Assert.Fail("IntelSession.Closed raised inappropriately");

            try {
                var requestBody = CreateRequestMock("SOMETHING IS NOT RIGHT");
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch {
                Assert.IsTrue(session.IsConnected);
                throw;
            }
        }

        /// <summary>
        ///     Tests that <see cref="IntelSession.Report"/> disconnects after
        ///     hitting the error limit.
        /// </summary>
        [TestMethod]
        public void ReportErrorLimit() {
            var session = Login();

            var closedCount = 0;
            session.Closed += (sender, e) => ++closedCount;

            try {
                var requestBody = CreateRequestMock("SOMETHING IS NOT RIGHT");
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = CreateRequestError();
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = CreateRequestMock("SOMETHING IS NOT RIGHT");
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch (WebException) {
            }
            Assert.IsFalse(session.IsConnected);
            Assert.AreEqual(1, closedCount);
        }
    }
}
