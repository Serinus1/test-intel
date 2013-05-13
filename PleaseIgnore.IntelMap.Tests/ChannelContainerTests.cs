using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;

namespace PleaseIgnore.IntelMap.Tests {
    [TestClass]
    public class ChannelContainerTests {
        private readonly static string[] channelList = new string[] { "Channel1", "ChannelA" };
        private readonly static string channelBody = string.Join("\r\n", channelList.Select(x => x + ",No Longer Used"));
        // For routing into our Mock WebRequest
        private readonly static Uri channelUri = new Uri(TestHelpers.TestScheme + "://blah-blah-blah");

        [TestCleanup]
        public void Cleanup() {
            TestHelpers.Cleanup();
        }

        /// <summary>
        ///     Verifies <see cref="IntelChannelContainer"/> initializes
        ///     correctly.
        /// </summary>
        [TestMethod]
        public void Constructor() {
            var container = new IntelChannelContainer();
            Assert.AreEqual(IntelStatus.Stopped, container.Status);
            Assert.IsFalse(container.IsRunning);
        }

        /// <summary>
        ///     Verifies <see cref="IntelChannelContainer"/> disposes
        ///     correctly.
        /// </summary>
        [TestMethod]
        public void Dispose() {
            var container = new IntelChannelContainer();
            container.Dispose();
            Assert.AreEqual(IntelStatus.Disposed, container.Status);
            Assert.IsFalse(container.IsRunning);
        }

        /// <summary>
        ///     Tests the <see cref="IntelChannelContainer.GetChannelList"/>
        ///     member.
        /// </summary>
        [TestMethod]
        public void GetChannelList() {
            TestHelpers.CreateRequestMock(channelUri, channelBody);
            var list = IntelChannelContainer.GetChannelList(channelUri);
            CollectionAssert.AreEqual(channelList, list);
        }

        /// <summary>
        ///     Tests the <see cref="IntelChannelContainer.Start"/> member.
        /// </summary>
        [TestMethod]
        public void Start() {
            TestHelpers.CreateRequestMock(channelUri, channelBody);
            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected().Setup("OnStart")
                .Verifiable();

            using (var container = containerMock.Object) {
                container.ChannelListUri = channelUri;
                containerMock.Protected().Verify("OnStart", Times.Never());

                container.Start();
                containerMock.Protected().Verify("OnStart", Times.Once());

                container.Start();
                container.Start();
                containerMock.Protected().Verify("OnStart", Times.Once());

                Assert.AreEqual(IntelStatus.Starting, container.Status);
                Assert.IsTrue(container.IsRunning);
            }
        }

        /// <summary>
        ///     Tests the <see cref="IntelChannelContainer.Start"/> member.
        /// </summary>
        [TestMethod]
        public void Stop() {
            TestHelpers.CreateRequestMock(channelUri, String.Join("\r\n", channelList));
            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected().Setup("OnStart")
                .Verifiable();
            containerMock.Protected().Setup("OnStop")
                .Verifiable();

            var container = containerMock.Object;
            container.ChannelListUri = channelUri;
            containerMock.Protected().Verify("OnStop", Times.Never());

            container.Start();
            containerMock.Protected().Verify("OnStop", Times.Never());

            container.Stop();
            containerMock.Protected().Verify("OnStop", Times.Once());
            container.Stop();
            container.Stop();
            containerMock.Protected().Verify("OnStop", Times.Once());

            Assert.AreEqual(IntelStatus.Stopped, container.Status);
            Assert.IsFalse(container.IsRunning);
        }

        /// <summary>
        ///     Tests that the <see cref="IntelChannelContainer.Channels"/>
        ///     member is populated.
        /// </summary>
        [TestMethod]
        public void Channels() {
            TestHelpers.CreateRequestMock(channelUri, channelBody);

            var chan1Mock = new Mock<IntelChannel>(MockBehavior.Loose);
            chan1Mock.Object.Name = channelList[0];
            chan1Mock.SetupGet(x => x.Status).Returns(IntelStatus.Waiting);
            var chan2Mock = new Mock<IntelChannel>(MockBehavior.Loose);
            chan2Mock.Object.Name = channelList[1];
            chan2Mock.SetupGet(x => x.Status).Returns(IntelStatus.Active);

            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected()
                .Setup<IntelChannel>("CreateChannel", ItExpr.IsAny<string>())
                .Throws<AssertFailedException>();
            containerMock.Protected()
                .Setup<IntelChannel>("CreateChannel", channelList[0])
                .Returns(chan1Mock.Object);
            containerMock.Protected()
                .Setup<IntelChannel>("CreateChannel", channelList[1])
                .Returns(chan2Mock.Object);

            using (var container = containerMock.Object) {
                container.ChannelListUri = channelUri;
                container.Start();
                Thread.Sleep(100);

                containerMock.Protected()
                    .Verify("CreateChannel", Times.Once(), channelList[0]);
                containerMock.Protected()
                    .Verify("CreateChannel", Times.Once(), channelList[1]);

                var channels = container.Channels;
                Assert.IsNotNull(channels);
                CollectionAssert.AllItemsAreUnique(channels);
                Assert.AreEqual(2, channels.Count);

                var chan1 = channels[channelList[0]];
                Assert.IsNotNull(chan1);
                Assert.AreEqual(chan1Mock.Object, chan1);
                chan1Mock.Verify(x => x.Start(), Times.Once());

                var chan2 = channels[channelList[1]];
                Assert.IsNotNull(chan2);
                Assert.AreEqual(chan2Mock.Object, chan2);
                chan2Mock.Verify(x => x.Start(), Times.Once());

                Assert.AreEqual(IntelStatus.Active, container.Status);
            }
        }

        /// <summary>
        ///     Tests that the <see cref="IntelChannelContainer.IntelReported"/>
        ///     event is properly forwarded.
        /// </summary>
        [TestMethod]
        public void IntelReported() {
            TestHelpers.CreateRequestMock(channelUri, channelBody);

            var chan1Mock = new Mock<IntelChannel>(MockBehavior.Loose);
            chan1Mock.Object.Name = channelList[0];
            chan1Mock.SetupGet(x => x.Status).Returns(IntelStatus.Waiting);
            var chan2Mock = new Mock<IntelChannel>(MockBehavior.Loose);
            chan2Mock.Object.Name = channelList[0];
            chan2Mock.SetupGet(x => x.Status).Returns(IntelStatus.Active);

            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected()
                .Setup<IntelChannel>("CreateChannel", channelList[0])
                .Returns(delegate() {
                    var obj = chan1Mock.Object;
                    var method = typeof(IntelChannelContainer)
                        .GetMethod("OnIntelReported", BindingFlags.NonPublic | BindingFlags.Instance);
                    obj.IntelReported += (sender, e) => method
                        .Invoke(containerMock.Object, new object[] { e });
                    return obj;
                });
            containerMock.Protected()
                .Setup<IntelChannel>("CreateChannel", channelList[1])
                .Returns(delegate() {
                    var obj = chan2Mock.Object;
                    var method = typeof(IntelChannelContainer)
                        .GetMethod("OnIntelReported", BindingFlags.NonPublic | BindingFlags.Instance);
                    obj.IntelReported += (sender, e) => method
                        .Invoke(containerMock.Object, new object[] { e });
                    return obj;
                });

            var raised = new List<IntelEventArgs>();
            var e1 = new IntelEventArgs(
                channelList[0],
                DateTime.UtcNow,
                "test message");
            var e2 = new IntelEventArgs(
                channelList[1],
                DateTime.UtcNow,
                "test message");
            var e3 = new IntelEventArgs(
                channelList[0],
                DateTime.UtcNow,
                "test message");

            using (var container = containerMock.Object) {
                container.ChannelListUri = channelUri;
                container.IntelReported += delegate(object sender, IntelEventArgs e) {
                    Assert.AreEqual(container, sender);
                    Assert.IsNotNull(e);
                    lock (raised) {
                        raised.Add(e);
                    }
                };
                container.Start();
                Thread.Sleep(100);

                chan1Mock.Object.OnIntelReported(e1);
                chan2Mock.Object.OnIntelReported(e2);
                chan1Mock.Object.OnIntelReported(e3);
                Thread.Sleep(100);

                Assert.AreEqual(IntelStatus.Active, container.Status);
            }

            CollectionAssert.AreEquivalent(
                new IntelEventArgs[] { e1, e2, e3 },
                raised);
        }
    }
}
