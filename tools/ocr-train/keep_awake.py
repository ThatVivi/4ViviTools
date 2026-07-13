#!/usr/bin/env python3
"""Keep Windows awake while a sentinel file exists.

RUN_OVERNIGHT_YOLO_2060S.bat creates the sentinel before training and deletes it
on success or failure. This helper exits by itself after that, so it does not
change the user's global power plan and does not leave a background process.
"""
from __future__ import annotations

import ctypes
import os
import sys
import time

ES_CONTINUOUS = 0x80000000
ES_SYSTEM_REQUIRED = 0x00000001
ES_AWAYMODE_REQUIRED = 0x00000040


def set_execution_state(flags: int) -> None:
    try:
        ctypes.windll.kernel32.SetThreadExecutionState(flags)
    except Exception:
        pass


def main() -> int:
    sentinel = sys.argv[1] if len(sys.argv) > 1 else ""
    if not sentinel:
        return 2

    flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_AWAYMODE_REQUIRED
    while os.path.exists(sentinel):
        set_execution_state(flags)
        time.sleep(30)

    set_execution_state(ES_CONTINUOUS)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
