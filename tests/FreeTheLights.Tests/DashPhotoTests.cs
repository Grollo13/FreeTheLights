using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Settings;
using FreeTheLights.UI;
using Newtonsoft.Json;
using Xunit;

namespace FreeTheLights.Tests
{
    /// <summary>Dashboard LEDs on SimHub's product pictures: detection, the built-in list, the wizard step, the settings.</summary>
    public class DashPhotoTests
    {
        private const string AscherId = "3799FBB8-51F3-4329-A759-13C3976D6612";

        private const int Width = 400;
        private const int Height = 260;

        /// <summary>
        /// A synthetic dashboard picture: a dark bezel with a screen showing "text", a top row of
        /// <paramref name="top"/> LEDs, <paramref name="left"/>/<paramref name="right"/> LEDs down the sides and four
        /// screws in the corners.
        /// </summary>
        private static int[] Picture(int top, int left, int right)
        {
            var pixels = Enumerable.Repeat(unchecked((int)0xFF202022), Width * Height).ToArray();
            Fill(pixels, 60, 50, 340, 230, unchecked((int)0xFF101830));
            for (var line = 0; line < 4; line++)
            {
                for (var c = 0; c < 9; c++) Fill(pixels, 90 + c * 12, 90 + line * 30, 97 + c * 12, 101 + line * 30, unchecked((int)0xFFE0E0E0));
            }

            foreach (var (x, y) in new[] { (12, 12), (388, 12), (12, 248), (388, 248) }) Dot(pixels, x, y, 6, unchecked((int)0xFF909090));
            for (var i = 0; i < top; i++) Dot(pixels, 110 + i * 180 / Math.Max(1, top - 1), 25, 5, unchecked((int)0xFF30FF40));
            for (var i = 0; i < left; i++) Dot(pixels, 25, 170 - i * 30, 5, unchecked((int)0xFFFF4040));
            for (var i = 0; i < right; i++) Dot(pixels, 375, 170 - i * 30, 5, unchecked((int)0xFF4060FF));
            return pixels;
        }

        private static void Fill(int[] pixels, int x0, int y0, int x1, int y1, int color)
        {
            for (var y = y0; y < y1; y++)
            {
                for (var x = x0; x < x1; x++) pixels[y * Width + x] = color;
            }
        }

        private static void Dot(int[] pixels, int cx, int cy, int r, int color)
        {
            for (var y = cy - r; y <= cy + r; y++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r) pixels[y * Width + x] = color;
                }
            }
        }

        private static void Near(double expected, double actual) => Assert.InRange(actual, expected - 1, expected + 1);

        // ---------- detection ----------

        [Fact]
        public void Detector_finds_the_top_row_and_side_columns_in_logical_order()
        {
            var layout = DashLedDetector.Detect(Picture(12, 3, 3), Width, Height, 18);

            Assert.NotNull(layout);
            Assert.Equal(new[] { 3, 12, 3 }, new[] { layout.Left.Count, layout.Top.Count, layout.Right.Count });
            Near(110, layout.Top[0].X);
            Near(290, layout.Top[11].X);
            Assert.All(layout.Top, l => Near(25, l.Y));

            // Columns run bottom to top, like the logical LED index of a column.
            Near(170, layout.Left[0].Y);
            Near(110, layout.Left[2].Y);
            Near(375, layout.Right[0].X);
            Assert.Equal(11, layout.Top[0].Width);
        }

        [Fact]
        public void Detector_only_answers_with_exactly_the_devices_led_count()
        {
            Assert.Null(DashLedDetector.Detect(Picture(12, 3, 3), Width, Height, 20));
        }

        [Fact]
        public void Detector_rejects_layouts_that_are_not_mirror_symmetric()
        {
            // A column on one side only is a line of buttons on a wheel, not a dashboard.
            Assert.Null(DashLedDetector.Detect(Picture(12, 3, 0), Width, Height, 15));
            Assert.NotNull(DashLedDetector.Detect(Picture(12, 0, 0), Width, Height, 12));
        }

        [Fact]
        public void Detector_handles_empty_input()
        {
            Assert.Null(DashLedDetector.Detect(null, Width, Height, 10));
            Assert.Null(DashLedDetector.Detect(new int[10], Width, Height, 10));
            Assert.Null(DashLedDetector.Detect(Picture(12, 3, 3), Width, Height, 0));
        }

        // ---------- built-in list ----------

        [Fact]
        public void Built_in_list_has_reviewed_symmetric_layouts_with_picture_hashes()
        {
            var all = DashPhotoLayouts.BuiltIn;
            Assert.True(all.Count >= 20, $"only {all.Count} dashboards in the list");
            foreach (var entry in all.Values)
            {
                Assert.Matches("^[0-9A-F]{64}$", entry.Sha256);
                var layout = entry.Layout;
                Assert.True(layout.Top.Count > 0, entry.Name);
                Assert.Equal(layout.Left.Count > 0, layout.Right.Count > 0);
                foreach (var led in layout.Left.Concat(layout.Top).Concat(layout.Right))
                {
                    Assert.InRange(led.X, 0, layout.PictureWidth);
                    Assert.InRange(led.Y, 0, layout.PictureHeight);
                    Assert.True(led.Width > 0 && led.Height > 0, entry.Name);
                }
            }

            var ascher = DashPhotoLayouts.Find(AscherId.ToLowerInvariant());
            Assert.Equal(new[] { 3, 15, 3 }, new[] { ascher.Layout.Left.Count, ascher.Layout.Top.Count, ascher.Layout.Right.Count });
        }

        [Fact]
        public void Resolve_takes_the_list_when_the_picture_is_unchanged_without_decoding_it()
        {
            var known = DashPhotoLayouts.Find(AscherId);
            var result = DashPhotoLayouts.Resolve(AscherId, "DevicesLogos\\x.png", known.Sha256.ToLowerInvariant(), 360, 225, () => throw new InvalidOperationException("decoded"), 21, null);

            Assert.Equal(PhotoLedSource.BuiltIn, result.Source);
            Assert.Same(known.Layout, result.Layout);
        }

        [Fact]
        public void Resolve_detects_on_a_changed_picture_and_reports_failures()
        {
            var decoded = 0;
            Func<int[]> pixels = () =>
            {
                decoded++;
                return Picture(12, 3, 3);
            };

            var changed = DashPhotoLayouts.Resolve(AscherId, "p.png", "00", Width, Height, pixels, 18, null);
            Assert.Equal(PhotoLedSource.Found, changed.Source);
            Assert.Equal(18, changed.Layout.Total);

            var unknown = DashPhotoLayouts.Resolve("unknown", "p.png", "00", Width, Height, pixels, 25, null);
            Assert.Equal(PhotoLedSource.None, unknown.Source);
            Assert.Null(unknown.Layout);
            Assert.Equal("p.png", unknown.PictureFile);
            Assert.Equal(2, decoded);
        }

        // ---------- positions ----------

        [Fact]
        public void Resample_keeps_the_ends_and_spreads_along_the_path()
        {
            var leds = new List<PhotoLed> { new PhotoLed(0, 10, 4, 4), new PhotoLed(10, 0, 4, 4), new PhotoLed(20, 10, 6, 6) };

            Assert.Equal(new[] { 0.0, 10, 20 }, DashPhotoLayouts.Resample(leds, 3).Select(l => l.X));
            var five = DashPhotoLayouts.Resample(leds, 5);
            Assert.Equal(new[] { 0.0, 5, 10, 15, 20 }, five.Select(l => l.X));
            Assert.Equal(new[] { 10.0, 5, 0, 5, 10 }, five.Select(l => l.Y));
            Assert.Equal(6, five[4].Width);
            Assert.Single(DashPhotoLayouts.Resample(leds, 1));
            Assert.Empty(DashPhotoLayouts.Resample(leds, 0));
        }

        [Fact]
        public void Dragging_an_end_stretches_the_group_and_others_move_alone()
        {
            var leds = DashPhotoLayouts.Line(new PhotoLed(0, 0, 4, 4), new PhotoLed(40, 0, 4, 4), 5);

            var stretched = DashPhotoLayouts.Move(leds, 4, 40, 20);
            Assert.Equal(new[] { 0.0, 20, 40, 60, 80 }, stretched.Select(l => l.X));
            Assert.Equal(new[] { 0.0, 5, 10, 15, 20 }, stretched.Select(l => l.Y));

            var first = DashPhotoLayouts.Move(leds, 0, -10, 0);
            Assert.Equal(new[] { -10.0, 0 + 10 - 7.5, 20 - 5, 30 - 2.5, 40 }, first.Select(l => l.X));

            var middle = DashPhotoLayouts.Move(leds, 2, 0, 7);
            Assert.Equal(new[] { 0.0, 0, 7, 0, 0 }, middle.Select(l => l.Y));
            Assert.Equal(0, leds[4].Y);
        }

        [Fact]
        public void Groups_without_positions_get_default_places_and_changed_counts_are_resampled()
        {
            var row = new LedGroup { Kind = LedGroupKind.HorizontalRow, Count = 10 };
            var left = new LedGroup { Kind = LedGroupKind.VerticalColumn, Count = 3, Side = LedGroupSide.Left };
            var right = new LedGroup { Kind = LedGroupKind.VerticalColumn, Count = 3, Side = LedGroupSide.Right };
            DashPhotoLayouts.EnsurePositions(new[] { left, row, right }, 500, 300);

            Assert.Equal(10, row.PhotoLeds.Count);
            Assert.True(row.PhotoLeds.All(l => l.Y < 50));
            Assert.True(left.PhotoLeds.All(l => l.X < 50) && right.PhotoLeds.All(l => l.X > 450));
            Assert.True(left.PhotoLeds[0].Y > left.PhotoLeds[2].Y, "columns start at the bottom");

            var ends = (row.PhotoLeds.First().X, row.PhotoLeds.Last().X);
            row.Count = 4;
            DashPhotoLayouts.EnsurePositions(new[] { row }, 500, 300);
            Assert.Equal(4, row.PhotoLeds.Count);
            Assert.Equal(ends, (row.PhotoLeds.First().X, row.PhotoLeds.Last().X));
        }

        // ---------- presets and wizard ----------

        private static SimHubDeviceInfo Dash()
        {
            var info = ShDeviceParser.Parse(@"{ ""DeviceTypeID"": """ + AscherId + @""", ""DeviceTypeName"": ""Ascher Racing 4/5 Dashboard"",
                ""Settings"": { ""LCD"": { ""display"": {} }, ""LEDS"": { ""leds"": { ""ForcedLedCount"": 21, ""Profiles"": [] } } } }");
            info.ProductImageFile = "DevicesLogos\\" + AscherId + ".png";
            return info;
        }

        private static DashPhotoResult Found(PhotoLedSource source = PhotoLedSource.BuiltIn) => new DashPhotoResult
        {
            PictureFile = "DevicesLogos\\" + AscherId + ".png",
            PictureWidth = 360,
            PictureHeight = 225,
            Layout = DashPhotoLayouts.Find(AscherId).Layout,
            Source = source,
        };

        [Fact]
        public void Preset_dashboards_take_split_and_positions_from_the_picture()
        {
            var device = PresetBuilder.Build(Dash(), DeviceType.Dashboard, photo: Found());

            Assert.Equal("DevicesLogos\\" + AscherId + ".png", device.Preset.PhotoFile);
            Assert.Equal(360, device.Preset.CanvasWidth);
            Assert.Equal(PhotoLedSource.BuiltIn, device.Preset.LedPositions);
            Assert.True(device.Preset.ShowsLedsOnPhoto);
            var groups = device.Parts.Single().Groups;
            Assert.Equal(new[] { 3, 15, 3 }, groups.Select(g => g.Count));
            Assert.All(groups, g => Assert.Equal(g.Count, g.PhotoLeds.Count));
            Assert.True(groups[0].PhotoLeds[0].X < 50 && groups[2].PhotoLeds[0].X > 300);
        }

        [Fact]
        public void Refresh_adds_picture_positions_to_a_dashboard_without_them()
        {
            var device = PresetBuilder.Build(Dash(), DeviceType.Dashboard, split: new LedSplit(3, 15, 3, true));
            Assert.Null(device.Preset.PhotoFile);
            device.Parts[0].Groups[1].Name = "My RPM LEDs";

            PresetBuilder.Refresh(device, PresetBuilder.Build(Dash(), DeviceType.Dashboard, photo: Found(PhotoLedSource.Found)));

            Assert.Equal(PhotoLedSource.Found, device.Preset.LedPositions);
            Assert.Equal("My RPM LEDs", device.Parts[0].Groups[1].Name);
            Assert.All(device.Parts[0].Groups, g => Assert.Equal(g.Count, g.PhotoLeds.Count));

            // Positions the user placed are kept by later refreshes.
            device.Preset.LedPositions = PhotoLedSource.Manual;
            device.Parts[0].Groups[0].PhotoLeds = DashPhotoLayouts.Move(device.Parts[0].Groups[0].PhotoLeds, 1, 5, 5);
            var moved = device.Parts[0].Groups[0].PhotoLeds[1].X;
            PresetBuilder.Refresh(device, PresetBuilder.Build(Dash(), DeviceType.Dashboard, photo: Found()));
            Assert.Equal(PhotoLedSource.Manual, device.Preset.LedPositions);
            Assert.Equal(moved, device.Parts[0].Groups[0].PhotoLeds[1].X);
        }

        private static DeviceWizardViewModel DashWizard(DashPhotoResult photo, Action onLoad = null)
        {
            var wizard = new DeviceWizardViewModel(null, _ =>
            {
                onLoad?.Invoke();
                return photo;
            });
            wizard.SetSimHubDevices(new[] { Dash() });
            wizard.SelectedChoice = wizard.Choices.Single(c => c.Preset != null);
            return wizard;
        }

        [Fact]
        public void Wizard_reads_the_picture_after_the_kind_step_and_shows_the_positions_step()
        {
            var loads = 0;
            var wizard = DashWizard(Found(), () => loads++);
            Assert.Equal(DeviceType.Dashboard, wizard.SelectedKind);
            Assert.Equal(0, loads);

            wizard.Step = DeviceWizardViewModel.StepLayout;
            Assert.Equal(1, loads);
            Assert.True(wizard.HasPhotoStep);
            Assert.Contains(wizard.StepHeaders, h => h.Title == "LED positions");
            Assert.Equal((3, 15, 3), (wizard.LeftCount, wizard.RowCount, wizard.RightCount));

            wizard.Step = DeviceWizardViewModel.StepPhoto;
            wizard.Step = DeviceWizardViewModel.StepFinish;
            Assert.Equal(1, loads);

            var device = wizard.Build();
            Assert.Equal(PhotoLedSource.BuiltIn, device.Preset.LedPositions);
            Assert.All(device.Parts.Single().Groups, g => Assert.Equal(g.Count, g.PhotoLeds.Count));

            wizard.PhotoPositionsEdited();
            Assert.Equal(PhotoLedSource.Manual, wizard.Build().Preset.LedPositions);

            wizard.UseGenericDrawing = true;
            device = wizard.Build();
            Assert.Equal(PhotoLedSource.Generic, device.Preset.LedPositions);
            Assert.False(device.Preset.ShowsLedsOnPhoto);
            Assert.All(device.Parts.Single().Groups, g => Assert.Empty(g.PhotoLeds));
        }

        [Fact]
        public void Wizard_lets_the_user_place_leds_when_none_were_found()
        {
            var wizard = DashWizard(new DashPhotoResult { PictureFile = "p.png", PictureWidth = 400, PictureHeight = 250, Source = PhotoLedSource.None });
            wizard.Step = DeviceWizardViewModel.StepLayout;
            Assert.True(wizard.PhotoNeedsPlacing);
            wizard.RowCount = 13;

            wizard.Step = DeviceWizardViewModel.StepPhoto;
            Assert.All(wizard.PhotoGroups, g => Assert.Equal(g.Count, g.PhotoLeds.Count));

            var device = wizard.Build();
            Assert.Equal(PhotoLedSource.Manual, device.Preset.LedPositions);
            Assert.Equal("p.png", device.Preset.PhotoFile);
        }

        [Fact]
        public void Wizard_has_no_positions_step_without_a_picture_or_for_other_kinds()
        {
            var wizard = DashWizard(null);
            wizard.Step = DeviceWizardViewModel.StepLayout;
            Assert.False(wizard.HasPhotoStep);
            Assert.DoesNotContain(wizard.StepHeaders, h => h.Title == "LED positions");
            Assert.Null(wizard.Build().Preset.PhotoFile);

            wizard = DashWizard(Found());
            wizard.SelectedKind = DeviceType.LedBar;
            Assert.False(wizard.HasPhotoStep);
            Assert.False(new DeviceWizardViewModel().HasPhotoStep);
        }

        // ---------- settings ----------

        [Fact]
        public void Picture_positions_are_saved_and_copied()
        {
            var device = PresetBuilder.Build(Dash(), DeviceType.Dashboard, photo: Found());
            var loaded = JsonConvert.DeserializeObject<DeviceConfig>(JsonConvert.SerializeObject(device));
            loaded.EnsureDefaults();

            Assert.Equal(PhotoLedSource.BuiltIn, loaded.Preset.LedPositions);
            var row = loaded.Parts[0].Groups[1];
            Assert.Equal(15, row.PhotoLeds.Count);
            Assert.Equal(device.Parts[0].Groups[1].PhotoLeds[7].X, row.PhotoLeds[7].X);

            var copy = row.Clone();
            copy.PhotoLeds[0].X = -1;
            Assert.NotEqual(-1, row.PhotoLeds[0].X);
        }
    }
}
