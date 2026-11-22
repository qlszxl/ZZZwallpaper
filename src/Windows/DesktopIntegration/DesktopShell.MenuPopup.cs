using LumaWall.Contracts;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace LumaWall.DesktopIntegration;

public sealed partial class DesktopShell
{
    // The popup owns its HWND from creation. No #32768 window, native menu
    // bevel, class shadow or owner-draw fallback can contribute extra pixels.
    private sealed partial class MenuPopup : Form
    {
        private readonly DesktopShell owner;
        private readonly MenuEntry[] entries;
        private readonly Rectangle[] rows;
        private readonly MenuRowBuffer buffer;
        private readonly Bitmap surface;
        private readonly NativeMenuText textRenderer;
        private readonly List<(string Text,Point Origin,Color Ink)> textPaints=new();
        private readonly Point anchor;
        private readonly Point popupLocation;
        private readonly System.Windows.Forms.Timer animation=new() {Interval=33};
        private readonly Dictionary<int,int> frameResults=new();
        private readonly System.Diagnostics.Stopwatch lifetime=System.Diagnostics.Stopwatch.StartNew();
        private readonly List<object> lifecycle=new();
        private string? closeReason;
        private int selected=-1,pressed=-1;
        private bool shown,ending,rendering,recorded,resourcesDisposed;
        private nint trackedMenuWindow;
        public int Command {get;private set;}
        public string? RenderError {get;private set;}

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window,int attribute,ref int value,int size);
        [DllImport("user32.dll",EntryPoint="GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint window,int index);

        public MenuPopup(DesktopShell owner,MenuEntry[] entries,Point anchor):this(owner,entries,anchor,null) { }
        public MenuPopup(DesktopShell owner,MenuEntry[] entries,Point anchor,DesktopMenuVolume? volume)
        {
            this.owner=owner;this.entries=entries;this.anchor=anchor;
            InitializeVolume(volume);
            FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;TopMost=true;
            Cursor=Cursors.Arrow;SizeGripStyle=SizeGripStyle.Hide;MaximizeBox=false;MinimizeBox=false;
            AutoScaleMode=AutoScaleMode.None;KeyPreview=true;Text=UiLanguage.Text("ZZZwallpaper 菜单");AccessibleRole=AccessibleRole.MenuPopup;
            rows=new Rectangle[entries.Length];int top=owner.MenuPixel(6);
            for(int i=0;i<entries.Length;i++) {
                if(entries[i].Id==11&&i+2<entries.Length&&entries[i+1].Id==2&&entries[i+2].Id==12) {
                    int margin=owner.MenuPixel(7),width=owner.menuWidth-margin*2,side=width/4,height=owner.MenuPixel(44);
                    rows[i]=new Rectangle(margin,top,side,height);
                    rows[i+1]=new Rectangle(margin+side,top,width-side*2,height);
                    rows[i+2]=new Rectangle(margin+width-side,top,side,height);
                    i+=2;top+=height;continue;
                }
                int rowHeight=entries[i].Separator?owner.MenuPixel(10):IsVolume(entries[i])?owner.MenuPixel(70):owner.menuRowHeight;
                rows[i]=new Rectangle(0,top,owner.menuWidth,rowHeight);top+=rowHeight;
            }
            ClientSize=new Size(owner.menuWidth,top+owner.MenuPixel(6));
            var work=Screen.FromPoint(anchor).WorkingArea;
            popupLocation=MenuPopupPlacement.Locate(anchor,ClientSize,work);
            Location=popupLocation;
            textRenderer=new NativeMenuText(owner.menuFontHandle);
            buffer=new MenuRowBuffer(0,Width,Height);
            try {surface=new Bitmap(Width,Height,Width*4,PixelFormat.Format32bppPArgb,buffer.Bits);}
            catch {buffer.Dispose();throw;}
            animation.Tick+=(_,_)=> {if(selected>=0)Render();};
        }

        protected override CreateParams CreateParams {
            get {
                var value=base.CreateParams;
                value.Style=(value.Style&0x1E000000)|unchecked((int)0x80000000); // Visibility/enabled/clip bits + WS_POPUP; no chrome.
                value.ExStyle=0x00080084; // WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST; no edge styles.
                value.ClassStyle=0; // Our own class, without CS_DROPSHADOW / CS_SAVEBITS.
                return value;
            }
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            trackedMenuWindow=Handle;owner.MenuWindowChanged?.Invoke(trackedMenuWindow,true);
            base.OnHandleCreated(e);
            RecordLifecycle("handle-created");
            void Set(int attribute,int value)=>frameResults[attribute]=DwmSetWindowAttribute(Handle,attribute,ref value,sizeof(int));
            Set(2,1); // Disable DWM nonclient rendering; alpha supplies the contour.
            if(OperatingSystem.IsWindowsVersionAtLeast(10,0,22000)) {Set(33,1);Set(34,unchecked((int)0xFFFFFFFE));}
            Render(); // Submit alpha before showing, avoiding an opaque first frame.
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if(trackedMenuWindow!=0) {owner.MenuWindowChanged?.Invoke(trackedMenuWindow,false);trackedMenuWindow=0;}
            base.OnHandleDestroyed(e);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if(ending)return;
            RecordLifecycle("shown-start");
            if(RenderError!=null) {Cancel("render-error");return;}
            // Opening transitions can deactivate/cancel the owner while the
            // popup acquires activation. Arm popup dismissal only afterwards.
            // WinForms may reposition a modal form during show/DPI processing.
            // Reapply the captured screen-space anchor and promote only this popup.
            Native.SetWindowPos(Handle,-1,popupLocation.X,popupLocation.Y,Width,Height,0x0240); // SHOWWINDOW | NOOWNERZORDER
            Native.SetForegroundWindow(Handle);Activate();Capture=true;
            SetMenuCursor();
            owner.LastMenuActivated=Native.GetForegroundWindow()==Handle;
            shown=true;
            RecordLifecycle("interactive");
            Render();if(RenderError!=null)return;
            owner.MenuBoundsChanged?.Invoke(Bounds);
            if(owner.desktopMenuActive)owner.MenuShown?.Invoke();
            if(owner.menuAnimationsEnabled)animation.Start();
        }
        protected override void OnDeactivate(EventArgs e) {base.OnDeactivate(e);RecordLifecycle("deactivate");if(shown)Cancel("deactivated");}
        protected override void OnFormClosing(FormClosingEventArgs e) {CommitVolume();closeReason??=e.CloseReason.ToString();RecordLifecycle("form-closing",e.CloseReason.ToString());ending=true;animation.Stop();Capture=false;base.OnFormClosing(e);}
        protected override void OnFormClosed(FormClosedEventArgs e) {RecordLifecycle("form-closed",e.CloseReason.ToString());base.OnFormClosed(e);}
        public void Cancel(string reason="explicit") {if(ending||IsDisposed)return;CommitVolume();closeReason=reason;RecordLifecycle("cancel",reason);ending=true;animation.Stop();Capture=false;Close();}
        public bool ContainsScreenPoint(Point point)=>Bounds.Contains(point);
        private void SetMenuCursor()
        {
            if(!ending)System.Windows.Forms.Cursor.Current=Cursors.Arrow;
        }
        protected override void WndProc(ref Message message)
        {
            if(message.Msg==0x84) { // WM_NCHITTEST: this popup has no draggable or resizable frame.
                long position=message.LParam.ToInt64();
                var point=PointToClient(new Point(unchecked((short)position),unchecked((short)(position>>16))));
                message.Result=ClientRectangle.Contains(point)?1:0; // HTCLIENT / HTNOWHERE; preserve signed multi-monitor coordinates.
                return;
            }
            if(message.Msg==0x20) { // WM_SETCURSOR before capture, or after it is released.
                SetMenuCursor();message.Result=1;return;
            }
            if(message.Msg is 0x85 or 0x14) {message.Result=message.Msg==0x14?1:0;return;} // No NC/GDI background painting.
            if(message.Msg==0x1F) {
                RecordLifecycle(shown?"popup-cancel-mode":"opening-cancel-mode-ignored");
                if(shown) {Cancel("popup-cancel-mode");message.Result=0;return;}
            }
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e) { } // UpdateLayeredWindow is the sole presenter.
        protected override void OnPaintBackground(PaintEventArgs e) { }
        private int Hit(Point point)
        {
            for(int i=0;i<rows.Length;i++)if(rows[i].Contains(point)&&entries[i].Enabled&&!entries[i].Separator)return i;
            return -1;
        }
        private void SelectRow(int index)
        {
            if(selected==index)return;selected=index;Render();
            if(index>=0)AccessibilityNotifyClients(AccessibleEvents.Focus,index);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // Captured mouse movement does not send WM_SETCURSOR. Refresh here
            // as well so a resize cursor from the previous window cannot persist.
            SetMenuCursor();if(MoveVolumePointer(e.Location))return;SelectRow(Hit(e.Location));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if(!ClientRectangle.Contains(e.Location)) {owner.OutsideDismissals++;Cancel("outside-mouse-down");return;}
            pressed=Hit(e.Location);SelectRow(pressed);
            BeginVolumePointer(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);if(EndVolumePointer(e)) {pressed=-1;return;}int index=Hit(e.Location);
            if(e.Button is MouseButtons.Left or MouseButtons.Right && index>=0&&index==pressed)InvokeRow(index);
            pressed=-1;
        }
        private void InvokeRow(int index)
        {
            if(ending||index<0||index>=entries.Length||!entries[index].Enabled||entries[index].Separator)return;
            if(IsVolume(entries[index])) {ToggleVolumeMute();return;}
            Command=(int)entries[index].Id;Cancel("command"); // Caller executes only after disposal.
        }
        protected override bool ProcessCmdKey(ref Message message,Keys keyData)
        {
            var key=keyData&Keys.KeyCode;
            if(key is Keys.Escape or Keys.Menu) {Cancel("keyboard-cancel");return true;}
            if(ProcessVolumeKey(key))return true;
            if(key is Keys.Enter or Keys.Space) {InvokeRow(selected);return true;}
            if(key is Keys.Left or Keys.Right && selected>=0&&IsPlaybackControl(entries[selected])) {
                int next=selected+(key==Keys.Left?-1:1);
                while(next>=0&&next<entries.Length&&IsPlaybackControl(entries[next])) {
                    if(entries[next].Enabled) {SelectRow(next);break;}
                    next+=key==Keys.Left?-1:1;
                }
                return true;
            }
            if(key is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.Tab) {
                int direction=key==Keys.Up||key==Keys.End||(key==Keys.Tab&&(keyData&Keys.Shift)!=0)?-1:1;
                int index=key==Keys.Home?-1:key==Keys.End?entries.Length:selected;
                if(index<0&&direction<0)index=0;
                for(int n=0;n<entries.Length;n++) {
                    index=(index+direction+entries.Length)%entries.Length;
                    if(entries[index].Enabled&&!entries[index].Separator) {SelectRow(index);break;}
                }
                return true;
            }
            return base.ProcessCmdKey(ref message,keyData);
        }
        private void Render()
        {
            if(rendering||ending||!IsHandleCreated||RenderError!=null)return;
            rendering=true;
            try {
                textPaints.Clear();
                using(var graphics=Graphics.FromImage(surface)) {
                    graphics.Clear(Color.Transparent);graphics.PageUnit=GraphicsUnit.Pixel;
                    graphics.SetClip(new Rectangle(1,1,Width-2,Height-2));
                    graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    // An explicit transparent one-pixel perimeter keeps both far
                    // edges and all four corners outside the painted silhouette.
                    using(var outline=MenuRoundedRectangle(new RectangleF(1,1,Width-2,Height-2),owner.MenuPixel(10)))
                    using(var background=new SolidBrush(owner.MenuBackground))graphics.FillPath(background,outline);
                    for(int i=0;i<entries.Length;i++) {
                        var item=entries[i];var bounds=rows[i];
                        if(item.Separator) {
                            using var line=new Pen(Color.FromArgb(38,38,38),owner.menuScale);
                            graphics.DrawLine(line,owner.MenuPixel(20),bounds.Top+bounds.Height/2f,Width-owner.MenuPixel(20),bounds.Top+bounds.Height/2f);continue;
                        }
                        if(IsVolume(item)) {RenderVolume(graphics,bounds,i==selected);continue;}
                        if(IsPlaybackControl(item)) {
                            if(item.Id==11) {
                                var group=new RectangleF(bounds.Left,bounds.Top+owner.MenuPixel(3),rows[i+2].Right-bounds.Left,bounds.Height-owner.MenuPixel(6));
                                using var groupShape=MenuRoundedRectangle(group,owner.MenuPixel(12));
                                using var groupFill=new LinearGradientBrush(group,owner.MenuSurface,owner.MenuBackground,LinearGradientMode.Vertical);
                                using var groupRim=new Pen(Color.FromArgb(56,56,56),owner.menuScale);
                                graphics.FillPath(groupFill,groupShape);graphics.DrawPath(groupRim,groupShape);
                            }
                            RenderPlaybackControl(graphics,item,bounds,i==selected);continue;
                        }
                        bool highlighted=i==selected&&item.Enabled;
                        var row=new RectangleF(owner.MenuPixel(7),bounds.Top+owner.MenuPixel(3),Width-owner.MenuPixel(14),bounds.Height-owner.MenuPixel(6));
                        using var shape=MenuRoundedRectangle(row,owner.MenuPixel(10));
                        using Brush fill=highlighted?new SolidBrush(owner.MenuAccent()):new LinearGradientBrush(row,owner.MenuSurface,owner.MenuBackground,LinearGradientMode.Vertical);
                        graphics.FillPath(fill,shape);
                        if(!highlighted) {using var rim=new Pen(Color.FromArgb(40,40,40),owner.menuScale);graphics.DrawPath(rim,shape);}
                        Color ink=!item.Enabled?Color.Gray:highlighted?Color.FromArgb(12,12,12):Color.FromArgb(242,242,242);
                        // Center the visible glyph bounds, not the font's asymmetric line box.
                        var inkBounds=textRenderer.MeasureInk(item.Label);float icon=owner.MenuPixel(18),gap=owner.MenuPixel(9);
                        float left=owner.MenuPixel(17);
                        owner.DrawMenuIcon(graphics,item.Id,new RectangleF(left,bounds.Top+(bounds.Height-icon)/2f,icon,icon),ink);
                        textPaints.Add((item.Label,new Point((int)Math.Round(left+icon+gap),(int)Math.Round(bounds.Top+(bounds.Height-inkBounds.Height)/2d)),ink));
                    }
                }
                foreach(var paint in textPaints)textRenderer.Paint(surface,paint.Text,paint.Origin,paint.Ink);
                if(!buffer.PresentLayered(Handle))throw new Win32Exception(Marshal.GetLastWin32Error(),"UpdateLayeredWindow failed");
                if(shown&&!recorded) {RecordDiagnostic(true);recorded=true;}
            } catch(Exception ex) when(ex is Win32Exception or ArgumentException or ExternalException) {
                RenderError=ex.ToString();RecordDiagnostic(false);if(shown)Cancel("render-error");
            } finally {rendering=false;}
        }
        private bool IsPlaybackControl(MenuEntry item)=>owner.desktopMenuActive&&item.Id is 11 or 2 or 12;
        private void RenderPlaybackControl(Graphics graphics,MenuEntry item,Rectangle bounds,bool selected)
        {
            bool highlighted=selected&&item.Enabled;
            if(highlighted) {
                var cell=new RectangleF(bounds.X+owner.MenuPixel(2),bounds.Y+owner.MenuPixel(5),bounds.Width-owner.MenuPixel(4),bounds.Height-owner.MenuPixel(10));
                using var shape=MenuRoundedRectangle(cell,owner.MenuPixel(9));
                using var brush=new SolidBrush(owner.MenuAccent());graphics.FillPath(brush,shape);
            }
            var ink=!item.Enabled?Color.Gray:highlighted?Color.FromArgb(12,12,12):Color.FromArgb(242,242,242);
            float icon=owner.MenuPixel(20),middle=bounds.Top+bounds.Height/2f;
            if(item.Id!=2) {
                owner.DrawMenuIcon(graphics,item.Id,new RectangleF(bounds.Left+(bounds.Width-icon)/2,middle-icon/2,icon,icon),ink);return;
            }
            string label=item.Label==UiLanguage.Text("继续播放")?UiLanguage.Text("播放"):UiLanguage.Text("暂停");
            var textBounds=textRenderer.MeasureInk(label);float gap=owner.MenuPixel(6),left=bounds.Left+(bounds.Width-icon-gap-textBounds.Width)/2;
            owner.DrawMenuIcon(graphics,item.Id,new RectangleF(left,middle-icon/2,icon,icon),ink);
            textPaints.Add((label,new Point((int)Math.Round(left+icon+gap),(int)Math.Round(middle-textBounds.Height/2f)),ink));
        }
        private void RecordDiagnostic(bool success)
        {
            try {
                Directory.CreateDirectory(owner.menuDiagnosticsDirectory);
                // Only our own menu pixels are saved, never the desktop behind it.
                surface.Save(Path.Combine(owner.menuDiagnosticsDirectory,"menu-surface.png"),ImageFormat.Png);
                var report=new {
                    version=System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3),renderer="owned-layered-popup-native-text",success,error=RenderError,utc=DateTimeOffset.UtcNow,foreground=Native.GetForegroundWindow()==Handle,
                    entryPoint=owner.desktopMenuActive?"desktop":"tray",anchor=new {anchor.X,anchor.Y},location=new {Left,Top},topMost=TopMost,
                    windowClass=Native.Class(Handle),classStyle=$"0x{GetClassLongPtr(Handle,-26).ToInt64():X}",
                    style=$"0x{Native.GetWindowLongPtr(Handle,-16).ToInt64():X}",exStyle=$"0x{Native.GetWindowLongPtr(Handle,-20).ToInt64():X}",
                    width=Width,height=Height,scale=owner.menuScale,dwmResults=frameResults,
                    outerCornerAlpha=new int[]{surface.GetPixel(0,0).A,surface.GetPixel(Width-1,0).A,surface.GetPixel(0,Height-1).A,surface.GetPixel(Width-1,Height-1).A},
                    rightEdgeMaxAlpha=Enumerable.Range(0,Height).Max(y=>(int)surface.GetPixel(Width-1,y).A),
                    bottomEdgeMaxAlpha=Enumerable.Range(0,Width).Max(x=>(int)surface.GetPixel(x,Height-1).A)
                };
                File.WriteAllText(Path.Combine(owner.menuDiagnosticsDirectory,"menu-render.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions {WriteIndented=true}));
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException) {System.Diagnostics.Debug.WriteLine(ex);}
        }
        internal void RecordLifecycle(string action,string? detail=null)
        {
            if(lifecycle.Count>=64)return;
            lifecycle.Add(new {milliseconds=lifetime.ElapsedMilliseconds,action,detail,shown,ending,hasHandle=IsHandleCreated,foreground=IsHandleCreated&&Native.GetForegroundWindow()==Handle});
            try {
                Directory.CreateDirectory(owner.menuDiagnosticsDirectory);
                File.WriteAllText(Path.Combine(owner.menuDiagnosticsDirectory,"menu-lifecycle.json"),JsonSerializer.Serialize(new {
                    version=System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3),utc=DateTimeOffset.UtcNow,closeReason,command=Command,renderError=RenderError,events=lifecycle
                },new JsonSerializerOptions {WriteIndented=true}));
            } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {System.Diagnostics.Debug.WriteLine(ex);}
        }
        protected override AccessibleObject CreateAccessibilityInstance()=>new PopupAccessible(this);
        private sealed class PopupAccessible(MenuPopup popup):ControlAccessibleObject(popup)
        {
            public override AccessibleRole Role=>AccessibleRole.MenuPopup;
            public override int GetChildCount()=>popup.entries.Length;
            public override AccessibleObject? GetChild(int index)=>index>=0&&index<popup.entries.Length?new RowAccessible(popup,index,this):null;
            public override AccessibleObject? GetFocused()=>GetChild(popup.selected);
            public override AccessibleObject? GetSelected()=>GetFocused();
            public override AccessibleObject? HitTest(int x,int y)=>GetChild(popup.Hit(popup.PointToClient(new Point(x,y))))??this;
        }
        private sealed class RowAccessible(MenuPopup popup,int index,AccessibleObject parent):AccessibleObject
        {
            public override string? Name {get=>popup.IsVolume(popup.entries[index])?popup.VolumeAccessibleName:popup.entries[index].Label;set { }}
            public override AccessibleRole Role=>popup.IsVolume(popup.entries[index])?AccessibleRole.Slider:popup.entries[index].Separator?AccessibleRole.Separator:AccessibleRole.MenuItem;
            public override string? Value {get=>popup.IsVolume(popup.entries[index])?popup.volumeSession!.Current.DisplayedVolume.ToString():null;set { }}
            public override AccessibleObject Parent=>parent;
            public override Rectangle Bounds=>popup.IsDisposed||!popup.IsHandleCreated?Rectangle.Empty:popup.RectangleToScreen(popup.rows[index]);
            public override string DefaultAction=>popup.IsVolume(popup.entries[index])?UiLanguage.Text("切换静音"):UiLanguage.Text("执行");
            public override AccessibleStates State=>!popup.entries[index].Enabled?AccessibleStates.Unavailable:
                popup.entries[index].Separator?AccessibleStates.None:AccessibleStates.Focusable|(index==popup.selected?AccessibleStates.Focused|AccessibleStates.Selected:AccessibleStates.None);
            public override void DoDefaultAction() {if(!popup.IsDisposed&&popup.IsHandleCreated)popup.BeginInvoke(()=>popup.InvokeRow(index));}
            public override void Select(AccessibleSelection flags) {if(!popup.IsDisposed&&popup.IsHandleCreated)popup.BeginInvoke(()=> {if(popup.entries[index].Enabled&&!popup.entries[index].Separator)popup.SelectRow(index);});}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!resourcesDisposed) {resourcesDisposed=true;volumePreviewTimer.Dispose();animation.Dispose();surface.Dispose();buffer.Dispose();}
            base.Dispose(disposing);
        }
    }
}
