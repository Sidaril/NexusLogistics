using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using crecheng.DSPModSave;

namespace NexusLogistics
{

    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("crecheng.DSPModSave")]
    public class NexusLogistics : BaseUnityPlugin, IModCanSave
    {
        public const string GUID = "com.Sidaril.dsp.NexusLogistics";
        public const string NAME = "NexusLogistics";
        public const string VERSION = "1.1.0";

        private const int SAVE_VERSION = 2; // Incremented save version for the new 'limit' field

        // ADD: Enum for Proliferator Selection
        private enum ProliferatorSelection
        {
            All,
            Mk1,
            Mk2,
            Mk3
        }

        // ADD: New class for items in our remote storage
        private class RemoteStorageItem
        {
            public int count;
            public int inc;
            public int limit = 1000000; // ADD: New field for storage limit, default to 1M

            public void Export(BinaryWriter w)
            {
                w.Write(count);
                w.Write(inc);
                w.Write(limit); // ADD: Save the limit
            }

            // MODIFIED: Import now handles different save versions
            public static RemoteStorageItem Import(BinaryReader r, int saveVersion)
            {
                var item = new RemoteStorageItem
                {
                    count = r.ReadInt32(),
                    inc = r.ReadInt32(),
                    limit = 1000000 // Default for old saves
                };
                if (saveVersion >= 2)
                {
                    item.limit = r.ReadInt32(); // Overwrite if present in save file
                }
                return item;
            }
        }
        // ADD: The new central remote storage and a lock for thread safety
        private readonly Dictionary<int, RemoteStorageItem> remoteStorage = new Dictionary<int, RemoteStorageItem>();
        private readonly object remoteStorageLock = new object();

        private ConfigEntry<Boolean> autoSpray;
        private ConfigEntry<Boolean> costProliferator;
        private ConfigEntry<ProliferatorSelection> proliferatorSelection; // ADD: New config entry
        private ConfigEntry<Boolean> infVeins;
        private ConfigEntry<Boolean> infItems;
        private ConfigEntry<Boolean> infSand;
        private ConfigEntry<Boolean> infBuildings;
        private ConfigEntry<Boolean> useStorege;
        private ConfigEntry<KeyboardShortcut> hotKey;
        private ConfigEntry<KeyboardShortcut> storageHotKey; // Hotkey for the new storage window
        private ConfigEntry<Boolean> enableMod;
        private ConfigEntry<Boolean> autoReplenishPackage;
        private ConfigEntry<Boolean> autoReplenishTPPFuel;

        private readonly List<(int, int)> proliferators = new List<(int, int)>();
        private readonly Dictionary<int, int> incPool = new Dictionary<int, int>()
        {
            {1141, 0 },
            {1142, 0 },
            {1143, 0 }
        };
        private Dictionary<string, bool> taskState = new Dictionary<string, bool>();

        private int stackSize = 0;
        private const float hydrogenThreshold = 0.6f;
        private bool showGUI = false;
        private bool showStorageGUI = false; // Controls visibility of the new storage GUI
        private Rect windowRect = new Rect(700, 250, 500, 400);
        private Rect storageWindowRect = new Rect(100, 250, 500, 500); // MODIFIED: New window position and size
        private Vector2 storageScrollPosition; // For the scroll view
        private readonly Texture2D windowTexture = new Texture2D(10, 10);
        private int selectedPanel = 0;
        private readonly Dictionary<int, string> fuelOptions = new Dictionary<int, string>();
        private ConfigEntry<int> fuelId;
        private int selectedFuelIndex;

        // ADD: For categorized storage GUI
        private enum StorageCategory
        {
            RawResources,
            IntermediateProducts,
            BuildingsAndVehicles,
            AmmunitionAndCombat,
            ScienceMatrices
        }
        private StorageCategory selectedStorageCategory = StorageCategory.RawResources;
        // END ADD

        // ADD: For editable text fields in the GUI
        private readonly Dictionary<int, string> limitInputStrings = new Dictionary<int, string>();
        // END ADD

        private ConfigEntry<Boolean> infFleet;
        private ConfigEntry<Boolean> infAmmo;
        Dictionary<EAmmoType, List<int>> ammos = new Dictionary<EAmmoType, List<int>>();

        private List<KeyValuePair<int, RemoteStorageItem>> storageItemsForGUI = new List<KeyValuePair<int, RemoteStorageItem>>();

        #region IModCanSave Implementation

        public void Export(BinaryWriter w)
        {
            w.Write(SAVE_VERSION);
            lock (remoteStorageLock)
            {
                w.Write(remoteStorage.Count);
                foreach (var item in remoteStorage)
                {
                    w.Write(item.Key); // Item ID
                    item.Value.Export(w); // The RemoteStorageItem data (now includes limit)
                }
            }
        }

        public void Import(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version > SAVE_VERSION)
            {
                // Don't try to load a newer save version
                return;
            }

            lock (remoteStorageLock)
            {
                remoteStorage.Clear();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int itemId = r.ReadInt32();
                    // MODIFIED: Pass save version to importer to handle old saves correctly
                    remoteStorage[itemId] = RemoteStorageItem.Import(r, version);
                }
            }
        }

        public void IntoOtherSave()
        {
            // This method is called when you load a different save or start a new game.
            // It's crucial to clear your data here to prevent data from one save
            // leaking into another.
            lock (remoteStorageLock)
            {
                remoteStorage.Clear();
            }
        }

        #endregion

        void Start()
        {
            hotKey = Config.Bind("Window Shortcut Key", "Key", new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl));
            storageHotKey = Config.Bind("Window Shortcut Key", "Storage_Key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl)); // New hotkey
            enableMod = Config.Bind<Boolean>("Configuration", "EnableMod", true, "Enable MOD");
            autoReplenishPackage = Config.Bind<Boolean>("Configuration", "autoReplenishPackage", true, "Automatically replenish items with filtering enabled in the backpack (middle-click on the slot to enable filtering)");
            autoSpray = Config.Bind<Boolean>("Configuration", "AutoSpray", true, "Automatic Spraying. Automatically sprays other items within the logistics backpack and interstellar logistics stations");
            costProliferator = Config.Bind<Boolean>("Configuration", "CostProliferator", true, "Consume Proliferator. Consumes proliferators from the backpack or interstellar logistics station during automatic spraying");
            proliferatorSelection = Config.Bind<ProliferatorSelection>("Configuration", "ProliferatorSelection", ProliferatorSelection.All, "Which Proliferator tier to use for automatic spraying."); // ADD: Bind new config
            infItems = Config.Bind<Boolean>("Configuration", "InfItems", false, "Infinite Items. All items in the logistics backpack and interstellar logistics stations have infinite quantity (cannot obtain achievements)");
            infVeins = Config.Bind<Boolean>("Configuration", "InfVeins", false, "Infinite Minerals. All minerals in the logistics backpack and interstellar logistics stations have infinite quantity");
            infBuildings = Config.Bind<Boolean>("Configuration", "InfBuildings", false, "Infinite Buildings. All buildings in the logistics backpack and interstellar logistics stations have infinite quantity");
            infSand = Config.Bind<Boolean>("Configuration", "InfSand", false, "Infinite Soil Pile. Soil pile quantity is infinite (fixed at 1G)");
            useStorege = Config.Bind<Boolean>("Configuration", "useStorege", true, "Recover items from storage boxes and liquid tanks");
            autoReplenishTPPFuel = Config.Bind<Boolean>("Configuration", "autoReplenishTPPFuel", true, "Automatically replenish fuel for thermal power plants");
            fuelId = Config.Bind<int>("Configuration", "fuelId", 0, "Thermal Power Plant Fuel ID\n" +
                "0: Auto-select, when Refined Oil and Hydrogen reserves exceed 60%, use whichever is more abundant, otherwise use Coal, to prevent Crude Oil cracking reaction blockage\n" +
                "1006: Coal, 1109: Graphite, 1007: Crude Oil, 1114: Refined Oil, 1120: Hydrogen, 1801: Hydrogen Fuel Rod, 1011: Fire Ice\n" +
                "5206: Energy Shard, 1128: Combustion Unit, 1030: Wood, 1031: Plant Fuel");
            fuelOptions.Add(0, "Auto");
            fuelOptions.Add(1006, "Coal");
            fuelOptions.Add(1109, "Graphite");
            fuelOptions.Add(1007, "Crude Oil");
            fuelOptions.Add(1114, "Refined Oil");
            fuelOptions.Add(1120, "Hydrogen");
            fuelOptions.Add(1801, "Hydrogen Fuel Rod");
            fuelOptions.Add(1011, "Fire Ice");
            fuelOptions.Add(5206, "Energy Shard");
            fuelOptions.Add(1128, "Combustion Unit");
            fuelOptions.Add(1030, "Wood");
            fuelOptions.Add(1031, "Plant Fuel");
            selectedFuelIndex = fuelOptions.Keys.ToList().FindIndex(id => id == fuelId.Value);

            proliferators.Add((1143, 4));
            //Proliferator MK.III
            proliferators.Add((1142, 2));
            //Proliferator MK.II
            proliferators.Add((1141, 1));
            //Proliferator MK.I

            windowTexture.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 1), 100).ToArray());
            windowTexture.Apply();
            infAmmo = Config.Bind<Boolean>("Configuration", "InfAmmo", false, "Infinite Ammo. Ammo in the logistics backpack and interstellar logistics stations have infinite quantity");
            infFleet = Config.Bind<Boolean>("Configuration", "infFleet", false, "Infinite Fleet. Drones and warships in the logistics backpack and interstellar logistics stations have infinite quantity");
            ammos.Add(EAmmoType.Bullet, new List<int> { 1603, 1602, 1601 });
            ammos.Add(EAmmoType.Missile, new List<int> { 1611, 1610, 1609 });
            ammos.Add(EAmmoType.Cannon, new List<int> { 1606, 1605, 1604 });
            ammos.Add(EAmmoType.Plasma, new List<int> { 1608, 1607 });
            ammos.Add(EAmmoType.EMCapsule, new List<int> { 1613, 1612 });
            new Thread(() =>
            {
                Logger.LogInfo("NexusLogistics start!");
                while (true)
                {
                    DateTime startTime = DateTime.Now;

                    try
                    {
                        if (GameMain.instance == null || GameMain.instance.isMenuDemo || GameMain.isPaused || !GameMain.isRunning || GameMain.data == null)
                        {

                            Logger.LogInfo("Game is not running!");
                            continue;
                        }

                        if (enableMod.Value)

                        {
                            if (infSand.Value && GameMain.mainPlayer.sandCount != 1000000000)
                            {

                                Traverse.Create(GameMain.mainPlayer).Property("sandCount").SetValue(1000000000);
                            }

                            CheckTech();

                            taskState["ProcessSpraying"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessSpraying, taskState);

                            // Add new task for personal logistics
                            taskState["ProcessDeliveryPackage"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessDeliveryPackage, taskState);

                            taskState["ProcessTransport"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessTransport, taskState);
                            if (useStorege.Value)
                            {
                                taskState["ProcessStorage"] = false;
                                ThreadPool.QueueUserWorkItem(ProcessStorage, taskState);
                            }
                            taskState["ProcessAssembler"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessAssembler, taskState);
                            taskState["ProcessMiner"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessMiner, taskState);
                            taskState["ProcessPowerGenerator"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessPowerGenerator, taskState);
                            taskState["ProcessPowerExchanger"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessPowerExchanger, taskState);
                            taskState["ProcessSilo"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessSilo, taskState);
                            taskState["ProcessEjector"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessEjector, taskState);
                            taskState["ProcessLab"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessLab, taskState);
                            taskState["ProcessTurret"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessTurret, taskState);
                            taskState["ProcessBattleBase"] = false;
                            ThreadPool.QueueUserWorkItem(ProcessBattleBase, taskState);
                            if (autoReplenishPackage.Value)
                            {
                                taskState["ProcessPackage"] = false;
                                ThreadPool.QueueUserWorkItem(ProcessPackage, taskState);
                            }

                            var keys = new List<string>(taskState.Keys);
                            string key = "";
                            while (true)
                            {
                                bool finish = true;
                                DateTime now = DateTime.Now;
                                for (int i = 0; i < keys.Count; i++)
                                {
                                    if (taskState[keys[i]] == false)

                                    {
                                        key = keys[i];
                                        finish = false;
                                        break;
                                    }
                                }
                                if ((now - startTime).TotalMilliseconds >= 1000)

                                {
                                    Logger.LogError(string.Format("{0} cost time >= 1000 ms", key));
                                    break;
                                }
                                if (finish)
                                    break;
                                else
                                    Thread.Sleep(5);
                            }
                        }
                    }
                    catch (Exception ex)
                    {

                        Logger.LogError("NexusLogistics exception!");
                        Logger.LogError(ex.ToString());
                    }
                    finally
                    {
                        DateTime endTime = DateTime.Now;
                        double cost = (endTime - startTime).TotalMilliseconds;

                        Logger.LogDebug(string.Format("loop cost:{0}", cost));
                        if (cost < 50)
                            Thread.Sleep((int)(50 - cost));
                    }
                }
            }).Start();
        }

        void Update()
        {
            if (hotKey.Value.IsDown())
            {
                showGUI = !showGUI;
            }
            if (storageHotKey.Value.IsDown())
            {
                showStorageGUI = !showStorageGUI;
            }

            if (showStorageGUI)
            {
                lock (remoteStorageLock)
                {
                    storageItemsForGUI = remoteStorage
                        .Where(pair => {
                            if (pair.Value.count <= 0) return false;
                            ItemProto itemProto = LDB.items.Select(pair.Key);
                            return itemProto != null && GetItemCategory(itemProto) == selectedStorageCategory;
                        })
                        .OrderBy(item => LDB.items.Select(item.Key)?.name ?? string.Empty)
                        .ToList();
                }

                // Pre-populate the input strings dictionary to avoid modifying it during the render loop.
                foreach (var pair in storageItemsForGUI)
                {
                    if (!limitInputStrings.ContainsKey(pair.Key))
                    {
                        limitInputStrings[pair.Key] = pair.Value.limit.ToString();
                    }
                }
            }
        }

        void OnGUI()
        {
            if (showGUI)
            {
                GUI.DrawTexture(windowRect, windowTexture);
                windowRect = GUI.Window(0, windowRect, WindowFunction, string.Format("{0} {1}", NAME, VERSION));
            }
            if (showStorageGUI)
            {
                GUI.DrawTexture(storageWindowRect, windowTexture); // Reuse existing texture
                storageWindowRect = GUI.Window(1, storageWindowRect, StorageWindowFunction, "Remote Storage Contents");
            }

            // Prevent click-through
            if ((showGUI && windowRect.Contains(Event.current.mousePosition)) || (showStorageGUI && storageWindowRect.Contains(Event.current.mousePosition)))
            {
                Input.ResetInputAxes();
            }
        }

        void WindowFunction(int windowID)
        {
            string[] panels = { "Main Options", "Items", "Combat" };
            selectedPanel = GUILayout.Toolbar(selectedPanel, panels);
            switch (selectedPanel)
            {
                case 0:
                    MainPanel();
                    break;
                case 1:
                    ItemPanel();
                    break;
                case 2:
                    FightPanel();
                    break;
            }
            GUI.DragWindow();
        }

        private StorageCategory GetItemCategory(ItemProto itemProto)
        {
            if (itemProto == null) return StorageCategory.IntermediateProducts;

            // Science Matrices
            if (itemProto.ID >= 6001 && itemProto.ID <= 6006)
            {
                return StorageCategory.ScienceMatrices;
            }

            // Ammunition & Combat
            if (itemProto.isAmmo || itemProto.isFighter)
            {
                return StorageCategory.AmmunitionAndCombat;
            }

            // Buildings & Vehicles
            if (itemProto.CanBuild)
            {
                return StorageCategory.BuildingsAndVehicles;
            }

            // Raw Resources
            if (IsVein(itemProto.ID))
            {
                return StorageCategory.RawResources;
            }

            // Intermediate Products
            return StorageCategory.IntermediateProducts;
        }

        // MODIFIED: Function to display and manage the storage window with limits
        void StorageWindowFunction(int windowID)
        {
            string[] categories = { "Raw", "Intermediates", "Buildings", "Combat", "Science" };
            selectedStorageCategory = (StorageCategory)GUILayout.Toolbar((int)selectedStorageCategory, categories);

            GUILayout.BeginVertical();
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Name", GUILayout.Width(150));
            GUILayout.Label("Count", GUILayout.Width(100));
            GUILayout.Label("Inc Points", GUILayout.Width(80));
            GUILayout.Label("Limit", GUILayout.Width(100)); // New "Limit" column header
            GUILayout.EndHorizontal();

            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition);

            try
            {
                foreach (var pair in storageItemsForGUI)
                {
                    int itemId = pair.Key;
                    RemoteStorageItem item = pair.Value;

                    ItemProto itemProto = LDB.items.Select(itemId);
                    string itemName = itemProto.name;

                    // Initialize the input string for the text field if it doesn't exist
                    if (!limitInputStrings.ContainsKey(itemId))
                    {
                        limitInputStrings[itemId] = item.limit.ToString();
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(itemName, GUILayout.Width(150));
                    GUILayout.Label(item.count.ToString("N0"), GUILayout.Width(100));
                    GUILayout.Label(item.inc.ToString("N0"), GUILayout.Width(80));

                    // Create a text field for the limit
                    string currentInput = limitInputStrings[itemId];
                    string newInput = GUILayout.TextField(currentInput, GUILayout.Width(100));

                    // If the text has changed, update the dictionary and the item's limit
                    if (newInput != currentInput)
                    {
                        limitInputStrings[itemId] = newInput;
                        // Try to parse the new limit and update it if valid
                        if (int.TryParse(newInput, out int newLimit) && newLimit >= 0)
                        {
                            // This lock is still necessary for thread-safe modification
                            lock (remoteStorageLock)
                            {
                                if (remoteStorage.ContainsKey(itemId))
                                {
                                    remoteStorage[itemId].limit = newLimit;
                                }
                            }
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            catch (Exception e)
            {
                GUILayout.Label("Error displaying storage: " + e.Message);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }


        void MainPanel()
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.Label("Enable or stop MOD operation");
            enableMod.Value = GUILayout.Toggle(enableMod.Value, "Enable MOD");

            GUILayout.Space(10);
            GUILayout.Label("Automatically replenish items with filtering enabled in the backpack");
            autoReplenishPackage.Value = GUILayout.Toggle(autoReplenishPackage.Value, "Auto Replenish");

            GUILayout.Space(15);
            GUILayout.Label("Automatically spray other items within the logistics backpack and interstellar logistics stations");
            GUILayout.BeginHorizontal();
            autoSpray.Value = GUILayout.Toggle(autoSpray.Value, "Auto Spray");
            costProliferator.Value = GUILayout.Toggle(costProliferator.Value, "Consume Proliferator");
            GUILayout.EndHorizontal();
            
            // ADD: Proliferator tier selection GUI
            if (autoSpray.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Proliferator Tier:", GUILayout.Width(120));
                proliferatorSelection.Value = (ProliferatorSelection)GUILayout.Toolbar((int)proliferatorSelection.Value, new string[] { "All", "MK.I", "MK.II", "MK.III" });
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(15);
            useStorege.Value = GUILayout.Toggle(useStorege.Value, "Recover items from storage boxes and liquid tanks");

            GUILayout.Space(15);
            autoReplenishTPPFuel.Value = GUILayout.Toggle(autoReplenishTPPFuel.Value, "Automatically replenish fuel for thermal power plants");
            if (autoReplenishTPPFuel.Value)
            {
                selectedFuelIndex = GUILayout.SelectionGrid(selectedFuelIndex, fuelOptions.Values.ToArray(), 4, GUI.skin.toggle);
                fuelId.Value = fuelOptions.Keys.ToArray()[selectedFuelIndex];
            }

            GUILayout.Space(5);
            GUILayout.EndVertical();
        }

        void ItemPanel()
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.Label("All buildings in the logistics backpack and interstellar logistics stations have infinite quantity");
            infBuildings.Value = GUILayout.Toggle(infBuildings.Value, "Infinite Buildings");

            GUILayout.Space(15);
            GUILayout.Label("All minerals in the logistics backpack and interstellar logistics stations have infinite quantity");
            infVeins.Value = GUILayout.Toggle(infVeins.Value, "Infinite Minerals");

            GUILayout.Space(15);
            GUILayout.Label("All items in the logistics backpack and interstellar logistics stations have infinite quantity (cannot obtain achievements)");
            infItems.Value = GUILayout.Toggle(infItems.Value, "Infinite Items");

            GUILayout.Space(15);
            GUILayout.Label("Soil pile quantity is infinite (fixed at 1G)");
            infSand.Value = GUILayout.Toggle(infSand.Value, "Infinite Soil Pile");

            GUILayout.Space(5);
            GUILayout.EndVertical();
        }

        void FightPanel()
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.Label("All ammo in the logistics backpack and interstellar logistics stations have infinite quantity");
            infAmmo.Value = GUILayout.Toggle(infAmmo.Value, "Infinite Ammo");

            GUILayout.Space(15);
            GUILayout.Label("Drones and warships in the logistics backpack and interstellar logistics stations have infinite quantity");
            infFleet.Value = GUILayout.Toggle(infFleet.Value, "Infinite Fleet");

            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("Clear Battlefield Analysis Base", "Items set not to drop will be discarded"), GUILayout.Width(150)))
            {
                ClearBattleBase();
            }
            GUILayout.Space(5);
            GUILayout.EndVertical();
        }

        void CheckTech()
        {
            Logger.LogDebug("CheckTech");
            var deliveryPackage = GameMain.mainPlayer.deliveryPackage;
            if (GameMain.history.TechUnlocked(2307) && deliveryPackage.colCount < 5)
            {
                deliveryPackage.colCount = 10;
                deliveryPackage.NotifySizeChange();
            }
            else if (GameMain.history.TechUnlocked(2304) && deliveryPackage.colCount < 4)
            {
                deliveryPackage.colCount = 8;
                deliveryPackage.NotifySizeChange();
            }
            else if (!GameMain.history.TechUnlocked(1608) && deliveryPackage.colCount < 3 || !deliveryPackage.unlocked)
            {
                deliveryPackage.colCount = 6;
                if (!deliveryPackage.unlocked)
                    deliveryPackage.unlocked = true;
                deliveryPackage.NotifySizeChange();
            }

            if (GameMain.history.TechUnlocked(2307))
            {
                stackSize = 5000;
                if (GameMain.mainPlayer.package.size < 160)
                    GameMain.mainPlayer.package.SetSize(160);
            }
            else if (GameMain.history.TechUnlocked(2306))
            {
                stackSize = 4000;
                if (GameMain.mainPlayer.package.size < 150)
                    GameMain.mainPlayer.package.SetSize(150);
            }
            else if (GameMain.history.TechUnlocked(2305))
            {
                stackSize = 3000;
                if (GameMain.mainPlayer.package.size < 140)
                    GameMain.mainPlayer.package.SetSize(140);
            }
            else if (GameMain.history.TechUnlocked(2304))
            {
                stackSize = 2000;
                if (GameMain.mainPlayer.package.size < 130)
                    GameMain.mainPlayer.package.SetSize(130);
            }
            else if (GameMain.history.TechUnlocked(2303))
            {
                stackSize = 1000;
                if (GameMain.mainPlayer.package.size < 120)
                    GameMain.mainPlayer.package.SetSize(120);
            }
            else if (GameMain.history.TechUnlocked(2302))
            {
                stackSize = 500;
                if (GameMain.mainPlayer.package.size < 110)
                    GameMain.mainPlayer.package.SetSize(110);
            }
            else if (GameMain.history.TechUnlocked(2301))
            {
                stackSize = 400;
                if (GameMain.mainPlayer.package.size < 100)
                    GameMain.mainPlayer.package.SetSize(100);
            }
            else
            {
                stackSize = 300;
                if (GameMain.mainPlayer.package.size < 90)
                    GameMain.mainPlayer.package.SetSize(90);
            }

            if (GameMain.history.TechUnlocked(3510))
            {
                GameMain.history.remoteStationExtraStorage = 40000;
            }
            else if (GameMain.history.TechUnlocked(3509))
            {
                GameMain.history.remoteStationExtraStorage = 15000;
            }
        }

        // MODIFIED: This method now respects the proliferator selection
        void ProcessSpraying(object state)
        {
            try
            {
                if (!autoSpray.Value) return;

                lock (remoteStorageLock)
                {
                    // Determine which proliferators to use based on the selection
                    var activeProliferators = new List<(int, int)>();
                    switch (proliferatorSelection.Value)
                    {
                        case ProliferatorSelection.Mk1:
                            activeProliferators.Add((1141, 1)); // Proliferator MK.I
                            break;
                        case ProliferatorSelection.Mk2:
                            activeProliferators.Add((1142, 2)); // Proliferator MK.II
                            break;
                        case ProliferatorSelection.Mk3:
                            activeProliferators.Add((1143, 4)); // Proliferator MK.III
                            break;
                        case ProliferatorSelection.All:
                        default:
                            activeProliferators.AddRange(proliferators); // Use all of them
                            break;
                    }


                    if (costProliferator.Value)
                    {
                        // Only process the selected proliferators
                        foreach (var proliferator in activeProliferators)
                        {
                            int proliferatorId = proliferator.Item1;
                            int factor = 0;
                            if (proliferatorId == 1143) factor = 75;
                            else if (proliferatorId == 1142) factor = 30;
                            else if (proliferatorId == 1141) factor = 15;

                            if (factor > 0 && remoteStorage.ContainsKey(proliferatorId))
                            {
                                int p_count = remoteStorage[proliferatorId].count;
                                if (p_count > 0)
                                {
                                    incPool[proliferatorId] += p_count * factor;
                                    remoteStorage[proliferatorId].count = 0;
                                }
                            }
                        }
                    }

                    foreach (var pair in remoteStorage)
                    {
                        int itemId = pair.Key;
                        var storageItem = pair.Value;
                        if (itemId <= 0 || storageItem.count <= 0) continue;

                        // Skip proliferator items themselves
                        if (itemId == 1141 || itemId == 1142 || itemId == 1143)
                        {
                            storageItem.inc = storageItem.count * 4;
                            continue;
                        }

                        ItemProto itemProto = LDB.items.Select(itemId);
                        if (itemProto.CanBuild || itemProto.isFighter) continue;

                        if (!costProliferator.Value)
                        {
                            // If not costing proliferator, use the highest available spray level from the selection
                            int maxSprayLevel = activeProliferators.Count > 0 ? activeProliferators.Max(p => p.Item2) : 4;
                            if (storageItem.inc < storageItem.count * maxSprayLevel) storageItem.inc = storageItem.count * maxSprayLevel;
                            continue;
                        }

                        // Iterate through the selected proliferators to apply points
                        foreach (var proliferator in activeProliferators)
                        {
                            int sprayLevel = proliferator.Item2;
                            int proliferatorId = proliferator.Item1;
                            int expectedInc = storageItem.count * sprayLevel - storageItem.inc;
                            if (expectedInc <= 0) break; // Already has enough inc for this level

                            int pointsToTake = expectedInc;
                            int availablePoints = incPool.ContainsKey(proliferatorId) ? incPool[proliferatorId] : 0;
                            int actualPoints = Math.Min(pointsToTake, availablePoints);

                            if (actualPoints > 0)
                            {
                                storageItem.inc += actualPoints;
                                incPool[proliferatorId] -= actualPoints;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessSpraying"] = true;
            }
        }

        // New function to handle personal logistics requests
        void ProcessDeliveryPackage(object state)
        {
            Logger.LogDebug("ProcessDeliveryPackage");
            try
            {
                var deliveryPackage = GameMain.mainPlayer.deliveryPackage;
                if (!deliveryPackage.unlocked) return;

                for (int i = 0; i < deliveryPackage.gridLength; i++)
                {
                    var grid = deliveryPackage.grids[i];
                    if (grid.itemId <= 0) continue;

                    if (grid.requireCount > grid.count)
                    {
                        int needCount = grid.requireCount - grid.count;
                        int[] result = TakeItem(grid.itemId, needCount);
                        if (result[0] > 0)
                        {
                            deliveryPackage.grids[i].count += result[0];
                            deliveryPackage.grids[i].inc += result[1];
                        }
                    }
                    else if (grid.recycleCount < grid.count)
                    {
                        int supplyCount = grid.count - grid.recycleCount;
                        int[] result = AddItem(grid.itemId, supplyCount, 0, false);
                        if (result[0] > 0)
                        {
                            deliveryPackage.grids[i].count -= result[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessDeliveryPackage"] = true;
            }
        }

        bool IsVein(int itemId)
        {
            int[] items = new int[4] { 1000, 1116, 1120, 1121 };
            if (items.Contains(itemId) || LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        // Updated function to handle both PLS and ILS
        void ProcessTransport(object state)
        {
            Logger.LogDebug("ProcessTransport");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    foreach (StationComponent sc in pf.transport.stationPool)
                    {
                        if (sc == null || sc.id <= 0 || sc.isCollector || sc.isVeinCollector) continue;

                        if (sc.isStellar) // Interstellar Logistics Station
                        {
                            for (int i = sc.storage.Length - 1; i >= 0; i--)
                            {
                                StationStore ss = sc.storage[i];
                                if (ss.itemId <= 0) continue;

                                if (ss.remoteLogic == ELogisticStorage.Supply && ss.count > 0)
                                {
                                    int[] result = AddItem(ss.itemId, ss.count, ss.inc, false);
                                    sc.storage[i].count -= result[0];
                                    sc.storage[i].inc -= result[1];
                                }
                                else if (ss.remoteLogic == ELogisticStorage.Demand)
                                {
                                    int expectCount = ss.max - ss.remoteOrder - ss.count;
                                    if (expectCount <= 0) continue;
                                    int[] result = TakeItem(ss.itemId, expectCount);
                                    sc.storage[i].count += result[0];
                                    sc.storage[i].inc += result[1];
                                }
                            }
                        }
                        else // Planetary Logistics Station
                        {
                            for (int i = sc.storage.Length - 1; i >= 0; i--)
                            {
                                StationStore ss = sc.storage[i];
                                if (ss.itemId <= 0) continue;

                                if (ss.localLogic == ELogisticStorage.Supply && ss.count > 0)
                                {
                                    int[] result = AddItem(ss.itemId, ss.count, ss.inc, false);
                                    sc.storage[i].count -= result[0];
                                    sc.storage[i].inc -= result[1];
                                }
                                else if (ss.localLogic == ELogisticStorage.Demand)
                                {
                                    int expectCount = ss.max - ss.localOrder - ss.count;
                                    if (expectCount <= 0) continue;
                                    int[] result = TakeItem(ss.itemId, expectCount);
                                    sc.storage[i].count += result[0];
                                    sc.storage[i].inc += result[1];
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessTransport"] = true;
            }
        }

        void ProcessStorage(object state)
        {
            Logger.LogDebug("ProcessStorage");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;

                    foreach (StorageComponent sc in pf.factoryStorage.storagePool)
                    {
                        if (sc == null || sc.isEmpty) continue;
                        for (int i = sc.grids.Length - 1; i >= 0; i--)
                        {
                            StorageComponent.GRID grid = sc.grids[i];
                            if (grid.itemId <= 0 || grid.count <= 0) continue;
                            int[] result = AddItem(grid.itemId, grid.count, grid.inc, false);
                            if (result[0] != 0)
                            {
                                sc.grids[i].count -= result[0];
                                sc.grids[i].inc -= result[1];
                                if (sc.grids[i].count <= 0)
                                {
                                    sc.grids[i].itemId = sc.grids[i].filter;
                                }
                            }
                        }
                        sc.NotifyStorageChange();
                    }

                    for (int i = pf.factoryStorage.tankPool.Length - 1; i >= 0; --i)
                    {
                        TankComponent tc = pf.factoryStorage.tankPool[i];
                        if (tc.id == 0 || tc.fluidId == 0 || tc.fluidCount == 0) continue;
                        int[] result = AddItem(tc.fluidId, tc.fluidCount, tc.fluidInc, false);
                        pf.factoryStorage.tankPool[i].fluidCount -= result[0];
                        pf.factoryStorage.tankPool[i].fluidInc -= result[1];
                        if (pf.factoryStorage.tankPool[i].fluidCount <= 0)
                        {
                            pf.factoryStorage.tankPool[i].fluidId = 0;
                            pf.factoryStorage.tankPool[i].fluidInc = 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessStorage"] = true;
            }
        }

        void ProcessAssembler(object state)
        {
            Logger.LogDebug("ProcessAssembler");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    foreach (AssemblerComponent ac in pf.factorySystem.assemblerPool)
                    {
                        if (ac.id <= 0 || ac.recipeId <= 0) continue;
                        for (int i = ac.products.Length - 1; i >= 0; i--)
                        {
                            if (ac.produced[i] > 0)
                                ac.produced[i] -= AddItem(ac.products[i], ac.produced[i], 0)[0];
                        }
                        for (int i = ac.requires.Length - 1; i >= 0; i--)
                        {
                            int expectCount = Math.Max(ac.requireCounts[i] * 5 - ac.served[i], 0);
                            if (expectCount > 0)
                            {
                                int[] result = TakeItem(ac.requires[i], expectCount);
                                ac.served[i] += result[0];
                                ac.incServed[i] += result[1];
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessAssembler"] = true;
            }
        }

        void ProcessMiner(object state)
        {
            Logger.LogDebug("ProcessMiner");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;

                    for (int i = pf.factorySystem.minerPool.Length - 1; i >= 0; i--)
                    {
                        MinerComponent mc = pf.factorySystem.minerPool[i];
                        if (mc.id <= 0 || mc.productId <= 0 || mc.productCount <= 0) continue;
                        int[] result = AddItem(mc.productId, mc.productCount, 0, false);
                        pf.factorySystem.minerPool[i].productCount -= result[0];
                    }

                    foreach (StationComponent sc in pf.transport.stationPool)
                    {
                        if (sc == null || sc.id <= 0)
                        {
                            continue;
                        }
                        if (sc.isStellar && sc.isCollector)
                        {
                            for (int i = sc.storage.Length - 1; i >= 0; i--)
                            {
                                StationStore ss = sc.storage[i];
                                if (ss.itemId <= 0 || ss.count <= 0 || ss.remoteLogic != ELogisticStorage.Supply)
                                    continue;
                                int[] result;
                                result = AddItem(ss.itemId, ss.count, 0, false);
                                sc.storage[i].count -= result[0];
                            }
                        }
                        else if (sc.isVeinCollector)
                        {
                            StationStore ss = sc.storage[0];
                            if (ss.itemId <= 0 || ss.count <= 0 || ss.localLogic != ELogisticStorage.Supply)
                                continue;
                            int[] result = AddItem(ss.itemId, ss.count, 0, false);
                            sc.storage[0].count -= result[0];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            taskState["ProcessMiner"] = true;
        }

        int ThermalPowerPlantFuel()
        {
            Logger.LogDebug("thermalPowerPlantFuel");
            if (fuelId.Value != 0 && TakeItem(fuelId.Value, 1)[0] > 0)
            {
                return fuelId.Value;
            }

            if (TakeItem(1114, 1)[0] > 0) return 1114;
            if (TakeItem(1120, 1)[0] > 0) return 1120;
            if (TakeItem(1006, 1)[0] > 0) return 1006;

            return 0;
        }

        void ProcessPowerGenerator(object state)
        {
            Logger.LogDebug("ProcessPowerGenerator");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    for (int i = pf.powerSystem.genPool.Length - 1; i >= 0; i--)
                    {
                        PowerGeneratorComponent pgc = pf.powerSystem.genPool[i];
                        if (pgc.id <= 0) continue;
                        if (pgc.gamma == true)
                        {
                            if (pgc.catalystPoint + pgc.catalystIncPoint < 3600)
                            {
                                int[] result = TakeItem(1209, 3);
                                if (result[0] > 0)
                                {
                                    pf.powerSystem.genPool[i].catalystId = 1209;
                                    pf.powerSystem.genPool[i].catalystPoint += result[0] * 3600;
                                    pf.powerSystem.genPool[i].catalystIncPoint += result[1] * 3600;
                                }
                            }
                            if (pgc.productId > 0 && pgc.productCount >= 1)
                            {
                                int[] result = AddItem(pgc.productId, (int)pgc.productCount, 0);
                                pf.powerSystem.genPool[i].productCount -= result[0];
                            }
                            continue;
                        }

                        int fuelId = 0;
                        switch (pgc.fuelMask)
                        {
                            case 1:
                                if (autoReplenishTPPFuel.Value)
                                {
                                    fuelId = ThermalPowerPlantFuel();
                                }
                                break;
                            case 2: fuelId = 1802; break;
                            case 4:
                                if (TakeItem(1804, 1)[0] > 0)
                                    fuelId = 1804;
                                else
                                    fuelId = 1803;
                                break;
                        }
                        if (fuelId == 0)
                        {
                            continue;
                        }
                        if (fuelId != pgc.fuelId && pgc.fuelCount == 0)
                        {
                            int[] result = TakeItem(fuelId, 5);
                            pf.powerSystem.genPool[i].SetNewFuel(fuelId, (short)result[0], (short)result[1]);
                        }
                        else if (fuelId == pgc.fuelId && pgc.fuelCount < 5)
                        {
                            int[] result = TakeItem(fuelId, 5 - pgc.fuelCount);
                            pf.powerSystem.genPool[i].fuelCount += (short)result[0];
                            pf.powerSystem.genPool[i].fuelInc += (short)result[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessPowerGenerator"] = true;
            }
        }

        void ProcessPowerExchanger(object state)
        {
            Logger.LogDebug("ProcessPowerExchanger");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    for (int i = pf.powerSystem.excPool.Length - 1; i >= 0; i--)
                    {
                        PowerExchangerComponent pec = pf.powerSystem.excPool[i];
                        if (pec.targetState == -1)
                        {
                            if (pec.fullCount < 3)
                            {
                                int[] result = TakeItem(pec.fullId, 3 - pec.fullCount);
                                pf.powerSystem.excPool[i].fullCount += (short)result[0];
                            }
                            if (pec.emptyCount > 0)
                            {
                                int[] result = AddItem(pec.emptyId, pec.emptyCount, 0);
                                pf.powerSystem.excPool[i].emptyCount -= (short)result[0];
                            }
                        }
                        else if (pec.targetState == 1)
                        {
                            if (pec.emptyCount < 5)
                            {
                                int[] result = TakeItem(pec.emptyId, 5 - pec.emptyCount);
                                pf.powerSystem.excPool[i].emptyCount += (short)result[0];
                            }
                            if (pec.fullCount > 0)
                            {
                                int[] result = AddItem(pec.fullId, pec.fullCount, 0);
                                pf.powerSystem.excPool[i].fullCount -= (short)result[0];
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessPowerExchanger"] = true;
            }
        }

        void ProcessSilo(object state)
        {
            Logger.LogDebug("ProcessSilo");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    for (int i = pf.factorySystem.siloPool.Length - 1; i >= 0; i--)
                    {
                        SiloComponent sc = pf.factorySystem.siloPool[i];
                        if (sc.id > 0 && sc.bulletCount <= 3)
                        {
                            int[] result = TakeItem(sc.bulletId, 10);
                            pf.factorySystem.siloPool[i].bulletCount += result[0];
                            pf.factorySystem.siloPool[i].bulletInc += result[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessSilo"] = true;
            }
        }

        void ProcessEjector(object state)
        {
            Logger.LogDebug("ProcessEjector");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    for (int i = pf.factorySystem.ejectorPool.Length - 1; i >= 0; i--)
                    {
                        EjectorComponent ec = pf.factorySystem.ejectorPool[i];
                        if (ec.id > 0 && ec.bulletCount <= 5)
                        {
                            int[] result = TakeItem(ec.bulletId, 15);
                            pf.factorySystem.ejectorPool[i].bulletCount += result[0];
                            pf.factorySystem.ejectorPool[i].bulletInc += result[1];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessEjector"] = true;
            }
        }

        void ProcessLab(object state)
        {
            Logger.LogDebug("ProcessLab");
            try
            {
                for (int index = GameMain.data.factories.Length - 1; index >= 0; index--)
                {
                    PlanetFactory pf = GameMain.data.factories[index];
                    if (pf == null) continue;
                    foreach (LabComponent lc in pf.factorySystem.labPool)
                    {
                        if (lc.id <= 0) continue;
                        if (lc.recipeId > 0)
                        {
                            for (int i = lc.products.Length - 1; i >= 0; i--)
                            {
                                if (lc.produced[i] > 0)
                                {
                                    int[] result = AddItem(lc.products[i], lc.produced[i], 0);
                                    lc.produced[i] -= result[0];
                                }
                            }
                            for (int i = lc.requires.Length - 1; i >= 0; i--)
                            {
                                int expectCount = lc.requireCounts[i] * 3 - lc.served[i] - lc.incServed[i];
                                int[] result = TakeItem(lc.requires[i], expectCount);
                                lc.served[i] += result[0];
                                lc.incServed[i] += result[1];
                            }
                        }
                        else if (lc.researchMode == true)
                        {
                            for (int i = lc.matrixPoints.Length - 1; i >= 0; i--)
                            {
                                if (lc.matrixPoints[i] <= 0) continue;
                                if (lc.matrixServed[i] >= lc.matrixPoints[i] * 3600) continue;
                                int[] result = TakeItem(LabComponent.matrixIds[i], lc.matrixPoints[i]);
                                lc.matrixServed[i] += result[0] * 3600;
                                lc.matrixIncServed[i] += result[1] * 3600;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessLab"] = true;
            }
        }

        void ProcessTurret(object state)
        {
            Logger.LogDebug("ProcessTurret");
            try
            {
                for (int pIndex = GameMain.data.factories.Length - 1; pIndex >= 0; pIndex--)
                {
                    PlanetFactory pf = GameMain.data.factories[pIndex];
                    if (pf == null) continue;
                    for (int index = pf.defenseSystem.turrets.buffer.Length - 1; index >= 0; index--)
                    {
                        TurretComponent tc = pf.defenseSystem.turrets.buffer[index];
                        if (tc.id == 0 || tc.type == ETurretType.Laser || tc.ammoType == EAmmoType.None || tc.itemCount > 0 || tc.bulletCount > 0) continue;
                        foreach (int itemId in ammos[tc.ammoType])
                        {
                            int[] result = TakeItem(itemId, 50 - tc.itemCount);
                            if (result[0] != 0)
                            {
                                pf.defenseSystem.turrets.buffer[index].SetNewItem(itemId, (short)result[0], (short)result[1]);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessTurret"] = true;
            }
        }

        void ProcessBattleBase(object state)
        {
            Logger.LogDebug("ProcessBattleBase");
            try
            {
                int[] fighters = { 5103, 5102, 5101 };
                for (int pIndex = GameMain.data.factories.Length - 1; pIndex >= 0; pIndex--)
                {
                    PlanetFactory pf = GameMain.data.factories[pIndex];
                    if (pf == null) continue;
                    for (int bIndex = pf.defenseSystem.battleBases.buffer.Length - 1; bIndex >= 0; bIndex--)
                    {
                        BattleBaseComponent bbc = pf.defenseSystem.battleBases.buffer[bIndex];
                        if (bbc == null || bbc.combatModule == null) continue;

                        ModuleFleet fleet = bbc.combatModule.moduleFleets[0];
                        for (int index = fleet.fighters.Length - 1; index >= 0; index--)
                        {
                            ModuleFighter fighter = fleet.fighters[index];
                            if (fighter.count == 0)
                            {
                                foreach (int itemId in fighters)
                                {
                                    int[] result = TakeItem(itemId, 1);
                                    if (result[0] != 0)
                                    {
                                        fleet.AddFighterToPort(index, itemId);
                                        break;
                                    }
                                }
                            }
                        }

                        if (useStorege.Value) continue;
                        StorageComponent sc = bbc.storage;
                        if (sc.isEmpty) continue;
                        for (int i = sc.grids.Length - 1; i >= 0; i--)
                        {
                            StorageComponent.GRID grid = sc.grids[i];
                            if (grid.itemId <= 0 || grid.count <= 0) continue;
                            int[] result = AddItem(grid.itemId, grid.count, grid.inc, false);
                            if (result[0] != 0)
                            {
                                sc.grids[i].count -= result[0];
                                sc.grids[i].inc -= result[1];
                                if (sc.grids[i].count <= 0)
                                {
                                    sc.grids[i].itemId = sc.grids[i].filter;
                                }
                            }
                        }
                        sc.NotifyStorageChange();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }

            taskState["ProcessBattleBase"] = true;
        }

        void ClearBattleBase()
        {
            Logger.LogDebug("ClearBattleBase");
            try
            {
                var bans = GameMain.data.trashSystem.enemyDropBans;
                for (int pIndex = GameMain.data.factories.Length - 1; pIndex >= 0; pIndex--)
                {
                    PlanetFactory pf = GameMain.data.factories[pIndex];
                    if (pf == null) continue;
                    for (int bIndex = pf.defenseSystem.battleBases.buffer.Length - 1; bIndex >= 0; bIndex--)
                    {
                        BattleBaseComponent bbc = pf.defenseSystem.battleBases.buffer[bIndex];
                        if (bbc == null || bbc.storage == null) continue;
                        StorageComponent sc = bbc.storage;
                        if (sc.isEmpty) continue;
                        for (int i = sc.grids.Length - 1; i >= 0; i--)
                        {
                            StorageComponent.GRID grid = sc.grids[i];
                            if (bans.Contains(grid.itemId))
                            {
                                sc.grids[i].count = 0;
                                sc.grids[i].inc = 0;
                                sc.grids[i].itemId = sc.grids[i].filter;
                            }
                        }
                        sc.NotifyStorageChange();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
        }

        void ProcessPackage(object state)
        {
            Logger.LogDebug("ProcessPackage");
            try
            {
                bool changed = false;
                StorageComponent package = GameMain.mainPlayer.package;
                for (int index = package.grids.Length - 1; index >= 0; index--)
                {
                    StorageComponent.GRID grid = package.grids[index];
                    if (grid.filter != 0 && grid.count < grid.stackSize)
                    {
                        int[] result = TakeItem(grid.itemId, grid.stackSize - grid.count);
                        if (result[0] != 0)
                        {
                            package.grids[index].count += result[0];
                            package.grids[index].inc += result[1];
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    package.NotifyStorageChange();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
            }
            finally
            {
                taskState["ProcessPackage"] = true;
            }
        }

        // MODIFIED: This function now respects item limits
        int[] AddItem(int itemId, int count, int inc, bool assembler = true)
        {
            if (itemId <= 0 || count <= 0)
            {
                return new int[] { 0, 0 };
            }

            lock (remoteStorageLock)
            {
                // Ensure the item exists in storage, if not, create it with a default limit.
                if (!remoteStorage.ContainsKey(itemId))
                {
                    remoteStorage[itemId] = new RemoteStorageItem { count = 0, inc = 0, limit = 1000000 };
                }

                var storageItem = remoteStorage[itemId];

                // Calculate how much space is available
                int spaceAvailable = storageItem.limit - storageItem.count;
                if (spaceAvailable <= 0)
                {
                    return new int[] { 0, 0 }; // No space, can't add anything
                }

                // Determine the actual amount to add
                int amountToAdd = Math.Min(count, spaceAvailable);
                // Calculate proportional inc for the amount we are actually adding
                int incToAdd = SplitInc(count, inc, amountToAdd);

                storageItem.count += amountToAdd;
                storageItem.inc += incToAdd;

                // Return the amount that was actually added
                return new int[] { amountToAdd, incToAdd };
            }
        }

        int[] TakeItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0)
            {
                return new int[] { 0, 0 };
            }

            ItemProto item = LDB.items.Select(itemId);
            bool isInfinite = (infItems.Value) ||
                              (infVeins.Value && IsVein(itemId)) ||
                              (infBuildings.Value && item.CanBuild) ||
                              (infAmmo.Value && item.isAmmo) ||
                              (infFleet.Value && item.isFighter);

            if (isInfinite)
            {
                int inc = (autoSpray.Value && !costProliferator.Value && !item.CanBuild && !item.isFighter) ? count * 4 : 0;
                return new int[] { count, inc };
            }

            lock (remoteStorageLock)
            {
                if (remoteStorage.ContainsKey(itemId))
                {
                    var itemInStorage = remoteStorage[itemId];
                    int availableCount = itemInStorage.count;

                    if (availableCount > 0)
                    {
                        int takenCount = Math.Min(count, availableCount);
                        int takenInc = SplitInc(availableCount, itemInStorage.inc, takenCount);

                        itemInStorage.count -= takenCount;
                        itemInStorage.inc -= takenInc;

                        return new int[] { takenCount, takenInc };
                    }
                }
            }
            return new int[] { 0, 0 };
        }

        int SplitInc(int count, int inc, int expectCount)
        {
            if (count <= 0 || inc <= 0) return 0;
            double incPerCount = (double)inc / count;
            return (int)Math.Round(incPerCount * expectCount);
        }
    }
}