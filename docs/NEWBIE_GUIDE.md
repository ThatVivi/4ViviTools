# 4ViviTools Newbie Guide

This guide is for the simple screen-based OCR + Smart Bot flow.

## 1. Download and open the tool

1. Go to the project GitHub page: https://github.com/ThatVivi/4ViviTools
2. Download the latest full build zip from Releases if one is available.
3. Extract the zip somewhere simple, for example `C:\4ViviTools`.
4. Run `4rVivi.exe`.

For the default Smart Bot click method, 4ViviTools checks the ViGEm driver for you. If it is missing, press `Install ViGEm` in Smart Bot; the app downloads the official setup and opens it. reWASD is optional: if you use it, 4ViviTools can work with its imported or active virtual-controller profiles. The Smart Bot page also shows the virtual mouse driver lane; it can install a packaged signed `vmouse` driver when one is shipped, or download and open the official FakerInput virtual HID mouse installer.

If you are building from source instead:

1. Install the .NET 8 Desktop Runtime or SDK.
2. Open a terminal in the repo folder.
3. Run `dotnet build "D:\vs code clone 4rtool\4ViviTools\4rVivi.sln" -c Release`.
4. Run the app from `D:\vs code clone 4rtool\4ViviTools\src\4rVivi.App\bin\Release\net8.0-windows10.0.19041.0`.

## 2. Attach the Ragnarok Online client

1. Start Ragnarok Online first.
2. In 4ViviTools, use the top bar `Process` picker.
3. Pick your running RO client.
4. If you do not see it, press the refresh button next to the process picker.

This attach step is important. The app treats OCR as attached to the selected client, so the bot can convert monster positions into client click positions. For multi-client setups, attach each client/server profile you want to manage.

## 3. Capture the screen with OCR Reader

1. Open the OCR Reader page.
2. Leave `Monitor capture` on.
3. Leave `DXGI default` on. This is the default capture path.
4. Pick the monitor where RO is visible.
5. Press `Capture client`.

DXGI is tried first. If it is unavailable, the app falls back to normal monitor capture.

## 3.1 Multi-client OCR

Use this when you run more than one RO client/server at the same time, for example four clients divided into the four corners of the screen.

1. Open `Bot`.
2. Open `Multi Client`.
3. Pick a running RO client from the dropdown.
4. Press `Add client`.
5. Repeat for every client you want to watch.
6. Press `Start OCR for all`.
7. Set each row's `Purpose`:
   - `Main` attacks detected monsters.
   - `Buffer` only sends buff keys, either by `Buff now` or by `Auto`.
   - `Watcher` only reads OCR.
8. Turn on the `Bot` checkbox for rows that should send input without focus.
9. For the main client, record a skill key if you want skill then click.
10. For buffer clients, record the buff key and optionally enable `Auto`.
11. Use `Use for bot` on the client that the main Smart Bot page should control right now.

Multi Client OCR is hard-attached to each client window handle. It captures that client's own client area and reads that client's size every frame, so it does not guess from the whole monitor. The clients do not need focus and do not need to be on top. Optional row bot input uses unfocused `PostMessage` clicks and keys sent to that same client handle. If a client stops rendering while minimized, restore it once; after that it can stay behind other windows or covered.

## 4. Put marks on the screen

Fast path:

1. Press `Auto-detect markers`.
2. Leave the mode on `Review detected`.
3. Run one OCR read and check the confidence message.
4. If the app says the markers are ready, press `Use detected markers`.

Manual path:

1. Change marker mode to `Manual`.
2. In `Mark`, start with `HP / MaxHP`.
3. Drag a box over the HP number area on the captured image.
4. Change `Mark` to `SP / MaxSP`, then drag a box over SP.
5. Add any other useful marks you want, such as `Weight / MaxWeight`, `PosX`, `PosY`, `MapName`, `SkillBar`, or `BuffBar`.
6. Press `Save`.

Use tight boxes around only the value you want OCR to read. For example, the HP box should cover the HP text, not the full UI panel.

## 5. Run OCR

1. Keep `Text`, `Monsters`, and `Skills and buffs` enabled.
2. Leave `Run live` selected.
3. Press `Start OCR`.
4. Press `Overlay` if you want to see OCR boxes and labels over the screen.

The app now reads your marked HUD values and scans the screen for monsters. Monster labels are cleaned before display, so item ID matches such as `1000` are not shown as monster names.

## 6. Set up Smart Bot

1. Open the Smart Bot page.
2. Leave `Target detected monsters` enabled.
3. Leave `Trusted cursor click` enabled.
4. Leave `Click to attack` enabled.
5. Leave `Click to move` enabled if you want the bot to walk around when no monster is found.
6. Leave the input backend on `ViGEm virtual click` after the ViGEm driver status turns green. If it is red, press `Install ViGEm`, finish the Windows installer, then press `Check driver`. reWASD can also be used if you want imported or active profiles.
7. In `Skill hotbar`, check the hotkey buttons the bot may use, for example `F2` and `F3`.
8. In `Checked buttons become bot skills`, pick the Ragnarok skill name for each checked button.
9. In `Buff upkeep`, record each buff key and pick the buff or skill name. Set the refresh seconds.
10. In `Potions`, turn autopot on, record the potion key, choose HP or SP, and set the trigger percent.
11. Put your ammo hotkey in `Ammo key`, choose the ammo item if needed, and mark the `Ammo` number in OCR if you want low-ammo stop.
12. Press `Start` on the Smart Bot header.

Without reWASD installed, `ViGEm virtual click` still works through the built-in normal-click fallback after the ViGEm tap. You can also switch to `SendInput`, `mouse/keybd_event`, or `PostMessage`.

## 7. How attacks work

Normal attack:

1. OCR detects a monster.
2. Smart Bot moves the cursor to the monster.
3. Smart Bot left-clicks the monster.

Skill attack:

1. Add a monster rule.
2. Pick the monster name if labels are reliable, or leave the rule list empty to attack any detected monster.
3. Record the skill hotkey or enable the skill button in the Smart Bot skill grid.
4. Press `Apply`.
5. When that monster is detected, Smart Bot presses the skill key first, then left-clicks the monster.

Movement:

1. If no monster is found and `Click to move` is enabled, Smart Bot left-clicks nearby walk points.
2. It waits for position/OCR updates before deciding the next move.

## 8. Input drivers: ViGEm, reWASD, and virtual mouse

The default Smart Bot click path is `ViGEm virtual click`. 4ViviTools checks whether the ViGEm driver is installed. If it is missing, it can download the official installer into `%AppData%\4rVivi\Drivers` and launch it for you. reWASD is optional and useful when you want to import profiles from reWASD or use a reWASD profile directly.

1. In Smart Bot, press `Check driver`.
2. If the driver status is red, press `Install ViGEm`.
3. Accept the Windows administrator prompt and finish the ViGEm setup.
4. Press `Check driver` again.
5. Confirm the status is green.
6. Keep the input backend on `ViGEm virtual click`.
7. Pick the `Virtual button` that your active reWASD profile maps to left mouse.
8. Press `Test virtual click` while the mouse is over a safe spot.
9. Keep `Click to attack` and `Click to move` enabled.
10. Optional: press `Get reWASD` if you want reWASD profile import/mapping. If reWASD is already installed, the same button opens it.
11. Optional: use the virtual mouse driver lane if you want to test FakerInput or a bundled signed `vmouse.inf` + `vmouse.sys` package. If a signed `vmouse` package is not bundled, the button downloads the official FakerInput installer instead of trying to install unsigned source code.

The Smart Bot header also has a global `Start/stop key`. Record any key there if you want one-button control, and use `F12` as the panic stop for all enabled features.

Debug log:

- Open Smart Bot.
- Press `Open debug log`.
- The file is `%AppData%\4rVivi\Logs\DebugTrace.log`.
- Upload that file when clicks, drivers, OCR, or panic stop need debugging.

What 4ViviTools provides:

- It moves the cursor to the OCR monster or walk point.
- It taps the ViGEm virtual Xbox button.
- If reWASD is running, it can receive that virtual button through your active profile.
- If reWASD is not running, 4ViviTools still performs the click through its normal cursor click path after the ViGEm tap.
- If a signed virtual mouse driver is packaged later, 4ViviTools can install it with the Windows driver installer and show its status in the same driver panel.

Important reWASD check:

- The virtual button in 4ViviTools must match the reWASD mapping.
- If reWASD maps Xbox `A` to a keyboard key, Xbox `A` will not click.
- If your reWASD macro is on keyboard `F2`, that is a different trigger than virtual Xbox `A`.
- For the cleanest setup, map the chosen Xbox button, for example `A` or `LeftShoulder`, to `Left mouse button down` then `Left mouse button up`, then use the same button in Smart Bot.
- If you need a temporary backup while fixing the profile, enable `Fallback normal click`.

## 9. Discord Rich Presence

Discord RPC works with the built-in app id by default.

1. Open `Settings`.
2. Leave `Enable Discord presence` on.
3. Leave `Discord Application (Client) ID` blank unless you made your own Discord app.
4. Put your server name and optional website URL.
5. Press `Save`.

Keep the Discord desktop app running. The presence card uses live OCR/memory stats when available: character, class, HP/SP, map, position, and idle/moving/attacking state.

## 10. When monster names look wrong

The detector and icon bank are separate systems:

- YOLO finds boxes such as monster, loot, portal, target, and player.
- The icon bank tries to name monster sprites.
- OCR text reads floating text and marked HUD values.

If a monster is shown only as `Monster`, the detector found it but the icon name was not confident enough. That is safer than showing a wrong item number. You can still attack all detected monsters with an empty monster rule list, or add specific monster rules once labels are reliable.

Training data lives in:

`tools\ocr-train\TrainingData`

Monster frame files live in:

`D:\vs code clone 4rtool\claude\data\sprite\¸ó½ºÅÍ`

The generated monster manifest lives in:

`D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\icons\monster_manifest.json`

The processed monster/skill icon training workspace lives in:

`D:\vs code clone 4rtool\4ViviTools\tools\ocr-train\icons`

The engineering guide lives in:

`Ragnarok Online OCR Engineering Guide.pdf`
