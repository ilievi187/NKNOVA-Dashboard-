using System;
using System.IO;
using NKNOVA.Installer.Core;
using Xunit;

namespace Nolvus.Core.Tests.Installer
{
    public class GraphicsOnlyInstallPlanTests : IDisposable
    {
        private readonly string _testRoot;
        private readonly string _mockGameDir;
        private readonly string _mockInstallDir;
        private readonly string _mockArchiveDir;

        public GraphicsOnlyInstallPlanTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "nknova-go-plan-" + Guid.NewGuid().ToString("N"));
            _mockGameDir = Path.Combine(_testRoot, "Skyrim");
            _mockInstallDir = Path.Combine(_testRoot, "NolvusInstance");
            _mockArchiveDir = Path.Combine(_testRoot, "Archives");

            Directory.CreateDirectory(_mockGameDir);
            Directory.CreateDirectory(_mockInstallDir);
            Directory.CreateDirectory(_mockArchiveDir);

            File.WriteAllText(Path.Combine(_mockGameDir, "SkyrimSE.exe"), "dummy exe");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                    Directory.Delete(_testRoot, recursive: true);
            }
            catch { }
        }

        [Fact]
        public void Valid_parameters_produce_correct_GraphicsOnlyInstallPlan()
        {
            var plan = GraphicsOnlyInstallPlan.Create(
                _mockGameDir,
                _mockInstallDir,
                _mockArchiveDir,
                "CABBAGE",
                1920,
                1080,
                instanceRegistered: false,
                variantPublished: true);

            Assert.Equal("16:9", plan.AspectRatio);
            Assert.Equal("1920", plan.ResolutionWidth);
            Assert.Equal("1080", plan.ResolutionHeight);
            Assert.Equal("CABBAGE", plan.EnbCode);
            Assert.Equal(GraphicsOnlyInstallPlan.Variant, "Graphics Only");
        }

        [Theory]
        [InlineData(2560, 1080, "21:9")]
        [InlineData(3440, 1440, "21:9")]
        [InlineData(5120, 1440, "32:9")]
        public void Ultra_wide_resolutions_yield_correct_aspect_ratios(int w, int h, string expectedAspect)
        {
            var plan = GraphicsOnlyInstallPlan.Create(
                _mockGameDir,
                _mockInstallDir,
                _mockArchiveDir,
                "KAUZ",
                w,
                h,
                instanceRegistered: false,
                variantPublished: true);

            Assert.Equal(expectedAspect, plan.AspectRatio);
        }

        [Fact]
        public void Already_registered_instance_throws_InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    _mockGameDir, _mockInstallDir, _mockArchiveDir,
                    "CABBAGE", 1920, 1080,
                    instanceRegistered: true,
                    variantPublished: true));

            Assert.Contains("already exists", ex.Message);
        }

        [Fact]
        public void Unpublished_variant_throws_InvalidOperationException()
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    _mockGameDir, _mockInstallDir, _mockArchiveDir,
                    "CABBAGE", 1920, 1080,
                    instanceRegistered: false,
                    variantPublished: false));

            Assert.Contains("unavailable from the Nolvus API", ex.Message);
        }

        [Fact]
        public void Unknown_enb_code_throws_ArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    _mockGameDir, _mockInstallDir, _mockArchiveDir,
                    "UNKNOWN_ENB", 1920, 1080,
                    instanceRegistered: false,
                    variantPublished: true));
        }

        [Fact]
        public void Out_of_range_resolution_throws_ArgumentOutOfRangeException()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    _mockGameDir, _mockInstallDir, _mockArchiveDir,
                    "CABBAGE", 640, 480,
                    instanceRegistered: false,
                    variantPublished: true));
        }

        [Fact]
        public void Missing_skyrim_executable_throws_InvalidOperationException()
        {
            var emptyGameDir = Path.Combine(_testRoot, "EmptyGame");
            Directory.CreateDirectory(emptyGameDir);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    emptyGameDir, _mockInstallDir, _mockArchiveDir,
                    "CABBAGE", 1920, 1080,
                    instanceRegistered: false,
                    variantPublished: true));

            Assert.Contains("Skyrim SE/AE is not configured", ex.Message);
        }

        [Fact]
        public void Non_empty_install_directory_throws_InvalidOperationException()
        {
            File.WriteAllText(Path.Combine(_mockInstallDir, "existing_file.txt"), "some content");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    _mockGameDir, _mockInstallDir, _mockArchiveDir,
                    "CABBAGE", 1920, 1080,
                    instanceRegistered: false,
                    variantPublished: true));

            Assert.Contains("folder is not empty", ex.Message);
        }

        [Fact]
        public void Overlapping_game_and_install_directory_throws_InvalidOperationException()
        {
            var nestedInstallDir = Path.Combine(_mockGameDir, "SubNolvus");
            Directory.CreateDirectory(nestedInstallDir);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                GraphicsOnlyInstallPlan.Create(
                    _mockGameDir, nestedInstallDir, _mockArchiveDir,
                    "CABBAGE", 1920, 1080,
                    instanceRegistered: false,
                    variantPublished: true));

            Assert.Contains("must have independent directories", ex.Message);
        }
    }
}
