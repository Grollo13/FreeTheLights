namespace FreeTheLights.Core
{
    public static class Blink
    {
        /// <summary>
        /// True during the "off" half of a blink cycle where on and off each last <paramref name="intervalMs"/>.
        /// </summary>
        public static bool IsOffPhase(long nowMs, int intervalMs)
        {
            if (intervalMs <= 0) return false;
            return (nowMs / intervalMs) % 2 == 1;
        }
    }
}
