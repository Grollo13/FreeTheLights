using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>
    /// The device wizard's SimHub profile step: whether SimHub's active LED profile of each part's channel shows the part,
    /// and setting up the profiles of those that don't (through <see cref="IWizardHost.Connect"/>). The wizard decides
    /// when the step exists and what follows from a connection (<see cref="Changed"/>).
    /// </summary>
    public sealed class WizardProfileStep : ObservableObject
    {
        private readonly IWizardHost host;
        private readonly Func<DeviceConfig> build;
        private string message;

        /// <param name="host">SimHub; null = nothing can be checked or connected.</param>
        /// <param name="build">The device as the wizard will add it (the same object while nothing changed).</param>
        public WizardProfileStep(IWizardHost host, Func<DeviceConfig> build)
        {
            this.host = host;
            this.build = build ?? throw new ArgumentNullException(nameof(build));
            ConnectCommand = new RelayCommand(_ => ConnectAll(), _ => CanConnect);
        }

        /// <summary>Raised after the connection states were read again (see <see cref="Refresh"/>).</summary>
        public event EventHandler Changed;

        /// <summary>Each part's SimHub channel: whether its active LED profile shows the part, and what setting it up does.</summary>
        public ObservableCollection<ProfileRow> Connections { get; } = new ObservableCollection<ProfileRow>();

        public bool CanConnect => Connections.Any(c => c.Connection.CanConnect);

        /// <summary>At least one part is shown by its SimHub channel.</summary>
        public bool IsConnected => Connections.Any(c => c.IsConnected);

        /// <summary>Every part is shown; an LED matrix, which Connect can't set up, still needs the manual steps.</summary>
        public bool IsFullyConnected => Connections.Count > 0 && Connections.All(c => c.IsConnected);

        /// <summary>The result of the last setup (errors), or null.</summary>
        public string Message { get => message; private set => Set(ref message, value); }

        public ICommand ConnectCommand { get; }

        /// <summary>The parts SimHub's profiles show (their ids), for the step's preview.</summary>
        public IEnumerable<Guid> ConnectedPartIds => Connections.Where(c => c.IsConnected).Select(c => c.Connection.Part.Id);

        /// <summary>SimHub's profile shows the part with <paramref name="partId"/>.</summary>
        public bool IsPartConnected(Guid partId) => Connections.Any(c => c.IsConnected && c.Connection.Part.Id == partId);

        /// <summary>SimHub's profile shows a part on <paramref name="channel"/>.</summary>
        public bool IsChannelConnected(LedChannel channel) => Connections.Any(c => c.IsConnected && c.Connection.Part.Channel == channel);

        /// <summary>
        /// Checks which parts SimHub's active LED profiles show; with <paramref name="available"/> false (the device has
        /// no profile step) none. Raises <see cref="Changed"/>.
        /// </summary>
        public void Refresh(bool available)
        {
            Connections.Clear();
            if (available && host != null)
            {
                var device = build();
                IReadOnlyList<PartConnection> connections;
                try
                {
                    connections = host.Check(device);
                }
                catch (Exception)
                {
                    connections = device.Parts.Select(p => new PartConnection(p, ConnectionState.Unknown, null)).ToList();
                }

                foreach (var connection in connections) Connections.Add(new ProfileRow(connection));
            }

            StatesChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Forgets the states and the last message: other hardware was picked.</summary>
        public void Reset()
        {
            Connections.Clear();
            Message = null;
            StatesChanged();
        }

        private void StatesChanged()
        {
            OnPropertyChanged(nameof(CanConnect));
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsFullyConnected));
        }

        /// <summary>Sets up the LED profile of every part that isn't shown yet.</summary>
        private void ConnectAll()
        {
            if (host == null) return;
            var device = build();
            var errors = new List<string>();
            foreach (var row in Connections.Where(c => c.Connection.CanConnect).ToList())
            {
                string error;
                try
                {
                    error = host.Connect(device, row.Connection.Part);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                if (error != null) errors.Add(row.Connection.Part.Name + ": " + error);
            }

            Message = errors.Count == 0 ? null : string.Join("\n", errors);
            Refresh(true);
        }
    }

    /// <summary>One part on the wizard's SimHub profile step.</summary>
    public sealed class ProfileRow
    {
        public ProfileRow(PartConnection connection)
        {
            Connection = connection;
        }

        public PartConnection Connection { get; }

        public string PartName => Connection.Part.Name;

        public bool IsConnected => Connection.State == ConnectionState.Connected;

        /// <summary>The state in a few words, or for a matrix that isn't shown yet, where to set it up.</summary>
        public string Text
        {
            get
            {
                switch (Connection.State)
                {
                    case ConnectionState.Connected: return "connected";
                    case ConnectionState.WrongDevice: return "shows another FreeTheLights device";
                    case ConnectionState.DeviceMissing: return "add it in SimHub first";
                    case ConnectionState.NotConnected: return Connection.Part.Channel == LedChannel.Matrix ? "set up by hand in SimHub" : "not connected";
                    default: return "unknown";
                }
            }
        }
    }
}
