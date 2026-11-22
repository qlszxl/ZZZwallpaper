using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LumaWall.Application.Steam;
using LumaWall.Contracts.Steam;

static partial class SteamAssetChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "ZZZwallpaper-steam-assets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var catalog = new SteamWallpaperCatalog();
            string scene = Project(root, "Scene", "scene");
            Directory.CreateDirectory(Path.Combine(scene, "materials"));
            File.WriteAllBytes(Path.Combine(scene, "preview.png"), [8, 9, 10]);
            File.WriteAllBytes(Path.Combine(scene, "materials", "art.png"), [1, 2, 3, 4]);
            File.WriteAllBytes(Path.Combine(scene, "background.mp4"), [9, 10, 11, 12]);
            File.WriteAllText(Path.Combine(scene, "index.js"), "throw new Error('never execute')");
            byte[] embedded = [1, 2, 3, 4];
            File.WriteAllBytes(Path.Combine(scene, "scene.pkg"), Package(("materials/packed.png", embedded), ("materials/second.png", embedded),
                ("preview.png", new byte[] { 8, 9, 10 }), ("scripts/unsafe.js", Encoding.UTF8.GetBytes("throw 1;"))));
            var project = (await catalog.DiscoverAsync([scene])).Projects.Single();
            var found = await catalog.InspectAssetsAsync(project);
            check(found.Issues.Length == 0 && found.Assets.Length == 4 && found.Assets.All(a => a.CanImport),
                "Scene material inspection includes loose images/videos and packed images without executing scripts");
            check(found.Assets.All(a => !a.RelativePath.Contains("preview")) && Directory.GetFiles(scene, "*", SearchOption.AllDirectories).Length == 6,
                "Inspection excludes loose/packed project previews and writes no extracted or cached files");
            check(found.Assets.Select(a => a.SourceId).Distinct().Count() == 4 &&
                (await catalog.InspectAssetsAsync(project)).Assets.Select(a => a.SourceId).SequenceEqual(found.Assets.Select(a => a.SourceId)),
                "Every project material has its own stable identity even when contents are identical");
            var loose = found.Assets.Single(a => a.RelativePath == "materials/art.png");
            var reference = await catalog.PrepareAssetAsync(loose);
            check(reference.Path == loose.MediaPath && !reference.Copied && reference.Source.ProjectSourceId == project.SourceId &&
                reference.Source.RelativePath == loose.RelativePath && SteamSourceIdentity.IsSteamManagedOriginal(reference.Path, reference.Source),
                "Loose scene material can be referenced in place with provenance and original-file protection");
            var packed = found.Assets.Single(a => a.PackageEntry == "materials/packed.png");
            check(packed.RequiresExtraction && packed.RequiresDestination && packed.PackagePath != null && packed.MediaPath == null,
                "Packed materials clearly require extraction and a user-chosen destination");
            await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(packed), check,
                "A packed material cannot bypass the destination requirement in reference mode");
            string copies = Path.Combine(root, "Copies");
            var extracted = await catalog.PrepareAssetAsync(packed, copies);
            var repeat = await catalog.PrepareAssetAsync(packed, copies);
            var sameContent = await catalog.PrepareAssetAsync(found.Assets.Single(a => a.PackageEntry == "materials/second.png"), copies);
            var copiedLoose = await catalog.PrepareAssetAsync(loose, copies);
            check(File.ReadAllBytes(extracted.Path).SequenceEqual(embedded) && repeat.Reused && sameContent.Reused && copiedLoose.Reused &&
                Directory.GetFiles(copies).Length == 1, "Packed and loose copies reuse identical content without duplicate disk files");
            check(extracted.Source.PackagePath == packed.PackagePath && extracted.Source.OriginalMediaPath == packed.PackagePath &&
                extracted.Source.SourceId == packed.SourceId && !SteamSourceIdentity.IsSteamManagedOriginal(extracted.Path, extracted.Source),
                "Extracted provenance records the actual package while copied material remains an ordinary deletable file");
            await Reject<IOException>(() => catalog.PrepareAssetAsync(packed, Path.Combine(scene, "extracted")), check,
                "Extraction refuses destinations inside the original project");
            string occupied = Path.Combine(root, "Occupied"); Directory.CreateDirectory(occupied);
            string conflict = Path.Combine(occupied, Path.GetFileName(extracted.Path)); File.WriteAllBytes(conflict, [0]);
            await Reject<IOException>(() => catalog.PrepareAssetAsync(packed, occupied), check,
                "Extraction never overwrites a different existing file using the expected hash name");
            check(File.ReadAllBytes(conflict).SequenceEqual(new byte[] { 0 }) && Directory.GetFiles(occupied).Length == 1,
                "Failed extraction preserves the conflicting file and removes its own partial file");
            var saved = JsonSerializer.Deserialize<SteamWallpaperSource>(JsonSerializer.Serialize(extracted.Source));
            check(saved == extracted.Source, "Per-material package provenance round-trips through saved metadata");

            string web = Project(root, "Web", "web");
            Directory.CreateDirectory(Path.Combine(web, "images"));
            File.WriteAllBytes(Path.Combine(web, "images", "background.jpg"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(web, "index.html"), "<script>fetch('https://invalid')</script>");
            var webAssets = await catalog.InspectAssetsAsync((await catalog.DiscoverAsync([web])).Projects.Single());
            check(webAssets.Assets.Single().RelativePath == "images/background.jpg", "Web projects expose local material without loading HTML or JavaScript");
            string image = Project(root, "Image", "image", "image.png", "image.png");
            File.WriteAllBytes(Path.Combine(image, "image.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(image, "attachment.jpg"), [4, 5, 6]);
            var imageProject = (await catalog.DiscoverAsync([image])).Projects.Single();
            var main = (await catalog.InspectAssetsAsync(imageProject)).Assets.Single();
            check(main.SourceId == imageProject.SourceId && (await catalog.PrepareAssetAsync(main)).Path == imageProject.MediaPath,
                "Legacy image projects still import only their main file, including a main file also used as preview");
            string oversizedPreview = Path.Combine(web, "preview.png");
            using (var huge = File.Create(oversizedPreview)) huge.SetLength(16 * 1024 * 1024 + 1);
            check((await catalog.InspectAssetsAsync((await catalog.DiscoverAsync([web])).Projects.Single())).Assets.Length == 1,
                "An oversized project preview stays excluded even when it cannot be used for the project thumbnail");
            string outside = Path.Combine(root, "outside.png"); File.WriteAllBytes(outside, [1]);
            await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(loose with { MediaPath = outside }), check,
                "Forged asset records cannot reference files outside the project");
            await Reject<InvalidDataException>(() => catalog.PrepareAssetAsync(loose with { MediaPath = Path.Combine(scene, "index.js") }), check,
                "Forged asset records cannot import a script as media");

            File.AppendAllText(loose.MediaPath!, "changed");
            await Reject<IOException>(() => catalog.PrepareAssetAsync(loose), check, "Loose material changed since inspection is rejected");
            File.AppendAllText(packed.PackagePath!, "changed");
            await Reject<IOException>(() => catalog.PrepareAssetAsync(packed, copies), check, "Packed material changed since inspection is rejected");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Reject<OperationCanceledException>(() => catalog.InspectAssetsAsync(project, cancellation.Token), check, "Cancelled material inspection stops before scanning");
            string noOutput = Path.Combine(root, "Cancelled");
            await Reject<OperationCanceledException>(() => catalog.PrepareAssetAsync(packed, noOutput, cancellation.Token), check, "Cancelled extraction does not create a destination");
            check(!Directory.Exists(noOutput), "A cancelled extraction leaves no output directory");
            await TestPackages(root, catalog, check);
            await TestTextures(root, catalog, check);
            await TestLinks(root, catalog, check);
            await TestActiveCancel(root, catalog, check);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task TestPackages(string root, SteamWallpaperCatalog catalog, Action<bool, string> check)
    {
        string projectPath = Project(root, "Invalid packages", "scene");
        File.WriteAllBytes(Path.Combine(projectPath, "keep.png"), [1]);
        var valid = Package(("image.png", new byte[] { 1, 2, 3 }));
        File.WriteAllBytes(Path.Combine(projectPath, "path.pkg"), Package(("../outside.png", new byte[] { 1 })));
        File.WriteAllBytes(Path.Combine(projectPath, "absolute.pkg"), Package(("C:/outside.png", new byte[] { 1 })));
        File.WriteAllBytes(Path.Combine(projectPath, "duplicate.pkg"), Package(("a.png", new byte[] { 1 }), ("A.png", new byte[] { 2 })));
        File.WriteAllBytes(Path.Combine(projectPath, "short.pkg"), valid[..^1]);
        File.WriteAllBytes(Path.Combine(projectPath, "overlap.pkg"), Package(("a.png", new byte[] { 1, 2 }), ("b.png", new byte[] { 2, 3 }), overlap: true));
        File.WriteAllBytes(Path.Combine(projectPath, "huge-count.pkg"), PackageCount(int.MaxValue));
        var overflow = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(overflow.AsSpan(16 + 4 + Encoding.UTF8.GetByteCount("image.png")), uint.MaxValue);
        File.WriteAllBytes(Path.Combine(projectPath, "offset.pkg"), overflow);
        var project = (await catalog.DiscoverAsync([projectPath])).Projects.Single();
        var result = await catalog.InspectAssetsAsync(project);
        check(result.Assets.Single().RelativePath == "keep.png" && result.Issues.Length == 7,
            "Traversal, absolute/duplicate paths, truncation, overlap, huge counts and out-of-range offsets isolate corrupt packages");
        check(!File.Exists(Path.Combine(root, "outside.png")) || File.ReadAllBytes(Path.Combine(root, "outside.png")).SequenceEqual(new byte[] { 1 }),
            "Malformed package inspection never writes package paths outside the project");
    }

    private static async Task TestLinks(string root, SteamWallpaperCatalog catalog, Action<bool, string> check)
    {
        string projectPath = Project(root, "Linked materials", "scene");
        string target = Path.Combine(root, "Outside linked"); Directory.CreateDirectory(target);
        File.WriteAllBytes(Path.Combine(target, "outside.png"), [1]);
        string link = Path.Combine(projectPath, "linked");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { Console.WriteLine("SKIP: Material link fixture unavailable: " + error.GetType().Name); return; }
        try
        {
            var project = (await catalog.DiscoverAsync([projectPath])).Projects.Single();
            var result = await catalog.InspectAssetsAsync(project);
            check(result.Assets.Length == 0 && result.Issues.Length == 1, "Material recursion rejects linked directories instead of scanning outside the project");
        }
        finally { Directory.Delete(link); }
    }

    private static async Task TestActiveCancel(string root, SteamWallpaperCatalog catalog, Action<bool, string> check)
    {
        string directory = Project(root, "Cancellation", "scene");
        string packagePath = Path.Combine(directory, "scene.pkg");
        byte[] index = Package(("large.mp4", Array.Empty<byte>()));
        BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(index.Length - 4), 256 * 1024 * 1024);
        File.WriteAllBytes(packagePath, index);
        using (var stream = File.OpenWrite(packagePath)) stream.SetLength(index.Length + 256L * 1024 * 1024);
        var project = (await catalog.DiscoverAsync([directory])).Projects.Single();
        var asset = (await catalog.InspectAssetsAsync(project)).Assets.Single();
        string target = Path.Combine(root, "Cancel active package");
        using var cancellation = new CancellationTokenSource();
        var extraction = catalog.PrepareAssetAsync(asset, target, cancellation.Token);
        while (!extraction.IsCompleted)
        {
            if (Directory.Exists(target) && Directory.GetFiles(target, "*.partial").Length > 0) { cancellation.Cancel(); break; }
            await Task.Delay(1);
        }
        bool cancelled = false; try { await extraction; } catch (OperationCanceledException) { cancelled = true; }
        check(cancelled && Directory.GetFiles(target).Length == 0 && new FileInfo(packagePath).Length == index.Length + 256L * 1024 * 1024,
            "Cancellation during a streamed package extraction removes partial output and preserves the package");
    }

    private static async Task Reject<T>(Func<Task> action, Action<bool, string> check, string label) where T : Exception
    {
        bool rejected = false; try { await action(); } catch (T) { rejected = true; }
        check(rejected, label);
    }
    private static string Project(string root, string name, string type, string file = "scene.pkg", string preview = "preview.png")
    {
        string path = Path.Combine(root, name); Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "project.json"), JsonSerializer.Serialize(new { title = name, type, file, preview }));
        return path;
    }
    private static byte[] Package(params (string Name, byte[] Data)[] entries) => Package(entries, false);
    private static byte[] Package((string Name, byte[] Data) first, (string Name, byte[] Data) second, bool overlap) => Package([first, second], overlap);
    private static byte[] Package((string Name, byte[] Data)[] entries, bool overlap)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        byte[] magic = Encoding.UTF8.GetBytes("PKGV0005"); writer.Write(magic.Length); writer.Write(magic); writer.Write(entries.Length);
        uint offset = 0;
        foreach (var entry in entries)
        {
            byte[] name = Encoding.UTF8.GetBytes(entry.Name); writer.Write(name.Length); writer.Write(name);
            writer.Write(overlap ? 0 : offset); writer.Write((uint)entry.Data.Length); offset += (uint)entry.Data.Length;
        }
        foreach (var entry in entries) writer.Write(entry.Data);
        return stream.ToArray();
    }
    private static byte[] PackageCount(int count)
    {
        byte[] data = Package(); BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12), count); return data;
    }
}
