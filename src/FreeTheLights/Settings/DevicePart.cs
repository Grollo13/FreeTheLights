using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FreeTheLights.Core;
using Newtonsoft.Json;

namespace FreeTheLights.Settings
{
    /// <summary>Feature layers of a device. Values are explicit because they are stored in the settings; never renumber them.</summary>
    public enum LayerKind
    {
        Flags = 0,
        Proximity = 1,
        Abs = 2,
        TractionControl = 3,
        Rpm = 4,

        /// <summary>Static colors of the front plate elements (the lowest layer by default).</summary>
        StaticColor = 5,

        /// <summary>Front plate elements whose button is pressed or dial turned (the highest layer by default).</summary>
        Press = 6,
    }

    /// <summary>
    /// The SimHub LED channel a part drives. SimHub keeps one LED profile per channel, numbered from LED 1.
    /// Values are explicit because they are stored in the settings; never renumber them.
    /// </summary>
    public enum LedChannel
    {
        /// <summary>RPM / shift lights and other "LEDs" (dashboards: also the side LEDs).</summary>
        Leds = 0,
        Buttons = 1,
        Encoders = 2,
        Matrix = 3,
    }

    /// <summary>
    /// One feature: its layer, its name in the UI and where a device keeps its settings.
    /// <see cref="DevicePart.AllFeatures"/> is the one list of features; everything else looks features up there.
    /// </summary>
    public sealed class FeatureInfo
    {
        public FeatureInfo(LayerKind kind, string displayName, Func<DevicePart, FeatureSettingsBase> settingsOf)
        {
            Kind = kind;
            DisplayName = displayName;
            SettingsOf = settingsOf;
        }

        public LayerKind Kind { get; }

        public string DisplayName { get; }

        /// <summary>The feature's settings within a device.</summary>
        public Func<DevicePart, FeatureSettingsBase> SettingsOf { get; }
    }

    /// <summary>
    /// One LED output of a device: the LEDs of one SimHub channel (for example a wheel's shift lights or its button
    /// LEDs), with their layout, features and layer priority. Parts of a device are independent of each other.
    /// </summary>
    public class DevicePart : ObservableObject
    {
        private Guid id = Guid.NewGuid();
        private string name = "LEDs";
        private LedChannel channel;
        private int brightness = 100;

        public Guid Id { get => id; set => Set(ref id, value); }

        public string Name { get => name; set => Set(ref name, value); }

        public LedChannel Channel { get => channel; set => Set(ref channel, value); }

        /// <summary>
        /// The SimHub LED profiles FreeTheLights created for this part (one per SimHub device of the preset's type).
        /// Only these, and profiles named like them, are changed by Connect; other profiles are never touched.
        /// </summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<Guid> SimHubProfileIds { get; set; } = new List<Guid>();

        /// <summary>Brightness of this part in percent.</summary>
        public int Brightness { get => brightness; set => Set(ref brightness, Math.Max(0, Math.Min(100, value))); }

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<LedGroup> Groups { get; set; } = new ObservableCollection<LedGroup>();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public RpmSettings Rpm { get; set; } = new RpmSettings();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public FlagSettings Flags { get; set; } = new FlagSettings();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ProximitySettings Proximity { get; set; } = new ProximitySettings();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public IndicatorSettings Abs { get; set; } = new IndicatorSettings(IndicatorSettings.AbsDefaultColor);

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public IndicatorSettings TractionControl { get; set; } = new IndicatorSettings(IndicatorSettings.TractionControlDefaultColor);

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public StaticColorSettings StaticColors { get; set; } = new StaticColorSettings();

        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public PressSettings Press { get; set; } = new PressSettings();

        /// <summary>Which presets the front plate elements were created from (see <see cref="ApplyFrontPlate"/>).</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public FrontPlateLayout FrontPlate { get; set; } = new FrontPlateLayout();

        /// <summary>Layer priority, highest first: where layers overlap, the first lit layer wins.</summary>
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public ObservableCollection<LayerKind> LayerPriority { get; set; } = new ObservableCollection<LayerKind>();

        /// <summary>All features, in the default layer priority (highest first). Adding a feature starts here.</summary>
        public static readonly FeatureInfo[] AllFeatures =
        {
            new FeatureInfo(LayerKind.Press, "Button press", d => d.Press),
            new FeatureInfo(LayerKind.Flags, "Race control flags", d => d.Flags),
            new FeatureInfo(LayerKind.Proximity, "Car proximity", d => d.Proximity),
            new FeatureInfo(LayerKind.Abs, "ABS", d => d.Abs),
            new FeatureInfo(LayerKind.TractionControl, "Traction control", d => d.TractionControl),
            new FeatureInfo(LayerKind.Rpm, "RPM", d => d.Rpm),
            new FeatureInfo(LayerKind.StaticColor, "Static colors", d => d.StaticColors),
        };

        /// <summary>The effects a front plate element can show (one at a time), in the order they are offered.</summary>
        public static readonly LayerKind[] ElementEffects = { LayerKind.Flags, LayerKind.Proximity, LayerKind.Abs, LayerKind.TractionControl };

        /// <summary>The feature of a layer, or null for an unknown layer (for example from a newer version's settings).</summary>
        public static FeatureInfo FindFeature(LayerKind layer)
        {
            foreach (var feature in AllFeatures)
            {
                if (feature.Kind == layer) return feature;
            }

            return null;
        }

        /// <summary>
        /// Total number of LEDs the part's LED strip needs (highest used index + 1): the length of its frames. The game
        /// loop reads it for every part on every frame, so it doesn't allocate.
        /// </summary>
        [JsonIgnore]
        public int TotalLeds
        {
            get
            {
                var total = 0;
                foreach (var group in Groups)
                {
                    if (group != null && group.EndIndexExclusive > total) total = group.EndIndexExclusive;
                }

                return total;
            }
        }

        /// <summary>Number of LED indices used by more than one group (0 = the layout is fine).</summary>
        public int CountOverlappingLeds()
        {
            // Per index: 0 = free, 1 = used once, 2 = used more than once (counted).
            var used = new byte[TotalLeds];
            var overlaps = 0;
            foreach (var group in Groups)
            {
                if (group == null) continue;
                for (var i = group.StartIndex; i < group.EndIndexExclusive; i++)
                {
                    if (used[i] < 2 && ++used[i] == 2) overlaps++;
                }
            }

            return overlaps;
        }

        [JsonIgnore]
        public IEnumerable<FeatureSettingsBase> Features => AllFeatures.Select(f => f.SettingsOf(this));

        /// <summary>Settings of a layer's feature, or null for an unknown layer.</summary>
        public FeatureSettingsBase GetFeature(LayerKind layer) => FindFeature(layer)?.SettingsOf(this);

        /// <summary>Repairs settings loaded from an older or partial file.</summary>
        public void EnsureDefaults()
        {
            if (Groups == null) Groups = new ObservableCollection<LedGroup>();
            foreach (var broken in Groups.Where(g => g == null).ToList()) Groups.Remove(broken);
            if (Rpm == null) Rpm = new RpmSettings();
            if (Flags == null) Flags = new FlagSettings();
            if (Proximity == null) Proximity = new ProximitySettings();
            if (Abs == null) Abs = new IndicatorSettings(IndicatorSettings.AbsDefaultColor);
            if (TractionControl == null) TractionControl = new IndicatorSettings(IndicatorSettings.TractionControlDefaultColor);
            if (StaticColors == null) StaticColors = new StaticColorSettings();
            if (Press == null) Press = new PressSettings();
            if (FrontPlate == null) FrontPlate = new FrontPlateLayout();
            foreach (var feature in Features)
            {
                if (feature.TargetGroupIds == null) feature.TargetGroupIds = new ObservableCollection<Guid>();
            }

            Flags.EnsureDefaults();
            RepairElements();

            if (LayerPriority == null) LayerPriority = new ObservableCollection<LayerKind>();

            // Unknown layers (for example from a newer version's settings) are dropped.
            foreach (var unknown in LayerPriority.Where(l => FindFeature(l) == null).ToList()) LayerPriority.Remove(unknown);
            foreach (var duplicate in LayerPriority.GroupBy(l => l).Where(g => g.Count() > 1).Select(g => g.Key).ToList())
            {
                while (LayerPriority.Count(l => l == duplicate) > 1) LayerPriority.Remove(duplicate);
            }

            // Missing layers are added last, except the press layer: the driver's own input wins over everything.
            if (!LayerPriority.Contains(LayerKind.Press)) LayerPriority.Insert(0, LayerKind.Press);
            foreach (var feature in AllFeatures)
            {
                if (!LayerPriority.Contains(feature.Kind)) LayerPriority.Add(feature.Kind);
            }
        }

        /// <summary>
        /// Adds a group and selects it for every feature whose default targets include its kind
        /// (vertical columns for flags/proximity/ABS/TC, the first row for RPM, matrices for flags).
        /// </summary>
        public void AddGroup(LedGroup group)
        {
            group.StartIndex = TotalLeds;
            Groups.Add(group);
            foreach (var feature in Features)
            {
                if (!feature.DefaultKinds.Contains(group.Kind)) continue;
                if (feature == Rpm && Groups.Any(g => g != group && Rpm.Targets(g))) continue;
                feature.TargetGroupIds.Add(group.Id);
            }
        }

        public void RemoveGroup(LedGroup group)
        {
            Groups.Remove(group);
            foreach (var feature in Features)
            {
                feature.TargetGroupIds.Remove(group.Id);
            }
        }

        // ---------- Front plate elements ----------

        [JsonIgnore]
        public IEnumerable<LedGroup> Elements => Groups.Where(g => g != null && g.IsElement);

        /// <summary>The effect an element shows, or null for none (it then shows its static color).</summary>
        public LayerKind? GetElementEffect(LedGroup element)
        {
            foreach (var effect in ElementEffects)
            {
                if (GetFeature(effect).TargetGroupIds.Contains(element.Id)) return effect;
            }

            return null;
        }

        /// <summary>Makes <paramref name="effect"/> the only effect of the element (null = no effect).</summary>
        public void SetElementEffect(LedGroup element, LayerKind? effect)
        {
            if (effect.HasValue && Array.IndexOf(ElementEffects, effect.Value) < 0) throw new ArgumentOutOfRangeException(nameof(effect));
            foreach (var other in ElementEffects)
            {
                var targets = GetFeature(other).TargetGroupIds;
                if (effect == other)
                {
                    if (!targets.Contains(element.Id)) targets.Add(element.Id);
                }
                else
                {
                    while (targets.Remove(element.Id))
                    {
                    }
                }
            }
        }

        /// <summary>Number of elements that <see cref="ApplyFrontPlate"/> with these presets would remove.</summary>
        public int CountElementsRemovedBy(string dialPresetId, string sidePresetId)
        {
            var dials = FrontPlatePresets.FindDials(dialPresetId);
            var sides = FrontPlatePresets.FindSides(sidePresetId);
            return Elements.Count(e => !FitsLayout(e, dials, sides));
        }

        /// <summary>
        /// Switches the front plate to the given presets. Elements whose position still exists keep all their settings
        /// (type, static color, effect, start index); the others are removed. New elements are appended to the LED strip
        /// in the order left side, dials, right side.
        /// </summary>
        public void ApplyFrontPlate(string dialPresetId, string sidePresetId)
        {
            var dials = FrontPlatePresets.FindDials(dialPresetId);
            var sides = FrontPlatePresets.FindSides(sidePresetId);
            FrontPlate.DialPresetId = dials.Id;
            FrontPlate.SidePresetId = sides.Id;

            foreach (var surplus in Elements.Where(e => !FitsLayout(e, dials, sides)).ToList()) RemoveGroup(surplus);

            for (var slot = 0; slot < sides.Count; slot++) EnsureElement(LedGroupSide.Left, slot, ElementType.Button, "Left " + (slot + 1));
            for (var slot = 0; slot < dials.Count; slot++) EnsureElement(LedGroupSide.None, slot, ElementType.Dial, "Dial " + (slot + 1));
            for (var slot = 0; slot < sides.Count; slot++) EnsureElement(LedGroupSide.Right, slot, ElementType.Button, "Right " + (slot + 1));
        }

        /// <summary>
        /// Numbers the elements consecutively after all other groups: left side, dials, right side. Elements from a
        /// SimHub layout keep their LED numbers.
        /// </summary>
        public void PackFrontPlateIndices()
        {
            var next = Groups.Where(g => g != null && !g.IsElement).Select(g => g.EndIndexExclusive).DefaultIfEmpty(0).Max();
            foreach (var element in ElementsInWiringOrder().Where(e => !e.HasPhotoPosition).ToList()) element.StartIndex = next++;
        }

        /// <summary>Elements ordered left side, dials, right side, each by slot.</summary>
        public IEnumerable<LedGroup> ElementsInWiringOrder()
        {
            return Elements.OrderBy(e => e.Side == LedGroupSide.Left ? 0 : e.Side == LedGroupSide.None ? 1 : 2).ThenBy(e => e.ElementSlot);
        }

        private static bool FitsLayout(LedGroup element, DialPreset dials, SidePreset sides)
        {
            return element.Side == LedGroupSide.None ? element.ElementSlot < dials.Count : element.ElementSlot < sides.Count;
        }

        private void EnsureElement(LedGroupSide side, int slot, ElementType type, string name)
        {
            if (Elements.Any(e => e.Side == side && e.ElementSlot == slot)) return;
            AddGroup(new LedGroup { Kind = LedGroupKind.Element, Side = side, ElementSlot = slot, ElementType = type, Name = name });
        }

        /// <summary>
        /// Repairs loaded elements: a second element at the same position is removed, dials sit in the center, each
        /// element shows at most one effect (the first in <see cref="ElementEffects"/> order wins) and every element
        /// is a target of the static color and press layers.
        /// </summary>
        private void RepairElements()
        {
            var seen = new HashSet<string>();
            foreach (var element in Elements.ToList())
            {
                if (element.ElementType == ElementType.Dial) element.Side = LedGroupSide.None;
                else if (element.Side == LedGroupSide.None) element.ElementType = ElementType.Dial;

                if (!seen.Add(element.Side + "/" + element.ElementSlot))
                {
                    RemoveGroup(element);
                    continue;
                }

                SetElementEffect(element, GetElementEffect(element));
                if (!StaticColors.TargetGroupIds.Contains(element.Id)) StaticColors.TargetGroupIds.Add(element.Id);
                if (!Press.TargetGroupIds.Contains(element.Id)) Press.TargetGroupIds.Add(element.Id);
            }
        }

        /// <summary>
        /// The matrix group with <paramref name="id"/>, or the first matrix group when there is no such group
        /// (empty id, or the group was deleted). The LED flag effect and its editor both use this, so they agree.
        /// </summary>
        public LedGroup FindMatrixGroup(Guid id)
        {
            LedGroup first = null;
            foreach (var group in Groups)
            {
                if (group == null || group.Kind != LedGroupKind.Matrix) continue;
                if (group.Id == id) return group;
                if (first == null) first = group;
            }

            return first;
        }

        /// <summary>The rows, columns and matrices in the order they are chained on the LED strip (by start index).</summary>
        public List<LedGroup> GroupsInWiringOrder() =>
            Groups.Where(g => g != null && !g.IsElement).OrderBy(g => g.StartIndex).ToList();

        /// <summary>
        /// Moves a row, column or matrix within the wiring order (<see cref="GroupsInWiringOrder"/>): the groups get
        /// consecutive start indices in the new order, from where the first of them started. Front plate elements keep
        /// their LED numbers. Returns false when nothing moved.
        /// </summary>
        public bool MoveWiring(int oldIndex, int newIndex)
        {
            var order = GroupsInWiringOrder();
            if (oldIndex < 0 || oldIndex >= order.Count || newIndex < 0 || newIndex >= order.Count || oldIndex == newIndex) return false;
            var moved = order[oldIndex];
            order.RemoveAt(oldIndex);
            order.Insert(newIndex, moved);
            Chain(order, order.Min(g => g.StartIndex));
            return true;
        }

        /// <summary>
        /// Chains the rows, columns and matrices on the strip again in their wiring order: after one was added, removed or
        /// resized there are no gaps or overlaps between them. They start at <paramref name="start"/>, or where the first
        /// of them starts; after removing the first group, pass where it started, or the strip begins with a gap. Front
        /// plate elements keep their LED numbers.
        /// </summary>
        public void PackWiring(int? start = null)
        {
            var order = GroupsInWiringOrder();
            if (order.Count > 0) Chain(order, start ?? order.Min(g => g.StartIndex));
        }

        private static void Chain(IEnumerable<LedGroup> order, int start)
        {
            foreach (var group in order)
            {
                group.StartIndex = start;
                start += group.LedCount;
            }
        }

        /// <summary>
        /// Re-packs the start indices so groups follow each other on the strip in list order. Elements from a SimHub
        /// layout keep their LED numbers.
        /// </summary>
        public void PackStartIndices()
        {
            var next = 0;
            foreach (var group in Groups.Where(g => !g.HasPhotoPosition))
            {
                group.StartIndex = next;
                next += group.LedCount;
            }
        }

        /// <summary>Deep copy with new ids for the part and its groups.</summary>
        public DevicePart Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            var copy = JsonConvert.DeserializeObject<DevicePart>(json);
            copy.Id = Guid.NewGuid();
            copy.SimHubProfileIds = new List<Guid>();
            foreach (var group in copy.Groups)
            {
                var oldId = group.Id;
                group.Id = Guid.NewGuid();
                foreach (var feature in copy.Features)
                {
                    var index = feature.TargetGroupIds.IndexOf(oldId);
                    if (index >= 0) feature.TargetGroupIds[index] = group.Id;
                }
            }

            copy.EnsureDefaults();
            return copy;
        }

        /// <summary>A typical wheel's LEDs: a 15 LED shift light row and two 3 LED columns.</summary>
        public static DevicePart CreateDefault(string name)
        {
            var device = new DevicePart { Name = name };
            device.EnsureDefaults();
            device.AddGroup(new LedGroup { Name = "RPM LEDs", Kind = LedGroupKind.HorizontalRow, Count = 15 });
            device.AddGroup(new LedGroup { Name = "Left column", Kind = LedGroupKind.VerticalColumn, Count = 3, Side = LedGroupSide.Left });
            device.AddGroup(new LedGroup { Name = "Right column", Kind = LedGroupKind.VerticalColumn, Count = 3, Side = LedGroupSide.Right });
            return device;
        }
    }
}
