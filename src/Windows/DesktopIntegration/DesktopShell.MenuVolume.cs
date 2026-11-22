using LumaWall.Contracts;
using System.Drawing.Drawing2D;

namespace LumaWall.DesktopIntegration;

public sealed partial class DesktopShell
{
    private sealed partial class MenuPopup
    {
        private MenuVolumeSession? volumeSession;
        private int volumeIndex=-1;
        private bool draggingVolume;
        private readonly System.Windows.Forms.Timer volumePreviewTimer=new() {Interval=MenuVolumeSession.PreviewIntervalMilliseconds};
        private bool IsVolume(MenuEntry item)=>volumeSession!=null&&item.Id==13;
        private string VolumeAccessibleName=>volumeSession is {Current:var value}
            ?UiLanguage.Format($"此屏幕壁纸音量 {value.DisplayedVolume}%，{(value.SoundEnabled?UiLanguage.Text("按空格静音"):UiLanguage.Text("按空格取消静音"))}，左右键调整音量"):UiLanguage.Text("壁纸音量");

        private void InitializeVolume(DesktopMenuVolume? volume)
        {
            if(volume==null)return;
            volumeSession=new MenuVolumeSession(volume);
            volumeIndex=Array.FindIndex(entries,item=>item.Id==13);
            volumePreviewTimer.Tick+=(_,_)=>FlushVolumePreview();
        }
        private Rectangle VolumeTrack
        {
            get {
                var row=rows[volumeIndex];int inset=owner.MenuPixel(23);
                return new Rectangle(inset,row.Top+owner.MenuPixel(50),Math.Max(1,Width-inset*2),owner.MenuPixel(4));
            }
        }
        private void VolumeChanged()
        {
            Render();AccessibilityNotifyClients(AccessibleEvents.ValueChange,volumeIndex);
            FlushVolumePreview();
        }
        private void FlushVolumePreview(bool force=false)
        {
            if(volumeSession?.TakePreview(lifetime.ElapsedMilliseconds,force) is { } value)owner.VolumePreviewRequested?.Invoke(value);
            if(volumeSession?.PreviewPending==true)volumePreviewTimer.Start();else volumePreviewTimer.Stop();
        }
        private void CommitVolume()
        {
            draggingVolume=false;
            FlushVolumePreview(true);
            volumePreviewTimer.Stop();
            if(volumeSession?.Commit() is { } value)owner.VolumeCommitted?.Invoke(value);
        }
        private void SetVolumeFromPointer(Point point)
        {
            var track=VolumeTrack;
            if(volumeSession!.SetVolume(MenuVolumeSession.VolumeAt(point.X,track.Left,track.Width)))VolumeChanged();
        }
        private void BeginVolumePointer(MouseEventArgs e)
        {
            if(volumeSession==null||pressed!=volumeIndex||e.Button!=MouseButtons.Left)return;
            if(e.Y<rows[volumeIndex].Top+owner.MenuPixel(35))return;
            draggingVolume=true;SetVolumeFromPointer(e.Location);
        }
        private bool MoveVolumePointer(Point point)
        {
            if(!draggingVolume)return false;
            SetVolumeFromPointer(point);return true;
        }
        private bool EndVolumePointer(MouseEventArgs e)
        {
            if(volumeSession==null)return false;
            if(draggingVolume&&e.Button==MouseButtons.Left) {SetVolumeFromPointer(e.Location);CommitVolume();return true;}
            if(pressed!=volumeIndex)return false;
            if(e.Button==MouseButtons.Left&&Hit(e.Location)==volumeIndex&&e.Y<rows[volumeIndex].Top+owner.MenuPixel(35))ToggleVolumeMute();
            return true;
        }
        private void ToggleVolumeMute()
        {
            volumeSession!.ToggleMute();VolumeChanged();CommitVolume();
        }
        private bool ProcessVolumeKey(Keys key)
        {
            if(volumeSession==null||selected!=volumeIndex)return false;
            int value=key switch {
                Keys.Left=>volumeSession.Current.DisplayedVolume-5,
                Keys.Right=>volumeSession.Current.DisplayedVolume+5,
                Keys.Home=>0,
                Keys.End=>100,
                _=>-1
            };
            if(key is not (Keys.Left or Keys.Right or Keys.Home or Keys.End))return false;
            if(volumeSession.SetVolume(value))VolumeChanged();CommitVolume();return true;
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if(volumeSession==null||Hit(e.Location)!=volumeIndex)return;
            SelectRow(volumeIndex);
            if(volumeSession.SetVolume(volumeSession.Current.DisplayedVolume+Math.Sign(e.Delta)*5))VolumeChanged();CommitVolume();
        }
        private void RenderVolume(Graphics graphics,Rectangle bounds,bool selected)
        {
            var value=volumeSession!.Current;
            var row=new RectangleF(owner.MenuPixel(7),bounds.Top+owner.MenuPixel(3),Width-owner.MenuPixel(14),bounds.Height-owner.MenuPixel(6));
            using var shape=MenuRoundedRectangle(row,owner.MenuPixel(10));
            using var fill=new LinearGradientBrush(row,owner.MenuSurface,owner.MenuBackground,LinearGradientMode.Vertical);
            using var rim=new Pen(selected?owner.MenuAccent():Color.FromArgb(40,40,40),owner.menuScale);
            graphics.FillPath(fill,shape);graphics.DrawPath(rim,shape);
            var ink=Color.FromArgb(242,242,242);var accent=owner.MenuAccent();
            DrawVolumeSpeaker(graphics,new RectangleF(owner.MenuPixel(17),bounds.Top+owner.MenuPixel(12),owner.MenuPixel(18),owner.MenuPixel(18)),value.SoundEnabled,ink);
            string label=UiLanguage.Format($"音量 {value.DisplayedVolume}%"),action=value.SoundEnabled?UiLanguage.Text("静音"):UiLanguage.Text("取消静音");
            var labelSize=textRenderer.MeasureInk(label);var actionSize=textRenderer.MeasureInk(action);
            int middle=bounds.Top+owner.MenuPixel(21);
            textPaints.Add((label,new Point(owner.MenuPixel(44),middle-labelSize.Height/2),ink));
            textPaints.Add((action,new Point(Width-owner.MenuPixel(19)-actionSize.Width,middle-actionSize.Height/2),accent));
            var track=VolumeTrack;float center=track.Top+track.Height/2f;
            float knob=track.Left+track.Width*value.DisplayedVolume/100f;
            using var background=new Pen(Color.FromArgb(82,82,82),track.Height) {StartCap=LineCap.Round,EndCap=LineCap.Round};
            using var active=new Pen(accent,track.Height) {StartCap=LineCap.Round,EndCap=LineCap.Round};
            graphics.DrawLine(background,track.Left,center,track.Right,center);
            if(value.DisplayedVolume>0)graphics.DrawLine(active,track.Left,center,knob,center);
            int radius=owner.MenuPixel(draggingVolume?7:6);
            using var thumb=new SolidBrush(ink);graphics.FillEllipse(thumb,knob-radius,center-radius,radius*2,radius*2);
        }
        private static void DrawVolumeSpeaker(Graphics graphics,RectangleF bounds,bool enabled,Color ink)
        {
            var saved=graphics.Save();graphics.TranslateTransform(bounds.X,bounds.Y);graphics.ScaleTransform(bounds.Width/24,bounds.Height/24);
            using var fill=new SolidBrush(ink);
            graphics.FillPolygon(fill,new PointF[]{new(3,9),new(7,9),new(13,4),new(13,20),new(7,15),new(3,15)});
            using var pen=new Pen(ink,2) {StartCap=LineCap.Round,EndCap=LineCap.Round};
            if(enabled) {graphics.DrawArc(pen,10,7,9,10,-65,130);graphics.DrawArc(pen,10,3,14,18,-65,130);}
            else {graphics.DrawLine(pen,17,9,22,15);graphics.DrawLine(pen,22,9,17,15);}
            graphics.Restore(saved);
        }
    }
}
