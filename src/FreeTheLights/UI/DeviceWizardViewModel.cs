using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    public enum DeviceTemplate
    {
        Wheel,
        Dashboard,
        LedBar,
        LedFlag,
        Custom,
    }

    /// <summary>
    /// Step-by-step setup of a new device:
    /// <list type="bullet">
    /// <item>generic devices: device → LED layout → front plate (wheels and custom devices) → summary;</item>
    /// <item>SimHub devices: device → kind (best guess preselected) → LED layout (dashboards and LED flags) → LED
    /// positions on the product picture (dashboards) → summary.</item>
    /// </list>
    /// Features are not asked for: a new device has all of them on, and they are set up on the settings page.
    /// The generic layout is kept as up to four groups (shift light row, left column, right column, LED flag matrix),
    /// chained on the LED strip in a default order. Order and direction are checked later on the device page, where a
    /// live test pattern shows them on the device.
    /// </summary>
    public class DeviceWizardViewModel : ObservableObject
    {
        // Grouped by topic: the device (which one, its kind, its SimHub profile), its LEDs (layout, positions, wiring),
        // its buttons (front plate, inputs).
        public const int StepDevice = 0;
        public const int StepKind = 1;
        public const int StepProfile = 2;
        public const int StepLayout = 3;
        public const int StepPhoto = 4;
        public const int StepWiring = 5;
        public const int StepFrontPlate = 6;
        public const int StepButtons = 7;
        public const int StepFinish = 8;

        private readonly LedGroup row = new LedGroup { Name = "RPM LEDs", Kind = LedGroupKind.HorizontalRow };
        private readonly LedGroup left = new LedGroup { Name = "Left column", Kind = LedGroupKind.VerticalColumn, Side = LedGroupSide.Left };
        private readonly LedGroup right = new LedGroup { Name = "Right column", Kind = LedGroupKind.VerticalColumn, Side = LedGroupSide.Right };
        private readonly LedGroup matrix = new LedGroup { Name = "LED flag", Kind = LedGroupKind.Matrix };

        private int step;
        private DeviceTemplate template;
        private string name;
        private bool nameEdited;
        private int rowCount;
        private int leftCount;
        private int rightCount;
        private bool matrixEnabled;
        private DialPreset dialPreset = FrontPlatePresets.Dials[0];
        private SidePreset sidePreset = FrontPlatePresets.Sides[0];
        private readonly Func<SimHubDeviceInfo, SimHubButtonLayout> loadLayout;
        private readonly Dictionary<string, SimHubButtonLayout> layouts = new Dictionary<string, SimHubButtonLayout>();
        private DeviceChoice selectedChoice;
        private SimHubDeviceInfo preset;
        private DeviceType selectedKind;
        private string searchText = string.Empty;
        private string catalogStatus = "Loading SimHub's devices...";
        private readonly Func<SimHubDeviceInfo, DashPhotoResult> loadPhoto;
        private DashPhotoResult photo;
        private string photoLoadedFor;
        private bool usePhoto = true;
        private bool photoEdited;
        private Guid deviceId = Guid.NewGuid();
        private readonly Dictionary<string, Guid> partIds = new Dictionary<string, Guid>();
        private DeviceConfig draft;
        private bool draftDirty = true;
        private readonly IWizardHost host;
        private readonly Dictionary<string, bool> simHubHasDevice = new Dictionary<string, bool>();

        /// <param name="loadLayout">Reads SimHub's button layout of a device (UI thread); null outside SimHub.</param>
        /// <param name="loadPhoto">Finds a dashboard's LED positions on its product picture; null outside SimHub.</param>
        /// <param name="host">SimHub, for the steps that work on the real device; null skips them.</param>
        public DeviceWizardViewModel(Func<SimHubDeviceInfo, SimHubButtonLayout> loadLayout = null, Func<SimHubDeviceInfo, DashPhotoResult> loadPhoto = null, IWizardHost host = null)
        {
            this.loadLayout = loadLayout;
            this.loadPhoto = loadPhoto;
            this.host = host;
            Profile = new WizardProfileStep(host, Build);
            Profile.Changed += (s, e) =>
            {
                // A connected LED channel replaces the layout step by the wiring step, and turns the confirmation on.
                StepsChanged();
                ShowConfirmationPattern();
            };
            SkipButtonCommand = new RelayCommand(_ => buttons?.Skip(), _ => buttons != null && !buttons.IsDone);
            PreviousButtonCommand = new RelayCommand(_ => buttons?.Back(), _ => buttons != null && buttons.CanGoBack);
            Groups.CollectionChanged += (s, e) => LayoutChanged();
            foreach (var group in new[] { row, left, right, matrix }) group.PropertyChanged += Group_PropertyChanged;

            ResetPhotoCommand = new RelayCommand(_ => ResetPhoto(), _ => HasPhoto);
            NextCommand = new RelayCommand(_ => Step = NeighbourStep(1), _ => CanGoNext);
            BackCommand = new RelayCommand(_ => Step = NeighbourStep(-1), _ => CanGoBack);
            SelectTemplateCommand = new RelayCommand(p => { if (p is DeviceTemplate t) Template = t; else if (p is string s) Template = (DeviceTemplate)Enum.Parse(typeof(DeviceTemplate), s); });

            foreach (var choice in DeviceChoice.Generic) Choices.Add(choice);
            ChoicesView = new ListCollectionView(Choices) { Filter = o => ((DeviceChoice)o).Matches(SearchText) };
            ChoicesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DeviceChoice.Group)));
            KindOptions = KindOption.All(this);

            ApplyTemplate(DeviceTemplate.Wheel);
            selectedChoice = Choices[0];
        }

        /// <summary>Raised whenever the resulting device changes (for the live preview).</summary>
        public event EventHandler PreviewChanged;

        /// <summary>
        /// Header of the step indicator. The kind step only exists for SimHub devices, the layout step for generic
        /// devices and SimHub dashboards/LED flags, the LED positions step for SimHub dashboards with a picture, the
        /// front plate step for generic wheels and custom devices.
        /// </summary>
        public StepHeader[] StepHeaders
        {
            get
            {
                var steps = ActiveSteps;
                var current = Array.IndexOf(steps, Step);
                return steps.Select((s, i) => new StepHeader(i + 1, StepName(s), i == current, i < current, i < steps.Length - 1)).ToArray();
            }
        }

        private int[] ActiveSteps
        {
            get
            {
                var steps = new List<int> { StepDevice };
                if (IsSimHubDevice) steps.Add(StepKind);
                if (HasProfileStep) steps.Add(StepProfile);
                if (HasLayoutStep) steps.Add(StepLayout);
                if (HasPhotoStep) steps.Add(StepPhoto);
                if (HasWiringStep) steps.Add(StepWiring);
                if (HasFrontPlateStep) steps.Add(StepFrontPlate);
                if (HasButtonsStep) steps.Add(StepButtons);
                steps.Add(StepFinish);
                return steps.ToArray();
            }
        }

        public int Step
        {
            get => step;
            set
            {
                var clamped = Math.Max(StepDevice, Math.Min(StepFinish, value));
                if (clamped > StepKind) EnsurePhoto();
                if (clamped == StepPhoto && HasPhoto && UsePhoto) DashPhotoLayouts.EnsurePositions(PhotoGroups, photo.PictureWidth, photo.PictureHeight);
                if (!Set(ref step, clamped)) return;
                if (step == StepProfile) RefreshConnections();
                if (step == StepWiring) RefreshWiringParts();
                ShowWiringPattern();
                ShowConfirmationPattern();
                UpdateButtonAssignment();
                OnPropertyChanged(nameof(DeviceToRender));
                OnPropertyChanged(nameof(CanGoBack));
                OnPropertyChanged(nameof(CanGoNext));
                OnPropertyChanged(nameof(IsLastStep));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(StepHeaders));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool CanGoBack => Step > StepDevice;

        /// <summary>A device is picked, and it has at least one LED: in its groups or (for wheels) on the front plate.</summary>
        public bool CanGoNext => Step < StepFinish
                                 && (Step != StepDevice || SelectedChoice != null)
                                 && (Step != StepLayout || TotalLeds > 0 || HasFrontPlateStep)
                                 && (Step != StepFrontPlate || TotalLeds + ElementCount > 0);

        public bool IsLastStep => Step == StepFinish;

        public ICommand NextCommand { get; }

        public ICommand BackCommand { get; }

        public ICommand SelectTemplateCommand { get; }

        // ---------- Step 1: device and name ----------

        /// <summary>The generic devices first, then every SimHub device with LEDs (see <see cref="SetSimHubDevices"/>).</summary>
        public ObservableCollection<DeviceChoice> Choices { get; } = new ObservableCollection<DeviceChoice>();

        /// <summary><see cref="Choices"/> filtered by <see cref="SearchText"/> and grouped (generic, then by brand).</summary>
        public ListCollectionView ChoicesView { get; }

        public string SearchText
        {
            get => searchText;
            set
            {
                if (!Set(ref searchText, value ?? string.Empty)) return;
                ChoicesView.Refresh();
            }
        }

        /// <summary>Whether SimHub's device list is loading, how many devices it has, or that it couldn't be read.</summary>
        public string CatalogStatus { get => catalogStatus; private set => Set(ref catalogStatus, value); }

        /// <summary>Adds SimHub's devices to the list (null = they could not be read).</summary>
        public void SetSimHubDevices(IEnumerable<SimHubDeviceInfo> devices)
        {
            var list = devices?.ToList();
            if (list == null)
            {
                CatalogStatus = "The device list couldn't be loaded. You can still add a generic device.";
                return;
            }

            foreach (var device in list) Choices.Add(DeviceChoice.ForSimHub(device));
            CatalogStatus = list.Count == 0 ? "No devices with LEDs found." : $"{list.Count} devices";
        }

        public DeviceChoice SelectedChoice
        {
            get => selectedChoice;
            set
            {
                if (!Set(ref selectedChoice, value) || value == null) return;
                ForgetHardware();
                Profile.Reset();
                if (value.Preset != null)
                {
                    preset = value.Preset;
                    var (kind, reason) = DeviceKindGuess.Guess(preset);
                    SuggestedKind = kind;
                    KindReason = reason;
                    nameEdited = false;
                    name = preset.Name;
                    OnPropertyChanged(nameof(Name));
                    ApplyKind(kind);
                }
                else
                {
                    preset = null;
                    ApplyTemplate(value.Template ?? DeviceTemplate.Custom);
                }

                OnPropertyChanged(nameof(Preset));
                OnPropertyChanged(nameof(IsSimHubDevice));
                OnPropertyChanged(nameof(SuggestedKind));
                OnPropertyChanged(nameof(KindReason));
                OnPropertyChanged(nameof(KindQuestion));
                OnPropertyChanged(nameof(PresetCapabilities));
                StepsChanged();
            }
        }

        /// <summary>
        /// Other hardware was picked: what the wizard set up in SimHub for the previous one is undone, and the device gets new
        /// ids, so no profile set up for the previous hardware shows it. (Layout changes for the same hardware keep the ids.)
        /// </summary>
        private void ForgetHardware()
        {
            try
            {
                host?.UndoConnections();
            }
            catch (Exception)
            {
                // Nothing more the wizard can do; SimHub's settings were backed up before they were changed.
            }

            deviceId = Guid.NewGuid();
            partIds.Clear();
            draft = null;
            draftDirty = true;
        }

        /// <summary>The SimHub device picked in step 1, or null for a generic device.</summary>
        public SimHubDeviceInfo Preset => preset;

        public bool IsSimHubDevice => preset != null;

        public DeviceTemplate Template
        {
            get => template;
            set
            {
                if (template == value) return;
                ApplyTemplate(value);
            }
        }

        public string Name
        {
            get => name;
            set
            {
                if (!Set(ref name, value)) return;
                nameEdited = true;

                // Only the name changes: the device keeps its wiring and inputs.
                if (draft != null) draft.Name = DeviceName;
            }
        }

        // ---------- Step 2 (SimHub devices): kind ----------

        public IReadOnlyList<KindOption> KindOptions { get; }

        /// <summary>The kind the user chose for the SimHub device (preselected: <see cref="SuggestedKind"/>).</summary>
        public DeviceType SelectedKind
        {
            get => selectedKind;
            set
            {
                if (selectedKind == value && preset != null) return;
                if (preset != null) ApplyKind(value);
            }
        }

        public DeviceType SuggestedKind { get; private set; }

        /// <summary>Why <see cref="SuggestedKind"/> is suggested, e.g. "its name contains "dashboard"".</summary>
        public string KindReason { get; private set; }

        public string KindQuestion => preset == null ? string.Empty : $"What kind of device is the {preset.Name}?";

        /// <summary>What SimHub tells about the device: display, LED channels, button layout.</summary>
        public string PresetCapabilities => preset == null
            ? string.Empty
            : (preset.HasDisplay ? "Display · " : string.Empty) + preset.ChannelSummary;

        // ---------- SimHub devices: the LED profile ----------

        /// <summary>
        /// A SimHub device that is added in SimHub's Devices: FreeTheLights can set up its LED profile (and then show the
        /// wiring test pattern on it). Generic devices, and devices SimHub doesn't have, are connected by hand later.
        /// </summary>
        public bool HasProfileStep
        {
            get
            {
                if (preset == null || host == null) return false;
                if (!simHubHasDevice.TryGetValue(preset.DeviceTypeId, out var has))
                {
                    try
                    {
                        has = host.HasSimHubDevice(preset.DeviceTypeId);
                    }
                    catch (Exception)
                    {
                        has = false;
                    }

                    simHubHasDevice[preset.DeviceTypeId] = has;
                }

                return has;
            }
        }

        /// <summary>The profile step: which parts SimHub's profiles show, and setting them up.</summary>
        public WizardProfileStep Profile { get; }

        /// <summary>
        /// The device the game loop renders while the wizard is open (null before the profile step): the SimHub profile
        /// set up in the wizard shows it.
        /// </summary>
        public DeviceConfig DeviceToRender
        {
            get
            {
                if (step < StepProfile || !HasProfileStep) return null;
                if (step != StepButtons || buttons == null) return Build();

                // The buttons step shows on the device which button to press (the copy is made again only after a change).
                if (displayRevision != buttons.Revision || displayDevice == null)
                {
                    displayDevice = buttons.DisplayDevice();
                    displayRevision = buttons.Revision;
                }

                return displayDevice;
            }
        }

        /// <summary>Checks which parts SimHub's active LED profiles show.</summary>
        public void RefreshConnections() => Profile.Refresh(HasProfileStep);

        /// <summary>Once connected, the device breathes green while the profile step is open: the user sees it worked.</summary>
        private void ShowConfirmationPattern()
        {
            if (host == null) return;
            try
            {
                host.ShowConfirmationPattern(step == StepProfile && Profile.IsConnected ? deviceId : Guid.Empty);
            }
            catch (Exception)
            {
                // The pattern is a help; the wizard goes on without it.
            }
        }

        // ---------- SimHub devices with a profile: the LED wiring ----------

        private DevicePart wiringPart;
        private WiringEditor wiring;

        /// <summary>
        /// Once SimHub's profile shows a part with rows, columns or matrices, the device shows a test pattern, and the user
        /// puts the groups into the order (and direction) they are wired.
        /// </summary>
        public bool HasWiringStep => WiringCandidates().Any();

        /// <summary>The parts whose wiring can be checked on the device (see <see cref="HasWiringStep"/>).</summary>
        public ObservableCollection<DevicePart> WiringParts { get; } = new ObservableCollection<DevicePart>();

        public bool HasSeveralWiringParts => WiringParts.Count > 1;

        /// <summary>The part showing the test pattern; <see cref="Wiring"/> edits its groups.</summary>
        public DevicePart WiringPart
        {
            get => wiringPart;
            set
            {
                if (!Set(ref wiringPart, value)) return;
                Wiring = value == null ? null : new WiringEditor(value, preset?.CountOf(value.Channel) ?? 0);
                ShowWiringPattern();
            }
        }

        /// <summary>The wiring of <see cref="WiringPart"/>, as the step's list edits it (null without a part).</summary>
        public WiringEditor Wiring
        {
            get => wiring;
            private set
            {
                if (wiring == value) return;
                if (wiring != null)
                {
                    wiring.Changed -= Wiring_Changed;
                    wiring.Detach();
                }

                wiring = value;
                if (wiring != null) wiring.Changed += Wiring_Changed;
                OnPropertyChanged();
            }
        }

        private void Wiring_Changed(object sender, EventArgs e) => PreviewChanged?.Invoke(this, EventArgs.Empty);

        /// <summary>Connected parts with rows, columns or matrices, or with an LED channel to make them from.</summary>
        private IEnumerable<DevicePart> WiringCandidates()
        {
            var connected = new HashSet<Guid>(Profile.ConnectedPartIds);
            if (connected.Count == 0 || !HasProfileStep) return Enumerable.Empty<DevicePart>();
            return Build().Parts.Where(p => connected.Contains(p.Id) && (p.GroupsInWiringOrder().Count > 0 || p.Channel == LedChannel.Leds));
        }

        private void RefreshWiringParts()
        {
            var keep = wiringPart?.Id;
            WiringParts.Clear();
            foreach (var part in WiringCandidates()) WiringParts.Add(part);
            OnPropertyChanged(nameof(HasSeveralWiringParts));
            wiringPart = null;
            WiringPart = WiringParts.FirstOrDefault(p => p.Id == keep) ?? WiringParts.FirstOrDefault();
        }

        /// <summary>The device shows the test pattern on <see cref="WiringPart"/> only while the wiring step is open.</summary>
        private void ShowWiringPattern()
        {
            if (host == null) return;
            try
            {
                host.ShowWiringPattern(step == StepWiring && wiringPart != null ? wiringPart.Id : Guid.Empty);
            }
            catch (Exception)
            {
                // The pattern is a help; the wizard goes on without it.
            }
        }

        // ---------- Buttons and dials: their inputs ----------

        private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        private ButtonAssignment buttons;
        private DeviceConfig displayDevice;
        private int displayRevision = -1;

        /// <summary>The device has buttons or dials, and SimHub's inputs are available: the user presses them one by one.</summary>
        public bool HasButtonsStep => host?.Inputs != null && ButtonAssignment.HasInputs(Build());

        /// <summary>The assignment of the buttons step (null before it is opened).</summary>
        public ButtonAssignment Buttons
        {
            get => buttons;
            private set
            {
                if (buttons == value) return;
                if (buttons != null)
                {
                    buttons.Stop();
                    buttons.PropertyChanged -= Buttons_PropertyChanged;
                }

                buttons = value;
                displayDevice = null;
                if (buttons != null) buttons.PropertyChanged += Buttons_PropertyChanged;
                OnPropertyChanged();
            }
        }

        /// <summary>Whether the button asked for lights up on the device too: its part (channel) is shown by the SimHub profile.</summary>
        public bool ButtonsShownOnDevice => buttons?.CurrentPart != null && Profile.IsPartConnected(buttons.CurrentPart.Id);

        public ICommand SkipButtonCommand { get; }

        public ICommand PreviousButtonCommand { get; }

        /// <summary>An input was assigned or skipped (on the UI thread, but not after a mouse or key event): update the buttons and the preview.</summary>
        private void Buttons_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ButtonAssignment.Revision)) return;
            CommandManager.InvalidateRequerySuggested();
            OnPropertyChanged(nameof(ButtonsShownOnDevice));
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The wizard was closed: stop listening to SimHub's inputs.</summary>
        public void Closed() => buttons?.Stop();

        /// <summary>Listens to SimHub's inputs while the buttons step is open; a rebuilt device starts a new assignment.</summary>
        private void UpdateButtonAssignment()
        {
            if (step != StepButtons)
            {
                buttons?.Stop();
                return;
            }

            if (host?.Inputs == null) return;
            var device = Build();
            if (buttons == null || buttons.Device != device) Buttons = new ButtonAssignment(device, host.Inputs, host.Post, () => clock.ElapsedMilliseconds);
            buttons.Start();
            OnPropertyChanged(nameof(ButtonsShownOnDevice));
        }

        // ---------- Step 3: layout ----------

        /// <summary>
        /// The layout step: for generic devices, and for SimHub dashboards (to check the suggested split of the RPM and
        /// side LEDs) and LED flags (matrix size). SimHub wheels take their layout from SimHub.
        /// Once a SimHub dashboard's LED channel is connected, the wiring step sets its groups instead (counts, sides, more
        /// groups), on the device as built, so the two never edit different layouts.
        /// </summary>
        public bool HasLayoutStep => BuildsLayoutPart && !(preset != null && selectedKind == DeviceType.Dashboard && LayoutChannelConnected);

        /// <summary>The wizard builds a part from its own layout (<see cref="Groups"/>): generic devices, SimHub dashboards and LED flags.</summary>
        private bool BuildsLayoutPart => preset == null || selectedKind == DeviceType.Dashboard || selectedKind == DeviceType.LedFlag;

        /// <summary>The SimHub channel of the part built from the layout.</summary>
        private LedChannel LayoutChannel => selectedKind == DeviceType.LedFlag && preset != null && preset.Has(LedChannel.Matrix) ? LedChannel.Matrix : LedChannel.Leds;

        private DevicePart LayoutPart(DeviceConfig device) => device?.Parts.FirstOrDefault(p => p.Channel == LayoutChannel);

        private bool LayoutChannelConnected => Profile.IsChannelConnected(LedChannel.Leds);

        /// <summary>Hint above the layout step for SimHub devices: where the numbers come from.</summary>
        public string LayoutSource
        {
            get
            {
                if (preset == null) return string.Empty;
                if (selectedKind == DeviceType.LedFlag) return "Enter the size of the LED matrix.";
                if (HasPhoto && photo.Layout != null) return "Counted on the picture of the dashboard.";
                var split = preset.SuggestedSplit;
                return split != null && split.Confident
                    ? "Suggested for this device."
                    : "This is a guess. Please check the numbers.";
            }
        }

        /// <summary>Groups in the order they are wired on the device's LED strip.</summary>
        public ObservableCollection<LedGroup> Groups { get; } = new ObservableCollection<LedGroup>();

        public bool ShowStripFields => Template != DeviceTemplate.LedFlag;

        public bool ShowMatrixFields => Template == DeviceTemplate.LedFlag || Template == DeviceTemplate.Custom;

        /// <summary>LEDs in the shift light row, 0 = none.</summary>
        public int RowCount { get => rowCount; set => SetCount(ref rowCount, value, row); }

        /// <summary>LEDs in the left column, 0 = none.</summary>
        public int LeftCount { get => leftCount; set => SetCount(ref leftCount, value, left); }

        /// <summary>LEDs in the right column, 0 = none.</summary>
        public int RightCount { get => rightCount; set => SetCount(ref rightCount, value, right); }

        public bool MatrixEnabled
        {
            get => matrixEnabled;
            set
            {
                if (!Set(ref matrixEnabled, value)) return;
                SyncMembership(matrix, value);
            }
        }

        public int MatrixColumns { get => matrix.Columns; set => matrix.Columns = value; }

        public int MatrixRows { get => matrix.Rows; set => matrix.Rows = value; }

        public int TotalLeds => Groups.Sum(g => g.LedCount);

        public bool HasRow => Groups.Contains(row);

        public bool HasColumns => Groups.Contains(left) || Groups.Contains(right);

        public bool HasMatrix => Groups.Contains(matrix);

        // ---------- Step 4 (SimHub dashboards): LED positions on the product picture ----------

        /// <summary>
        /// SimHub dashboards with a product picture: where the LEDs are on it. The positions come from FreeTheLights'
        /// list or are found on the picture; otherwise the user places them, or picks the generic drawing.
        /// </summary>
        public bool HasPhotoStep => preset != null && selectedKind == DeviceType.Dashboard && preset.ProductImageFile != null && loadPhoto != null
                                    && (photoLoadedFor != preset.DeviceTypeId || photo != null);

        /// <summary>The picture could be read (the LED positions step can be shown).</summary>
        public bool HasPhoto => photo != null && HasPhotoStep;

        /// <summary>The product picture, relative to the SimHub folder.</summary>
        public string PhotoFile => HasPhoto ? photo.PictureFile : null;

        /// <summary>Show the LEDs on the picture; false = the generic dashboard drawing.</summary>
        public bool UsePhoto
        {
            get => usePhoto;
            set
            {
                if (!Set(ref usePhoto, value)) return;
                OnPropertyChanged(nameof(UseGenericDrawing));
                PhotoSourceChanged();
            }
        }

        public bool UseGenericDrawing
        {
            get => !usePhoto;
            set => UsePhoto = !value;
        }

        /// <summary>Where the positions on the picture come from, and what to do.</summary>
        public string PhotoStatus
        {
            get
            {
                if (!HasPhoto) return string.Empty;
                if (photoEdited) return "Positions adjusted by you.";
                switch (photo.Source)
                {
                    case PhotoLedSource.BuiltIn: return "The LEDs are already in place.";
                    case PhotoLedSource.Found: return "The LEDs were found on the picture. Please check them.";
                    default: return "Drag the LEDs to their places, or use the generic drawing.";
                }
            }
        }

        /// <summary>True when the positions still need the user (nothing was found).</summary>
        public bool PhotoNeedsPlacing => HasPhoto && photo.Source == PhotoLedSource.None && !photoEdited;

        public ICommand ResetPhotoCommand { get; }

        /// <summary>Called by the view after the user dragged LEDs on the picture.</summary>
        /// <summary>
        /// The rows and columns the positions step places: those of the device as built, with the wiring step's edits, so
        /// both steps show the same layout and moving LEDs on the picture keeps the wiring.
        /// </summary>
        public IList<LedGroup> PhotoGroups => (IList<LedGroup>)LayoutPart(Build())?.GroupsInWiringOrder() ?? new List<LedGroup>();

        /// <summary>Positions on the picture of the groups while the generic drawing is chosen (by group id), to bring back.</summary>
        private readonly Dictionary<Guid, List<PhotoLed>> hiddenPhotoLeds = new Dictionary<Guid, List<PhotoLed>>();

        /// <summary>The picture positions or the generic drawing changed: applied to the device as built, which is kept.</summary>
        private void PhotoSourceChanged()
        {
            ApplyPhotoSource(Build());

            // Groups added or resized while the generic drawing was chosen get positions for their LED count.
            if (HasPhoto && UsePhoto) DashPhotoLayouts.EnsurePositions(PhotoGroups, photo.PictureWidth, photo.PictureHeight);
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Where the device's LED positions come from (the picture, placed by the user, or the generic drawing). With the
        /// generic drawing the groups have no positions; theirs are kept aside for switching back.
        /// </summary>
        private void ApplyPhotoSource(DeviceConfig device)
        {
            if (!HasPhoto || device?.Preset == null) return;
            device.Preset.LedPositions = !UsePhoto ? PhotoLedSource.Generic
                : photoEdited || photo.Source == PhotoLedSource.None ? PhotoLedSource.Manual
                : photo.Source;
            var part = LayoutPart(device);
            if (part == null) return;
            foreach (var group in part.Groups.Where(g => !g.IsElement))
            {
                if (!UsePhoto)
                {
                    if (group.PhotoLeds?.Count > 0) hiddenPhotoLeds[group.Id] = group.PhotoLeds;
                    group.PhotoLeds = new List<PhotoLed>();
                }
                else if (hiddenPhotoLeds.TryGetValue(group.Id, out var leds))
                {
                    group.PhotoLeds = leds;
                    hiddenPhotoLeds.Remove(group.Id);
                }
            }
        }

        public void PhotoPositionsEdited()
        {
            photoEdited = true;
            PhotoChanged();
            PhotoSourceChanged();
        }

        /// <summary>
        /// Reads the picture's LED positions once the kind is settled (reading it while browsing the device list would
        /// be slow). Found positions also give the dashboard's split of RPM and side LEDs.
        /// </summary>
        private void EnsurePhoto()
        {
            if (preset == null || selectedKind != DeviceType.Dashboard || loadPhoto == null || preset.ProductImageFile == null || photoLoadedFor == preset.DeviceTypeId) return;
            photoLoadedFor = preset.DeviceTypeId;
            photoEdited = false;
            usePhoto = true;
            try
            {
                photo = loadPhoto(preset);
            }
            catch (Exception)
            {
                photo = null;
            }

            if (photo?.Layout != null)
            {
                var split = photo.Layout.Split;
                LeftCount = split.Left;
                RowCount = split.Top;
                RightCount = split.Right;
                Groups.Clear();
                foreach (var group in new[] { left, row, right }.Where(g => g.Count > 0 && (g == left ? split.Left : g == row ? split.Top : split.Right) > 0)) Groups.Add(group);
                DashPhotoLayouts.Apply(Groups, photo.Layout);
            }

            PhotoChanged();
            StepsChanged();
            LayoutChanged();
        }

        private void ResetPhoto()
        {
            if (!HasPhoto) return;
            var groups = PhotoGroups;
            foreach (var group in groups) group.PhotoLeds = new List<PhotoLed>();
            if (photo.Layout != null) DashPhotoLayouts.Apply(groups, photo.Layout);
            DashPhotoLayouts.EnsurePositions(groups, photo.PictureWidth, photo.PictureHeight);
            photoEdited = false;
            PhotoChanged();
            PhotoSourceChanged();
        }

        /// <summary>Forgets the picture positions (another device or kind was picked).</summary>
        private void ForgetPhoto()
        {
            photo = null;
            photoLoadedFor = null;
            photoEdited = false;
            usePhoto = true;
            foreach (var group in new[] { row, left, right }) group.PhotoLeds = new List<PhotoLed>();
            hiddenPhotoLeds.Clear();
            PhotoChanged();
        }

        private void PhotoChanged()
        {
            OnPropertyChanged(nameof(HasPhoto));
            OnPropertyChanged(nameof(LayoutSource));
            OnPropertyChanged(nameof(PhotoFile));
            OnPropertyChanged(nameof(PhotoStatus));
            OnPropertyChanged(nameof(PhotoNeedsPlacing));
            OnPropertyChanged(nameof(UsePhoto));
            OnPropertyChanged(nameof(UseGenericDrawing));
        }

        // ---------- Step 5 (generic wheels and custom devices): front plate ----------

        /// <summary>Generic wheels (and custom devices) can have buttons, lights and dials on the front plate; SimHub devices bring their own.</summary>
        public bool HasFrontPlateStep => preset == null && (Template == DeviceTemplate.Wheel || Template == DeviceTemplate.Custom);

        public DialPreset[] DialPresets => FrontPlatePresets.Dials;

        public SidePreset[] SidePresets => FrontPlatePresets.Sides;

        public DialPreset SelectedDialPreset
        {
            get => dialPreset;
            set
            {
                if (Set(ref dialPreset, value ?? FrontPlatePresets.Dials[0])) LayoutChanged();
            }
        }

        public SidePreset SelectedSidePreset
        {
            get => sidePreset;
            set
            {
                if (Set(ref sidePreset, value ?? FrontPlatePresets.Sides[0])) LayoutChanged();
            }
        }

        /// <summary>Front plate elements the device will get (none when the template has no front plate).</summary>
        public int ElementCount => HasFrontPlateStep ? SelectedDialPreset.Count + 2 * SelectedSidePreset.Count : 0;

        public string FrontPlateHint => ElementCount == 0
            ? "No lit buttons or dials."
            : $"{ElementCount} lit buttons and dials, one LED each.";

        // ---------- Summary ----------

        public string Summary
        {
            get
            {
                if (preset != null) return PresetSummary();
                var groups = Groups.ToList();
                var lines = groups.Select(g => g.IsMatrix ? $"{g.Name}: {g.Columns} × {g.Rows} LEDs" : $"{g.Name}: {g.LedCount} LEDs");
                if (ElementCount > 0)
                {
                    var first = TotalLeds + 1;
                    lines = lines.Concat(new[] { $"Front plate: {SelectedDialPreset.Name.ToLowerInvariant()}, {SelectedSidePreset.Name.ToLowerInvariant()} per side" });
                }

                return $"\"{Name}\" with {TotalLeds + ElementCount} LEDs:\n" + string.Join("\n", lines) + InputSummary();
            }
        }

        /// <summary>How many inputs the buttons step assigned, or nothing when it wasn't opened.</summary>
        private string InputSummary()
        {
            if (buttons == null || buttons.Device != Build()) return string.Empty;
            return $"\n\nInputs: {buttons.AssignedCount} of {buttons.Count} assigned";
        }

        /// <summary>
        /// The configured device: the one the wizard adds. It is built from the wizard's choices when they changed and
        /// kept otherwise, so the later steps can edit it (wiring, inputs). Its ids never change during the wizard: a
        /// SimHub profile set up for it stays connected when it is built again.
        /// </summary>
        public DeviceConfig Build()
        {
            if (draft != null && !draftDirty) return draft;
            var fresh = BuildFresh();

            // The SimHub profiles FreeTheLights created for the parts belong to the same parts in the new build.
            if (draft != null)
            {
                foreach (var part in fresh.Parts)
                {
                    var old = draft.FindPart(part.Id);
                    if (old != null && old.Id == part.Id) part.SimHubProfileIds = new List<Guid>(old.SimHubProfileIds);
                }
            }

            draft = fresh;
            draftDirty = false;
            return draft;
        }

        /// <summary>The name the device gets: the one entered, or "New device".</summary>
        private string DeviceName => string.IsNullOrWhiteSpace(Name) ? "New device" : Name.Trim();

        /// <summary>A new build of the device from the wizard's choices (start indices follow the wiring order), with the wizard's ids.</summary>
        private DeviceConfig BuildFresh()
        {
            var device = preset != null ? BuildFromSimHub() : DeviceConfig.WithPart(string.Empty, TypeOf(Template), BuildLayoutPart(new DevicePart()));
            device.Name = DeviceName;
            device.Id = deviceId;
            var perChannel = new Dictionary<LedChannel, int>();
            foreach (var part in device.Parts)
            {
                perChannel.TryGetValue(part.Channel, out var index);
                perChannel[part.Channel] = index + 1;
                var key = part.Channel + "/" + index;
                if (!partIds.TryGetValue(key, out var id)) partIds[key] = id = Guid.NewGuid();
                part.Id = id;
            }

            return device;
        }

        /// <summary>
        /// A SimHub device: one part per SimHub channel. Where the user checked the layout (dashboards: RPM and side
        /// LEDs; LED flags: the matrix), that part is built from the layout step instead.
        /// </summary>
        private DeviceConfig BuildFromSimHub()
        {
            var device = PresetBuilder.Build(preset, selectedKind, ButtonLayout(), photo: HasPhoto ? photo : null);
            if (!BuildsLayoutPart) return device;

            var channel = LayoutChannel;
            var index = device.Parts.ToList().FindIndex(p => p.Channel == channel);
            var part = BuildLayoutPart(new DevicePart { Name = PresetBuilder.PartName(channel, selectedKind), Channel = channel });
            if (index >= 0) device.Parts[index] = part;
            else device.Parts.Insert(0, part);

            hiddenPhotoLeds.Clear();
            ApplyPhotoSource(device);

            return device;
        }

        /// <summary>The part of the layout step (and, for generic wheels, the front plate step).</summary>
        private DevicePart BuildLayoutPart(DevicePart part)
        {
            part.EnsureDefaults();
            foreach (var group in Groups) part.AddGroup(group.Clone());
            if (HasFrontPlateStep) part.ApplyFrontPlate(SelectedDialPreset.Id, SelectedSidePreset.Id);
            return part;
        }

        /// <summary>SimHub's button layout of the picked device (read once), or null.</summary>
        private SimHubButtonLayout ButtonLayout()
        {
            if (preset == null || !preset.HasButtonLayout || loadLayout == null) return null;
            if (!layouts.TryGetValue(preset.DeviceTypeId, out var layout))
            {
                layout = loadLayout(preset);
                layouts[preset.DeviceTypeId] = layout;
            }

            return layout;
        }

        private string PresetSummary()
        {
            var device = Build();
            var lines = device.Parts.Select(p => $"{p.Name}: {p.TotalLeds} LEDs" +
                                                 (p.Elements.Any() ? $", {p.Elements.Count()} buttons and lights" : string.Empty) +
                                                 (p.Groups.Any(g => !g.IsElement) ? " (" + string.Join(", ", p.Groups.Where(g => !g.IsElement).Select(g => g.Name.ToLowerInvariant())) + ")" : string.Empty));
            return $"\"{device.Name}\" ({DeviceTypeName(selectedKind)}):\n" + string.Join("\n", lines) + InputSummary();
        }

        public static string DeviceTypeName(DeviceType type)
        {
            switch (type)
            {
                case DeviceType.Wheel: return "wheel";
                case DeviceType.Dashboard: return "dashboard";
                case DeviceType.LedBar: return "LED bar";
                case DeviceType.LedFlag: return "LED flag";
                default: return "device";
            }
        }

        /// <summary>The stored device type of a wizard template.</summary>
        public static DeviceType TypeOf(DeviceTemplate template)
        {
            switch (template)
            {
                case DeviceTemplate.Wheel: return DeviceType.Wheel;
                case DeviceTemplate.Dashboard: return DeviceType.Dashboard;
                case DeviceTemplate.LedBar: return DeviceType.LedBar;
                case DeviceTemplate.LedFlag: return DeviceType.LedFlag;
                default: return DeviceType.Custom;
            }
        }

        private static string StepName(int step)
        {
            switch (step)
            {
                case StepDevice: return "Device";
                case StepKind: return "Kind";
                case StepProfile: return "Connect";
                case StepLayout: return "LED layout";
                case StepPhoto: return "LED positions";
                case StepWiring: return "LED wiring";
                case StepFrontPlate: return "Front plate";
                case StepButtons: return "Assign buttons";
                default: return "Finish";
            }
        }

        /// <summary>The next (+1) or previous (-1) step, skipping the steps this device doesn't have.</summary>
        private int NeighbourStep(int direction)
        {
            var steps = ActiveSteps;
            var next = Step + direction;
            while (next > StepDevice && next < StepFinish && !steps.Contains(next)) next += direction;
            return Math.Max(StepDevice, Math.Min(StepFinish, next));
        }

        /// <summary>
        /// Switches the SimHub device to <paramref name="kind"/>: dashboards get the suggested split of their LEDs in the
        /// layout step, LED flags a matrix of the LED count.
        /// </summary>
        private void ApplyKind(DeviceType kind)
        {
            selectedKind = kind;
            ForgetPhoto();
            var keepName = name;
            switch (kind)
            {
                case DeviceType.Dashboard:
                    ApplyTemplate(DeviceTemplate.Dashboard);
                    var total = Math.Max(1, preset.CountOf(LedChannel.Leds));
                    var split = preset.SuggestedSplit != null && preset.SuggestedSplit.Total == total ? preset.SuggestedSplit : new LedSplit(0, total, 0, false);
                    LeftCount = split.Left;
                    RowCount = split.Top;
                    RightCount = split.Right;
                    Groups.Clear();
                    foreach (var group in new[] { left, row, right }.Where(g => g == left ? split.Left > 0 : g == row ? split.Top > 0 : split.Right > 0)) Groups.Add(group);
                    row.Name = "RPM LEDs";
                    left.Name = "Left side";
                    right.Name = "Right side";
                    right.Reversed = true; // wired top to bottom, continuing from the top row (see PresetBuilder)
                    break;
                case DeviceType.LedFlag:
                    ApplyTemplate(DeviceTemplate.LedFlag);
                    var count = preset.CountOf(preset.Has(LedChannel.Matrix) ? LedChannel.Matrix : LedChannel.Leds);
                    MatrixColumns = count > 0 ? (int)Math.Ceiling(Math.Sqrt(count)) : 8;
                    MatrixRows = count > 0 ? (int)Math.Ceiling(count / (double)MatrixColumns) : 8;
                    break;
                default:
                    ApplyTemplate(kind == DeviceType.Wheel ? DeviceTemplate.Wheel : kind == DeviceType.LedBar ? DeviceTemplate.LedBar : DeviceTemplate.Custom);
                    break;
            }

            name = keepName;
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(SelectedKind));
            OnPropertyChanged(nameof(LayoutSource));
            OnPropertyChanged(nameof(MatrixColumns));
            OnPropertyChanged(nameof(MatrixRows));
            foreach (var option in KindOptions) option.Refresh();
            StepsChanged();
            LayoutChanged();
        }

        private void StepsChanged()
        {
            OnPropertyChanged(nameof(HasProfileStep));
            OnPropertyChanged(nameof(HasLayoutStep));
            OnPropertyChanged(nameof(HasPhotoStep));
            OnPropertyChanged(nameof(HasWiringStep));
            OnPropertyChanged(nameof(HasFrontPlateStep));
            OnPropertyChanged(nameof(HasButtonsStep));
            OnPropertyChanged(nameof(StepHeaders));
            OnPropertyChanged(nameof(CanGoNext));
            CommandManager.InvalidateRequerySuggested();
        }

        private static int Start(System.Collections.Generic.IList<LedGroup> groups, int index) => groups.Take(index).Sum(g => g.LedCount);

        private void ApplyTemplate(DeviceTemplate value)
        {
            template = value;
            int r = 0, l = 0, rr = 0;
            var m = false;
            switch (value)
            {
                case DeviceTemplate.Wheel:
                    r = 15; l = 3; rr = 3;
                    break;
                case DeviceTemplate.Dashboard:
                    r = 10; l = 4; rr = 4;
                    break;
                case DeviceTemplate.LedBar:
                    r = 16;
                    break;
                case DeviceTemplate.LedFlag:
                    m = true;
                    matrix.Columns = 8;
                    matrix.Rows = 8;
                    break;
            }

            RowCount = r;
            LeftCount = l;
            RightCount = rr;
            MatrixEnabled = m;

            if (!nameEdited)
            {
                name = DefaultName(value);
                OnPropertyChanged(nameof(Name));
            }

            OnPropertyChanged(nameof(Template));
            OnPropertyChanged(nameof(MatrixColumns));
            OnPropertyChanged(nameof(MatrixRows));
            OnPropertyChanged(nameof(ShowStripFields));
            OnPropertyChanged(nameof(ShowMatrixFields));
            OnPropertyChanged(nameof(HasFrontPlateStep));
            OnPropertyChanged(nameof(HasLayoutStep));
            OnPropertyChanged(nameof(StepHeaders));
            row.Name = "RPM LEDs";
            left.Name = "Left column";
            right.Name = "Right column";
            right.Reversed = false;
            LayoutChanged();
        }

        private static string DefaultName(DeviceTemplate value)
        {
            switch (value)
            {
                case DeviceTemplate.Dashboard: return "Dashboard";
                case DeviceTemplate.LedBar: return "LED bar";
                case DeviceTemplate.LedFlag: return "LED flag";
                case DeviceTemplate.Custom: return "Custom device";
                default: return "Wheel";
            }
        }

        private void SetCount(ref int field, int value, LedGroup group, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            value = Math.Max(0, Math.Min(512, value));
            if (field == value) return;
            field = value;
            OnPropertyChanged(propertyName);
            if (value > 0) group.Count = value;
            SyncMembership(group, value > 0);
            LayoutChanged();
        }

        private void SyncMembership(LedGroup group, bool wanted)
        {
            if (wanted && !Groups.Contains(group)) Groups.Add(group);
            else if (!wanted && Groups.Contains(group)) Groups.Remove(group);
        }

        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e) => LayoutChanged();

        private void LayoutChanged()
        {
            draftDirty = true;
            OnPropertyChanged(nameof(TotalLeds));
            OnPropertyChanged(nameof(HasRow));
            OnPropertyChanged(nameof(HasColumns));
            OnPropertyChanged(nameof(HasMatrix));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(ElementCount));
            OnPropertyChanged(nameof(FrontPlateHint));
            PreviewChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>One entry of the wizard's device list: a generic device or a SimHub device.</summary>
    public sealed class DeviceChoice
    {
        public const string GenericGroup = "Generic devices";

        private DeviceChoice(string title, string subtitle, string group, DeviceTemplate? template, SimHubDeviceInfo preset)
        {
            Title = title;
            Subtitle = subtitle;
            Group = group;
            Template = template;
            Preset = preset;
        }

        public static readonly DeviceChoice[] Generic =
        {
            new DeviceChoice("Generic wheel", "RPM LEDs, side columns, buttons", GenericGroup, DeviceTemplate.Wheel, null),
            new DeviceChoice("Generic dashboard", "RPM LEDs on top, LEDs on the sides", GenericGroup, DeviceTemplate.Dashboard, null),
            new DeviceChoice("Generic LED flag", "a matrix of LEDs", GenericGroup, DeviceTemplate.LedFlag, null),
            new DeviceChoice("Generic LED bar", "a single row of LEDs", GenericGroup, DeviceTemplate.LedBar, null),
            new DeviceChoice("Custom", "start from scratch", GenericGroup, DeviceTemplate.Custom, null),
        };

        public string Title { get; }

        public string Subtitle { get; }

        /// <summary>"Generic devices" or the SimHub device's brand.</summary>
        public string Group { get; }

        /// <summary>The template of a generic device, null for a SimHub device.</summary>
        public DeviceTemplate? Template { get; }

        /// <summary>The SimHub device, null for a generic device.</summary>
        public SimHubDeviceInfo Preset { get; }

        public bool IsGeneric => Preset == null;

        public bool HasDisplay => Preset?.HasDisplay == true;

        /// <summary>Product picture relative to the SimHub folder, or null.</summary>
        public string ImageFile => Preset?.ProductImageFile;

        public static DeviceChoice ForSimHub(SimHubDeviceInfo device) => new DeviceChoice(device.Name, device.ChannelSummary, device.Brand, null, device);

        /// <summary>Every word of <paramref name="search"/> appears in the title or brand.</summary>
        public bool Matches(string search)
        {
            if (string.IsNullOrWhiteSpace(search)) return true;
            var text = (Title + " " + Group).ToLowerInvariant();
            return search.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).All(word => text.Contains(word));
        }

        public override string ToString() => Title;
    }

    /// <summary>One answer of the wizard's "what kind of device is it" step.</summary>
    public sealed class KindOption : ObservableObject
    {
        private readonly DeviceWizardViewModel owner;

        private KindOption(DeviceWizardViewModel owner, DeviceType type, string title, string description)
        {
            this.owner = owner;
            Type = type;
            Title = title;
            Description = description;
        }

        public DeviceType Type { get; }

        public string Title { get; }

        public string Description { get; }

        public bool IsSelected
        {
            get => owner.SelectedKind == Type;
            set
            {
                if (value) owner.SelectedKind = Type;
            }
        }

        public bool IsSuggested => owner.IsSimHubDevice && owner.SuggestedKind == Type;

        public static IReadOnlyList<KindOption> All(DeviceWizardViewModel owner) => new[]
        {
            new KindOption(owner, DeviceType.Wheel, "Wheel", "RPM LEDs and buttons"),
            new KindOption(owner, DeviceType.Dashboard, "Dashboard", "RPM LEDs on top, LEDs on the sides"),
            new KindOption(owner, DeviceType.LedFlag, "LED flag", "a matrix of LEDs"),
            new KindOption(owner, DeviceType.LedBar, "LED bar", "a row of LEDs"),
            new KindOption(owner, DeviceType.Custom, "Other", "button box, panel, ..."),
        };

        public void Refresh()
        {
            OnPropertyChanged(nameof(IsSelected));
            OnPropertyChanged(nameof(IsSuggested));
        }
    }

    /// <summary>One entry of the wizard's step indicator.</summary>
    public sealed class StepHeader
    {
        public StepHeader(int number, string title, bool isCurrent, bool isDone, bool hasNext)
        {
            Number = number;
            Title = title;
            IsCurrent = isCurrent;
            IsDone = isDone;
            HasNext = hasNext;
        }

        public int Number { get; }

        public string Title { get; }

        public bool IsCurrent { get; }

        /// <summary>The step lies before the current one.</summary>
        public bool IsDone { get; }

        /// <summary>Another step follows (draws the connector line).</summary>
        public bool HasNext { get; }
    }
}
