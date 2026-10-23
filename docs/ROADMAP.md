# Roadmap: limitations and what to build next

This lists what the editor can't do yet, roughly in order of value to blind and low-vision creators, with how each could be built. It is honest about the current limits so nobody is surprised.

## What version 0.2 added

- **Text**: titles, captions, lower thirds, credits and calls to action, with 12 Windows fonts, outline, a dark or light box, and 8 ways in and out: fade, slide from any side, rise, and typewriter. `Ctrl+Shift+T` adds text, `F2` edits it, and the library's Text tab has ready-made styles.
- **Placement you can understand without seeing**: `Shift+W` describes the picture at the playhead. It names every visible picture and text, the area of the frame it is in (a 3 by 3 grid: top left, centre and so on), its size, the edges it touches and what it covers.
- **Position mode** (`P`), a keyboard version of drag and drop on the preview:
  - arrows move the picture or text, and plus and minus resize it;
  - number keys snap it to a grid laid out like a number pad, `0` makes it full frame (maximise) and `F` switches between fit and fill;
  - `Ctrl+arrows` crop and `O` changes opacity;
  - `Enter` keeps the changes and `Escape` undoes them all.
  - After every step a short tone plays, panned left or right by the position and higher the nearer the top, so you can hear where the picture is.
- **Sound for every item**: `Ctrl+Up` and `Ctrl+Down` change the item's volume, `Ctrl+M` mutes the item, and `Ctrl+Shift+Up` and `Ctrl+Shift+Down` change the layer volume. Fades, noise reduction, hum removal, equaliser and voice presets are in the properties area and in item options.
- **Clipchamp-style areas**: library (`Ctrl+1`), timeline (`Ctrl+2`), properties (`Ctrl+3`), preview (`Ctrl+4`) and layers (`Ctrl+5`). `F6` and `Shift+F6` move between them.
- **Templates with folders**: save any project as a template, with or without its media; text and colour cards are always kept. You can rename, move, delete and import templates, and new projects can start from any of them.
- **Free music online**: Openverse search, limited to public domain and CC BY. CC BY credits are recorded and written to a "credits" file next to each export.

## Known limitations

| Limitation | Why | How to fix |
|---|---|---|
| No sound while moving the playhead with the arrows | The preview is a pre-rendered file played by Windows Media Foundation | Add libmpv behind `PreviewService` for instant seeking and scrubbing |
| First play of a section waits 1–3 seconds | Each preview window is rendered with FFmpeg | Render ahead in the background after each edit; use libmpv's live filters for simple cases |
| No reverse playback | Media Foundation can't play backwards | libmpv supports it |
| Text size, and so placement, is an estimate in speech | Exact text width depends on the font | Measure with the font itself (WPF `FormattedText`) and pass the real width to `Geometry` |
| Urdu, Arabic and other right-to-left or joined scripts may draw wrongly | Depends on whether the FFmpeg build includes HarfBuzz text shaping | Check `ffmpeg -h filter=drawtext` for `text_shaping`; bundle a build that has it; add Urdu and Arabic fonts to the font list |
| Typewriter text on centred lines starts from an estimated left edge | drawtext can't measure the whole line before it is typed | Measure as above |
| Text animations are only in and out | Kept simple and reliable first | Add keyframed motion (start and end position) using the same expressions |
| Crop and position have no keyframes (no Ken Burns pan or zoom) | Not built yet | `zoompan` filter for pictures, or per-frame crop expressions |
| No speed change (slow motion or fast forward) | Not built yet | `setpts` and `atempo` per item, with the timeline lengths adjusted |
| No automatic ducking (music getting quieter under speech) | Not built yet | `sidechaincompress` with the voice layer as the key; one "Duck under layer N" option on audio layers |
| No captions from speech, no transcript editing | Needs speech recognition | Whisper (whisper.cpp, MIT licence) run locally: captions as text items, and "delete these words" cuts the video (the AVscript idea) |
| No AI description of what is in a video | Needs a vision model | Optional cloud or local model describing the frame at the playhead, added to `Shift+W` |
| Online search is limited to about 20 searches a minute | Anonymous Openverse limit | Register a free Openverse application and use its token |
| Not yet tested by blind users with NVDA and JAWS | It's a new project | Follow `TESTING.md` with 5–8 testers and fix what they find |
| Audio is always mixed to stereo 48 kHz | Kept simple | Fine for almost all videos; add a 5.1 option only if asked |

## Next features, in suggested order

1. **libmpv preview**: instant play, scrubbing sound, reverse playback and frame stepping you can hear.
2. **Auto-ducking**: "Make music quieter when someone speaks", in one checkbox.
3. **Captions from speech** with Whisper, run on the computer with no upload. Captions become text items you can edit like any other.
4. **Speed**: 0.25 to 4 times, with pitch kept for speech.
5. **Ken Burns** pan and zoom for photos: "slowly zoom in", "pan left to right".
6. **Keyframed text and picture motion**: give a start place and an end place; the item glides between them.
7. **Picture filters and colour**: brightness, contrast, black and white, warm and cool, blur the background.
8. **Spoken frame check**: `F7` also warns when text covers a face or a logo, when text is too small to read on a phone, or when white text sits on a white area.
9. **Audio description track**: a layer for spoken descriptions, with ducking of the main sound (the ADCanvas idea).
10. **Recording**: record voice-over straight onto an audio layer at the playhead.
11. **More free media**: Freesound (free API key), and Openverse images and video for B-roll.
12. **Upload to YouTube** with the title, description and credits filled in.
13. **Translations** of the interface and the spoken text.

## Where to find free music you can use

Built into the program:

- **Library, Sounds**: 18 sound effects and music beds made by the program itself. They are public domain (CC0), free for any use with no credit.
- **Library, Free music online** (`Ctrl+Shift+O`): searches [Openverse](https://openverse.org), which collects openly licensed audio from Jamendo, Freesound, ccMixter, Wikimedia Commons and others. It only shows:
  - **CC0** and the **Public Domain Mark**: use freely, no credit needed;
  - **CC BY**: use freely, including in monetised videos, but you must credit the artist. The program saves the credit and writes it to a "credits" file next to your export, ready to paste into the video description.

  Licences that forbid commercial use (NC), forbid changes (ND) or force your video to use the same licence (SA) are left out on purpose, because trimming or monetising would break them.

Elsewhere (download, then put the files in Documents, Accessible Video Editor, Sound Effects, and they appear in the library):

| Source | Licence | Notes |
|---|---|---|
| [Freesound](https://freesound.org) | Each sound is CC0, CC BY or CC BY-NC | Filter by licence; use only CC0 and CC BY for monetised videos |
| [Incompetech (Kevin MacLeod)](https://incompetech.com/music/) | CC BY | Credit required, in the format the site gives |
| [Free Music Archive](https://freemusicarchive.org) | Various Creative Commons | Check each track; avoid NC for monetised videos |
| [ccMixter](https://ccmixter.org) | Various Creative Commons | Many CC BY tracks |
| [Wikimedia Commons audio](https://commons.wikimedia.org/wiki/Category:Audio_files) | Public domain and Creative Commons | Classical recordings, often public domain |
| [YouTube Audio Library](https://studio.youtube.com) | YouTube's own terms | Free for YouTube videos; read the terms before using elsewhere |

Always check the licence on the track's own page, and keep the credit file with your project.
