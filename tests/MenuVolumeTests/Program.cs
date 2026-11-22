using LumaWall.DesktopIntegration;

int checks=0;
void Check(bool result,string message) {if(!result)throw new Exception(message);checks++;Console.WriteLine("PASS: "+message);}

var drag=new MenuVolumeSession(new("screen-2",35,true));
var previews=new List<DesktopMenuVolume>();
var commits=new List<DesktopMenuVolume>();
for(int value=36;value<=100;value++) {
    drag.SetVolume(value);
    if(drag.TakePreview((value-36)*5) is { } preview)previews.Add(preview);
}
Check(previews.Count==5,"65 pointer moves produce five live previews over 320 ms");
Check(commits.Count==0,"Dragging never requests persistence");
if(drag.TakePreview(321,true) is { } lastPreview)previews.Add(lastPreview);
if(drag.Commit() is { } commit)commits.Add(commit);
Check(previews[^1].DisplayedVolume==100&&commits is [{DisplayedVolume:100}],"Release flushes the exact final value and commits once");
Check(drag.Commit()==null&&drag.TakePreview(400,true)==null,"Closing after release does not duplicate previews or writes");
Check(previews.All(item=>item.ScreenId=="screen-2")&&commits.All(item=>item.ScreenId=="screen-2"),"All previews and commits remain bound to the invocation screen");

var mute=new MenuVolumeSession(new("screen-1",64,true));
mute.ToggleMute();
Check(mute.Current.DisplayedVolume==0&&mute.Current.Volume==64,"Mute retains the audible volume");
Check(mute.Commit() is {SoundEnabled:false,Volume:64},"Mute commits its retained volume and muted state");
mute.ToggleMute();
Check(mute.Current.DisplayedVolume==64,"Unmute restores the retained level");
mute.SetVolume(77);mute.SetVolume(0);
Check(mute.Commit() is {SoundEnabled:false,Volume:77},"Dragging through zero preserves the last audible level for later unmute");
mute.ToggleMute();Check(mute.Current.DisplayedVolume==77,"Unmute after a zero drag restores that last audible level");

var initiallyMuted=new MenuVolumeSession(new("other-screen",81,false));
Check(initiallyMuted.Current.DisplayedVolume==0,"A muted screen opens with a zero-position slider");
initiallyMuted.SetVolume(5);
Check(initiallyMuted.Commit() is {SoundEnabled:true,Volume:5},"Raising a muted slider enables sound at the requested level");

var cancelled=new MenuVolumeSession(new("screen-3",25,true));
cancelled.SetVolume(80);Check(cancelled.TakePreview(0) is {Volume:80},"First change previews immediately");
cancelled.SetVolume(26);Check(cancelled.TakePreview(30)==null,"Rapid changes wait inside the preview interval");
Check(cancelled.TakePreview(30,true) is {Volume:26}&&cancelled.Commit() is {Volume:26},"Dismissal flushes an outstanding drag immediately");

var reverted=new MenuVolumeSession(new("screen-4",50,true));
reverted.SetVolume(90);reverted.TakePreview(0);reverted.SetVolume(50);
Check(reverted.TakePreview(1,true) is {Volume:50}&&reverted.Commit()==null,"Returning to the opening value restores live sound without a redundant write");
Check(!reverted.SetVolume(50)&&!reverted.PreviewPending,"Repeated identical pointer positions do not schedule work");
reverted.SetVolume(-10);Check(reverted.Current.DisplayedVolume==0,"Negative keyboard steps clamp to mute");
reverted.SetVolume(500);Check(reverted.Current.DisplayedVolume==100,"High keyboard steps clamp to maximum");
Check(MenuVolumeSession.VolumeAt(-20,23,194)==0&&MenuVolumeSession.VolumeAt(400,23,194)==100,"Captured drags outside the track clamp to its endpoints");
Check(MenuVolumeSession.VolumeAt(120,23,194)==50,"Track midpoint maps to 50 percent at scaled pixel widths");
Check(MenuVolumeSession.VolumeAt(10,10,0)==0,"Degenerate track width is safe");
Check(new MenuVolumeSession(new("screen-5",0,false)).Current.Volume==1,"Malformed retained volume is normalized");
bool rejected=false;
try {new MenuVolumeSession(new("",80,true));}catch(ArgumentException) {rejected=true;}
Check(rejected,"An unknown screen cannot receive a volume gesture");
Console.WriteLine($"Menu volume checks passed: {checks}");
