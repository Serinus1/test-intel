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
        const string testScheme = "container-test";
        private readonly static string[] channelList = new string[] { "Channel1", "ChannelA" };
        // For routing into our Mock WebRequest
        private readonly static Uri channelUri = new Uri(testScheme + "://blah-blah-blah");

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
            proxyMock.Setup(x => x.Create(channelUri))
                .Returns(requestMock.Object);
            requestProxy = proxyMock.Object;

            return requestBody;
        }
        
        /// <summary>
        ///     Verifies <see cref="IntelChannelContainer"/> initializes
        ///     correctly.
        /// </summary>
        [TestMethod]
        public void Constructor() {
            var container = new IntelChannelContainer();
            Assert.AreEqual(IntelChannelStatus.Stopped, container.Status);
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
            Assert.AreEqual(IntelChannelStatus.Disposed, container.Status);
            Assert.IsFalse(container.IsRunning);
        }

        /// <summary>
        ///     Tests the <see cref="IntelChannelContainer.GetChannelList"/>
        ///     member.
        /// </summary>
        [TestMethod]
        public void GetChannelList() {
            this.CreateRequestMock(String.Join("\r\n", channelList));
            var list = IntelChannelContainer.GetChannelList(channelUri);
            CollectionAssert.AreEqual(channelList, list);
        }

        /// <summary>
        ///     Tests the <see cref="IntelChannelContainer.Start"/> member.
        /// </summary>
        [TestMethod]
        public void Start() {
            this.CreateRequestMock(String.Join("\r\n", channelList));
            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected().Setup("OnStart")
                .Verifiable();

            using (var container = containerMock.Object) {
                container.ChannelListUri = channelUri.OriginalString;
                containerMock.Protected().Verify("OnStart", Times.Never());

                container.Start();
                containerMock.Protected().Verify("OnStart", Times.Once());

                container.Start();
                container.Start();
                containerMock.Protected().Verify("OnStart", Times.Once());

                Assert.AreEqual(IntelChannelStatus.Waiting, container.Status);
                Assert.IsTrue(container.IsRunning);
            }
        }

        /// <summary>
        ///     Tests the <see cref="IntelChannelContainer.Start"/> member.
        /// </summary>
        [TestMethod]
        public void Stop() {
            this.CreateRequestMock(String.Join("\r\n", channelList));
            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected().Setup("OnStart")
                .Verifiable();
            containerMock.Protected().Setup("OnStop")
                .Verifiable();

            var container = containerMock.Object;
            container.ChannelListUri = channelUri.OriginalString;
            containerMock.Protected().Verify("OnStop", Times.Never());

            container.Start();
            containerMock.Protected().Verify("OnStop", Times.Never());

            container.Stop();
            containerMock.Protected().Verify("OnStop", Times.Once());
            container.Stop();
            container.Stop();
            containerMock.Protected().Verify("OnStop", Times.Once());

            Assert.AreEqual(IntelChannelStatus.Stopped, container.Status);
            Assert.IsFalse(container.IsRunning);
        }

        /// <summary>
        ///     Tests that the <see cref="IntelChannelContainer.Channels"/>
        ///     member is populated.
        /// </summary>
        [TestMethod]
        public void Channels() {
            this.CreateRequestMock(String.Join("\r\n", channelList));

            var chan1Mock = new Mock<IIntelChannel>(MockBehavior.Strict);
            chan1Mock.SetupProperty(x => x.Site);
            chan1Mock.SetupGet(x => x.Status).Returns(IntelChannelStatus.Waiting);
            chan1Mock.SetupGet(x => x.Name).Returns(channelList[0]);
            chan1Mock.Setup(x => x.Start()).Verifiable();
            var chan2Mock = new Mock<IIntelChannel>(MockBehavior.Strict);
            chan2Mock.SetupProperty(x => x.Site);
            chan2Mock.SetupGet(x => x.Status).Returns(IntelChannelStatus.Active);
            chan2Mock.SetupGet(x => x.Name).Returns(channelList[1]);
            chan2Mock.Setup(x => x.Start()).Verifiable();

            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected()
                .Setup<IIntelChannel>("CreateChannel", ItExpr.IsAny<string>())
                .Throws<AssertFailedException>();
            containerMock.Protected()
                .Setup<IIntelChannel>("CreateChannel", channelList[0])
                .Returns(chan1Mock.Object);
            containerMock.Protected()
                .Setup<IIntelChannel>("CreateChannel", channelList[1])
                .Returns(chan2Mock.Object);

            using (var container = containerMock.Object) {
                container.ChannelListUri = channelUri.OriginalString;
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

                Assert.AreEqual(IntelChannelStatus.Active, container.Status);
            }
        }

        /// <summary>
        ///     Tests that the <see cref="IntelChannelContainer.IntelReported"/>
        ///     event is properly forwarded.
        /// </summary>
        [TestMethod]
        public void IntelReported() {
            this.CreateRequestMock(String.Join("\r\n", channelList));

            var chan1Mock = new Mock<IIntelChannel>(MockBehavior.Strict);
            chan1Mock.SetupProperty(x => x.Site);
            chan1Mock.SetupGet(x => x.Status).Returns(IntelChannelStatus.Waiting);
            chan1Mock.SetupGet(x => x.Name).Returns(channelList[0]);
            chan1Mock.Setup(x => x.Start()).Verifiable();
            var chan2Mock = new Mock<IIntelChannel>(MockBehavior.Strict);
            chan2Mock.SetupProperty(x => x.Site);
            chan2Mock.SetupGet(x => x.Status).Returns(IntelChannelStatus.Active);
            chan2Mock.SetupGet(x => x.Name).Returns(channelList[1]);
            chan2Mock.Setup(x => x.Start()).Verifiable();

            var containerMock = new Mock<IntelChannelContainer>(MockBehavior.Loose) {
                CallBase = true
            };
            containerMock.Protected()
                .Setup<IIntelChannel>("CreateChannel", channelList[0])
                .Returns(delegate() {
                    var obj = chan1Mock.Object;
                    var method = typeof(IntelChannelContainer)
                        .GetMethod("OnIntelReported", BindingFlags.NonPublic | BindingFlags.Instance);
                    obj.IntelReported += (sender, e) => method
                        .Invoke(containerMock.Object, new object[] { e });
                    return obj;
                });
            containerMock.Protected()
                .Setup<IIntelChannel>("CreateChannel", channelList[1])
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
                container.ChannelListUri = channelUri.OriginalString;
                container.IntelReported += delegate(object sender, IntelEventArgs e) {
                    Assert.AreEqual(container, sender);
                    Assert.IsNotNull(e);
                    lock (raised) {
                        raised.Add(e);
                    }
                };
                container.Start();
                Thread.Sleep(100);

                chan1Mock.Raise(x => x.IntelReported += null, e1);
                chan2Mock.Raise(x => x.IntelReported += null, e2);
                chan1Mock.Raise(x => x.IntelReported += null, e3);
                Thread.Sleep(100);

                Assert.AreEqual(IntelChannelStatus.Active, container.Status);
            }

            CollectionAssert.AreEquivalent(
                new IntelEventArgs[] { e1, e2, e3 },
                raised);
        }
    }
}
