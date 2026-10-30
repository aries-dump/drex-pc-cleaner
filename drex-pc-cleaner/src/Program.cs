namespace Drex;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            var mutex = new Mutex(true, "DREX_PC_CLEANER_SINGLE", out bool first);
            if (!first)
            {
                if (args.Contains("--elevated"))
                {
                    try { first = mutex.WaitOne(8000); } catch (AbandonedMutexException) { first = true; }
                }
                if (!first)
                {
                    MessageBox.Show("DREX PC CLEANER is already running.\nCheck Task Manager if you cannot see it.", "DREX PC CLEANER");
                    return;
                }
            }
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Report(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }
        catch (Exception ex) { Report(ex); }
    }

    static void Report(Exception? ex)
    {
        string m = ex?.ToString() ?? "Unknown error";
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "DREX_crash.txt"), m); } catch { }
        MessageBox.Show(m, "DREX PC CLEANER - error");
    }
}
