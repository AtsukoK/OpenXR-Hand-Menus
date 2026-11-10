namespace VRRadialOverlayEditor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--tourshots") { MainForm.TourShots(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--shots") { MainForm.Shots(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--selftest") return MainForm.SelfTest(args[1]);
        Application.Run(new MainForm());
        return 0;
    }
}
