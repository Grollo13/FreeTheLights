using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;
using FreeTheLights.Updates;

namespace FreeTheLights.UI
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Func<object, bool> canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        public bool CanExecute(object parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object parameter) => execute(parameter);
    }

    /// <summary>One checkbox of a feature's "drive these LED groups" list.</summary>
    public class TargetOption : ObservableObject
    {
        private readonly FeatureSettingsBase feature;

        public TargetOption(FeatureSettingsBase feature, LedGroup group)
        {
            this.feature = feature;
            Group = group;
        }

        public LedGroup Group { get; }

        public string Label => $"{Group.Name} ({KindLabel(Group)})";

        public bool IsChecked
        {
            get => feature.TargetGroupIds.Contains(Group.Id);
            set
            {
                if (value == IsChecked) return;
                if (value) feature.TargetGroupIds.Add(Group.Id);
                else feature.TargetGroupIds.Remove(Group.Id);
                OnPropertyChanged();
            }
        }

        public static string KindLabel(LedGroup group)
        {
            switch (group.Kind)
            {
                case LedGroupKind.VerticalColumn: return $"column, {group.Count} LEDs" + (group.Side != LedGroupSide.None ? $", {group.Side.ToString().ToLowerInvariant()}" : string.Empty);
                case LedGroupKind.Matrix: return $"matrix {group.Columns}x{group.Rows}";
                case LedGroupKind.Element: return group.ElementType.ToString().ToLowerInvariant() + (group.Side != LedGroupSide.None ? $", {group.Side.ToString().ToLowerInvariant()}" : string.Empty);
                default: return $"row, {group.Count} LEDs";
            }
        }
    }

    /// <summary>One choice of a flag's pattern drop-down: the pattern, its name, and what it looks like.</summary>
    public sealed class PatternChoice
    {
        private PatternChoice(FlagPattern value, string name, string description)
        {
            Value = value;
            Name = name;
            Description = description;
        }

        public FlagPattern Value { get; }

        public string Name { get; }

        /// <summary>For the tooltip.</summary>
        public string Description { get; }

        /// <summary>A part with an LED flag (matrix): every pattern looks different there.</summary>
        public static readonly PatternChoice[] WithMatrix =
        {
            new PatternChoice(FlagPattern.Alternate, "Alternate", "The colors alternate LED by LED (on an LED flag: a fine checkerboard)"),
            new PatternChoice(FlagPattern.Checker, "Checker", "Large squares on an LED flag; elsewhere like Alternate"),
            new PatternChoice(FlagPattern.Diagonal, "Diagonal", "Split corner to corner on an LED flag; elsewhere one color per half"),
        };

        /// <summary>Rows and columns only: Checker would look like Alternate, and Diagonal splits them in halves.</summary>
        public static readonly PatternChoice[] RowsAndColumns =
        {
            new PatternChoice(FlagPattern.Alternate, "Alternate", "The colors alternate LED by LED"),
            new PatternChoice(FlagPattern.Diagonal, "Halves", "One color on each half"),
        };
    }

    /// <summary>One choice of the per-element effect drop-down (null kind = no effect, static color only).</summary>
    public sealed class EffectChoice
    {
        private EffectChoice(LayerKind? kind, string name)
        {
            Kind = kind;
            Name = name;
        }

        public LayerKind? Kind { get; }

        public string Name { get; }

        public static readonly EffectChoice[] All =
            new[] { new EffectChoice(null, "None (just the color)") }
                .Concat(DevicePart.ElementEffects.Select(k => new EffectChoice(k, DevicePart.FindFeature(k).DisplayName)))
                .ToArray();

        public static EffectChoice For(LayerKind? kind) => All.First(c => c.Kind == kind);

        public override string ToString() => Name;
    }

    public class LayerItem
    {
        public LayerItem(DevicePart device, LayerKind kind)
        {
            Kind = kind;
            Feature = device.GetFeature(kind);
        }

        public LayerKind Kind { get; }

        /// <summary>The layer's feature settings (null for an unknown layer); the priority list shows whether it is on.</summary>
        public FeatureSettingsBase Feature { get; }

        public string Name => DevicePart.FindFeature(Kind)?.DisplayName ?? Kind.ToString();
    }

    /// <summary>View model of the selected part (LED channel) of the selected device.</summary>
    public class DeviceViewModel : ObservableObject, IDisposable
    {
        private bool attached;

        public DeviceViewModel(DevicePart part)
        {
            Part = part;
            MoveLayerCommand = new RelayCommand(p => { if (p is MoveRequest r) MoveIndex(Part.LayerPriority, r.OldIndex, r.NewIndex); });
            Attach();
        }

        public DevicePart Part { get; }

        /// <summary>
        /// Follows the part's changes again after <see cref="Detach"/> (the settings page is shown again) and catches up
        /// on what changed meanwhile.
        /// </summary>
        public void Attach()
        {
            if (attached) return;
            attached = true;
            Part.Groups.CollectionChanged += Groups_CollectionChanged;
            foreach (var group in Part.Groups) group.PropertyChanged += Group_PropertyChanged;
            Part.LayerPriority.CollectionChanged += LayerPriority_CollectionChanged;
            foreach (var feature in Part.Features) feature.PropertyChanged += Feature_PropertyChanged;

            RebuildTargets();
            RebuildLayers();
            RebuildWiring();
            LayoutChanged();
        }

        /// <summary>
        /// Stops following the part's changes. The settings live as long as the plugin, so a hidden or discarded settings
        /// page must not stay subscribed to them.
        /// </summary>
        public void Detach()
        {
            if (!attached) return;
            attached = false;
            Part.Groups.CollectionChanged -= Groups_CollectionChanged;
            foreach (var group in Part.Groups) group.PropertyChanged -= Group_PropertyChanged;
            Part.LayerPriority.CollectionChanged -= LayerPriority_CollectionChanged;
            foreach (var feature in Part.Features) feature.PropertyChanged -= Feature_PropertyChanged;
        }

        public ObservableCollection<TargetOption> RpmTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> FlagTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> ProximityTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> AbsTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> TcTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> IdleTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> EngineTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<TargetOption> PitLimiterTargets { get; } = new ObservableCollection<TargetOption>();
        public ObservableCollection<LayerItem> Layers { get; } = new ObservableCollection<LayerItem>();

        /// <summary>The part has a row of LEDs, the only kind RPM lights can use (else the RPM tab is hidden).</summary>
        public bool HasRpmTargets => RpmTargets.Count > 0;

        /// <summary>The part has front plate elements (buttons, lights, dials): the animation tabs offer to include them.</summary>
        public bool HasElements => Part.Elements.Any();

        /// <summary>One line under the device name, e.g. "31 LEDs · 3 LED groups · 10 front plate elements".</summary>
        public string HeaderSummary
        {
            get
            {
                var groups = Part.Groups.Count(g => !g.IsElement);
                var elements = Part.Elements.Count();
                var text = $"{Part.TotalLeds} LEDs · {groups} group{(groups == 1 ? string.Empty : "s")}";
                if (elements > 0) text += $" · {elements} button{(elements == 1 ? string.Empty : "s")} and lights";
                return text;
            }
        }

        /// <summary>Number of LED indices used by more than one group (0 = the layout is fine); counted when the layout changes.</summary>
        public int OverlapCount { get; private set; }

        public bool HasOverlap => OverlapCount > 0;

        public string OverlapWarning
        {
            get
            {
                var overlaps = OverlapCount;
                return overlaps == 0 ? string.Empty : $"{overlaps} LED{(overlaps == 1 ? " is" : "s are")} used by more than one group. Click the groups in the preview to fix where they start.";
            }
        }

        /// <summary>Drag &amp; drop target of the priority list (parameter: <see cref="MoveRequest"/>).</summary>
        public ICommand MoveLayerCommand { get; }

        // ---------- Wiring ----------

        /// <summary>The rows, columns and matrices in the order they are chained on the LED strip (by start index).</summary>
        public ObservableCollection<LedGroup> WiringGroups { get; } = new ObservableCollection<LedGroup>();

        /// <summary>
        /// The device page offers its wiring dialog: for every part except one of front plate elements only (buttons, lights,
        /// dials, which keep their LED numbers). A part whose last group was removed keeps it, to add groups again.
        /// </summary>
        public bool HasWiring => WiringGroups.Count > 0 || !Part.Elements.Any();

        /// <summary>The groups in wiring order, for the device page: "Shift lights → Left column → Right column".</summary>
        public string WiringSummary => WiringGroups.Count == 0 ? "No LED groups yet." : string.Join(" → ", WiringGroups.Select(g => g.Name));

        private void RebuildWiring()
        {
            var order = Part.GroupsInWiringOrder();
            if (order.SequenceEqual(WiringGroups)) return;
            WiringGroups.Clear();
            foreach (var group in order) WiringGroups.Add(group);
            OnPropertyChanged(nameof(HasWiring));
            OnPropertyChanged(nameof(WiringSummary));
        }

        public void Dispose() => Detach();

        private void Feature_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeatureSettingsBase.Enabled)) OnPropertyChanged(nameof(HeaderSummary));
        }

        /// <summary>
        /// The flag patterns offered for this part: all of them with an LED flag (matrix), else the two that look different
        /// on rows and columns. There, Checker looks like Alternate and is shown as such (flags set to it become Alternate).
        /// </summary>
        public PatternChoice[] PatternChoices => HasMatrix ? PatternChoice.WithMatrix : PatternChoice.RowsAndColumns;

        private bool HasMatrix => Part.Groups.Any(g => g != null && g.IsMatrix);

        private void LayoutChanged()
        {
            if (!HasMatrix)
            {
                foreach (var rule in Part.Flags.Rules.Where(r => r.Pattern == FlagPattern.Checker)) rule.Pattern = FlagPattern.Alternate;
            }

            OnPropertyChanged(nameof(PatternChoices));
            OnPropertyChanged(nameof(HeaderSummary));

            // Every edit of a group comes here (typing a name, dragging a count), so the overlaps are counted once per change.
            var overlaps = Part.CountOverlappingLeds();
            if (overlaps == OverlapCount) return;
            OverlapCount = overlaps;
            OnPropertyChanged(nameof(OverlapCount));
            OnPropertyChanged(nameof(HasOverlap));
            OnPropertyChanged(nameof(OverlapWarning));
        }

        private static void MoveIndex<T>(ObservableCollection<T> list, int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= list.Count || newIndex < 0 || newIndex >= list.Count || oldIndex == newIndex) return;
            list.Move(oldIndex, newIndex);
        }

        private void Groups_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null) foreach (LedGroup g in e.OldItems) g.PropertyChanged -= Group_PropertyChanged;
            if (e.NewItems != null) foreach (LedGroup g in e.NewItems) g.PropertyChanged += Group_PropertyChanged;
            RebuildTargets();
            RebuildWiring();
            OnPropertyChanged(nameof(HasWiring));
            LayoutChanged();
        }

        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LedGroup.StartIndex) || e.PropertyName == nameof(LedGroup.Kind)) RebuildWiring();
            if (e.PropertyName == nameof(LedGroup.Name)) OnPropertyChanged(nameof(WiringSummary));

            if (e.PropertyName == nameof(LedGroup.Kind) || e.PropertyName == nameof(LedGroup.Name) || e.PropertyName == nameof(LedGroup.Side) || e.PropertyName == nameof(LedGroup.LedCount))
            {
                RebuildTargets();
            }

            LayoutChanged();
        }

        private void LayerPriority_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e) => RebuildLayers();

        private void RebuildLayers()
        {
            Layers.Clear();
            foreach (var layer in Part.LayerPriority) Layers.Add(new LayerItem(Part, layer));
        }

        /// <summary>
        /// The target chips of a feature's tab, or null for a feature without a tab (it only drives front plate elements,
        /// which are set up in the inspector). A test checks that every feature that can drive rows, columns or matrices
        /// has one.
        /// </summary>
        public ObservableCollection<TargetOption> TargetsOf(LayerKind kind)
        {
            switch (kind)
            {
                case LayerKind.Rpm: return RpmTargets;
                case LayerKind.Flags: return FlagTargets;
                case LayerKind.Proximity: return ProximityTargets;
                case LayerKind.Abs: return AbsTargets;
                case LayerKind.TractionControl: return TcTargets;
                case LayerKind.Idle: return IdleTargets;
                case LayerKind.Engine: return EngineTargets;
                case LayerKind.PitLimiter: return PitLimiterTargets;
                default: return null;
            }
        }

        private void RebuildTargets()
        {
            foreach (var feature in DevicePart.AllFeatures)
            {
                var targets = TargetsOf(feature.Kind);
                if (targets != null) Fill(targets, feature.SettingsOf(Part));
            }

            OnPropertyChanged(nameof(HasRpmTargets));
            OnPropertyChanged(nameof(HasElements));
        }

        private void Fill(ObservableCollection<TargetOption> options, FeatureSettingsBase feature)
        {
            options.Clear();
            // Front plate elements get their effect in the preview's inspector (one effect each), not in the feature lists.
            foreach (var group in Part.Groups.Where(g => !g.IsElement && feature.AllowedKinds.Contains(g.Kind)))
            {
                options.Add(new TargetOption(feature, group));
            }
        }
    }

    /// <summary>Root view model of the plugin settings page.</summary>
    public class SettingsViewModel : ObservableObject
    {
        private DeviceConfig selectedDevice;
        private DevicePart selectedPart;
        private DeviceViewModel selectedDeviceViewModel;
        private string carDataStatus;
        private string carDataVersion;
        private string carDataState = "idle";
        private string liveStatus;
        private string liveTitle;
        private string liveState = "idle";
        private int deviceListRevision;
        private bool attached = true;
        private string updateText;
        private string updateDetail;
        private string updateState = "idle";
        private bool canInstallUpdate;
        private bool hasNewRelease;

        public SettingsViewModel(FreeTheLightsPlugin plugin)
        {
            Plugin = plugin;
            DuplicateDeviceCommand = new RelayCommand(_ => DuplicateDevice(), _ => SelectedDevice != null);
            RemoveDeviceCommand = new RelayCommand(_ => RemoveDevice(), _ => SelectedDevice != null);
            ReloadCarDataCommand = new RelayCommand(_ => Plugin.CarData.Reload());
            ClearTestsCommand = new RelayCommand(_ => TestMode.Clear());
            EngineStartCommand = new RelayCommand(_ => TestMode.RequestEngineStart());
            EngineStopCommand = new RelayCommand(_ => TestMode.RequestEngineStop());
            // No CanExecute: the button only shows while an update can be installed (CanInstallUpdate), and WPF would only
            // ask again on its next requery, leaving the button disabled after the status changed.
            InstallUpdateCommand = new RelayCommand(_ => InstallUpdate());
            CheckForUpdatesNowCommand = new RelayCommand(_ => Plugin.Updates?.Check(force: true), _ => Plugin.Updates != null);
            OpenReleaseNotesCommand = new RelayCommand(_ => OpenReleaseNotes());
            Inspector.InputSource = plugin.Inputs;
            SelectedDevice = Devices.FirstOrDefault();
        }

        public FreeTheLightsPlugin Plugin { get; }

        /// <summary>
        /// Stops following the settings (the page was unloaded). They live as long as the plugin, so without this every
        /// settings page SimHub ever created would stay in memory and keep reacting to edits.
        /// </summary>
        public void Detach()
        {
            if (!attached) return;
            attached = false;
            if (selectedDevice != null) selectedDevice.PropertyChanged -= SelectedDevice_PropertyChanged;
            selectedDeviceViewModel?.Detach();
            Inspector.Detach();
        }

        /// <summary>Follows the settings again (the page is shown again) and catches up on what changed meanwhile.</summary>
        public void Attach()
        {
            if (attached) return;
            attached = true;
            if (selectedDevice != null) selectedDevice.PropertyChanged += SelectedDevice_PropertyChanged;
            selectedDeviceViewModel?.Attach();
            Inspector.Attach();
            OnPropertyChanged(nameof(CanPlaceLedsOnPhoto));
            OnPropertyChanged(nameof(DeviceSummary));
        }

        /// <summary>What is selected in the device preview and the panel next to it that edits it.</summary>
        public LedInspector Inspector { get; } = new LedInspector();

        public ObservableCollection<DeviceConfig> Devices => Plugin.Settings.Devices;

        public TestMode TestMode => Plugin.TestMode;

        public PluginSettings Settings => Plugin.Settings;

        public DeviceConfig SelectedDevice
        {
            get => selectedDevice;
            set
            {
                var old = selectedDevice;
                if (!Set(ref selectedDevice, value)) return;
                if (old != null) old.PropertyChanged -= SelectedDevice_PropertyChanged;
                if (value != null && attached) value.PropertyChanged += SelectedDevice_PropertyChanged;
                OnPropertyChanged(nameof(HasSeveralParts));
                OnPropertyChanged(nameof(DeviceSummary));
                OnPropertyChanged(nameof(CanPlaceLedsOnPhoto));
                SelectedPart = value?.Parts.FirstOrDefault();

                // Also clears the selection when the device changed but the part didn't (no devices left, or the same part).
                Inspector.SetPart(selectedPart);
            }
        }

        /// <summary>The selected device is a dashboard from SimHub with a picture its LEDs can be placed on.</summary>
        public bool CanPlaceLedsOnPhoto => PhotoLedDialog.CanEdit(SelectedDevice);

        private void SelectedDevice_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DeviceConfig.Type) || e.PropertyName == nameof(DeviceConfig.Preset)) OnPropertyChanged(nameof(CanPlaceLedsOnPhoto));
        }

        /// <summary>The part (LED channel) of the selected device whose settings are shown below the part switcher.</summary>
        public DevicePart SelectedPart
        {
            get => selectedPart;
            set
            {
                if (value != null && selectedDevice != null && !selectedDevice.Parts.Contains(value)) return;
                if (!Set(ref selectedPart, value)) return;
                selectedDeviceViewModel?.Dispose();
                var partViewModel = value == null ? null : new DeviceViewModel(value);
                if (!attached) partViewModel?.Detach();
                SelectedDeviceViewModel = partViewModel;

                // The inspector follows the part switcher and clicks in the preview on another part; its selection is cleared.
                if (Inspector.Part != value) Inspector.SetPart(value);
            }
        }

        /// <summary>The part switcher is only shown for devices with more than one part.</summary>
        public bool HasSeveralParts => SelectedDevice?.Parts.Count > 1;

        /// <summary>One line under the device name, e.g. "Shift lights: 21 LEDs · Buttons: 14 LEDs".</summary>
        public string DeviceSummary
        {
            get
            {
                var device = SelectedDevice;
                if (device == null) return string.Empty;
                if (device.Parts.Count == 1) return SelectedDeviceViewModel?.HeaderSummary ?? string.Empty;
                return string.Join(" · ", device.Parts.Select(p => $"{p.Name}: {p.TotalLeds} LED{(p.TotalLeds == 1 ? string.Empty : "s")}"));
            }
        }

        public DeviceViewModel SelectedDeviceViewModel
        {
            get => selectedDeviceViewModel;
            private set
            {
                if (selectedDeviceViewModel != null) selectedDeviceViewModel.PropertyChanged -= SelectedDeviceViewModel_PropertyChanged;
                Set(ref selectedDeviceViewModel, value);
                if (value != null) value.PropertyChanged += SelectedDeviceViewModel_PropertyChanged;
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(DeviceSummary));
            }
        }

        public bool HasSelection => SelectedDeviceViewModel != null;

        public bool HasNoDevices => Devices.Count == 0;

        /// <summary>Changes whenever the selected device's layout or features change, so the device list rebuilds its summary lines.</summary>
        public int DeviceListRevision { get => deviceListRevision; private set => Set(ref deviceListRevision, value); }

        public string CarDataStatus { get => carDataStatus; private set => Set(ref carDataStatus, value); }

        public string CarDataVersion { get => carDataVersion; private set => Set(ref carDataVersion, value); }

        /// <summary>Car data status dot: "ok", "busy", "warn" or "idle".</summary>
        public string CarDataState { get => carDataState; private set => Set(ref carDataState, value); }

        public string LiveStatus { get => liveStatus; private set => Set(ref liveStatus, value); }

        /// <summary>First line of the live status: the game and car, or that no game is running.</summary>
        public string LiveTitle { get => liveTitle; private set => Set(ref liveTitle, value); }

        /// <summary>Live status dot: "ok" while a game runs, "busy" in test mode, otherwise "idle".</summary>
        public string LiveState { get => liveState; private set => Set(ref liveState, value); }

        public ICommand DuplicateDeviceCommand { get; }
        public ICommand RemoveDeviceCommand { get; }
        public ICommand ReloadCarDataCommand { get; }
        public ICommand ClearTestsCommand { get; }

        public ICommand EngineStartCommand { get; }

        public ICommand EngineStopCommand { get; }
        public ICommand InstallUpdateCommand { get; }
        public ICommand CheckForUpdatesNowCommand { get; }
        public ICommand OpenReleaseNotesCommand { get; }

        /// <summary>The running plugin's version, e.g. "Version 0.1.0".</summary>
        public string PluginVersionText
        {
            get
            {
                var version = Plugin.Updates?.Installed ?? typeof(FreeTheLightsPlugin).Assembly.GetName().Version;
                return "Version " + ReleaseInfo.Normalize(version);
            }
        }

        /// <summary>What the update check found, e.g. "Version 0.3.0 is available".</summary>
        public string UpdateText { get => updateText; private set => Set(ref updateText, value); }

        /// <summary>Tooltip of <see cref="UpdateText"/>: when it was checked, or what went wrong.</summary>
        public string UpdateDetail { get => updateDetail; private set => Set(ref updateDetail, value); }

        /// <summary>Update status dot: "ok" up to date, "busy" an update to install or a restart to do, "warn" a failure, otherwise "idle".</summary>
        public string UpdateState { get => updateState; private set => Set(ref updateState, value); }

        /// <summary>Whether the "Update plugin" button shows.</summary>
        public bool CanInstallUpdate { get => canInstallUpdate; private set => Set(ref canInstallUpdate, value); }

        /// <summary>Whether a release newer than the running plugin is known (its notes can be opened).</summary>
        public bool HasNewRelease { get => hasNewRelease; private set => Set(ref hasNewRelease, value); }

        /// <summary>The "Check for updates" switch; turning it on checks at once.</summary>
        public bool CheckForUpdates
        {
            get => Settings.CheckForUpdates;
            set
            {
                if (value == Settings.CheckForUpdates) return;
                Settings.CheckForUpdates = value;
                OnPropertyChanged();
                Plugin.SaveSettings();
                if (value) Plugin.Updates?.Check();
                RefreshUpdateStatus();
            }
        }

        private void InstallUpdate()
        {
            Plugin.Updates?.Install();
            RefreshUpdateStatus();
        }

        private void OpenReleaseNotes()
        {
            var url = Plugin.Updates?.Latest?.NotesUrl ?? ReleaseInfo.ReleasesPageUrl;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Plugin.LogErrorThrottled("FreeTheLights: could not open the release notes", ex);
            }
        }

        private void RefreshUpdateStatus()
        {
            var updates = Plugin.Updates;
            if (updates == null)
            {
                UpdateText = null;
                UpdateState = "idle";
                CanInstallUpdate = false;
                HasNewRelease = false;
                return;
            }

            var latest = updates.Latest;
            var version = latest?.Version;
            var checkedUtc = updates.CheckedUtc;
            var checkedText = checkedUtc.HasValue
                ? "Checked " + checkedUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
                  + (updates.Offline ? " (offline)" : string.Empty)
                : null;

            HasNewRelease = latest != null && latest.IsNewerThan(updates.Installed);
            CanInstallUpdate = updates.CanInstall;
            switch (updates.Status)
            {
                case UpdateStatus.Checking:
                    UpdateText = "Looking for updates...";
                    UpdateDetail = null;
                    UpdateState = "idle";
                    break;
                case UpdateStatus.UpToDate:
                    UpdateText = "Up to date";
                    UpdateDetail = checkedText;
                    UpdateState = "ok";
                    break;
                case UpdateStatus.Available:
                    UpdateText = latest.CanInstall ? $"Version {version} is available" : $"Version {version} is available on GitHub";
                    UpdateDetail = checkedText;
                    UpdateState = "busy";
                    break;
                case UpdateStatus.CheckFailed:
                    UpdateText = "Couldn't look for updates";
                    UpdateDetail = updates.Error;
                    UpdateState = "warn";
                    break;
                case UpdateStatus.Installing:
                    UpdateText = $"Installing version {version}...";
                    UpdateDetail = null;
                    UpdateState = "busy";
                    break;
                case UpdateStatus.Installed:
                    UpdateText = $"Version {version} is installed. Restart SimHub to use it.";
                    UpdateDetail = null;
                    UpdateState = "busy";
                    break;
                case UpdateStatus.InstallFailed:
                    UpdateText = updates.Error ?? "The update failed.";
                    UpdateDetail = null;
                    UpdateState = "warn";
                    break;
                default:
                    UpdateText = Settings.CheckForUpdates ? null : "Not looking for updates";
                    UpdateDetail = null;
                    UpdateState = "idle";
                    break;
            }
        }

        public Array GroupSides => Enum.GetValues(typeof(LedGroupSide));
        public Array FallbackModes => Enum.GetValues(typeof(RpmFallbackMode));
        public Array FillOrigins => Enum.GetValues(typeof(FillOrigin));
        public Array IdleAnimations => Enum.GetValues(typeof(IdleAnimation));
        public Array AnimationColorModes => Enum.GetValues(typeof(AnimationColorMode));
        public Array EngineAnimations => Enum.GetValues(typeof(EngineAnimation));
        public Array PitLimiterAnimations => Enum.GetValues(typeof(PitLimiterAnimation));

        /// <summary>Called periodically by the view to refresh status texts.</summary>
        public void RefreshStatus()
        {
            CarDataStatus = Plugin.CarData?.StatusText ?? "No car yet";
            CarDataVersion = Plugin.CarData?.VersionText;
            CarDataState = StateOf(Plugin.CarData?.Status);
            RefreshUpdateStatus();
            var t = Plugin.LastTelemetry;
            if (t == null) return;

            LiveTitle = t.GameRunning
                ? string.Join(" · ", new[] { t.GameName, t.CarId }.Where(s => !string.IsNullOrEmpty(s)))
                : TestMode.AnyActive ? "Test mode" : "Game not running";
            LiveState = t.GameRunning ? "ok" : TestMode.AnyActive ? "busy" : "idle";
            if (!t.GameRunning)
            {
                LiveStatus = TestMode.AnyActive ? "The tests are lighting the LEDs." : "Start a game or use the tests below to light up the LEDs.";
                return;
            }

            var flags = new[]
            {
                t.FlagYellow ? "yellow" : null,
                t.FlagBlue ? "blue" : null,
                t.FlagWhite ? "white" : null,
                t.FlagBlack ? "black" : null,
                t.FlagCheckered ? "checkered" : null,
                t.FlagGreen ? "green" : null,
                t.FlagOrange ? "orange" : null,
            }.Where(f => f != null).ToArray();

            LiveStatus = $"{t.Rpm:F0} rpm · gear {t.Gear} · flags: {(flags.Length == 0 ? "none" : string.Join(", ", flags))}"
                         + (t.SpotterCarLeft ? $" · car left {t.SpotterCarLeftDistance:F1} m" : string.Empty)
                         + (t.SpotterCarRight ? $" · car right {t.SpotterCarRightDistance:F1} m" : string.Empty)
                         + (t.AbsActive ? " · ABS" : string.Empty)
                         + (t.TcActive ? " · TC" : string.Empty)
                         + (TestMode.AnyActive ? " · tests on" : string.Empty);
        }

        private static string StateOf(CarData.CarDataStatus? status)
        {
            switch (status)
            {
                case CarData.CarDataStatus.Loaded:
                case CarData.CarDataStatus.LoadedFromCache:
                    return "ok";
                case CarData.CarDataStatus.Loading:
                    return "busy";
                case CarData.CarDataStatus.NotFound:
                case CarData.CarDataStatus.Error:
                    return "warn";
                default:
                    return "idle";
            }
        }

        private void SelectedDeviceViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DeviceViewModel.HeaderSummary)) return;
            DeviceListRevision++;
            OnPropertyChanged(nameof(DeviceSummary));
        }

        /// <summary>Whether each part of the selected device is shown in its SimHub LED profile (preset devices only).</summary>
        public ObservableCollection<PartConnection> Connections { get; } = new ObservableCollection<PartConnection>();

        public bool HasConnections => Connections.Count > 0;

        public void SetConnections(IEnumerable<PartConnection> connections)
        {
            Connections.Clear();
            foreach (var connection in connections ?? Enumerable.Empty<PartConnection>()) Connections.Add(connection);
            OnPropertyChanged(nameof(HasConnections));
        }

        /// <summary>Call after the selected device's parts changed (for example a refresh from SimHub).</summary>
        public void PartsChanged()
        {
            if (SelectedDevice != null && (SelectedPart == null || !SelectedDevice.Parts.Contains(SelectedPart))) SelectedPart = SelectedDevice.Parts.FirstOrDefault();
            if (Inspector.Part != null && (SelectedDevice == null || !SelectedDevice.Parts.Contains(Inspector.Part))) Inspector.SetPart(SelectedPart);
            Inspector.Refresh();
            OnPropertyChanged(nameof(HasSeveralParts));
            OnPropertyChanged(nameof(DeviceSummary));
            OnPropertyChanged(nameof(CanPlaceLedsOnPhoto));
            DeviceListRevision++;
        }

        /// <summary>Adds a device created by the setup wizard and selects it.</summary>
        public void AddDevice(DeviceConfig device)
        {
            Devices.Add(device);
            SelectedDevice = device;
            OnPropertyChanged(nameof(HasNoDevices));
        }

        private void DuplicateDevice()
        {
            var copy = SelectedDevice.Clone();
            Devices.Insert(Devices.IndexOf(SelectedDevice) + 1, copy);
            SelectedDevice = copy;
        }

        private void RemoveDevice()
        {
            var index = Devices.IndexOf(SelectedDevice);
            Devices.Remove(SelectedDevice);
            SelectedDevice = Devices.Count == 0 ? null : Devices[Math.Min(index, Devices.Count - 1)];
            OnPropertyChanged(nameof(HasNoDevices));
        }
    }
}
