using System;
using System.Threading;
using System.Windows.Forms;

namespace TestIntelReporter {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            bool createdMutex;
            using (var mutex = new Mutex(true, "{8E5BA95B-79CC-4F9A-9CF1-88297BD8FEA7}", out createdMutex)) {
                if (createdMutex) {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    var mainform = new MainForm();
                    Application.Run();
                    GC.KeepAlive(mutex);
                }
            }
        }
    }
}
