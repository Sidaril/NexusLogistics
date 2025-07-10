# NexusLogistics for Dyson Sphere Program

**Version: 1.0.0**

This mod is an unofficial update and translation of the original PackageLogistic mod, expanded with new features, quality-of-life improvements, and compatibility with the latest versions of Dyson Sphere Program. It provides a centralized, "magic" logistics network that simplifies resource management across your entire cluster.

## A Note on Permissions and Original Work

This mod is an unofficial update and continuation of the `PackageLogistic` mod, originally created by `qlvlp-com`. I have made significant efforts to contact the original author but have been unable to reach them, as they appear to have been inactive in the community for over a year.

My goal is to keep this fantastic mod alive for the community, ensure it works with the latest versions of Dyson Sphere Program, and add new features for everyone to enjoy. All credit for the original concept and foundational code goes to `qlvlp-com`.

If the original author, `qlvlp-com`, wishes for this version to be taken down, I will comply with their request immediately.

## Credits & Acknowledgements

This mod is built upon the foundation of the PackageLogistic mod by qlvlp-com. A huge thank you to them for their amazing original work. This version is my effort to translate, update, and add new features for the community to enjoy.

- **Original Author:** qlvlp-com
- **Original Mod:** PackageLogistic

## Features

NexusLogistics creates a global, invisible storage network that automatically supplies all your production buildings and personal inventory needs.

- **Central Remote Storage:** All items supplied by logistics stations, storage boxes, and miners are pooled into a central "nexus" storage.
- **Automatic Supply Network:** Buildings that require items (Assemblers, Labs, Power Plants, Turrets, etc.) will automatically draw what they need from the nexus.
- **Personal Logistics:** Automatically replenishes filtered items in your main inventory and fills requests in your personal logistics slots.
- **Automatic Proliferation:** Items added to the network can be automatically sprayed with Proliferator MK.I, II, or III. This can be toggled to consume proliferator points or be free.
- **Item Limit Control:** A new UI allows you to set a storage limit for every item in the network, preventing overproduction of specific resources.
- **"Infinite" Cheats:** Optional toggles for infinite items, minerals, buildings, soil pile, ammo, and fleet units.
- **In-Game GUI:** Manage all features through two intuitive in-game windows.

## Installation

This mod requires the following to be installed first:

- BepInEx
- DSPModSave

1.  Make sure you have both BepInEx and DSPModSave installed for Dyson Sphere Program.
2.  Download the latest release of `NexusLogistics.dll` from the Releases page.
3.  Place the `NexusLogistics.dll` file into your `Dyson Sphere Program/BepInEx/plugins/` folder.
4.  Launch the game. A configuration file will be generated at `Dyson Sphere Program/BepInEx/config/com.Sidaril.dsp.NexusLogistics.cfg` on the first run.

## How to Use

Once in-game, you can use the following hotkeys to access the mod's features.

-   **Toggle Main Window:** `LeftControl + L`
    -   This window contains the main toggles for enabling the mod, auto-replenishment, auto-spraying, and cheat options.
-   **Toggle Storage Window:** `LeftControl + K`
    -   This window shows you the current contents of your central remote storage. You can browse items by category (Raw, Intermediates, Buildings, etc.).
    -   **Set Item Limits:** In this window, you can click on the number in the "Limit" column for any item and type a new value to cap how much of that item the network will store.

## Main Panels Explained

-   **Main Options:** Core features like enabling the mod, auto-replenishing your inventory, and managing fuel for thermal power plants.
-   **Items:** Contains the "infinite" toggles for buildings, minerals, general items, and soil pile.
-   **Combat:** Contains the "infinite" toggles for ammo and fleet units, plus a button to clear non-essential items from Battlefield Analysis Bases.

## Building from Source (Optional)

If you wish to build the mod yourself:

1.  Clone this repository.
2.  Open the `.sln` file in Visual Studio.
3.  You will need to add references to the game's `Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll`, and `UnityEngine.UIModule.dll`, as well as the `0Harmony.dll` from BepInEx and `DSPModSave.dll` from crecheng.
4.  Build the solution. The `NexusLogistics.dll` will be generated in the `bin/Debug` or `bin/Release` folder.