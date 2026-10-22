# Research: A Screen-Reader-First Video Editor for Windows

Status: research and recommendation, October 2026
Target users: blind and low-vision creators using **NVDA** and **JAWS** (Narrator as a secondary target)
Core jobs: **trim**, **add audio**, **combine multiple videos**, **layer images and videos** (picture-in-picture, logos, title cards), and **export high-quality video**.

---

## 1. Summary

- No mainstream editor (Premiere, Resolve, Final Cut, Shotcut, Kdenlive, OpenShot) works well with a screen reader. The timeline, the part every edit depends on, is a custom-drawn canvas that NVDA and JAWS cannot read.
- Blind creators get by today with **Reaper + OSARA** (an audio tool used for video), **FFmpeg on the command line**, and a few new projects. Research prototypes such as **AVscript** (CHI 2023) found that editing through *text and lists* gave blind editors lower mental demand and more confidence than their usual tools.
- **Recommendation:** don't fork an existing editor. Build a new, small Windows-native UI from **standard controls**. Represent the timeline as **lists and spoken summaries**, not a canvas, and build on proven open-source engines:

| Layer | Recommended | Why |
|---|---|---|
| UI | **C# / .NET 8 + WPF** (standard controls only) | Full UI Automation (UIA) support, `AutomationProperties`, live regions, and `RaiseNotificationEvent` for announcements; native Windows look and behaviour |
| Speech feedback | UIA notifications first, with **Tolk** as an optional direct NVDA/JAWS channel | Announcements go through the user's own screen reader, voice and verbosity settings |
| Playback / preview / scrubbing | **libmpv** (via Mpv.NET or similar) | Frame-accurate seeking, audio scrubbing, plays almost every format, embeds in a window handle |
| Render / export engine | **FFmpeg** (generated `filter_complex`) | Industry-standard quality; trim, concat, overlay, amix, scale and fade cover every v1 feature |
| Project file | Own JSON model, with **OpenTimelineIO** export later | Simple, diff-friendly; OTIO allows hand-off to other editors |
| Optional later engine | **MLT** (the engine behind Shotcut) | Real-time multitrack composite preview if it's needed later |

The rest of this document explains each choice and sets out the UX design, keyboard map and roadmap.

---

## 2. What exists today

### 2.1 Mainstream open-source editors

| Editor | Engine / toolkit | Screen-reader reality | Reuse value |
|---|---|---|---|
| **Shotcut** | MLT + Qt (QML) | Menus and some dialogs are readable, but the timeline is not. A community "guide for blind users" says to skip the timeline and edit one clip at a time with `I`/`O` in the Source player. A blind user reported they could move through a clip but not select a section of it. | Use its engine (MLT), not its UI |
| **Kdenlive** | MLT + Qt/KDE | No meaningful screen-reader support found; it has a rich shortcut set but a visual timeline | Engine only |
| **OpenShot** | libopenshot + Qt/PyQt | Users on the Orca list described it as less accessible than even Pitivi | Low |
| **Pitivi** | GStreamer Editing Services + GTK | Linux-first; Windows support is weak | GES is a clean timeline API, but awkward on Windows |
| **Avidemux / LosslessCut** | FFmpeg | Simple cut tools; LosslessCut is Electron with a custom UI | Good ideas for lossless trim |

**Qt on Windows caveat:** Qt switched from MSAA to UI Automation in 5.15/6.x. Bug reports (QTBUG-90897, QTBUG-91029) describe NVDA and Narrator losing focus announcements on child widgets after the switch. Custom-painted Qt widgets also need hand-written `QAccessibleInterface` code, and NVDA has called that code differently from Orca. Making an existing Qt editor accessible would mean rewriting most of its UI anyway.

### 2.2 Tools blind creators actually use

- **Reaper + OSARA** (Windows and macOS, works with NVDA, JAWS and VoiceOver). OSARA adds spoken feedback and keyboard actions to Reaper. Blind users already edit video soundtracks in Reaper because it is non-destructive, everything is keyboard-driven, and edits are announced. **This is the closest working model to copy.**
- **Audacity**: works well with JAWS, NVDA and Narrator on Windows. Its keyboard-editable Selection Toolbar (start/end/length fields that you type into) is a proven pattern for precise selection without a mouse.
- **FFmpeg CLI**: fully accessible because it's text, but it takes expert knowledge.
- **Ultra Creative Suite**: a Windows video editor built by a blind JAWS user. It shows there is demand, but we could not confirm its source or license.
- **zEdit** (iOS, VoiceOver): the "fully accessible video editor" pitch exists on mobile, not on Windows.

### 2.3 Academic research (worth reading before designing)

- **AVscript** (Huh et al., CHI 2023): text-based editing using an "audio-visual script" that mixes the speech transcript, scene descriptions and detected visual errors (blur, dark frames). In a study with 12 blind and low-vision editors, it reduced mental demand compared with their usual tools (Reaper, FFmpeg). It also found that even "accessible" timeline tools had unlabeled controls.
- **Understanding Digital Content Creation Needs of BLV People** (ASSETS 2023): few tools support non-visual video editing, and there is demand for accessible control over effects.
- **Video content creation by creators with sensory impairment in Kenya** (2025): there is no non-visual way to judge quality or move through a timeline.
- **ADCanvas** (2026): keyboard-driven, plain-text authoring of audio description with conversational AI.

**Lessons for us:** (1) present the timeline as structured text and lists; (2) give a spoken way to check what's on screen (black frames, a missing overlay, where a logo is); (3) let the transcript drive cuts (a later phase).

---

## 3. Open-source building blocks, compared

### 3.1 Media engines

| Engine | License | Strengths | Weaknesses for us |
|---|---|---|---|
| **FFmpeg** (libav*, CLI) | LGPL 2.1+; GPL if built with x264/x265 | Best codec coverage and quality; `trim`, `atrim`, `concat`, `overlay`, `amix`, `adelay`, `afade`, `scale`, `pad`, `drawtext` cover every v1 feature; a single `ffmpeg.exe` is easy to ship | Not an interactive timeline; filtergraphs must be generated; no real-time composite preview |
| **MLT** (`melt`, MLT XML) | LGPL 2.1 core; some modules GPL | Built for multitrack timelines; powers Shotcut and Kdenlive on Windows; XML projects; real-time preview; transitions and filters | Python/C# bindings are hard to build on Windows (MSYS2 + SWIG); we would ship Shotcut-sized dependencies |
| **GStreamer Editing Services (GES)** | LGPL | Clean object model (Timeline → Layers → Clips); good bindings (Rust, Python, C) | Windows packaging is heavy; smaller editing community than MLT |
| **libmpv** | LGPL (when built that way) / GPL | Excellent player: frame stepping, exact seeks, audio while scrubbing, speed control, embeds into a window handle | Player, not a renderer |
| **OpenTimelineIO** | Apache 2.0 | Standard interchange (EDL, FCP XML, AAF, MLT adapters) | Format only, no rendering |

**Decision:** use **FFmpeg for export** and **libmpv for preview** in v1. Keep the project model engine-neutral so an **MLT backend** can be added later if real-time multilayer preview becomes important (mostly useful for sighted collaborators; blind users mainly preview audio and get spoken checks).

**Licensing:** H.264 export through x264 makes the shipped FFmpeg build GPL. Simplest path: release the app as **GPL-3.0**, which allows bundling x264, FFmpeg and MLT freely. (An LGPL FFmpeg build with Windows Media Foundation encoders is the fallback.)

### 3.2 UI toolkits for NVDA and JAWS

| Toolkit | Screen-reader quality on Windows | Notes |
|---|---|---|
| **WPF (.NET 8)** | Very good with standard controls | Full UIA tree; `AutomationProperties.Name/HelpText/LiveSetting`; `AutomationPeer.RaiseNotificationEvent` for announcements; native Windows UX |
| **WinForms (.NET 8)** | Excellent; these are real Win32 controls, which JAWS has supported for decades | Older look; fine for a minimal UI |
| **WinUI 3** | Good UIA support, but younger | More packaging friction |
| **wxPython** | Good; uses native Win32 controls | Popular with blind developers (e.g. TWBlue). Labels must be created right before their input; custom widgets need `wx.Accessible` |
| **Qt** | Mixed (see 2.1) | Avoid for custom widgets |
| **Electron / web** | Good if ARIA is perfect, but browse/focus-mode switching confuses users in an app | Not "native" |
| **Tk** | Poor | Avoid |

**Decision: WPF with only standard controls** (`ListView`, `TreeView`, `TextBox`, `ComboBox`, `Slider`, `Menu`, `StatusBar`). No custom-drawn control is ever focusable. A visual waveform or timeline strip may be drawn for low-vision and sighted helpers, but it is decorative and kept out of the accessibility tree.

**Speech feedback strategy**
1. **UIA notification events** (`RaiseNotificationEvent`, with `ImportantMostRecent` for playhead updates and `All` for confirmations). The user's own screen reader speaks them, using the voice and rate they chose.
2. **Live-region status bar** (`AutomationProperties.LiveSetting="Polite"`) as a fallback, also readable on demand.
3. **Optional Tolk** (LGPL) or the **NVDA Controller Client** for direct speech when UIA notifications aren't spoken (e.g. older JAWS versions). This is a user setting: "Speech output: Automatic / Screen reader API / UI Automation".
4. **Earcons** (short sounds) for frequent events: hitting a clip edge, snapping, start/end of the project, a selection being made. These are faster than speech and can be turned off.

---

## 4. Accessible UX design

### 4.1 Principles

1. **The timeline is a list, not a picture.** Each track is a list; each clip is a list item that announces a full sentence.
2. **Everything is reachable from the keyboard with one or two keys.** The mouse is never required.
3. **Every action is confirmed in speech, briefly:** "Split. Clip 3 now ends at 1 minute 12.4 seconds."
4. **Every action can be undone** (`Ctrl+Z`), and undo says what it reversed.
5. **Plain-language positions instead of dragging:** "Top right, 25 percent size" instead of x/y pixels (pixels are still available for precision).
6. **Times are typed, not dragged:** any time field accepts `1:23.5`, `83.5`, `+5s` or `-2s`.
7. **Minimal UI:** one main window, three panes, no floating toolbars, no icon-only buttons.
8. **Verbosity levels** (Beginner / Normal / Expert), like OSARA and NVDA.
9. **Behave like Windows:** standard menus with access keys (`Alt+F`), standard dialogs, `F6` cycles panes, `Shift+F10` / the Applications key opens context menus.

### 4.2 Main window layout (in reading and `F6` order)

```
┌ Menu bar: File  Edit  Clip  Track  Playback  Export  Help
├ Pane 1  Media Bin (ListView)   - imported files: name, type, length, resolution
├ Pane 2  Timeline (TreeView)    - Tracks → clips, in time order
│      Video 1 (main)            ▸ Clip 1: intro.mp4, 0:00 to 0:12.5, 12.5 s
│      Overlay 1 (images/video)  ▸ logo.png, top right 20%, 0:00 to end
│      Audio 1 (clip sound)      ▸ linked to Video 1
│      Audio 2 (music)           ▸ music.mp3, 0:00 to 3:10, volume 30%, fade out 3 s
├ Pane 3  Inspector (form)       - editable fields for the selected clip
└ Status bar (live region)       - playhead, selection, project length, last action
```

When a clip in the Timeline gets focus, it announces, for example:
> "Video 1, clip 2 of 5, interview.mp4, starts 12.5 seconds, length 30 seconds, ends 42.5 seconds."

Verbosity settings choose which parts are spoken.

### 4.3 Editing model (borrowed from Reaper/OSARA and Audacity)

- **Playhead:** one shared position. `Space` plays and pauses; `J`/`K`/`L` shuttle; `Left`/`Right` step by 1 frame in the Timeline (with `Ctrl` for 1 second, `Ctrl+Shift` for 10 seconds). Audio scrubs while you move.
- **Selection by marks:** `I` sets the in mark, `O` sets the out mark, `Ctrl+I`/`Ctrl+O` jump to them. The status bar says "Selection 12.0 to 18.5, 6.5 seconds."
- **Type an exact time:** `Ctrl+G` → "Go to time" dialog.
- **Trim:** `[` trims the clip start to the playhead, `]` trims the clip end to the playhead, `S` splits at the playhead, `Delete` removes, `Shift+Delete` ripple-deletes (closes the gap).
- **Precise trim:** the Inspector fields **Source in**, **Source out**, **Timeline start** and **Length** are editable text boxes, like Audacity's Selection Toolbar.
- **Moving clips:** `Alt+Left/Right` nudges by a frame or a second; `Alt+Up/Down` moves the clip to the track above or below; or type "Timeline start".

### 4.4 Multiple videos, audio and layers

- **Add multiple videos:** select files in the Media Bin (or the Open dialog, multi-select) → `Enter` adds them at the end of Video 1 in order; `Ctrl+Enter` inserts at the playhead.
- **Add audio:** `Ctrl+Shift+A` adds the chosen file to a new or existing audio track at the playhead. The Inspector has Volume (%), Fade in, Fade out, Mute and "Duck under speech" (sidechain-style ducking, done with FFmpeg `sidechaincompress`).
- **Image/video overlays (layers):** `Ctrl+Shift+O` adds the file to an Overlay track. The Inspector has:
  - **Position:** a combo box with 9 presets (Top left … Center … Bottom right), Full screen, or Custom (X, Y)
  - **Size:** a percentage of the frame
  - **Opacity, Fade in/out, Start, Length**
  - "Length to end of project" checkbox (for logos and watermarks)
- **Layer order** is the track order: Overlay 2 sits above Overlay 1. `Ctrl+Up/Down` on a track reorders it, and the app announces "Overlay 2 is now above Overlay 1."
- **Title cards and text:** "Add text" creates a text clip (font, size, color by name, position preset), drawn with FFmpeg `drawtext`.

### 4.5 Non-visual checks (from the AVscript findings)

- **`Ctrl+Shift+W` "What's on screen?"** speaks the layers visible at the playhead: "Video 1 interview.mp4; Overlay 1 logo.png top right 20 percent; Text 'Chapter 2' bottom center."
- **Project check (`F7`)**, a list of warnings: gaps (black frames), clips with no audio, overlays outside the frame, audio clipping or very low loudness (measured with FFmpeg `ebur128`), mixed frame rates or resolutions.
- **Optional AI scene description** of the frame at the playhead (later phase, opt-in).

### 4.6 Export ("high quality made easy")

`Ctrl+E` opens one dialog with presets:

| Preset | Settings |
|---|---|
| YouTube 1080p (recommended) | H.264 High, CRF 18, 30/60 fps matching the source, AAC 320 kb/s, loudness −14 LUFS |
| YouTube 4K | H.264/H.265 2160p, CRF 18 |
| Social square/vertical | 1080×1080 / 1080×1920 with a blurred or padded background |
| Audio only | MP3/WAV/M4A |
| Archive / lossless-ish | ProRes or H.264 CRF 10 |

Progress is announced every 10% (configurable). On completion the app says "Export finished, 2 minutes 31 seconds, 182 MB" and offers *Open folder* / *Play*.

---

## 5. Proposed keyboard map (first draft)

| Area | Key | Action |
|---|---|---|
| Global | `F6` / `Shift+F6` | Next / previous pane |
| | `Ctrl+N / O / S` | New / open / save project |
| | `Ctrl+I` (in Media Bin) | Import media |
| | `Ctrl+E` | Export |
| | `Ctrl+Z / Ctrl+Y` | Undo / redo (spoken) |
| | `F1` | Context help for the focused item |
| | `Ctrl+Shift+H` / `?` | Spoken list of shortcuts for the current pane |
| Playback | `Space` | Play / pause |
| | `J / K / L` | Reverse / stop / forward (press again for faster) |
| | `Home / End` | Project start / end |
| | `Left / Right` | ±1 frame |
| | `Ctrl+Left / Right` | ±1 second |
| | `Ctrl+Shift+Left / Right` | ±10 seconds |
| | `Ctrl+G` | Go to time |
| | `T` | Speak playhead time |
| Selection | `I / O` | Set in / out mark |
| | `Shift+I / O` | Clear marks |
| | `Ctrl+A` | Select whole project |
| Editing | `S` | Split at playhead |
| | `[ / ]` | Trim clip start / end to playhead |
| | `Delete / Shift+Delete` | Delete / ripple delete |
| | `Ctrl+C / X / V` | Copy / cut / paste at playhead |
| | `Alt+Left / Right` | Nudge clip |
| | `Alt+Up / Down` | Move clip to track above / below |
| Navigation | `Up / Down` | Previous / next clip in track |
| | `Ctrl+Up / Down` (on track) | Reorder track (layer order) |
| | `Tab` (in Timeline) | Next track |
| | `Enter` | Open the clip in the Inspector |
| Adding | `Ctrl+Shift+V` | Add video at end |
| | `Ctrl+Shift+A` | Add audio |
| | `Ctrl+Shift+O` | Add image/video overlay |
| | `Ctrl+Shift+T` | Add text/title |
| Checks | `Ctrl+Shift+W` | What's on screen at the playhead |
| | `F7` | Project check |
| Verbosity | `Ctrl+Shift+F12` | Cycle Beginner / Normal / Expert |

Rules: keys must not clash with NVDA (`Insert`/`CapsLock` combinations) or JAWS (`Insert` combinations); all keys can be remapped and the keymap is saved as editable JSON; single-letter keys only work while the Timeline has focus (never in text fields).

---

## 6. Architecture

```
┌────────────────────── WPF app (C# / .NET 8) ───────────────────────┐
│  Views: MediaBin · Timeline (TreeView) · Inspector · StatusBar     │
│  Announcer: UIA notifications │ Tolk (optional) │ earcons          │
│  Commands + undo stack  ◀── Keymap (JSON)                          │
├────────────────────────────────────────────────────────────────────┤
│  Core (UI-free, unit-tested):                                      │
│   Project model: Project → Tracks(Video/Overlay/Audio/Text) → Clips│
│   Time math (frames ↔ seconds), validation, project check          │
│   Describer: builds the spoken sentences for each object           │
├────────────────────────────────────────────────────────────────────┤
│  Engines:                                                          │
│   PreviewEngine  → libmpv (playback, scrub, frame-accurate seek)   │
│   RenderEngine   → FFmpeg (filter_complex generated from model)    │
│   ProbeService   → ffprobe (duration, fps, size, audio streams)    │
│   Interchange    → OpenTimelineIO / MLT XML export (later)         │
└────────────────────────────────────────────────────────────────────┘
```

Example of what the RenderEngine generates for "two videos in a row, logo overlay top right at 20%, music at 30% with a 3 s fade out":

```
ffmpeg -i a.mp4 -i b.mp4 -i logo.png -i music.mp3 -filter_complex "
 [0:v]trim=0:12.5,setpts=PTS-STARTPTS[v0];[0:a]atrim=0:12.5,asetpts=PTS-STARTPTS[a0];
 [1:v]trim=3:33,setpts=PTS-STARTPTS[v1];[1:a]atrim=3:33,asetpts=PTS-STARTPTS[a1];
 [v0][a0][v1][a1]concat=n=2:v=1:a=1[base][dialog];
 [2:v]scale=384:-1[logo];          (20% of a 1920-wide frame)
 [base][logo]overlay=W-w-40:40[vout];
 [3:a]volume=0.3,afade=t=out:st=39.5:d=3[mus];
 [dialog][mus]amix=inputs=2:duration=first:normalize=0[aout]"
 -map [vout] -map [aout] -c:v libx264 -crf 18 -preset slow -c:a aac -b:a 320k out.mp4
```

(Timestamps must be reset with `setpts=PTS-STARTPTS` after `trim`; build `amix` graphs carefully, since FFmpeg has had crash bugs when combining `concat` and `amix` on many streams. Test with the FFmpeg version we bundle.)

---

## 7. Roadmap

| Phase | Scope | Done when |
|---|---|---|
| **0 – Prototype (2–3 weeks)** | WPF shell, Media Bin, one video track, play/scrub with libmpv, `I`/`O`, split, delete, export via FFmpeg | A blind tester trims and exports a clip using NVDA and JAWS without help |
| **1 – MVP** | Multiple videos, audio tracks (volume, fades), overlay track (images and video, position presets), undo/redo, project save/load, export presets, verbosity levels | Testers do the full "intro + 2 clips + logo + music" task |
| **2 – Quality** | Project check, "What's on screen?", loudness normalization, ducking, text/title clips, transitions (crossfade) | Fewer errors in exported videos in testing |
| **3 – Smart editing** | Transcript-based editing (Whisper, run locally): delete words to cut video; optional AI scene descriptions; OTIO/MLT export | AVscript-style workflow available |
| **4 – Polish** | Remappable keys UI, translations, installer + portable build, NVDA add-on / JAWS scripts only if needed | 1.0 release |

---

## 8. Accessibility testing plan

- **Screen readers:** latest NVDA, current JAWS (and the previous major version), Narrator. Test with focus mode and with default settings.
- **Automated checks:** Accessibility Insights for Windows (FastPass) on every view; Inspect.exe to check names, roles and patterns; UI tests that every focusable element has a non-empty `Name`.
- **Scripted manual tests:** each user task (import → trim → add music → add logo → export) run with NVDA and with JAWS; record time on task and errors.
- **Real users:** recruit 5–8 blind editors (Reaper/OSARA users are a good group, e.g. via the OSARA, audiogames.net and NVDA communities). Hold sessions each phase.
- **Rules that block a release:** no unlabeled control; no keyboard trap; every action announced; nothing reachable only by mouse; announcements under about 1 second for navigation.

---

## 9. Open questions for the project owner

1. **Language:** C#/WPF (recommended) or Python/wxPython (more familiar to many blind developers, but slower video previews and harder packaging)?
2. **License:** GPL-3.0 (simplest with x264/FFmpeg/MLT) — OK?
3. **Minimum Windows version:** Windows 10 22H2+ and Windows 11?
4. **Do we need real-time composite preview in v1** (for sighted collaborators), or is audio preview plus spoken checks enough? If yes, start with MLT instead of FFmpeg-only.
5. **AI features** (transcription, scene description): local only, cloud allowed, or opt-in?

---

## Sources

Editors and engines
- MLT Multimedia Framework — https://mltframework.org
- MLT Python bindings on Windows (Shotcut forum) — https://forum.shotcut.org/t/have-anyone-make-python-bindings-on-windows-for-mltframwork/32943
- otio-mlt-adapter — https://pypi.org/project/otio-mlt-adapter
- GStreamer Editing Services docs — https://gstreamer.freedesktop.org/documentation/gst-editing-services/ and https://gstreamer.freedesktop.org/documentation/gst-editing-services/gestimeline.html
- OpenTimelineIO — https://github.com/AcademySoftwareFoundation/OpenTimelineIO , https://opentimelineio.readthedocs.io/
- Mpv.NET (embedding libmpv by window handle) — https://github.com/hudec117/Mpv.NET-lib-
- MPVSharp — https://github.com/NickvisionApps/MPVSharp/blob/main/README.md
- python-mpv — https://awesome.ecosyste.ms/projects/github.com%2Fjaseg%2Fpython-mpv
- FFmpeg editing essentials — https://www.bannerbear.com/blog/ffmpeg-video-editing-essentials-trimming-merging-subtitling-and-more/
- FFmpeg cheatsheet (setpts after trim) — https://docsearch.algolia.com/mcp/docs/repo/rendi-api/ffmpeg-cheatsheet
- FFmpeg concat + amix crash report — https://ffmpeg.org/pipermail/ffmpeg-trac/2020-August/055424.html

Accessibility of existing tools
- Shotcut: A guide for blind users — https://forum.shotcut.org/t/a-guide-for-blind-users/13213
- Shotcut: Accessibility & keyboard question — https://forum.shotcut.org/t/accessibility-keyboard-shortcut-question/12698
- Shotcut: Accessibility for the visually impaired — https://forum.shotcut.org/t/accessibility-for-the-visually-impaired/48171
- Orca list on OpenShot/Pitivi — https://lists.gnome.org/archives/orca-list/2018-May/msg00089.html
- Reaper + OSARA (AFB AccessWorld) — https://afb.org/aw/23/6/17971
- Audacity accessibility manual — https://manual.audacityteam.org/man/accessibility.html
- Audacity Selection Toolbar — https://manual.audacityteam.org/man/selection_toolbar.html
- Ultra Creative Suite article — https://www.squaredtech.co/blind-developer-builds-the-best-accessible-video-editor
- zEdit (iOS) — https://mwm.ai/apps/zedit/6768833999

UI toolkits and speech
- Qt accessibility — https://doc.qt.io/qt-5/accessible.html
- Qt UIA regressions — https://bugreports-test.qt.io/browse/QTBUG-90897 , https://bugreports-test.qt.io/browse/QTBUG-91029
- Qt/NVDA interface issue — https://lists.qt-project.org/pipermail/interest/2017-July/027512.html
- wxPython + NVDA — https://discuss.wxpython.org/t/creating-nvda-accessible-applications/40334
- wx.Accessible regression — https://discuss.wxpython.org/t/possible-regression-wx-accessible-causes-screen-readers-to-announce-generic-labels-if-wx-control-name-kwarg-is-empty/30570
- Python GUI accessibility thread — https://mail.python.org/pipermail/tutor/2019-June/114923.html
- UI Automation events overview — https://learn.microsoft.com/en-us/dotnet/framework/UI-automation/ui-automation-events-overview
- Announcing Win32 UI changes to Narrator — https://learn.microsoft.com/zh-cn/archive/blogs/winuiautomation/how-to-have-important-changes-in-your-win32-ui-announced-by-narrator
- accessible_output2 — https://pypi.org/project/accessible-output2/
- Tolk / screen-reader output overview (Lights Off docs) — https://lights-off.readthedocs.io/en/latest/screen-readers/overview/
- JAWS — https://www.freedomscientific.com/products/software/jaws/
- NVDA — https://www.nvaccess.org/download/

Research
- AVscript (CHI 2023) — https://arxiv.org/pdf/2302.14117 , https://minahuh.com/AVscript/
- Digital content creation needs of BLV people (ASSETS 2023) — https://dl.acm.org/doi/fullHtml/10.1145/3597638.3608387
- Creators with sensory impairment in Kenya (2025) — https://dx.doi.org/10.1145/3663547.3746356
- ADCanvas (2026) — https://research.google/pubs/adcanvas-accessible-and-conversational-audio-description-authoring-for-blind-and-low-vision-creators/
