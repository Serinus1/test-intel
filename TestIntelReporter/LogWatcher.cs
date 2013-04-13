using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.IO;

namespace TestIntelReporter {
    public sealed class LogWatcher : Component {
        private List<ChannelWatcher> watchers;
        private volatile string logDirectory;
        private string[] channelList;
        private Thread thread;
        private volatile bool running;
        private AutoResetEvent signal;
        private IntelSession session;
        private DateTime lastMessage;
        private volatile string username;
        private volatile string password;
        private DateTime lastKeepAlive;
        private volatile bool loginRejected;

        public readonly static string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "EVE",
            "logs",
            "ChatLogs");

        private static readonly TimeSpan KeepAlivePeriod
            = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan IdlePeriod
            = TimeSpan.FromMinutes(10);

        public LogWatcher() {
            signal = new AutoResetEvent(false);
            logDirectory = DefaultPath;
        }

        public LogWatcher(IContainer container) : this() {
            if (container != null) container.Add(this);
        }

        public int Users {
            get {
                return (session == null) ? 0 : session.Users;
            }
        }

        public int IntelReported { get; private set; }

        public bool IsRunning { get { return (thread != null); } }

        public bool IsConnected { get { return (session != null); } }

        public bool BadPassword { get { return loginRejected; } }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Username {
            get { return username; }
            set {
                this.username = value;
                loginRejected = false;
                signal.Set();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string PasswordHash {
            set {
                this.password = value;
                loginRejected = false;
                signal.Set();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string Password {
            set {
                if (value == null) throw new ArgumentNullException("value");
                PasswordHash = IntelSession.HashPassword(value);
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string LogDirectory {
            get { return logDirectory; }
            set {
                logDirectory = value;
                signal.Set();
            }
        }

        public void Start() {
            if (!running) {
                thread = new Thread(this.ThreadStart);
                running = true;
                thread.Start();
            }
        }

        public void Stop() {
            if (running) {
                running = false;
                signal.Set();
                thread.Join();
                thread = null;
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                Stop();
                if (watchers != null) {
                    watchers.ForEach(x => x.Dispose());
                    watchers = null;
                }
            }
            base.Dispose(disposing);
        }

        private void ThreadStart() {
            onStatusChanged(EventArgs.Empty);

            while (running) {
                // Make sure we have the channel list
                if (channelList == null) {
                    try {
                        channelList = WebMethods.GetChannelList();
                        watchers = channelList.Select(x => new ChannelWatcher(x) {
                            LogDirectory = logDirectory
                        }).ToList();
                        watchers.ForEach(x => x.Message += watchers_message);
                    } catch (IntelException) {
                        channelList = null;
                        signal.WaitOne(TimeSpan.FromMinutes(5));
                        continue;
                    }
                }

                // Ping all the file system watchers
                watchers.ForEach(x => x.LogDirectory = logDirectory);
                watchers.ForEach(x => x.Tick());

                // Check the various time outs
                var now = DateTime.UtcNow;
                if ((session != null) && (now - lastMessage > IdlePeriod)) {
                    // Disconnect from the server when we aren't doing anything
                    session.Dispose();
                    session = null;
                } else if ((session != null) && (now - lastKeepAlive > KeepAlivePeriod)) {
                    // Maintain the session by pinging every minute or so
                    if (session.KeepAlive()) {
                        lastKeepAlive = now;
                        onStatusChanged(EventArgs.Empty);
                    } else if (!session.IsOpen) {
                        // Session expired (or something)
                        session.Dispose();
                        session = null;
                        onStatusChanged(EventArgs.Empty);
                    } else {
                        // Error contacting the server, try again in a few minutes
                        lastKeepAlive += TimeSpan.FromMinutes(1);
                    }
                }

                // TODO: Use a filesystem watcher object
                signal.WaitOne(TimeSpan.FromSeconds(5));
            }

            if (session != null) {
                session.Dispose();
                session = null;
            }
        }

        private void watchers_message(object sender, IntelEventArgs e) {
            // Make sure we have a session ready to accept the data
            if (session == null) {
                try {
                    if (loginRejected)
                        return;
                    session = new IntelSession(username, password);
                    onStatusChanged(EventArgs.Empty);
                } catch (IntelAuthorizationException) {
                    // Server rejected our authentication
                    loginRejected = true;
                    onStatusChanged(EventArgs.Empty);
                    return;
                } catch (IntelException) {
                    // Something happened, try again later
                    return;
                }
            }

            // Submit the Intel report
            if (session.Report(e.Timestamp,
                    ((ChannelWatcher)sender).ChannelName,
                    e.Message)) {
                lastMessage = e.Timestamp;
                ++IntelReported;
                onStatusChanged(EventArgs.Empty);
            } else if (!session.IsOpen) {
                session.Dispose();
                session = null;
                onStatusChanged(EventArgs.Empty);
            }
        }

        private void onStatusChanged(EventArgs e) {
            var handler = StatusChanged;
            if (handler != null) {
                handler(this, e);
            }
        }

        public event EventHandler StatusChanged;
    }
}
