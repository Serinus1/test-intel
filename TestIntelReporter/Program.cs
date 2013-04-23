using System;
using System.Threading;
using System.Windows.Forms;

namespace TestIntelReporter {
    static class Program {
        private const string mutexName = "{8E5BA95B-79CC-4F9A-9CF1-88297BD8FEA7}";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var mainform = new MainForm();
            Application.Run(mainform);
        }
    }
}
