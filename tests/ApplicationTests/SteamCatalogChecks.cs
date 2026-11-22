using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LumaWall.Application.Steam;
using LumaWall.Contracts.Steam;

static class SteamCatalogChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "ZZZwallpaper-steam-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var catalog = new SteamWallpaperCatalog();
        string steam = Path.Combine(root, "Steam"), second = Path.Combine(root, "Second library");
        string collection = Path.Combine(steam, "steamapps", "workshop", "content", "431960");
        string secondCollection = Path.Combine(second, "steamapps", "workshop", "content", "431960");
        string copies = Path.Combine(root, "Copies");
        try
        {
            string video = Project(collection, "101", "video", "main.mp4", "First video");
            string image = Project(collection, "102", "image", "main.png", "Still image");
            string scene = Project(collection, "103", "scene", "scene.pkg", "Scene");
            string web = Project(collection, "104", "web", "index.html", "Web");
            string other = Project(secondCollection, "201", "video", "nested/main.webm", "Second library");
            string preview = Path.Combine(video, "preview.jpg");
            File.WriteAllBytes(preview, [9, 8, 7]);
            WriteProject(video, "video", "main.mp4", "First video", "preview.jpg");
            File.WriteAllBytes(Path.Combine(video, "unrelated.mp4"), [6, 7, 8]);
            Directory.CreateDirectory(Path.Combine(video, "textures"));
            File.WriteAllBytes(Path.Combine(video, "textures", "texture.png"), [6, 7, 8]);
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "\"libraryfolders\" { \"0\" { \"path\" \"" + Escape(steam) + "\" } \"1\" { \"path\" \"" + Escape(second) + "\" \"apps\" { \"431960\" \"1\" } } }");
            var found = await catalog.DiscoverAsync([steam, Path.Combine(steam, "steamapps"), collection, video]);
            check(found.Projects.Length == 5 && found.Issues.Length == 0, "Steam discovers multiple libraries and deduplicates explicit roots without scanning project attachments");
            check(found.Projects.Count(p => p.Status == SteamProjectStatus.Supported) == 3 &&
                found.Projects.Count(p => p.Status == SteamProjectStatus.Unsupported) == 2, "Steam recognizes video/image and visibly classifies scene/web as unsupported");
            var selected = found.Projects.Single(p => p.ProjectDirectory == video);
            check(selected.MediaPath == Path.Combine(video, "main.mp4") && selected.PreviewPath == preview &&
                selected.WorkshopId == "101" && selected.SourceId.Length == 64, "Steam resolves only the declared main file and validates a separate project preview");
            var explicitProject = await catalog.DiscoverAsync([other]);
            check(explicitProject.Projects.Single().MediaPath == Path.Combine(other, "nested", "main.webm"), "An explicitly chosen project supports a safe nested relative media path");
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"),
                "\uFEFF\"LibraryFolders\"\n{ // old Steam format\n \"0\" \"" + Escape(steam) + "\"\n \"1\" \"" + Escape(second) + "\"\n}");
            check((await catalog.DiscoverAsync([steam])).Projects.Length == 5, "Legacy Steam numeric library paths, BOM and comments are parsed");

            var referenced = await catalog.PrepareAsync(selected);
            check(referenced.Path == selected.MediaPath && !referenced.Copied && !Directory.Exists(copies) &&
                referenced.Source.OriginalMediaPath == selected.MediaPath, "Reference mode creates no media copy and reports the original provenance");
            check(SteamSourceIdentity.IsSteamManagedOriginal(referenced.Path, referenced.Source) &&
                SteamSourceIdentity.SettingKey("asset") == "media.steam.v1:asset", "A reference exposes the shared provenance key and original-file protection");
            var copied = await catalog.PrepareAsync(selected, copies);
            var repeated = await catalog.PrepareAsync(selected, copies);
            check(copied.Copied && !copied.Reused && repeated.Reused && copied.Path == repeated.Path &&
                File.ReadAllBytes(copied.Path).SequenceEqual(File.ReadAllBytes(selected.MediaPath!)) &&
                Directory.GetFiles(copies).Length == 1, "Copies are byte-identical and repeated imports reuse a verified content-addressed file");
            check(!SteamSourceIdentity.IsSteamManagedOriginal(copied.Path, copied.Source) &&
                SteamSourceIdentity.IsSteamManagedOriginal(selected.MediaPath!, copied.Source), "Copied ordinary media remains deletable while actual Steam-managed paths remain protected");
            var provenance = JsonSerializer.Deserialize<SteamWallpaperSource>(JsonSerializer.Serialize(copied.Source));
            check(provenance == copied.Source, "Steam source metadata round-trips independently of UI or storage");

            File.WriteAllBytes(selected.MediaPath!, [8, 7, 6, 5, 4]);
            await Reject<IOException>(() => catalog.PrepareAsync(selected), "A selected project changed after scanning is rejected before import", check);
            selected = (await catalog.DiscoverAsync([video])).Projects.Single();
            var update = await catalog.PrepareAsync(selected, copies);
            check(update.Path != copied.Path && File.Exists(copied.Path) && Directory.GetFiles(copies).Length == 2,
                "Updated source content produces a new copy without overwriting an earlier imported file");
            string occupiedRoot = Path.Combine(root, "Occupied");
            Directory.CreateDirectory(occupiedRoot);
            string occupied = Path.Combine(occupiedRoot, Path.GetFileName(update.Path));
            File.WriteAllBytes(occupied, [1]);
            await Reject<IOException>(() => catalog.PrepareAsync(selected, occupiedRoot), "A different existing file is never overwritten even when it uses the expected hash filename", check);
            check(File.ReadAllBytes(occupied).SequenceEqual(new byte[] { 1 }) && Directory.GetFiles(occupiedRoot).Length == 1,
                "A failed copy preserves the existing destination and removes only its own temporary file");
            await Reject<IOException>(() => catalog.PrepareAsync(selected, Path.Combine(video, "copy")), "Copy mode rejects destinations inside the source project", check);
            await Reject<IOException>(() => catalog.PrepareAsync(selected, Path.Combine(collection, "999")), "Copy mode rejects a different Steam-managed project destination", check);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Reject<OperationCanceledException>(() => catalog.DiscoverAsync([steam], cancelled.Token), "Cancelled discovery does not scan sources", check);
            string neverMade = Path.Combine(root, "Cancelled copy");
            await Reject<OperationCanceledException>(() => catalog.PrepareAsync(selected, neverMade, cancelled.Token), "Cancelled preparation never creates a destination directory", check);
            check(!Directory.Exists(neverMade), "Cancellation before preparation leaves no copied files");

            string malformed = Path.Combine(collection, "301"); Directory.CreateDirectory(malformed);
            File.WriteAllText(Path.Combine(malformed, "project.json"), "{broken");
            string traversal = Project(collection, "302", "video", "main.mp4", "Traversal");
            WriteProject(traversal, "video", "../101/main.mp4", "Traversal");
            string absolute = Project(collection, "303", "video", "main.mp4", "Absolute");
            WriteProject(absolute, "video", selected.MediaPath!, "Absolute");
            string missing = Project(collection, "304", "video", "main.mp4", "Missing");
            File.Delete(Path.Combine(missing, "main.mp4"));
            string mismatched = Project(collection, "305", "image", "main.mp4", "Mismatch");
            string duplicate = Project(collection, "306", "video", "main.mp4", "Duplicate");
            File.WriteAllText(Path.Combine(duplicate, "project.json"), "{\"type\":\"video\",\"file\":\"main.mp4\",\"FILE\":\"other.mp4\"}");
            string oversized = Project(collection, "307", "video", "main.mp4", "Oversized");
            File.WriteAllText(Path.Combine(oversized, "project.json"), new string(' ', 1024 * 1024 + 1));
            string unsafePreview = Project(collection, "308", "video", "main.mp4", "Bad preview");
            WriteProject(unsafePreview, "video", "main.mp4", "Bad preview", "../101/preview.jpg");
            found = await catalog.DiscoverAsync([collection]);
            check(found.Projects.Count(p => p.Status == SteamProjectStatus.Invalid) == 7 &&
                found.Projects.Single(p => p.ProjectDirectory == video).Status == SteamProjectStatus.Supported,
                "Malformed, missing, oversized, duplicate, type-mismatched and escaping project files are isolated from valid projects");
            check(found.Projects.Single(p => p.ProjectDirectory == unsafePreview) is { Status: SteamProjectStatus.Supported, PreviewPath: null },
                "An unsafe optional preview is ignored without turning it into an import candidate");
            string largePreview = Path.Combine(unsafePreview, "large.png");
            using (var file = File.Create(largePreview)) file.SetLength(16 * 1024 * 1024 + 1);
            WriteProject(unsafePreview, "video", "main.mp4", "Large preview", "large.png");
            check((await catalog.DiscoverAsync([unsafePreview])).Projects.Single().PreviewPath == null, "Oversized project preview files are omitted");
            await Reject<InvalidDataException>(() => catalog.PrepareAsync(found.Projects.Single(p => p.ProjectDirectory == scene)),
                "Unsupported scene projects cannot be prepared by bypassing the UI selection", check);
            File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" {");
            found = await catalog.DiscoverAsync([steam, other]);
            check(found.Issues.Length == 1 && found.Projects.Any(p => p.ProjectDirectory == other),
                "A broken library list is reported without preventing explicit or already discovered project roots");
            check((await catalog.DiscoverAsync(["relative", video])).Issues.Length == 1, "Invalid roots are isolated while valid explicit roots remain usable");

            string local = Project(Path.Combine(root, "wallpaper_engine", "projects", "myprojects"), "custom", "image", "local.png", "Local project");
            check((await catalog.DiscoverAsync([Path.Combine(root, "wallpaper_engine")])).Projects.Single().ProjectDirectory == local,
                "Wallpaper Engine installation roots discover immediate local project folders");
            string custom = Project(Path.Combine(root, "Custom"), "mine", "image", "local.png", "Manual project");
            var customPrepared = await catalog.PrepareAsync((await catalog.DiscoverAsync([custom])).Projects.Single());
            check(!SteamSourceIdentity.IsManagedPath(customPrepared.Path) && SteamSourceIdentity.IsSteamManagedOriginal(customPrepared.Path, customPrepared.Source),
                "An explicitly imported original project remains protected by its matching provenance outside standard Steam paths");
            check(!SteamSourceIdentity.IsManagedPath(Path.Combine(root, "Steam wallpaper", "main.mp4")) &&
                !SteamSourceIdentity.IsSteamManagedOriginal(copied.Path, referenced.Source), "Steam-like names and unrelated stale provenance do not classify an ordinary file as managed");

            await CheckLinks(root, catalog, video, check);
            await CheckCancellationDuringCopy(root, catalog, check);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CheckLinks(string root, SteamWallpaperCatalog catalog, string video, Action<bool, string> check)
    {
        string links = Path.Combine(root, "Links", "431960"); Directory.CreateDirectory(links);
        string link = Path.Combine(links, "401");
        try { Directory.CreateSymbolicLink(link, video); }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { Console.WriteLine("SKIP: Symbolic-link fixture is unavailable: " + error.GetType().Name); return; }
        try
        {
            var result = await catalog.DiscoverAsync([links, link]);
            check(result.Projects.Length == 0 && result.Issues.Length > 0, "Linked project folders are rejected in both discovery and explicit-root modes");
            string project = Project(Path.Combine(root, "LinkedMedia"), "402", "video", "main.mp4", "Linked media");
            File.Delete(Path.Combine(project, "main.mp4"));
            File.CreateSymbolicLink(Path.Combine(project, "main.mp4"), Path.Combine(video, "main.mp4"));
            check((await catalog.DiscoverAsync([project])).Projects.Single().Status == SteamProjectStatus.Invalid,
                "A linked main file cannot escape the project through an otherwise valid relative path");
            File.Delete(Path.Combine(project, "main.mp4"));
        }
        finally { Directory.Delete(link); }
    }

    private static async Task CheckCancellationDuringCopy(string root, SteamWallpaperCatalog catalog, Action<bool, string> check)
    {
        string project = Project(Path.Combine(root, "Large"), "501", "video", "main.mp4", "Cancellation");
        using (var large = File.OpenWrite(Path.Combine(project, "main.mp4"))) large.SetLength(256L * 1024 * 1024);
        var selected = (await catalog.DiscoverAsync([project])).Projects.Single();
        string target = Path.Combine(root, "Cancel active");
        using var cancel = new CancellationTokenSource();
        var preparing = catalog.PrepareAsync(selected, target, cancel.Token);
        while (!preparing.IsCompleted)
        {
            if (Directory.Exists(target) && Directory.GetFiles(target, "*.partial").Length > 0) { cancel.Cancel(); break; }
            await Task.Delay(1);
        }
        bool interrupted = false;
        try { await preparing; } catch (OperationCanceledException) { interrupted = true; }
        check(interrupted && Directory.GetFiles(target).Length == 0 && File.Exists(selected.MediaPath),
            "Cancellation during an active streamed copy removes the partial copy and retains the original source");
    }

    private static async Task Reject<T>(Func<Task> action, string label, Action<bool, string> check) where T : Exception
    {
        bool rejected = false; try { await action(); } catch (T) { rejected = true; }
        check(rejected, label);
    }
    private static string Escape(string path) => path.Replace("\\", "\\\\").Replace("\"", "\\\"");
    private static string Project(string parent, string name, string type, string file, string title)
    {
        string directory = Path.Combine(parent, name); Directory.CreateDirectory(directory);
        string media = Path.Combine(directory, file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(media)!); File.WriteAllBytes(media, [1, 2, 3, 4]);
        WriteProject(directory, type, file, title);
        return directory;
    }
    private static void WriteProject(string directory, string type, string file, string title, string? preview = null) =>
        File.WriteAllText(Path.Combine(directory, "project.json"), JsonSerializer.Serialize(new { type, file, title, preview = preview ?? "" }), new UTF8Encoding(false));
}
