using LumaWall.ControlCenter;

int checks = 0;
void Check(bool passed, string label)
{
    if (!passed) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label);
    checks++;
}

// The current workspace uses the full available width for up to four equal
// slots. Selecting a detail panel consumes a slot without changing card width.
foreach (double fullWidth in new[] { 620d, 900, 1200, 1500, 2200 })
{
    var measured = LibraryPresentation.ResponsiveGrid(fullWidth);
    Check(measured.CardWidth >= 240 && measured.Columns <= 4,
        $"Workspace retains usable cards and at most four slots at {fullWidth} DIP");
    Check(LibraryPresentation.ResponsiveGrid(fullWidth, measured.Columns) == measured,
        $"Previous layout state does not change card sizing at {fullWidth} DIP");
}

// Sparse libraries use the same geometry: the calculation has no item count.
// Wide windows grow the existing four slots instead of adding more columns.
foreach (var (available, columns, width) in new[]
{
    (320d, 1, 320d), (491.99, 1, 491d), (492d, 2, 240d),
    (600d, 2, 294d), (743.99, 2, 365d), (744d, 3, 240d),
    (960d, 3, 312d), (995.99, 3, 323d), (996d, 4, 240d),
    (1104d, 4, 267d), (1280d, 4, 311d), (1600d, 4, 391d), (2560d, 4, 631d),
    (239.99, 1, 239d), (200d, 1, 200d), (1d, 1, 1d)
})
{
    Check(LibraryPresentation.ResponsiveGrid(available) == (columns, width),
        $"Viewport {available} DIP keeps {columns} columns of {width} DIP");
}

Check(LibraryPresentation.ResponsiveGrid(996, 3) == (4, 240), "Four slots start when every slot reaches its minimum usable width");
Check(LibraryPresentation.ResponsiveGrid(995, 4) == (3, 323), "Shrinking below the four-slot boundary restores three slots");
Check(LibraryPresentation.ResponsiveGrid(743, 3) == (2, 365), "The minimum card width takes priority over prior column state");

// Check row fit, minimum card width, and the stable four-slot wide workspace.
for (double available = 1; available < 4096; available += 0.5)
{
    var grid = LibraryPresentation.ResponsiveGrid(available);
    if (grid.Columns * grid.CardWidth + (grid.Columns - 1) * 12 > available || grid.Columns is < 1 or > 4 ||
        (available >= 240 && grid.CardWidth < 240) ||
        (available >= 744 && available < 996 && grid.Columns != 3) ||
        (available >= 996 && grid.Columns != 4))
        throw new Exception($"Invalid grid at {available}: {grid}");
}
Check(true, "Fractional viewport sweep respects card bounds and fits every row");

foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, 0, -1 })
    Check(LibraryPresentation.ResponsiveGrid(invalid) == (1, 1), "Unmeasured viewport has a safe fallback: " + invalid);

Check(LibraryPresentation.Grid(960) == (3, 320), "The effect-target picker retains fixed-size cells");

Console.WriteLine($"Passed {checks} library layout checks.");
