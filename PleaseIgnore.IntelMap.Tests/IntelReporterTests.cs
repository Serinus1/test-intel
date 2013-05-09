using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
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
            Assert.AreEqual(false, reporter.IsRunning);
            Assert.AreEqual(IntelStatus.Disposed, reporter.Status);
        }

        /// <summary>
        ///     Verifies that <see cref="IntelReporter"/> starts up correctly.
        /// </summary>
        [TestMethod]
        public void Start() {
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
                    Thread.Sleep(1000);

                    Assert.IsTrue(reporter.IsRunning);
                    Assert.IsTrue(requestBody.Length > 0);
                    Assert.AreEqual(IntelStatus.Active, reporter.Status);

                    TestHelpers.Cleanup();
                    TestHelpers.CreateRequestMock(serviceUri, "201 AUTH Logged Off");
                }
            }
        }
    }
}
