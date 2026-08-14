using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using FO4RecordEditor.Services.Archives;
using Microsoft.Win32;
using Xunit;

namespace FO4RecordEditor.Tests;

public sealed class ArchiveWindowsAclReviewTests
{
    // Wine reports IsWindows()==true, but its ACL layer can block indefinitely on
    // GetAccessControl, and these tests assert real-Windows DACL semantics anyway.
    private static bool IsRealWindows() =>
        OperatingSystem.IsWindows()
        && Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wine") is null;

    [Fact]
    public void ExplicitWrite_PreservesAnExistingProtectedWindowsDacl()
    {
        if (!IsRealWindows()) return;

        var root = Path.Combine(
            Path.GetTempPath(),
            $"ArchiveAclReview_{Guid.NewGuid():N}");
        var destination = Path.Combine(root, "Thing.nif");
        Directory.CreateDirectory(root);
        File.WriteAllText(destination, "old payload");

        try
        {
            var identity = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("The current Windows identity has no SID.");
            var file = new FileInfo(destination);
            var security = FileSystemAclExtensions.GetAccessControl(
                file,
                AccessControlSections.Access);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                InheritanceFlags.None,
                PropagationFlags.None,
                AccessControlType.Allow));
            FileSystemAclExtensions.SetAccessControl(file, security);

            var before = FileSystemAclExtensions.GetAccessControl(
                    new FileInfo(destination),
                    AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);

            ArchiveExtraction.TryWriteExplicitFile(
                    destination,
                    "new payload"u8.ToArray(),
                    out var error)
                .Should().BeTrue(error);

            var after = FileSystemAclExtensions.GetAccessControl(
                    new FileInfo(destination),
                    AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            after.Should().Be(before,
                "Windows replacement must retain the existing file's protected DACL");
            File.ReadAllText(destination).Should().Be("new payload");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch { }
        }
    }
}
