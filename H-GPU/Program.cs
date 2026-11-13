using System;
using System.Security.Principal;
using System.Windows.Forms;

namespace HGPU
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // Extra safety net: the manifest already requires elevation,
            // but if someone launches the exe with UAC disabled, refuse to run.
            if (!IsAdministrator())
            {
                MessageBox.Show(
                    "H-GPU must run as Administrator to read hardware sensors.\n" +
                    "Please right-click the executable and choose \"Run as administrator\".",
                    "H-GPU - Administrator Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
