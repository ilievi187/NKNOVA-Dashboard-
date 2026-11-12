using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NKNOVA.Installer.Core;
using Xunit;

namespace Nolvus.Core.Tests.Installer
{
    public class ManifestValidationTests
    {
        private Manifest CreateValidManifest()
        {
            return new Manifest
            {
                SchemaVersion = 1,
                Id = "nknova-test-profile",
                DisplayName = "NKNOVA Test Profile",
                Game = new GameSpec
                {
                    SteamAppId = 489830,
                    Executable = "SkyrimSE.exe"
                },
                Runtime = new RuntimeSpec
                {
                    ProtonVersion = "GE-Proton8-26",
                    DisplayBackend = "xwayland",
                    ModManager = "fluorine"
                },
                Components = new List<ComponentSpec>
                {
                    new()
                    {
                        Id = "base-clean-skyrim",
                        Version = "1.6.1170",
                        SourceKind = "upstream-dashboard",
                        Dependencies = new List<string>()
                    },
                    new()
                    {
                        Id = "skse64",
                        Version = "2.2.6",
                        SourceKind = "user-archive",
                        ArchiveFile = "skse64_2_02_06.7z",
                        Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                        Dependencies = new List<string> { "base-clean-skyrim" }
                    }
                }
            };
        }

        [Fact]
        public void Valid_manifest_passes_check_and_orders_dependencies()
        {
            var manifest = CreateValidManifest();
            ManifestValidation.Check(manifest);

            var ordered = ManifestValidation.Ordered(manifest);
            Assert.Equal(2, ordered.Count);
            Assert.Equal("base-clean-skyrim", ordered[0].Id);
            Assert.Equal("skse64", ordered[1].Id);
        }

        [Fact]
        public void Unsupported_schema_version_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.SchemaVersion = 2;
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Missing_display_name_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.DisplayName = " ";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Invalid_manifest_id_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Id = "bad id with spaces";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Trailing_newline_in_id_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Id = "valid-id\n";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Trailing_newline_in_sha256_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[1].Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef\n";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Malformed_sha256_length_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[1].Sha256 = "short-hash";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Upstream_component_with_archive_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[0].ArchiveFile = "illegal-upstream-archive.zip";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Upstream_component_with_sha256_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[0].Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Path_traversal_archive_filename_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[1].ArchiveFile = "../outside.7z";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Subdirectory_slash_in_archive_filename_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[1].ArchiveFile = "sub/file.7z";
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Duplicate_component_id_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components.Add(new ComponentSpec
            {
                Id = "skse64",
                Version = "2.2.7",
                SourceKind = "user-archive",
                ArchiveFile = "skse64_new.7z"
            });
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Dependency_cycle_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            // Create cycle: base-clean-skyrim -> skse64 -> base-clean-skyrim
            manifest.Components[0].Dependencies.Add("skse64");
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Unknown_dependency_throws_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components[1].Dependencies.Add("non-existent-package");
            Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
        }

        [Fact]
        public void Conflicting_enb_and_community_shaders_components_throw_InvalidDataException()
        {
            var manifest = CreateValidManifest();
            manifest.Components.Add(new ComponentSpec
            {
                Id = "cabbage-enb-preset",
                Version = "1.0",
                SourceKind = "user-archive",
                ArchiveFile = "cabbage.7z"
            });
            manifest.Components.Add(new ComponentSpec
            {
                Id = "community-shaders",
                Version = "0.8.4",
                SourceKind = "user-archive",
                ArchiveFile = "cs.7z"
            });

            var ex = Assert.Throws<InvalidDataException>(() => ManifestValidation.Check(manifest));
            Assert.Contains("ENB and Community Shaders", ex.Message);
        }
    }
}
