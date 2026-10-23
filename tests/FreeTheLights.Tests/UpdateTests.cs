using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FreeTheLights.Updates;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FreeTheLights.Tests
{
    public class UpdateTests
    {
        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "github-release-latest.json")));

        private static string NewFolder() => Path.Combine(Path.GetTempPath(), "FreeTheLights-update-" + Guid.NewGuid().ToString("N"));

        // ---------- Release info ----------

        [Theory]
        [InlineData("v1.2.3", "1.2.3")]
        [InlineData("1.2.3", "1.2.3")]
        [InlineData("v0.10.0", "0.10.0")]
        [InlineData("v1.2.3-beta.1", null)]
        [InlineData("v1.2", null)]
        [InlineData("v1.2.3.4", null)]
        [InlineData("latest", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Release_tags_are_versions(string tag, string expected)
        {
            var parsed = ReleaseInfo.TryParseTag(tag, out var version);
            Assert.Equal(expected != null, parsed);
            Assert.Equal(expected, version?.ToString());
        }

        [Fact]
        public void A_github_release_gives_its_version_page_and_assets()
        {
            var release = ReleaseInfo.FromGitHubJson(Fixture());

            Assert.Equal("v0.2.0", release.Tag);
            Assert.Equal(new Version(0, 2, 0), release.Version);
            Assert.Equal("https://github.com/Grollo13/FreeTheLights/releases/tag/v0.2.0", release.NotesUrl);
            Assert.Equal(new DateTime(2026, 10, 20, 8, 5, 0, DateTimeKind.Utc), release.PublishedUtc);
            Assert.Equal("https://github.com/Grollo13/FreeTheLights/releases/download/v0.2.0/FreeTheLights.dll", release.DllUrl);
            Assert.Equal("https://github.com/Grollo13/FreeTheLights/releases/download/v0.2.0/FreeTheLights.dll.sha256", release.ChecksumUrl);
            Assert.True(release.CanInstall);
        }

        [Theory]
        [InlineData("draft")]
        [InlineData("prerelease")]
        public void Drafts_and_pre_releases_are_not_offered(string flag)
        {
            var json = Fixture();
            json[flag] = true;
            Assert.Null(ReleaseInfo.FromGitHubJson(json));
        }

        [Fact]
        public void A_release_without_a_version_tag_is_not_offered()
        {
            var json = Fixture();
            json["tag_name"] = "nightly";
            Assert.Null(ReleaseInfo.FromGitHubJson(json));
        }

        [Fact]
        public void A_release_without_the_plugin_or_its_checksum_cannot_be_installed()
        {
            var json = Fixture();
            ((JArray)json["assets"])[1].Remove();
            var release = ReleaseInfo.FromGitHubJson(json);
            Assert.NotNull(release.DllUrl);
            Assert.Null(release.ChecksumUrl);
            Assert.False(release.CanInstall);

            json["assets"] = new JArray();
            Assert.False(ReleaseInfo.FromGitHubJson(json).CanInstall);
        }

        [Theory]
        [InlineData("https://github.com/Grollo13/FreeTheLights/releases/download/v0.2.0/FreeTheLights.dll", true)]
        [InlineData("http://github.com/Grollo13/FreeTheLights/releases/download/v0.2.0/FreeTheLights.dll", false)]
        [InlineData("https://github.com/someone/FreeTheLights/releases/download/v0.2.0/FreeTheLights.dll", false)]
        [InlineData("https://github.com.evil.example/Grollo13/FreeTheLights/releases/download/v0.2.0/FreeTheLights.dll", false)]
        [InlineData("https://github.com/Grollo13/FreeTheLights/releases/download/../../../evil/FreeTheLights.dll", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_downloads_of_this_repository_are_trusted(string url, bool expected)
        {
            Assert.Equal(expected, ReleaseInfo.IsTrustedAssetUrl(url));
        }

        [Fact]
        public void Release_notes_only_open_pages_of_this_repository()
        {
            Assert.Equal(ReleaseInfo.ReleasesPageUrl, new ReleaseInfo { PageUrl = "https://evil.example/" }.NotesUrl);
            Assert.Equal(ReleaseInfo.ReleasesPageUrl, new ReleaseInfo { PageUrl = "file:///C:/Windows/notepad.exe" }.NotesUrl);
            Assert.Equal(ReleaseInfo.ReleasesPageUrl, new ReleaseInfo().NotesUrl);
        }

        [Theory]
        [InlineData("v0.2.0", "0.1.0.0", true)]
        [InlineData("v0.2.0", "0.2.0.0", false)]
        [InlineData("v0.2.0", "0.3.0.0", false)]
        [InlineData("v0.10.0", "0.9.0.0", true)]
        [InlineData("v1.0.0", "0.99.99.0", true)]
        public void A_release_is_offered_only_when_it_is_newer(string tag, string installed, bool expected)
        {
            Assert.Equal(expected, new ReleaseInfo { Tag = tag }.IsNewerThan(Version.Parse(installed)));
        }

        // ---------- Checking ----------

        [Fact]
        public void A_newer_release_is_available()
        {
            var handler = new FakeHandler(_ => Json(Fixture().ToString()));
            using (var service = new UpdateService(NewFolder(), new Version(0, 1, 0, 0), null, null, handler))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.Available);
                Assert.Equal("v0.2.0", service.Latest.Tag);
                Assert.Equal(UpdateService.LatestReleaseUrl, handler.LastUrl);
            }
        }

        [Fact]
        public void The_installed_release_is_up_to_date()
        {
            using (var service = new UpdateService(NewFolder(), new Version(0, 2, 0, 0), null, null, new FakeHandler(_ => Json(Fixture().ToString()))))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.UpToDate);
                Assert.Equal("v0.2.0", service.Latest.Tag);
            }
        }

        [Fact]
        public void No_release_yet_is_up_to_date()
        {
            using (var service = new UpdateService(NewFolder(), new Version(0, 1, 0, 0), null, null, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.UpToDate);
                Assert.Null(service.Latest);
            }
        }

        [Fact]
        public void A_fresh_check_is_reused_instead_of_asking_github_again()
        {
            var folder = NewFolder();
            var handler = new FakeHandler(_ => Json(Fixture().ToString()));
            using (var service = new UpdateService(folder, new Version(0, 1, 0, 0), null, null, handler))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.Available);
            }

            using (var service = new UpdateService(folder, new Version(0, 1, 0, 0), null, null, handler))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.Available);
                Assert.Equal(1, handler.Calls);

                service.Check(force: true);
                WaitFor(() => handler.Calls == 2 && service.Status == UpdateStatus.Available);
            }
        }

        [Fact]
        public void Offline_the_last_answer_is_used()
        {
            var folder = NewFolder();
            using (var service = new UpdateService(folder, new Version(0, 1, 0, 0), null, null, new FakeHandler(_ => Json(Fixture().ToString()))))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.Available);
            }

            using (var service = new UpdateService(folder, new Version(0, 1, 0, 0), null, null, new FakeHandler(_ => throw new HttpRequestException("offline"))))
            {
                service.Check(force: true);
                WaitFor(() => service.Status == UpdateStatus.Available && service.Offline);
                Assert.Equal("v0.2.0", service.Latest.Tag);
            }
        }

        [Fact]
        public void A_failed_first_check_reports_the_error()
        {
            using (var service = new UpdateService(NewFolder(), new Version(0, 1, 0, 0), null, null, new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.CheckFailed);
                Assert.NotNull(service.Error);
                Assert.Null(service.Latest);
            }
        }

        // ---------- Verifying ----------

        /// <summary>The plugin DLL the tests run against: a real FreeTheLights assembly of version <see cref="BuiltVersion"/>.</summary>
        private static string BuiltDll => typeof(ReleaseInfo).Assembly.Location;

        private static Version BuiltVersion => ReleaseInfo.Normalize(typeof(ReleaseInfo).Assembly.GetName().Version);

        [Theory]
        [InlineData("0f4d3c5e7a9b1d2f3e4a5b6c7d8e9f00112233445566778899aabbccddeeff00  FreeTheLights.dll\n", "0f4d3c5e7a9b1d2f3e4a5b6c7d8e9f00112233445566778899aabbccddeeff00")]
        [InlineData("0F4D3C5E7A9B1D2F3E4A5B6C7D8E9F00112233445566778899AABBCCDDEEFF00", "0f4d3c5e7a9b1d2f3e4a5b6c7d8e9f00112233445566778899aabbccddeeff00")]
        [InlineData("not a checksum", null)]
        [InlineData("", null)]
        public void Checksum_files_are_read_as_sha256sum_writes_them(string text, string expected)
        {
            Assert.Equal(expected, UpdateInstaller.ParseChecksum(text));
        }

        [Fact]
        public void The_plugin_of_the_release_version_passes_verification()
        {
            UpdateInstaller.Verify(BuiltDll, UpdateInstaller.Sha256OfFile(BuiltDll), BuiltVersion);
        }

        [Fact]
        public void A_damaged_download_is_rejected()
        {
            Assert.Throws<UpdateException>(() => UpdateInstaller.Verify(BuiltDll, new string('0', 64), BuiltVersion));
        }

        [Fact]
        public void A_plugin_of_another_version_is_rejected()
        {
            var other = new Version(BuiltVersion.Major, BuiltVersion.Minor, BuiltVersion.Build + 1);
            Assert.Throws<UpdateException>(() => UpdateInstaller.Verify(BuiltDll, UpdateInstaller.Sha256OfFile(BuiltDll), other));
        }

        [Fact]
        public void Files_that_are_not_the_plugin_are_rejected()
        {
            var testDll = typeof(UpdateTests).Assembly.Location;
            Assert.Throws<UpdateException>(() => UpdateInstaller.Verify(testDll, UpdateInstaller.Sha256OfFile(testDll), BuiltVersion));

            var text = Path.Combine(NewFolder() + ".dll");
            File.WriteAllText(text, "not a DLL");
            Assert.Throws<UpdateException>(() => UpdateInstaller.Verify(text, UpdateInstaller.Sha256OfFile(text), BuiltVersion));
        }

        // ---------- Installing ----------

        /// <summary>A plugin folder with an old FreeTheLights.dll (and .pdb), and a new DLL to install.</summary>
        private static (string plugin, string newDll) PluginFolder()
        {
            var folder = NewFolder();
            Directory.CreateDirectory(folder);
            var plugin = Path.Combine(folder, "FreeTheLights.dll");
            File.WriteAllText(plugin, "old plugin");
            File.WriteAllText(Path.Combine(folder, "FreeTheLights.pdb"), "old symbols");
            var newDll = Path.Combine(folder, "download.bin");
            File.WriteAllText(newDll, "new plugin");
            return (plugin, newDll);
        }

        [Fact]
        public void Installing_replaces_the_plugin_and_keeps_the_old_one_aside()
        {
            var (plugin, newDll) = PluginFolder();
            new UpdateInstaller(plugin, null).Install(newDll);

            Assert.Equal("new plugin", File.ReadAllText(plugin));
            Assert.Equal("old plugin", File.ReadAllText(plugin + ".old"));
            Assert.False(File.Exists(Path.ChangeExtension(plugin, ".pdb")));
        }

        [Fact]
        public void Cleanup_deletes_the_old_plugin()
        {
            var (plugin, newDll) = PluginFolder();
            var installer = new UpdateInstaller(plugin, null);
            installer.Install(newDll);

            Assert.Equal(0, installer.Cleanup());
            Assert.False(File.Exists(plugin + ".old"));
            Assert.True(File.Exists(plugin));
        }

        [Fact]
        public void A_loaded_plugin_can_be_replaced()
        {
            // As in SimHub: the running plugin's file is loaded, so it can only be renamed, not overwritten or deleted.
            var (plugin, newDll) = PluginFolder();
            File.Copy(BuiltDll, plugin, true);
            System.Reflection.Assembly.LoadFile(plugin);

            var installer = new UpdateInstaller(plugin, null);
            installer.Install(newDll);
            Assert.Equal("new plugin", File.ReadAllText(plugin));
            Assert.True(File.Exists(plugin + ".old"));

            // Updating again before a restart: the loaded old file stays and the next one gets another name.
            File.WriteAllText(newDll, "newer plugin");
            installer.Install(newDll);
            Assert.Equal("newer plugin", File.ReadAllText(plugin));
            Assert.True(File.Exists(plugin + ".old"));
            Assert.Equal("new plugin", File.ReadAllText(plugin + ".1.old"));
            Assert.Equal(1, installer.Cleanup());
        }

        [Fact]
        public void Without_write_access_the_install_runs_elevated()
        {
            var (plugin, newDll) = PluginFolder();
            var folder = Path.GetDirectoryName(plugin);
            var runner = new FakeElevatedRunner(() =>
            {
                // What the elevated script does, once the user allowed it.
                AllowWriting(folder);
                File.Move(plugin, plugin + ".old");
                File.Copy(newDll, plugin);
                return true;
            });

            DenyWriting(folder);
            try
            {
                new UpdateInstaller(plugin, runner).Install(newDll);
            }
            finally
            {
                AllowWriting(folder);
            }

            Assert.Equal(1, runner.Calls);
            Assert.Contains("Move-Item -LiteralPath $dll -Destination $old", runner.Script);
            Assert.Equal("new plugin", File.ReadAllText(plugin));
        }

        [Fact]
        public void Declining_administrator_rights_keeps_the_old_plugin()
        {
            var (plugin, newDll) = PluginFolder();
            var folder = Path.GetDirectoryName(plugin);
            var runner = new FakeElevatedRunner(() => false);

            DenyWriting(folder);
            try
            {
                Assert.Throws<UpdateException>(() => new UpdateInstaller(plugin, runner).Install(newDll));
            }
            finally
            {
                AllowWriting(folder);
            }

            Assert.Equal("old plugin", File.ReadAllText(plugin));
        }

        [Fact]
        public void The_elevated_script_quotes_its_paths()
        {
            var installer = new UpdateInstaller(@"C:\Sim Hub's\FreeTheLights.dll", null);
            var script = installer.ElevatedScript(@"C:\Temp\new.dll", @"C:\Sim Hub's\FreeTheLights.dll.old");
            Assert.Contains(@"$dll = 'C:\Sim Hub''s\FreeTheLights.dll'", script);
            Assert.Contains(@"$old = 'C:\Sim Hub''s\FreeTheLights.dll.old'", script);
            Assert.Contains(@"$new = 'C:\Temp\new.dll'", script);
        }

        // ---------- Updating end to end ----------

        /// <summary>GitHub with a release of the built plugin (<see cref="BuiltVersion"/>), its checksum file being <paramref name="checksum"/>.</summary>
        private static FakeHandler GitHub(string checksum = null)
        {
            var tag = "v" + BuiltVersion;
            var release = Fixture();
            release["tag_name"] = tag;
            release["html_url"] = ReleaseInfo.ReleasesPageUrl + "/tag/" + tag;
            release["assets"][0]["browser_download_url"] = ReleaseInfo.DownloadUrlPrefix + tag + "/FreeTheLights.dll";
            release["assets"][1]["browser_download_url"] = ReleaseInfo.DownloadUrlPrefix + tag + "/FreeTheLights.dll.sha256";
            checksum = checksum ?? UpdateInstaller.Sha256OfFile(BuiltDll) + "  FreeTheLights.dll\n";

            return new FakeHandler(request =>
            {
                var url = request.RequestUri.ToString();
                if (url == UpdateService.LatestReleaseUrl) return Json(release.ToString());
                if (url.EndsWith("/FreeTheLights.dll.sha256", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(checksum) };
                if (url.EndsWith("/FreeTheLights.dll", StringComparison.Ordinal)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(BuiltDll)) };
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });
        }

        [Fact]
        public void Updating_downloads_verifies_and_installs_the_release()
        {
            var (plugin, _) = PluginFolder();
            var installer = new UpdateInstaller(plugin, null);
            var folder = NewFolder();
            using (var service = new UpdateService(folder, new Version(0, 0, 1, 0), installer, null, GitHub()))
            {
                Assert.False(service.CanInstall);
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.Available);
                Assert.True(service.CanInstall);

                service.Install();
                WaitFor(() => service.Status == UpdateStatus.Installed);
                Assert.Null(service.Error);
                Assert.Equal(UpdateInstaller.Sha256OfFile(BuiltDll), UpdateInstaller.Sha256OfFile(plugin));
                Assert.Empty(Directory.GetFiles(folder, "*.dll"));

                // Nothing more to do until SimHub restarts.
                Assert.False(service.CanInstall);
                service.Check(force: true);
                Assert.Equal(UpdateStatus.Installed, service.Status);
            }
        }

        [Fact]
        public void A_failed_update_keeps_the_old_plugin_and_can_be_tried_again()
        {
            var (plugin, _) = PluginFolder();
            using (var service = new UpdateService(NewFolder(), new Version(0, 0, 1, 0), new UpdateInstaller(plugin, null), null, GitHub(new string('a', 64))))
            {
                service.Check();
                WaitFor(() => service.Status == UpdateStatus.Available);

                service.Install();
                WaitFor(() => service.Status == UpdateStatus.InstallFailed);
                Assert.NotNull(service.Error);
                Assert.Equal("old plugin", File.ReadAllText(plugin));
                Assert.True(service.CanInstall);
            }
        }

        // ---------- Helpers ----------

        private static void DenyWriting(string folder)
        {
            var security = Directory.GetAccessControl(folder);
            security.AddAccessRule(WriteRule(folder));
            Directory.SetAccessControl(folder, security);
        }

        private static void AllowWriting(string folder)
        {
            var security = Directory.GetAccessControl(folder);
            security.RemoveAccessRule(WriteRule(folder));
            Directory.SetAccessControl(folder, security);
        }

        /// <summary>Denies the current user adding files to <paramref name="folder"/>, so renaming the plugin fails like under Program Files.</summary>
        private static System.Security.AccessControl.FileSystemAccessRule WriteRule(string folder) =>
            new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User,
                System.Security.AccessControl.FileSystemRights.CreateFiles,
                System.Security.AccessControl.AccessControlType.Deny);

        private sealed class FakeElevatedRunner : IElevatedRunner
        {
            private readonly Func<bool> run;

            public FakeElevatedRunner(Func<bool> run)
            {
                this.run = run;
            }

            public int Calls { get; private set; }

            public string Script { get; private set; }

            public bool Run(string script)
            {
                Calls++;
                Script = script;
                return run();
            }
        }

        // ---------- Helpers: HTTP ----------

        private static HttpResponseMessage Json(string json) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };

        private static void WaitFor(Func<bool> condition)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the update service");
                Thread.Sleep(5);
            }
        }

        /// <summary>Answers every request with <c>respond</c>'s response and counts them.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;
            private int calls;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public int Calls => Volatile.Read(ref calls);

            public string LastUrl { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref calls);
                LastUrl = request.RequestUri.ToString();
                return Task.FromResult(respond(request));
            }
        }
    }
}
