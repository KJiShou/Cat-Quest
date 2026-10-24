using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public interface IChatGptStorage
{
    string Read(string name, bool encrypted);
    void Write(string name, string json, bool encrypted);
}

// Separate from player data. DPAPI binds encrypted records to the current Windows user.
public sealed class ChatGptStorage : IChatGptStorage
{
    readonly string directory;
    public ChatGptStorage(string directory) { this.directory = directory; }
    string PathFor(string name)
    {
        if (name != "host.json" && name != "preferences.json" && name != "connection.dpapi" && name != "chat.dpapi") throw new ArgumentException("Unknown AI record.");
        return Path.Combine(directory, name);
    }
    public string Read(string name, bool encrypted)
    {
        string path = PathFor(name); if (!File.Exists(path)) return null;
        byte[] bytes = File.ReadAllBytes(path), plain = null;
        try { plain = encrypted ? Protect(bytes, false) : bytes; return Encoding.UTF8.GetString(plain); }
        finally { Array.Clear(bytes, 0, bytes.Length); if (plain != null) Array.Clear(plain, 0, plain.Length); }
    }
    public void Write(string name, string json, bool encrypted)
    {
        Directory.CreateDirectory(directory);
        string path = PathFor(name), temporary = path + ".tmp";
        byte[] plain = Encoding.UTF8.GetBytes(json), bytes = null;
        try
        {
            bytes = encrypted ? Protect(plain, true) : plain;
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally
        {
            Array.Clear(plain, 0, plain.Length); if (bytes != null) Array.Clear(bytes, 0, bytes.Length);
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int size; public IntPtr data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static byte[] Protect(byte[] bytes, bool encrypt)
    {
        var input = new Blob { size = bytes.Length, data = Marshal.AllocHGlobal(bytes.Length) };
        var output = new Blob();
        try
        {
            Marshal.Copy(bytes, 0, input.data, bytes.Length);
            bool ok = encrypt ? CryptProtectData(ref input, "Cat Quest ChatGPT", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new IOException("Windows could not protect or restore the AI connection.");
            var result = new byte[output.size]; Marshal.Copy(output.data, result, 0, result.Length); return result;
        }
        finally
        {
            for (int i = 0; i < input.size; i++) Marshal.WriteByte(input.data, i, 0);
            Marshal.FreeHGlobal(input.data);
            if (output.data != IntPtr.Zero) { for (int i = 0; i < output.size; i++) Marshal.WriteByte(output.data, i, 0); LocalFree(output.data); }
        }
    }
}
