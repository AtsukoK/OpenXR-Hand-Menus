namespace OpenXRHandMenusEditor;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--tourshots") { MainForm.TourShots(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--shots") { MainForm.Shots(args[1]); return 0; }
        if (args.Length == 2 && args[0] == "--selftest") return MainForm.SelfTest(args[1]);
        if (args.Length == 2 && args[0] == "--updatecheck") // writes this version and the latest GitHub release to a file
        {
            var r = MainForm.LatestReleaseAsync().GetAwaiter().GetResult();
            File.WriteAllText(args[1], "this=" + MainForm.AppVersion.ToString(3) + " latest=" + (r == null ? "unknown" : r.Value.tag) +
                " update=" + (r != null && r.Value.ver > MainForm.AppVersion));
            return 0;
        }
        Application.Run(new MainForm());
        return 0;
    }
}
