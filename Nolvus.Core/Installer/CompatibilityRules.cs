using System;
using System.Collections.Generic;
using System.IO;

namespace NKNOVA.Installer.Core
{
    public sealed class IncompatibleShaderConfigurationException : InvalidOperationException
    {
        public IncompatibleShaderConfigurationException(string message) : base(message) { }
    }

    public sealed record DiskSpaceCheckResult(bool Sufficient, long AvailableBytes, long RequiredBytes)
    {
        public string FormattedAvailable => $"{AvailableBytes / (1024.0 * 1024 * 1024):F1} GiB";
        public string FormattedRequired => $"{RequiredBytes / (1024.0 * 1024 * 1024):F1} GiB";
    }

    public static class CompatibilityRules
    {
        public static readonly string[] SupportedEnbPresets = new[]
        {
            "cabbage",
            "kauz",
            "picho",
            "amon"
        };

        /// <summary>
        /// Validates that ENB and Community Shaders are never enabled simultaneously.
        /// Throws an IncompatibleShaderConfigurationException if both are configured.
        /// </summary>
        public static void ValidateShaderMutualExclusivity(string shaderFramework, string? enbPreset)
        {
            var framework = shaderFramework?.Trim().ToLowerInvariant() ?? "";

            if (framework == "enb")
            {
                if (string.IsNullOrWhiteSpace(enbPreset))
                {
                    throw new IncompatibleShaderConfigurationException("An ENB preset must be selected when the ENB framework is chosen.");
                }

                bool supported = false;
                foreach (var p in SupportedEnbPresets)
                {
                    if (string.Equals(p, enbPreset.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        supported = true;
                        break;
                    }
                }

                if (!supported)
                {
                    throw new IncompatibleShaderConfigurationException($"Unsupported ENB preset '{enbPreset}'. Supported presets are: {string.Join(", ", SupportedEnbPresets)}");
                }
            }
            else if (framework == "community-shaders" || framework == "cs")
            {
                if (!string.IsNullOrWhiteSpace(enbPreset))
                {
                    throw new IncompatibleShaderConfigurationException("Community Shaders and ENB presets are mutually exclusive. ENB presets cannot be combined with Community Shaders.");
                }
            }
            else if (!string.IsNullOrEmpty(framework) && framework != "none" && framework != "vanilla")
            {
                throw new IncompatibleShaderConfigurationException($"Unknown shader framework '{shaderFramework}'. Must be 'enb', 'community-shaders', 'vanilla', or 'none'.");
            }
        }

        /// <summary>
        /// Checks if the destination volume has enough free bytes for the required installation size plus safety margin.
        /// </summary>
        public static DiskSpaceCheckResult CheckDiskSpace(string destinationPath, long requiredBytes, long safetyMarginBytes = 5L * 1024 * 1024 * 1024)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

            long totalRequired = requiredBytes + safetyMarginBytes;
            long available = 0;

            try
            {
                var full = Path.GetFullPath(destinationPath);
                var root = Path.GetPathRoot(full) ?? full;
                var drive = new DriveInfo(root);
                available = drive.AvailableFreeSpace;
            }
            catch (Exception)
            {
                available = 0;
            }

            return new DiskSpaceCheckResult(available >= totalRequired, available, totalRequired);
        }
    }
}
