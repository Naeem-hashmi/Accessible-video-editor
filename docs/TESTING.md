# Manual screen reader test plan

Automated tests cover the editing logic, rendering and the UI Automation names. The test that matters most, a real person with a real screen reader, has to be done by hand. Run this list with **NVDA** (latest), **JAWS** (current and previous version) and **Narrator** before each release. Note anything that is silent, said twice, cut off or confusing.

## Setup

1. Install from the setup program. The installer pages should all be read.
2. Start the program. You should hear: "Accessible Video Editor. New basic project. Layer 1, Video, empty..." and "Press F1 for keyboard help."

## Core tasks

| # | Task | Expected |
|---|---|---|
| 1 | `Control I`, choose two videos | Standard Open dialog; after it closes: "Added ... on layer 1", focus on the first item |
| 2 | `Right` / `Left` | Each item read once: "2 of 2, name, video, starts ..., length ..." |
| 3 | `Right` at the last item | Edge sound and "Last item." |
| 4 | `K`, wait, `K` | Playback starts (maybe "Preparing preview"); pause says "Paused at ..." |
| 5 | `L` twice while playing | "2 times speed", "4 times speed" |
| 6 | `T`, `W`, `Shift T` | Time, what's on each layer, project summary |
| 7 | `S` in the middle of an item | "Split ... First part ..., second part ...", focus on the second part, read once |
| 8 | `Delete` | "Deleted name." then the next item, as one utterance |
| 9 | `Control Z` | "Undo delete item." |
| 10 | `Control Shift A`, then `Control Shift E`, choose Whoosh | New audio layer; sound added at the playhead |
| 11 | `Alt 1`, `Alt 2`, `Alt 0` | Layer announced with "listening to this layer only"; then "Listening to all layers." |
| 12 | `Up` / `Down` | Layer changes; the item at the same time is focused |
| 13 | `[`, move, `]`, `Shift Delete` | Range start and end spoken; range removed from this layer |
| 14 | `B`, `Alt Left`, `Alt Right` | Marker added; jumps announce marker names and times |
| 15 | `Alt Shift Right`, `Alt Shift Left` | "Moved to position ..." |
| 16 | `Alt L`, `Alt J` | "Slid 1 second later, starts ..." |
| 17 | `M`, `Shift M`, `Control Shift M` | Mute, solo, unmute all, each spoken with its sound |
| 18 | `Enter` on an item | Options dialog: every field read with its label; `Control Tab` changes tab; Enter saves |
| 19 | `Control T` at a join, choose Dissolve | "Dissolve into name." |
| 20 | `Control Shift I` with a PNG logo, then `Enter`, Picture tab, Top right, 20 percent | Logo layer created; `W` says "top right, 20 percent size" |
| 21 | `F7` | Findings list; Enter goes to the problem |
| 22 | `Control E`, YouTube 1080p | Progress every 10 percent; "Export finished ..."; file plays in another player |
| 23 | `Control S`, close, reopen the file | Project is the same |
| 24 | `F1`, type "slide" | The list filters; Enter runs the chosen command |
| 25 | `F6` | Moves between the timeline and the layer list; both read properly |
| 26 | Preferences: theme Retro, verbosity Brief | Sample sound plays when the theme changes; descriptions get shorter |
| 27 | `Ctrl+1` to `Ctrl+5`, `F6`, `Shift+F6` | Library, timeline, properties, preview and layers are each announced; focus lands on a useful control |
| 28 | `Ctrl+Shift+T`, type two lines, choose Lower third | Text added on a Text layer; `Shift+W` names it and says "bottom left" |
| 29 | `P` on the text, arrows, `+`, `9`, `Ctrl+Left` on a picture, `Escape` | Each step is spoken with a panned tone; Escape says it went back |
| 30 | `P`, `0`, `F`, `Enter` on a portrait photo | "Fill: covers the whole frame"; export shows no black bars |
| 31 | `Ctrl+Up` / `Ctrl+Down` / `Ctrl+M` on a clip | "Volume 110 percent", "sound muted" |
| 32 | `Ctrl+3`, change Volume to 50, Enter | Applied and spoken; the timeline item then says "volume 50 percent" |
| 33 | `Ctrl+Shift+O`, search "rain", Space, Enter | Results read with their licence; preview plays; downloaded and added at the playhead |
| 34 | Export a project with a CC BY sound | A "credits.txt" file sits next to the video and the finish message mentions it |
| 35 | `Ctrl+Shift+N` save as a template in a new folder, then `Ctrl+N` | The template is listed under its folder and creates a project with the same layers and text |

## Things to listen for

- Nothing should be read twice after a single key press.
- Messages and focus changes should never cut each other off.
- Every control in every dialog has a spoken label.
- No key traps: Escape closes every dialog, and Tab and Shift Tab reach every control.
- Letter keys do nothing unexpected inside text boxes.
- With JAWS, check that the virtual cursor is off in the main window (the timeline is an application list, not a document).

## Known limits

- No scrubbing sound while moving the playhead with the arrows (the playhead moves silently, and `T` speaks it).
- Reverse playback isn't available; `J` jumps back instead.
- Spoken text sizes are estimates (about 0.55 of the letter height per letter).
- Right-to-left scripts such as Urdu depend on the bundled FFmpeg's text shaping; check them in an export.
- On keyboard layouts without bracket keys, change the range keys in the keyboard settings file.
