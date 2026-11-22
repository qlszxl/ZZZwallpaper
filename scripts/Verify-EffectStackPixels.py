"""Compare synthetic GPU readbacks; no desktop capture or UI automation."""
import json
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

folder = Path(sys.argv[1]).resolve()
checks = []
images = {}
renders = {}
for line in (folder / "renders.tsv").read_text(encoding="utf-8").splitlines():
    name, passes, submit, gpu, samples = line.split("\t")
    renders[name] = dict(passes=int(passes), submit_ms=float(submit), gpu_ms=float(gpu), gpu_samples=int(samples))


def pixels(name):
    if name not in images:
        images[name] = np.asarray(Image.open(folder / (name + ".bmp")).convert("RGB"), dtype=np.float64)
    return images[name]


def check(ok, name, **metrics):
    checks.append(dict(name=name, passed=bool(ok), **metrics))
    print(("PASS " if ok else "FAIL ") + name, metrics, flush=True)


def difference(a, b):
    return abs(pixels(a) - pixels(b))


def gradient(name):
    image = pixels(name)
    return float(np.mean(np.diff(image, axis=0) ** 2) + np.mean(np.diff(image, axis=1) ** 2))


for name in ("neutral-stack", "intensity-zero", "disabled-particles", "ordered-disabled", "mask-library-only"):
    error = float(difference(name, "original").max())
    check(error <= 1 and renders[name]["passes"] == 1, name + " preserves original and skips disabled passes", max_error=error, passes=renders[name]["passes"])

singles = [name[:-7] for name in renders if name.endswith("-legacy") and name != "pointwise-legacy"]
for name in singles:
    error = difference(name + "-legacy", name + "-stack")
    # Intermediate half-float surfaces can introduce a final rounding step.
    check(error.max() <= 3 and error.mean() < .35, name + " single-layer pixels match the legacy effect", max_error=float(error.max()), mean_error=float(error.mean()))

for before, after in (("color-particles", "particles-color"), ("blur-particles", "particles-blur"), ("glow-particles", "particles-glow")):
    mean = float(difference(before, after).mean())
    check(mean > .5, "Reordering " + before + " changes actual pixels", mean_difference=mean)

chroma_after = float(np.ptp(pixels("particles-color"), axis=2).max())
chroma_before = float(np.ptp(pixels("color-particles"), axis=2).mean())
check(chroma_after <= 1 and chroma_before > 5, "Color above particles processes their colors", after_max_chroma=chroma_after, before_mean_chroma=chroma_before)
check(np.ptp(pixels("subparticles-color"), axis=2).max() <= 1 and pixels("subparticles-color").mean() > 1, "Later color also processes the secondary particle layer")
sharp, blurred = gradient("blur-particles"), gradient("particles-blur")
check(blurred < sharp * .8, "Blur above particles softens their edges", sharp_gradient=sharp, blurred_gradient=blurred)
before, after = float(pixels("glow-particles").mean()), float(pixels("particles-glow").mean())
check(after > before + .5, "Glow above particles uses their highlights", before_mean=before, after_mean=after)
check(np.ptp(pixels("rotated-particles-color"), axis=2).max() <= 1 and np.ptp(pixels("rotated-color-particles"), axis=2).mean() > 1, "Fractional rotated viewport keeps particle and color ordering")
check(gradient("rotated-particles-blur") < gradient("rotated-blur-particles") * .8, "Fractional rotated viewport keeps blur sampling and particles aligned")
check(pixels("rotated-particles-glow").mean() > pixels("rotated-glow-particles").mean() + .5, "Fractional rotated viewport keeps glow sampling and particles aligned")
half_error = abs(pixels("intensity-half") - (pixels("original") + pixels("intensity-full")) / 2)
check(half_error.max() <= 2, "Master intensity blends the complete stack exactly once", max_error=float(half_error.max()))
audio_error = abs(pixels("audiozoom-half") - (pixels("original") + pixels("audiozoom-full")) / 2)
check(audio_error.max() <= 2 and difference("audiozoom-full", "original").mean() > 1, "Audio zoom also receives master intensity exactly once", max_error=float(audio_error.max()))
check(difference("audiozoom-zero", "original").max() <= 1, "Zero master intensity also disables audio zoom")

masked = pixels("masked-color")
check(np.ptp(masked[:, :280], axis=2).max() <= 1, "Color mask applies to its selected region")
outside_error = abs(masked[:, 365:] - pixels("original")[:, 365:])
check(outside_error.max() <= 2, "Color mask preserves pixels outside its region", max_error=float(outside_error.max()))
masked_particles = pixels("particles-masked-color")
check(np.ptp(masked_particles[:, :280], axis=2).max() <= 1 and np.ptp(masked_particles[:, 365:], axis=2).mean() > 3, "Layer mask processes earlier particles only inside the selected region")
preview = pixels("mask-preview")
check(np.ptp(preview, axis=2).max() == 0 and preview[:, :280].mean() > 250 and preview[:, 365:].mean() < 1, "Mask preview bypasses the stack and both particle layers")
library_preview = pixels("mask-library-preview")
check(np.ptp(library_preview, axis=2).max() == 0 and library_preview[:, :280].mean() > 250 and library_preview[:, 365:].mean() < 1, "Auxiliary-only mask preview works with zero master intensity")

for source in ("portrait", "rotated", "wide"):
    error = difference(source + "-original", source + "-neutral")
    check(error.max() <= 1, source + " neutral stack preserves framing and letterbox", max_error=float(error.max()))

forward, reverse = renders["pointwise-forward"]["passes"], renders["pointwise-reverse"]["passes"]
check(forward < reverse, "Compatible adjacent pointwise effects share a pass", forward_passes=forward, reverse_passes=reverse)
check(renders["pointwise-default"]["passes"] == forward and difference("pointwise-default", "pointwise-forward").max() == 0, "Unsorted saved profiles render in the displayed default card order immediately")
check(difference("pointwise-forward", "pointwise-reverse").mean() > .05, "Reverse pointwise order is not incorrectly fused")
same = difference("pointwise-forward", "pointwise-legacy")
check(same.max() <= 3 and same.mean() < .35, "Fused pointwise layers retain the existing arithmetic order", max_error=float(same.max()), mean_error=float(same.mean()))

# Export a compact evidence image using only synthetic test frames.
pairs = [("color-particles", "particles-color", "调色与粒子"), ("blur-particles", "particles-blur", "模糊与粒子"), ("glow-particles", "particles-glow", "泛光与粒子")]
captions = {"color-particles": "先调色 → 后粒子", "particles-color": "先粒子 → 后调色", "blur-particles": "先模糊 → 后粒子", "particles-blur": "先粒子 → 后模糊", "glow-particles": "先泛光 → 后粒子", "particles-glow": "先粒子 → 后泛光"}
board = Image.new("RGB", (1000, 990), "#14171d")
draw = ImageDraw.Draw(board)
font_path = Path("C:/Windows/Fonts/msyh.ttc")
font = ImageFont.truetype(str(font_path), 22) if font_path.exists() else ImageFont.load_default()
small = ImageFont.truetype(str(font_path), 18) if font_path.exists() else ImageFont.load_default()
for row, (a, b, title) in enumerate(pairs):
    y = row * 330
    draw.text((20, y + 12), title, font=font, fill="white")
    for column, name in enumerate((a, b)):
        x = 20 + column * 500
        draw.text((x, y + 47), captions[name], font=small, fill="#b6c9e5")
        image = Image.open(folder / (name + ".bmp")).convert("RGB").resize((460, 259))
        board.paste(image, (x, y + 71))
board.save(folder / "effect-order-comparison.png")
report = dict(passed=all(check["passed"] for check in checks), checks=checks, renders=renders,
              scope="Synthetic frames in an owned hidden D3D window; no Explorer attachment or interactive desktop validation.")
(folder / "pixel-verification.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(f"Pixel checks passed: {sum(check['passed'] for check in checks)}/{len(checks)}")
sys.exit(0 if report["passed"] else 1)
