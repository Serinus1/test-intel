using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PleaseIgnore.IntelMap;

namespace TestIntelReporter {
    public partial class MessageView : Control {
        private const int maxMessages = 100;
        private readonly Queue<MessageData> messages
            = new Queue<MessageData>(maxMessages);

        public MessageView() {
            this.DoubleBuffered = true;
        }

        public void PushMessage(IntelEventArgs message) {
            if (messages.Count > maxMessages) {
                messages.Dequeue();
            }
            messages.Enqueue(new MessageData {
                Message = message
            });
            this.Invalidate();
        }

        protected override void OnResize(EventArgs e) {
            // Will need to lay it out again
            foreach (var message in messages) {
                message.Lines = null;
            }
            base.OnResize(e);
        }

        protected override void OnFontChanged(EventArgs e) {
            // Will need to lay it out again
            foreach (var message in messages) {
                message.Lines = null;
            }
            base.OnFontChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e) {
            var rect = this.ClientRectangle;
            float height = rect.Bottom;
            using (var brush = new SolidBrush(this.ForeColor)) {
                foreach (var message in this.messages.Reverse()) {
                    // Check if we need to layout the line
                    if (message.Lines == null) {
                        // Compute the initial string
                        var str = String.Format(
                            Application.CurrentCulture,
                            "[{0:HH:mm:ss}] {1}",
                            message.Message.Timestamp,
                            message.Message.Message);
                        var size = e.Graphics.MeasureString(str, this.Font);
                        // TODO: Word wrap, etc.
                        // Store the finished string
                        message.Lines = str;
                        message.Height = size.Height;
                    }

                    // Render the text
                    height -= message.Height;
                    e.Graphics.DrawString(message.Lines, this.Font, brush, 0, height);
                    // Cease rendering once we get atop the screen
                    if (height <= 0) {
                        break;
                    }
                }
            }

            base.OnPaint(e);
        }

        private class MessageData {
            public IntelEventArgs Message;

            public string Lines;

            public float Height;
        }
    }
}
