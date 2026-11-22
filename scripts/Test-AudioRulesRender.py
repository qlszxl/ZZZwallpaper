"""Isolated hidden rendering checks; no desktop attachment or audio-device use."""
from pathlib import Path
import subprocess,json,sys
from PIL import Image,ImageChops
root=Path(__file__).resolve().parents[1]
fixtures=Path(sys.argv[1]);package=Path(sys.argv[2]);out=Path(sys.argv[3]);out.mkdir(parents=True,exist_ok=True)
exe=package/'WallpaperHost.exe';source=root/'src/Windows/ControlCenter/Assets/EffectPreviews/calibration.png'
checks=[]
def check(value,label):
 if not value:raise RuntimeError(label)
 checks.append(label);print('PASS '+label)
def wire(name):return (fixtures/name).read_text(encoding='utf-8-sig').strip()
def render(name,effects,level=0,original=False,preview=True):
 destination=out/(name+'.bmp')
 args=[str(exe),'--file',str(source),'--image','--contain','--rect','0','0','640','360']
 if preview:
  args+=['--image-preview',str(destination)]
  command=f'frame 0 {int(original)} {level} 0 0 {effects}\nstop\n'
 else:args+=['--headless','--render-frame',str(destination),'--effects',effects];command=''
 result=subprocess.run(args,input=command,text=True,capture_output=True,creationflags=subprocess.CREATE_NO_WINDOW,timeout=30)
 (out/(name+'.jsonl')).write_text(result.stdout,encoding='utf-8');(out/(name+'.stderr')).write_text(result.stderr,encoding='utf-8')
 if result.returncode:raise RuntimeError(result.stdout+result.stderr)
 image=Image.open(destination).convert('RGB');check(image.size==(640,360),'Rendered '+name);return image
def equal(a,b):return ImageChops.difference(a,b).getbbox() is None
base=wire('audio-baseline-wire.txt');strong=wire('audio-static-strong-wire.txt');mapped=wire('audio-glow-wire.txt')
a=render('baseline',base);b=render('static-strong',strong);c=render('mapped-strong',mapped,1)
check(equal(b,c),'Audio strong endpoint equals actual static renderer output')
parts=base.split();parts[15]='0.100000';weak=' '.join(parts)
d=render('static-weak',weak);e=render('mapped-silence',mapped,0)
check(equal(d,e),'Silence endpoint equals actual static renderer output')
check(not equal(a,c),'Audio mapping changes actual pixels from a zero static strength')
f=render('source-unavailable',mapped,preview=False)
check(equal(a,f),'Unavailable drive restores actual static rendering')
g=render('compare-original',mapped,1,original=True);h=render('baseline-original',base,original=True)
check(equal(g,h),'Compare-original bypasses audio rules')
render('all-parameters',wire('audio-all-wire.txt'),1)
(out/'verification.json').write_text(json.dumps({'passed':True,'checks':checks,'headless':True,'desktopAttached':False,'audioDeviceOpened':False},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'Render checks passed: {len(checks)}')
