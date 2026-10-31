using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace HapticGamingBridge
{
    public enum SignatureStatus { Valid, NotSigned, Invalid }

    // Checks the Authenticode signature of a file with the same Windows call that Explorer and SmartScreen rely on.
    public static class Authenticode
    {
        private const uint NoSignature = 0x800B0100;          // TRUST_E_NOSIGNATURE
        private const uint SubjectFormUnknown = 0x800B0003;   // TRUST_E_SUBJECT_FORM_UNKNOWN
        private const uint ProviderUnknown = 0x800B0001;      // TRUST_E_PROVIDER_UNKNOWN

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustFileInfo
        {
            public uint cbStruct;
            public IntPtr pcwszFilePath;
            public IntPtr hFile;
            public IntPtr pgKnownSubject;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WintrustData
        {
            public uint cbStruct;
            public IntPtr pPolicyCallbackData;
            public IntPtr pSIPClientData;
            public uint dwUIChoice;
            public uint fdwRevocationChecks;
            public uint dwUnionChoice;
            public IntPtr pFile;
            public uint dwStateAction;
            public IntPtr hWVTStateData;
            public IntPtr pwszURLReference;
            public uint dwProvFlags;
            public uint dwUIContext;
            public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false)]
        private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WintrustData data);

        private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

        public static SignatureStatus Verify(string path)
        {
            var pathPtr = Marshal.StringToHGlobalUni(path);
            var filePtr = IntPtr.Zero;
            try
            {
                var file = new WintrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(), pcwszFilePath = pathPtr };
                filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
                Marshal.StructureToPtr(file, filePtr, false);

                var data = new WintrustData
                {
                    cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                    dwUIChoice = 2,              // WTD_UI_NONE
                    fdwRevocationChecks = 0,     // WTD_REVOKE_NONE: no network lookups from here
                    dwUnionChoice = 1,           // WTD_CHOICE_FILE
                    pFile = filePtr,
                    dwStateAction = 0,           // WTD_STATEACTION_IGNORE
                    dwProvFlags = 0x10           // WTD_CACHE_ONLY_URL_RETRIEVAL
                };
                var result = WinVerifyTrust(new IntPtr(-1), GenericVerifyV2, ref data);
                if (result == 0) return SignatureStatus.Valid;
                if (result is NoSignature or SubjectFormUnknown or ProviderUnknown) return SignatureStatus.NotSigned;
                return SignatureStatus.Invalid;
            }
            finally
            {
                if (filePtr != IntPtr.Zero) Marshal.FreeHGlobal(filePtr);
                Marshal.FreeHGlobal(pathPtr);
            }
        }

        // The subject of the certificate that signed the file ("CN=..., O=..."), or "" when it cannot be read.
        public static string SignerSubject(string path)
        {
            try { return new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).Subject; }
            catch { return ""; }
        }
    }
}
