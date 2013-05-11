using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Timers;
using System.Xml;
using System.Xml.Linq;

namespace TestIntelReporter {
    /// <summary>
    ///     Queries the version service to make sure we are running the most
    ///     recent version.
    /// </summary>
    /// <remarks>
    ///     The URL must return an XML document with the following format:
    ///     <code escaped="true">
    ///         <assembly-list>
    ///             <assembly name="BlahBlahBlah" file-version="X.Y.Z.A"
    ///                       update-uri="blah://blah/blah/blah" />
    ///             <!--As many assembly's as you want-->
    ///         </assembly-list>
    ///     </code>
    /// </remarks>
    [DefaultEvent("UpdateAvailable"), DefaultProperty("CheckUri")]
    public class UpdateCheck : Component {
        // Default value for CheckInterval: One day
        private const double defaultUpdateInterval = 1000.0 * 60.0 * 60.0 * 24.0;
        // Timer object to periodically ping the server
        private readonly System.Timers.Timer timer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="UpdateCheck"/>
        ///     class.
        /// </summary>
        public UpdateCheck()
            : this(null) {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UpdateCheck"/>
        ///     class with the specified container.
        /// </summary>
        public UpdateCheck(IContainer container) {
            this.timer = new System.Timers.Timer(defaultUpdateInterval);
            this.timer.Elapsed += timer_Elapsed;
            this.timer.AutoReset = true;

            if (container != null) {
                container.Add(this);
            }
        }

        public event EventHandler<UpdateEventArgs> UpdateAvailable;

        /// <summary>
        ///     The URI to use when downloading the list of assembly versions.
        /// </summary>
        [DefaultValue(null)]
        public string CheckUri { get; set; }

        /// <summary>
        ///     The URI to use when checking for new versions.
        /// </summary>
        [DefaultValue(defaultUpdateInterval)]
        public double CheckInterval {
            get { return this.timer.Interval; }
            set { this.timer.Interval = value; }
        }

        [DefaultValue(null)]
        public ISynchronizeInvoke SynchronizationObject { get; set; }

        public void Start() {
            this.timer_Elapsed(null, null);
            this.timer.Start();
        }

        public void Stop() {
            this.timer.Stop();
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing) {
            if (disposing) {
                this.timer.Dispose();
            }
            base.Dispose(disposing);
        }

        private void timer_Elapsed(object sender, ElapsedEventArgs e) {
            var requestUri = this.CheckUri;
            if (String.IsNullOrEmpty(requestUri)) {
                return;
            }

            try {
                // Figure out our identity
                var oldAssembly = Assembly.GetEntryAssembly();
                var assemblyName = oldAssembly.GetName().Name;
                var oldVersion = new Version(oldAssembly
                    .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true)
                    .Cast<AssemblyFileVersionAttribute>()
                    .Single()
                    .Version);
                // Figure out what the server has available
                var doc = XDocument.Load(requestUri);
                var newAssembly = doc.Root
                    .Elements("assembly")
                    .Single(x => x.Attribute("id").Value == assemblyName);
                var newVersion = new Version(newAssembly.Attribute("version").Value);
                // Check if we have an update
                if (newVersion > oldVersion) {
                    var handler = this.UpdateAvailable;
                    if (handler != null) {
                        var sync = this.SynchronizationObject;
                        var args = new UpdateEventArgs(
                            newVersion.ToString(),
                            newAssembly.Attribute("update-uri").Value);
                        if ((sync != null) && sync.InvokeRequired) {
                            sync.BeginInvoke(new Action(() => handler(this, args)), null);
                        } else {
                            ThreadPool.QueueUserWorkItem((state) => handler(this, args));
                        }
                    }
                }
            } catch (WebException) {
                // Error downloading the document
            } catch (FormatException) {
                // The user's URI is invalid
            } catch (XmlException) {
                // Error parsing the XML
            } catch (InvalidOperationException) {
                // Our assembly isn't listed/is listed twice
            } catch (NullReferenceException) {
                // If an attribute doesn't exist
            }
        }
    }
}
