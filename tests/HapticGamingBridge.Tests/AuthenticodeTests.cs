using HapticGamingBridge;
using Xunit;

namespace HapticGamingBridge.Tests
{
    // Uses real files and the real Windows signature check. dotnet.exe is signed by Microsoft with an embedded signature.
    public class AuthenticodeTests : IDisposable
    {
        private static readonly string Dotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "hb-sig-" + Guid.NewGuid().ToString("N"));
        public AuthenticodeTests() => Directory.CreateDirectory(_dir);
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        [SkippableFact]
        public void A_file_signed_by_microsoft_is_valid()
        {
            Skip.IfNot(File.Exists(Dotnet), "dotnet.exe not found");
            Assert.Equal(SignatureStatus.Valid, Authenticode.Verify(Dotnet));
            Assert.Contains("Microsoft", Authenticode.SignerSubject(Dotnet));
        }

        [Fact]
        public void An_unsigned_file_is_reported_as_unsigned()
        {
            var f = Path.Combine(_dir, "plain.exe");
            File.WriteAllBytes(f, new byte[200_000]);
            Assert.Equal(SignatureStatus.NotSigned, Authenticode.Verify(f));
            Assert.Equal("", Authenticode.SignerSubject(f));
        }

        [SkippableFact]
        public void A_signed_file_that_was_changed_afterwards_is_invalid()
        {
            Skip.IfNot(File.Exists(Dotnet), "dotnet.exe not found");
            var copy = Path.Combine(_dir, "dotnet-tampered.exe");
            File.Copy(Dotnet, copy);
            var bytes = File.ReadAllBytes(copy);
            bytes[bytes.Length / 3] ^= 0xFF;                      // somewhere in the signed part of the file
            File.WriteAllBytes(copy, bytes);
            Assert.Equal(SignatureStatus.Invalid, Authenticode.Verify(copy));
        }

        [Fact]
        public void A_missing_file_is_not_valid()
            => Assert.NotEqual(SignatureStatus.Valid, Authenticode.Verify(Path.Combine(_dir, "does-not-exist.exe")));

        [Fact]
        public void The_apps_own_unsigned_build_is_unsigned()
            => Assert.Equal(SignatureStatus.NotSigned, Authenticode.Verify(typeof(Engine).Assembly.Location));
    }
}
