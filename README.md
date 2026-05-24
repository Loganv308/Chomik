# 🐹 Chomik

A Windows desktop pet hamster that lives on your screen, reacts to what you're doing, and can securely shred files you drag onto it.

Original character and animations by **blaing**. This version is a refactored C# / Avalonia rewrite.

---

## Features

- **Idle animations** — the hamster sits on your desktop and cycles through various idle behaviours: yawning, stretching, looking around, and more
- **Typing detection** — detects when you're typing and plays a typing animation in sync
- **Music detection** — puts on headphones when it detects music or video playing on your PC
- **AFK detection** — falls asleep if you haven't touched your keyboard or mouse for a while
- **File shredding** — drag and drop files onto the hamster to delete them. Three modes available in settings:
  - **Recycle Bin** — moves files to the OS trash (default)
  - **Permanent delete** — deletes without recovery
  - **Secure shred** — encrypts in-place with AES-256, overwrites 7× with random data, renames 5×, then deletes. Makes software-based file recovery impossible
- **Transparent, frameless window** — sits directly on the desktop with no border or taskbar entry, click-through on transparent pixels

---

## Requirements

- Windows 10 or 11
- [.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) (or use the self-contained publish)

---

## Setup

1. Clone or download the repository
2. Use the `files/` folder containing the hamster sprite PNGs (`hamster_0000.png` through `hamster_1639.png`) and `anims.txt`
3. Place the `files/` folder in the project root (next to `chomik.csproj`)
4. Build and run:

```
dotnet run
```

Or open `chomik.sln` in Visual Studio and press **F5**.

The `files/` folder will be automatically copied to the build output directory on each build.

---

## Settings

Right-click the hamster to open the context menu, then click **Settings**.

| Setting | Description |
|---|---|
| Idle delay | How many seconds after stopping activity before a random idle animation plays |
| Music listening | Enable/disable the music detection and headphones animation |
| Shred files | Enable secure multi-pass file shredding when files are dropped |
| Delete mode | When shredding is off: send to Recycle Bin or permanently delete |
| App whitelist | List of process names to monitor for music/video playback |
| AFK timeout | Minutes of inactivity before the hamster falls asleep |

---

## Project Structure

```
chomik/
├── App.cs                      # Avalonia application entry point
├── Program.cs                  # Main entry point, platform configuration
├── files/                      # Animation sprites and anims.txt (not in repo)
├── Helpers/
│   └── PixelHitTestHelper.cs   # Per-pixel alpha hit testing for click-through
├── Models/
│   ├── AnimationFrame.cs       # Single animation frame (bitmap + duration)
│   ├── AppSettings.cs          # User settings model
│   └── HamsterState.cs         # State machine enum
├── Services/
│   ├── AnimationService.cs     # Loads and parses anims.txt, manages frames
│   ├── FileShredderService.cs  # Secure file deletion logic
│   ├── KeyboardMonitorService.cs # Global keyboard hook for typing detection
│   ├── MusicMonitorService.cs  # Detects music/video playback by process name
│   └── SettingsService.cs      # Loads/saves settings.json
└── Views/
    ├── MainWindow.cs           # Main hamster window, animation state machine
    ├── BubbleWindow.cs         # Speech bubble overlay
    ├── MessageBox.cs           # About dialog with animation
    ├── SettingsDialog.cs       # Settings window
    └── WriteDialog.cs          # Text input for speech bubble
```

---

## File Shredding — Security Notes

## ⚠️ Disclaimer

Chomik permanently destroys files. Once a file has been shredded or permanently deleted, it is gone. The authors and contributors of this project accept no responsibility for:

- Files accidentally dragged onto the hamster and deleted
- Data loss of any kind resulting from use of this software
- Any consequences arising from the secure shredding of files you did not intend to shred

**Use at your own risk.** There is no undo. If in doubt, use the Recycle Bin mode in settings rather than permanent delete or secure shred.

The secure shred mode makes files unrecoverable by any software-based tool:

1. Encrypts file contents in-place with AES-256 using a randomly generated key that is never written to disk
2. Overwrites the encrypted content 7 times with cryptographically random data
3. Truncates the file to zero length
4. Renames the file 5 times with random names to scrub the original filename from directory entries and filesystem journals
5. Deletes the file

**Limitation:** On SSDs with wear-levelling, some original data may persist in reallocated NAND blocks that the OS cannot address. This is a hardware-level constraint that no purely software-based shredder can overcome.

---

## License

Code released under MIT License.