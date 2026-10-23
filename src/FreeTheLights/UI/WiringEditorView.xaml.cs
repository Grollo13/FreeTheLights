using System;
using System.Windows.Controls;
using FreeTheLights.Core;
using FreeTheLights.Engine;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>The wiring editor's list and preview (DataContext: <see cref="WiringEditor"/>).</summary>
    public partial class WiringEditorView : UserControl
    {
        public WiringEditorView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Draws <paramref name="device"/> with the wiring test pattern on the edited part: the frame the game loop rendered
        /// for it (<paramref name="renderedFrame"/>), so the dot moves in step with the device, or the pattern drawn here
        /// when there is none (outside SimHub, or the game loop isn't running).
        /// </summary>
        public void UpdatePreview(DeviceConfig device, Func<Guid, LedColor[]> renderedFrame, long nowMs)
        {
            var part = (DataContext as WiringEditor)?.Part;
            var frame = part == null ? null : renderedFrame?.Invoke(part.Id) ?? WiringPattern.Render(part, nowMs);
            Preview.Update(device, id => part != null && id == part.Id ? frame : null);
            Preview.RefreshFrames();
        }
    }
}
