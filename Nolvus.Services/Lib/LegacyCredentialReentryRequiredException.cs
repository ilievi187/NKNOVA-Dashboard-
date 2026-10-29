using System.Security.Cryptography;

namespace Nolvus.Services.Lib
{
    /// <summary>
    /// Signals that a stored credential predates authenticated user-scoped storage and must be re-entered.
    /// The old fixed-key format is intentionally not decrypted.
    /// </summary>
    public sealed class LegacyCredentialReentryRequiredException : CryptographicException
    {
        public LegacyCredentialReentryRequiredException()
            : base("This saved credential uses an unsupported legacy format. Re-enter it and save again; existing settings were not changed.")
        {
        }
    }
}
