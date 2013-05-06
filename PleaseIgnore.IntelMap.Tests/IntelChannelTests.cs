using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace PleaseIgnore.IntelMap.Tests {
    [TestClass]
    public class IntelChannelTests {
        private const string channelName = "test_channel";

        /// <summary>
        ///     Verifies the component is properly constructed.
        /// </summary>
        [TestMethod]
        public void Construct() {
            var container = new Container();
            var channel = new IntelChannel(channelName, container);

            Assert.AreEqual(channelName, channel.Name);
            Assert.AreEqual(container, channel.Container);
            Assert.AreEqual(IntelStatus.Stopped, channel.Status);
            Assert.IsFalse(channel.IsRunning);
        }

        /// <summary>
        ///     Verifies the component properly disposes.
        /// </summary>
        [TestMethod]
        public void Dispose() {
            var channel = new IntelChannel();
            channel.Dispose();
            Assert.AreEqual(IntelStatus.Disposed, channel.Status);
            Assert.IsFalse(channel.IsRunning);
        }

        /// <summary>
        ///     Verifies the <see cref="IntelChannel.PropertyChanged"/> event is
        ///     properly raised upon a call to <see cref="IntelChannel.OnIntelReported"/>
        /// </summary>
        [TestMethod]
        public void IntelReported() {
            var sent = new IntelEventArgs("channel", DateTime.UtcNow, "message");
            IntelEventArgs received = null;

            using (var channel = new IntelChannel()) {
                channel.IntelReported += (sender, e) => {
                    Assert.IsNull(received, "IntelReported was raised multiple times.");
                    Assert.AreEqual(channel, sender);
                    received = e;
                };
                channel.OnIntelReported(sent);
                Thread.Sleep(10);
                Assert.AreEqual(1, channel.IntelCount);
            }

            Assert.AreEqual(sent, received);
        }

        /// <summary>
        ///     Verifies the <see cref="IntelChannel.PropertyChanged"/> event is
        ///     properly raised upon a call to <see cref="IntelChannel.OnPropertyChanged"/>
        /// </summary>
        [TestMethod]
        public void PropertyChanged() {
            var sent = new PropertyChangedEventArgs("Status");
            PropertyChangedEventArgs received = null;

            using (var channel = new IntelChannel()) {
                channel.PropertyChanged += (sender, e) => {
                    Assert.IsNull(received, "PropertyChanged was raised multiple times.");
                    Assert.AreEqual(channel, sender);
                    received = e;
                };
                channel.OnPropertyChanged(sent);
                Thread.Sleep(10);
            }

            Assert.AreEqual(sent, received);
        }

        /// <summary>
        ///     Verifies component will refuse to start if <see cref="IntelChannel.Name"/>
        ///     is not provided.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidOperationException))]
        public void NameMissing() {
            using (var channel = new IntelChannel()) {
                channel.Start();
            }
        }

        /// <summary>
        ///     Verifies the component enters a running state upon a call to
        ///     <see cref="IntelChannel.Start"/>
        /// </summary>
        [TestMethod]
        public void Start() {
            using (var channel = new IntelChannel(channelName)) {
                channel.Start();
                Assert.IsTrue(channel.IsRunning);
            }
        }

        /// <summary>
        ///     Verifies the component exits the running state upon a call to
        ///     <see cref="IntelChannel.Stop"/>
        /// </summary>
        [TestMethod]
        public void Stop() {
            using (var channel = new IntelChannel(channelName)) {
                channel.Start();
                Assert.IsTrue(channel.IsRunning);

                channel.Stop();
                Assert.IsFalse(channel.IsRunning);
                Assert.AreEqual(IntelStatus.Stopped, channel.Status);
            }
        }

        /// <summary>
        ///     Makes sure <see cref="IntelChannel.OnStart"/> is called
        ///     once, and only once, per call to <see cref="IntelChannel.Start"/>
        /// </summary>
        [TestMethod]
        public void OnStart() {
            var channelMock = new Mock<IntelChannel> {
                CallBase = true
            };

            using (var channel = channelMock.Object) {
                channel.Name = channelName;
                channelMock.Protected().Verify("OnStart", Times.Never());

                channel.Start();
                channelMock.Protected().Verify("OnStart", Times.Once());
                channel.Start();
                channelMock.Protected().Verify("OnStart", Times.Once());
                channel.Start();
                channelMock.Protected().Verify("OnStart", Times.Once());

                channel.Stop();
                channelMock.Protected().Verify("OnStart", Times.Once());

                channel.Start();
                channelMock.Protected().Verify("OnStart", Times.Exactly(2));
                channel.Start();
                channelMock.Protected().Verify("OnStart", Times.Exactly(2));
            }
        }

        /// <summary>
        ///     Makes sure <see cref="IntelChannel.OnStop"/> is called
        ///     once, and only once, per call to <see cref="IntelChannel.Stop"/>
        /// </summary>
        [TestMethod]
        public void OnStop() {
            var channelMock = new Mock<IntelChannel> {
                CallBase = true
            };

            using (var channel = channelMock.Object) {
                channel.Name = channelName;
                channelMock.Protected().Verify("OnStop", Times.Never());

                channel.Start();
                channelMock.Protected().Verify("OnStop", Times.Never());

                channel.Stop();
                channelMock.Protected().Verify("OnStop", Times.Once());
                channel.Stop();
                channelMock.Protected().Verify("OnStop", Times.Once());
                channel.Stop();
                channelMock.Protected().Verify("OnStop", Times.Once());

                channel.Start();
                channelMock.Protected().Verify("OnStop", Times.Once());
                channel.Stop();
                channelMock.Protected().Verify("OnStop", Times.Exactly(2));
                channel.Stop();
                channelMock.Protected().Verify("OnStop", Times.Exactly(2));
            }
        }

        /// <summary>
        ///     Makes sure <see cref="IntelChannel"/> will properly parse the
        ///     content from a log file.
        /// </summary>
        [TestMethod]
        public void ParseLog() {
            var lines = new string[] {
                ﻿"[ 2013.04.29 23:54:10 ] Addemar > Drevas  6VDT-H nv",
                "[ 2013.04.29 23:54:38 ] Addemar > capsole",
                "[ 2013.04.29 23:54:48 ] Addemar > *pod",
                "[ 2013.04.29 23:55:03 ] Scilus > Drevas (Purifier*)",
                "t stryker > Gheos  B17O-R NV",
                "[ 2013.04.30 00:04:54 ] Arayan Light > Kill: Drevas (Capsule) =)"
            };

            var received = new List<IntelEventArgs>();

            using (var file = new TempFile(channelName)) {
                using (var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.Read)) {
                    using (var writer = new StreamWriter(stream, new UnicodeEncoding(false, false))) {
                        writer.AutoFlush = true;
                        writer.WriteLine(lines[0]);

                        using (var channel = new IntelChannel(channelName)) {
                            channel.IntelReported += (sender, e) => {
                                lock (received) {
                                    received.Add(e);
                                }
                            };
                            channel.Start();

                            lock (channel.syncRoot) {
                                channel.OpenFile(file.FileInfo);
                            }

                            Assert.IsNotNull(channel.LogFile);
                            Assert.AreEqual(file.Name, channel.LogFile.Name, true);

                            for (int line = 1; line < lines.Length; ++line) {
                                if (line % 2 == 1) {
                                    writer.Write('\uFEFF');
                                }
                                writer.WriteLine(lines[line]);
                            }

                            Thread.Sleep(10000);
                            Assert.AreEqual(4, channel.IntelCount);
                        } //using (var channel = channelMock.Object) {
                    } //using (var writer = new StreamWriter(...)) {
                } //using (var stream = file.Open(...)) {
            } //using (var file = new TempFile()) {

            Assert.AreEqual(4, received.Count);
            Assert.IsFalse(received.Any(x => (x.Message.Contains("6VDT-H"))),
                "Should not have parsed " + lines[0]);
            Assert.IsTrue(received.Any(x
                => (x.Message == lines[1].Substring(23))
                && (x.Timestamp == new DateTime(2013, 4, 29, 23, 54, 38, DateTimeKind.Utc))
                && (x.Channel == channelName)),
                "Did not parse " + lines[1]);
            Assert.IsTrue(received.Any(x
                => (x.Message == lines[2].Substring(23))
                && (x.Timestamp == new DateTime(2013, 4, 29, 23, 54, 48, DateTimeKind.Utc))
                && (x.Channel == channelName)),
                "Did not parse " + lines[2]);
            Assert.IsTrue(received.Any(x
                => (x.Message == lines[3].Substring(23))
                && (x.Timestamp == new DateTime(2013, 4, 29, 23, 55,  3, DateTimeKind.Utc))
                && (x.Channel == channelName)),
                "Did not parse " + lines[3]);
            Assert.IsFalse(received.Any(x => (x.Message.Contains("B17O-R"))),
                "Should not have parsed " + lines[4]);
            Assert.IsTrue(received.Any(x
                => (x.Message == lines[5].Substring(23))
                && (x.Timestamp == new DateTime(2013, 4, 30,  0,  4, 54, DateTimeKind.Utc))
                && (x.Channel == channelName)),
                "Did not parse " + lines[5]);
        }

        /// <summary>
        ///     Makes sure the <see cref="IntelChannel"/> will properly read
        ///     entries from new log files.
        /// </summary>
        [TestMethod]
        public void DirectorySearch() {
            using (var sync = new AutoResetEvent(false)) {
                using (var directory = new TempDirectory()) {
                    using (var channel = new IntelChannel(channelName)) {
                        channel.Path = directory.FullName;
                        channel.PropertyChanged += (sender, e) => {
                            if (e.PropertyName == "LogFile") {
                                sync.Set();
                            }
                        };

                        // Make sure 'something' exists before the test
                        var file1 = new TempFile(channelName, directory);
                        using (file1.Open(FileMode.Create, FileAccess.Write, FileShare.Read)) {
                        }

                        // Start the component
                        channel.Start();

                        // Make sure it is logging the appropriate file
                        Assert.IsTrue(sync.WaitOne(5000), "Did not raise PropertyChanged for file1");
                        Assert.AreEqual(IntelStatus.Active, channel.Status);
                        Assert.IsNotNull(channel.LogFile);
                        Assert.AreEqual(file1.Name, channel.LogFile.Name, true);

                        // Create a new file to monitor
                        Thread.Sleep(2000);
                        var file2 = new TempFile(channelName, directory);
                        using (file2.Open(FileMode.Create, FileAccess.Write, FileShare.Read)) {
                        }

                        // Make sure it is logging the appropriate file
                        Assert.IsTrue(sync.WaitOne(5000), "Did not raise PropertyChanged for file2");
                        Assert.AreEqual(IntelStatus.Active, channel.Status);
                        Assert.IsNotNull(channel.LogFile);
                        Assert.AreEqual(file2.Name, channel.LogFile.Name, true);
                    }
                }
            }
        }
    }
}
