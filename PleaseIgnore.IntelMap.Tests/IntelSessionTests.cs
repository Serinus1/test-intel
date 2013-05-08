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
        const string testUsername = "test-user";
        const string testSession = "01234567890abcdef";
        const string intelChannel = "random-intel";
        const string intelString = "this-is-some-hot-intel";
        // Combination tested in ExtensionTests.ToUnixTime()
        const string intelTimeString = "1095357343";
        private readonly static DateTime intelTimestamp = new DateTime(2004, 9, 16, 17, 55, 43, DateTimeKind.Utc);
        // For routing into our Mock WebRequest
        private readonly static Uri sessionUri = new Uri(TestHelpers.TestScheme + "://blah-blah-blah");

        [TestCleanup]
        public void Cleanup() {
            TestHelpers.Cleanup();
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
            var requestBody = TestHelpers.CreateRequestMock(sessionUri, "200 AUTH " + testSession + " 5");

            var session = new IntelSession(testUsername, testPasswordHash, sessionUri);
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(5, session.Users);

            Assert.AreEqual(
                "username=" + testUsername + "&"
                    + "password=" + testPasswordHash
                    + "&action=AUTH&version=2.2.0",
                requestBody.ToString());
            return session;
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
            TestHelpers.CreateRequestError<WebException>(sessionUri);
            var session = new IntelSession(testUsername, testPasswordHash, sessionUri);
        }

        /// <summary>
        ///     Tests the <see cref="IntelSession.IntelSession"/> constructor
        ///     with a rejected login.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(AuthenticationException))]
        public void LoginRejected() {
            TestHelpers.CreateRequestMock(sessionUri, "500 ERROR AUTH");
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

            var requestBody = TestHelpers.CreateRequestMock(sessionUri, "201 AUTH Logged Off");
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

            var requestBody = TestHelpers.CreateRequestMock(sessionUri, "203 ALIVE OK 15");
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
                var requestBody = TestHelpers.CreateRequestMock(sessionUri, "SOMETHING IS NOT RIGHT");
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
                var requestBody = TestHelpers.CreateRequestMock(sessionUri, "SOMETHING IS NOT RIGHT");
                session.KeepAlive();
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = TestHelpers.CreateRequestError<WebException>(sessionUri);
                session.KeepAlive();
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = TestHelpers.CreateRequestMock(sessionUri, "SOMETHING IS NOT RIGHT");
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

            var requestBody = TestHelpers.CreateRequestMock(sessionUri, "202 INTEL Accepted");
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
                var requestBody = TestHelpers.CreateRequestMock(sessionUri, "SOMETHING IS NOT RIGHT");
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
                var requestBody = TestHelpers.CreateRequestMock(sessionUri, "SOMETHING IS NOT RIGHT");
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = TestHelpers.CreateRequestError<WebException>(sessionUri);
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch (WebException) {
            }
            Assert.IsTrue(session.IsConnected);
            Assert.AreEqual(0, closedCount);

            try {
                var requestBody = TestHelpers.CreateRequestMock(sessionUri, "SOMETHING IS NOT RIGHT");
                session.Report(intelChannel, intelTimestamp, intelString);
            } catch (WebException) {
            }
            Assert.IsFalse(session.IsConnected);
            Assert.AreEqual(1, closedCount);
        }
    }
}
