using LumaWall.Storage;

internal static class SettingsSnapshotChecks
{
    internal static void Run(string root,Action<bool,string> check)
    {
        using var store=new LibraryStore(Path.Combine(root,"settings-snapshots"));
        store.SetSetting("poster.v4:first","before");
        store.SetSetting("poster.v4:second","second");
        store.SetSetting("poster.v40:other","other");
        store.SetSetting("Poster.v4:case","case");
        store.SetSetting("media.steam.v1:asset","source");
        var snapshot=store.GetSettingsByPrefix("poster.v4:");
        check(snapshot.Count==2&&snapshot["poster.v4:first"]=="before"&&snapshot["poster.v4:second"]=="second",
            "Bulk settings reads match the literal case-sensitive prefix without other settings");
        store.SetSetting("poster.v4:first","after");
        store.SetSetting("poster.v4:third","third");
        check(snapshot.Count==2&&snapshot["poster.v4:first"]=="before"&&store.GetSettingsByPrefix("poster.v4:").Count==3,
            "A settings snapshot remains stable while subsequent settings are edited or added");

        const string special="literal%_'[";
        store.SetSetting(special+"item","match");
        store.SetSetting("literal-other","excluded");
        var literal=store.GetSettingsByPrefix(special);
        check(literal.Count==1&&literal[special+"item"]=="match",
            "SQL wildcard and quote characters in a settings prefix remain literal");
        check(store.GetSettingsByPrefix("missing:").Count==0,
            "An absent settings prefix returns an empty snapshot");

        store.SetSetting("sharing.undo.v1","""{"Profile":{"Effects":{"Masks":[{"Image":"isolated-image-data"}]}}}""");
        var backup=store.GetSettingsByPrefix("sharing.undo.");
        check(backup["sharing.undo.v1"]==store.GetSetting("sharing.undo.v1")&&
            backup["sharing.undo.v1"].Contains("isolated-image-data",StringComparison.Ordinal),
            "Bulk settings reads preserve resolved sharing-backup resources");
        // Poster metadata without a media row is intentionally pruned on open.
        store.SetSetting("snapshot.persisted:first","reopened");
        using var reopened=new LibraryStore(store.Root);
        check(reopened.GetSettingsByPrefix("snapshot.persisted:")["snapshot.persisted:first"]=="reopened",
            "Bulk settings snapshots read persisted values after reopening the database");
    }
}
