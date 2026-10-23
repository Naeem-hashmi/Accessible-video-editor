# Keyboard reference

This file is generated from the program's command table (`tools/AccessibleVideoEditor.AssetGenerator --docs docs`). Do not edit it by hand.

Keys without Control or Alt (letters, arrows, Space, Enter, Delete, brackets) work while the timeline, the layer list or the preview has focus. Every key can be changed in the keyboard settings file (Tools menu).

**Laptops:** every command has at least one key that needs no number pad, no Fn key (Home, End, F1 to F12) and no AltGr (brackets on many non-US keyboards). Where the first key listed is one of those, the second one is the laptop-friendly alternative, for example `I` and `O` for range start and end, `Ctrl+Left` and `Ctrl+Right` for first and last item, `Ctrl+H` for help and `Alt+.` for the next area.

## File

| Keys | Command | What it does |
|---|---|---|
| `Ctrl+N` | New project from template | Start a new project from a built-in or saved template. |
| `Ctrl+Shift+N` | Save as template | Save this project as a template in a folder of your choice, with or without its media. |
| `Ctrl+Shift+K` | Manage templates | List templates by folder: create a project, rename, move, delete, import or open the folder. |
| `Ctrl+O` | Open project | Open a saved project. |
| `Ctrl+S` | Save project | Save the project. |
| `Ctrl+Shift+S` | Save project as | Save the project under a new name. |
| `Ctrl+E` | Export video | Make the finished video or audio file. |
| `Ctrl+Shift+P` | Project settings | Title, picture size and frame rate. |
| `Ctrl+Shift+,` | Preferences | Speech, sounds, step sizes and folders. |

## Edit

| Keys | Command | What it does |
|---|---|---|
| `Ctrl+Z` | Undo | Undo the last change. Says what was undone. |
| `Ctrl+Y` or `Ctrl+Shift+Z` | Redo | Redo what you undid. |
| `Ctrl+C` | Copy item | Copy the focused item. |
| `Ctrl+X` | Cut item | Copy the focused item and remove it. |
| `Ctrl+V` | Paste item | Paste the copied item after the focused item. |
| `Ctrl+D` | Duplicate item | Put a copy of the focused item right after it. |
| `Delete` | Delete item | Delete the focused item. With ripple on, later items move up to close the space. |
| `Ctrl+Delete` | Delete item, keep timing | Delete the focused item and leave silence in its place, so later items do not move. |
| `Shift+Delete` | Delete range on this layer | Remove everything between range start and range end on the focused layer. |
| `Ctrl+R` | Ripple editing on or off | With ripple on, deleting, trimming and sliding move later items too. With ripple off, later items keep their time. |
| `F2` or `Shift+Enter` | Edit text or rename | Change the words of a text item, or rename any other item. |
| `Enter` | Item options | Open all options for the focused item: length, speed, volume, fades, transition, position, sound clean-up and equaliser. With several items selected, sets options for all of them. |
| `Menu key` or `Shift+F10` | Options menu | Open the menu of everything you can do with the focused item, the selected items or the focused layer. |
| `Shift+Left` | Select to the previous item | Add the previous item to the selection and go to it. Delete, copy, duplicate, mute, volume, slide, length and speed then work on every selected item. |
| `Shift+Right` | Select to the next item | Add the next item to the selection and go to it. |
| `Ctrl+Space` | Select or unselect item | Add the focused item to the selection, or take it out. Lets you pick items that are not next to each other. |
| `Ctrl+A` | Select all items on layer | Select every item on the focused layer. Escape clears the selection. |

## Add

| Keys | Command | What it does |
|---|---|---|
| `Ctrl+I` | Add files to end of layer | Choose video, picture or audio files and add them after the last item of the focused layer. Sound files go to an audio layer automatically. |
| `Ctrl+Shift+I` | Add files at playhead | Add files at the playhead time without moving anything; a free layer is used or created. |
| `Ctrl+Shift+E` | Add sound effect or music | Choose a built-in or your own sound and add it at the playhead. |
| `Ctrl+Shift+T` | Add text | Add a title, caption or credit at the playhead, with style, position and animation. |
| `Ctrl+Shift+O` | Find free music online | Search Openverse for music and sound effects you may use freely (public domain or credit required). Credits are added to the project. |
| `Ctrl+Shift+C` | Add colour card | Add a solid colour picture, for a title background or a pause. |
| `Ctrl+Shift+V` | New video layer | Add a video layer above the others. |
| `Ctrl+Shift+A` | New audio layer | Add an audio layer. |
| `Ctrl+Shift+Delete` | Delete layer | Delete the focused layer and everything on it. |
| `Alt+Enter` | Layer options | Name, volume and defaults for new items on the focused layer. |

## Move around

| Keys | Command | What it does |
|---|---|---|
| `Left` | Previous item | Go to the previous item on this layer. The playhead moves to its start. |
| `Right` | Next item | Go to the next item on this layer. The playhead moves to its start. |
| `Home` or `Ctrl+Left` | First item | Go to the first item on this layer. |
| `End` or `Ctrl+Right` | Last item | Go to the last item on this layer. |
| `Up` | Layer above in the list | Go to the previous layer, to the item at the playhead. Works with any number of layers. |
| `Down` | Layer below in the list | Go to the next layer, to the item at the playhead. Works with any number of layers. |
| `Alt+1` | Go to layer 1 | Focus layer 1 and listen to it on its own. |
| `Alt+2` | Go to layer 2 | Focus layer 2 and listen to it on its own. |
| `Alt+3` | Go to layer 3 | Focus layer 3 and listen to it on its own. |
| `Alt+4` | Go to layer 4 | Focus layer 4 and listen to it on its own. |
| `Alt+5` | Go to layer 5 | Focus layer 5 and listen to it on its own. |
| `Alt+6` | Go to layer 6 | Focus layer 6 and listen to it on its own. |
| `Alt+7` | Go to layer 7 | Focus layer 7 and listen to it on its own. |
| `Alt+8` | Go to layer 8 | Focus layer 8 and listen to it on its own. |
| `Alt+9` | Go to layer 9 | Focus layer 9 and listen to it on its own. |
| `Alt+0` | Listen to all layers | Play every layer together (muted layers stay silent). Press again to listen to the focused layer only. |
| `Ctrl+L` | Go to layer number | Type a layer number to jump to it. Useful with more than 9 layers. |
| `Alt+Left` | Previous marker | Jump the playhead back to the previous marker, range start or range end. |
| `Alt+Right` | Next marker | Jump the playhead forward to the next marker, range start or range end. |
| `Ctrl+G` | Go to time | Type a time to move the playhead, for example 1:30, 90, or +5. |
| `Alt+Shift+B` | Previous beat | Move the playhead to the previous beat of the music (after Find beats). |
| `Alt+B` | Next beat | Move the playhead to the next beat of the music (after Find beats). |
| `Ctrl+Home` or `Ctrl+Shift+Left` | Go to start | Move the playhead to the start of the project. |
| `Ctrl+End` or `Ctrl+Shift+Right` | Go to end | Move the playhead to the end of the project. |
| `F6` or `Alt+.` | Next area | Move to the next area of the window: library, timeline, layers, properties, preview. |
| `Shift+F6` or `Alt+,` | Previous area | Move to the previous area of the window. |
| `Ctrl+1` | Go to library | The left area: media, sounds, online music, text styles, templates, import and export. |
| `Ctrl+2` | Go to timeline | The items of the focused layer. |
| `Ctrl+3` | Go to properties | The right area: quick settings for the focused item. |
| `Ctrl+4` | Go to preview | The picture area. It describes what is on screen at the playhead. |
| `Ctrl+5` | Go to layer list | The list of all layers. |

## Arrange

| Keys | Command | What it does |
|---|---|---|
| `Alt+Shift+Left` | Move item earlier in order | Swap the focused item with the one before it. |
| `Alt+Shift+Right` | Move item later in order | Swap the focused item with the one after it. |
| `Alt+Shift+Home` or `Alt+Shift+,` | Move item to the start | Make the focused item the first on its layer. |
| `Alt+Shift+End` or `Alt+Shift+.` | Move item to the end | Make the focused item the last on its layer. |
| `Alt+J` | Slide item earlier | Slide the focused item earlier by the slide step (1 second unless changed in preferences). |
| `Alt+L` | Slide item later | Slide the focused item later by the slide step. |
| `Alt+Shift+J` | Slide item earlier a little | Slide the focused item earlier by the fine step (a tenth of a second). Hold the keys to go faster and faster. |
| `Alt+Shift+L` | Slide item later a little | Slide the focused item later by the fine step. Hold the keys to go faster and faster. |
| `Ctrl+Shift+J` | Slide whole layer earlier | Move every item on the focused layer earlier by the slide step. |
| `Ctrl+Shift+L` | Slide whole layer later | Move every item on the focused layer later by the slide step. |
| `Alt+Shift+Up` | Move layer up the list | Give the focused layer a lower number. For video layers this puts its picture further back. |
| `Alt+Shift+Down` | Move layer down the list | Give the focused layer a higher number. For video layers this brings its picture to the front. |
| `Alt+=` or `Alt+Num+` | Make longer | Stretch the focused item by the slide step, keeping its start: a picture or text stays on longer, a clip shows more of its file. Hold to go faster. |
| `Alt+-` or `Alt+Num-` | Make shorter | Shorten the focused item by the slide step, keeping its start. |
| `Alt+Shift+=` or `Alt+Shift+Num+` | Make a little longer | Stretch the focused item by the fine step. |
| `Alt+Shift+-` or `Alt+Shift+Num-` | Make a little shorter | Shorten the focused item by the fine step. |
| `Alt+]` or `Ctrl+Shift+=` | Play clip faster | Speed up the focused video or sound clip one step (up to 4 times). It gets shorter but keeps all its content; sound keeps its pitch. |
| `Alt+[` or `Ctrl+Shift+-` | Play clip slower | Slow down the focused clip one step (down to a quarter speed, slow motion). It gets longer. |
| `Alt+Shift+]` or `Ctrl+Shift+0` | Normal speed | Play the focused clip at its normal speed again. |

## Cut and mark

| Keys | Command | What it does |
|---|---|---|
| `S` | Split at playhead | Cut the item at the playhead into two items. |
| `Shift+[` or `Shift+I` | Trim start to playhead | Remove the part of the item before the playhead. |
| `Shift+]` or `Shift+O` | Trim end to playhead | Remove the part of the item after the playhead. |
| `[` or `I` | Set range start | Mark the playhead as the start of a range. I (in) works on every keyboard. |
| `]` or `O` | Set range end | Mark the playhead as the end of a range. O (out) works on every keyboard. |
| `Ctrl+Shift+R` | Clear range | Remove the range start and end marks. |
| `B` | Add marker | Put a named marker at the playhead. |
| `Shift+B` | Markers and their options | List all markers to jump, rename, move, delete, split or add a transition there. |
| `Ctrl+T` | Transition at playhead | Choose the transition at the join nearest the playhead on the focused layer. |

## Play and listen

| Keys | Command | What it does |
|---|---|---|
| `K` or `Space` | Play or pause | Play from the playhead, or pause. |
| `L` | Play forward, faster | Play; press again for double and quadruple speed. |
| `J` | Jump back | Jump the playhead back by the jump step (5 seconds). |
| `Escape` | Stop | Stop playing. The playhead stays where it stopped. |
| `,` | Back 1 second | Move the playhead back 1 second. |
| `.` | Forward 1 second | Move the playhead forward 1 second. |
| `Shift+,` | Back 10 seconds | Move the playhead back 10 seconds. |
| `Shift+.` | Forward 10 seconds | Move the playhead forward 10 seconds. |
| `Ctrl+,` | Back 1 frame | Move the playhead back one frame. |
| `Ctrl+.` | Forward 1 frame | Move the playhead forward one frame. |
| `M` | Mute or unmute layer | Mute the focused layer. Muted layers are left out of playback and export. |
| `Shift+M` | Solo layer | Mute all other layers. Press again to unmute them. |
| `Ctrl+Shift+M` | Unmute all layers | Unmute every layer. |
| `Ctrl+Up` | Item louder | Raise the focused item's volume by 10 percent. |
| `Ctrl+Down` | Item quieter | Lower the focused item's volume by 10 percent. |
| `Ctrl+M` | Mute or unmute item | Mute or unmute only the focused item's sound. |
| `Ctrl+Shift+Up` | Layer louder | Raise the focused layer's volume by 10 percent. |
| `Ctrl+Shift+Down` | Layer quieter | Lower the focused layer's volume by 10 percent. |

## Picture

| Keys | Command | What it does |
|---|---|---|
| `P` | Position mode | Move, resize, crop, fill and fade the focused picture or text with the keyboard. Arrows move, plus and minus resize, number keys 1 to 9 snap to a place, 0 fills the frame, Control arrows crop, F fills, O changes opacity, D describes, Enter keeps, Escape undoes. |
| `Shift+W` | Describe the picture | Say what is visible at the playhead: every picture and text, where it is, its size and what it covers. |

## Smart tools

| Keys | Command | What it does |
|---|---|---|
| `Ctrl+Shift+Q` | Quick video | Make a finished video in three steps: choose files, a style (Reel, YouTube, Slideshow or Talking video) and a title. Silences, timing, transitions, music, captions and loudness are done for you, and everything can still be changed. |
| `Ctrl+Shift+W` | Transcribe speech | Write down the words spoken on the focused layer, on this computer with no upload. Works in English, Urdu, Hindi and many more languages. |
| `Ctrl+Shift+X` | Edit by transcript | Open the words of the focused layer as a document. Delete words or sentences to cut them out of the video, or correct the words for captions. |
| `Ctrl+Shift+G` | Make captions | Turn the transcript of the focused layer into captions on a Captions layer at the top. |
| `Alt+Shift+E` | Export transcript | Save the words of the focused layer as plain text (to edit anywhere, even in an AI chat) or as SRT or VTT captions. |
| `Alt+Shift+I` | Import edited transcript | Read back a transcript you edited outside ClipScribe. Deleted words become cuts and corrected words fix the transcript; you check the list first. |
| `Alt+Shift+R` | Find repeats and fillers | List repeated phrases, false starts, filler sounds and stray symbols on the focused layer, on this computer. Tick the ones to cut. |
| `Alt+Shift+A` | Clean up with AI | Send the transcript words to your AI service (OpenRouter, Ollama, OpenAI or another) to suggest repeats, fillers and mistakes to cut. You check every suggestion first. |
| `Ctrl+Shift+H` | Shorten pauses | Find long pauses in the focused clip and shorten or remove them. Says how many and how long before changing anything. |
| `Ctrl+Shift+B` | Find beats | Find the tempo and beats of the focused music. Then Alt B and Alt Shift B jump from beat to beat. |
| `Ctrl+Shift+U` | Cut to the beat | Make every picture and clip on the focused layer end on a beat of the music. |
| `Ctrl+Shift+D` | Lower music under speech | Make the focused music layer quieter wherever someone speaks on the other layers (auto-ducking), or remove that. |

## Information

| Keys | Command | What it does |
|---|---|---|
| `T` | Say playhead time | Say where the playhead is. |
| `Shift+T` | Say project status | Say project length, layer, listening mode, ripple and range. |
| `W` | What's at the playhead | Say what every layer has at the playhead, and where pictures are placed. |
| `D` | Describe item in detail | Say every setting of the focused item. |
| `F7` or `Ctrl+Shift+F` | Check project | List possible problems: missing files, black gaps, muted layers and transitions that cannot play. |
| `Shift+V` | Change how much is spoken | Switch between brief, normal and detailed descriptions. |
| `F1` or `Ctrl+H` | Keyboard help | List every command and its keys. Type to search. |

## Keys inside position mode

Press `P` on a picture, video or text to enter position mode. A short tone plays after each move: it is panned left or right by where the picture is across the frame, and higher the nearer it is to the top.

| Keys | What it does |
|---|---|
| Arrows | Move 5 percent of the frame. With Shift, 1 percent. |
| Plus and minus | Bigger and smaller. With Shift, in small steps. |
| 1 to 9 (the number row above the letters, or a number pad) | Snap to a place laid out like a phone keypad upside down, or a number pad: 7 top left, 8 top centre, 9 top right, 4 middle left, 5 centre, 6 middle right, 1 bottom left, 2 bottom centre, 3 bottom right. No number pad is needed. |
| 0 (number row or number pad) | Full frame (maximise). |
| F | Full frame only: switch between fit (whole picture, maybe with bars) and fill (covers the frame, edges cut off). |
| Control and an arrow | Crop 5 percent more from that edge. |
| Control Shift and an arrow | Give back 5 percent of that edge. |
| R | Remove all cropping. |
| O and Shift O | More and less opacity, in steps of 10 percent. |
| D or W | Describe where it is and everything else in the picture. |
| Enter | Keep the changes and leave position mode. |
| Escape | Undo every change made in position mode and leave. |
