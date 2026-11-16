# Third-party notices

Booty Board is MIT licensed (see LICENSE). It is built on these projects:

| Project | License | Used for |
|---|---|---|
| [FortniteReplayReader / Unreal.Core](https://github.com/Shiqan/FortniteReplayDecompressor) | MIT | Reading `.replay` files |
| [CUE4Parse](https://github.com/FabianFG/CUE4Parse) | Apache-2.0 | Reading item names from the installed game's own files |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) | MIT | Desktop UI |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | MIT | View models |

Downloaded at runtime, only after the user agrees on the setup screen, and never bundled:

- The public decryption key for the installed game version, from fortnite-api.com.
- The "mappings" (field layout) file for that game version, from uedb.dev.
- An Oodle decompression library built from the Unreal Engine Oodle source (community build at
  github.com/WorkingRobot/OodleUE, the one FModel uses). Oodle is owned by Epic Games Tools; it is not redistributed
  with Booty Board.

---

Portions of the materials used are trademarks and/or copyrighted works of Epic Games, Inc. All rights reserved by Epic.
This material is not official and is not endorsed by Epic.
