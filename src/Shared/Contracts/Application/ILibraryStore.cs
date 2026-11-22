namespace LumaWall.Contracts.Application;

public interface ILibraryStore
{
    List<MediaAsset> Assets();
    MediaAsset? FindAssetByPath(string path);
    void Upsert(MediaAsset asset);
    void Remove(string assetId);
    List<MediaFolder> Folders();
    List<MediaAsset> FolderAssets(string folderId);
    MediaFolder SaveFolder(MediaFolder folder);
    void ReplaceFolderAssets(string folderId, IEnumerable<string> assetIds);
    void RemoveFolder(string folderId);
    string Poster(string id);
    bool? AssetHasAudio(MediaAsset asset);
    void RememberAssetAudio(MediaAsset asset, bool? hasAudio);
}
