using LumaWall.Contracts;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LumaWall.DesktopIntegration;

public sealed partial class DesktopShell
{
    private void DrawMenuIcon(Graphics graphics, uint id, RectangleF rectangle, Color ink)
    {
        var state = graphics.Save();
        try
        {
            graphics.TranslateTransform(rectangle.X, rectangle.Y);
            graphics.ScaleTransform(rectangle.Width / 24, rectangle.Height / 24);
            using var pen = new Pen(ink, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var brush = new SolidBrush(ink);
            void Line(float x1, float y1, float x2, float y2) => graphics.DrawLine(pen, x1, y1, x2, y2);
            void Box(float x, float y, float width, float height, float radius = 2)
            {
                using var path = MenuRoundedRectangle(new RectangleF(x, y, width, height), radius);
                graphics.DrawPath(pen, path);
            }
            void Tile(float x, float y) { using var path = MenuRoundedRectangle(new RectangleF(x, y, 8, 8), 2); graphics.FillPath(brush, path); }
            switch (id)
            {
                case 1: // Folder and plus: add/replace video.
                    graphics.DrawLines(pen, (PointF[])[new(2, 19), new(2, 5), new(9, 5), new(12, 8), new(21, 8), new(21, 11)]);
                    Line(2, 19, 10, 19); Line(17, 13, 17, 22); Line(12.5f, 17.5f, 21.5f, 17.5f); break;
                case 2:
                    if (menuLabels[id] == UiLanguage.Text("继续播放")) graphics.FillPolygon(brush, (PointF[])[new(6, 2), new(22, 12), new(6, 22)]);
                    else { graphics.FillRectangle(brush, 4, 3, 6, 18); graphics.FillRectangle(brush, 14, 3, 6, 18); }
                    break;
                case 11: // Previous item.
                    graphics.FillRectangle(brush,3,4,4,16);
                    graphics.FillPolygon(brush,(PointF[])[new(21,3),new(21,21),new(8,12)]);break;
                case 12: // Next item.
                    graphics.FillRectangle(brush,17,4,4,16);
                    graphics.FillPolygon(brush,(PointF[])[new(3,3),new(16,12),new(3,21)]);break;
                case 3: // Adjustable picture parameters.
                    Line(3, 5, 21, 5); Line(3, 12, 21, 12); Line(3, 19, 21, 19);
                    graphics.FillEllipse(brush, 5, 1, 8, 8); graphics.FillEllipse(brush, 13, 8, 8, 8); graphics.FillEllipse(brush, 4, 15, 8, 8); break;
                case 4: // Screen.
                    Box(2, 3, 20, 14); Line(12, 17, 12, 22); Line(7, 22, 17, 22); break;
                case 5: // Widgets.
                    Tile(2, 2); Tile(14, 2); Tile(2, 14); Tile(14, 14); break;
                case 9: // Stop wallpaper on this screen.
                    Box(2, 3, 20, 14); Line(12, 17, 12, 22); Line(7, 22, 17, 22);
                    Line(9, 7, 15, 13); Line(15, 7, 9, 13); break;
                case 6: // Control center/home.
                    graphics.DrawLines(pen, (PointF[])[new(2, 11), new(12, 2), new(22, 11)]);
                    graphics.DrawLines(pen, (PointF[])[new(5, 10), new(5, 22), new(10, 22), new(10, 15), new(15, 15), new(15, 22), new(20, 22), new(20, 10)]); break;
                case 7: // Restore the desktop's system menu.
                case 10: // Resume interception from the tray menu.
                    Box(2, 2, 19, 19); Line(6, 7, 16, 7); Line(6, 12, 12, 12); Line(6, 17, 9, 17);
                    graphics.FillPolygon(brush, (PointF[])[new(14, 12), new(24, 18), new(19, 19), new(17, 24)]); break;
                case 8: // Power/exit.
                    graphics.DrawArc(pen, 3, 3, 18, 18, -45, 270); Line(12, 1, 12, 11); break;
            }
        }
        finally { graphics.Restore(state); }
    }
}
