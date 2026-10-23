using System.Collections.Generic;

namespace FreeTheLights.Settings
{
    /// <summary>
    /// The button LEDs of a SimHub device as SimHub draws them in its button color editor: a photo and one marker per
    /// button LED, in the coordinates of a canvas (1000 units wide). Plain data, read from SimHub at runtime.
    /// </summary>
    public class SimHubButtonLayout
    {
        /// <summary>Photo relative to the SimHub folder (DevicesUI\...), or null.</summary>
        public string PhotoFile { get; set; }

        public double CanvasWidth { get; set; } = 1000;

        public double CanvasHeight { get; set; }

        public List<ButtonMarker> Markers { get; } = new List<ButtonMarker>();
    }

    /// <summary>One button LED on the photo.</summary>
    public class ButtonMarker
    {
        /// <summary>LED number in the buttons channel (1 = first LED).</summary>
        public int LedNumber { get; set; }

        /// <summary>Center of the marker in canvas units.</summary>
        public double X { get; set; }

        public double Y { get; set; }

        /// <summary>Diameter in canvas units (about 40 = small light, 45-50 = button, 85 = large ring).</summary>
        public double Size { get; set; }

        /// <summary>SimHub hides markers of LED numbers the device doesn't use.</summary>
        public bool Visible { get; set; } = true;
    }
}
