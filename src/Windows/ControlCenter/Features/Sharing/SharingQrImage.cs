using LumaWall.Contracts;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LumaWall.Contracts.Sharing;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace LumaWall.ControlCenter;

internal sealed record SharingQrExport(byte[] Png,int Parts);

internal static class SharingQrImage
{
    // Integer module scaling and an untouched white quiet zone preserve scan quality.
    private const int Tile=640,Gap=24,Caption=32,Top=80;
    public const int MaxImageBytes=32*1024*1024,MaxPixels=16_000_000,MaxDimension=8192;

    public static SharingQrExport Create(byte[] packet)
    {
        string[] parts=ShareQrPayload.Split(packet);
        int columns=(int)Math.Ceiling(Math.Sqrt(parts.Length)),rows=(parts.Length+columns-1)/columns;
        int width=Gap+columns*(Tile+Gap),height=Top+rows*(Tile+Caption+Gap);
        using var canvas=new Bitmap(width,height,PixelFormat.Format32bppArgb);
        using(var graphics=Graphics.FromImage(canvas)) {
            graphics.Clear(Color.White);
            using var title=new Font("Segoe UI",22,FontStyle.Bold,GraphicsUnit.Pixel);
            using var caption=new Font("Segoe UI",16,FontStyle.Regular,GraphicsUnit.Pixel);
            graphics.DrawString("ZZZwallpaper",title,Brushes.Black,Gap,14);
            graphics.DrawString("OFFLINE CONFIGURATION  /  KEEP THE COMPLETE ORIGINAL IMAGE",caption,Brushes.Black,Gap,46);
            var writer=new QRCodeWriter();
            var hints=new Dictionary<EncodeHintType,object> {{EncodeHintType.ERROR_CORRECTION,ErrorCorrectionLevel.M},{EncodeHintType.MARGIN,4}};
            for(int i=0;i<parts.Length;i++) {
                var matrix=writer.encode(parts[i],BarcodeFormat.QR_CODE,Tile,Tile,hints);
                byte[] pixels=new byte[Tile*Tile*4];Array.Fill(pixels,(byte)255);
                for(int y=0;y<Tile;y++)for(int x=0;x<Tile;x++)if(matrix[x,y]) {
                    int offset=(y*Tile+x)*4;pixels[offset]=pixels[offset+1]=pixels[offset+2]=0;
                }
                using var bitmap=new Bitmap(Tile,Tile,PixelFormat.Format32bppArgb);
                var data=bitmap.LockBits(new Rectangle(0,0,Tile,Tile),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                try {for(int y=0;y<Tile;y++)Marshal.Copy(pixels,y*Tile*4,data.Scan0+y*data.Stride,Tile*4);}finally {bitmap.UnlockBits(data);}
                int left=Gap+i%columns*(Tile+Gap),top=Top+i/columns*(Tile+Caption+Gap);
                graphics.DrawImageUnscaled(bitmap,left,top);
                graphics.DrawString($"{i+1} / {parts.Length}",caption,Brushes.Black,left,top+Tile+4);
            }
        }
        using var output=new MemoryStream();canvas.Save(output,ImageFormat.Png);return new(output.ToArray(),parts.Length);
    }

    public static SharedConfiguration Read(byte[] pixels,int width,int height)
    {
        if(width<=0||height<=0||width>MaxDimension||height>MaxDimension||(long)width*height>MaxPixels||pixels.Length!=(long)width*height*4)
            throw new ArgumentException(UiLanguage.Text("二维码图片尺寸无效或过大。"));
        // Clipboard PNGs can be transparent. Composite against white before decoding.
        for(int i=0;i<pixels.Length;i+=4) {
            int alpha=pixels[i+3];
            for(int channel=0;channel<3;channel++)pixels[i+channel]=(byte)((pixels[i+channel]*alpha+255*(255-alpha)+127)/255);
            pixels[i+3]=255;
        }
        var source=new RGBLuminanceSource(pixels,width,height,RGBLuminanceSource.BitmapFormat.BGRA32);
        var reader=new BarcodeReaderGeneric {AutoRotate=true,Options=new DecodingOptions {TryHarder=true,TryInverted=true,PossibleFormats=new List<BarcodeFormat> {BarcodeFormat.QR_CODE}}};
        var results=reader.DecodeMultiple(source)??Array.Empty<Result>();
        var texts=new HashSet<string>(results.Select(r=>r.Text).Where(t=>t!=null),StringComparer.Ordinal);
        // For an exported contact sheet, retry each cell. This avoids missing dense
        // neighbouring symbols when the multi-detector only finds part of the set.
        foreach(int total in texts.Where(t=>t.StartsWith(ShareQrPayload.Prefix,StringComparison.Ordinal)).Select(t=>t.Split('|')).Where(f=>f.Length==5)
            .Select(f=>int.TryParse(f[3],out int n)?n:0).Where(n=>n is >1 and <=ShareQrPayload.MaxParts).Distinct().ToArray()) {
            if(texts.Count(t=>t.StartsWith(ShareQrPayload.Prefix,StringComparison.Ordinal))>=total)continue;
            int columns=(int)Math.Ceiling(Math.Sqrt(total)),rows=(total+columns-1)/columns;
            double sx=(double)width/(Gap+columns*(Tile+Gap)),sy=(double)height/(Top+rows*(Tile+Caption+Gap));
            if(Math.Abs(sx/sy-1)>.03)continue;
            for(int i=0;i<total;i++) {
                int x=(int)Math.Round((Gap+i%columns*(Tile+Gap))*sx),y=(int)Math.Round((Top+i/columns*(Tile+Caption+Gap))*sy);
                int w=Math.Min(width-x,(int)Math.Round(Tile*sx)),h=Math.Min(height-y,(int)Math.Round(Tile*sy));
                if(w<=0||h<=0)continue;
                var result=reader.Decode(source.crop(x,y,w,h));if(result?.Text!=null)texts.Add(result.Text);
            }
        }
        return ShareQrPayload.Read(texts);
    }
}
