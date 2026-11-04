using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// The Windows player uses the system picker, including its overwrite prompt.
internal static class SaveFileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    sealed class OpenFileName
    {
        public int size;
        public IntPtr owner, instance;
        public string filter;
        public IntPtr customFilter;
        public int maxCustomFilter, filterIndex;
        public IntPtr file;
        public int maxFile;
        public IntPtr fileTitle;
        public int maxFileTitle;
        public string initialDirectory, title;
        public int flags;
        public short fileOffset, extensionOffset;
        public string defaultExtension;
        public IntPtr customData, hook, templateName, reserved;
        public int reservedValue, flagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] static extern bool GetOpenFileNameW([In, Out] OpenFileName dialog);
    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)] static extern bool GetSaveFileNameW([In, Out] OpenFileName dialog);
    [DllImport("comdlg32.dll")] static extern int CommDlgExtendedError();
    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr window, string message, string caption, uint flags);

    public static bool ConfirmImport(string path) =>
        MessageBoxW(GetActiveWindow(), $"Replace your local tasks, settings and progress with {Path.GetFileName(path)}? A recovery copy will be kept beside your save.", "Import Cat Quest backup", 0x134) == 6;

    public static string Pick(bool export)
    {
        const int capacity = 32768;
        IntPtr buffer = Marshal.AllocHGlobal(capacity * 2);
        try
        {
            var initial = (export ? "Cat Quest Backup.json\0" : "\0").ToCharArray();
            Marshal.Copy(initial, 0, buffer, initial.Length);
            var dialog = new OpenFileName {
                size = Marshal.SizeOf(typeof(OpenFileName)), owner = GetActiveWindow(),
                filter = "JSON backup (*.json)\0*.json\0\0", filterIndex = 1, file = buffer, maxFile = capacity,
                defaultExtension = "json", title = export ? "Export Cat Quest backup" : "Import Cat Quest backup",
                flags = 0x80000 | 0x800 | 8 | (export ? 2 : 0x1000)
            };
            bool selected = export ? GetSaveFileNameW(dialog) : GetOpenFileNameW(dialog);
            if (selected) return Marshal.PtrToStringUni(buffer);
            int error = CommDlgExtendedError();
            if (error != 0) throw new IOException($"The file picker could not open ({error}).");
            return null;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }
}
