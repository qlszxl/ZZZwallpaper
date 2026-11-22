"""Targeted loop and handoff regression. Only owned/offscreen output; no desktop input."""
import json, subprocess, threading, queue, time, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts/test-results/playback-v0.6.0'
OUT.mkdir(parents=True,exist_ok=True)
HOST=ROOT/'artifacts/native/WallpaperHost.exe'
FFMPEG=ROOT/'third_party/ffmpeg/bin/ffmpeg.exe'
FLAGS=subprocess.CREATE_NO_WINDOW
def run(args,timeout=60):
    p=subprocess.run([str(a) for a in args],capture_output=True,text=True,encoding='utf-8',creationflags=FLAGS,timeout=timeout)
    if p.returncode:raise RuntimeError(p.stdout+p.stderr)
    return p.stdout
def frames(path):return [json.loads(line) for line in path.read_text().splitlines() if line]
def verify_trace(path,min_loops=2):
    presentations=frames(path);trace=[]
    for row in presentations:
        if trace and row['frame']==trace[-1]['frame']:
            assert row['hash']==trace[-1]['hash'], 'Original-mode redraw changed the held picture'
            continue
        trace.append(row)
    assert trace and [x['frame'] for x in trace]==list(range(1,len(trace)+1)), 'Frames lost/reordered'
    assert all(x['mean']>5 for x in trace), 'Unexpected black frame'
    starts=[0]+[i for i,x in enumerate(trace) if x['loop']]
    assert len(starts)-1>=min_loops, 'Too few loop boundaries'
    reference=[x['hash'] for x in trace[:starts[1]]]
    for a,b in zip(starts[1:],starts[2:]):
        assert [x['hash'] for x in trace[a:b]]==reference,'Loop pixel sequence changed'
    gaps=[trace[i]['wall']-trace[i-1]['wall'] for i in starts[1:]]
    assert all(trace[i]['time']>trace[i-1]['time'] for i in range(1,len(trace))), 'Playback clock moved backward'
    return {'frames':len(trace),'framesPerLoop':len(reference),'loops':len(starts)-1,'minMean':min(x['mean'] for x in trace),'maxBoundaryGapMs':round(max(gaps)*1000,2),'identicalLoopPixels':True,'noSkippedFrames':True}
only_handoff='--handoff-only' in sys.argv
results=json.loads((OUT/'results.json').read_text(encoding='utf-8')) if only_handoff and (OUT/'results.json').exists() else []
def record(name,body):
    results[:]=[r for r in results if r['case']!=name]
    try:
        info=body();results.append({'case':name,'passed':True,**info});print(name+': PASS '+json.dumps(info),flush=True)
    except Exception as e:
        results.append({'case':name,'passed':False,'error':str(e)});print(name+': FAIL '+str(e),flush=True)
    (OUT/'results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2),encoding='utf-8')
def handoff():return json.loads(run([ROOT/'artifacts/playback-tests/PlaybackTransitions.exe']))
record('Window handoff',handoff)
if only_handoff:
    assert all(x['passed'] for x in results),'Playback regression failed; see results.json'
    sys.exit(0)
fixtures=[('FFV1','ffv1.mkv',['-c:v','ffv1'],[]),
          ('MPEG4 B frames','bframes.mp4',['-c:v','mpeg4','-bf','2','-g','12'],[]),
          ('MPEG2 TS nonzero origin','offset.ts',['-c:v','mpeg2video','-bf','2','-g','12'],[]),
          ('VFR','variable.mkv',['-c:v','ffv1','-fps_mode','vfr'],['-vf',"select='not(mod(n,2))+not(mod(n,5))',setparams=color_trc=bt709"])]
for name,file,codec,filters in fixtures:
    def case(file=file,codec=codec,filters=filters):
        path=OUT/file
        run([FFMPEG,'-hide_banner','-loglevel','error','-f','lavfi','-i','testsrc2=size=320x180:rate=30:duration=1',*(filters or ['-vf','setparams=color_trc=bt709']),'-an',*codec,'-y',path])
        trace=OUT/(file+'.jsonl')
        output=run([HOST,'--software','--smoke','4','--file',path,'--rect','0','0','320','180','--trace-frames',trace])
        (OUT/(file+'.events.jsonl')).write_text(output,encoding='utf-8')
        return verify_trace(trace)
    record(name,case)
NEUTRAL='2 1 0 0.12 170 0 1.2 0 0.6 0 1 1 0 0.1 0 0.15 0.75 0 0'
def pause_case():
    trace=OUT/'pause.jsonl'
    p=subprocess.Popen([str(HOST),'--headless','--software','--file',str(OUT/'ffv1.mkv'),'--rect','0','0','320','180','--trace-frames',str(trace)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,encoding='utf-8',creationflags=FLAGS)
    events=queue.Queue();all_events=[]
    def read():
        for line in p.stdout:
            e=json.loads(line);all_events.append(e);events.put(e)
    threading.Thread(target=read,daemon=True).start()
    def wait(kind):
        deadline=time.monotonic()+6
        while time.monotonic()<deadline:
            e=events.get(timeout=max(.01,deadline-time.monotonic()))
            if e.get('event')=='error':raise RuntimeError(e)
            if e.get('event')==kind:return e
        raise TimeoutError(kind)
    def send(cmd):p.stdin.write(cmd+'\n');p.stdin.flush()
    try:
        wait('prepared');send('pause');send('show');wait('ready');wait('paused')
        send('configure 1 0 0 '+NEUTRAL);first=wait('configured');assert first['frame']==1
        send('resume');wait('playing');time.sleep(.94);send('pause');wait('paused')
        before=frames(trace)[-1];send('configure 2 0 0 '+NEUTRAL);held=wait('configured')
        assert held['frame']==before['frame'] and held['hash']==before['hash'],'Paused redraw exposed a future frame'
        time.sleep(.2);send('resume');wait('playing');time.sleep(2.5);send('stop');p.wait(timeout=5)
        assert p.returncode==0,p.stderr.read()
        return {**verify_trace(trace),'pausedRedrawPreservesFrame':True}
    finally:
        if p.poll() is None:p.kill();p.wait()
        (OUT/'pause.events.jsonl').write_text('\n'.join(json.dumps(e) for e in all_events),encoding='utf-8')
record('Pause/resume at loop seam',pause_case)
sample=ROOT/'video/初音.mp4'
if sample.exists():
    for software in [True,False]:
        def sample_case(software=software):
            trace=OUT/('sample-software.jsonl' if software else 'sample-hardware.jsonl')
            output=run([HOST,'--smoke','26','--file',sample,'--rect','0','0','320','180','--trace-frames',trace,*(['--software'] if software else [])],timeout=45)
            events=[json.loads(x) for x in output.splitlines()]
            (OUT/(trace.stem+'.events.jsonl')).write_text(output,encoding='utf-8')
            return {**verify_trace(trace),'decoder':next(e['decoder'] for e in events if e.get('event')=='ready')}
        record('User H264 '+('software' if software else 'hardware'),sample_case)
assert all(x['passed'] for x in results),'Playback regression failed; see results.json'
