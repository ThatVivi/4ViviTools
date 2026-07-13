#!/usr/bin/env python3
"""
Build a Vision Assist GRF from the user's own Ragnarok client data.

The generated GRF replaces monster SPR files with the same frames plus:
  - a red rectangle around the sprite canvas
  - small hue-coded cells in the top-left corner that encode the mob id

4ViviTools can then color-scan the live game frame instead of guessing with YOLO.
The output GRF always uses the standard "Master of Magic" header so an unpatched
client can load it.
"""
from __future__ import annotations

import argparse
import os
import json
import shutil
import struct
import subprocess
import sys
import zlib
from pathlib import Path
from typing import Iterable

_ROOT = Path(__file__).resolve().parent.parent.parent
_HERE = Path(__file__).resolve().parent
import sys as _sys
_BUNDLE = Path(getattr(_sys, "_MEIPASS", _HERE))   # PyInstaller extract dir when frozen

def _find_data(name: str) -> Path | None:
    """Locate a bundled/dev data file across dev tree, script dir, and frozen bundle."""
    for c in (_ROOT / "src/4rVivi.Core/Data" / name, _HERE / name, _BUNDLE / name,
              Path(name)):
        if c.exists():
            return c
    return None


def _configure_console() -> None:
    for stream in (sys.stdout, sys.stderr):
        try:
            stream.reconfigure(encoding="utf-8", errors="replace")
        except Exception:
            pass


_configure_console()


def _ensure(pkg: str, imp: str | None = None):
    try:
        return __import__(imp or pkg)
    except ImportError:
        print(f"[grf] installing {pkg} ...")
        subprocess.check_call([sys.executable, "-m", "pip", "install", "--quiet", pkg])
        return __import__(imp or pkg)


_ensure("Pillow", "PIL")
from PIL import Image, ImageDraw, ImageFont  # noqa: E402


BOX_RGBA = (255, 0, 0, 255)
BOX_PX = 2
CODE_CELL = 5
CODE_CELLS = 3
CODE_LEVELS = (48, 96, 144, 192, 240)
STANDARD_GRF_MAGIC = b"Master of Magic"


def _u16(data: bytes, off: int) -> int:
    return struct.unpack_from("<H", data, off)[0]


def _u32(data: bytes, off: int) -> int:
    return struct.unpack_from("<I", data, off)[0]


def _norm_internal(path: str) -> str:
    return path.replace("\\", "/").lstrip("/").lower()


def _decode_grf_name(raw: bytes) -> str:
    for enc in ("cp949", "euc-kr", "cp1252"):
        try:
            return raw.decode(enc)
        except UnicodeDecodeError:
            continue
    return raw.decode("cp1252", errors="replace")


def _encode_grf_name(name: str) -> bytes:
    for enc in ("cp949", "euc-kr", "cp1252"):
        try:
            return name.encode(enc)
        except UnicodeEncodeError:
            continue
    return name.encode("cp949", errors="replace")


def load_gamedata() -> dict[int, str]:
    p = _find_data("gamedata.json")
    if p is None:
        raise SystemExit("[grf] gamedata.json not found (dev tree, script dir, or bundle).")
    gd = json.loads(p.read_text(encoding="utf-8"))
    out: dict[int, str] = {}
    for m in gd.get("mobs", []):
        try:
            mob_id = int(m["id"])
        except (KeyError, ValueError, TypeError):
            continue
        out[mob_id] = str(m.get("name") or m.get("aegis") or f"mob_{mob_id}")
    return out


def load_sprite_map(path: Path | None, client: Path | None = None, auto: bool = True) -> dict[int, str]:
    p = path or _find_data("mobid_sprite_map.json") or (_HERE / "mobid_sprite_map.json")
    if not p.exists():
        if auto and client is not None:
            try:
                import build_sprite_map
                print(f"[grf] missing {p}; building it from client Lua data...")
                build_sprite_map.build_sprite_map(client, p)
            except Exception as e:
                raise SystemExit(
                    f"[grf] missing {p} and auto-build failed: {e}\n"
                    "[grf] run tools/vision-grf/build_sprite_map.py --client <RO root> first."
                ) from e
        else:
            raise SystemExit(
                f"[grf] missing {p}\n"
                "[grf] run tools/vision-grf/build_sprite_map.py --client <RO root> first."
            )
    raw = json.loads(p.read_text(encoding="utf-8"))
    return {int(k): str(v).replace("/", "\\") for k, v in raw.items()}


def scope_mob_ids(args, gamedata: dict[int, str], sprite_map: dict[int, str]) -> set[int]:
    if args.scope == "all":
        return set(sprite_map)

    maps = json.loads(Path(args.maps_json).read_text(encoding="utf-8"))
    wanted_maps = {m.strip().lower() for m in (args.map or []) if m.strip()}
    ids: set[int] = set()
    name_to_id = {v.lower(): k for k, v in gamedata.items()}
    for map_name, rows in maps.items():
        if wanted_maps and map_name.lower() not in wanted_maps:
            continue
        for row in rows if isinstance(rows, list) else []:
            mob_id = row.get("id") or row.get("mobId") or row.get("mob_id") or row.get("MobId")
            if mob_id is not None:
                try:
                    ids.add(int(mob_id))
                    continue
                except (ValueError, TypeError):
                    pass
            nm = str(row.get("name", "")).lower()
            if nm in name_to_id:
                ids.add(name_to_id[nm])
    return ids & set(sprite_map)


def _digits_base(n: int, width: int, base: int) -> list[int]:
    digits = [0] * width
    for i in range(width - 1, -1, -1):
        digits[i] = n % base
        n //= base
    return digits


def color_code(mob_id: int) -> list[tuple[int, int, int]]:
    """Lighting-tolerant base-5 code. Each cell has one dominant channel and two
    quantized payload channels, so the runtime can normalize against cave tint."""
    digits = _digits_base(mob_id, CODE_CELLS * 2, len(CODE_LEVELS))
    cells: list[tuple[int, int, int]] = []
    channels = ((255, 0, 0), (0, 255, 0), (0, 0, 255))
    for i in range(CODE_CELLS):
        a = CODE_LEVELS[digits[i * 2]]
        b = CODE_LEVELS[digits[i * 2 + 1]]
        if channels[i] == (255, 0, 0):
            cells.append((255, a, b))
        elif channels[i] == (0, 255, 0):
            cells.append((a, 255, b))
        else:
            cells.append((a, b, 255))
    return cells


NAME_FONT_SIZE = 15
NAME_STROKE_PX = 2
_NAME_FONT = None


def get_name_font(path: str | None = None):
    """Load a readable TTF for the baked name; fall back to Pillow's default."""
    global _NAME_FONT
    if _NAME_FONT is not None:
        return _NAME_FONT
    candidates = ([path] if path else []) + [
        "arialbd.ttf", "Arial Bold.ttf", "DejaVuSans-Bold.ttf", "arial.ttf", "DejaVuSans.ttf",
    ]
    for c in candidates:
        try:
            _NAME_FONT = ImageFont.truetype(c, NAME_FONT_SIZE)
            return _NAME_FONT
        except Exception:
            continue
    _NAME_FONT = ImageFont.load_default()
    return _NAME_FONT


def bake_frame(img: Image.Image, mob_id: int, name: str = "", font=None) -> Image.Image:
    """Bake the marker onto a frame:
      - the TRUE in-game monster name (from mob_db/gamedata) in a strip ABOVE the body
      - a tight red box around the body
      - the machine color-code inside the body corner
    The canvas is padded SYMMETRICALLY (equal top/bottom, equal left/right) so the sprite's
    center is unchanged -> every .act offset stays valid, no .act rewrite needed."""
    img = img.convert("RGBA")
    w, h = img.size
    if w < 1 or h < 1:
        return img                      # empty placeholder frame: nothing to bake
    font = font or get_name_font()

    # measure the name
    probe = ImageDraw.Draw(img)
    try:
        l, t, r, b = probe.textbbox((0, 0), name, font=font, stroke_width=NAME_STROKE_PX)
        tw, th = r - l, b - t
    except Exception:
        tw, th = len(name) * 7, NAME_FONT_SIZE
    strip = (th + 3) if name else 0

    new_w = max(w, tw + 4)
    new_h = h + 2 * strip            # top strip = bottom pad = strip  -> center preserved
    px = (new_w - w) // 2
    py = strip

    canvas = Image.new("RGBA", (new_w, new_h), (0, 0, 0, 0))
    canvas.alpha_composite(img, (px, py))
    d = ImageDraw.Draw(canvas)

    # red box tight around the body (guard tiny frames so x1>=x0 / y1>=y0 always holds)
    if w > 2 * BOX_PX and h > 2 * BOX_PX:
        for i in range(BOX_PX):
            d.rectangle([px + i, py + i, px + w - 1 - i, py + h - 1 - i], outline=BOX_RGBA)
    else:
        d.rectangle([px, py, px + w - 1, py + h - 1], outline=BOX_RGBA)
    # machine color-code inside the body top-left corner
    if w >= CODE_CELLS * CODE_CELL + 2 * BOX_PX and h >= CODE_CELL + 2 * BOX_PX:
        for idx, rgb in enumerate(color_code(mob_id)):
            x0 = px + BOX_PX + idx * CODE_CELL
            y0 = py + BOX_PX
            d.rectangle([x0, y0, x0 + CODE_CELL - 1, y0 + CODE_CELL - 1], fill=(*rgb, 255))
    # TRUE in-game name, centered in the top strip (white + black outline for contrast/OCR)
    if name:
        d.text(((new_w - tw) // 2, 1), name, font=font,
               fill=(255, 255, 255, 255), stroke_width=NAME_STROKE_PX, stroke_fill=(0, 0, 0, 255))
    return canvas


def bake_frames(frames, mob_id: int, name: str = "", font=None):
    """Bake a whole animation with ONE fixed marker: the box, name strip and color-code are
    identical on every frame (sized to the BIGGEST frame), so nothing jitters frame-to-frame.
    Frames are centered in a uniform canvas -> sprite center is preserved -> .act stays valid."""
    imgs = [f.convert("RGBA") for f in frames if f.width > 0 and f.height > 0]
    if not imgs:
        return []
    font = font or get_name_font()
    wb = max(i.width for i in imgs)
    hb = max(i.height for i in imgs)
    probe = ImageDraw.Draw(imgs[0])
    try:
        l, t, r, b = probe.textbbox((0, 0), name, font=font, stroke_width=NAME_STROKE_PX)
        tw, th = r - l, b - t
    except Exception:
        tw, th = len(name) * 7, NAME_FONT_SIZE
    strip = (th + 3) if name else 0
    wo = max(wb, tw + 4)
    ho = hb + 2 * strip
    # ONE fixed box around the max body region (centered), used on every frame
    bx0 = (wo - wb) // 2
    by0 = strip
    bx1 = bx0 + wb - 1
    by1 = by0 + hb - 1
    out = []
    for im in imgs:
        canvas = Image.new("RGBA", (wo, ho), (0, 0, 0, 0))
        canvas.alpha_composite(im, ((wo - im.width) // 2, (ho - im.height) // 2))  # centered -> .act valid
        d = ImageDraw.Draw(canvas)
        for i in range(BOX_PX):
            d.rectangle([bx0 + i, by0 + i, bx1 - i, by1 - i], outline=BOX_RGBA)   # fixed red box
        if wb >= CODE_CELLS * CODE_CELL + 2 * BOX_PX and hb >= CODE_CELL + 2 * BOX_PX:
            for idx, rgb in enumerate(color_code(mob_id)):                        # fixed color-code
                x0 = bx0 + BOX_PX + idx * CODE_CELL
                y0 = by0 + BOX_PX
                d.rectangle([x0, y0, x0 + CODE_CELL - 1, y0 + CODE_CELL - 1], fill=(*rgb, 255))
        if name:
            d.text(((wo - tw) // 2, 1), name, font=font,                          # fixed name at top
                   fill=(255, 255, 255, 255), stroke_width=NAME_STROKE_PX, stroke_fill=(0, 0, 0, 255))
        out.append(canvas)
    return out


def _flip_v(img: "Image.Image") -> "Image.Image":
    return img.transpose(Image.FLIP_TOP_BOTTOM)


def _swap_rb(img: "Image.Image") -> "Image.Image":
    # SPR truecolor pixels are stored BGRA; swap R and B to get true RGBA.
    b, g, r, a = img.split()
    return Image.merge("RGBA", (r, g, b, a))


def spr_to_frames(data: bytes) -> list[Image.Image]:
    if len(data) < 6 or data[:2] != b"SP":
        raise ValueError("not an SPR file")
    minor = data[2]
    major = data[3]
    version = major + minor / 10.0
    indexed_count = _u16(data, 4)
    off = 6
    rgba_count = 0
    if version >= 2.0:
        rgba_count = _u16(data, off)
        off += 2

    palette = data[-1024:] if indexed_count else b""
    frames: list[Image.Image] = []

    # --- indexed (type 0) frames: palette is R,G,B,reserved; index 0 = transparent ---
    for _ in range(indexed_count):
        w = _u16(data, off)
        h = _u16(data, off + 2)
        off += 4
        pixel_count = w * h
        indexes: list[int] = []
        if version >= 2.1:
            size = _u16(data, off)
            off += 2
            end = off + size
            while off < end and len(indexes) < pixel_count:
                c = data[off]
                off += 1
                if c == 0 and off < end:
                    run = data[off]
                    off += 1
                    indexes.extend([0] * max(1, run))
                else:
                    indexes.append(c)
            off = end
        else:
            indexes = list(data[off:off + pixel_count])
            off += pixel_count
        if len(indexes) < pixel_count:
            indexes.extend([0] * (pixel_count - len(indexes)))

        rgba = bytearray(pixel_count * 4)
        for i, idx in enumerate(indexes[:pixel_count]):
            po = idx * 4
            ro = i * 4
            if len(palette) >= po + 3:
                r = palette[po]; g = palette[po + 1]; b = palette[po + 2]   # palette is RGB order
            else:
                r = g = b = 0
            a = 0 if idx == 0 else 255                                       # index 0 = transparent
            rgba[ro] = r; rgba[ro + 1] = g; rgba[ro + 2] = b; rgba[ro + 3] = a
        img = Image.frombytes("RGBA", (w, h), bytes(rgba))
        frames.append(img)                                                  # indexed frames are top-down (no flip)

    # --- truecolor (type 1) frames: stored BGRA, bottom-up ---
    for _ in range(rgba_count):
        w = _u16(data, off)
        h = _u16(data, off + 2)
        off += 4
        size = w * h * 4
        img = Image.frombytes("RGBA", (w, h), data[off:off + size])
        img = _flip_v(_swap_rb(img))                                        # BGRA->RGBA + un-flip
        frames.append(img)
        off += size

    return frames


def frames_to_spr(frames: list[Image.Image]) -> bytes:
    valid = [f.convert("RGBA") for f in frames if f.width > 0 and f.height > 0]
    if not valid:
        raise ValueError("no frames to write")
    out = bytearray()
    out.extend(b"SP")
    out.extend(bytes([1, 2]))  # v2.1, truecolor frames
    out.extend(struct.pack("<H", 0))
    out.extend(struct.pack("<H", len(valid)))
    for img in valid:
        out.extend(struct.pack("<HH", img.width, img.height))
        # inverse of the read transform: re-flip to bottom-up, then RGBA->BGRA
        enc = _swap_rb(_flip_v(img))      # note: _swap_rb is its own inverse (swaps R/B back)
        out.extend(enc.tobytes("raw", "RGBA"))   # bytes now B,G,R,A = BGRA, bottom-up
    return bytes(out)


def _read_grf_table(grf_path: Path) -> dict[str, tuple[int, int, int, int]]:
    data = grf_path.read_bytes()
    if len(data) < 46:
        raise ValueError(f"{grf_path} is too small to be a GRF")
    magic = data[:15].decode("ascii", errors="ignore").rstrip("\0 ")
    if not magic:
        raise ValueError(f"{grf_path} has an empty GRF magic")
    table_offset = _u32(data, 30)
    seed = _u32(data, 34)
    raw_count = _u32(data, 38)
    count = max(0, raw_count - seed - 7)
    pos = 46 + table_offset
    comp_len = _u32(data, pos)
    real_len = _u32(data, pos + 4)
    table = zlib.decompress(data[pos + 8:pos + 8 + comp_len])
    if real_len and len(table) != real_len:
        print(f"[grf] warning: table size {len(table)} != declared {real_len}")

    entries: dict[str, tuple[int, int, int, int]] = {}
    off = 0
    for _ in range(count):
        end = table.index(0, off)
        name = _decode_grf_name(table[off:end])
        off = end + 1
        comp = _u32(table, off)
        aligned = _u32(table, off + 4)
        real = _u32(table, off + 8)
        flags = table[off + 12]
        entry_off = _u32(table, off + 13)
        off += 17
        entries[_norm_internal(name)] = (entry_off, comp, real, flags)
    return entries


_GRF_TABLE_CACHE: dict[str, dict[str, tuple[int, int, int, int]]] = {}


def read_grf_table_cached(grf_path: Path) -> dict[str, tuple[int, int, int, int]]:
    key = str(grf_path.resolve() if grf_path.exists() else grf_path).lower()
    table = _GRF_TABLE_CACHE.get(key)
    if table is None:
        table = _read_grf_table(grf_path)
        _GRF_TABLE_CACHE[key] = table
        print(f"[grf] indexed {grf_path} entries={len(table)}", flush=True)
    return table


def read_from_grf(grf_path: Path, internal: str) -> bytes:
    internal_norm = _norm_internal(internal)
    client = grf_path.parent
    extracted = [
        client / internal_norm,
        client / internal_norm.replace("data/", "", 1),
        client / "data" / internal_norm.replace("data/", "", 1),
    ]
    for p in extracted:
        if p.exists():
            return p.read_bytes()

    entries = read_grf_table_cached(grf_path)
    if internal_norm not in entries:
        raise FileNotFoundError(internal)
    entry_off, comp_len, real_len, flags = entries[internal_norm]
    if flags & 0x02:
        raise ValueError(f"{internal} is encrypted; extract with GRFEditor first")
    with grf_path.open("rb") as f:
        f.seek(46 + entry_off)
        blob = f.read(comp_len)
    out = zlib.decompress(blob)
    if real_len and len(out) != real_len:
        print(f"[grf] warning: {internal} size {len(out)} != declared {real_len}")
    return out


def parse_data_ini(client: Path) -> list[Path]:
    ini = client / "DATA.INI"
    if not ini.exists():
        ini = client / "data.ini"
    if not ini.exists():
        return []
    grfs: list[Path] = []
    for raw in ini.read_text(encoding="utf-8", errors="ignore").splitlines():
        line = raw.strip()
        if not line or line.startswith(("#", ";", "[")) or "=" not in line:
            continue
        _, value = line.split("=", 1)
        value = value.strip().strip('"')
        if value.lower().endswith(".grf"):
            p = Path(value)
            grfs.append(p if p.is_absolute() else client / p)
    return grfs


def source_grfs(client: Path, explicit: list[Path] | None) -> list[Path]:
    candidates: list[Path] = []
    if explicit:
        candidates.extend(explicit)
    else:
        candidates.extend(parse_data_ini(client))
        candidates.append(client / "data.grf")
    out: list[Path] = []
    seen = set()
    for p in candidates:
        if p.name.lower() == "visionassist.grf" and not explicit:
            continue
        key = str(p.resolve() if p.exists() else p).lower()
        if key not in seen and p.exists():
            seen.add(key)
            out.append(p)
    return out


def read_from_sources(grfs: list[Path], client: Path, internal: str) -> bytes:
    internal_norm = _norm_internal(internal)
    extracted = [
        client / internal_norm,
        client / internal_norm.replace("data/", "", 1),
        client / "data" / internal_norm.replace("data/", "", 1),
    ]
    for p in extracted:
        if p.exists():
            return p.read_bytes()

    last_error: Exception | None = None
    for grf in grfs:
        try:
            return read_from_grf(grf, internal)
        except Exception as e:
            last_error = e
    if last_error:
        raise last_error
    raise FileNotFoundError(internal)


def pack_grf(entries: dict[str, bytes], out_path: Path):
    """Write a GRF 0x200 (magic 'Master of Magic'). Header + body + file-table are written
    SEQUENTIALLY (no seek-back), so the container can never be left half-finalized, then the
    result is verified by re-parsing the header (not just the magic)."""
    out_path.parent.mkdir(parents=True, exist_ok=True)

    # 1) compress each entry into the body, record table rows (offsets are relative to byte 46)
    body = bytearray()
    rows: list[tuple[str, int, int, int, int]] = []
    for internal in sorted(entries):
        raw = entries[internal]
        comp = zlib.compress(raw, 9)
        offset = len(body)
        body.extend(comp)
        rows.append((internal.replace("/", "\\"), offset, len(comp), len(raw), 1))

    # 2) build + compress the file table
    table = bytearray()
    for name, offset, comp_len, real_len, flags in rows:
        table.extend(_encode_grf_name(name))
        table.append(0)
        table.extend(struct.pack("<III", comp_len, comp_len, real_len))
        table.append(flags)
        table.extend(struct.pack("<I", offset))
    table_comp = zlib.compress(bytes(table), 9)
    table_offset = len(body)                       # table starts right after the body

    # 3) write it all in one forward pass
    # write to a temp file first so a locked target never wastes the whole bake
    tmp = out_path.with_name(out_path.name + ".tmp")
    with tmp.open("wb") as f:
        f.write(b"Master of Magic".ljust(15, b"\0"))          # signature[15]
        f.write(b"\0" * 15)                                   # key[15] (zeros = standard)
        f.write(struct.pack("<IIII", table_offset, 0, len(rows) + 7, 0x200))  # offset/seed/count/version
        f.write(bytes(body))
        f.write(struct.pack("<II", len(table_comp), len(table)))
        f.write(table_comp)

    _verify_grf(tmp, len(rows))
    try:
        os.replace(tmp, out_path)                              # atomic swap into place
    except PermissionError:
        raise SystemExit(
            f"[grf] '{out_path.name}' is LOCKED (close GRFEditor and the RO client), then rename the "
            f"finished file:\n    {tmp}\n    -> {out_path}\n"
            f"[grf] the build itself succeeded — nothing was lost.")


def _verify_grf(path: Path, expect_count: int):
    """Re-parse the written header so a broken container is caught here, not in-game."""
    d = path.read_bytes()
    if d[:15] != b"Master of Magic":
        raise RuntimeError("[grf] verify: bad magic")
    table_offset, seed, raw_count, version = struct.unpack_from("<IIII", d, 30)
    if version != 0x200:
        raise RuntimeError(f"[grf] verify: version 0x{version:X} != 0x200")
    count = max(0, raw_count - seed - 7)
    if count != expect_count:
        raise RuntimeError(f"[grf] verify: count {count} != {expect_count}")
    pos = 46 + table_offset
    comp_len, real_len = struct.unpack_from("<II", d, pos)
    tbl = zlib.decompress(d[pos + 8:pos + 8 + comp_len])
    if real_len and len(tbl) != real_len:
        raise RuntimeError(f"[grf] verify: table size {len(tbl)} != {real_len}")
    print(f"[grf] verify OK: {count} entries, tableOffset={table_offset}, version=0x200")


def _to_lib(path: str) -> str:
    """Move data/sprite/<monster-folder>/x.spr -> data/sprite/visionassistant/x.spr,
    byte-agnostic about the Korean folder name (replaces the folder after 'sprite')."""
    bs = chr(92)
    sep = bs if bs in path else "/"
    parts = path.split(sep)
    for i in range(len(parts) - 1):
        if parts[i].lower() == "sprite" and i + 1 < len(parts) - 1:
            parts[i + 1] = "visionassistant"
            break
    return sep.join(parts)




def _sibling_act(path: str) -> str:
    p = path.replace("/", "\\")
    return p[:-4] + ".act" if p.lower().endswith(".spr") else p + ".act"


def build(args) -> int:
    gamedata = load_gamedata()
    client = Path(args.client)
    sprite_map = load_sprite_map(args.sprite_map, client=client, auto=not args.no_auto_sprite_map)
    targets = ({args.only} & set(sprite_map)) if getattr(args, 'only', None) else scope_mob_ids(args, gamedata, sprite_map)
    _font = get_name_font(getattr(args, 'font', None))
    print(f"[grf] baking {len(targets)} monsters (scope={args.scope})", flush=True)

    grfs = source_grfs(client, args.source_grf)
    if not grfs:
        raise SystemExit(f"[grf] no source GRFs found. Set DATA.INI or pass --source-grf <path>.")
    print("[grf] source GRFs:", flush=True)
    for g in grfs:
        print(f"  - {g}", flush=True)
    for g in grfs:
        read_grf_table_cached(g)

    out = args.out or (client / "VisionAssist.grf")
    manifest_path = args.manifest or (out.parent / "VisionAssist.manifest.json")
    manifest = {
        "version": 1,
        "codeCells": CODE_CELLS,
        "codeCell": CODE_CELL,
        "codeCellPx": CODE_CELL,
        "boxPx": BOX_PX,
        "boxColor": list(BOX_RGBA[:3]),
        "mobs": {},
    }
    entries: dict[str, bytes] = {}
    sprite_users: dict[str, list[int]] = {}
    baked = skipped = 0

    for mob_id in sorted(targets):
        spr_path = sprite_map.get(mob_id)
        name = gamedata.get(mob_id, f"mob_{mob_id}")  # TRUE in-game display name (mob_db), never the .spr filename
        if not spr_path:
            skipped += 1
            continue
        sprite_users.setdefault(_norm_internal(spr_path), []).append(mob_id)
        try:
            raw_spr = read_from_sources(grfs, client, spr_path)
            act_path = _sibling_act(spr_path)
            act_raw = read_from_sources(grfs, client, act_path)
            # Body animation is collapsed to one stable visible frame. The ACT keeps its action
            # slots but every action points to the same body + marker, so death/fall frames vanish.
            idxf, rgbaf, pal = spr_split(raw_spr)
            if not idxf and not rgbaf:
                raise ValueError("sprite has no frames")
            pal = bytearray(pal) if pal else bytearray(1024)
            body = _act_select_body_frame(act_raw, idxf, rgbaf)
            if body["type"] == 1:
                body_indexed = []
                body_rgba = [rgbaf[body["src_index"]]]
                marker_index = 0
                body_index = 0
            else:
                body_indexed = [idxf[body["src_index"]]]
                body_rgba = []
                marker_index = 1
                body_index = 0
            wb = int(body["w"]); hb = int(body["h"])
            used_indices = set()
            for _, _, idx_bytes in body_indexed:
                used_indices.update(idx_bytes)
            mw, mh, midx, pal2 = build_marker_index(marker_lines(mob_id, name), wb, hb, pal, _font, mob_id, used_indices)
            baked_bytes = spr_emit(body_indexed + [(mw, mh, midx)], body_rgba, pal2)
            bx, by = int(body["x"]), int(body["y"])
            act_bytes = _act_static_body_marker(act_raw, int(body["type"]), body_index, marker_index, (bx, by))
            library = getattr(args, "library", False)
            dest_spr = _to_lib(spr_path) if library else spr_path               # library -> visionassistant\ (몬스터 stays empty)
            dest_act = _to_lib(act_path) if library else act_path
            entries[dest_spr] = baked_bytes
            entries[dest_act] = act_bytes
            manifest["mobs"][str(mob_id)] = {"name": name, "sprite": spr_path, "code": color_code(mob_id)}
            baked += 1
            if baked % 100 == 0:
                print(f"[grf] baked {baked}/{len(targets)}", flush=True)
        except Exception as e:
            print(f"[grf] skip mob {mob_id} ({name}): {e}")
            skipped += 1

    if not entries:
        raise SystemExit("[grf] no sprites were baked; check --scope, --map, and mobid_sprite_map.json")

    if targets and skipped / max(1, len(targets)) > 0.20:
        raise SystemExit(f"[grf] refusing output: skipped {skipped}/{len(targets)} scoped mobs (>20%). Fix the sprite map first.")

    shared = {k: v for k, v in sprite_users.items() if len(v) > 1}
    if shared:
        print(f"[grf] warning: {len(shared)} shared sprite(s); first baked name wins in-game.")
        manifest["sharedSprites"] = {k: v for k, v in shared.items()}

    pack_grf(entries, out)
    assert out.read_bytes()[:15].rstrip(b"\0 ") == STANDARD_GRF_MAGIC, "[grf] output magic is not Master of Magic"
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    staging = out.parent / "_vision_staging"
    if staging.exists():
        shutil.rmtree(staging, ignore_errors=True)

    print(f"[grf] baked={baked} skipped={skipped}")
    print(f"[grf] wrote {out}")
    print(f"[grf] wrote {manifest_path}")
    print(f"[grf] DATA.INI: put {out.name} before the source GRFs, usually 0={out.name}")
    return 0


def selftest() -> int:
    import tempfile

    print(f"[grf] selftest constants boxPx={BOX_PX} codeCell={CODE_CELL} codeCells={CODE_CELLS}")
    with tempfile.TemporaryDirectory(prefix="4vivi-vision-grf-") as tmp:
        root = Path(tmp)
        mob_id = 1002
        img = Image.new("RGBA", (32, 36), (10, 20, 30, 255))
        spr = frames_to_spr([bake_frame(img, mob_id, "", get_name_font())])
        frames = spr_to_frames(spr)
        assert len(frames) == 1 and frames[0].size == (32, 36), "SPR round-trip failed"
        out = root / "VisionAssist.grf"
        korean_path = "data\\sprite\\몬스터\\poring.spr"
        pack_grf({korean_path: spr}, out)
        assert out.read_bytes()[:15].rstrip(b"\0 ") == STANDARD_GRF_MAGIC, "bad GRF magic"
        reread = read_from_grf(out, korean_path)
        assert reread == spr, "GRF read-back mismatch"
    print("[grf] selftest ok")
    return 0



# ===================== INDEXED bake path (keeps original palette -> exact colors) =====================
def spr_to_index_frames(data):
    """(frames, palette) with frames=[(w,h,bytearray idx)], palette=256*4 bytes. (None,None) if truecolor."""
    if data[:2] != b"SP":
        raise ValueError("not SPR")
    minor, major = data[2], data[3]; ver = major + minor / 10.0
    idx_c = _u16(data, 4); off = 6; rgba_c = 0
    if ver >= 2.0:
        rgba_c = _u16(data, off); off += 2
    if rgba_c > 0:
        return None, None
    pal = data[-1024:]
    frames = []
    for _ in range(idx_c):
        w = _u16(data, off); h = _u16(data, off + 2); off += 4; pc = w * h
        idxs = bytearray()
        if ver >= 2.1:
            size = _u16(data, off); off += 2; end = off + size
            while off < end and len(idxs) < pc:
                c = data[off]; off += 1
                if c == 0 and off < end:
                    run = data[off]; off += 1; idxs.extend(b"\x00" * max(1, run))
                else:
                    idxs.append(c)
            off = end
        else:
            idxs.extend(data[off:off + pc]); off += pc
        if len(idxs) < pc:
            idxs.extend(b"\x00" * (pc - len(idxs)))
        frames.append((w, h, idxs))
    return frames, bytearray(pal)


def _name_pixel_indices(name, font, wo, strip, white_idx, black_idx):
    img = Image.new("RGBA", (wo, max(1, strip)), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    try:
        l, t, r, b = d.textbbox((0, 0), name, font=font, stroke_width=NAME_STROKE_PX); tw = r - l
    except Exception:
        tw = len(name) * 7
    d.text(((wo - tw) // 2, 1), name, font=font, fill=(255, 255, 255, 255),
           stroke_width=NAME_STROKE_PX, stroke_fill=(0, 0, 0, 255))
    px = img.load(); out = {}
    for y in range(img.height):
        for x in range(wo):
            r, g, b, a = px[x, y]
            if a < 96:
                continue
            out[(x, y)] = white_idx if (r + g + b) > 300 else black_idx
    return out


def bake_index_frames(frames, palette, mob_id, name, font):
    font = font or get_name_font()
    wb = max(w for w, h, _ in frames); hb = max(h for w, h, _ in frames)
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    try:
        l, t, r, b = probe.textbbox((0, 0), name, font=font, stroke_width=NAME_STROKE_PX); tw, th = r - l, b - t
    except Exception:
        tw, th = len(name) * 7, NAME_FONT_SIZE
    strip = (th + 3) if name else 0
    wo = max(wb, tw + 4); ho = hb + 2 * strip
    used = set()
    for _, _, idxs in frames:
        used.update(idxs)
    free = [i for i in range(1, 256) if i not in used] or list(range(255, 247, -1))
    def take():
        return free.pop() if free else 255
    red_idx, white_idx, black_idx = take(), take(), take()
    palette[red_idx * 4:red_idx * 4 + 4] = bytes((255, 0, 0, 255))
    palette[white_idx * 4:white_idx * 4 + 4] = bytes((255, 255, 255, 255))
    palette[black_idx * 4:black_idx * 4 + 4] = bytes((0, 0, 0, 255))
    code_idx = []
    for (r, g, b) in color_code(mob_id):
        ci = take(); palette[ci * 4:ci * 4 + 4] = bytes((r, g, b, 255)); code_idx.append(ci)
    bx0 = (wo - wb) // 2; by0 = strip; bx1 = bx0 + wb - 1; by1 = by0 + hb - 1
    name_map = _name_pixel_indices(name, font, wo, strip, white_idx, black_idx) if name else {}
    out = []
    for (w, h, idxs) in frames:
        canvas = bytearray(wo * ho)               # all 0 = transparent
        ox = (wo - w) // 2; oy = (ho - h) // 2
        for y in range(h):
            base = (oy + y) * wo + ox
            canvas[base:base + w] = idxs[y * w:(y + 1) * w]
        for i in range(BOX_PX):                    # fixed red box
            for x in range(bx0 + i, bx1 - i + 1):
                canvas[(by0 + i) * wo + x] = red_idx; canvas[(by1 - i) * wo + x] = red_idx
            for y in range(by0 + i, by1 - i + 1):
                canvas[y * wo + bx0 + i] = red_idx; canvas[y * wo + bx1 - i] = red_idx
        if code_idx and wb >= CODE_CELLS * CODE_CELL + 2 * BOX_PX and hb >= CODE_CELL + 2 * BOX_PX:
            for k, ci in enumerate(code_idx):
                cx = bx0 + BOX_PX + k * CODE_CELL; cy = by0 + BOX_PX
                for yy in range(cy, cy + CODE_CELL):
                    for xx in range(cx, cx + CODE_CELL):
                        canvas[yy * wo + xx] = ci
        for (x, y), ci in name_map.items():
            if 0 <= x < wo and 0 <= y < ho:
                canvas[y * wo + x] = ci
        out.append((wo, ho, canvas))
    return out, palette


def index_frames_to_spr(frames, palette):
    out = bytearray(b"SP" + bytes([1, 2]))         # v2.1
    out += struct.pack("<H", len(frames)); out += struct.pack("<H", 0)
    for (w, h, idxs) in frames:
        out += struct.pack("<HH", w, h)
        rle = bytearray(); i = 0; n = len(idxs)
        while i < n:
            v = idxs[i]
            if v == 0:
                run = 1
                while i + run < n and idxs[i + run] == 0 and run < 255:
                    run += 1
                rle += bytes((0, run)); i += run
            else:
                rle.append(v); i += 1
        out += struct.pack("<H", len(rle)); out += rle
    out += bytes(palette[:1024])
    return bytes(out)



# ===================== marker-layer approach (leave body frames untouched) =====================
# element the player should ATTACK WITH (counter to the monster's defense element)
_COUNTER = {"Fire": "Water", "Water": "Wind", "Wind": "Earth", "Earth": "Fire",
            "Holy": "Shadow", "Shadow": "Holy", "Dark": "Holy", "Undead": "Holy",
            "Ghost": "Ghost", "Poison": "", "Neutral": ""}
_MOBMETA: dict[int, dict] = {}


def load_mobmeta() -> dict[int, dict]:
    if _MOBMETA:
        return _MOBMETA
    p = _find_data("gamedata.json")
    if p:
        for m in json.loads(p.read_text(encoding="utf-8")).get("mobs", []):
            try:
                _MOBMETA[int(m["id"])] = {"element": str(m.get("element", "")),
                                          "race": str(m.get("race", "")),
                                          "size": str(m.get("size", ""))}
            except (KeyError, ValueError, TypeError):
                pass
    return _MOBMETA


def marker_lines(mob_id: int, name: str) -> list[str]:
    """Two rows: [ '<name> - <use-element>', '<size> - <race>' ] (empty parts dropped)."""
    meta = load_mobmeta().get(mob_id, {})
    use = _COUNTER.get(meta.get("element", ""), "")
    line1 = f"{name} - {use}" if use else name
    line2 = " - ".join(p for p in (meta.get("size", ""), meta.get("race", "")) if p)
    return [x for x in (line1, line2) if x]


def build_marker_index(lines, wb, hb, palette, font, mob_id, used_indices=None):
    """One indexed marker image: multi-row label on top + red box (body-sized) + color-code.
    Symmetric vertical padding so the image center == the box center (place it at the body offset)."""
    font = font or get_name_font()
    lines = [x for x in lines if x]
    probe = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    def measure(t):
        try:
            l, tp, r, b = probe.textbbox((0, 0), t, font=font, stroke_width=NAME_STROKE_PX); return r - l, b - tp
        except Exception:
            return len(t) * 7, NAME_FONT_SIZE
    dims = [measure(t) for t in lines]
    tw = max((w for w, _ in dims), default=0)
    lh = (max((h for _, h in dims), default=NAME_FONT_SIZE)) + 2
    strip = lh * len(lines) + 3 if lines else 0
    box_w, box_h = wb, hb   # box == biggest frame
    wo = max(box_w, tw + 4); ho = box_h + 2 * strip
    used = set(used_indices or ())
    free = [i for i in range(255, 0, -1) if i not in used]
    needed = 3 + CODE_CELLS
    if len(free) < needed:
        raise ValueError(f"not enough free palette slots for marker ({len(free)} available, {needed} needed)")
    take = lambda: free.pop(0)
    pal = bytearray(palette)
    red_i, white_i, black_i = take(), take(), take()
    pal[red_i*4:red_i*4+4] = bytes((255, 0, 0, 255))
    pal[white_i*4:white_i*4+4] = bytes((255, 255, 255, 255))
    pal[black_i*4:black_i*4+4] = bytes((0, 0, 0, 255))
    code_i = []
    for (r, g, b) in color_code(mob_id):
        ci = take(); pal[ci*4:ci*4+4] = bytes((r, g, b, 255)); code_i.append(ci)
    bx0 = (wo - box_w) // 2; by0 = strip; bx1 = bx0 + box_w - 1; by1 = by0 + box_h - 1
    canvas = bytearray(wo * ho)
    for i in range(BOX_PX):
        for x in range(bx0 + i, bx1 - i + 1):
            canvas[(by0 + i) * wo + x] = red_i; canvas[(by1 - i) * wo + x] = red_i
        for y in range(by0 + i, by1 - i + 1):
            canvas[y * wo + bx0 + i] = red_i; canvas[y * wo + bx1 - i] = red_i
    if wb >= CODE_CELLS * CODE_CELL + 2 * BOX_PX and hb >= CODE_CELL + 2 * BOX_PX:
        for k, ci in enumerate(code_i):
            cx = bx0 + BOX_PX + k * CODE_CELL; cy = by0 + BOX_PX
            for yy in range(cy, cy + CODE_CELL):
                for xx in range(cx, cx + CODE_CELL):
                    canvas[yy * wo + xx] = ci
    if lines:
        img = Image.new("RGBA", (wo, strip), (0, 0, 0, 0)); d = ImageDraw.Draw(img)
        for i, t in enumerate(lines):
            wt = measure(t)[0]
            d.text(((wo - wt) // 2, 1 + i * lh), t, font=font, fill=(255, 255, 255, 255),
                   stroke_width=NAME_STROKE_PX, stroke_fill=(0, 0, 0, 255))
        px = img.load()
        for y in range(strip):
            for x in range(wo):
                r, g, b, a = px[x, y]
                if a < 96: continue
                canvas[y * wo + x] = white_i if (r + g + b) > 300 else black_i
    return wo, ho, canvas, pal


def _act_layer_size(ver):
    sz = 32
    if ver >= 2.4: sz += 4
    if ver >= 2.5: sz += 8
    return sz


def _act_parse(act):
    ver = act[3] + act[2] / 10.0; nact = _u16(act, 4); laysz = _act_layer_size(ver)
    o = 16; actions = []
    for _ in range(nact):
        nf = _u32(act, o); o += 4; frames = []
        for _ in range(nf):
            ranges = act[o:o + 32]; o += 32
            nl = _u32(act, o); o += 4
            layers = act[o:o + nl * laysz]; o += nl * laysz
            eventId = act[o:o + 4]; o += 4
            nanchor = 0; anc = b""
            if ver >= 2.3:
                nanchor = _u32(act, o); o += 4; anc = act[o:o + nanchor * 16]; o += nanchor * 16
            frames.append([ranges, nl, layers, eventId, nanchor, anc])
        actions.append(frames)
    return ver, laysz, act[:16], actions, act[o:]


def _act_first_layer_xy(act):
    ver, laysz, hdr, actions, trailing = _act_parse(act)
    for frames in actions:
        for fr in frames:
            if fr[1] > 0:
                x = struct.unpack_from("<i", fr[2], 0)[0]; y = struct.unpack_from("<i", fr[2], 4)[0]
                return x, y
    return 0, 0


def _act_add_marker(act, marker_idx, off):
    ver, laysz, hdr, actions, trailing = _act_parse(act)
    def layer(x, y, si):
        b = bytearray(laysz); struct.pack_into("<iiii", b, 0, x, y, si, 0); p = 16
        struct.pack_into("<I", b, p, 0xFFFFFFFF); p += 4
        struct.pack_into("<f", b, p, 1.0); p += 4
        if ver >= 2.4: struct.pack_into("<f", b, p, 1.0); p += 4
        struct.pack_into("<i", b, p, 0); p += 4
        struct.pack_into("<I", b, p, 0); p += 4
        if ver >= 2.5: struct.pack_into("<ii", b, p, 0, 0); p += 8
        return bytes(b)
    ml = layer(off[0], off[1], marker_idx)
    out = bytearray(hdr)
    for frames in actions:
        out += struct.pack("<I", len(frames))
        for ranges, nl, layers, eventId, nanchor, anc in frames:
            out += ranges
            if nl > 0:
                out += struct.pack("<I", nl + 1); out += layers; out += ml
            else:
                out += struct.pack("<I", nl); out += layers
            out += eventId
            if ver >= 2.3:
                out += struct.pack("<I", nanchor) + anc
    out += trailing
    return bytes(out)


def _act_select_body_frame(act, idxf, rgbaf) -> dict[str, int]:
    """Pick one stable visible body frame from the ACT.

    Prefer action0/frame0 because that is normally the standing pose. If it has
    no valid layer, scan the rest of the ACT and take the largest valid layer.
    The returned type/index refer to the original SPR split lists.
    """
    ver, laysz, hdr, actions, trailing = _act_parse(act)
    styp_off = 28 if ver < 2.4 else 32

    def candidates(action_limit=None, frame_limit=None):
        selected_actions = actions[:action_limit] if action_limit is not None else actions
        for frames in selected_actions:
            selected_frames = frames[:frame_limit] if frame_limit is not None else frames
            for fr in selected_frames:
                for L in range(fr[1]):
                    base = L * laysz
                    x = struct.unpack_from("<i", fr[2], base)[0]
                    y = struct.unpack_from("<i", fr[2], base + 4)[0]
                    si = struct.unpack_from("<i", fr[2], base + 8)[0]
                    typ = struct.unpack_from("<I", fr[2], base + styp_off)[0]
                    typ = 1 if typ == 1 else 0
                    seq = rgbaf if typ == 1 else idxf
                    if not (0 <= si < len(seq)):
                        continue
                    if typ == 1:
                        w = _u16(seq[si], 0); h = _u16(seq[si], 2)
                    else:
                        w, h, _ = seq[si]
                    yield {"type": typ, "src_index": si, "x": x, "y": y, "w": w, "h": h, "area": w * h}

    first_pose = list(candidates(1, 1))
    pool = first_pose or list(candidates())
    if not pool:
        raise ValueError("ACT has no valid sprite layers")
    return max(pool, key=lambda c: c["area"])


def _act_static_body_marker(act, body_type, body_index, marker_index, off):
    """Collapse every ACT action to one frame that draws the same body + marker.

    The SPR is reduced to one body frame plus one marker frame. Keeping one frame
    per ACT action preserves client expectations while removing movement/death
    animation visually.
    """
    ver, laysz, hdr, actions, trailing = _act_parse(act)

    def layer(x, y, si, typ):
        b = bytearray(laysz)
        struct.pack_into("<iiii", b, 0, x, y, si, 0)
        p = 16
        struct.pack_into("<I", b, p, 0xFFFFFFFF); p += 4
        struct.pack_into("<f", b, p, 1.0); p += 4
        if ver >= 2.4:
            struct.pack_into("<f", b, p, 1.0); p += 4
        struct.pack_into("<i", b, p, 0); p += 4
        struct.pack_into("<I", b, p, 1 if body_type == 1 and typ == 1 else 0); p += 4
        if ver >= 2.5:
            struct.pack_into("<ii", b, p, 0, 0); p += 8
        return bytes(b)

    body_layer = layer(off[0], off[1], body_index, 1 if body_type == 1 else 0)
    marker_layer = layer(off[0], off[1], marker_index, 0)
    static_layers = body_layer + marker_layer

    out = bytearray(hdr)
    for frames in actions:
        if frames:
            ranges, _, _, event_id, nanchor, anc = frames[0]
        else:
            ranges, event_id, nanchor, anc = bytes(32), bytes(4), 0, b""
        out += struct.pack("<I", 1)
        out += ranges
        out += struct.pack("<I", 2)
        out += static_layers
        out += event_id
        if ver >= 2.3:
            out += struct.pack("<I", nanchor) + anc
    out += trailing
    return bytes(out)



def spr_split(data):
    """(indexed=[(w,h,bytearray idx)], rgba_raw=[bytes per frame incl w,h,pixels], palette|None).
    rgba frames are kept RAW so their original colors are preserved byte-for-byte."""
    if data[:2] != b"SP":
        raise ValueError("not SPR")
    minor, major = data[2], data[3]; ver = major + minor / 10.0
    idx_c = _u16(data, 4); off = 6; rgba_c = 0
    if ver >= 2.0:
        rgba_c = _u16(data, off); off += 2
    pal = data[-1024:] if idx_c > 0 else None
    idxf = []
    for _ in range(idx_c):
        w = _u16(data, off); h = _u16(data, off + 2); off += 4; pc = w * h
        idxs = bytearray()
        if ver >= 2.1:
            size = _u16(data, off); off += 2; end = off + size
            while off < end and len(idxs) < pc:
                c = data[off]; off += 1
                if c == 0 and off < end:
                    run = data[off]; off += 1; idxs.extend(b"\x00" * max(1, run))
                else:
                    idxs.append(c)
            off = end
        else:
            idxs.extend(data[off:off + pc]); off += pc
        if len(idxs) < pc:
            idxs.extend(b"\x00" * (pc - len(idxs)))
        idxf.append((w, h, idxs))
    rgbaf = []
    for _ in range(rgba_c):
        start = off; w = _u16(data, off); h = _u16(data, off + 2); off += 4 + w * h * 4
        rgbaf.append(data[start:off])            # raw: w,h,pixels
    return idxf, rgbaf, pal


def spr_emit(indexed, rgba_raw, palette):
    """SPR v2.1 = indexed (RLE) + rgba (raw) + palette."""
    out = bytearray(b"SP" + bytes([1, 2]))
    out += struct.pack("<H", len(indexed)); out += struct.pack("<H", len(rgba_raw))
    for (w, h, idxs) in indexed:
        out += struct.pack("<HH", w, h)
        rle = bytearray(); i = 0; n = len(idxs)
        while i < n:
            v = idxs[i]
            if v == 0:
                run = 1
                while i + run < n and idxs[i + run] == 0 and run < 255:
                    run += 1
                rle += bytes((0, run)); i += run
            else:
                rle.append(v); i += 1
        out += struct.pack("<H", len(rle)); out += rle
    for raw in rgba_raw:
        out += raw
    out += bytes((bytes(palette) if palette else bytes(1024))[:1024])
    return bytes(out)



def _act_body_offset(act, idxf, rgbaf):
    """(x,y) of the largest sprite layer in action0/frame0 = the body's center, so the marker box
    is placed on the monster (not on a small accessory layer)."""
    ver, laysz, hdr, actions, trailing = _act_parse(act)
    styp_off = 28 if ver < 2.4 else 32
    best = None; best_area = -1
    for frames in actions[:1]:
        for fr in frames[:1]:
            for L in range(fr[1]):
                base = L * laysz
                x = struct.unpack_from("<i", fr[2], base)[0]
                y = struct.unpack_from("<i", fr[2], base + 4)[0]
                si = struct.unpack_from("<i", fr[2], base + 8)[0]
                typ = struct.unpack_from("<I", fr[2], base + styp_off)[0]
                seq = rgbaf if typ == 1 else idxf
                if 0 <= si < len(seq):
                    if typ == 1:
                        w = _u16(seq[si], 0); h = _u16(seq[si], 2)
                    else:
                        w, h, _ = seq[si]
                    if w * h > best_area:
                        best_area = w * h; best = (x, y)
    return best or (0, 0)


def main() -> int:
    ap = argparse.ArgumentParser(description="Generate VisionAssist.grf from user-owned Ragnarok client files.")
    ap.add_argument("--selftest", action="store_true", help="Run a synthetic bake/pack/read validation and exit.")
    ap.add_argument("--client", type=Path, help="RO client root.")
    ap.add_argument("--source-grf", type=Path, action="append", help="Source GRF. Can repeat. Defaults to DATA.INI entries then <client>/data.grf.")
    ap.add_argument("--scope", choices=("map", "all"), default="map")
    ap.add_argument("--only", type=int, help="Bake just this one mob id (fast test).")
    ap.add_argument("--library", action="store_true", help="Two-folder library: bake into visionassistant\\ (몬스터 stays empty) for the picker to promote from.")
    ap.add_argument("--font", default=None, help="TTF for the baked name (default: Arial/DejaVu).")
    ap.add_argument("--map", action="append", default=[], help="Only bake this map when --scope map. Can repeat.")
    ap.add_argument("--maps-json", default=str(_ROOT / "src/4rVivi.Core/Data/map_mobs.json"))
    ap.add_argument("--sprite-map", type=Path, help="mobId -> internal SPR path json. Defaults next to this script.")
    ap.add_argument("--no-auto-sprite-map", action="store_true", help="Do not auto-build mobid_sprite_map.json when missing.")
    ap.add_argument("--out", type=Path, help="Output GRF. Defaults to <client>/VisionAssist.grf.")
    ap.add_argument("--manifest", type=Path, help="Output manifest. Defaults next to output GRF.")
    args = ap.parse_args()
    if args.selftest:
        return selftest()
    if args.client is None:
        ap.error("--client is required unless --selftest is used")
    return build(args)


if __name__ == "__main__":
    raise SystemExit(main())
