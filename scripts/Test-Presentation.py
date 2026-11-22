"""Isolated native rendering regression for 0.1.10.9. Never binds to the desktop."""
import json, subprocess, threading, queue, time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts/presentation-tests'
OUT.mkdir(parents=True, exist_ok=True)
HOST = ROOT / 'artifacts/native/WallpaperHost.exe'
FFMPEG = ROOT / 'third_party/ffmpeg/bin/ffmpeg.exe'
FLAGS = subprocess.CREATE_NO_WINDOW
NEUTRAL = '2 1 0 0.12 170 0 1.2 0 0.6 0 1 1 0 0.1 0 0.15 0.75 0 0'
BRIGHT = '2 1 0 0.12 170 0 1.2 0 0.6 0.2 1 1 0 0.1 0 0.15 0.75 0 0'
PARTICLES = (ROOT / 'artifacts/particle-tests/style-8.wire').read_text(encoding='utf-8-sig').strip()
tokens = PARTICLES.split()
tokens[-60+3] = '100'  # Keep the sprite inside the small render target.
tokens[-60+9] = '45'   # The existing pixel fixture intentionally has every motion disabled.
PARTICLES = ' '.join(tokens)
results = []

def run(args, timeout=45):
    p = subprocess.run([str(a) for a in args], capture_output=True, text=True,
                       encoding='utf-8', creationflags=FLAGS, timeout=timeout)
    assert p.returncode == 0, p.stdout + p.stderr
    return p.stdout

def read_trace(path):
    return [json.loads(line) for line in path.read_text().splitlines() if line]

def record(name, info):
    results.append({'case': name, 'passed': True, **info})
    (OUT / 'results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
    print(name + ': PASS ' + json.dumps(info), flush=True)

video = OUT / '24fps.mkv'
still = OUT / 'still.png'
run([FFMPEG, '-v', 'error', '-y', '-f', 'lavfi', '-i', 'color=c=gray:s=320x180:r=24',
     '-t', '1', '-vf', 'setparams=color_trc=bt709', '-c:v', 'ffv1', video])
run([FFMPEG, '-v', 'error', '-y', '-i', video, '-frames:v', '1', still])

for name, source, image, effects, cap in [
    ('video-particles-60', video, False, PARTICLES, 0),
    ('video-particles-15', video, False, PARTICLES, 15),
    ('video-original-24', video, False, NEUTRAL, 0),
    ('image-particles-60', still, True, PARTICLES, 0),
    ('image-particles-15', still, True, PARTICLES, 15),
    ('image-static-idle', still, True, BRIGHT, 0),
]:
    trace = OUT / (name + '.jsonl')
    output = run([HOST, '--software', '--headless', '--smoke', '3', '--file', source,
                  *(['--image'] if image else []), '--rect', '0', '0', '320', '180',
                  '--effects', effects, '--fps-limit', cap, '--trace-frames', trace])
    (OUT / (name + '.events.jsonl')).write_text(output, encoding='utf-8')
    rows = read_trace(trace)
    if name == 'image-static-idle':
        assert len(rows) == 1, 'Static color adjustment must not redraw continuously'
        record(name, {'frames': len(rows)})
        continue
    active = [r for r in rows if .3 < r['wall'] < 2.8]
    fps = (len(active)-1) / (active[-1]['wall']-active[0]['wall'])
    assert all(b['time'] >= a['time'] for a, b in zip(rows, rows[1:])), 'Effect clock moved backwards'
    if effects == PARTICLES:
        assert len({r['hash'] for r in active}) > 10, 'Particles did not visibly animate'
        if cap == 0:
            assert fps > 40, f'Effects still follow source rate: {fps}'
            assert any(a['frame'] == b['frame'] and a['hash'] != b['hash']
                       for a, b in zip(active, active[1:])), 'No animation between source frames'
        else:
            assert 11 <= fps <= 16.5, f'Cap ignored or excessive missed frames: {fps}'
    else:
        assert 21 <= fps <= 26, f'Original source cadence changed: {fps}'
    record(name, {'fps': round(fps, 2), 'frames': len(rows), 'uniqueHashes': len({r['hash'] for r in active})})

class Session:
    def __init__(self, name, effects, cap):
        self.trace = OUT / (name + '.jsonl')
        self.events = queue.Queue()
        self.all_events = []
        self.p = subprocess.Popen([str(HOST), '--headless', '--software', '--file', str(video),
            '--rect', '0', '0', '320', '180', '--effects', effects, '--fps-limit', str(cap),
            '--trace-frames', str(self.trace)], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.PIPE, text=True, encoding='utf-8', creationflags=FLAGS)
        def read():
            for line in self.p.stdout:
                item = json.loads(line); item['received'] = time.monotonic()
                self.all_events.append(item); self.events.put(item)
        threading.Thread(target=read, daemon=True).start()
    def send(self, line):
        self.p.stdin.write(line + '\n'); self.p.stdin.flush()
    def wait(self, kind, timeout=6):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try: item = self.events.get(timeout=max(.01, deadline-time.monotonic()))
            except queue.Empty: break
            assert item.get('event') not in ('error', 'configuration-error'), item
            if item.get('event') == kind: return item
        raise AssertionError('Missing event ' + kind)
    def close(self):
        if self.p.poll() is None:
            self.send('stop')
            try: self.p.wait(timeout=8)
            except subprocess.TimeoutExpired: self.p.kill(); self.p.wait(); raise
        assert self.p.returncode == 0, self.p.stderr.read()

s = Session('throttled-confirmation', NEUTRAL, 1)
try:
    s.wait('prepared'); s.send('show'); s.wait('ready'); s.send('pause'); s.wait('paused')
    before = read_trace(s.trace)[-1]
    sent = time.monotonic(); s.send('configure 10 0 0 ' + BRIGHT)
    time.sleep(.2)
    assert not any(e.get('event') == 'configured' for e in s.all_events), 'Premature render confirmation'
    # A newer edit supersedes the throttled edit, and must be the acknowledged revision.
    s.send('configure 11 0 0 ' + BRIGHT)
    ack = s.wait('configured')
    assert ack['revision'] == 11 and ack['presentation'] > before['presentation'], ack
    assert str(ack['hash']) != before['hash'], 'Acknowledged old pixels'
    time.sleep(.08)
    match = [r for r in read_trace(s.trace) if r['presentation'] == ack['presentation']]
    assert match and match[-1]['hash'] == str(ack['hash']), 'Acknowledgement does not match presented pixels'
    record('throttled-confirmation', {'delaySeconds': round(ack['received']-sent, 3), 'revision': ack['revision'], 'pixelsChanged': True})
finally: s.close()

s = Session('pause-resume-effects', PARTICLES, 0)
try:
    s.wait('prepared'); s.send('show'); s.wait('ready'); time.sleep(.7)
    s.send('pause'); s.wait('paused'); time.sleep(.08)
    before = read_trace(s.trace)
    time.sleep(.7)
    assert len(read_trace(s.trace)) == len(before), 'Paused effects still refresh'
    s.send('resume'); s.wait('playing'); time.sleep(.5)
    after = read_trace(s.trace)
    resumed = after[len(before)]
    assert 0 <= resumed['time']-before[-1]['time'] < .15, 'Pause causes effect time jump'
    s.send('fps-limit 15'); time.sleep(1)
    rows = read_trace(s.trace)
    tail = [r for r in rows if r['wall'] > rows[-1]['wall']-.7]
    fps = (len(tail)-1)/(tail[-1]['wall']-tail[0]['wall'])
    assert 11 <= fps <= 16.5, 'Live cap change ignored'
    record('pause-resume-effects', {'pausedFrames': 0, 'resumeDelta': resumed['time']-before[-1]['time'], 'changedCapFps': round(fps, 2)})
finally: s.close()
print('PASS: 8 isolated presentation regression scenarios', flush=True)
