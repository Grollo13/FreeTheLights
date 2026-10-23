using System;
using FreeTheLights.CarData;
using Xunit;

namespace FreeTheLights.Tests
{
    public class CarDataVersionTests
    {
        [Fact]
        public void Parses_branch_from_the_default_url()
        {
            Assert.True(CarDataVersion.TryParseBaseUrl(CarDataService.DefaultBaseUrl, out var owner, out var repo, out var branch));
            Assert.Equal("Lovely-Sim-Racing", owner);
            Assert.Equal("lovely-car-data", repo);
            Assert.Equal("main", branch);

            Assert.True(CarDataVersion.TryParseBaseUrl("https://raw.githubusercontent.com/me/lovely-car-data/v2.0.0/data", out _, out _, out branch));
            Assert.Equal("v2.0.0", branch);

            Assert.False(CarDataVersion.TryParseBaseUrl("http://localhost/data/", out _, out _, out _));
        }

        [Fact]
        public void Takes_the_first_version_of_the_changelog()
        {
            const string changelog = "<h1>Lovely Car Data</h1>\n---\n## Changelog\n\n#### v2.0.0\n1. New File Format\n\n#### v1.2.0\n1. Added `carId`\n";
            Assert.Equal("v2.0.0", CarDataVersion.ParseFormatVersion(changelog));
            Assert.Equal("v3.1", CarDataVersion.ParseFormatVersion("### 3.1\n"));
            Assert.Null(CarDataVersion.ParseFormatVersion("no versions here"));
        }

        [Fact]
        public void Display_contains_format_branch_commit_and_date()
        {
            var version = new CarDataVersion
            {
                FormatVersion = "v2.0.0",
                Branch = "main",
                CommitSha = "d5d6893089c7163e2a6ca37cb88a7be8f4c95dce",
                CommitDateUtc = new DateTime(2026, 10, 9, 13, 51, 59, DateTimeKind.Utc),
                CheckedUtc = DateTime.UtcNow,
            };

            Assert.Equal("lovely-car-data v2.0.0 · main @ d5d6893 (2026-10-09)", version.Display);

            version.Offline = true;
            Assert.Contains("offline", version.Display);
        }
    }
}
