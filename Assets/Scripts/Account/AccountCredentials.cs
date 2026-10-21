using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class AccountCredential
{
    public string uid, refreshToken, email;
}

// Windows encrypts generic credential blobs for the current Windows user. Never use PlayerPrefs.
public static class AccountCredentials
{
    public static string Target(string projectId, string savePath)
    {
        using var hash = SHA256.Create();
        return "CatQuest/Firebase/" + projectId + "/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(savePath))).Replace("-", "");
    }

    public static AccountCredential Read(string target)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        if (!CredRead(target, 1, 0, out IntPtr pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.blobSize == 0 || credential.blobSize > 5120) return null;
            var bytes = new byte[credential.blobSize]; Marshal.Copy(credential.blob, bytes, 0, bytes.Length);
            var result = JsonUtility.FromJson<AccountCredential>(Encoding.Unicode.GetString(bytes));
            Array.Clear(bytes, 0, bytes.Length);
            return string.IsNullOrEmpty(result?.uid) || string.IsNullOrEmpty(result.refreshToken) ? null : result;
        }
        catch (ArgumentException) { return null; }
        finally { CredFree(pointer); }
#else
        return null;
#endif
    }

    public static bool Write(string target, AccountCredential value)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        byte[] bytes = Encoding.Unicode.GetBytes(JsonUtility.ToJson(value));
        if (bytes.Length > 5120) { Array.Clear(bytes, 0, bytes.Length); return false; }
        IntPtr memory = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, memory, bytes.Length);
            var credential = new NativeCredential { type = 1, targetName = target, userName = value.uid, persist = 2,
                blobSize = (uint)bytes.Length, blob = memory };
            return CredWrite(ref credential, 0);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length); Marshal.Copy(bytes, 0, memory, bytes.Length); Marshal.FreeCoTaskMem(memory);
        }
#else
        return false;
#endif
    }

    public static bool Delete(string target)
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        return CredDelete(target, 1, 0) || Marshal.GetLastWin32Error() == 1168;
#else
        return true;
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NativeCredential
    {
        public uint flags, type;
        public string targetName, comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME lastWritten;
        public uint blobSize;
        public IntPtr blob;
        public uint persist, attributeCount;
        public IntPtr attributes;
        public string targetAlias, userName;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CredWrite(ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] static extern bool CredDelete(string target, uint type, uint flags);
    [DllImport("advapi32.dll")] static extern void CredFree(IntPtr credential);
#endif
}
