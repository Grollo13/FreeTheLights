using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FreeTheLights.Settings;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FreeTheLights.Tests
{
    public class SettingsMigrationTests
    {
        private static string FixturePath(int version) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", $"settings-v{version}.json");

        // ---------- The real migrations ----------

        [Fact]
        public void Every_format_since_the_oldest_migratable_one_has_exactly_one_step()
        {
            Assert.True(SettingsMigrator.OldestMigratableVersion <= PluginSettings.CurrentVersion);
            for (var version = SettingsMigrator.OldestMigratableVersion; version < PluginSettings.CurrentVersion; version++)
            {
                Assert.Single(SettingsMigrator.Migrations, m => m.FromVersion == version);
            }

            Assert.All(SettingsMigrator.Migrations, m => Assert.InRange(m.FromVersion, SettingsMigrator.OldestMigratableVersion, PluginSettings.CurrentVersion - 1));
        }

        [Fact]
        public void Settings_of_every_supported_format_load_without_a_reset()
        {
            // Keep a fixture of every format: when CurrentVersion is bumped, add settings-v<new>.json next to the old ones.
            for (var version = SettingsMigrator.OldestMigratableVersion; version <= PluginSettings.CurrentVersion; version++)
            {
                Assert.True(File.Exists(FixturePath(version)), $"missing fixture settings-v{version}.json");
                var saved = JObject.Parse(File.ReadAllText(FixturePath(version)));
                Assert.Equal(version, (int)saved["SettingsVersion"]);

                var loaded = SettingsMigrator.Load(saved);

                Assert.Null(loaded.Error);
                Assert.Equal(version == PluginSettings.CurrentVersion ? SettingsLoadOutcome.Current : SettingsLoadOutcome.Migrated, loaded.Outcome);
                Assert.Equal(PluginSettings.CurrentVersion, loaded.Settings.SettingsVersion);
                Assert.Equal(2, loaded.Settings.Devices.Count);
                Assert.Equal(12, loaded.Settings.CarDataRefreshHours);
            }
        }

        // ---------- How migrations are applied ----------

        private static JObject Saved(int version) =>
            JObject.Parse($@"{{ ""SettingsVersion"": {version}, ""RefreshMinutes"": 120, ""Devices"": [ {{ ""Name"": ""Wheel"" }} ] }}");

        /// <summary>Format 4 had "RefreshMinutes", format 5 "RefreshHours", format 6 calls it "CarDataRefreshHours".</summary>
        private static List<SettingsMigration> TwoSteps(List<string> log) => new List<SettingsMigration>
        {
            new SettingsMigration(5, "rename", json =>
            {
                log.Add("5:" + json["SettingsVersion"]);
                json["CarDataRefreshHours"] = json["RefreshHours"];
                json.Remove("RefreshHours");
            }),
            new SettingsMigration(4, "minutes to hours", json =>
            {
                log.Add("4:" + json["SettingsVersion"]);
                json["RefreshHours"] = (int)json["RefreshMinutes"] / 60;
                json.Remove("RefreshMinutes");
            }),
        };

        [Fact]
        public void Steps_run_in_order_from_the_saved_format()
        {
            var log = new List<string>();
            var loaded = SettingsMigrator.Load(Saved(4), TwoSteps(log), 6, 4);

            Assert.Equal(SettingsLoadOutcome.Migrated, loaded.Outcome);
            Assert.Equal(new[] { "4:4", "5:5" }, log);
            Assert.Equal(4, loaded.SavedVersion);
            Assert.Equal(2, loaded.Settings.CarDataRefreshHours);
            Assert.Equal("Wheel", loaded.Settings.Devices.Single().Name);
            Assert.Single(loaded.Settings.Devices.Single().Parts);
            Assert.Equal(PluginSettings.CurrentVersion, loaded.Settings.SettingsVersion);
            Assert.True(loaded.NeedsBackup);
            Assert.False(loaded.WasReset);
        }

        [Fact]
        public void Only_the_steps_after_the_saved_format_run()
        {
            var log = new List<string>();
            var saved = JObject.Parse(@"{ ""SettingsVersion"": 5, ""RefreshHours"": 3 }");
            var loaded = SettingsMigrator.Load(saved, TwoSteps(log), 6, 4);

            Assert.Equal(new[] { "5:5" }, log);
            Assert.Equal(3, loaded.Settings.CarDataRefreshHours);
        }

        [Fact]
        public void The_saved_json_is_left_untouched_for_the_backup()
        {
            var saved = Saved(4);
            var before = saved.ToString();
            SettingsMigrator.Load(saved, TwoSteps(new List<string>()), 6, 4);
            Assert.Equal(before, saved.ToString());
        }

        [Fact]
        public void Current_settings_load_as_they_are()
        {
            var loaded = SettingsMigrator.Load(JObject.Parse(@"{ ""SettingsVersion"": 6, ""CarDataRefreshHours"": 7 }"), TwoSteps(new List<string>()), 6, 4);

            Assert.Equal(SettingsLoadOutcome.Current, loaded.Outcome);
            Assert.Equal(7, loaded.Settings.CarDataRefreshHours);
            Assert.False(loaded.NeedsBackup);
            Assert.Null(loaded.Message);
        }

        [Fact]
        public void Nothing_saved_gives_defaults_without_a_backup()
        {
            var loaded = SettingsMigrator.Load(null);

            Assert.Equal(SettingsLoadOutcome.New, loaded.Outcome);
            Assert.Empty(loaded.Settings.Devices);
            Assert.False(loaded.NeedsBackup);
            Assert.False(loaded.WasReset);
        }

        [Theory]
        [InlineData(@"{ ""SettingsVersion"": 3, ""CarDataRefreshHours"": 5, ""Devices"": [ { ""Name"": ""Old"" } ] }", 3)]
        [InlineData(@"{ ""CarDataRefreshHours"": 5, ""Devices"": [ { ""Name"": ""Unversioned"" } ] }", 0)]
        [InlineData(@"{ ""SettingsVersion"": ""4"", ""CarDataRefreshHours"": 5 }", 0)]
        public void Formats_older_than_the_oldest_migratable_one_are_reset(string json, int savedVersion)
        {
            var loaded = SettingsMigrator.Load(JObject.Parse(json), TwoSteps(new List<string>()), 6, 4);

            Assert.Equal(SettingsLoadOutcome.TooOld, loaded.Outcome);
            Assert.Equal(savedVersion, loaded.SavedVersion);
            AssertReset(loaded);
        }

        [Fact]
        public void Settings_of_a_newer_plugin_are_reset_and_backed_up()
        {
            var loaded = SettingsMigrator.Load(JObject.Parse(@"{ ""SettingsVersion"": 7, ""CarDataRefreshHours"": 5 }"), TwoSteps(new List<string>()), 6, 4);

            Assert.Equal(SettingsLoadOutcome.TooNew, loaded.Outcome);
            AssertReset(loaded);
        }

        [Fact]
        public void A_missing_step_resets()
        {
            var steps = TwoSteps(new List<string>()).Where(m => m.FromVersion != 5).ToList();
            var loaded = SettingsMigrator.Load(Saved(4), steps, 6, 4);

            Assert.Equal(SettingsLoadOutcome.Failed, loaded.Outcome);
            Assert.Contains("no migration from format 5", loaded.Message);
            AssertReset(loaded);
        }

        [Fact]
        public void A_failing_step_resets()
        {
            var steps = new[] { new SettingsMigration(4, "broken", json => throw new InvalidOperationException("boom")) };
            var loaded = SettingsMigrator.Load(Saved(4), steps, 5, 4);

            Assert.Equal(SettingsLoadOutcome.Failed, loaded.Outcome);
            Assert.Equal("boom", loaded.Error.Message);
            AssertReset(loaded);
        }

        [Fact]
        public void Unreadable_settings_reset()
        {
            var loaded = SettingsMigrator.Load(JObject.Parse(@"{ ""SettingsVersion"": 4, ""Devices"": ""not a list"" }"), new SettingsMigration[0], 4, 4);

            Assert.Equal(SettingsLoadOutcome.Failed, loaded.Outcome);
            Assert.NotNull(loaded.Error);
            AssertReset(loaded);
        }

        private static void AssertReset(SettingsLoadResult loaded)
        {
            Assert.True(loaded.WasReset);
            Assert.True(loaded.NeedsBackup);
            Assert.NotNull(loaded.Message);
            Assert.Empty(loaded.Settings.Devices);
            Assert.Equal(24, loaded.Settings.CarDataRefreshHours);
            Assert.Equal(PluginSettings.CurrentVersion, loaded.Settings.SettingsVersion);
        }
    }
}
