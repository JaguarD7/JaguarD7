FC27 ASSIST — Windows x64
FINAL AUDIT BUILD

Purpose:
Offline/controller accessibility helper for EA SPORTS FC 27.

IMPORTANT — SINGLE CONTROLLER MODE
FC27 must see ONE controller only.
FC27Assist reads your physical wired Xbox controller, applies the configured
accessibility/skill logic, and outputs ONE virtual Xbox controller to the game.

Required:
1) Windows 10/11 x64
2) One wired Xbox-compatible controller connected
3) ViGEmBus installed
4) HidHide installed

First run:
- Connect ONLY the physical controller you want to use.
- Start FC27Assist BEFORE FC27.
- FC27Assist registers itself in HidHide and hides the physical gaming-device
  interfaces before it creates the virtual controller.
- If HidHide was configured for the first time, the app will ask you to
  unplug/replug the wired controller once and restart FC27Assist.
- The virtual controller is NOT created until Single Controller Mode is ready.
  This prevents FC27 from showing Controller 2 / a second Ready player.

After setup:
Physical Xbox pad -> FC27Assist -> Virtual Xbox pad -> FC27
FC27 should not receive the physical pad directly.

If Single Controller Mode says SETUP REQUIRED:
- Close HidHide Configuration Client if it is open.
- Make sure HidHide is installed.
- Disconnect extra physical gamepads.
- Keep only the Xbox controller you want to use connected.
- Restart FC27Assist.
- If prompted, unplug/replug that controller once and restart the app.

Default modes:
LB -> ATTACK mode (one transition only)
LT -> DEFENSE mode (one transition only)

Attack:
RS Up = Explosive Stepover
RS Right = Ball Roll Spin Right
RS Left = Ball Roll Spin Left
RS Down = Stepover Ball Right
LB+RS Up = Lateral Heel to Heel
LB+RS Right = Skilled Bridge
LB+RS Left = Stop and Go
LB+RS Down = Trickster Fake Shot
A = Driven Ground Pass
Y = Lobbed Through Pass
Quick B = calibrated Low Driven
Hold B = normal shot with program-controlled power cap

Defense:
RS = native player switching
Smart Second-Man Press
Optional Sprint Jockey Assist
Manual final tackle remains yours

Conflict protections:
- Held inputs from the previous mode are quarantined until release.
- RS carried from Attack into Defense is blocked until centered.
- LT carried into Attack is blocked until released.
- LB skill chords cannot leak a raw LB command.
- Face buttons cancel running skill macros.
- Shot automation blocks LB/RB shot modifiers while active.
- Auto Press stops around tackles, keeper rush, pass/contain actions and RS switch.
- Macro scheduler never skips an injected press/release edge after an OS stall.
- Skill paths use 8-way RS vectors including diagonals.
