using System;
using System.Windows.Forms;

namespace TestIntelReporter {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            using (var watcher = new LogWatcher()) {
                watcher.LogDirectory = @"C:\Users\mcgee\Documents\EVE\logs\Chatlogs";
                watcher.Username = "testuser";
                watcher.Password = "blahblahblah";
                watcher.Start();

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
