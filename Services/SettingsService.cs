using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using UsSDAU.Models;

namespace UsSDAU.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root = ResolveRoot();
    private string SettingsPath => Path.Combine(_root, "settings.json");

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("USSDAU_DATA_DIR");
        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsSDAU")
            : Path.GetFullPath(overridden);
    }

    public AcademicSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AcademicSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new();
        }
        catch { }
        return new AcademicSettings();
    }

    public void Save(AcademicSettings settings, string password)
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        if (!string.IsNullOrWhiteSpace(settings.Account) && !string.IsNullOrWhiteSpace(password))
            CredentialVault.Write(settings.Account, password);
        ConfigureStartup(settings.StartWithWindows);
    }

    public string ReadPassword(string account) => CredentialVault.Read(account) ?? "";

    private static void ConfigureStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (enabled)
        {
            var executable = Environment.ProcessPath ?? "UsSDAU.exe";
            key.SetValue("UsSDAU", $"\"{executable}\" --tray");
        }
        else key.DeleteValue("UsSDAU", false);
    }
}

internal static class CredentialVault
{
    private const uint GenericCredential = 1;
    private const uint LocalMachinePersistence = 2;
    private static string Target(string account) => $"UsSDAU:Academic:{account}";

    public static void Write(string account, string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        var blob = Marshal.AllocCoTaskMem(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeCredential
            {
                Type = GenericCredential,
                TargetName = Target(account),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = LocalMachinePersistence,
                UserName = account
            };
            if (!CredWrite(ref credential, 0))
                throw new InvalidOperationException($"无法保存 Windows 凭据（{Marshal.GetLastWin32Error()}）");
        }
        finally { Marshal.FreeCoTaskMem(blob); }
    }

    public static string? Read(string account)
    {
        if (string.IsNullOrWhiteSpace(account) || !CredRead(Target(account), GenericCredential, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally { CredFree(pointer); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}
