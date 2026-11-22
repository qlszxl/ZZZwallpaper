using LumaWall.Contracts;
using LumaWall.Contracts.Application;

namespace LumaWall.Application;

/// <summary>Material catalog operations shared by UI, import and automation adapters.</summary>
public sealed class LibraryService(ILibraryStore store)
{
    public List<MediaAsset> Assets() => store.Assets();
    public List<MediaFolder> Folders() => store.Folders();
    public List<MediaAsset> FolderAssets(string folderId) => store.FolderAssets(folderId);
    public MediaAsset? FindAssetByPath(string path) => store.FindAssetByPath(path);
    public void Upsert(MediaAsset asset) => store.Upsert(asset);
    public void Remove(string assetId) => store.Remove(assetId);
    public MediaFolder SaveFolder(MediaFolder folder) => store.SaveFolder(folder);
    public void ReplaceFolderAssets(string folderId, IEnumerable<string> assetIds) => store.ReplaceFolderAssets(folderId, assetIds);
    public void RemoveFolder(string folderId) => store.RemoveFolder(folderId);
    public string Poster(string id) => store.Poster(id);
    public bool? AssetHasAudio(MediaAsset asset) => store.AssetHasAudio(asset);
    public void RememberAssetAudio(MediaAsset asset, bool? hasAudio) => store.RememberAssetAudio(asset, hasAudio);

    public List<MediaAsset> ResolveQueue(PlaybackSettings settings)
    {
        if (settings.FolderId is { } folder)
            return store.FolderAssets(folder)
                .OrderBy(asset => asset.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(asset => asset.Path, StringComparer.OrdinalIgnoreCase).ToList();
        var assets = store.Assets().ToDictionary(asset => asset.Id);
        return settings.AssetIds.Where(assets.ContainsKey).Select(id => assets[id]).ToList();
    }
}
