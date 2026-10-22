# Architecture

## Decisions, and how they changed from the research

The research (`RESEARCH.md`) recommended WPF, FFmpeg for export and libmpv for preview. The build kept WPF and FFmpeg, and replaced libmpv with **FFmpeg-rendered preview windows played by Windows Media Foundation** (WPF `MediaElement`):

- One engine renders both the preview and the export, so what you hear while editing is exactly what you export: same transitions, same overlays, same sound clean-up.
- There is no second native library to ship and keep up to date.
- A screen reader user mostly listens, so a preview that takes a second or two to prepare and then plays smoothly is a good trade. Previews are cached and start 10 seconds before the playhead, so `J` (jump back) is instant.

Real-time scrubbing and reverse playback are the cost. They could be added later with libmpv behind the same `PreviewService` interface.

## Layers of the program

```
WPF window (App)            keys → commands, focus, speech, earcons, dialogs, preview player
        │
EditorController (Core)     focus, playhead, listening mode; every keyboard command → Feedback
        │
ProjectEditor + Session     pure edits on the model; undo snapshots; failed edits roll back
        │
Model + Timeline            Project → Layers → Items; positions are computed, never stored
        │
GraphBuilder + RenderRunner the project as one FFmpeg filter graph; progress and cancel
```

### Model and timeline

Items in a layer have no stored start time. Each one starts after the previous one ends, plus its `GapBefore`, minus the overlap of its transition. `Timeline.Spans` is the one place that does this arithmetic. The spoken descriptions, the editing operations and the renderer all use it, so speech and video always agree.

A transition only plays when two items touch (no gap), and it never takes more than half of either item. That rule keeps every transition renderable.

### Edits

Every edit is a static method on `ProjectEditor` that returns an `EditResult` with a spoken message. `ProjectSession.Execute` takes a JSON snapshot first. If the edit fails or throws, the snapshot is restored, so a project is never left half-changed. The same snapshots give 200 levels of undo.

### Controller and feedback

`EditorController` holds what a screen reader user needs to know: which layer and item have focus, where the playhead is, and whether they are listening to one layer or all of them. `Execute(CommandId)` returns a `Feedback` record: what to say, which earcon to play, and what the player should do. The controller has no window code, so every keyboard workflow is unit-tested on any operating system (`EditorControllerTests`).

### Speech and focus in the window

- Screen readers read the UI Automation name of the focused list entry. Each entry's name is its full description.
- When a command moves focus (for example, delete moves to the next item), the command's message is put in front of the new entry's name for that one focus event. The user hears "Deleted clip 2. 2 of 4, clip 3, ..." as one utterance, and nothing is cut off.
- When focus stays on the same entry (slide, mute), the message goes through `Announcer`: NVDA's controller client, JAWS's COM API, or a UI Automation notification.
- While an entry has focus its name is frozen, so a screen reader doesn't re-read the whole entry after every small change. The new name applies when focus leaves.

### Rendering

`GraphBuilder` turns the project into a single FFmpeg filter graph:

1. Each item is normalised to the canvas size, frame rate and stereo 48 kHz sound, placed on a transparent canvas (`scale`, `pad`), and given its fades, opacity, volume and sound clean-up filters.
2. The items of a layer, and generated gaps, are joined with `concat`, or with `xfade` and `acrossfade` where there are transitions.
3. Video layers are stacked on a black base with `overlay` (`eof_action=pass`). All sound is mixed with `amix` (no automatic level changes) and passed through a limiter.

All lengths are rounded to whole frames and whole samples, so picture and sound stay in step through any number of transitions. The frame rate is restated after `trim`, `concat` and `xfade`, because FFmpeg 7 drops it and `xfade` refuses inputs without one. The integration tests caught this; they run on FFmpeg 6.1, 7.0 and the build bundled on Windows.

`ProjectWindow.StartingAt` cuts a project down to start at a given time. This lets a preview or a range export render only the part it needs.

## Testing

- **Unit tests**: time parsing, timeline arithmetic, every edit, undo, project files, descriptions, keymap and docs consistency.
- **Integration tests**: real renders, checked by length, by pixel colour (for example, that a top-right logo is really top right) and by loudness.
- **Controller tests**: keyboard workflows, exactly as a user presses the keys.
- **App self-test** (`AccessibleVideoEditor.exe --self-test log.txt`): on Windows CI it drives the real window. It presses keys, checks the UI Automation names that screen readers read, checks that every dialog control has a name, and runs a full export with the bundled FFmpeg.
- **Manual tests with screen readers**: see `TESTING.md`.
