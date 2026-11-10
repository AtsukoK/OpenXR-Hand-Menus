using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace OpenXRHandMenusEditor;

// Asks GitHub once per start whether a newer release exists. Only reads the public
// "latest release" record; nothing is sent about the user and nothing is downloaded.
// Turn it off with the "Check for updates" option (a flag file in the settings folder).
public partial class MainForm
{
    const string RepoApi = "https://api.github.com/repos/AtsukoK/OpenXR-Hand-Menus/releases/latest";
    const string ReleasesPage = "https://github.com/AtsukoK/OpenXR-Hand-Menus/releases/latest";
    static string NoUpdateFlag => Path.Combine(DataDir, "no_update_check");
    public static bool UpdateCheckOn
    {
        get => !File.Exists(NoUpdateFlag);
        set { try { if (value) File.Delete(NoUpdateFlag); else { Directory.CreateDirectory(DataDir); File.WriteAllText(NoUpdateFlag, "off"); } } catch { } }
    }

    readonly ToolStripButton updateBtn = new() { Visible = false, Alignment = ToolStripItemAlignment.Right };

    public static Version AppVersion
    {
        get
        {
            var s = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            s = s.Split('+', '-')[0];
            return Version.TryParse(s, out var v) ? v : new Version(0, 0, 0);
        }
    }

    void AddUpdateButton(ToolStrip ts)
    {
        updateBtn.BackColor = Color.FromArgb(240, 140, 30);
        updateBtn.ForeColor = Color.White;
        updateBtn.Font = new Font(Font, FontStyle.Bold);
        updateBtn.Click += (s, e) => { try { Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true }); } catch { } };
        ts.Items.Add(updateBtn);
        if (Environment.GetCommandLineArgs().Length > 1 || !UpdateCheckOn) return; // tests and screenshots never go online
        Shown += async (s, e) =>
        {
            var latest = await LatestReleaseAsync();
            if (latest == null || IsDisposed) return;
            var (ver, tag) = latest.Value;
            if (ver <= AppVersion) return;
            updateBtn.Text = "Update available: " + tag;
            updateBtn.ToolTipText = "You have v" + AppVersion.ToString(3) + ". Click to open the download page.";
            updateBtn.Visible = true;
            SetStatus("A new version (" + tag + ") is available: click the orange button at the top right to download it.", false);
        };
    }

    // Returns the newest release version, or null when offline or anything goes wrong.
    public static async Task<(Version ver, string tag)?> LatestReleaseAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenXRHandMenus-Editor/" + AppVersion.ToString(3));
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(RepoApi));
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            return Version.TryParse(tag.TrimStart('v', 'V').Split('-')[0], out var v) ? (v, tag) : null;
        }
        catch { return null; }
    }
}
