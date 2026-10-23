using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using FreeTheLights.SimHubData;
using SimHub.Plugins.UI;

namespace FreeTheLights.UI
{
    /// <summary>
    /// Edits where a dashboard's LEDs are on its product picture, or switches its preview to the generic drawing.
    /// Works on copies of the groups; <see cref="Save_Click"/> writes them back.
    /// </summary>
    public partial class PhotoLedDialog : SHDialogContentBase
    {
        private readonly DeviceConfig device;
        private readonly PhotoLedEditor editor;

        public PhotoLedDialog(DeviceConfig device)
        {
            this.device = device;
            editor = new PhotoLedEditor(device);
            DataContext = editor;
            InitializeComponent();
            ShowOk = false;
            ShowCancel = false;
            ShowLowerContentBar = false;

            var picture = PreviewLayout.LoadPhoto(device.Preset?.PhotoFile);
            if (picture != null) editor.EnsurePositions(picture.PixelWidth, picture.PixelHeight);
            Placer.Show(picture, editor.Groups);
            Placer.PositionsChanged += (s, e) => editor.Edited();
        }

        /// <summary>A dashboard whose preview can show its LEDs on SimHub's picture.</summary>
        public static bool CanEdit(DeviceConfig device) => device?.Type == DeviceType.Dashboard && device.Preset?.PhotoFile != null && device.Parts.Count > 0;

        private async void Reset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var catalog = await SimHubCatalog.LoadAsync();
                var info = catalog.FirstOrDefault(d => string.Equals(d.DeviceTypeId, device.Preset?.DeviceTypeId, StringComparison.OrdinalIgnoreCase));
                editor.Reset(info == null ? null : await SimHubCatalog.LoadDashPhotoAsync(info));
                Placer.Show(PreviewLayout.LoadPhoto(device.Preset?.PhotoFile), editor.Groups);
            }
            catch (Exception ex)
            {
                SimHub.Logging.Current.Error("FreeTheLights: could not reset the LED positions", ex);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            editor.Apply();
            Close(System.Windows.Forms.DialogResult.OK);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close(System.Windows.Forms.DialogResult.Cancel);
    }

    /// <summary>State of <see cref="PhotoLedDialog"/>: copies of the dashboard's rows and columns and the choice of preview.</summary>
    public class PhotoLedEditor : ObservableObject
    {
        private readonly DeviceConfig device;
        private readonly List<LedGroup> originals;
        private PhotoLedSource source;
        private bool usePhoto;
        private bool edited;

        public PhotoLedEditor(DeviceConfig device)
        {
            this.device = device;
            var part = device.Parts.FirstOrDefault(p => p.Channel == LedChannel.Leds) ?? device.Parts.First();
            originals = part.Groups.Where(g => g.Kind == LedGroupKind.HorizontalRow || g.Kind == LedGroupKind.VerticalColumn).ToList();
            Groups = originals.Select(g =>
            {
                var copy = g.Clone();
                copy.Name = g.Name;
                return copy;
            }).ToList();
            source = device.Preset?.LedPositions ?? PhotoLedSource.None;
            usePhoto = source != PhotoLedSource.Generic;
        }

        public string Title => device.Name;

        /// <summary>Copies of the rows and columns being edited, in wiring order.</summary>
        public IList<LedGroup> Groups { get; }

        public bool UsePhoto
        {
            get => usePhoto;
            set
            {
                if (Set(ref usePhoto, value)) OnPropertyChanged(nameof(Status));
            }
        }

        public string Status
        {
            get
            {
                if (!UsePhoto) return "The preview shows a generic dashboard.";
                if (edited) return "Positions adjusted by you.";
                switch (source)
                {
                    case PhotoLedSource.BuiltIn: return "The LEDs are already in place.";
                    case PhotoLedSource.Found: return "The LEDs were found on the picture. Please check them.";
                    case PhotoLedSource.Manual: return "Positions placed by you.";
                    default: return "Drag the LEDs to their places.";
                }
            }
        }

        /// <summary>Gives groups without (or with outdated) positions a place on a picture of the given size.</summary>
        public void EnsurePositions(int width, int height)
        {
            DashPhotoLayouts.EnsurePositions(Groups, width, height);
        }

        public void Edited()
        {
            edited = true;
            OnPropertyChanged(nameof(Status));
        }

        /// <summary>Back to the positions FreeTheLights knows or finds for the picture (<paramref name="photo"/>), else the default places.</summary>
        public void Reset(DashPhotoResult photo)
        {
            foreach (var group in Groups) group.PhotoLeds = new List<PhotoLed>();
            if (photo?.Layout != null) DashPhotoLayouts.Apply(Groups, photo.Layout);
            var width = photo?.PictureWidth ?? (int)device.Preset.CanvasWidth;
            var height = photo?.PictureHeight ?? (int)device.Preset.CanvasHeight;
            DashPhotoLayouts.EnsurePositions(Groups, Math.Max(1, width), Math.Max(1, height));
            source = photo?.Layout != null ? photo.Source : PhotoLedSource.Manual;
            edited = false;
            OnPropertyChanged(nameof(Status));
        }

        /// <summary>Writes the positions and the choice of preview back to the device.</summary>
        public void Apply()
        {
            for (var i = 0; i < originals.Count; i++) originals[i].PhotoLeds = Groups[i].PhotoLeds.Select(l => l.Clone()).ToList();
            device.Preset.LedPositions = !UsePhoto ? PhotoLedSource.Generic
                : edited || source == PhotoLedSource.None || source == PhotoLedSource.Generic ? PhotoLedSource.Manual
                : source;
        }
    }
}
