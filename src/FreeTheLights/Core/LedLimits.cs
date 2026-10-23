namespace FreeTheLights.Core
{
    /// <summary>Bounds for LED numbers that come from files (downloaded car data, the settings file).</summary>
    public static class LedLimits
    {
        /// <summary>
        /// Most LEDs of a car file, and the highest start index of a group. The render loop allocates arrays of this size
        /// every frame, so a broken or hand-edited file must not make them huge. The settings page's inputs use the same
        /// limit (4096; NumericUpDown needs it as a literal double).
        /// </summary>
        public const int MaxLeds = 4096;
    }
}
