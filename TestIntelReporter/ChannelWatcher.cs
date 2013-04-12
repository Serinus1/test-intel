using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;

namespace TestIntelReporter {
    public sealed class ChannelWatcher : IDisposable {
        private StreamReader reader;
        private DateTime lastMessage;
        private string filename;

        private static readonly Regex Parser = new Regex(
            @"\[\s*(\d{4})\.(\d{2})\.(\d{2})\s+(\d{2}):(\d{2}):(\d{2})\s*\](.*)$",
            RegexOptions.CultureInvariant);

        private static readonly TimeSpan Recheck = TimeSpan.FromMinutes(5);

        public ChannelWatcher(string channelName) {
            if (channelName == null) throw new ArgumentNullException("channelName");
            this.ChannelName = channelName;
        }

        public string ChannelName { get; private set; }

        public string LogDirectory { get; set; }

        public void Dispose() {
            if (reader != null) {
                reader.Dispose();
                reader = null;
            }
        }

        public void Tick() {
            try {
                if (reader != null) {
                    for (var line = reader.ReadLine(); line != null;
                            line = reader.ReadLine()) {
                        var match = Parser.Match(line.Trim());
                        if (!match.Success) {
                            continue;
                        }

                        var timestamp = new DateTime(
                            int.Parse(match.Groups[1].Value),
                            int.Parse(match.Groups[2].Value),
                            int.Parse(match.Groups[3].Value),
                            int.Parse(match.Groups[4].Value),
                            int.Parse(match.Groups[5].Value),
                            int.Parse(match.Groups[6].Value),
                            DateTimeKind.Utc);
                        lastMessage = timestamp;

                        var args = new IntelEventArgs {
                            Timestamp = timestamp,
                            Message = match.Groups[7].Value
                        };

                        var handler = Message;
                        if (handler != null) {
                            handler(this, args);
                        }
                    }
                }
            } catch (IOException) {
                reader.Close();
                reader = null;
            }

            try {
                var now = DateTime.UtcNow;
                if ((now - lastMessage) > Recheck) {
                    var dir = new DirectoryInfo(LogDirectory);
                    var recent = dir.EnumerateFiles(ChannelName + "_*.txt")
                        .OrderByDescending(x => x.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if ((recent != null) && ((reader == null) || (filename != recent.FullName))) {
                        if (reader != null) {
                            reader.Close();
                            reader = null;
                        }

                        var stream = new FileStream(recent.FullName, FileMode.Open,
                            FileAccess.Read, FileShare.ReadWrite);
                        reader = new StreamReader(stream, true);
                        filename = recent.FullName;

                        stream.Seek(0, SeekOrigin.End);
                        reader.DiscardBufferedData();
                        lastMessage = now;
                    }
                }
            } catch (IOException) {
                reader = null;
            }
        }

        public event EventHandler<IntelEventArgs> Message;
    }

    [Serializable]
    public sealed class IntelEventArgs : EventArgs {
        public DateTime Timestamp { get; set; }

        public string Message { get; set; }
    }
}
