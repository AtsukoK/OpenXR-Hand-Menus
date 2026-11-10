namespace OpenXRHandMenusEditor;

// The layer can change the profile too (adjust mode in the headset writes new positions).
// When the editor gets focus again, reload the file if it changed on disk.
public partial class MainForm
{
    DateTime knownWrite;
    bool askingReload;

    void NoteFileTime()
    {
        try { knownWrite = currentFile != null && File.Exists(currentFile) ? File.GetLastWriteTimeUtc(currentFile) : default; } catch { }
    }

    void CheckFileChangedOutside()
    {
        if (askingReload || currentFile == null || coach != null || !File.Exists(currentFile)) return;
        DateTime t;
        try { t = File.GetLastWriteTimeUtc(currentFile); } catch { return; }
        if (knownWrite == default || t == knownWrite) return;
        knownWrite = t;
        if (dirty)
        {
            askingReload = true;
            var r = MessageBox.Show(this, Path.GetFileName(currentFile) + " was changed outside the editor (for example by adjust mode in the headset).\n\n" +
                "Load the new version? Your unsaved changes here would be lost. Choose No to keep editing; saving will then overwrite the outside change.",
                "Profile changed", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            askingReload = false;
            if (r != DialogResult.Yes) return;
        }
        LoadFile(currentFile);
        SetStatus("Reloaded " + Path.GetFileName(currentFile) + ": it was changed outside the editor", false);
    }
}
