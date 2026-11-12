using System;
using NKNOVA.Installer.Core;
using Xunit;

namespace Nolvus.Core.Tests.Installer
{
    public class CompatibilityRulesTests
    {
        [Theory]
        [InlineData("cabbage")]
        [InlineData("kauz")]
        [InlineData("picho")]
        [InlineData("amon")]
        public void Valid_enb_presets_pass_validation(string preset)
        {
            CompatibilityRules.ValidateShaderMutualExclusivity("enb", preset);
        }

        [Fact]
        public void Enb_without_preset_throws_IncompatibleShaderConfigurationException()
        {
            Assert.Throws<IncompatibleShaderConfigurationException>(() =>
                CompatibilityRules.ValidateShaderMutualExclusivity("enb", null));
        }

        [Fact]
        public void Unknown_enb_preset_throws_IncompatibleShaderConfigurationException()
        {
            Assert.Throws<IncompatibleShaderConfigurationException>(() =>
                CompatibilityRules.ValidateShaderMutualExclusivity("enb", "unsupported-preset-xyz"));
        }

        [Fact]
        public void Community_shaders_with_enb_preset_throws_IncompatibleShaderConfigurationException()
        {
            var ex = Assert.Throws<IncompatibleShaderConfigurationException>(() =>
                CompatibilityRules.ValidateShaderMutualExclusivity("community-shaders", "cabbage"));
            Assert.Contains("mutually exclusive", ex.Message);
        }

        [Fact]
        public void Community_shaders_without_preset_passes()
        {
            CompatibilityRules.ValidateShaderMutualExclusivity("community-shaders", null);
        }

        [Fact]
        public void Disk_space_check_calculates_sufficient_and_insufficient_correctly()
        {
            var tempDir = System.IO.Path.GetTempPath();
            // Checking for 1 byte required space should succeed on any development machine
            var resSmall = CompatibilityRules.CheckDiskSpace(tempDir, 1024, safetyMarginBytes: 1024);
            Assert.True(resSmall.Sufficient);
            Assert.True(resSmall.AvailableBytes > 0);

            // Checking for 500 Terabytes should fail
            var resHuge = CompatibilityRules.CheckDiskSpace(tempDir, 500L * 1024 * 1024 * 1024 * 1024);
            Assert.False(resHuge.Sufficient);
        }
    }
}
