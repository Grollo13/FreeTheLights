using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace FreeTheLights.Settings
{
    /// <summary>One step of the settings format: rewrites the JSON of format <see cref="FromVersion"/> into format FromVersion + 1.</summary>
    public sealed class SettingsMigration
    {
        public SettingsMigration(int fromVersion, string description, Action<JObject> apply)
        {
            FromVersion = fromVersion;
            Description = description ?? "";
            Apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public int FromVersion { get; }

        /// <summary>What the step changes, for the log.</summary>
        public string Description { get; }

        /// <summary>Rewrites the saved JSON in place.</summary>
        public Action<JObject> Apply { get; }
    }

    public enum SettingsLoadOutcome
    {
        /// <summary>Nothing was saved yet.</summary>
        New,

        /// <summary>Saved in the current format.</summary>
        Current,

        /// <summary>Saved in an older format and migrated.</summary>
        Migrated,

        /// <summary>Saved in a format older than <see cref="SettingsMigrator.OldestMigratableVersion"/>: replaced by defaults.</summary>
        TooOld,

        /// <summary>Saved by a newer version of the plugin, which this one can't read: replaced by defaults.</summary>
        TooNew,

        /// <summary>Unreadable, or a migration step failed: replaced by defaults.</summary>
        Failed,
    }

    public sealed class SettingsLoadResult
    {
        internal SettingsLoadResult(PluginSettings settings, SettingsLoadOutcome outcome, int savedVersion, Exception error)
        {
            Settings = settings;
            Outcome = outcome;
            SavedVersion = savedVersion;
            Error = error;
        }

        /// <summary>The settings to use, with <see cref="PluginSettings.EnsureDefaults"/> applied.</summary>
        public PluginSettings Settings { get; }

        public SettingsLoadOutcome Outcome { get; }

        /// <summary>Format version of the saved settings (0 when nothing was saved or the version is missing).</summary>
        public int SavedVersion { get; }

        /// <summary>Why the saved settings were replaced by defaults, for <see cref="SettingsLoadOutcome.Failed"/>.</summary>
        public Exception Error { get; }

        /// <summary>True when the saved settings were changed or discarded, so the original should be backed up before it is overwritten.</summary>
        public bool NeedsBackup => Outcome != SettingsLoadOutcome.New && Outcome != SettingsLoadOutcome.Current;

        /// <summary>True when the user's saved settings were replaced by defaults.</summary>
        public bool WasReset => Outcome == SettingsLoadOutcome.TooOld || Outcome == SettingsLoadOutcome.TooNew || Outcome == SettingsLoadOutcome.Failed;

        /// <summary>A line for the log, or null when there is nothing to report.</summary>
        public string Message
        {
            get
            {
                switch (Outcome)
                {
                    case SettingsLoadOutcome.Migrated:
                        return $"settings were migrated from format {SavedVersion} to {PluginSettings.CurrentVersion}";
                    case SettingsLoadOutcome.TooOld:
                        return $"settings of format {SavedVersion} are too old to migrate and were replaced by defaults";
                    case SettingsLoadOutcome.TooNew:
                        return $"settings of format {SavedVersion} were saved by a newer version of the plugin and were replaced by defaults";
                    case SettingsLoadOutcome.Failed:
                        return $"settings of format {SavedVersion} could not be read and were replaced by defaults: {Error?.Message}";
                    default:
                        return null;
                }
            }
        }
    }

    /// <summary>
    /// Loads saved settings, migrating them step by step from the format they were saved in to
    /// <see cref="PluginSettings.CurrentVersion"/>. Migrations work on the JSON, before it is deserialized, so they can
    /// rename, move or convert anything without keeping old properties in the settings classes.
    /// </summary>
    public static class SettingsMigrator
    {
        /// <summary>
        /// The oldest format that can still be migrated. Older settings are replaced by defaults. Raise it only to drop
        /// support for old formats on purpose.
        /// </summary>
        public const int OldestMigratableVersion = 4;

        /// <summary>
        /// One step per format change, from <see cref="OldestMigratableVersion"/> up to the version before
        /// <see cref="PluginSettings.CurrentVersion"/>. When the format changes, bump CurrentVersion and add the step
        /// from the previous version here, plus a fixture of the old format (see SettingsMigrationTests).
        /// </summary>
        public static readonly IReadOnlyList<SettingsMigration> Migrations = new SettingsMigration[]
        {
            // new SettingsMigration(4, "What changed", json => { ... }),
        };

        public static SettingsLoadResult Load(JObject saved) =>
            Load(saved, Migrations, PluginSettings.CurrentVersion, OldestMigratableVersion);

        /// <summary>Loads <paramref name="saved"/> (null = nothing saved) with the given migrations. Never throws; <paramref name="saved"/> is not modified.</summary>
        public static SettingsLoadResult Load(JObject saved, IReadOnlyList<SettingsMigration> migrations, int currentVersion, int oldestVersion)
        {
            if (saved == null) return Defaults(SettingsLoadOutcome.New, 0, null);

            var version = 0;
            try
            {
                version = ReadVersion(saved);
                if (version > currentVersion) return Defaults(SettingsLoadOutcome.TooNew, version, null);
                if (version < oldestVersion) return Defaults(SettingsLoadOutcome.TooOld, version, null);

                var json = (JObject)saved.DeepClone();
                for (var from = version; from < currentVersion; from++)
                {
                    var step = migrations.SingleOrDefault(m => m.FromVersion == from)
                        ?? throw new InvalidOperationException($"no migration from format {from}");
                    step.Apply(json);
                    json["SettingsVersion"] = from + 1;
                }

                var settings = json.ToObject<PluginSettings>() ?? throw new InvalidOperationException("the settings are empty");
                settings.EnsureDefaults();
                var outcome = version < currentVersion ? SettingsLoadOutcome.Migrated : SettingsLoadOutcome.Current;
                return new SettingsLoadResult(settings, outcome, version, null);
            }
            catch (Exception ex)
            {
                return Defaults(SettingsLoadOutcome.Failed, version, ex);
            }
        }

        private static int ReadVersion(JObject saved)
        {
            var token = saved["SettingsVersion"];
            return token != null && token.Type == JTokenType.Integer ? token.Value<int>() : 0;
        }

        private static SettingsLoadResult Defaults(SettingsLoadOutcome outcome, int version, Exception error)
        {
            var settings = new PluginSettings();
            settings.EnsureDefaults();
            return new SettingsLoadResult(settings, outcome, version, error);
        }
    }
}
