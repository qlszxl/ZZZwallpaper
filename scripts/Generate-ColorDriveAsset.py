"""Build vector hardware and a continuous, antialiased hue texture for the UI."""
from pathlib import Path
import colorsys
import math
import struct
import zlib

root = Path(__file__).resolve().parents[1]
output = root / "src/Windows/ControlCenter/Assets/ColorPicker/DiscDrive.svg"
parts = ['''<svg xmlns="http://www.w3.org/2000/svg" width="460" height="400" viewBox="0 0 460 400">
<defs>
 <linearGradient id="rim" x2="0" y2="1"><stop stop-color="#626262"/><stop offset=".12" stop-color="#1b1b1b"/><stop offset=".85" stop-color="#101010"/><stop offset="1" stop-color="#424242"/></linearGradient>
 <linearGradient id="tray" x2=".85" y2="1"><stop stop-color="#292929"/><stop offset=".48" stop-color="#131313"/><stop offset="1" stop-color="#222222"/></linearGradient>
 <linearGradient id="rail" x2="1" y2="0"><stop stop-color="#080808"/><stop offset=".28" stop-color="#4d4d4d"/><stop offset=".55" stop-color="#242424"/><stop offset="1" stop-color="#080808"/></linearGradient>
 <linearGradient id="hub" x2="1" y2="1"><stop stop-color="#454545"/><stop offset=".22" stop-color="#161616"/><stop offset=".65" stop-color="#242424"/><stop offset="1" stop-color="#080808"/></linearGradient>
</defs>
<rect x="1" y="1" width="458" height="398" rx="14" fill="#050505" stroke="#343434" stroke-width="2"/>
<rect x="9" y="10" width="442" height="380" rx="8" fill="url(#rim)"/>
<rect x="12" y="12" width="436" height="374" rx="6" fill="#030303"/>
<rect x="27" y="14" width="406" height="370" rx="5" fill="url(#tray)" stroke="#434343"/>
<rect x="30" y="17" width="400" height="364" rx="4" fill="none" stroke="#080808" stroke-width="2"/>
''']
for x in (12, 436):
    parts.append(f'<rect x="{x}" y="14" width="12" height="364" rx="2" fill="url(#rail)"/>')
    parts.append(f'<rect x="{x+2}" y="88" width="8" height="120" rx="2" fill="#030303" stroke="#454545"/>')
for x in (47, 413):
    for y in (35, 365):
        parts.append(f'<circle cx="{x}" cy="{y}" r="7" fill="#030303" stroke="#515151"/><circle cx="{x}" cy="{y-1}" r="4.5" fill="url(#hub)"/>')
parts.append('<circle cx="230" cy="200" r="190" fill="#030303" stroke="#424242" stroke-width="2"/><circle cx="230" cy="200" r="184" fill="#8d8d8d"/><circle cx="230" cy="200" r="181" fill="#191919"/>')

output.parent.mkdir(parents=True, exist_ok=True)
output.write_text('\n'.join(parts)+ '\n</svg>\n', encoding='utf-8')

# Keep spindle and tray lip above the hue texture. The background no longer
# contains hundreds of adjacent wedges or closely spaced concentric strokes.
parts = [parts[0].split('</defs>')[0] + '</defs>']
parts.append('''<circle cx="230" cy="200" r="81" fill="url(#hub)" stroke="#030303" stroke-width="3"/>
<circle cx="230" cy="200" r="78" fill="none" stroke="#555"/>
<circle cx="230" cy="200" r="46" fill="#030303" stroke="#686868" stroke-width="2"/>
<circle cx="230" cy="200" r="42" fill="url(#hub)" stroke="#343434" stroke-width="2"/>
<circle cx="230" cy="200" r="36" fill="none" stroke="#080808" stroke-width="2"/>
''')
for angle in range(0,360,60):
    parts.append(f'<rect x="228" y="158" width="4" height="10" rx="2" fill="#040404" stroke="#494949" stroke-width=".6" transform="rotate({angle} 230 200)"/>')
parts.append('''<circle cx="230" cy="200" r="24" fill="#020202" stroke="#626262"/>
<path d="M29 376 H202 Q230 358 258 376 H431 V386 H29 Z" fill="url(#rim)" stroke="#030303" stroke-width="2"/>
<path d="M202 15 H258 Q255 23 250 23 H210 Q205 23 202 15 Z" fill="#050505" stroke="#494949"/>
</svg>''')
hub_output = output.with_name('DiscHub.svg')
hub_output.write_text('\n'.join(parts)+'\n', encoding='utf-8')

# Directly sample a continuous hue field: no antialiased slice boundaries, no
# subpixel groove texture. Large smooth highlights retain the optical sheen.
# PNG encoding uses only the standard library; this is a generated UI resource.
size = 1024
pixels = bytearray()
for y in range(size):
    pixels.append(0)  # PNG filter: none
    dy = (y + .5 - size/2) / (size/2)
    for x in range(size):
        dx = (x + .5 - size/2) / (size/2)
        radius = math.hypot(dx,dy)
        if radius > 1 + 1/size or radius < 80/179:
            pixels.extend((0,0,0,0))
            continue
        angle = math.atan2(dy,dx)
        hue = ((math.degrees(angle) + 20) % 360) / 360
        rgb = colorsys.hsv_to_rgb(hue,.94,1)
        sheen = .16 * max(0,math.cos(2*(angle-.18)))**12
        sheen += .09 * max(0,math.cos(2*(angle-1.4)))**10
        alpha = round(max(0,min(1,(1-radius)*size/2+.5))*255)
        pixels.extend((*[round((c+(1-c)*sheen)*255) for c in rgb],alpha))

def chunk(kind,data):
    return struct.pack('!I',len(data))+kind+data+struct.pack('!I',zlib.crc32(kind+data)&0xffffffff)

png = b'\x89PNG\r\n\x1a\n'
png += chunk(b'IHDR',struct.pack('!2I5B',size,size,8,6,0,0,0))
png += chunk(b'IDAT',zlib.compress(pixels,9))+chunk(b'IEND',b'')
texture_output = output.with_name('DiscHue.png')
texture_output.write_bytes(png)
for asset in (output,hub_output,texture_output):
    print(f'Created {asset.name} ({asset.stat().st_size} bytes)')
