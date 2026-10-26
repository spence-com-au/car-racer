#!/usr/bin/env python3
"""Draw the app icon as SVG and render the textures ProjectConfigurator sets as Android's icons.

The SVGs are the source; the PNGs in Unity/Assets/CarRacer/Icon are renders of them. Rendering
uses Node and Playwright's Chromium (`npm i playwright`, or any machine where
`require('playwright')` works).

Usage: tools/icon/generate_icon.py
"""

import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
SOURCES = Path(__file__).resolve().parent
TEXTURES = ROOT / "Unity" / "Assets" / "CarRacer" / "Icon"

# Six of the player colours: the Okabe-Ito hues in CarColours.
YELLOW, BLUE, VERMILION, GREEN, PURPLE, SKY = "#F2BF1A", "#0073B3", "#D65E00", "#009E73", "#CC78A6", "#57B5E8"
NAVY, NAVY_DARK, INK = "#16264A", "#0B1428", "#1A1A20"

# Android shows the middle 72 of an adaptive icon's 108 dp and may cut that to a circle, so the
# foreground keeps within the 66 dp safe zone.
ADAPTIVE_SCALE = 0.6
ROUND_SCALE = 0.86
SIZE = 432  # xxxhdpi adaptive layer; Unity scales it down for the other densities

KART = f"""
  <ellipse cx="0" cy="4" rx="170" ry="14" fill="#000" opacity=".28"/>
  <path d="M-150 -118 h70 l-6 22 h-70 z" fill="{YELLOW}" stroke="{INK}" stroke-width="9" stroke-linejoin="round"/>
  <path d="M-112 -98 l8 40" stroke="{INK}" stroke-width="12" stroke-linecap="round"/>
  <path d="M-158 -40 Q-160 -70 -128 -74 L-58 -78 Q-40 -116 6 -118 L40 -118 Q66 -116 80 -86
           L150 -66 Q182 -58 178 -34 L174 -18 Q170 -6 156 -6 L-140 -6 Q-158 -6 -158 -24 Z"
        fill="{YELLOW}" stroke="{INK}" stroke-width="10" stroke-linejoin="round"/>
  <path d="M-150 -44 L170 -40" stroke="#fff" stroke-width="10" stroke-linecap="round" opacity=".9"/>
  <circle cx="-10" cy="-44" r="21" fill="#fff" stroke="{INK}" stroke-width="7"/>
  <path d="M-15 -55 l7 -4 v32" fill="none" stroke="{INK}" stroke-width="7" stroke-linecap="round" stroke-linejoin="round"/>
  <circle cx="18" cy="-140" r="36" fill="{VERMILION}" stroke="{INK}" stroke-width="10"/>
  <path d="M26 -152 h26 q8 0 8 10 v6 q0 10 -10 10 h-24 z" fill="{SKY}" stroke="{INK}" stroke-width="7" stroke-linejoin="round"/>
  <path d="M-14 -148 q14 -22 40 -24" fill="none" stroke="#fff" stroke-width="7" stroke-linecap="round" opacity=".7"/>
  <g stroke="{INK}" stroke-width="10">
    <circle cx="-100" cy="-28" r="48" fill="#2A2A33"/>
    <circle cx="-100" cy="-28" r="20" fill="#D9DDE3"/>
    <circle cx="122" cy="-24" r="42" fill="#2A2A33"/>
    <circle cx="122" cy="-24" r="17" fill="#D9DDE3"/>
  </g>"""


def streaks():
    lengths = [96, 130, 84, 118, 72, 104]
    colours = [YELLOW, BLUE, VERMILION, GREEN, PURPLE, SKY]
    return "".join(
        f'<path d="M{150 - ln} {210 + i * 26} H150" stroke="{c}" stroke-width="13" stroke-linecap="round"/>'
        for i, (c, ln) in enumerate(zip(colours, lengths)))


def flag():
    cells = "".join(
        f'<rect x="{c * 14}" y="{r * 14}" width="14" height="14"/>'
        for r in range(4) for c in range(6) if (r + c) % 2 == 0)
    return (f'<g transform="translate(350 92) rotate(12)">'
            f'<path d="M0 0 V124" stroke="#D9DDE3" stroke-width="8" stroke-linecap="round"/>'
            f'<g transform="translate(4 4)"><rect width="84" height="56" fill="#fff"/><g fill="{INK}">{cells}</g></g></g>')


# Artwork in a 512 square, centred on (256, 256).
SKY_LAYER = f"""<defs><linearGradient id="bg" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="{NAVY}"/><stop offset="1" stop-color="{NAVY_DARK}"/></linearGradient></defs>
<rect width="512" height="512" fill="url(#bg)"/>"""
# The road runs well past the edges so it still reaches them when the scene is scaled down.
SCENE_BACK = f"""<circle cx="300" cy="230" r="170" fill="{SKY}" opacity=".10"/>
<path d="M-500 470 Q256 330 1000 470 V1200 H-500 Z" fill="#2C3550"/>
<path d="M-500 470 Q256 330 1000 470" fill="none" stroke="#fff" stroke-width="10" opacity=".85"/>
<path d="M-500 530 Q256 400 1000 530" fill="none" stroke="{YELLOW}" stroke-width="9" stroke-dasharray="34 26" opacity=".9"/>"""
SCENE_FRONT = f"""<g transform="rotate(-6 256 256)">{streaks()}</g>
<g transform="translate(286 372) rotate(-6) scale(.92)">{KART}</g>
{flag()}"""


def scaled(content, scale):
    return f'<g transform="translate(256 256) scale({scale}) translate(-256 -256)">{content}</g>'


def svg(body, clip=None):
    clip_def = f'<clipPath id="clip">{clip}</clipPath>' if clip else ""
    inner = f'<g clip-path="url(#clip)">{body}</g>' if clip else body
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">'
            f'<defs>{clip_def}</defs>{inner}</svg>\n')


ICONS = {
    # Adaptive icon layers (Android 8+): the launcher masks and moves them.
    "icon-background": svg(SKY_LAYER + scaled(SCENE_BACK, ADAPTIVE_SCALE)),
    "icon-foreground": svg(scaled(SCENE_FRONT, ADAPTIVE_SCALE)),
    # Pre-masked icons for launchers that ask for a round or a plain one.
    "icon-round": svg(SKY_LAYER + scaled(SCENE_BACK + SCENE_FRONT, ROUND_SCALE), clip='<circle cx="256" cy="256" r="256"/>'),
    "icon-legacy": svg(SKY_LAYER + SCENE_BACK + SCENE_FRONT, clip='<rect width="512" height="512" rx="112"/>'),
}

RENDER = """
const { chromium } = require('playwright');
const jobs = JSON.parse(process.argv[1]);
(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: jobs.size, height: jobs.size } });
  for (const [svg, png] of jobs.files) {
    const sized = svg.replace('width="512" height="512"', `width="${jobs.size}" height="${jobs.size}"`);
    await page.setContent(`<html><body style="margin:0;background:transparent">${sized}</body></html>`);
    await page.screenshot({ path: png, omitBackground: true });
  }
  await browser.close();
})();
"""


def main():
    TEXTURES.mkdir(parents=True, exist_ok=True)
    files = []
    for name, content in ICONS.items():
        (SOURCES / f"{name}.svg").write_text(content, encoding="utf-8")
        files.append([content, str(TEXTURES / f"{name}.png")])
    subprocess.run(["node", "-e", RENDER, json.dumps({"size": SIZE, "files": files})], check=True)
    print("rendered", ", ".join(ICONS))


if __name__ == "__main__":
    main()
