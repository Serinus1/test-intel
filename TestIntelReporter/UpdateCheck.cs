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
        private const double intervalSecond = 1000.0;
        private const double intervalMinute = intervalSecond * 60;
        private const double intervalHour = intervalMinute * 60;
        private const double intervalDay = intervalHour * 24;
        // Default value for CheckInterval: One day
        private const double defaultUpdateInterval = intervalDay;

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
            this.timer.Elapsed += this.timer_Elapsed;
            this.timer.AutoReset = true;

            // Add to the parent container
            if (container != null) {
                container.Add(this);
            }
        }

        /// <summary>
        ///     Raised after pinging the server an a new version is available.
        /// </summary>
        public event EventHandler<UpdateEventArgs> UpdateAvailable;

        /// <summary>
        ///     Gets the name of the currently executing assembly.
        /// </summary>
        /// <remarks>
        ///     <see cref="UpdateCheck"/> uses <see cref="AssemblyName"/> as the
        ///     assembly to perform a version check on.
        /// </remarks>
        public string AssemblyName {
            get {
                var assembly = Assembly.GetEntryAssembly();
                return (assembly != null) ? assembly.GetName().Name : String.Empty;
            }
        }

        /// <summary>
        ///     Gets the <em>file version</em> of the currently executing
        ///     assembly.
        /// </summary>
        /// <remarks>
        ///     <see cref="UpdateCheck"/> uses <see cref="AssemblyVersion"/> as
        ///     the "current version" when looking for updates.
        /// </remarks>
        public Version AssemblyVersion {
            get {
                var assembly = Assembly.GetEntryAssembly();
                if (assembly != null) {
                    var attribute = assembly
                        .GetCustomAttributes(typeof(AssemblyFileVersionAttribute), true)
                        .Cast<AssemblyFileVersionAttribute>()
                        .SingleOrDefault();
                    return (attribute != null) ? Version.Parse(attribute.Version) : null;
                } else {
                    return null;
                }
            }
        }

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

        /// <summary>
        ///     Gets or sets the instance of <see cref="ISynchronizeInvoke"/>
        ///     to use when raising events.
        /// </summary>
        [DefaultValue(null)]
        public ISynchronizeInvoke SynchronizationObject { get; set; }

        /// <summary>
        ///     Starts checking for updates.  Begins a background check
        ///     immediately.
        /// </summary>
        public void Start() {
            if (!this.timer.Enabled) {
                ThreadPool.QueueUserWorkItem((state) => this.timer_Elapsed(null, null));
                this.timer.Start();
            }
        }

        /// <summary>
        ///     Terminates checking for updates.
        /// </summary>
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

        /// <summary>
        ///     Called periodically by <see cref="timer"/> to download
        ///     the version list and check for updates.
        /// </summary>
        private void timer_Elapsed(object sender, ElapsedEventArgs e) {
            var requestUri = this.CheckUri;
            if (String.IsNullOrEmpty(requestUri)) {
                return;
            }

            // Grab the assembly information
            var assemblyName = this.AssemblyName;
            var assemblyVersion = this.AssemblyVersion;
            if (assemblyName == null || assemblyVersion == null) {
                return;
            }

            try {
                // Figure out what the server has available
                var doc = XDocument.Load(requestUri);
                var newAssembly = doc.Root
                    .Elements("assembly")
                    .Select(x => new {
                        Name = x.Attribute("name"),
                        Version = x.Attribute("version"),
                        UpdateUri = x.Attribute("update-uri")
                    })
                    .Where(x => (x.Name != null)
                        && (x.Name.Value == assemblyName)
                        && (x.Version != null))
                    .Select(x => new {
                        Version = new Version(x.Version.Value),
                        UpdateUri = (x.UpdateUri != null) ? x.UpdateUri.Value : null
                    })
                    .OrderBy(x => x.Version)
                    .Last();

                // Check if we have an update
                if (newAssembly.Version > assemblyVersion) {
                    var handler = this.UpdateAvailable;
                    if (handler != null) {
                        var sync = this.SynchronizationObject;
                        var args = new UpdateEventArgs(
                            assemblyVersion,
                            newAssembly.Version,
                            newAssembly.UpdateUri);
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
                // Our assembly isn't listed
            }
        }
    }
}
