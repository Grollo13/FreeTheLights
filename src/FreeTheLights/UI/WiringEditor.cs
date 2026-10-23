using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using FreeTheLights.Core;
using FreeTheLights.Settings;

namespace FreeTheLights.UI
{
    /// <summary>
    /// Edits how a part's rows, columns and matrices are wired on its LED strip, while the device shows the wiring test
    /// pattern: their order (drag), direction, LED count, side and name, and adding or removing groups. After each change
    /// the groups are chained on the strip again. Used by the device wizard's wiring step and the device page's wiring
    /// dialog (<see cref="WiringEditorView"/>); it never touches WPF.
    /// </summary>
    public sealed class WiringEditor : ObservableObject
    {
        public WiringEditor(DevicePart part, int channelLeds)
        {
            Part = part ?? throw new ArgumentNullException(nameof(part));
            ChannelLeds = Math.Max(0, channelLeds);
            MoveCommand = new RelayCommand(p => { if (p is MoveRequest r) Move(r.OldIndex, r.NewIndex); });
            AddCommand = new RelayCommand(p => Add(p as string));
            RemoveCommand = new RelayCommand(p => Remove(p as LedGroup));
            Refresh();
        }

        /// <summary>Raised after every change of the wiring (for the preview and the device).</summary>
        public event EventHandler Changed;

        public DevicePart Part { get; }

        /// <summary>LEDs of the part's SimHub channel, or 0 when SimHub doesn't say.</summary>
        public int ChannelLeds { get; }

        /// <summary>The rows, columns and matrices in the order they are wired.</summary>
        public ObservableCollection<LedGroup> Groups { get; } = new ObservableCollection<LedGroup>();

        /// <summary>Drag &amp; drop target of the list (parameter: <see cref="MoveRequest"/>).</summary>
        public ICommand MoveCommand { get; }

        /// <summary>Adds a row ("Row") or a column ("Column", on the side that has none yet), chained last.</summary>
        public ICommand AddCommand { get; }

        /// <summary>Removes a group (parameter: the <see cref="LedGroup"/>); the others close the gap.</summary>
        public ICommand RemoveCommand { get; }

        /// <summary>Sides a column can be on.</summary>
        public LedGroupSide[] ColumnSides { get; } = { LedGroupSide.Left, LedGroupSide.Right };

        /// <summary>How many of the channel's LEDs the groups use: "21 of 23 LEDs are in a group".</summary>
        public string LedSummary
        {
            get
            {
                var used = UsedLeds;
                if (ChannelLeds <= 0) return $"{used} LEDs in groups";
                if (used > ChannelLeds) return $"{used} LEDs in groups, but the device has only {ChannelLeds}";
                return $"{used} of {ChannelLeds} LEDs are in a group";
            }
        }

        /// <summary>The groups use more LEDs than the channel has.</summary>
        public bool HasTooManyLeds => ChannelLeds > 0 && UsedLeds > ChannelLeds;

        private int UsedLeds => Groups.Sum(g => g.LedCount);

        /// <summary>Moves a group within the wiring order (renumbers the LED strip).</summary>
        public void Move(int oldIndex, int newIndex)
        {
            if (!Part.MoveWiring(oldIndex, newIndex)) return;
            Refresh();
            RaiseChanged();
        }

        public void Add(string kind)
        {
            var free = Math.Max(0, ChannelLeds - UsedLeds);
            LedGroup group;
            if (kind == "Column")
            {
                var side = Part.Groups.Any(g => g.Kind == LedGroupKind.VerticalColumn && g.Side == LedGroupSide.Left) ? LedGroupSide.Right : LedGroupSide.Left;
                group = new LedGroup { Kind = LedGroupKind.VerticalColumn, Side = side, Name = side == LedGroupSide.Left ? "Left column" : "Right column", Count = free > 0 ? Math.Min(3, free) : 3 };
            }
            else
            {
                group = new LedGroup { Kind = LedGroupKind.HorizontalRow, Name = "Row", Count = free > 0 ? free : 10 };
            }

            Part.AddGroup(group);
            GroupsChanged(null);
        }

        public void Remove(LedGroup group)
        {
            if (group == null || !Part.Groups.Contains(group)) return;

            // The others close the gap from where the strip started, also when the first group goes.
            var start = Part.GroupsInWiringOrder().Min(g => g.StartIndex);
            Part.RemoveGroup(group);
            GroupsChanged(start);
        }

        /// <summary>Stops following the groups' changes (the editor is no longer shown).</summary>
        public void Detach()
        {
            foreach (var group in Groups) group.PropertyChanged -= Group_PropertyChanged;
        }

        /// <summary>A group was added or removed: chain them again (from <paramref name="start"/>, or where the first starts).</summary>
        private void GroupsChanged(int? start)
        {
            Part.PackWiring(start);
            Refresh();
            RaiseChanged();
        }

        private void Group_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Start indices change when the editor chains the groups; it reports those changes once itself.
            if (e.PropertyName == nameof(LedGroup.StartIndex)) return;

            if (e.PropertyName == nameof(LedGroup.Count) || e.PropertyName == nameof(LedGroup.Columns) || e.PropertyName == nameof(LedGroup.Rows))
            {
                // Only the start indices change: the list stays as it is, so the count being typed keeps its focus.
                Part.PackWiring();
                OnPropertyChanged(nameof(LedSummary));
                OnPropertyChanged(nameof(HasTooManyLeds));
            }

            RaiseChanged();
        }

        private void Refresh()
        {
            Detach();
            Groups.Clear();
            foreach (var group in Part.GroupsInWiringOrder())
            {
                group.PropertyChanged += Group_PropertyChanged;
                Groups.Add(group);
            }

            OnPropertyChanged(nameof(LedSummary));
            OnPropertyChanged(nameof(HasTooManyLeds));
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }
}
