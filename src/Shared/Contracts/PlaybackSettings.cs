namespace LumaWall.Contracts;

// Keep existing persisted numeric values stable; UI ordering is independent.
public enum PlaybackMode { SingleLoop=0, SingleOnce=1, Sequential=2, Shuffle=3, Automatic=4 }
public enum PlaybackTrigger { EndOfVideo, Interval }
public enum PlaybackTransition { Cut, CrossFade }

public sealed record PlaybackPlaylist
{
    public string Id {get;init;}="";
    public string Name {get;init;}="自定义播放列表";
    public List<string> AssetIds {get;init;}=new();
}

public sealed record PlaybackSettings
{
    public int Version {get;init;}=2;
    public PlaybackMode Mode {get;init;}=PlaybackMode.Automatic;
    public PlaybackTrigger Trigger {get;init;}=PlaybackTrigger.EndOfVideo;
    public PlaybackTransition Transition {get;init;}=PlaybackTransition.Cut;
    public int IntervalSeconds {get;init;}=300;
    public List<string> AssetIds {get;init;}=new();
    public string? FolderId {get;init;}
    // Null denotes a legacy single queue; an empty list is an intentional deletion.
    public List<PlaybackPlaylist>? CustomPlaylists {get;init;}
    public string? PlaylistId {get;init;}
    public PlaybackSettings Validate()
    {
        static List<string> NormalizeAssets(IEnumerable<string>? ids)=>(ids??[])
            .Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        var assetIds=NormalizeAssets(AssetIds);
        string? folderId=string.IsNullOrWhiteSpace(FolderId)?null:FolderId;
        string? playlistId=string.IsNullOrWhiteSpace(PlaylistId)?null:PlaylistId.Trim();
        var playlists=new List<PlaybackPlaylist>();
        if(CustomPlaylists==null)
        {
            playlists.Add(new() {Id="default",Name="自定义播放列表",AssetIds=assetIds.ToList()});
            playlistId="default";
        }
        else
        {
            var usedIds=new HashSet<string>(StringComparer.Ordinal);
            foreach(var playlist in CustomPlaylists)
            {
                if(playlist==null)continue;
                string id=string.IsNullOrWhiteSpace(playlist.Id)?$"playlist-{playlists.Count+1}":playlist.Id.Trim();
                string uniqueId=id;int suffix=2;
                while(!usedIds.Add(uniqueId))uniqueId=id+"-"+suffix++;
                playlists.Add(playlist with {Id=uniqueId,Name=string.IsNullOrWhiteSpace(playlist.Name)?"自定义播放列表":playlist.Name.Trim(),AssetIds=NormalizeAssets(playlist.AssetIds)});
            }
        }
        // Existing queue editors still write AssetIds. Preserve their ability to
        // create a queue after the user has removed every named playlist.
        if(playlists.Count==0&&assetIds.Count>0)
        {
            playlists.Add(new() {Id="default",Name="自定义播放列表",AssetIds=assetIds.ToList()});
            playlistId="default";
        }
        int selected=playlists.FindIndex(playlist=>playlist.Id==playlistId);
        if(selected<0)
        {
            playlistId=playlists.FirstOrDefault()?.Id;
            assetIds=playlists.FirstOrDefault()?.AssetIds.ToList()??[];
        }
        else if(folderId!=null)
        {
            // Folder mode owns its effective queue; an old folder caller may
            // clear AssetIds, but must not erase the remembered custom list.
            assetIds=playlists[selected].AssetIds.ToList();
        }
        else playlists[selected]=playlists[selected] with {AssetIds=assetIds.ToList()};
        return this with {
            Version=2,
            Mode=Enum.IsDefined(Mode)?Mode:PlaybackMode.Automatic,
            Trigger=Enum.IsDefined(Trigger)?Trigger:PlaybackTrigger.EndOfVideo,
            Transition=Enum.IsDefined(Transition)?Transition:PlaybackTransition.Cut,
            IntervalSeconds=Math.Clamp(IntervalSeconds,10,86400),
            AssetIds=assetIds,
            FolderId=folderId,
            CustomPlaylists=playlists,
            PlaylistId=playlistId
        };
    }
    public PlaybackMode ResolveMode(int queueCount)=>Mode==PlaybackMode.Automatic
        ?(queueCount>1?PlaybackMode.Sequential:PlaybackMode.SingleLoop):Mode;
}

public sealed record MediaFolder(string Id,string Path,bool IncludeSubfolders);
