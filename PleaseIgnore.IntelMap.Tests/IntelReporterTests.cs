using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading;

namespace PleaseIgnore.IntelMap.Tests {
    /// <summary>
    ///     Unit tests for the <see cref="IntelReporter"/> component.
    /// </summary>
    [TestClass]
    public class IntelReporterTests {
        private readonly static string[] channelList = new string[] { "Channel1", "ChannelA" };
        private static readonly Uri channelListUri = new Uri(TestHelpers.TestScheme + "://channels");
        private static readonly Uri serviceUri = new Uri(TestHelpers.TestScheme + "://service");

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> constructs without
        ///     error.
        /// </summary>
        [TestMethod]
        public void Construct() {
            var reporter = new IntelReporter();
            Assert.AreEqual(false, reporter.IsRunning);
            Assert.AreEqual(IntelStatus.Stopped, reporter.Status);
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> disposes without
        ///     error.
        /// </summary>
        [TestMethod]
        public void Dispose() {
            var reporter = new IntelReporter();
            reporter.Dispose();
            Assert.IsFalse(reporter.IsRunning);
            Assert.AreEqual(IntelStatus.Disposed, reporter.Status);
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> starts and stops correctly.
        /// </summary>
        [TestMethod]
        public void StartStop() {
            using (var testDir = new TempDirectory()) {
                using (var reporter = new IntelReporter()) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";

                    reporter.ChannelListUri = channelListUri.OriginalString;
                    TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));

                    reporter.ServiceUri = serviceUri.OriginalString;
                    var requestBody = TestHelpers.CreateRequestMock(serviceUri, "200 AUTH 0123456789ABCDEF 5");

                    reporter.Start();
                    Thread.Sleep(100);

                    Assert.IsTrue(reporter.IsRunning);
                    Assert.IsTrue(requestBody.Length > 0);
                    Assert.AreEqual(IntelStatus.Active, reporter.Status);
                    Assert.AreEqual(2, reporter.Channels.Count);
                    Assert.IsNotNull(reporter.Channels.Single(x => x.Name == channelList[0]));
                    Assert.IsNotNull(reporter.Channels.Single(x => x.Name == channelList[1]));
                    Assert.IsTrue(reporter.Channels.All(x => x.IsRunning));

                    TestHelpers.Cleanup();
                    requestBody = TestHelpers.CreateRequestMock(serviceUri, "201 AUTH Logged Off");
                    reporter.Stop();
                    Thread.Sleep(100);

                    Assert.IsFalse(reporter.IsRunning);
                    Assert.IsTrue(requestBody.Length > 0);
                    Assert.AreEqual(IntelStatus.Stopped, reporter.Status);
                    Assert.IsTrue(reporter.Channels.All(x => !x.IsRunning));
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> correctly signals an
        ///     error if it cannot contact the server when starting.
        /// </summary>
        [TestMethod]
        public void StartWebError() {
            using (var testDir = new TempDirectory()) {
                using (var reporter = new IntelReporter()) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";

                    reporter.ChannelListUri = channelListUri.OriginalString;
                    TestHelpers.CreateRequestError<WebException>(channelListUri);

                    reporter.ServiceUri = serviceUri.OriginalString;
                    var requestBody = TestHelpers.CreateRequestError<WebException>(serviceUri);

                    reporter.Start();
                    Thread.Sleep(100);

                    Assert.IsTrue(reporter.IsRunning);
                    Assert.IsTrue(requestBody.Length > 0);
                    Assert.AreEqual(IntelStatus.NetworkError, reporter.Status);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> correctly signals an
        ///     error if it cannot contact login when starting.
        /// </summary>
        [TestMethod]
        public void StartAuthError() {
            using (var testDir = new TempDirectory()) {
                using (var reporter = new IntelReporter()) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";

                    reporter.ChannelListUri = channelListUri.OriginalString;
                    TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));

                    reporter.ServiceUri = serviceUri.OriginalString;
                    var requestBody = TestHelpers.CreateRequestMock(serviceUri, "500 ERROR AUTH");

                    reporter.Start();
                    Thread.Sleep(100);

                    Assert.IsTrue(reporter.IsRunning);
                    Assert.IsTrue(requestBody.Length > 0);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> correctly signals an
        ///     error if it starts without any credentials set.
        /// </summary>
        [TestMethod]
        public void StartNoAuth() {
            using (var testDir = new TempDirectory()) {
                using (var reporter = new IntelReporter()) {
                    reporter.Path = testDir.FullName;

                    reporter.ChannelListUri = channelListUri.OriginalString;
                    TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));

                    reporter.Start();
                    Thread.Sleep(100);

                    Assert.IsTrue(reporter.IsRunning);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> correctly raises the
        ///     <see cref="IntelReporter.IntelReported"/> event.
        /// </summary>
        [TestMethod]
        public void IntelReported() {
            using (var testDir = new TempDirectory()) {
                using (var reporter = new IntelReporter()) {
                    reporter.Path = testDir.FullName;
                    IntelEventArgs received = null;
                    reporter.IntelReported += (sender, e) => received = e;

                    reporter.ChannelListUri = channelListUri.OriginalString;
                    TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));

                    reporter.Start();
                    var testEvent = new IntelEventArgs(channelList[0], DateTime.UtcNow, "Test Message");
                    Thread.Sleep(100);
                    Assert.AreEqual(0, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);

                    reporter.Channels.First().OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.IsTrue(reporter.IsRunning);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);
                    Assert.AreEqual(received, testEvent);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelStatus.NetworkError"/> properly clears.
        /// </summary>
        [TestMethod]
        public void WebErrorClear() {
            var reporterMock = new Mock<IntelReporter>(MockBehavior.Loose) {
                CallBase = true
            };

            TestHelpers.CreateRequestMock(serviceUri, "200 AUTH 0123456789ABCDEF 5");
            TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));

            var testEvent = new IntelEventArgs(channelList[0], DateTime.UtcNow, "Test Message");
            var sessionMock = new Mock<IntelSession>(MockBehavior.Loose, "username", "password", serviceUri);
            sessionMock.Setup(x => x.Report(testEvent.Channel, testEvent.Timestamp, testEvent.Message))
                .Returns(true);

            IntelSession session = null;
            reporterMock.Protected()
                .Setup<IntelSession>("GetSession", ItExpr.IsAny<bool>())
                .Returns(new Func<IntelSession>(delegate() {
                    if (session == null) throw new WebException();
                    return session; 
                }));

            using (var testDir = new TempDirectory()) {
                using (var reporter = reporterMock.Object) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";

                    reporter.ServiceUri = serviceUri.OriginalString;
                    reporter.ChannelListUri = channelListUri.OriginalString;

                    reporter.Start();
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.NetworkError, reporter.Status);

                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.NetworkError, reporter.Status);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);

                    session = sessionMock.Object;
                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.Active, reporter.Status);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(1, reporter.IntelSent);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelStatus.AuthenticationError"/>
        ///     properly clears.
        /// </summary>
        [TestMethod]
        public void AuthErrorClear() {
            var reporterMock = new Mock<IntelReporter>(MockBehavior.Loose) {
                CallBase = true
            };

            TestHelpers.CreateRequestMock(serviceUri, "200 AUTH 0123456789ABCDEF 5");
            TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));

            var testEvent = new IntelEventArgs(channelList[0], DateTime.UtcNow, "Test Message");
            var sessionMock = new Mock<IntelSession>(MockBehavior.Loose, "username", "password", serviceUri);
            sessionMock.Setup(x => x.Report(testEvent.Channel, testEvent.Timestamp, testEvent.Message))
                .Returns(true);

            IntelSession session = null;
            reporterMock.Protected()
                .Setup<IntelSession>("GetSession", ItExpr.IsAny<bool>())
                .Returns(new Func<IntelSession>(delegate() {
                if (session == null) throw new AuthenticationException();
                return session;
            }));

            using (var testDir = new TempDirectory()) {
                using (var reporter = reporterMock.Object) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";
                    reporter.AuthenticationRetryTimeout = new TimeSpan(0, 0, 0, 0, 10);

                    reporter.ServiceUri = serviceUri.OriginalString;
                    reporter.ChannelListUri = channelListUri.OriginalString;

                    reporter.Start();
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);

                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);

                    session = sessionMock.Object;
                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.Active, reporter.Status);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(1, reporter.IntelSent);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelStatus.AuthenticationError"/>
        ///     transitions into <see cref="IntelStatus.NetworkError"/>.
        /// </summary>
        [TestMethod]
        public void AuthErrorToNetworkError() {
            var reporterMock = new Mock<IntelReporter>(MockBehavior.Loose) {
                CallBase = true
            };

            TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));
            var testEvent = new IntelEventArgs(channelList[0], DateTime.UtcNow, "Test Message");
            var sessionMock = new Mock<IntelSession>(MockBehavior.Loose, "username", "password", serviceUri);
            sessionMock.Setup(x => x.Report(testEvent.Channel, testEvent.Timestamp, testEvent.Message))
                .Returns(true);

            Exception exception = new AuthenticationException();
            reporterMock.Protected()
                .Setup<IntelSession>("GetSession", ItExpr.IsAny<bool>())
                .Returns(() => { throw exception; });

            using (var testDir = new TempDirectory()) {
                using (var reporter = reporterMock.Object) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";
                    reporter.AuthenticationRetryTimeout = new TimeSpan(0, 0, 0, 0, 10);

                    reporter.ServiceUri = serviceUri.OriginalString;
                    reporter.ChannelListUri = channelListUri.OriginalString;

                    reporter.Start();
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);

                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);

                    exception = new WebException();
                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.NetworkError, reporter.Status);
                    Assert.AreEqual(2, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);
                }
            }
        }

        /// <summary>
        ///     Verifies that <see cref="IntelStatus.NetworkError"/>
        ///     transitions into <see cref="IntelStatus.AuthenticationException"/>.
        /// </summary>
        [TestMethod]
        public void NetworkErrorToAuthError() {
            var reporterMock = new Mock<IntelReporter>(MockBehavior.Loose) {
                CallBase = true
            };

            TestHelpers.CreateRequestMock(channelListUri, String.Join("\r\n", channelList));
            var testEvent = new IntelEventArgs(channelList[0], DateTime.UtcNow, "Test Message");
            var sessionMock = new Mock<IntelSession>(MockBehavior.Loose, "username", "password", serviceUri);
            sessionMock.Setup(x => x.Report(testEvent.Channel, testEvent.Timestamp, testEvent.Message))
                .Returns(true);

            Exception exception = new WebException();
            reporterMock.Protected()
                .Setup<IntelSession>("GetSession", ItExpr.IsAny<bool>())
                .Returns(() => { throw exception; });

            using (var testDir = new TempDirectory()) {
                using (var reporter = reporterMock.Object) {
                    reporter.Path = testDir.FullName;
                    reporter.Username = "username";
                    reporter.PasswordHash = "password";
                    reporter.AuthenticationRetryTimeout = new TimeSpan(0, 0, 0, 0, 10);

                    reporter.ServiceUri = serviceUri.OriginalString;
                    reporter.ChannelListUri = channelListUri.OriginalString;

                    reporter.Start();
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.NetworkError, reporter.Status);

                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.NetworkError, reporter.Status);
                    Assert.AreEqual(1, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);

                    exception = new AuthenticationException();
                    reporter.OnIntelReported(testEvent);
                    Thread.Sleep(100);
                    Assert.AreEqual(IntelStatus.AuthenticationError, reporter.Status);
                    Assert.AreEqual(2, reporter.IntelDropped);
                    Assert.AreEqual(0, reporter.IntelSent);
                }
            }
        }
    }
}
