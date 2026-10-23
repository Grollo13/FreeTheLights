using System;
using System.Linq;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Xunit;

namespace FreeTheLights.Tests
{
    public class WizardTests
    {
        [Fact]
        public void Wheel_template_builds_row_and_columns_with_default_targets()
        {
            var wizard = new DeviceWizardViewModel();
            var built = wizard.Build();
            var device = built.Parts.Single();

            Assert.Equal("Wheel", built.Name);
            Assert.Equal(new[] { 15, 3, 3 }, device.Groups.Select(g => g.LedCount));
            Assert.Equal(new[] { 0, 15, 18 }, device.Groups.Select(g => g.StartIndex));
            Assert.Equal(new[] { LedGroupSide.None, LedGroupSide.Left, LedGroupSide.Right }, device.Groups.Select(g => g.Side));
            Assert.Equal(new[] { device.Groups[0].Id }, device.Rpm.TargetGroupIds);
            Assert.Equal(new[] { device.Groups[1].Id, device.Groups[2].Id }, device.Proximity.TargetGroupIds);
            Assert.Equal(FlagSettings.DefaultOrder.Length, device.Flags.Rules.Count);
        }

        [Theory]
        [InlineData(DeviceTemplate.Dashboard, new[] { 10, 4, 4 })]
        [InlineData(DeviceTemplate.LedBar, new[] { 16 })]
        [InlineData(DeviceTemplate.LedFlag, new[] { 64 })]
        [InlineData(DeviceTemplate.Custom, new int[0])]
        public void Templates_prefill_the_layout(DeviceTemplate template, int[] counts)
        {
            var wizard = new DeviceWizardViewModel { Template = template };
            Assert.Equal(counts, wizard.Build().Parts.Single().Groups.Select(g => g.LedCount));
        }

        [Theory]
        [InlineData(DeviceTemplate.Wheel, DeviceType.Wheel)]
        [InlineData(DeviceTemplate.Dashboard, DeviceType.Dashboard)]
        [InlineData(DeviceTemplate.LedBar, DeviceType.LedBar)]
        [InlineData(DeviceTemplate.LedFlag, DeviceType.LedFlag)]
        [InlineData(DeviceTemplate.Custom, DeviceType.Custom)]
        public void Templates_set_the_device_type(DeviceTemplate template, DeviceType expected)
        {
            var wizard = new DeviceWizardViewModel { Template = template };
            Assert.Equal(expected, wizard.Build().Type);
        }

        [Fact]
        public void Counts_are_applied_in_the_default_wiring_order()
        {
            // The wiring is checked on the device page later; the wizard chains the groups in a default order.
            var wizard = new DeviceWizardViewModel { LeftCount = 2, RightCount = 0 };

            var device = wizard.Build().Parts.Single();
            Assert.Equal(new[] { "RPM LEDs", "Left column" }, device.Groups.Select(g => g.Name));
            Assert.Equal(new[] { 0, 15 }, device.Groups.Select(g => g.StartIndex));
            Assert.All(device.Groups, g => Assert.False(g.Reversed));
            Assert.Equal(17, wizard.TotalLeds);
        }

        [Fact]
        public void Every_feature_is_on_and_name_is_kept_across_templates()
        {
            // Features are set up on the settings page, not in the wizard.
            var wizard = new DeviceWizardViewModel { Name = "My Fanatec" };
            wizard.Template = DeviceTemplate.Dashboard;

            var built = wizard.Build();
            var device = built.Parts.Single();
            Assert.Equal("My Fanatec", built.Name);
            Assert.All(device.Features, f => Assert.True(f.Enabled));
        }

        [Fact]
        public void The_device_keeps_its_ids_and_edits_until_the_choices_change()
        {
            var wizard = new DeviceWizardViewModel();
            var first = wizard.Build();
            var part = first.Parts.Single();

            // Later steps edit the built device (wiring, inputs); building again returns the same device.
            part.Groups[0].Reversed = true;
            part.SimHubProfileIds.Add(Guid.NewGuid());
            Assert.Same(first, wizard.Build());

            // A changed choice builds it again, with the same ids, so a SimHub profile set up for it stays connected.
            wizard.RowCount = 12;
            var second = wizard.Build();
            Assert.NotSame(first, second);
            Assert.Equal(first.Id, second.Id);
            Assert.Equal(part.Id, second.Parts.Single().Id);
            Assert.Equal(part.SimHubProfileIds, second.Parts.Single().SimHubProfileIds);
            Assert.False(second.Parts.Single().Groups[0].Reversed);
            Assert.Equal(12, second.Parts.Single().Groups[0].LedCount);

            // A new name doesn't build it again: the wiring and inputs edited so far stay.
            second.Parts.Single().Groups[0].Reversed = true;
            wizard.Name = "Renamed";
            Assert.Same(second, wizard.Build());
            Assert.Equal("Renamed", second.Name);
            Assert.True(second.Parts.Single().Groups[0].Reversed);
            wizard.Name = " ";
            Assert.Equal("New device", second.Name);
        }

        [Fact]
        public void Navigation_stops_on_an_empty_layout_and_ends_at_finish()
        {
            var wizard = new DeviceWizardViewModel();
            Assert.False(wizard.CanGoBack);

            wizard.Step = DeviceWizardViewModel.StepLayout;
            Assert.True(wizard.CanGoNext);
            wizard.Template = DeviceTemplate.LedBar;
            wizard.RowCount = 0;
            Assert.False(wizard.CanGoNext);

            // A custom device may consist of front plate elements only: it stops on the front plate step instead.
            wizard.Template = DeviceTemplate.Custom;
            Assert.True(wizard.CanGoNext);
            wizard.Step = DeviceWizardViewModel.StepFrontPlate;
            Assert.False(wizard.CanGoNext);

            wizard.RowCount = 5;
            wizard.Step = 99;
            Assert.Equal(DeviceWizardViewModel.StepFinish, wizard.Step);
            Assert.True(wizard.IsLastStep);
            Assert.Contains("5 LEDs", wizard.Summary);
        }

        // ---------- Steps that work on the real device (SimHub profile) ----------

        /// <summary>SimHub as the wizard sees it: whether it has the device, and which parts its profiles show.</summary>
        private sealed class FakeHost : IWizardHost
        {
            public bool HasDevice = true;
            public readonly System.Collections.Generic.HashSet<Guid> Connected = new System.Collections.Generic.HashSet<Guid>();
            public int Connects;
            public string ConnectError;
            public LedChannel? RefusedChannel;
            public DeviceConfig Shown;

            public bool HasSimHubDevice(string deviceTypeId) => HasDevice;

            public System.Collections.Generic.IReadOnlyList<PartConnection> Check(DeviceConfig device) =>
                device.Parts.Select(p => new PartConnection(p, !HasDevice ? ConnectionState.DeviceMissing : Connected.Contains(p.Id) ? ConnectionState.Connected : ConnectionState.NotConnected, "Profile")).ToList();

            public string Connect(DeviceConfig device, DevicePart part)
            {
                Connects++;
                if (ConnectError != null) return ConnectError;
                if (part.Channel == RefusedChannel) return "SimHub said no";
                Connected.Add(part.Id);
                return null;
            }

            public void ShowDevice(DeviceConfig device) => Shown = device;

            public int Undos;

            public void UndoConnections()
            {
                Undos++;
                Connected.Clear();
            }

            public void KeepConnections()
            {
            }

            public Guid WiringPart;

            public void ShowWiringPattern(Guid partId) => WiringPart = partId;

            public Guid ConfirmationDevice;

            public void ShowConfirmationPattern(Guid deviceId) => ConfirmationDevice = deviceId;

            public Core.LedColor[] GetFrame(Guid partId) => null;

            public Core.InputState Inputs { get; set; } = new Core.InputState();

            public void Post(Action action) => action();
        }

        private static DeviceWizardViewModel SimHubDashboardWizard(FakeHost host)
        {
            var wizard = new DeviceWizardViewModel(host: host);
            wizard.SetSimHubDevices(new[] { ShDeviceParser.Parse(PresetTests.Dashboard) });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            return wizard;
        }

        [Fact]
        public void Profile_step_only_for_simhub_devices_that_simhub_has()
        {
            Assert.DoesNotContain(new DeviceWizardViewModel(host: new FakeHost()).StepHeaders, h => h.Title == "Connect");
            Assert.DoesNotContain(SimHubDashboardWizard(new FakeHost { HasDevice = false }).StepHeaders, h => h.Title == "Connect");

            var wizard = SimHubDashboardWizard(new FakeHost());
            Assert.Equal(new[] { "Device", "Kind", "Connect", "LED layout", "Finish" }, wizard.StepHeaders.Select(h => h.Title));

            // Without SimHub (no host) the step is skipped too.
            var offline = new DeviceWizardViewModel();
            offline.SetSimHubDevices(new[] { ShDeviceParser.Parse(PresetTests.Dashboard) });
            offline.SelectedChoice = offline.Choices.Single(c => c.Preset != null);
            Assert.False(offline.HasProfileStep);
        }

        [Fact]
        public void Profile_step_connects_the_device_that_is_added()
        {
            var host = new FakeHost();
            var wizard = SimHubDashboardWizard(host);
            Assert.Null(wizard.DeviceToRender);

            wizard.NextCommand.Execute(null);
            wizard.NextCommand.Execute(null);
            Assert.Equal(DeviceWizardViewModel.StepProfile, wizard.Step);
            var row = Assert.Single(wizard.Profile.Connections);
            Assert.False(row.IsConnected);
            Assert.True(wizard.Profile.CanConnect);
            Assert.Same(wizard.Build(), wizard.DeviceToRender);

            Assert.Equal(Guid.Empty, host.ConfirmationDevice);
            wizard.Profile.ConnectCommand.Execute(null);
            Assert.True(wizard.Profile.IsConnected);
            Assert.True(wizard.Profile.IsFullyConnected);
            Assert.False(wizard.Profile.CanConnect);
            Assert.Null(wizard.Profile.Message);

            // The device shows at once that it worked, until the step is left.
            Assert.Equal(wizard.Build().Id, host.ConfirmationDevice);
            Assert.Equal(new[] { wizard.Build().Parts.Single().Id }, wizard.Profile.ConnectedPartIds);

            // Changing the layout afterwards keeps the ids the profile shows.
            wizard.Step = DeviceWizardViewModel.StepLayout;
            Assert.Equal(Guid.Empty, host.ConfirmationDevice);
            wizard.LeftCount = 4;
            var device = wizard.Build();
            Assert.Contains(device.Parts.Single().Id, host.Connected);
        }

        [Fact]
        public void Picking_other_hardware_undoes_the_connection_and_gets_new_ids()
        {
            var host = new FakeHost();
            var wizard = SimHubDashboardWizard(host);
            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);
            Assert.True(wizard.Profile.IsConnected);
            var connected = wizard.Build();
            var undos = host.Undos;

            // Finish must not show the previous device as connected.
            var changed = new System.Collections.Generic.List<string>();
            wizard.Profile.PropertyChanged += (s, e) => changed.Add(e.PropertyName);
            wizard.SelectedChoice = wizard.Choices.First(c => c.Preset == null);
            Assert.False(wizard.Profile.IsConnected);
            Assert.False(wizard.Profile.IsFullyConnected);
            Assert.Contains(nameof(WizardProfileStep.IsConnected), changed);
            Assert.Contains(nameof(WizardProfileStep.IsFullyConnected), changed);

            // SimHub's profile set up for the previous hardware is undone, and the new device has other ids,
            // so nothing set up for the previous hardware shows it.
            Assert.Equal(undos + 1, host.Undos);
            var other = wizard.Build();
            Assert.NotEqual(connected.Id, other.Id);
            Assert.DoesNotContain(other.Parts, p => connected.Parts.Any(c => c.Id == p.Id));

            // Changing the layout of the same hardware keeps the ids (and the connection).
            wizard.RowCount = 9;
            Assert.Equal(other.Id, wizard.Build().Id);
            Assert.Equal(undos + 1, host.Undos);
        }

        [Fact]
        public void An_led_matrix_still_needs_the_manual_setup()
        {
            // Connect can't set up a matrix channel: such a device is never fully connected, so Finish keeps the manual steps.
            var host = new FakeHost();
            var wizard = new DeviceWizardViewModel(host: host);
            var flag = new SimHubDeviceInfo { DeviceTypeId = "flag", Name = "Some LED flag" };
            flag.Channels[LedChannel.Matrix] = 64;
            wizard.SetSimHubDevices(new[] { flag });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            wizard.SelectedKind = DeviceType.LedFlag;
            wizard.Step = DeviceWizardViewModel.StepProfile;

            Assert.Equal(LedChannel.Matrix, wizard.Profile.Connections.Single().Connection.Part.Channel);
            Assert.False(wizard.Profile.IsConnected);
            Assert.False(wizard.Profile.IsFullyConnected);
        }

        [Fact]
        public void Profile_step_reports_errors_and_can_be_skipped()
        {
            var host = new FakeHost { ConnectError = "SimHub said no" };
            var wizard = SimHubDashboardWizard(host);
            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);

            Assert.Contains("SimHub said no", wizard.Profile.Message);
            Assert.False(wizard.Profile.IsConnected);
            Assert.True(wizard.CanGoNext);
        }

        [Fact]
        public void Wiring_step_appears_once_connected_and_shows_the_pattern_while_open()
        {
            var host = new FakeHost();
            var wizard = SimHubDashboardWizard(host);
            Assert.False(wizard.HasWiringStep);
            wizard.Step = DeviceWizardViewModel.StepProfile;
            Assert.False(wizard.HasWiringStep);

            wizard.Profile.ConnectCommand.Execute(null);
            Assert.True(wizard.HasWiringStep);
            // Connected, the wiring step sets the groups: the LED layout step is no longer needed.
            Assert.Equal(new[] { "Device", "Kind", "Connect", "LED wiring", "Finish" }, wizard.StepHeaders.Select(h => h.Title));

            wizard.NextCommand.Execute(null);
            Assert.Equal(DeviceWizardViewModel.StepWiring, wizard.Step);
            var part = wizard.Build().Parts.Single();
            Assert.Same(part, wizard.WiringPart);
            Assert.Equal(part.Id, host.WiringPart);
            Assert.Equal(part.GroupsInWiringOrder(), wizard.Wiring.Groups);

            // Dragging changes the wiring of the device that will be added.
            var first = wizard.Wiring.Groups[0];
            wizard.Wiring.MoveCommand.Execute(new MoveRequest(0, 2));
            Assert.Same(first, wizard.Wiring.Groups[2]);
            Assert.Same(first, wizard.Build().Parts.Single().GroupsInWiringOrder()[2]);

            wizard.NextCommand.Execute(null);
            Assert.Equal(Guid.Empty, host.WiringPart);
        }

        [Fact]
        public void Wiring_step_adds_removes_and_resizes_groups()
        {
            // A device whose SimHub data doesn't say how its 21 LEDs are split (like the generic Asetek device as a wheel).
            var host = new FakeHost();
            var wizard = SimHubDashboardWizard(host);
            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);
            wizard.Step = DeviceWizardViewModel.StepWiring;
            var part = wizard.WiringPart;
            Assert.Equal(21, wizard.Wiring.ChannelLeds);

            // Remove everything: the step stays, as the LED channel can still get groups.
            foreach (var group in wizard.Wiring.Groups.ToList()) wizard.Wiring.RemoveCommand.Execute(group);
            Assert.Empty(part.GroupsInWiringOrder());
            Assert.True(wizard.HasWiringStep);

            // A new row takes the free LEDs; shortened, it makes room for a column on each side.
            wizard.Wiring.AddCommand.Execute("Row");
            var row = wizard.Wiring.Groups.Single();
            Assert.Equal(LedGroupKind.HorizontalRow, row.Kind);
            Assert.Equal(21, row.LedCount);
            Assert.Equal("21 of 21 LEDs are in a group", wizard.Wiring.LedSummary);
            Assert.Contains(row.Id, part.Rpm.TargetGroupIds);

            row.Count = 15;
            wizard.Wiring.AddCommand.Execute("Column");
            wizard.Wiring.AddCommand.Execute("Column");
            Assert.Equal(new[] { 0, 15, 18 }, wizard.Wiring.Groups.Select(g => g.StartIndex));
            Assert.Equal(new[] { LedGroupSide.None, LedGroupSide.Left, LedGroupSide.Right }, wizard.Wiring.Groups.Select(g => g.Side));
            Assert.All(wizard.Wiring.Groups.Skip(1), g => Assert.Equal(3, g.LedCount));
            Assert.False(wizard.Wiring.HasTooManyLeds);

            // Resizing re-chains the strip; too many LEDs are pointed out.
            row.Count = 17;
            Assert.Equal(new[] { 0, 17, 20 }, wizard.Wiring.Groups.Select(g => g.StartIndex));
            Assert.True(wizard.Wiring.HasTooManyLeds);
            Assert.Contains("only 21", wizard.Wiring.LedSummary);

            // Removing one closes the gap; the device that is added has the edited groups.
            wizard.Wiring.RemoveCommand.Execute(wizard.Wiring.Groups[1]);
            Assert.Equal(new[] { 0, 17 }, wizard.Wiring.Groups.Select(g => g.StartIndex));
            Assert.Equal(new[] { 17, 3 }, wizard.Build().Parts.Single().GroupsInWiringOrder().Select(g => g.LedCount));

            // Removing the first group doesn't leave a gap at the start of the strip.
            wizard.Wiring.RemoveCommand.Execute(wizard.Wiring.Groups[0]);
            Assert.Equal(0, wizard.Wiring.Groups.Single().StartIndex);
        }

        [Fact]
        public void Positions_step_places_the_groups_edited_in_the_wiring_step()
        {
            // A connected dashboard with a picture: LED positions, then wiring. Going back from the wiring step must show
            // its edits, and moving LEDs must not undo them.
            var host = new FakeHost();
            var photo = new DashPhotoResult { PictureFile = "p.png", PictureWidth = 400, PictureHeight = 250, Source = PhotoLedSource.None };
            var wizard = new DeviceWizardViewModel(null, _ => photo, host);
            var info = ShDeviceParser.Parse(PresetTests.Dashboard);
            info.ProductImageFile = "DevicesLogos\\dash.png";
            wizard.SetSimHubDevices(new[] { info });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);
            Assert.Equal(new[] { "Device", "Kind", "Connect", "LED positions", "LED wiring", "Finish" }, wizard.StepHeaders.Select(h => h.Title));

            wizard.Step = DeviceWizardViewModel.StepWiring;
            var device = wizard.Build();
            wizard.Wiring.Groups[0].Count = 2;
            wizard.Wiring.AddCommand.Execute("Row");
            var groups = wizard.Wiring.Groups.ToList();

            wizard.Step = DeviceWizardViewModel.StepPhoto;
            Assert.Equal(groups, wizard.PhotoGroups);
            Assert.All(wizard.PhotoGroups, g => Assert.Equal(g.Count, g.PhotoLeds.Count));

            // Moving LEDs, resetting them or switching to the generic drawing changes the same device.
            wizard.PhotoPositionsEdited();
            wizard.ResetPhotoCommand.Execute(null);
            wizard.UseGenericDrawing = true;
            Assert.Same(device, wizard.Build());
            Assert.Equal(PhotoLedSource.Generic, device.Preset.LedPositions);
            Assert.All(wizard.PhotoGroups, g => Assert.Empty(g.PhotoLeds));
            // Groups changed in the wiring step meanwhile get positions for their LED count when the picture is chosen again.
            wizard.Step = DeviceWizardViewModel.StepWiring;
            wizard.Wiring.Groups[0].Count = 4;
            wizard.Wiring.AddCommand.Execute("Column");
            wizard.Step = DeviceWizardViewModel.StepPhoto;
            wizard.UseGenericDrawing = false;
            Assert.Equal(groups.Count + 1, wizard.PhotoGroups.Count);
            Assert.All(wizard.PhotoGroups, g => Assert.Equal(g.Count, g.PhotoLeds.Count));
            groups = wizard.Wiring.Groups.ToList();

            Assert.Same(device, wizard.Build());
            Assert.Equal(groups, wizard.Build().Parts.Single().GroupsInWiringOrder());
            Assert.Equal(4, groups[0].Count);
        }

        [Fact]
        public void Wiring_step_needs_a_connected_part_with_groups()
        {
            var host = new FakeHost();
            var wizard = SimHubDashboardWizard(host);
            wizard.Step = DeviceWizardViewModel.StepProfile;
            Assert.False(wizard.HasWiringStep);
            wizard.Step = DeviceWizardViewModel.StepFinish;
            Assert.Equal(Guid.Empty, host.WiringPart);
        }

        [Fact]
        public void Buttons_step_for_devices_with_buttons_while_inputs_are_available()
        {
            // The generic wheel gets buttons and dials on the front plate step.
            var wizard = new DeviceWizardViewModel(host: new FakeHost());
            Assert.DoesNotContain(wizard.StepHeaders, h => h.Title == "Assign buttons");
            wizard.SelectedSidePreset = FrontPlatePresets.FindSides("1x2");
            Assert.Equal(new[] { "Device", "LED layout", "Front plate", "Assign buttons", "Finish" }, wizard.StepHeaders.Select(h => h.Title));

            Assert.DoesNotContain(new DeviceWizardViewModel(host: new FakeHost()) { Template = DeviceTemplate.LedBar }.StepHeaders, h => h.Title == "Assign buttons");
            Assert.DoesNotContain(new DeviceWizardViewModel(host: new FakeHost { Inputs = null }).StepHeaders, h => h.Title == "Assign buttons");
            Assert.DoesNotContain(new DeviceWizardViewModel().StepHeaders, h => h.Title == "Assign buttons");
        }

        [Fact]
        public void Buttons_step_assigns_pressed_inputs_to_the_device_that_is_added()
        {
            var host = new FakeHost();
            var wizard = new DeviceWizardViewModel(host: host) { SelectedSidePreset = FrontPlatePresets.FindSides("1x2") };
            wizard.Step = DeviceWizardViewModel.StepFrontPlate;
            wizard.NextCommand.Execute(null);
            Assert.Equal(DeviceWizardViewModel.StepButtons, wizard.Step);

            var first = wizard.Buttons.Current;
            host.Inputs.Press("JoystickPlugin.Wheel_B01");
            Assert.Equal("JoystickPlugin.Wheel_B01", first.ButtonInput);
            Assert.Contains(wizard.Build().Parts.Single().Elements, e => e == first);
            Assert.True(wizard.SkipButtonCommand.CanExecute(null));

            // Leaving the step stops listening.
            wizard.NextCommand.Execute(null);
            var current = wizard.Buttons.Current;
            host.Inputs.Press("JoystickPlugin.Wheel_B02");
            Assert.Equal(string.Empty, current.GetInput(wizard.Buttons.CurrentSlot));

            // Generic devices have no SimHub profile: nothing is rendered on the device.
            Assert.Null(wizard.DeviceToRender);
            Assert.Contains($"Inputs: 1 of {wizard.Buttons.Count} assigned", wizard.Summary);
        }

        [Fact]
        public void Buttons_only_light_up_when_their_own_channel_is_connected()
        {
            // The shift lights connect, the buttons' channel doesn't: the wizard must not promise a lit button.
            var host = new FakeHost { RefusedChannel = LedChannel.Buttons };
            var wizard = new DeviceWizardViewModel(_ => PresetTests.GT4Layout(), host: host);
            var info = new SimHubDeviceInfo { DeviceTypeId = "wheel", Name = "Some wheel", ButtonLayoutName = "DefaultColorsEditorGT4" };
            info.Channels[LedChannel.Leds] = 15;
            info.Channels[LedChannel.Buttons] = 12;
            wizard.SetSimHubDevices(new[] { info });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            wizard.SelectedKind = DeviceType.Wheel;
            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);
            Assert.True(wizard.Profile.IsConnected);

            wizard.Step = DeviceWizardViewModel.StepButtons;
            Assert.Equal(LedChannel.Buttons, wizard.Buttons.CurrentPart.Channel);
            Assert.False(wizard.ButtonsShownOnDevice);

            host.RefusedChannel = null;
            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);
            wizard.Step = DeviceWizardViewModel.StepButtons;
            Assert.True(wizard.ButtonsShownOnDevice);
        }

        [Fact]
        public void A_connected_wheel_lights_the_button_to_press_on_the_device()
        {
            var host = new FakeHost();
            var wizard = new DeviceWizardViewModel(_ => PresetTests.GT4Layout(), host: host);
            var info = ShDeviceParser.Parse(PresetTests.Wheel);
            info.ButtonLayoutName = "DefaultColorsEditorGT4";
            wizard.SetSimHubDevices(new[] { info });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            Assert.Equal(DeviceType.Wheel, wizard.SelectedKind);
            Assert.Equal(new[] { "Device", "Kind", "Connect", "Assign buttons", "Finish" }, wizard.StepHeaders.Select(h => h.Title));

            wizard.Step = DeviceWizardViewModel.StepProfile;
            wizard.Profile.ConnectCommand.Execute(null);
            wizard.NextCommand.Execute(null);
            Assert.Equal(DeviceWizardViewModel.StepButtons, wizard.Step);
            Assert.True(wizard.ButtonsShownOnDevice);

            var shown = wizard.DeviceToRender;
            Assert.NotSame(wizard.Build(), shown);
            Assert.Equal(wizard.Build().Id, shown.Id);
            var current = wizard.Buttons.Current;
            Assert.Equal(ButtonAssignment.CurrentColor.ToHex(), shown.Parts.SelectMany(p => p.Groups).Single(g => g.Id == current.Id).StaticColor);
            Assert.Same(shown, wizard.DeviceToRender);

            host.Inputs.Press("JoystickPlugin.GT4_B01");
            Assert.NotSame(shown, wizard.DeviceToRender);

            wizard.NextCommand.Execute(null);
            Assert.Same(wizard.Build(), wizard.DeviceToRender);
        }

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0, 1, 0)]
        [InlineData(0, 3, 2)]
        [InlineData(3, 0, 0)]
        [InlineData(2, 5, 4)]
        public void Drop_position_accounts_for_the_removed_item(int oldIndex, int insertionIndex, int expected)
        {
            Assert.Equal(expected, DragReorder.TargetIndex(oldIndex, insertionIndex));
        }

        [Fact]
        public void Priority_drag_and_drop_moves_the_layer()
        {
            var device = DevicePart.CreateDefault("Wheel");
            using (var viewModel = new DeviceViewModel(device))
            {
                viewModel.MoveLayerCommand.Execute(new MoveRequest(device.LayerPriority.IndexOf(LayerKind.Rpm), 0));
                Assert.Equal(LayerKind.Rpm, device.LayerPriority[0]);
                Assert.Equal(LayerKind.Rpm, viewModel.Layers[0].Kind);
                Assert.Equal(LayerKind.Press, device.LayerPriority[1]);
            }
        }
    }
}
