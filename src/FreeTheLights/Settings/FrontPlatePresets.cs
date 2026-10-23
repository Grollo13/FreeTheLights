using System;
using System.Linq;
using FreeTheLights.Core;

namespace FreeTheLights.Settings
{
    /// <summary>Arrangement of the dials in the center of a wheel: how many dials each row has, top to bottom.</summary>
    public sealed class DialPreset
    {
        public DialPreset(string id, string name, params int[] rows)
        {
            Id = id;
            Name = name;
            Rows = rows;
            Count = rows.Sum();
        }

        /// <summary>Stored in the settings; never change an existing id.</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>Dials per row, top to bottom. Shared array: never modify it.</summary>
        public int[] Rows { get; }

        public int Count { get; }

        public override string ToString() => Name;
    }

    /// <summary>Arrangement of the buttons/lights on one side of a wheel (the other side is its mirror image).</summary>
    public sealed class SidePreset
    {
        public SidePreset(string id, string name, int columns, int rows)
        {
            Id = id;
            Name = name;
            Columns = columns;
            Rows = rows;
        }

        /// <summary>Stored in the settings; never change an existing id.</summary>
        public string Id { get; }

        public string Name { get; }

        public int Columns { get; }

        public int Rows { get; }

        public int Count => Columns * Rows;

        public override string ToString() => Name;
    }

    /// <summary>
    /// The front plate layouts a user can pick. A layout is a <see cref="DialPreset"/> for the center plus a
    /// <see cref="SidePreset"/> for each side; the right side is always the mirror image of the left side.
    ///
    /// Slots: dials are numbered row by row, left to right. Side slots are numbered column by column starting at the
    /// outer edge of the wheel, top to bottom; right slot k is the mirror image of left slot k.
    /// </summary>
    public static class FrontPlatePresets
    {
        public const int MaxDials = 6;
        public const int MaxPerSide = 12;

        public const string NoneId = "none";

        public static readonly DialPreset[] Dials =
        {
            new DialPreset(NoneId, "No dials"),
            new DialPreset("1", "1 dial", 1),
            new DialPreset("2", "2 dials side by side", 2),
            new DialPreset("1-2", "3 dials: 1 on top, 2 below", 1, 2),
            new DialPreset("2-1", "3 dials: 2 on top, 1 below", 2, 1),
            new DialPreset("3", "3 dials in a row", 3),
            new DialPreset("2-2", "4 dials: 2 × 2", 2, 2),
            new DialPreset("1-2-1", "4 dials: diamond", 1, 2, 1),
            new DialPreset("3-1", "4 dials: 3 on top, 1 below", 3, 1),
            new DialPreset("1-3", "4 dials: 1 on top, 3 below", 1, 3),
            new DialPreset("2-1-2", "5 dials: X (2, 1, 2)", 2, 1, 2),
            new DialPreset("3-2", "5 dials: 3 on top, 2 below", 3, 2),
            new DialPreset("2-3", "5 dials: 2 on top, 3 below", 2, 3),
            new DialPreset("3-3", "6 dials: 2 rows of 3", 3, 3),
            new DialPreset("2-2-2", "6 dials: 3 rows of 2", 2, 2, 2),
            new DialPreset("1-2-2-1", "6 dials: hexagon (1, 2, 2, 1)", 1, 2, 2, 1),
        };

        public static readonly SidePreset[] Sides =
        {
            new SidePreset(NoneId, "No buttons or lights", 0, 0),
            new SidePreset("1x2", "1 column of 2", 1, 2),
            new SidePreset("1x3", "1 column of 3", 1, 3),
            new SidePreset("1x4", "1 column of 4", 1, 4),
            new SidePreset("1x6", "1 column of 6", 1, 6),
            new SidePreset("2x2", "2 columns of 2 (4)", 2, 2),
            new SidePreset("2x3", "2 columns of 3 (6)", 2, 3),
            new SidePreset("2x4", "2 columns of 4 (8)", 2, 4),
            new SidePreset("2x5", "2 columns of 5 (10)", 2, 5),
            new SidePreset("2x6", "2 columns of 6 (12)", 2, 6),
            new SidePreset("3x2", "3 columns of 2 (6)", 3, 2),
            new SidePreset("3x3", "3 columns of 3 (9)", 3, 3),
            new SidePreset("3x4", "3 columns of 4 (12)", 3, 4),
            new SidePreset("4x3", "4 columns of 3 (12)", 4, 3),
        };

        /// <summary>The dial preset with <paramref name="id"/>, or "no dials" for an unknown id.</summary>
        public static DialPreset FindDials(string id) => Dials.FirstOrDefault(p => p.Id == id) ?? Dials[0];

        /// <summary>The side preset with <paramref name="id"/>, or "no buttons" for an unknown id.</summary>
        public static SidePreset FindSides(string id) => Sides.FirstOrDefault(p => p.Id == id) ?? Sides[0];

        /// <summary>Row (from the top) and position within that row (from the left) of dial <paramref name="slot"/>.</summary>
        public static (int row, int indexInRow, int rowLength) DialCell(DialPreset preset, int slot)
        {
            var remaining = slot;
            for (var row = 0; row < preset.Rows.Length; row++)
            {
                if (remaining < preset.Rows[row]) return (row, remaining, preset.Rows[row]);
                remaining -= preset.Rows[row];
            }

            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        /// <summary>Column (0 = outer edge of the wheel) and row (0 = top) of side slot <paramref name="slot"/>.</summary>
        public static (int column, int row) SideCell(SidePreset preset, int slot)
        {
            if (slot < 0 || slot >= preset.Count) throw new ArgumentOutOfRangeException(nameof(slot));
            return (slot / preset.Rows, slot % preset.Rows);
        }

        /// <summary>
        /// Position of an element on the front plate in grid units: x = 0 is the wheel's vertical axis (negative = left),
        /// y = 0 is the top. Left and right elements with the same slot are mirror images (x and -x).
        /// </summary>
        public static (double x, double y) Position(FrontPlateLayout layout, LedGroupSide side, int slot)
        {
            var dials = FindDials(layout?.DialPresetId);
            var sides = FindSides(layout?.SidePresetId);

            // Dial area: one grid unit per dial, centered on the axis. The side grids start next to it.
            var dialHalfWidth = dials.Rows.Length == 0 ? 0.0 : dials.Rows.Max() / 2.0;

            // The shorter of the dial area and the side grids is centered vertically on the taller one.
            if (side == LedGroupSide.None)
            {
                var (row, index, length) = DialCell(dials, slot);
                return (index - (length - 1) / 2.0, row + Math.Max(0, (sides.Rows - dials.Rows.Length) / 2.0));
            }

            // Innermost column: half a unit of space next to the dials; column 0 is the outermost.
            var (column, sideRow) = SideCell(sides, slot);
            var distanceFromAxis = dialHalfWidth + 1 + (sides.Columns - 1 - column);
            var x = side == LedGroupSide.Left ? -distanceFromAxis : distanceFromAxis;
            return (x, sideRow + Math.Max(0, (dials.Rows.Length - sides.Rows) / 2.0));
        }
    }

    /// <summary>The front plate layout of a device: which dial and side presets its elements were created from.</summary>
    public class FrontPlateLayout : ObservableObject
    {
        private string dialPresetId = FrontPlatePresets.NoneId;
        private string sidePresetId = FrontPlatePresets.NoneId;

        public string DialPresetId { get => dialPresetId; set => Set(ref dialPresetId, value); }

        public string SidePresetId { get => sidePresetId; set => Set(ref sidePresetId, value); }
    }
}
