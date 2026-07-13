# Input Driver Research

Checked: 2026-07-02

## Usable for 4ViviTools now

- `yannbouteiller/vgamepad`: Python wrapper around ViGEmBus. Confirms the ViGEmBus lane we already use. It is gamepad-only, not mouse.
- `Keyboard2Xinput`: maps keyboard to virtual Xbox 360 gamepads through ViGEmBus. Confirms the same controller-first approach, but does not add a new mouse driver.
- `Nefarius/ViGEmBus`: release-ready virtual gamepad bus. This remains the safest default install path for 4ViviTools because signed public installers exist and the app already uses `Nefarius.ViGEm.Client`.

## Real driver source, but not release-ready

- `djpnewton/vmulti`: true Windows virtual HID driver for multitouch, mouse, digitizer, keyboard, and joystick. It can move the cursor through `/mouse`, but the upstream README says 64-bit systems need proper driver signing before install. Good technical reference, not an end-user install package.
- `koharubiyori/VirtualInput`: real Windows 10/11 HID virtual mouse and keyboard driver source, including absolute-coordinate mouse support. No releases; install instructions require test mode, local Debug build, WDK `devcon`, and reboot. Good research candidate, not end-user release-ready.
- `wadrych/vmouse`: real Windows HID virtual mouse minidriver plus user-mode client. Best mouse-driver source candidate, but has no published signed release.
- `Kolyn090/vmulti-mice`: includes vmulti HID driver source and Python examples that can click without moving the system cursor. Old/unsigned/test-mode flow; not safe as a one-click release install.
- `changeofpace/MouHidInputHook`: real kernel driver technique for hooking/injecting MouHid packets. Source/research project, not a packaged end-user driver.
- `mihirgupte/Mouse-Driver-KMDF`: educational KMDF mouse/filter driver. Tested only on Windows 8.1, no release, asks for disabled integrity/signing workarounds.
- `E-Heerschap/VirtualMouse`: real virtual mouse driver, but for Linux kernel/X11, not Windows. Not useful for 4ViviTools release.
- `Ryochan7/FakerInput`: true Windows virtual keyboard plus relative/absolute mouse driver used by DS4Windows. Latest release exposes `FakerInput_Setup_0.1.1_x64.msi`; the INF installs a root `FakerInput` UMDF HID device with keyboard, relative mouse, and absolute mouse reports. This is now the practical first test for the app's virtual mouse lane. Direct click support still needs a client implementation against its HID control reports before it becomes a full Smart Bot backend.
- `oblitum/Interception`: real low-level keyboard/mouse driver with a public installer. It is not a virtual HID mouse device; it intercepts and controls physical input devices. License/commercial terms and reboot/admin install make it a poor default for a newbie-friendly release, but it can remain an advanced/manual backend candidate.

## Build-your-own driver foundations

- Microsoft `VHF` (Virtual HID Framework): official Windows 10+ framework for building a KMDF HID source driver that reports virtual HID data to Windows. This is the clean long-term route if 4ViviTools eventually ships its own signed virtual mouse/keyboard driver.
- Microsoft `vhidmini2`: WDK virtual HID minidriver sample. Useful as a starter/reference project, not a product driver by itself.
- Microsoft `hclient`: HID client sample. Useful for learning HID stack/client behavior, not a virtual mouse deliverable.
- Microsoft `hidusbfx2`: WDF HID minidriver sample for exposing a non-HID USB device as HID. Useful reference, but tied to the OSR USB-FX2 learning kit and not a virtual input solution for our release.

## Real drivers, wrong category

- `VirtualDrivers/Virtual-Display-Driver`: signed virtual monitor/display driver. Useful for headless capture/streaming, not mouse input.
- `Nonary/Vibepollo`: streaming app/fork that bundles or manages virtual display behavior. Useful for display, not mouse input.
- Reddit Moonlight thread: points to virtual display choices such as Virtual Display Driver and Vibepollo; not mouse input.
- `Toxpox/MouseDrive`: uses the vJoy virtual joystick driver. It is joystick/wheel output, not mouse output.

## App-level input helpers, not drivers

- `AutoHotkey`: mature user-mode scripting layer over Windows input APIs such as SendInput. Useful as a comparison point or power-user export format, not a kernel HID driver.
- `pynput`: Python mouse/keyboard control and monitoring library. Useful for prototypes, not for the .NET app's release backend.
- `PyAutoGUI`: Python GUI automation toolkit that controls mouse/keyboard and screenshots. Useful OCR/clicking prototype reference, not a virtual HID driver.
- `Enigo`: Rust cross-platform keyboard/mouse event simulation. Useful if we ever build a Rust helper, not a driver.
- `RobotJS`: Node desktop automation library for mouse/keyboard/screen. Not appropriate for the Avalonia/.NET release backend.
- `boppreh/mouse`: Python mouse hook/simulation library. No driver, and its own notes warn generated Windows events do not report a device id.
- `InputSimulator`: C# wrapper around Win32 `SendInput`. This duplicates the app's existing SendInput backend rather than solving the virtual-driver problem.
- `AVISIX/Inputs.NET`: .NET input simulation library using `mouse_event`, undocumented `NtUser*` methods, and optionally external ddxoft driver. It is archived and does not provide a redistributable driver.
- `BlankyWacky/razerctl`: talks to the installed Razer Synapse driver. Hardware/vendor-specific; does not provide a redistributable driver.
- `sampsonjoliver/Windows-Virtual-Keyboard-Helper`: touch injection, virtual keyboard launching, and mouse hook helper. No driver.
- `DavidAnson/MouseButtonClicker`: small user-mode auto-click utility. No driver.
- `Xavi007/Virtual-Mouse`: Python/webcam/image-processing virtual mouse. No driver.
- `bharathjoshi/Virtual-Mouse`: OpenCV/PyAutoGUI-style webcam mouse. No driver.
- `04Deepak/Virual-Mouse`: webcam/Python virtual mouse. No driver.
- GitHub `virtual-mouse` and `hand-gesture-mouse-controller` topic pages: discovery lists mostly user-mode webcam/OpenCV/MediaPipe/PyAutoGUI projects, not installable drivers.
- Reddit botting/WFH/techsupport/mouse-review/sysadmin/Logitech threads: mostly discussion of physical clickers, vendor virtual devices, USB emulation ideas, or vendor software. They do not provide a redistributable Windows virtual mouse driver suitable for the app.

## Decision

Keep ViGEmBus as the release-ready default. Keep reWASD optional as a profile bridge. For a true virtual mouse driver, test `FakerInput` first because it has public releases and known DS4Windows usage, then compare against `vmulti`, `wadrych/vmouse`, and `koharubiyori/VirtualInput` if we need our own signed driver path. Do not advertise one-click installation for source-only drivers until we have a signed `.inf`, `.sys`, and `.cat` package. The current app behavior is: install packaged signed `vmouse` if present; otherwise download/open the official FakerInput MSI.
