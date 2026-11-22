"""GPU pixel regressions and paused-preview protocol tests; only owned render targets."""
import json
import queue
import subprocess
import threading
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "artifacts/test-results/effects-v2"
OUT.mkdir(parents=True, exist_ok=True)
HOST = ROOT / "artifacts/native/WallpaperHost.exe"
FFMPEG = ROOT / "third_party/ffmpeg/bin/ffmpeg.exe"
DEFAULT = dict(version=2, intensity=1, scan=0, scan_strength=.12, density=170,
               rgb=0, offset=1.2, soft=0, radius=.6, brightness=0, contrast=1,
               saturation=1, vignette=0, vignette_strength=.1, glow=0,
               glow_strength=.15, threshold=.75, noise=0, noise_strength=0)


def wire(**settings):
    return " ".join(str(v) for v in (DEFAULT | settings).values())


NEUTRAL = wire()
SCREEN = wire(scan=1, rgb=1, soft=1, contrast=1.05, saturation=.95, vignette=1, glow=1)
SOFT = wire(soft=1, radius=1.2, glow=1, glow_strength=.18, vignette=1,
            vignette_strength=.08, contrast=.98, saturation=.95)
checks = []


def check(ok, name):
    checks.append({"name": name, "passed": bool(ok)})
    print(f"{'PASS' if ok else 'FAIL'}: {name}", flush=True)
    if not ok:
        raise AssertionError(name)


def fixture(name, pixels, transfer=None):
    source = OUT / (name + ".png")
    Image.fromarray(pixels.astype(np.uint8)).save(source)
    video = OUT / (name + ".mkv")
    metadata = ["-vf", "setparams=color_trc=" + transfer, "-color_trc", transfer] if transfer else []
    subprocess.run([str(FFMPEG), "-v", "error", "-y", "-loop", "1", "-i", str(source),
                    "-t", "1", "-r", "30", "-c:v", "ffv1", "-pix_fmt", "bgr0",
                    *metadata, str(video)], check=True)
    return video


def render(source, name, effects=NEUTRAL, width=320, height=180, extra=()):
    target = OUT / (name + ".bmp")
    run = subprocess.run([str(HOST), "--software", "--file", str(source), "--rect",
                          "0", "0", str(width), str(height), "--effects", effects,
                          "--render-frame", str(target), *extra],
                         capture_output=True, text=True, encoding="utf-8", timeout=20)
    if run.returncode:
        raise RuntimeError(run.stdout + run.stderr)
    return np.array(Image.open(target).convert("RGB"), dtype=np.float64), json.loads(run.stdout)["passes"]


try:
    gray_pixels = np.full((180, 320, 3), 128)
    gray = fixture("gray", gray_pixels)
    y, x = np.indices((180, 320))
    color = fixture("color", np.stack((40 + x / 2, 40 + y, 200 - x / 3), axis=2))
    light = np.zeros((180, 320, 3)); light[60:120, 130:190] = 192
    lights = fixture("lights", light)
    edge = np.zeros((180, 320, 3)); edge[:, 154:166] = 255
    edges = fixture("edge", edge)
    original, passes = render(color, "neutral")
    check(passes == 1, "neutral skips post-processing passes")
    disabled, passes = render(color, "disabled", wire(scan_strength=.35, offset=3, radius=2, glow_strength=.5, noise_strength=.02))
    check(np.array_equal(original, disabled) and passes == 1, "disabled switches retain parameters without changing original pixels")
    zero, passes = render(color, "zero", wire(scan=1, scan_strength=0, rgb=1, offset=0, soft=1, radius=0, glow=1, glow_strength=0, vignette=1, vignette_strength=0, noise=1))
    check(np.array_equal(original, zero) and passes == 1, "zero strengths bypass every effect")
    master, passes = render(color, "master-zero", wire(intensity=0, scan=1, rgb=1, soft=1, glow=1, noise=1, noise_strength=.02, brightness=.2))
    check(np.array_equal(original, master) and passes == 1, "master intensity zero bypasses every effect and bloom allocation")
    compared, passes = render(color, "compare", SCREEN, extra=("--original",))
    check(np.array_equal(original, compared) and passes == 1, "original comparison bypasses every effect")
    bright, _ = render(color, "brightness", wire(brightness=.2))
    check(bright.mean() > original.mean() + 35, "brightness increases video luminance")
    half, _ = render(color, "half-intensity", wire(brightness=.2, intensity=.5))
    check(np.max(abs(half - (original + bright) / 2)) <= 1, "master intensity mixes original and styled pixels proportionally")
    contrasted, _ = render(color, "contrast", wire(contrast=.5))
    check(contrasted.std() < original.std() * .65, "contrast changes tonal range")
    mono, _ = render(color, "monochrome", wire(saturation=0))
    check(np.max(np.ptp(mono, axis=2)) <= 1, "zero saturation produces neutral gray")
    vignette, _ = render(gray, "vignette", wire(vignette=1, vignette_strength=.8))
    check(vignette[80:100, 150:170].mean() > vignette[:15, :15].mean() + 35, "vignette preserves center and darkens edges")
    for height, density in ((180, 170), (360, 170), (360, 80), (360, 260)):
        scan, passes = render(gray, f"scanlines-{height}-{density}", wire(scan=1, scan_strength=.35, density=density), height=height)
        rows = scan.mean(axis=(1, 2))
        peak = np.argmax(abs(np.fft.rfft(rows - rows.mean()))[1:]) + 1
        check(abs(peak - height * density / 1080) <= 1 and np.ptp(rows) > 25 and passes == 1,
              f"scanline frequency follows {density} cycles per 1080 output pixels at {height}p")
    centroids = []
    for height in (540, 1080):
        split, _ = render(edges, f"rgb-{height}", wire(rgb=1, offset=3), height=height)
        profile = split.mean(axis=0)
        centers = (profile * np.arange(320)[:, None]).sum(axis=0) / profile.sum(axis=0)
        centroids.append(centers)
        check(abs((centers[2] - centers[0]) - 6 * height / 1080) < .15
              and abs(centers[1] - 159.5) < .1, f"RGB offsets scale with output height at {height}p and preserve green")
    edge_hd = np.zeros((1080, 320, 3)); edge_hd[:, 154:166] = 255
    hd_edges = fixture("edge-hd", edge_hd)
    sharp, _ = render(hd_edges, "sharp", height=1080)
    softened, _ = render(hd_edges, "softened", wire(soft=1, radius=2), height=1080)
    check(abs(np.diff(softened[540, :, 1])).max() < abs(np.diff(sharp[540, :, 1])).max() * .9
          and np.ptp(softened, axis=2).max() == 0, "independent softening smooths edges without RGB separation")
    bloom, passes = render(lights, "bloom-low", wire(glow=1, glow_strength=.5, threshold=.2))
    no_bloom, _ = render(lights, "bloom-high", wire(glow=1, glow_strength=.5, threshold=.9))
    plain_light, _ = render(lights, "plain-light")
    check(passes == 4 and bloom[70:110, 121:130].mean() > 8, "glow spreads highlights with reduced-resolution passes")
    check(np.max(abs(no_bloom - plain_light)) <= 1, "higher glow threshold excludes mid-bright regions")
    for trc in ("iec61966-2-1", "bt709", "linear"):
        tagged = fixture("transfer-" + trc, gray_pixels, trc)
        probe = subprocess.run([str(HOST), "--file", str(tagged), "--software", "--probe"], capture_output=True, text=True, check=True)
        check(json.loads(probe.stdout)["color"] == "SDR / " + trc, "decode preserves declared " + trc + " transfer metadata")
        glowing, _ = render(tagged, "linear-glow-" + trc, wire(glow=1, glow_strength=.5, threshold=.1))
        value = 128 / 255
        if trc == "iec61966-2-1":
            expected = (1.055 * (((value + .055) / 1.055) ** 2.4 * 1.5) ** (1 / 2.4) - .055) * 255
        elif trc == "bt709":
            expected = (1.099 * (((value + .099) / 1.099) ** (1 / .45) * 1.5) ** .45 - .099) * 255
        else:
            expected = 192
        check(abs(glowing[90, 160].mean() - expected) < 2, f"glow composites in linear light using declared {trc} transfer")
    noisy, _ = render(gray, "noise-frame-1", wire(noise=1, noise_strength=.02))
    same, _ = render(gray, "noise-frame-1-again", wire(noise=1, noise_strength=.02))
    next_frame, _ = render(gray, "noise-frame-2", wire(noise=1, noise_strength=.02), extra=("--capture-frame", "2"))
    check(np.max(abs(noisy - 128)) <= 6 and noisy.std() > 2.5, "noise amplitude stays within configured encoded RGB range")
    check(np.array_equal(noisy, same) and np.mean(noisy != next_frame) > .8, "noise is deterministic per video frame and changes on the next frame")
    bars, _ = render(color, "letterbox", SCREEN, width=320, height=320, extra=("--contain",))
    check(bars[:60].max() == 0 and bars[-60:].max() == 0 and bars[80:240].mean() > 10, "effects leave contain-mode black bars untouched")
    for name, values in (("version", wire(version=7)), ("nonfinite", wire(brightness="nan")),
                         ("range", wire(offset=4)), ("boolean", wire(scan=2)), ("trailing", NEUTRAL + " extra")):
        bad = subprocess.run([str(HOST), "--file", str(color), "--effects", values, "--smoke", "1"], capture_output=True, text=True, encoding="utf-8", timeout=10)
        check(bad.returncode != 0 and json.loads(bad.stdout)["event"] == "error", "native rejects invalid " + name)

    process = subprocess.Popen([str(HOST), "--file", str(color), "--headless"], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8")
    events = queue.Queue()
    def reader():
        for line in process.stdout:
            events.put(json.loads(line))
    threading.Thread(target=reader, daemon=True).start()
    def wait(kind):
        while True:
            event = events.get(timeout=10)
            if event.get("event") == "error":
                raise RuntimeError(event)
            if event.get("event") == kind:
                return event
    def send(text):
        process.stdin.write(text + "\n"); process.stdin.flush()
    try:
        wait("prepared"); send("show"); wait("ready"); send("pause"); wait("paused")
        send("configure 1 0 0 " + SCREEN); ack = wait("configured")
        check(ack["revision"] == 1 and ack["paused"] and ack["passes"] == 4, "paused preview redraws with new effects")
        send("configure 2 0 1 " + SCREEN); ack = wait("configured")
        check(ack["paused"] and ack["passes"] == 1, "original comparison works while paused")
        send("configure 3 0 0 invalid"); wait("configuration-error")
        send("configure 4 0 0 " + NEUTRAL); ack = wait("configured")
        check(ack["revision"] == 4 and ack["passes"] == 1, "invalid live update leaves preview usable and reset restores bypass")
        send("stop"); wait("stopped"); process.wait(timeout=5)
        check(process.returncode == 0, "preview shuts down cleanly after effect changes")
    finally:
        if process.poll() is None:
            process.kill()
        process.wait()

    tiles = []
    samples = [("original", NEUTRAL, "原画"), ("screen", SCREEN, "屏幕质感 · 默认"), ("soft", SOFT, "柔和 · 默认"),
               ("scan", wire(scan=1, scan_strength=.35, density=170), "扫描线 · 暗化 35% / 密度 170"),
               ("rgb", wire(rgb=1, offset=3), "RGB 分离 · 3 px"),
               ("glow-noise", wire(glow=1, glow_strength=.5, threshold=.35, noise=1, noise_strength=.02), "辉光 50% / 阈值 35% + 噪点 2%")]
    for name, settings, label in samples:
        pixels, _ = render(ROOT / "video/初音.mp4", "sample-" + name, settings, width=640, height=360)
        tile = Image.new("RGB", (640, 400), "#141923"); tile.paste(Image.fromarray(pixels.astype(np.uint8)), (0, 40))
        ImageDraw.Draw(tile).text((18, 8), label, fill="#e8e9ff", font=ImageFont.truetype("C:/Windows/Fonts/msyh.ttc", 20))
        tiles.append(tile)
    montage = Image.new("RGB", (1920, 800))
    for i, tile in enumerate(tiles):
        montage.paste(tile, ((i % 3) * 640, (i // 3) * 400))
    montage.save(OUT / "comparison.png")
finally:
    (OUT / "results.json").write_text(json.dumps(checks, indent=2), encoding="utf-8")

print(f"{len(checks)} effect checks passed.")
