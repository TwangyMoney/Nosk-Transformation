# Nosk Transformation

Transform into Nosk and troll your friends!!!! Become the Mimic Spider on command, unleash roars, fling acid spits, and make dramatic leaps as you stalk Hallownest. Pop in, prank your squad, and switch back whenever you feel like it — all while keeping things low-key with a stealthy name.

## Stealth
- The mod’s displayed name and its options menu title are taken directly from the DLL filename (minus `.dll`).
- Rename the DLL to anything innocuous (e.g., `QoL_Helper.dll`) before sharing.
- Ask friends to manually place the DLL into their Hollow Knight Mods folder — it will appear under the new name in both the mod list and the mod menu.



https://github.com/user-attachments/assets/84e51780-5002-4aab-a885-2da3b47d3778



---

## Features
- Transform into Nosk and control it directly.
- Manual attacks and actions:
  - Roar
  - Spit (acid)
  - Jump Attack
  - Strike (short charge)
  - Roof moves: RS Jump, Roof Drop, Roof Jump?
- In-game keybind editor with “Press any key…” prompts.
- “Fix Mod” button if something gets stuck.
- Settings save between sessions.
- Dynamic naming: the mod’s name and menu title follow the DLL filename.

---

## Requirements
- Hollow Knight (modded)
- HK Modding API
- Satchel.BetterMenus

---

## Installation
1. Build or download the DLL for this mod.
2. (Optional, for stealth) Rename the DLL to any name you want to appear in-game (e.g., `InputOptimizer.dll`).
3. Place the DLL into your Hollow Knight `Mods` folder.
4. Launch the game and enable the mod in the mod options.

---

## Default controls
- Toggle Transform: O (Ctrl required by default)
- Move: A (left), D (right)
- Roar: 0
- Infectious Outburst: 1
- Jump Attack: Space
- Roof Mode: [NOT ALWAYS AVAILABLE]

All controls can be rebound in the in-game menu.

---

## Keybind capture
- Click a binding in the menu to make it show “Press any key…”.
- Press any key or mouse button to set it.
- The label updates immediately to the new key.

---

## Usage
1. Enable the mod in the options menu.
2. Press Ctrl+O (by default) to transform.
3. Nosk will follow you; use your bound keys to move and attack.

---

## Multiplayer (HKMP) note
- With the right addon setup, others can see your Nosk and react to its actions.
- Everyone needs the same addon installed.
- This repository is focused on local play; multiplayer messaging isn’t included.

---

## Known limitations
- If you’ve used the mod before, saved settings may override new defaults — rebind once to apply them.
- If something feels off, use the “Fix Mod” button, then toggle transform off/on.

---

## Troubleshooting
- Something stuck? Use “Fix Mod” and toggle transform again.
- Keybind label didn’t change? Back out and reopen the keybinds menu after rebinding.

---

## Building
- The mod’s displayed name and menu title are taken from the DLL’s filename at runtime.
- Rename the file after building to change how it appears in-game (no rebuild needed).

---

## Contributing & Attribution
- Pull requests are welcome to improve the mod.
- Do not claim this project as your own; credit the original author when sharing or modifying.

---

## Changelog
- 1.0.0
  - DLL-name-driven mod/menu title
  - “Press any key…” keybind capture
  - Space as default Jump Attack
  - Nosk control set and Fix button
  - Fixes/Fine tuning to remote system

---

## Credits
- Hollow Knight by Team Cherry
- DebugMod
- HK Modding API, Satchel.BetterMenus, and the modding community

## Play Testers

A huge thanks to the early explorers who helped poke, prod, break, and perfect the Nosk experience:

* **JoSeBach (@josebach)** — Tested the mod extensively with logs and recordings to debug most of the annoying errors.
* **Traksu (@traksu)** — Helped me find a lot of early bugs, helped test with recordings.
* **The Lakitu King (@lakitu1818)** — Helped test early bugs, hoped to showcase the mod/use to troll friends in a video.
* **Grimm (@gaster_os)** — Helped testing some really early bugs, alongside others.
* **Viraj (@viraj_khiste_00)** — Helped testing some really early bugs, alongside others.

If your name isn't mentioned above and you can prove your contribution to the project, please don't hesitate to DM me about it.

## Attribution 
- Not required, but appreciated. If you feature this mod in videos/streams/posts, a link back to this repo helps others find it.
- Please don’t claim the original project as your own.
