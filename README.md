# NexusLogistics for Dyson Sphere Program

**Version:** 1.4.0

An unofficial update and feature-rich continuation of the original **PackageLogistic** mod by `qlvlp-com`. NexusLogistics provides a centralized, "magic" logistics network that dramatically simplifies resource management across your entire star cluster, allowing you to focus on designing and expanding your factory.

---

## A Note on Permissions and Original Work

This mod is an unofficial update and continuation of the `PackageLogistic` mod, originally created by `qlvlp-com`. I have made significant efforts to contact the original author but have been unable to reach them, as they appear to have been inactive in the community for over a year.

My goal is to keep this fantastic mod alive for the community, ensure it works with the latest versions of Dyson Sphere Program, and add new features for everyone to enjoy. All credit for the original concept and foundational code goes to `qlvlp-com`.

If the original author, `qlvlp-com`, wishes for this version to be taken down, I will comply with their request immediately.

## Credits & Acknowledgements

This mod is built upon the foundation of the PackageLogistic mod by qlvlp-com. A huge thank you to them for their amazing original work. This version is my effort to translate, update, and add new features for the community to enjoy.

- **Original Author:** qlvlp-com
- **Original Mod:** PackageLogistic

---

## How It Works

NexusLogistics creates a global, invisible storage network that acts as a central hub for all your resources. Here's the basic concept:

* **Producers Add to the Network:** Any building that generates items (like Miners, Smelters, Assemblers, and Logistics Stations set to "Supply") will automatically add their output to the central "Nexus" storage.
* **Consumers Take from the Network:** Any building that requires items (like Assemblers, Labs, Power Plants, Turrets, and Logistics Stations set to "Demand") will automatically draw what they need from the Nexus.
* **It's All Connected:** This happens instantaneously and across any distance, without the need for logistics vessels for the items managed by this mod. Your entire industrial empire is treated as one interconnected inventory.

## Key Features

* **Central Remote Storage:** All items supplied by logistics stations, storage boxes, and miners are pooled into a central "nexus" storage.
* **Automatic Supply Network:** Buildings that require items (Assemblers, Labs, Power Plants, Turrets, etc.) will automatically draw what they need from the nexus.
* **Personal Logistics:** Automatically replenishes filtered items in your main inventory and fills requests in your personal logistics slots.
* **Automatic Proliferation:** Items added to the network can be automatically sprayed with Proliferator MK.I, II, or III. This can be toggled to consume proliferator points or be free.
* **Item Limit Control:** A new UI allows you to set a storage limit for every item in the network, preventing overproduction of specific resources.
* **"Infinite" Cheats:** Optional toggles for infinite items, minerals, buildings, soil pile, ammo, and fleet units.
* **In-Game GUI:** Manage all features through two intuitive in-game windows.

## Installation

This mod requires the following to be installed first:

-   BepInEx
-   DSPModSave

1.  Ensure you have both **BepInEx** and **DSPModSave** installed for Dyson Sphere Program.
2.  Download the latest release of `NexusLogistics.dll` from the Releases page.
3.  Place the `NexusLogistics.dll` file into your `Dyson Sphere Program/BepInEx/plugins/` folder.
4.  Launch the game. A configuration file will be generated at `Dyson Sphere Program/BepInEx/config/com.Sidaril.dsp.NexusLogistics.cfg` on the first run.

## How to Use

Once in-game, you can use the following hotkeys to access the mod's features.

* **Toggle Main Window:** `LeftControl + L`
    * This window contains the main toggles for enabling the mod, auto-replenishment, auto-spraying, and cheat options.
* **Toggle Storage Window:** `LeftControl + K`
    * This window shows you the current contents of your central remote storage. You can browse items by category (Raw, Intermediates, Buildings, etc.).
    * **Set Item Limits:** In this window, you can click on the number in the "Limit" column for any item and type a new value to cap how much of that item the network will store.

### UI Panels Explained

#### Main Options Panel

* **Enable Mod:** The master switch for the entire mod.
* **Auto Replenish:** Automatically replenishes items in your inventory that have a filter set (middle-click a slot to set a filter).
* **Auto Spray:** Enables automatic proliferation of items in the network.
    * **Consume Proliferator:** If checked, this will consume proliferator points from the sprayers you have in the network. If unchecked, proliferation is free.
* **Recover from storage boxes/tanks:** When enabled, the mod will pull items from standard storage containers and liquid tanks into the network.
* **Auto-Replenish Thermal Power Plant Fuel:** Automatically supplies fuel to your thermal power plants. You can select a specific fuel type or leave it on "Auto" to let the mod intelligently choose based on your resource reserves.

#### Items Panel

Contains the "infinite" resource toggles. Useful for testing or sandbox-style gameplay.

* **Infinite Buildings**
* **Infinite Minerals**
* **Infinite Items**
* **Infinite Soil Pile**

#### Combat Panel

Contains "infinite" toggles for military supplies and a utility button.

* **Infinite Ammo**
* **Infinite Fleet**
* **Clear Battlefield Analysis Base:** Removes items from Battlefield Analysis Bases that you have marked to not be picked up, keeping their storage clean.

## Building from Source (Optional)

If you wish to build the mod yourself:

1.  Clone this repository.
2.  Open the `.sln` file in Visual Studio.
3.  You will need to add references to the game's `Assembly-CSharp.dll`, `UnityEngine.dll`, `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll`, and `UnityEngine.UIModule.dll`, as well as the `0Harmony.dll` from BepInEx and `DSPModSave.dll` from crecheng.
4.  Build the solution. The `NexusLogistics.dll` will be generated in the `bin/Debug` or `bin/Release` folder.

## Changelog

### Version 1.4.0
* **Feature:** Player inventory logistic slots will now bypass the global item limit when recycling items back into the network. This ensures you can always clear your logistic slots regardless of storage caps.
* **Feature:** Added a new optional feature: "Auto Clean Inventory". When enabled, items in your main inventory that have a corresponding logistic slot will be automatically moved to that slot, helping to keep your bags clean.
* **Tweak:** Changed the default storage limit for all items in the "Buildings" and "Combat" categories to 1,000. All other items will continue to default to 1,000,000.

### Version 1.3.0
* **Major Refactor:** The entire internal codebase has been overhauled for improved stability, readability, and future maintainability.
* **Code Quality:** Replaced hardcoded "magic numbers" with descriptive constants, making the code easier to understand and modify.
* **Modernized Concurrency:** The core processing loop has been updated to use the modern `Task` Parallel Library, improving the stability and efficiency of the mod's multithreading.
* **Bug Fixes:** Corrected several internal bugs that could cause build errors and issues with item removal from storage containers.

### Version 1.2.0
* **UI Overhaul:** The Remote Storage window has been updated to provide a more intuitive and visually informative display of item proliferation.
    * The "Inc Points" column has been replaced with a "Proliferation" column.
    * The proliferation level of an item is now displayed with color-coded text that matches the in-game proliferator colors (Orange for Mk1, Green for Mk2, Blue for Mk3).
    * For items that are not fully proliferated to the next tier, a percentage now indicates their progress, e.g., "Mk 1 (50%)".
* **Code Refactor:** The underlying logic for calculating and displaying proliferation status has been refactored to be more robust, accurate, and easier to maintain.

### Version 1.1.1
* **UI Polish:** The main options window has been made wider to prevent cramping, and a vertical scrollbar has been added to automatically handle content that overflows the window height. This ensures all options, including the fuel selection grid, are fully visible and accessible.
* **UI Fix:** The "Intermediates" category tab in the remote storage window has been shortened to "Intermeds" to correct a layout issue where the text would not fit.

### Version 1.1.0
* **Feature:** Added granular control over the Automatic Proliferation system. Players can now select a specific tier of proliferator to be used (MK.I, MK.II, or MK.III), or select "All" to maintain the original behaviour. This can be configured from a new toolbar in the main options window.

### Version 1.0.2
* **Bug Fix:** Fixed an issue where clicking on the mod's UI windows would also click on buildings or objects in the game world behind them.

### Version 1.0.1
* **Bug Fix:** Fixed a critical bug that could cause the game to throw an `ArgumentException` error when opening the remote storage window. This was most likely to occur when items were being added to or removed from the network rapidly. The remote storage UI is now stable.

### Version 1.0.0
* **Initial Release:**
    * Introduced the central "Nexus" logistics network, a global, invisible storage hub for all resources.
    * **Automatic Supply:** Buildings automatically draw required items from the Nexus, and producers automatically add their output.
    * **Personal Logistics:** Added automatic replenishment for filtered items in the player's inventory.
    * **Automatic Proliferation:** Items can be automatically sprayed with Proliferator, with an option to make it free or consume points.
    * **Item Limit Control:** Implemented a new UI (`LeftControl + K`) to set storage limits for every item in the network, preventing overproduction.
    * **In-Game GUI:** Added the main control window (`LeftControl + L`) to manage all mod features, including optional "infinite" resource cheats.