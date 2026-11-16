# Third-party notices

Drop Report is MIT licensed (see LICENSE). It is built on these projects:

| Project | License | Used for |
|---|---|---|
| [FortniteReplayReader / Unreal.Core](https://github.com/Shiqan/FortniteReplayDecompressor) | MIT | Reading `.replay` files |
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) | Apache-2.0 | Reading item names from the installed game's own files |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT | Desktop UI |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | View models |
| [Inter](https://github.com/rsms/inter) font | SIL OFL 1.1 | Body text (bundled; license in `src/DropReport.App/Assets/Fonts/Inter-OFL.txt`) |
| [Barlow Condensed](https://github.com/jpt/barlow) font | SIL OFL 1.1 | Headlines and numbers (bundled; `BarlowCondensed-OFL.txt`) |

Downloaded at runtime, only after the user agrees on the setup screen, and never bundled:

- The public decryption key for the installed game version, from fortnite-api.com.
- The "mappings" (field layout) file for that game version, from uedb.dev.
- The current season's map image and location names, from fortnite-api.com (the map art belongs to Epic Games; it is
  fetched to your PC, never bundled or redistributed).
- An Oodle decompression library built from the Unreal Engine Oodle source (community build at
  github.com/WorkingRobot/OodleUE, the one FModel uses). Oodle is owned by Epic Games Tools; it is not redistributed
  with Drop Report.

---

Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
This material is not official and is not endorsed by Epic.
