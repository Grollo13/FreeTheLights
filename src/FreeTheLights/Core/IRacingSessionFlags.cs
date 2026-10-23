using System;

namespace FreeTheLights.Core
{
    /// <summary>
    /// iRacing's session flags (irsdk_Flags, SimHub property DataCorePlugin.GameRawData.Telemetry.SessionFlags). SimHub's
    /// common game data has no debris flag, so FreeTheLights reads it from iRacing's own data.
    /// </summary>
    public static class IRacingSessionFlags
    {
        /// <summary>SimHub's property holding iRacing's session flags.</summary>
        public const string PropertyName = "DataCorePlugin.GameRawData.Telemetry.SessionFlags";

        /// <summary>irsdk_debris: debris (or a slippery surface) on track, the yellow flag with red stripes.</summary>
        public const long Debris = 0x40;

        /// <summary>
        /// Whether the session flags in <paramref name="value"/> (a number or iRacing's flags enum, as SimHub reports it)
        /// show the debris flag. False for anything else, including null.
        /// </summary>
        public static bool HasDebris(object value)
        {
            if (value == null) return false;
            try
            {
                return (Convert.ToInt64(value) & Debris) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
