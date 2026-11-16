using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace SecureConfig.Editor.Core;

/// <summary>Writes the master key file (ACL: Administrators/SYSTEM full control, IIS_IUSRS read, optionally current user read)</summary>
public static class KeyFileWriter
{
    /// <summary>BUILTIN\IIS_IUSRS. The group every IIS app pool identity belongs to automatically (specified by SID so it works in any OS language)</summary>
    private static readonly SecurityIdentifier IisIusrs = new("S-1-5-32-568");

    /// <summary>
    /// Saves the key file. Disables inherited permissions and keeps only the following:
    /// Administrators/SYSTEM full control, IIS_IUSRS read, (optional) current user read.
    /// </summary>
    /// <param name="path">Key file path (the folder is created if missing)</param>
    /// <param name="base64Key">Key (Base64, 32 bytes)</param>
    /// <param name="grantCurrentUser">true on a dev PC where Visual Studio/IIS Express runs under your own account</param>
    public static void Write(string path, string base64Key, bool grantCurrentUser)
    {
        SecretFormat.ParseKey(base64Key); // format check
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(IisIusrs, FileSystemRights.Read, AccessControlType.Allow));
        if (grantCurrentUser)
        {
            var user = WindowsIdentity.GetCurrent().User;
            if (user != null)
                security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.Read, AccessControlType.Allow));
        }

        byte[] bytes = Encoding.ASCII.GetBytes(base64Key.Trim() + "\r\n");
        var info = new FileInfo(path);
        if (info.Exists)
        {
            // lock down permissions first, then replace the content
            info.SetAccessControl(security);
            File.WriteAllBytes(path, bytes);
        }
        else
        {
            // create with restricted permissions from the start (never exposed via inherited permissions, even briefly)
            using var fs = info.Create(FileMode.CreateNew, FileSystemRights.Write | FileSystemRights.ReadPermissions,
                FileShare.None, 4096, FileOptions.None, security);
            fs.Write(bytes, 0, bytes.Length);
        }
    }
}
