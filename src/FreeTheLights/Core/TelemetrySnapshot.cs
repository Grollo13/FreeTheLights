namespace FreeTheLights.Core
{
    /// <summary>
    /// The subset of SimHub's normalized game data the features need.
    /// Built once per game loop at the plugin boundary so the engine never touches SimHub types.
    /// </summary>
    public class TelemetrySnapshot
    {
        public bool GameRunning { get; set; }
        public string GameName { get; set; }
        public string CarId { get; set; }
        public string CarModel { get; set; }

        public double Rpm { get; set; }
        public double MaxRpm { get; set; }
        public double Redline { get; set; }
        public string Gear { get; set; }

        public bool FlagYellow { get; set; }
        public bool FlagBlue { get; set; }
        public bool FlagWhite { get; set; }
        public bool FlagBlack { get; set; }
        public bool FlagCheckered { get; set; }
        public bool FlagGreen { get; set; }
        public bool FlagOrange { get; set; }

        /// <summary>Debris on track (yellow flag with red stripes); only iRacing reports it (<see cref="IRacingSessionFlags"/>).</summary>
        public bool FlagDebris { get; set; }
        public string FlagName { get; set; }

        public bool AbsActive { get; set; }
        public bool TcActive { get; set; }

        public bool SpotterCarLeft { get; set; }
        public bool SpotterCarRight { get; set; }

        /// <summary>Distance to the car on the left in meters; 0 or NaN when the sim does not provide it.</summary>
        public double SpotterCarLeftDistance { get; set; }

        /// <summary>Distance to the car on the right in meters; 0 or NaN when the sim does not provide it.</summary>
        public double SpotterCarRightDistance { get; set; }

        /// <summary>The driver's inputs (buttons, encoders), for elements that react to them; null = none known. Shared, not a copy.</summary>
        public InputState Inputs { get; set; }

        /// <summary>Test mode: every element with a press color shows it, as if all its inputs were pressed.</summary>
        public bool SimulatePress { get; set; }
    }
}
