# Keyboard reference

This file is generated from the program's command table (`tools/AccessibleVideoEditor.AssetGenerator --docs docs`). Do not edit it by hand.

Keys without Control or Alt (letters, arrows, Space, Enter, Delete, brackets) work while the timeline or the layer list has focus. Every key can be changed in the keyboard settings file (Tools menu).

## File

| Keys | Command | What it does |
|---|---|---|
| `Ctrl+N` | New project from template | Start a new project from a template. |
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
| `Enter` | Item options | Open all options for the focused item: length, volume, fades, transition, position, sound clean-up and equaliser. |

## Add

| Keys | Command | What it does |
|---|---|---|
| `Ctrl+I` | Add files to end of layer | Choose video, picture or audio files and add them after the last item of the focused layer. Sound files go to an audio layer automatically. |
| `Ctrl+Shift+I` | Add files at playhead | Add files at the playhead time without moving anything; a free layer is used or created. |
| `Ctrl+Shift+E` | Add sound effect or music | Choose a built-in or your own sound and add it at the playhead. |
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
| `Home` | First item | Go to the first item on this layer. |
| `End` | Last item | Go to the last item on this layer. |
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
| `Ctrl+Home` | Go to start | Move the playhead to the start of the project. |
| `Ctrl+End` | Go to end | Move the playhead to the end of the project. |
| `F6` | Switch between timeline and layer list | Move between the items of the focused layer and the list of all layers. |

## Arrange

| Keys | Command | What it does |
|---|---|---|
| `Alt+Shift+Left` | Move item earlier in order | Swap the focused item with the one before it. |
| `Alt+Shift+Right` | Move item later in order | Swap the focused item with the one after it. |
| `Alt+Shift+Home` | Move item to the start | Make the focused item the first on its layer. |
| `Alt+Shift+End` | Move item to the end | Make the focused item the last on its layer. |
| `Alt+J` | Slide item earlier | Slide the focused item earlier by the slide step (1 second unless changed in preferences). |
| `Alt+L` | Slide item later | Slide the focused item later by the slide step. |
| `Alt+Shift+J` | Slide item earlier a little | Slide the focused item earlier by the fine step (a tenth of a second). |
| `Alt+Shift+L` | Slide item later a little | Slide the focused item later by the fine step. |
| `Ctrl+Shift+J` | Slide whole layer earlier | Move every item on the focused layer earlier by the slide step. |
| `Ctrl+Shift+L` | Slide whole layer later | Move every item on the focused layer later by the slide step. |
| `Alt+Shift+Up` | Move layer up the list | Give the focused layer a lower number. For video layers this puts its picture further back. |
| `Alt+Shift+Down` | Move layer down the list | Give the focused layer a higher number. For video layers this brings its picture to the front. |

## Cut and mark

| Keys | Command | What it does |
|---|---|---|
| `S` | Split at playhead | Cut the item at the playhead into two items. |
| `Shift+[` | Trim start to playhead | Remove the part of the item before the playhead. |
| `Shift+]` | Trim end to playhead | Remove the part of the item after the playhead. |
| `[` | Set range start | Mark the playhead as the start of a range. |
| `]` | Set range end | Mark the playhead as the end of a range. |
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

## Information

| Keys | Command | What it does |
|---|---|---|
| `T` | Say playhead time | Say where the playhead is. |
| `Shift+T` | Say project status | Say project length, layer, listening mode, ripple and range. |
| `W` | What's at the playhead | Say what every layer has at the playhead, and where pictures are placed. |
| `D` | Describe item in detail | Say every setting of the focused item. |
| `F7` | Check project | List possible problems: missing files, black gaps, muted layers and transitions that cannot play. |
| `Shift+V` | Change how much is spoken | Switch between brief, normal and detailed descriptions. |
| `F1` | Keyboard help | List every command and its keys. Type to search. |
