param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/Windows/ControlCenter/Assets/MetalButton'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
# Original procedural material maps. No button screenshot or lettering is
# baked into them. The clean silver has only fine, unclustered powder grain.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
public static class MetalButtonAssets {
    static double Bell(double x) { return Math.Exp(-x*x*2.0); }
    public static void Write(string directory) {
        var random = new Random(11022);
        using(var grain = new Bitmap(256,256,PixelFormat.Format32bppArgb)) {
            for(int y=0;y<256;y++) for(int x=0;x<256;x++) {
                // Independent subpixel grain only: no contours, scratches,
                // clustered stains or mottling on the clean silver face.
                int shade = random.Next(2)==0 ? 255 : 80;
                int alpha = random.Next(3,25);
                grain.SetPixel(x,y,Color.FromArgb(alpha,shade,shade,shade));
            }
            grain.Save(System.IO.Path.Combine(directory,"grain.png"),ImageFormat.Png);
        }
        using(var reflection = new Bitmap(768,384,PixelFormat.Format32bppArgb))
        using(var highlight = new Bitmap(768,384,PixelFormat.Format32bppArgb)) {
            for(int y=0;y<384;y++) for(int x=0;x<768;x++) {
                double u=x/767.0*2-1, v=y/383.0*2-1;
                double bend=.13*Math.Sin(v*2.5+.4)+.16*v;
                double width=.38+.06*Math.Cos(v*3.2-.6);
                double bright=Bell((u-bend)/width)*.88;
                bright+=Bell((u-bend-.37)/.14)*.22;
                double shade=Bell((u-bend+.43)/.2)*.31;
                double intensity=bright-shade;
                double feather=Math.Pow(Math.Max(0,1-Math.Pow(Math.Abs(u),10)),2);
                int alpha=(int)Math.Min(255,Math.Abs(intensity)*feather*255);
                int c=intensity>=0 ? 255 : 87;
                reflection.SetPixel(x,y,Color.FromArgb(alpha,c,c,c));
                // The moving layer can only brighten the face. Keep the
                // original light/dark map exclusively on the fixed folds.
                int lightAlpha=(int)Math.Min(255,Math.Max(0,intensity)*feather*255);
                highlight.SetPixel(x,y,Color.FromArgb(lightAlpha,255,255,255));
            }
            reflection.Save(System.IO.Path.Combine(directory,"reflection.png"),ImageFormat.Png);
            highlight.Save(System.IO.Path.Combine(directory,"highlight.png"),ImageFormat.Png);
        }
    }
}
'@
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
[MetalButtonAssets]::Write([IO.Path]::GetFullPath($OutputDirectory))
Get-ChildItem -LiteralPath $OutputDirectory | Select-Object Name,Length
