using System;
using FreeTheLights.CarData;
using FreeTheLights.Core;

namespace FreeTheLights.Engine
{
    /// <summary>
    /// Simulated inputs toggled from the settings UI so LEDs can be checked without driving.
    /// Test inputs are OR-ed into the live telemetry.
    /// </summary>
    public class TestMode : ObservableObject
    {
        private bool yellow;
        private bool blue;
        private bool white;
        private bool checkered;
        private bool black;
        private bool green;
        private bool debris;
        private bool meatball;
        private bool carLeft;
        private bool carRight;
        private double carDistance;
        private bool abs;
        private bool tc;
        private bool rpmSweep;
        private bool press;
        private bool idle;

        /// <summary>The part showing the wiring test pattern (boxed Guid, swapped atomically: the game loop reads it).</summary>
        private volatile object wiringPart = Guid.Empty;

        /// <summary>The device showing the confirmation pattern (boxed Guid, swapped atomically like <see cref="wiringPart"/>).</summary>
        private volatile object confirmationDevice = Guid.Empty;

        public bool Yellow { get => yellow; set => Set(ref yellow, value); }
        public bool Blue { get => blue; set => Set(ref blue, value); }
        public bool White { get => white; set => Set(ref white, value); }
        public bool Checkered { get => checkered; set => Set(ref checkered, value); }
        public bool Black { get => black; set => Set(ref black, value); }
        public bool Green { get => green; set => Set(ref green, value); }
        public bool Debris { get => debris; set => Set(ref debris, value); }
        public bool Meatball { get => meatball; set => Set(ref meatball, value); }
        public bool CarLeft { get => carLeft; set => Set(ref carLeft, value); }
        public bool CarRight { get => carRight; set => Set(ref carRight, value); }

        /// <summary>Simulated distance in meters for the simulated cars, 0 = distance not available.</summary>
        public double CarDistance { get => carDistance; set => Set(ref carDistance, Math.Max(0, value)); }

        public bool Abs { get => abs; set => Set(ref abs, value); }
        public bool Tc { get => tc; set => Set(ref tc, value); }
        public bool RpmSweep { get => rpmSweep; set => Set(ref rpmSweep, value); }

        /// <summary>Every front plate element with a press color shows it, as if its button were held.</summary>
        public bool Press { get => press; set => Set(ref press, value); }

        /// <summary>The idle animation shows, also while a game runs.</summary>
        public bool Idle { get => idle; set => Set(ref idle, value); }

        /// <summary>
        /// The part that shows <see cref="WiringPattern"/> instead of its features, on the device and in the preview;
        /// <see cref="Guid.Empty"/> = none.
        /// </summary>
        public Guid WiringPartId
        {
            get => (Guid)wiringPart;
            set
            {
                if (WiringPartId == value) return;
                wiringPart = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AnyActive));
            }
        }

        /// <summary>
        /// The device whose parts show <see cref="ConfirmationPattern"/> instead of their features (the wizard, after
        /// setting up its SimHub profile); <see cref="Guid.Empty"/> = none.
        /// </summary>
        public Guid ConfirmationDeviceId
        {
            get => (Guid)confirmationDevice;
            set
            {
                if (ConfirmationDeviceId == value) return;
                confirmationDevice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AnyActive));
            }
        }

        public bool AnyActive => AnyFeatureTest || Idle || WiringPartId != Guid.Empty || ConfirmationDeviceId != Guid.Empty;

        /// <summary>A test of a game feature is on: the idle animation then gives way, so it doesn't hide the test.</summary>
        private bool AnyFeatureTest => Yellow || Blue || White || Checkered || Black || Green || Debris || Meatball || CarLeft || CarRight || Abs || Tc || RpmSweep || Press;

        public void Clear()
        {
            Yellow = Blue = White = Checkered = Black = Green = Debris = Meatball = CarLeft = CarRight = Abs = Tc = RpmSweep = Press = Idle = false;
            WiringPartId = Guid.Empty;
            ConfirmationDeviceId = Guid.Empty;
        }

        public void Apply(TelemetrySnapshot t, CarLedProfile profile, long nowMs)
        {
            t.FlagYellow |= Yellow;
            t.FlagBlue |= Blue;
            t.FlagWhite |= White;
            t.FlagCheckered |= Checkered;
            t.FlagBlack |= Black;
            t.FlagGreen |= Green;
            t.FlagDebris |= Debris;
            t.FlagOrange |= Meatball;
            t.AbsActive |= Abs;
            t.TcActive |= Tc;
            t.SimulatePress |= Press;
            t.Idle = Idle || (!t.GameRunning && !AnyFeatureTest);

            if (CarLeft)
            {
                t.SpotterCarLeft = true;
                t.SpotterCarLeftDistance = CarDistance;
            }

            if (CarRight)
            {
                t.SpotterCarRight = true;
                t.SpotterCarRightDistance = CarDistance;
            }

            if (RpmSweep)
            {
                double redline;
                if (profile != null)
                {
                    t.Gear = "3";
                    var thresholds = profile.GetThresholds(t.Gear);
                    redline = thresholds[0] > 0 ? thresholds[0] : Max(thresholds);
                }
                else
                {
                    redline = t.Redline > 0 ? t.Redline : t.MaxRpm > 0 ? t.MaxRpm : 8000;
                    t.Redline = redline;
                }

                // Triangle wave 50% → 105% of the redline over 4 seconds.
                var phase = (nowMs % 4000) / 2000.0;
                var ramp = phase <= 1 ? phase : 2 - phase;
                t.Rpm = redline * (0.5 + 0.55 * ramp);
            }
        }

        private static double Max(int[] values)
        {
            var max = 0;
            foreach (var v in values) max = Math.Max(max, v);
            return max > 0 ? max : 8000;
        }
    }
}
