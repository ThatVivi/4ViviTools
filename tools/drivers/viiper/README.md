# VIIPER optional virtual USB backend

4ViviTools can use VIIPER as a virtual USB keyboard and mouse backend. The app does not install or enable it on startup.

Use the Smart Bot input panel:

1. Press `Check driver`.
2. Press `Enable VIIPER`.
3. Press `Test VIIPER`.
4. Select `VIIPER virtual USB (keyboard + mouse)` in the input picker.

VIIPER installs to:

```text
%LOCALAPPDATA%\VIIPER\viiper.exe
```

Logs are written to:

```text
%APPDATA%\4rVivi\Logs\VIIPER.log
%APPDATA%\4rVivi\Logs\DebugTrace.log
```

Official project links:

- https://github.com/Alia5/VIIPER
- https://github.com/vadimgrn/usbip-win2

`usbip-win2` is the signed Windows USB/IP kernel driver VIIPER uses. VIIPER itself creates keyboard/mouse/gamepad device behavior in user space over that generic driver.
