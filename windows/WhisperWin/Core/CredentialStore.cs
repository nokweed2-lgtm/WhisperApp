using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WhisperWin.Core
{
    /// <summary>
    /// Stores the Groq API key in Windows Credential Manager via advapi32 CredRead/CredWrite/
    /// CredDelete P/Invoke — never written to disk as plain text, and no third-party package
    /// needed for something this small.
    /// </summary>
    public sealed class CredentialStore
    {
        private const string DefaultTargetName = "WhisperWin:GroqApiKey";
        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;

        private readonly string _targetName;

        public CredentialStore(string? targetName = null)
        {
            _targetName = targetName ?? DefaultTargetName;
        }

        public string? ReadApiKey()
        {
            if (!CredRead(_targetName, CRED_TYPE_GENERIC, 0, out var credPtr))
            {
                return null;
            }

            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                {
                    return null;
                }

                var bytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(credPtr);
            }
        }

        public void SaveApiKey(string apiKey)
        {
            if (apiKey == null)
            {
                throw new ArgumentNullException(nameof(apiKey));
            }

            var bytes = Encoding.Unicode.GetBytes(apiKey);
            var blobPtr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blobPtr, bytes.Length);

                var cred = new CREDENTIAL
                {
                    Flags = 0,
                    Type = CRED_TYPE_GENERIC,
                    TargetName = _targetName,
                    Comment = "Whisper for Windows — Groq API key",
                    LastWritten = default,
                    CredentialBlobSize = bytes.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = null,
                    UserName = Environment.UserName,
                };

                if (!CredWrite(ref cred, 0))
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to save credential (Win32 error {error}).");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
            }
        }

        public void DeleteApiKey()
        {
            CredDelete(_targetName, CRED_TYPE_GENERIC, 0);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite(ref CREDENTIAL userCredential, int flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr cred);
    }
}
