using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PleaseIgnore.IntelMap.Tests {
    /// <summary>
    ///     Unit tests for the <see cref="IntelReporter"/> component.
    /// </summary>
    [TestClass]
    public class IntelReporterTests {
        [TestMethod]
        public void Construct() {
            var reporter = new IntelReporter();
            Assert.AreEqual(false, reporter.IsRunning);
            Assert.AreEqual(IntelStatus.Stopped, reporter.Status);
        }

        [TestMethod]
        public void Dispose() {
            var reporter = new IntelReporter();
            reporter.Dispose();
            Assert.AreEqual(false, reporter.IsRunning);
            Assert.AreEqual(IntelStatus.Disposed, reporter.Status);
        }
    }
}
