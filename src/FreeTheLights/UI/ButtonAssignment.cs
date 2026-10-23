using System;
using System.Collections.Generic;
using System.Linq;
using FreeTheLights.Core;
using FreeTheLights.Settings;
using Newtonsoft.Json;

namespace FreeTheLights.UI
{
    /// <summary>
    /// The wizard's button assignment: asks for the buttons and dials of a device one input slot after the other
    /// ("Press Left 3", "Turn Dial 1 clockwise") and assigns the next input SimHub reports to it. Single inputs or the
    /// rest can be skipped. Inputs arrive on SimHub's threads and are handled on the UI thread (<c>post</c>); like the
    /// inspector's capture, a session token keeps a late input from reaching a later prompt.
    /// </summary>
    public sealed class ButtonAssignment : ObservableObject
    {
        /// <summary>
        /// After an input is assigned, every input reported within this time is ignored: a bouncing button, an encoder
        /// detent reported twice, or one click that SimHub reports under two names.
        /// </summary>
        public const int LockoutMs = 400;

        public static readonly LedColor CurrentColor = LedColor.FromRgb(0xFF, 0xFF, 0xFF);
        public static readonly LedColor AssignedColor = LedColor.FromRgb(0x30, 0xC0, 0x50).Scale(0.5);

        private readonly InputState inputs;
        private readonly Action<Action> post;
        private readonly Func<long> clock;
        private readonly List<Entry> entries;
        private int index;
        private string note;
        private Action<string> handler;
        private object session;
        private string lastInput;
        private Entry lastEntry;
        private long lastInputMs = long.MinValue / 2;

        /// <param name="device">The device whose buttons and dials get the inputs (edited in place).</param>
        /// <param name="inputs">SimHub's inputs.</param>
        /// <param name="post">Runs an action on the UI thread.</param>
        /// <param name="clock">Milliseconds, for ignoring repeated inputs.</param>
        public ButtonAssignment(DeviceConfig device, InputState inputs, Action<Action> post, Func<long> clock)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            this.inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
            this.post = post ?? throw new ArgumentNullException(nameof(post));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            entries = Queue(device).ToList();
        }

        public DeviceConfig Device { get; }

        /// <summary>Changes whenever an input is assigned or the prompt moves (for redrawing the device).</summary>
        public int Revision { get; private set; }

        public int Count => entries.Count;

        /// <summary>How many of the inputs asked for have one.</summary>
        public int AssignedCount => entries.Count(e => !string.IsNullOrEmpty(e.Element.GetInput(e.Slot)));

        /// <summary>Position of the current prompt; <see cref="Count"/> once every input was asked for.</summary>
        public int Index => index;

        public bool IsDone => index >= entries.Count;

        public bool CanGoBack => index > 0;

        /// <summary>The element asked for, or null when done.</summary>
        public LedGroup Current => IsDone ? null : entries[index].Element;

        public DevicePart CurrentPart => IsDone ? null : entries[index].Part;

        public InputSlot CurrentSlot => IsDone ? InputSlot.Button : entries[index].Slot;

        /// <summary>What to do: "Press Left 3", "Turn Dial 1 clockwise"; a summary once done.</summary>
        public string Prompt
        {
            get
            {
                if (IsDone)
                {
                    var assigned = AssignedCount;
                    return assigned == entries.Count ? "All buttons are assigned." : $"{assigned} of {entries.Count} assigned.";
                }

                var entry = entries[index];
                switch (entry.Slot)
                {
                    case InputSlot.Clockwise: return $"Turn {entry.Element.Name} clockwise";
                    case InputSlot.CounterClockwise: return $"Turn {entry.Element.Name} counter-clockwise";
                    default: return $"Press {entry.Element.Name}";
                }
            }
        }

        /// <summary>"4 of 18"; empty once done.</summary>
        public string Progress => IsDone ? string.Empty : $"{index + 1} of {entries.Count}";

        /// <summary>The input of the current prompt, if it already has one.</summary>
        public string CurrentInput => IsDone ? null : NullIfEmpty(InputState.DisplayName(entries[index].Element.GetInput(entries[index].Slot)));

        /// <summary>What happened to the last input ("Left 2: JoystickPlugin.Wheel_B05"), or null.</summary>
        public string Note { get => note; private set => Set(ref note, value); }

        /// <summary>The buttons and dials in the order they are asked for: per part left side, dials, right side, each top to bottom.</summary>
        public static IEnumerable<(DevicePart Part, LedGroup Element, InputSlot Slot)> Order(DeviceConfig device)
        {
            foreach (var part in device.Parts)
            {
                var elements = part.Elements
                    .Where(e => e.CanBePressed)
                    .OrderBy(e => e.Side == LedGroupSide.Left ? 0 : e.Side == LedGroupSide.None ? 1 : 2)
                    .ThenBy(e => e.HasPhotoPosition ? e.PhotoY : e.ElementSlot)
                    .ThenBy(e => e.HasPhotoPosition ? e.PhotoX : 0)
                    .ThenBy(e => e.StartIndex);
                foreach (var element in elements)
                {
                    foreach (var slot in element.InputSlots) yield return (part, element, slot);
                }
            }
        }

        /// <summary>Whether a device has anything to assign.</summary>
        public static bool HasInputs(DeviceConfig device) => device != null && device.Parts.Any(p => p.Elements.Any(e => e.CanBePressed));

        /// <summary>Starts listening to SimHub's inputs.</summary>
        public void Start()
        {
            if (handler != null) return;
            var token = new object();
            session = token;
            handler = input =>
            {
                // The time it was reported, not when the UI thread gets to it: inputs queued together stay close together.
                var at = clock();
                post(() => Captured(token, input, at));
            };
            inputs.Pressed += handler;
        }

        /// <summary>Stops listening (the step was left).</summary>
        public void Stop()
        {
            if (handler == null) return;
            inputs.Pressed -= handler;
            handler = null;
            session = null;
        }

        /// <summary>Leaves the current input as it is and asks for the next one.</summary>
        public void Skip()
        {
            if (IsDone) return;
            Note = null;
            LeavePrompt();
            MoveTo(index + 1);
        }

        /// <summary>Asks for the previous input again (its current input can be pressed again for it).</summary>
        public void Back()
        {
            if (!CanGoBack) return;
            Note = null;
            LeavePrompt();
            MoveTo(index - 1);
        }

        /// <summary>The user moved to another prompt (Skip, Previous).</summary>
        private void LeavePrompt()
        {
            lastInput = null;
            lastEntry = null;

            // Inputs on their way to the UI thread belonged to the prompt that was left: a new session drops them.
            if (handler == null) return;
            Stop();
            Start();
        }

        /// <summary>A part's frame showing the progress: the element asked for white, assigned ones dim green, the rest off.</summary>
        public LedColor[] Frame(DevicePart part)
        {
            var frame = new LedColor[part.TotalLeds];
            foreach (var element in part.Elements)
            {
                if (element.StartIndex < 0 || element.StartIndex >= frame.Length) continue;
                frame[element.StartIndex] = ColorOf(element);
            }

            return frame;
        }

        /// <summary>
        /// A copy of the device (same ids) for the game loop that shows the progress on the hardware: every feature off,
        /// the elements' static colors as in <see cref="Frame"/>.
        /// </summary>
        public DeviceConfig DisplayDevice()
        {
            var copy = JsonConvert.DeserializeObject<DeviceConfig>(JsonConvert.SerializeObject(Device));
            copy.EnsureDefaults();
            foreach (var part in copy.Parts)
            {
                foreach (var feature in part.Features) feature.Enabled = false;
                part.StaticColors.Enabled = true;
                var original = Device.FindPart(part.Id);
                foreach (var element in part.Elements)
                {
                    var source = original?.Groups.FirstOrDefault(g => g.Id == element.Id);
                    element.StaticColor = source == null ? LedColor.Off.ToHex() : ColorOf(source).ToHex();
                }
            }

            return copy;
        }

        private LedColor ColorOf(LedGroup element)
        {
            if (element == Current) return CurrentColor;
            if (element.CanBePressed && element.InputSlots.All(s => !string.IsNullOrEmpty(element.GetInput(s)))) return AssignedColor;
            return LedColor.Off;
        }

        /// <param name="at">When SimHub reported the input (<c>clock</c>).</param>
        private void Captured(object token, string input, long at)
        {
            if (token != session || IsDone || string.IsNullOrEmpty(input)) return;

            // Anything right after an assigned input belongs to the same click or turn.
            if (at - lastInputMs < LockoutMs) return;

            // The input just assigned, again: a held button repeating, the dial turned on, or the previous button pressed by
            // mistake. It stays where it is; Previous asks for that button again.
            var entry = entries[index];
            if (lastEntry != null && string.Equals(input, lastInput, StringComparison.OrdinalIgnoreCase))
            {
                Note = $"That's already {SlotName(lastEntry)}. {Prompt}, or click Previous to change {SlotName(lastEntry)}.";
                return;
            }

            lastInput = input;
            lastEntry = entry;
            lastInputMs = at;

            // One input belongs to one button or dial direction: an input pressed again for another one moves there.
            string movedFrom = null;
            foreach (var other in entries)
            {
                if (other.Element == entry.Element && other.Slot == entry.Slot) continue;
                if (!string.Equals(other.Element.GetInput(other.Slot), input, StringComparison.OrdinalIgnoreCase)) continue;
                other.Element.SetInput(other.Slot, string.Empty);
                movedFrom = SlotName(other);
            }

            entry.Element.SetInput(entry.Slot, input);
            Note = $"{SlotName(entry)}: {InputState.DisplayName(input)}" + (movedFrom == null ? string.Empty : $" (moved from {movedFrom})");
            MoveTo(index + 1);
        }

        private static string SlotName(Entry entry)
        {
            switch (entry.Slot)
            {
                case InputSlot.Clockwise: return entry.Element.Name + " clockwise";
                case InputSlot.CounterClockwise: return entry.Element.Name + " counter-clockwise";
                default: return entry.Element.Name;
            }
        }

        private void MoveTo(int newIndex)
        {
            index = Math.Max(0, Math.Min(entries.Count, newIndex));
            Revision++;
            OnPropertyChanged(nameof(Index));
            OnPropertyChanged(nameof(IsDone));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(Current));
            OnPropertyChanged(nameof(CurrentPart));
            OnPropertyChanged(nameof(CurrentSlot));
            OnPropertyChanged(nameof(Prompt));
            OnPropertyChanged(nameof(Progress));
            OnPropertyChanged(nameof(CurrentInput));
            OnPropertyChanged(nameof(Revision));
        }

        private static IEnumerable<Entry> Queue(DeviceConfig device) => Order(device).Select(o => new Entry(o.Part, o.Element, o.Slot));

        private static string NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

        private sealed class Entry
        {
            public Entry(DevicePart part, LedGroup element, InputSlot slot)
            {
                Part = part;
                Element = element;
                Slot = slot;
            }

            public DevicePart Part { get; }

            public LedGroup Element { get; }

            public InputSlot Slot { get; }
        }
    }
}
