namespace FreeTheLights.Settings
{
    /// <summary>Whether a part's SimHub channel shows it.</summary>
    public enum ConnectionState
    {
        /// <summary>SimHub's device list could not be read (for example outside SimHub).</summary>
        Unknown,

        /// <summary>No device of this type is added in SimHub.</summary>
        DeviceMissing,

        /// <summary>The channel's active LED profile has no FreeTheLights effect for this part.</summary>
        NotConnected,

        /// <summary>The channel's active LED profile has a FreeTheLights effect, but it shows another (or a deleted) device.</summary>
        WrongDevice,

        /// <summary>The channel's active LED profile shows this part.</summary>
        Connected,
    }

    /// <summary>The connection of one part, and the SimHub profile it was checked in (see SimHubConnector.Check).</summary>
    public sealed class PartConnection
    {
        public PartConnection(DevicePart part, ConnectionState state, string profileName)
        {
            Part = part;
            State = state;
            ProfileName = profileName;
        }

        public DevicePart Part { get; }

        public ConnectionState State { get; }

        /// <summary>Name of the active LED profile of the part's channel, or null.</summary>
        public string ProfileName { get; }

        /// <summary>Matrix profiles are set up in SimHub's matrix editor; Connect handles LED strip channels.</summary>
        public bool CanConnect => (State == ConnectionState.NotConnected || State == ConnectionState.WrongDevice) && Part.Channel != LedChannel.Matrix;

        /// <summary>"Fix" when the profile shows another FreeTheLights device, else "Connect".</summary>
        public string ActionText => State == ConnectionState.WrongDevice ? "Fix" : "Connect";

        public string Text
        {
            get
            {
                switch (State)
                {
                    case ConnectionState.Connected: return "connected";
                    case ConnectionState.NotConnected: return "not connected";
                    case ConnectionState.WrongDevice: return "connected to another FreeTheLights device";
                    case ConnectionState.DeviceMissing: return "add this device in SimHub first";
                    default: return "unknown";
                }
            }
        }
    }
}
