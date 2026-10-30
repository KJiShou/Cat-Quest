# App cursor

The main Unity application installs one persistent `AppCursor` before its first
scene. It uses `Cursor.SetCursor` (hardware cursor where supported), without a UI
overlay or changes to button input, navigation, rewards or save data. The separate
`--companion` process retains its native window resize cursors.

## Artwork and replacement

Select `Assets/Resources/AppCursorSettings.asset` in the Inspector:

- **Normal**: default texture. Empty restores the operating system cursor.
- **Hotspot**: click point in pixels from the **top left of the imported texture**.
- **Button Hover / Button Hover Hotspot**: optional hand over an enabled,
  interactable UI Button. Empty keeps Normal. Disabled buttons and buttons behind
  raycast-blocking graphics retain the normal cursor. Leaving/closing the button
  restores Normal, even when the mouse stays still and the UI changes beneath it.
- **Pressed / Pressed Hotspot**: optional left-button-down image and click point.
  Leave Pressed empty to keep the current normal/hover cursor when clicking. Match the fingertip location
  between images so pressing does not visually shift the click point.

Import replacement PNG artwork as **Cursor**, enable **Read/Write**, disable mipmaps
and compression, and start with **Max Size 32** (pixels, independent of Canvas scale).
Assign the imported texture; adjust the hotspot to the visible tip. Keep this asset
at its existing Resources path. No scene changes or generator runs are needed.

The active cursor is Kenney Cursor Pack 1.1's
`PNG/Outline/Default/pointer_toon_a.png`, copied unchanged to
`Assets/Resources/Cursors/KenneyToonArrow.png`. It is a 32x32 white cartoon arrow
with a black outline, imported as Cursor (readable, uncompressed, no mipmaps).
The top-left arrow tip hotspot is `(3, 3)`. Pressed remains empty, so clicking
keeps the current arrow or hover hand without a visual jump.

Button hover uses `PNG/Outline/Default/hand_point.png` from the same pack, copied
unchanged to `Assets/Resources/Cursors/KenneyHandPoint.png`, with fingertip hotspot
`(9, 3)`. Its import settings match the arrow. `AppCursor` reuses raycast storage
and checks the EventSystem's top hit and click-handler hierarchy each frame;
it does not add listeners/components to buttons or interfere with JuicyButton.
Keyboard selection alone does not change the mouse cursor. Sliders, toggles and
text fields retain the arrow unless their actual click handler is a Button.

Source: https://opengameart.org/content/cursor-pack-1 (Kenney, CC0).
The original license is preserved in `Assets/Resources/Cursors/Kenney-License.txt`.
This user-selected artwork replaces the previous SimpleCasual tutorial hand;
the existing `SimpleCasualHand.png` and vendor originals remain untouched.
The prior reference component was SimpleCasual's
`Prefabs/Prefabs_Component_UI_Etc/Tutorial_Focus_Icon_Hand.prefab`; no page layout,
typography or button feedback is changed by this cursor artwork substitution.

Focus loss, pointer leaving the client viewport, cursor locking, disabling the
component and application exit restore the system cursor. The component does not
change cursor visibility/locking, time scale, selection or pointer events.

## Manual visual check

In Play Mode, check the arrow tip aligns with small button edges at 1920x1080 and
1280x720. Click/drag sliders, type in fields, navigate with the keyboard, open and
close dialogs, switch scenes and Alt-Tab out/back. Ordinary ScreenCapture output
does not include a hardware cursor. Inspect the actual pointer in the Game View
and Windows player before signing off its appearance. Companion's native resize
handles should continue to display their usual directional cursors.
