using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        try
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string script = Path.Combine(exeDir, "Rose_Setup.ps1");
            string log = Path.Combine(exeDir, "Rose_Launcher.log");

            if (!File.Exists(script))
            {
                MessageBox.Show(
                    "Rose_Setup.ps1 غير موجود بجانب البرنامج.\n\nفك الضغط بالكامل ثم شغّل Rose_Setup.exe من نفس المجلد.",
                    "Rose Cinematic Ultra",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe"),
                Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                WorkingDirectory = exeDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            var p = new Process { StartInfo = psi };
            var output = new StringBuilder();
            var errors = new StringBuilder();

            p.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) errors.AppendLine(e.Data); };

            if (!p.Start())
                throw new Exception("تعذر تشغيل Windows PowerShell.");

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();

            File.WriteAllText(
                log,
                "ExitCode: " + p.ExitCode + Environment.NewLine +
                "--- OUTPUT ---" + Environment.NewLine + output +
                "--- ERRORS ---" + Environment.NewLine + errors,
                new UTF8Encoding(true));

            if (p.ExitCode != 0)
            {
                string err = errors.ToString().Trim();
                if (err.Length > 1800) err = err.Substring(0, 1800);
                MessageBox.Show(
                    "Rose لم يبدأ بشكل صحيح.\n\n" +
                    (string.IsNullOrWhiteSpace(err) ? "راجع Rose_Launcher.log في نفس المجلد." : err) +
                    "\n\nLog: " + log,
                    "Rose Cinematic Ultra",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "تعذر تشغيل Rose:\n\n" + ex.Message,
                "Rose Cinematic Ultra",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}