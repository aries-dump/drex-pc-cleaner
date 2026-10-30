namespace Drex;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, "DREX_PC_CLEANER_SINGLE", out bool first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
