#!/usr/bin/env python3
"""
build_training_set.py -- render the FULL OCR training set into user_images/.

Renders every game string the reader must recognize, as labeled crops in the RO fonts:
  text : skills, monsters, items, classes, maps, hairstyles, movements/states, buffs, debuffs, HUD labels
  nums : HP, SP, AP, EXP (value + percent), base level, job level, zeny, weight, X/Y position, player name

Output (in --out, default user_images/):
  crops/*.png  rec_gt.txt  train_list.txt  val_list.txt  manifest.json

Single run:   python build_training_set.py --out user_images
Sliced runs:  python build_training_set.py --out user_images --parts 8 --part 0   (then 1..7)
              python build_training_set.py --out user_images --finalize
"""
import argparse, glob, json, os, random
from PIL import Image, ImageDraw, ImageFont, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.join(HERE, "corpus")

BUFFS = ["Blessing","Increase AGI","Decrease AGI","Endure","Concentration","Magnificat","Gloria",
    "Kyrie Eleison","Assumptio","Impositio Manus","Suffragium","Aspersio","Angelus","Wind Walk",
    "Cart Boost","Adrenaline Rush","Weapon Perfection","Maximize Power","Two-Hand Quicken","Spear Quicken",
    "Berserk","Provoke","Loud Exclamation","True Sight","Sight","Ruwach","Energy Coat","Steel Body",
    "Fury","Mental Strength","Soul Link","Battle Chant","Magic Strings","Apple of Idun","Poem of Bragi",
    "Assassin Cross of Sunset","Service for You","Eternal Chaos","Power-Up","Speed Potion","Awakening",
    "Berserk Potion","Food Atk","Food Matk","Food Hit","Food Crit","Food Flee","Mvp Buff","Guild Aura"]
DEBUFFS = ["Poison","Deadly Poison","Curse","Stun","Freeze","Frozen","Sleep","Blind","Silence","Stone",
    "Petrified","Bleeding","Confusion","Chaos","Burning","Frost","Crystallize","Deep Sleep","Fear",
    "Hallucination","Decrease AGI","Quagmire","Slow Grace","Mandragora Howling","Marsh of Abyss",
    "Lex Aeterna","Lex Divina","Stop","Spider Web","Ankle Snare","Frost Joke","Scream","Hell Inferno"]
NAMES = ["Vivi","DarkLord","Aizen","ProtoVivi","GMVivi","Eldrynn","Sora","Kael","Mira","Zen","Nyx","Auron"]

def _load(name):
    try:
        with open(os.path.join(CORPUS, name), encoding="utf-8") as f:
            return [ln.strip() for ln in f if ln.strip()]
    except Exception:
        return []

CORP = {
    "SkillName": _load("skills.txt"), "Monster": _load("monsters.txt"), "ItemName": _load("items.txt"),
    "ClassName": _load("classes.txt"), "MapName": _load("maps.txt"), "HairStyle": _load("hairstyles.txt"),
    "Movement": _load("movements.txt"), "HUD": _load("hud.txt"), "Buff": BUFFS, "Debuff": DEBUFFS,
}

def _num(lo, hi): return str(random.randint(lo, hi))
def _pair(lo, hi):
    mx = random.randint(lo, hi); cur = random.randint(0, mx)
    return random.choice(["%d/%d","%d / %d","%d /%d","%d/ %d"]) % (cur, mx)
def _pct(): return "%d.%02d%%" % (random.randint(0,100), random.randint(0,99))

NUMERIC = {
    "HP": lambda: _pair(50, 999999), "SP": lambda: _pair(10, 99999), "AP": lambda: _pair(0, 200),
    "EXPval": lambda: _num(0, 2000000000), "EXPpct": _pct,
    "BaseLevel": lambda: _num(1, 999), "JobLevel": lambda: _num(1, 70),
    "Zeny": lambda: _num(0, 2000000000), "Weight": lambda: _pair(0, 30000),
    "PosX": lambda: _num(0, 400), "PosY": lambda: _num(0, 400),
    "Position": lambda: "%s %d, %d" % (random.choice(CORP['MapName'] or ['prontera']), random.randint(0,400), random.randint(0,400)),
    "CharName": lambda: random.choice(NAMES),
}

WEIGHTS = {
    "ItemName": 2500, "Monster": 3000, "SkillName": 2600, "HUD": 1500, "Buff": 1300, "Debuff": 1300,
    "ClassName": 900, "Movement": 700, "MapName": 500, "HairStyle": 450,
    "HP": 700, "SP": 700, "AP": 400, "EXPval": 500, "EXPpct": 500, "BaseLevel": 500, "JobLevel": 400,
    "Zeny": 500, "Weight": 400, "PosX": 250, "PosY": 250, "Position": 500, "CharName": 450,
}

_WIN_FONTS = ["gulim.ttc","arial.ttf","arialbd.ttf","tahoma.ttf","tahomabd.ttf","verdana.ttf","micross.ttf","cour.ttf","msgothic.ttc","segoeui.ttf"]
def _fonts(fonts_dir):
    p = []
    for ext in ("*.ttf", "*.TTF", "*.otf", "*.OTF", "*.ttc", "*.TTC"):
        p += glob.glob(os.path.join(fonts_dir, "**", ext), recursive=True)
    win = os.path.join(os.environ.get("WINDIR", r"C:\\Windows"), "Fonts")
    if os.path.isdir(win):
        for f in _WIN_FONTS:
            fp = os.path.join(win, f)
            if os.path.exists(fp): p.append(fp)
    return p

_FG = [(255,255,255),(255,255,0),(110,255,110),(120,200,255),(255,180,90),(255,110,110),(230,230,230),(200,220,255)]
def _bggrad(w, h, c0, c1):
    im = Image.new("RGB", (w, h)); px = im.load()
    for y in range(h):
        t = y / max(1, h - 1)
        r = int(c0[0]+(c1[0]-c0[0])*t); g = int(c0[1]+(c1[1]-c0[1])*t); b = int(c0[2]+(c1[2]-c0[2])*t)
        for x in range(w): px[x, y] = (r, g, b)
    return im
def _bg(w, h):
    k = random.random()
    if k < 0.30: c = random.randint(8, 40); return Image.new("RGB", (w, h), (c, c, c+random.randint(0,10)))
    if k < 0.55: return _bggrad(w, h, (180,200,230), (120,150,200))
    if k < 0.75: c = random.randint(200, 255); return Image.new("RGB", (w, h), (c, c, c))
    im = _bggrad(w, h, tuple(random.randint(20,120) for _ in range(3)), tuple(random.randint(20,140) for _ in range(3)))
    px = im.load()
    for _ in range((w*h)//8):
        x = random.randint(0, w-1); y = random.randint(0, h-1); d = random.randint(-30, 30); r, g, b = px[x, y]
        px[x, y] = (max(0,min(255,r+d)), max(0,min(255,g+d)), max(0,min(255,b+d)))
    return im

_ZW = ('\u200b','\u200c','\u200d','\ufeff')
def _clean(t):
    return ''.join(c for c in t if c not in _ZW).strip()

def _sample(role, n):
    if role in NUMERIC:
        return [NUMERIC[role]() for _ in range(n)]
    pool = CORP.get(role) or []
    if not pool:
        return []
    random.shuffle(pool)
    # render up to the weight cap; if the pool is larger we take a random subset
    # (the rec model learns CHARACTERS/fonts, not every unique string -> caps keep training fast)
    target = min(n, len(pool)) if len(pool) >= n else n
    out = list(pool)
    while len(out) < target:
        out.append(random.choice(pool))
    return [_clean(x) for x in out[:target]]

def render(text, fonts, out_path):
    size = random.choice([9,10,11,11,12,12,13,14,16,18,22])   # weighted small (RO HUD)
    fp = random.choice(fonts) if fonts else None
    try:
        if fp and fp.lower().endswith(".ttc"):
            font = ImageFont.truetype(fp, size, index=random.randint(0, 3))
        elif fp:
            font = ImageFont.truetype(fp, size)
        else:
            font = ImageFont.load_default()
    except Exception:
        try: font = ImageFont.truetype(fp, size)
        except Exception: font = ImageFont.load_default()
    d0 = ImageDraw.Draw(Image.new("RGB", (4, 4)))
    stroke = random.choice([0, 1, 1, 2])
    try: bb = d0.textbbox((0, 0), text, font=font, stroke_width=stroke)
    except TypeError: stroke = 0; bb = d0.textbbox((0, 0), text, font=font)
    tw, th = bb[2]-bb[0], bb[3]-bb[1]
    pad = random.randint(2, 7)
    W, H = max(2, tw + pad*2), max(2, th + pad*2)
    im = _bg(W, H); dr = ImageDraw.Draw(im)
    ox, oy = pad - bb[0], pad - bb[1]
    if random.random() < 0.6: dr.text((ox+1, oy+1), text, font=font, fill=(0,0,0))   # shadow
    fg = random.choice(_FG); outline = (0,0,0) if sum(fg) > 200 else (255,255,255)
    try: dr.text((ox, oy), text, font=font, fill=fg, stroke_width=stroke, stroke_fill=outline)
    except TypeError: dr.text((ox, oy), text, font=font, fill=fg)
    if random.random() < 0.5:
        sc = random.uniform(0.6, 0.95)
        im = im.resize((max(2,int(W*sc)), max(2,int(H*sc))), Image.BILINEAR).resize((W, H), Image.BILINEAR)
    if random.random() < 0.35:
        im = im.filter(ImageFilter.GaussianBlur(random.uniform(0.3, 0.9)))
    im.convert("RGB").save(out_path)

def _plan(total, seed):
    random.seed(seed)
    weights = dict(WEIGHTS)
    if total > 0:
        s = sum(weights.values()); weights = {k: max(1, round(v * total / s)) for k, v in weights.items()}
    plan = []
    for role, n in weights.items():
        for text in _sample(role, n):
            plan.append((text, role))
    random.seed(seed + 1); random.shuffle(plan)
    return plan

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(HERE, "user_images"))
    ap.add_argument("--fonts", default=os.path.join(HERE, "fonts"))
    ap.add_argument("--total", type=int, default=0)
    ap.add_argument("--val-frac", type=float, default=0.04)
    ap.add_argument("--seed", type=int, default=7)
    ap.add_argument("--part", type=int, default=0)
    ap.add_argument("--parts", type=int, default=1)
    ap.add_argument("--finalize", action="store_true")
    a = ap.parse_args()

    fonts = _fonts(a.fonts)
    os.makedirs(os.path.join(a.out, "crops"), exist_ok=True)
    plan = _plan(a.total, a.seed)
    rows = [("crops/s_%06d.png" % i, text, role) for i, (text, role) in enumerate(plan)]

    if a.finalize:
        random.seed(a.seed + 2); order = rows[:]; random.shuffle(order)
        lines = ["%s\t%s" % (r, t) for r, t, _ in order]
        open(os.path.join(a.out, "rec_gt.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
        k = max(1, int(len(lines) * a.val_frac))
        open(os.path.join(a.out, "val_list.txt"), "w", encoding="utf-8").write("\n".join(lines[:k]) + "\n")
        open(os.path.join(a.out, "train_list.txt"), "w", encoding="utf-8").write("\n".join(lines[k:]) + "\n")
        man = {}
        for _, _, role in rows:
            man[role] = man.get(role, 0) + 1
        json.dump({"total": len(lines), "val": k, "train": len(lines) - k, "by_category": man},
                  open(os.path.join(a.out, "manifest.json"), "w"), indent=2)
        print(json.dumps({"total": len(lines), "categories": man}, indent=2))
        return

    random.seed(a.seed * 1000 + a.part)
    sl = rows[a.part::a.parts]
    for rel, text, _ in sl:
        render(text, fonts, os.path.join(a.out, rel))
    print("part %d/%d: rendered %d of %d" % (a.part, a.parts, len(sl), len(rows)))

if __name__ == "__main__":
    main()
