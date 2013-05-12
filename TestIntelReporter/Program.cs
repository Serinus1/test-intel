using System;
using System.Threading;
using System.Windows.Forms;

namespace TestIntelReporter {
    static class Program {
        /// <summary>
        ///     Global "application name" to maintain single instance
        ///     operation.
        /// </summary>
        internal const string mutexName = "{8E5BA95B-79CC-4F9A-9CF1-88297BD8FEA7}";

        /// <summary>
        ///     The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            bool createdMutex;

            var updateCheck = new UpdateCheck();
            updateCheck.CheckUri = "http://minecraft.etherealwake.com/updates.xml";
            updateCheck.Start();

            using (var mutex = new Mutex(true, mutexName, out createdMutex)) {
                if (createdMutex) {
                    // Created the mutex, so we are the first instance
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var mainform = new MainForm();
                    Application.Run(mainform);
                    GC.KeepAlive(mutex);
                } else {
                    // Not the first instance, so send a signal to wake up the other
                    try {
                        var message = NativeMethods.RegisterWindowMessage(mutexName);
                        if (message != 0) {
                            NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST,
                                message, IntPtr.Zero, IntPtr.Zero);
                        }
                    } catch {
                        // We really don't care, we're quitting anyway
                    }
                }
            }
        }
    }
}
