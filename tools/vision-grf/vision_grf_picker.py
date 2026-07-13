#!/usr/bin/env python3
"""
vision_grf_picker.py -- pick which monsters get a Vision Assist box, build a small GRF.

Small window, two lists:
  LEFT  = all monsters found in the source GRF (searchable)
  RIGHT = your focus monsters (only these get the red box + real name in-game)

Apply bakes ONLY the focus monsters and packs them into VisionAssist.grf. Because that GRF
loads first and only overrides the sprites it contains, every non-focus monster still renders
normally from your data.grf. A one-time bake CACHE makes re-Apply fast.

Requires: build_vision_grf.py next to this file (reuses its bake/pack/read functions),
gamedata.json (names) and mobid_sprite_map.json (mobId -> sprite path).
Run:  python vision_grf_picker.py
"""
from __future__ import annotations

import json
import sys
import threading
import traceback
from pathlib import Path

import tkinter as tk
from tkinter import filedialog, messagebox, ttk

HERE = Path(__file__).resolve().parent
# When frozen by PyInstaller, data + modules live in the extraction dir (sys._MEIPASS).
BASE = Path(getattr(sys, "_MEIPASS", HERE))
# writable dirs go next to the exe (not the temp extract dir) when frozen
APPDIR = Path(sys.executable).resolve().parent if getattr(sys, "frozen", False) else HERE
CONFIG = APPDIR / "picker_config.json"
CACHE = APPDIR / "_cache"                    # baked .spr per mob id (the "assistantvision" store)

# import the shared generator as a real module so PyInstaller bundles its deps (Pillow, etc.)
sys.path.insert(0, str(BASE))
sys.path.insert(0, str(HERE))
import build_vision_grf as bvg  # noqa: E402


class PickerApp:
    def __init__(self, root: tk.Tk):
        self.root = root
        root.title("4ViviTools - Vision Assist Monster Picker")
        root.geometry("900x560")

        self.cfg = self._load_cfg()
        self.gamedata: dict[int, str] = {}
        self.sprite_map: dict[int, str] = {}
        self.all_ids: list[int] = []         # bakeable mob ids (have a sprite)
        self.focus_ids: list[int] = list(self.cfg.get("focus", []))
        self.font = bvg.get_name_font()

        self._build_ui()
        self._load_catalog()

    # ---------- config ----------
    def _load_cfg(self) -> dict:
        try:
            return json.loads(CONFIG.read_text(encoding="utf-8"))
        except Exception:
            return {}

    def _save_cfg(self):
        self.cfg["focus"] = self.focus_ids
        self.cfg["source"] = self.src_var.get()
        self.cfg["out"] = self.out_var.get()
        try:
            CONFIG.write_text(json.dumps(self.cfg, indent=2), encoding="utf-8")
        except Exception:
            pass

    # ---------- ui ----------
    def _build_ui(self):
        top = ttk.Frame(self.root, padding=8)
        top.pack(fill="x")
        ttk.Label(top, text="Source GRF (your clean data.grf or box GRF):").grid(row=0, column=0, sticky="w")
        self.src_var = tk.StringVar(value=self.cfg.get("source", ""))
        ttk.Entry(top, textvariable=self.src_var, width=70).grid(row=1, column=0, sticky="we")
        ttk.Button(top, text="Browse", command=self._browse_src).grid(row=1, column=1, padx=4)
        ttk.Button(top, text="Load monsters", command=self._load_catalog).grid(row=1, column=2, padx=4)

        ttk.Label(top, text="Output GRF:").grid(row=2, column=0, sticky="w", pady=(6, 0))
        self.out_var = tk.StringVar(value=self.cfg.get("out", str(HERE / "output" / "VisionAssist.grf")))
        ttk.Entry(top, textvariable=self.out_var, width=70).grid(row=3, column=0, sticky="we")
        ttk.Button(top, text="Browse", command=self._browse_out).grid(row=3, column=1, padx=4)
        top.columnconfigure(0, weight=1)

        mid = ttk.Frame(self.root, padding=8)
        mid.pack(fill="both", expand=True)

        # left: all monsters + search
        left = ttk.Frame(mid)
        left.pack(side="left", fill="both", expand=True)
        ttk.Label(left, text="All monsters").pack(anchor="w")
        self.search_var = tk.StringVar()
        self.search_var.trace_add("write", lambda *_: self._refresh_all())
        ttk.Entry(left, textvariable=self.search_var).pack(fill="x")
        self.all_list = tk.Listbox(left, selectmode="extended")
        self.all_list.pack(fill="both", expand=True)
        self.all_list.bind("<Double-Button-1>", lambda e: self._add())

        # middle buttons
        btns = ttk.Frame(mid)
        btns.pack(side="left", fill="y", padx=6)
        ttk.Button(btns, text=">>  Add", command=self._add).pack(pady=8)
        ttk.Button(btns, text="<<  Remove", command=self._remove).pack(pady=8)
        ttk.Button(btns, text="Clear", command=self._clear_focus).pack(pady=8)

        # right: focus
        right = ttk.Frame(mid)
        right.pack(side="left", fill="both", expand=True)
        ttk.Label(right, text="Focus monsters (get the box)").pack(anchor="w")
        self.focus_list = tk.Listbox(right, selectmode="extended")
        self.focus_list.pack(fill="both", expand=True)
        self.focus_list.bind("<Double-Button-1>", lambda e: self._remove())

        # bottom: apply + log
        bot = ttk.Frame(self.root, padding=8)
        bot.pack(fill="x")
        self.rebuild_cache = tk.BooleanVar(value=False)
        ttk.Checkbutton(bot, text="Rebuild cache (re-bake even if cached)", variable=self.rebuild_cache).pack(side="left")
        self.apply_btn = ttk.Button(bot, text="APPLY  ->  build VisionAssist.grf", command=self._apply)
        self.apply_btn.pack(side="right")
        self.status = tk.StringVar(value="Load a source GRF, then pick monsters.")
        ttk.Label(self.root, textvariable=self.status, relief="sunken", anchor="w").pack(fill="x", side="bottom")

    def _browse_src(self):
        p = filedialog.askopenfilename(title="Source GRF", filetypes=[("GRF", "*.grf"), ("All", "*.*")])
        if p:
            self.src_var.set(p)

    def _browse_out(self):
        p = filedialog.asksaveasfilename(title="Output GRF", defaultextension=".grf",
                                         initialfile="VisionAssist.grf", filetypes=[("GRF", "*.grf")])
        if p:
            self.out_var.set(p)

    # ---------- catalog ----------
    def _load_catalog(self):
        try:
            self.gamedata = bvg.load_gamedata()
            self.sprite_map = bvg.load_sprite_map(None, client=None, auto=False)
        except Exception as e:
            messagebox.showerror("Load failed", f"Could not load gamedata/sprite map:\n{e}")
            return
        # bakeable = mobs that have a sprite path AND a name
        self.all_ids = sorted(i for i in self.sprite_map if i in self.gamedata)
        self._refresh_all()
        self._refresh_focus()
        self.status.set(f"{len(self.all_ids)} monsters available. {len(self.focus_ids)} in focus.")

    def _label(self, mid: int) -> str:
        return f"{self.gamedata.get(mid, 'mob_%d' % mid)}  (#{mid})"

    def _refresh_all(self):
        q = self.search_var.get().strip().lower()
        self.all_list.delete(0, "end")
        self._all_shown = []
        for mid in self.all_ids:
            if mid in self.focus_ids:
                continue
            label = self._label(mid)
            if q and q not in label.lower():
                continue
            self.all_list.insert("end", label)
            self._all_shown.append(mid)

    def _refresh_focus(self):
        self.focus_list.delete(0, "end")
        for mid in self.focus_ids:
            self.focus_list.insert("end", self._label(mid))

    def _add(self):
        for i in self.all_list.curselection():
            mid = self._all_shown[i]
            if mid not in self.focus_ids:
                self.focus_ids.append(mid)
        self._refresh_all(); self._refresh_focus(); self._save_cfg()
        self.status.set(f"{len(self.focus_ids)} monsters in focus.")

    def _remove(self):
        keep = [self.focus_ids[i] for i in range(len(self.focus_ids))
                if i not in self.focus_list.curselection()]
        self.focus_ids = keep
        self._refresh_all(); self._refresh_focus(); self._save_cfg()

    def _clear_focus(self):
        self.focus_ids = []
        self._refresh_all(); self._refresh_focus(); self._save_cfg()

    # ---------- apply ----------
    def _apply(self):
        if not self.focus_ids:
            messagebox.showwarning("Nothing selected", "Add at least one monster to the focus list.")
            return
        src = self.src_var.get().strip()
        if not src or not Path(src).exists():
            messagebox.showwarning("No source GRF", "Pick a valid source GRF first.")
            return
        self.apply_btn.config(state="disabled")
        self._save_cfg()
        threading.Thread(target=self._apply_worker, daemon=True).start()

    def _apply_worker(self):
        try:
            src = Path(self.src_var.get().strip())
            out = Path(self.out_var.get().strip())
            out.parent.mkdir(parents=True, exist_ok=True)
            CACHE.mkdir(exist_ok=True)
            grfs = [src]
            client = src.parent
            bvg.read_grf_table_cached(src)

            entries: dict[str, bytes] = {}
            manifest = {"version": 1, "codeCells": bvg.CODE_CELLS, "codeCell": bvg.CODE_CELL,
                        "boxPx": bvg.BOX_PX, "boxColor": list(bvg.BOX_RGBA[:3]), "mobs": {}}
            done = 0
            total = len(self.focus_ids)
            for mid in self.focus_ids:
                spr_path = self.sprite_map.get(mid)
                name = self.gamedata.get(mid, f"mob_{mid}")
                if not spr_path:
                    continue
                cache_spr = CACHE / f"{mid}.spr"
                try:
                    if cache_spr.exists() and not self.rebuild_cache.get():
                        baked = cache_spr.read_bytes()
                    else:
                        frames = bvg.spr_to_frames(bvg.read_from_sources(grfs, client, spr_path))
                        baked = bvg.frames_to_spr([bvg.bake_frame(f, mid, name, self.font) for f in frames])
                        cache_spr.write_bytes(baked)
                    entries[spr_path] = baked
                    entries[bvg._sibling_act(spr_path)] = bvg.read_from_sources(grfs, client, bvg._sibling_act(spr_path))
                    manifest["mobs"][str(mid)] = {"name": name, "sprite": spr_path, "code": bvg.color_code(mid)}
                    done += 1
                    self._set_status(f"Baking {done}/{total}: {name}")
                except Exception as e:
                    self._set_status(f"skip {name}: {e}")

            if not entries:
                raise RuntimeError("no sprites baked (check the source GRF has these monsters)")
            bvg.pack_grf(entries, out)      # prints '[grf] verify OK' -> raises if the container is bad
            man_path = out.with_name("VisionAssist.manifest.json")
            man_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
            self._done(out, man_path, done)
        except Exception as e:
            traceback.print_exc()
            self._error(str(e))

    # thread-safe UI updates
    def _set_status(self, msg): self.root.after(0, lambda: self.status.set(msg))
    def _error(self, msg):
        self.root.after(0, lambda: (messagebox.showerror("Apply failed", msg),
                                    self.apply_btn.config(state="normal")))
    def _done(self, out: Path, man: Path, n: int):
        def ui():
            self.apply_btn.config(state="normal")
            self.status.set(f"Done: {n} monsters -> {out.name}")
            messagebox.showinfo(
                "Done",
                f"Built {out}\n{man}\n\n{n} monsters baked (verify OK).\n\n"
                f"1) Copy VisionAssist.grf into your RO client folder.\n"
                f"2) DATA.INI:  0=VisionAssist.grf  (first entry).\n"
                f"3) RESTART the client (GRFs load at startup).\n"
                f"4) In 4ViviTools set the manifest path and enable Vision Assist GRF.")
        self.root.after(0, ui)


def main():
    root = tk.Tk()
    PickerApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
