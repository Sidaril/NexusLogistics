﻿using BepInEx;
using BepInEx.Configuration;
using crecheng.DSPModSave;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NexusLogistics
{
    #region Helper Classes
    
    /// <summary>
    /// Contains constant definitions for in-game item IDs to improve readability and maintainability.
    /// Replaces "magic numbers" with descriptive names.
    /// </summary>
    public static class ItemIds
    {
        // Proliferators
        public const int ProliferatorMk1 = 1141;
        public const int ProliferatorMk2 = 1142;
        public const int ProliferatorMk3 = 1143;

        // Fuels
        public const int Coal = 1006;
        public const int Graphite = 1109;
        public const int CrudeOil = 1007;
        public const int RefinedOil = 1114;
        public const int Hydrogen = 1120;
        public const int HydrogenFuelRod = 1801;
        public const int FireIce = 1011;
        public const int EnergyShard = 5206;
        public const int CombustionUnit = 1128;
        public const int Wood = 1030;
        public const int PlantFuel = 1031;

        // Special Power Items
        public const int AntimatterFuelRod = 1802;
        public const int DeuteronFuelRod = 1803;
        public const int StrangeAnnihilationFuelRod = 1804;
        public const int CriticalPhoton = 1209;

        // Ammunition
        public const int TitaniumBullet = 1601;
        public const int SuperalloyBullet = 1602;
        public const int GravitonBullet = 1603;
        public const int ShellSet = 1604;
        public const int HighExplosiveShellSet = 1605;
        public const int CrystalShellSet = 1606;
        public const int PlasmaCapsule = 1607;
        public const int AntimatterCapsule = 1608;
        public const int SupersonicMissileSet = 1609;
        public const int GravitonMissileSet = 1610;
        public const int PrecisionDroney = 1611;
        public const int JammingCapsule = 1612;
        public const int SuppressionCapsule = 1613;

        // Fighters & Drones
        public const int AttackDrone = 5101;
        public const int Corvette = 5102;
        public const int Destroyer = 5103;

        // Vein / Raw Resources
        public const int Water = 1000;
        public const int SulfuricAcid = 1116;
        public const int Deuterium = 1121;
    }

    /// <summary>
    /// Provides helper methods related to Proliferator spray.
    /// </summary>
    public static class ProliferatorBonus
    {
        public static int GetSprayInc(int proliferatorId)
        {
            if (proliferatorId == ItemIds.ProliferatorMk3) return 75;
            if (proliferatorId == ItemIds.ProliferatorMk2) return 30;
            if (proliferatorId == ItemIds.ProliferatorMk1) return 15;
            return 0;
        }
    }

    #endregion

    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("crecheng.DSPModSave")]
    public class NexusLogistics : BaseUnityPlugin, IModCanSave
    {
        #region Constants and Fields
        public const string GUID = "com.Sidaril.dsp.NexusLogistics";
        public const string NAME = "NexusLogistics";
        public const string VERSION = "1.4.0";

        private const int SAVE_VERSION = 2;

        private enum ProliferatorSelection { All, Mk1, Mk2, Mk3 }
        private enum StorageCategory { RawResources, IntermediateProducts, BuildingsAndVehicles, AmmunitionAndCombat, ScienceMatrices }

        // Remote Storage Data
        private readonly Dictionary<int, RemoteStorageItem> remoteStorage = new Dictionary<int, RemoteStorageItem>();
        private readonly object remoteStorageLock = new object();

        // Configuration Entries
        private ConfigEntry<bool> autoSpray, costProliferator, infVeins, infItems, infSand, infBuildings, useStorege, autoCleanInventory;
        private ConfigEntry<bool> enableMod, autoReplenishPackage, autoReplenishTPPFuel, infFleet, infAmmo;
        private ConfigEntry<KeyboardShortcut> hotKey, storageHotKey;
        private ConfigEntry<ProliferatorSelection> proliferatorSelection;
        private ConfigEntry<int> fuelId;

        // Proliferation and Item Data
        private readonly List<(int, int)> proliferators = new List<(int, int)>();
        private readonly Dictionary<int, int> incPool = new Dictionary<int, int>();
        private Dictionary<EAmmoType, List<int>> ammos = new Dictionary<EAmmoType, List<int>>();

        // GUI State
        private bool showGUI = false;
        private bool showStorageGUI = false;
        private Rect windowRect = new Rect(700, 250, 600, 500);
        private Rect storageWindowRect = new Rect(100, 250, 550, 500);
        private Vector2 storageScrollPosition, mainPanelScrollPosition;
        private readonly Texture2D windowTexture = new Texture2D(10, 10);
        private int selectedPanel = 0;
        private readonly Dictionary<int, string> fuelOptions = new Dictionary<int, string>();
        private int selectedFuelIndex;
        private StorageCategory selectedStorageCategory = StorageCategory.RawResources;
        private readonly Dictionary<int, string> limitInputStrings = new Dictionary<int, string>();
        private List<KeyValuePair<int, RemoteStorageItem>> storageItemsForGUI = new List<KeyValuePair<int, RemoteStorageItem>>();

        #endregion

        #region Embedded Classes
        private class RemoteStorageItem
        {
            public int count;
            public int inc;
            public int limit = 1000000;

            public void Export(BinaryWriter w)
            {
                w.Write(count);
                w.Write(inc);
                w.Write(limit);
            }

            public static RemoteStorageItem Import(BinaryReader r, int saveVersion)
            {
                var item = new RemoteStorageItem
                {
                    count = r.ReadInt32(),
                    inc = r.ReadInt32(),
                    limit = 1000000
                };
                if (saveVersion >= 2)
                {
                    item.limit = r.ReadInt32();
                }
                return item;
            }
        }
        #endregion

        #region Unity Lifecycle Methods

        /// <summary>
        /// Main entry point for the mod. Called by BepInEx upon game start.
        /// </summary>
        void Start()
        {
            BindConfigs();
            InitializeData();

            // Start the main processing thread.
            new Thread(MainProcessingLoop)
            {
                IsBackground = true // Ensure thread doesn't prevent game from closing
            }.Start();
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
                if (showStorageGUI)
                {
                    // Refresh the item list for the GUI when it's opened.
                    RefreshStorageItemsForGUI();
                }
            }

            // Continuously update the item list if the storage window is open to reflect real-time changes.
            if (showStorageGUI)
            {
                RefreshStorageItemsForGUI();
            }
        }

        void OnGUI()
        {
            if (showGUI)
            {
                GUI.DrawTexture(windowRect, windowTexture);
                windowRect = GUI.Window(0, windowRect, WindowFunction, $"{NAME} {VERSION}");
            }
            if (showStorageGUI)
            {
                GUI.DrawTexture(storageWindowRect, windowTexture);
                storageWindowRect = GUI.Window(1, storageWindowRect, StorageWindowFunction, "Remote Storage Contents");
            }

            // Prevent click-through to the game world when GUI is active.
            if ((showGUI && windowRect.Contains(Event.current.mousePosition)) || (showStorageGUI && storageWindowRect.Contains(Event.current.mousePosition)))
            {
                Input.ResetInputAxes();
            }
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Binds all configuration settings from the config file.
        /// </summary>
        private void BindConfigs()
        {
            hotKey = Config.Bind("Window Shortcut Key", "Key", new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl));
            storageHotKey = Config.Bind("Window Shortcut Key", "Storage_Key", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl));
            enableMod = Config.Bind("Configuration", "EnableMod", true, "Enable MOD");
            autoReplenishPackage = Config.Bind("Configuration", "autoReplenishPackage", true, "Automatically replenish items with filtering enabled in the backpack (middle-click on the slot to enable filtering)");
            autoCleanInventory = Config.Bind("Configuration", "AutoCleanInventory", true, "Automatically move items from main inventory to matching logistic slots.");
            autoSpray = Config.Bind("Configuration", "AutoSpray", true, "Automatic Spraying. Automatically sprays other items within the logistics backpack and interstellar logistics stations");
            costProliferator = Config.Bind("Configuration", "CostProliferator", true, "Consume Proliferator. Consumes proliferators from the backpack or interstellar logistics station during automatic spraying");
            proliferatorSelection = Config.Bind("Configuration", "ProliferatorSelection", ProliferatorSelection.All, "Which Proliferator tier to use for automatic spraying.");
            infItems = Config.Bind("Configuration", "InfItems", false, "Infinite Items. All items in the logistics backpack and interstellar logistics stations have infinite quantity (cannot obtain achievements)");
            infVeins = Config.Bind("Configuration", "InfVeins", false, "Infinite Minerals. All minerals in the logistics backpack and interstellar logistics stations have infinite quantity");
            infBuildings = Config.Bind("Configuration", "InfBuildings", false, "Infinite Buildings. All buildings in the logistics backpack and interstellar logistics stations have infinite quantity");
            infSand = Config.Bind("Configuration", "InfSand", false, "Infinite Soil Pile. Soil pile quantity is infinite (fixed at 1G)");
            useStorege = Config.Bind("Configuration", "useStorege", true, "Recover items from storage boxes and liquid tanks");
            autoReplenishTPPFuel = Config.Bind("Configuration", "autoReplenishTPPFuel", true, "Automatically replenish fuel for thermal power plants");
            fuelId = Config.Bind("Configuration", "fuelId", 0, "Thermal Power Plant Fuel ID\n0: Auto-select...");
            infAmmo = Config.Bind("Configuration", "InfAmmo", false, "Infinite Ammo. Ammo in the logistics backpack and interstellar logistics stations have infinite quantity");
            infFleet = Config.Bind("Configuration", "infFleet", false, "Infinite Fleet. Drones and warships in the logistics backpack and interstellar logistics stations have infinite quantity");
        }

        /// <summary>
        /// Initializes data structures and other resources needed by the mod.
        /// </summary>
        private void InitializeData()
        {
            fuelOptions.Add(0, "Auto");
            fuelOptions.Add(ItemIds.Coal, "Coal");
            fuelOptions.Add(ItemIds.Graphite, "Graphite");
            fuelOptions.Add(ItemIds.CrudeOil, "Crude Oil");
            fuelOptions.Add(ItemIds.RefinedOil, "Refined Oil");
            fuelOptions.Add(ItemIds.Hydrogen, "Hydrogen");
            fuelOptions.Add(ItemIds.HydrogenFuelRod, "Hydrogen Fuel Rod");
            fuelOptions.Add(ItemIds.FireIce, "Fire Ice");
            fuelOptions.Add(ItemIds.EnergyShard, "Energy Shard");
            fuelOptions.Add(ItemIds.CombustionUnit, "Combustion Unit");
            fuelOptions.Add(ItemIds.Wood, "Wood");
            fuelOptions.Add(ItemIds.PlantFuel, "Plant Fuel");
            selectedFuelIndex = fuelOptions.Keys.ToList().FindIndex(id => id == fuelId.Value);

            proliferators.Add((ItemIds.ProliferatorMk3, 4));
            proliferators.Add((ItemIds.ProliferatorMk2, 2));
            proliferators.Add((ItemIds.ProliferatorMk1, 1));

            incPool.Add(ItemIds.ProliferatorMk1, 0);
            incPool.Add(ItemIds.ProliferatorMk2, 0);
            incPool.Add(ItemIds.ProliferatorMk3, 0);

            windowTexture.SetPixels(Enumerable.Repeat(new Color(0, 0, 0, 1), 100).ToArray());
            windowTexture.Apply();

            ammos.Add(EAmmoType.Bullet, new List<int> { ItemIds.GravitonBullet, ItemIds.SuperalloyBullet, ItemIds.TitaniumBullet });
            ammos.Add(EAmmoType.Missile, new List<int> { ItemIds.PrecisionDroney, ItemIds.GravitonMissileSet, ItemIds.SupersonicMissileSet });
            ammos.Add(EAmmoType.Cannon, new List<int> { ItemIds.CrystalShellSet, ItemIds.HighExplosiveShellSet, ItemIds.ShellSet });
            ammos.Add(EAmmoType.Plasma, new List<int> { ItemIds.AntimatterCapsule, ItemIds.PlasmaCapsule });
            ammos.Add(EAmmoType.EMCapsule, new List<int> { ItemIds.SuppressionCapsule, ItemIds.JammingCapsule });
        }

        #endregion

        #region Main Processing Loop

        /// <summary>
        /// The core logic loop of the mod, running on a separate thread.
        /// It orchestrates all the logistics processing tasks.
        /// </summary>
        private void MainProcessingLoop()
        {
            Logger.LogInfo("NexusLogistics processing thread started!");
            while (true)
            {
                DateTime startTime = DateTime.Now;
                try
                {
                    if (GameMain.instance == null || GameMain.instance.isMenuDemo || GameMain.isPaused || !GameMain.isRunning || GameMain.data == null)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    if (enableMod.Value)
                    {
                        if (infSand.Value && GameMain.mainPlayer.sandCount != 1000000000)
                        {
                            Traverse.Create(GameMain.mainPlayer).Property("sandCount").SetValue(1000000000);
                        }

                        CheckTech();

                        // Create a list of all processing tasks to be run this tick.
                        var tasks = new List<Task>
                        {
                            ProcessSpraying(),
                            ProcessDeliveryPackage(),
                            ProcessTransport(),
                            ProcessAssembler(),
                            ProcessMiner(),
                            ProcessPowerGenerator(),
                            ProcessPowerExchanger(),
                            ProcessSilo(),
                            ProcessEjector(),
                            ProcessLab(),
                            ProcessTurret(),
                            ProcessBattleBase()
                        };

                        if (useStorege.Value) tasks.Add(ProcessStorage());
                        if (autoReplenishPackage.Value) tasks.Add(ProcessPackage());
                        if (autoCleanInventory.Value) tasks.Add(ProcessInventoryToLogistics());

                        // Wait for all tasks to complete before starting the next tick.
                        Task.WhenAll(tasks).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("NexusLogistics encountered a critical exception in the main loop!");
                    Logger.LogError(ex.ToString());
                }
                finally
                {
                    double cost = (DateTime.Now - startTime).TotalMilliseconds;
                    if (cost < 50)
                    {
                        Thread.Sleep((int)(50 - cost));
                    }
                }
            }
        }

        #endregion

        #region GUI Methods

        void WindowFunction(int windowID)
        {
            string[] panels = { "Main Options", "Items", "Combat" };
            selectedPanel = GUILayout.Toolbar(selectedPanel, panels);
            switch (selectedPanel)
            {
                case 0: MainPanel(); break;
                case 1: ItemPanel(); break;
                case 2: FightPanel(); break;
            }
            GUI.DragWindow();
        }

        void StorageWindowFunction(int windowID)
        {
            string[] categories = { "Raw", "Intermeds", "Buildings", "Combat", "Science" };
            selectedStorageCategory = (StorageCategory)GUILayout.Toolbar((int)selectedStorageCategory, categories);

            GUILayout.BeginVertical();
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Name", GUILayout.Width(150));
            GUILayout.Label("Count", GUILayout.Width(100));
            GUILayout.Label("Proliferation", GUILayout.Width(100));
            GUILayout.Label("Limit", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition);

            var originalContentColor = GUI.contentColor;
            try
            {
                // Use the cached list for display to avoid locking during GUI rendering.
                foreach (var pair in storageItemsForGUI)
                {
                    int itemId = pair.Key;
                    RemoteStorageItem item = pair.Value;
                    ItemProto itemProto = LDB.items.Select(itemId);
                    string itemName = itemProto.name;

                    if (!limitInputStrings.ContainsKey(itemId))
                    {
                        limitInputStrings[itemId] = item.limit.ToString();
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(itemName, GUILayout.Width(150));
                    GUILayout.Label(item.count.ToString("N0"), GUILayout.Width(100));

                    var (prolifText, prolifColor) = GetProliferationStatus(item.count, item.inc, itemId);
                    GUI.contentColor = prolifColor;
                    GUILayout.Label(prolifText, GUILayout.Width(100));
                    GUI.contentColor = originalContentColor;

                    string currentInput = limitInputStrings[itemId];
                    string newInput = GUILayout.TextField(currentInput, GUILayout.Width(100));

                    if (newInput != currentInput)
                    {
                        limitInputStrings[itemId] = newInput;
                        if (int.TryParse(newInput, out int newLimit) && newLimit >= 0)
                        {
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
                GUI.contentColor = originalContentColor;
                GUILayout.Label("Error displaying storage: " + e.Message);
            }
            finally
            {
                GUI.contentColor = originalContentColor;
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        void MainPanel()
        {
            mainPanelScrollPosition = GUILayout.BeginScrollView(mainPanelScrollPosition, false, true);
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            enableMod.Value = GUILayout.Toggle(enableMod.Value, "Enable MOD");
            autoReplenishPackage.Value = GUILayout.Toggle(autoReplenishPackage.Value, "Auto Replenish Filtered Items");
            autoCleanInventory.Value = GUILayout.Toggle(autoCleanInventory.Value, "Auto Clean Inventory to Logistic Slots");

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            autoSpray.Value = GUILayout.Toggle(autoSpray.Value, "Auto Spray");
            if (autoSpray.Value)
            {
                costProliferator.Value = GUILayout.Toggle(costProliferator.Value, "Consume Proliferator");
            }
            GUILayout.EndHorizontal();

            if (autoSpray.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Proliferator Tier:", GUILayout.Width(120));
                proliferatorSelection.Value = (ProliferatorSelection)GUILayout.Toolbar((int)proliferatorSelection.Value, new string[] { "All", "MK.I", "MK.II", "MK.III" });
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(15);
            useStorege.Value = GUILayout.Toggle(useStorege.Value, "Recover from storage boxes/tanks");

            GUILayout.Space(15);
            autoReplenishTPPFuel.Value = GUILayout.Toggle(autoReplenishTPPFuel.Value, "Auto-refuel Thermal Power Plants");
            if (autoReplenishTPPFuel.Value)
            {
                selectedFuelIndex = GUILayout.SelectionGrid(selectedFuelIndex, fuelOptions.Values.ToArray(), 3, GUI.skin.toggle);
                fuelId.Value = fuelOptions.Keys.ToArray()[selectedFuelIndex];
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        void ItemPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            infBuildings.Value = GUILayout.Toggle(infBuildings.Value, "Infinite Buildings");
            infVeins.Value = GUILayout.Toggle(infVeins.Value, "Infinite Minerals");
            infItems.Value = GUILayout.Toggle(infItems.Value, "Infinite Items (Disables Achievements)");
            infSand.Value = GUILayout.Toggle(infSand.Value, "Infinite Soil Pile");
            GUILayout.EndVertical();
        }

        void FightPanel()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            infAmmo.Value = GUILayout.Toggle(infAmmo.Value, "Infinite Ammo");
            infFleet.Value = GUILayout.Toggle(infFleet.Value, "Infinite Fleet");
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("Clear Banned Items from Battle Bases", "Removes items from Battlefield Analysis Bases that you have marked not to be picked up."), GUILayout.ExpandWidth(true)))
            {
                ClearBattleBaseBannedItems();
            }
            GUILayout.EndVertical();
        }

        #endregion

        #region Logistics Processing Tasks

        Task ProcessSpraying() => Task.Run(() =>
        {
            try
            {
                if (!autoSpray.Value) return;

                lock (remoteStorageLock)
                {
                    var activeProliferators = new List<(int, int)>();
                    switch (proliferatorSelection.Value)
                    {
                        case ProliferatorSelection.Mk1: activeProliferators.Add((ItemIds.ProliferatorMk1, 1)); break;
                        case ProliferatorSelection.Mk2: activeProliferators.Add((ItemIds.ProliferatorMk2, 2)); break;
                        case ProliferatorSelection.Mk3: activeProliferators.Add((ItemIds.ProliferatorMk3, 4)); break;
                        case ProliferatorSelection.All: default: activeProliferators.AddRange(proliferators); break;
                    }

                    if (costProliferator.Value)
                    {
                        foreach (var (proliferatorId, sprayLevel) in activeProliferators)
                        {
                            int factor = ProliferatorBonus.GetSprayInc(proliferatorId);
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

                        if (itemId >= ItemIds.ProliferatorMk1 && itemId <= ItemIds.ProliferatorMk3)
                        {
                            storageItem.inc = storageItem.count * 4;
                            continue;
                        }

                        ItemProto itemProto = LDB.items.Select(itemId);
                        if (itemProto.CanBuild || itemProto.isFighter) continue;

                        if (!costProliferator.Value)
                        {
                            int maxSprayLevel = activeProliferators.Count > 0 ? activeProliferators.Max(p => p.Item2) : 4;
                            if (storageItem.inc < storageItem.count * maxSprayLevel) storageItem.inc = storageItem.count * maxSprayLevel;
                            continue;
                        }

                        foreach (var (proliferatorId, sprayLevel) in activeProliferators)
                        {
                            int expectedInc = storageItem.count * sprayLevel - storageItem.inc;
                            if (expectedInc <= 0) break;

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
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessDeliveryPackage() => Task.Run(() =>
        {
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
                        int supplyInc = SplitInc(grid.count, grid.inc, supplyCount);
                        int[] result = AddItem(grid.itemId, supplyCount, supplyInc, true);
                        if (result[0] > 0)
                        {
                            deliveryPackage.grids[i].count -= result[0];
                            deliveryPackage.grids[i].inc -= result[1];
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessTransport() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    foreach (StationComponent sc in pf.transport.stationPool)
                    {
                        if (sc == null || sc.id <= 0 || sc.isCollector || sc.isVeinCollector) continue;

                        for (int i = 0; i < sc.storage.Length; i++)
                        {
                            StationStore ss = sc.storage[i];
                            if (ss.itemId <= 0) continue;

                            var logic = sc.isStellar ? ss.remoteLogic : ss.localLogic;
                            var order = sc.isStellar ? ss.remoteOrder : ss.localOrder;

                            if (logic == ELogisticStorage.Supply && ss.count > 0)
                            {
                                int[] result = AddItem(ss.itemId, ss.count, ss.inc);
                                sc.storage[i].count -= result[0];
                                sc.storage[i].inc -= result[1];
                            }
                            else if (logic == ELogisticStorage.Demand)
                            {
                                int expectCount = ss.max - order - ss.count;
                                if (expectCount <= 0) continue;
                                int[] result = TakeItem(ss.itemId, expectCount);
                                sc.storage[i].count += result[0];
                                sc.storage[i].inc += result[1];
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessStorage() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;

                    foreach (StorageComponent sc in pf.factoryStorage.storagePool)
                    {
                        if (sc == null || sc.isEmpty) continue;
                        bool changed = false;
                        for (int i = 0; i < sc.grids.Length; i++)
                        {
                            StorageComponent.GRID grid = sc.grids[i];
                            if (grid.itemId <= 0 || grid.count <= 0) continue;
                            int[] result = AddItem(grid.itemId, grid.count, grid.inc);
                            if (result[0] > 0)
                            {
                                sc.grids[i].count -= result[0];
                                sc.grids[i].inc -= result[1];
                                if (sc.grids[i].count <= 0)
                                {
                                    sc.grids[i].itemId = sc.grids[i].filter;
                                }
                                changed = true;
                            }
                        }
                        if (changed) sc.NotifyStorageChange();
                    }

                    for (int i = 0; i < pf.factoryStorage.tankPool.Length; ++i)
                    {
                        TankComponent tc = pf.factoryStorage.tankPool[i];
                        if (tc.id == 0 || tc.fluidId == 0 || tc.fluidCount == 0) continue;
                        int[] result = AddItem(tc.fluidId, tc.fluidCount, tc.fluidInc);
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
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessAssembler() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    foreach (AssemblerComponent ac in pf.factorySystem.assemblerPool)
                    {
                        if (ac.id <= 0 || ac.recipeId <= 0) continue;
                        for (int i = 0; i < ac.products.Length; i++)
                        {
                            if (ac.produced[i] > 0)
                                ac.produced[i] -= AddItem(ac.products[i], ac.produced[i], 0)[0];
                        }
                        for (int i = 0; i < ac.requires.Length; i++)
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
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessMiner() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;

                    for (int i = 0; i < pf.factorySystem.minerPool.Length; i++)
                    {
                        MinerComponent mc = pf.factorySystem.minerPool[i];
                        if (mc.id <= 0 || mc.productId <= 0 || mc.productCount <= 0) continue;
                        int[] result = AddItem(mc.productId, mc.productCount, 0);
                        pf.factorySystem.minerPool[i].productCount -= result[0];
                    }

                    foreach (StationComponent sc in pf.transport.stationPool)
                    {
                        if (sc == null || sc.id <= 0) continue;

                        if (sc.isStellar && sc.isCollector)
                        {
                            for (int i = 0; i < sc.storage.Length; i++)
                            {
                                StationStore ss = sc.storage[i];
                                if (ss.itemId <= 0 || ss.count <= 0 || ss.remoteLogic != ELogisticStorage.Supply) continue;
                                int[] result = AddItem(ss.itemId, ss.count, 0);
                                sc.storage[i].count -= result[0];
                            }
                        }
                        else if (sc.isVeinCollector)
                        {
                            StationStore ss = sc.storage[0];
                            if (ss.itemId <= 0 || ss.count <= 0 || ss.localLogic != ELogisticStorage.Supply) continue;
                            int[] result = AddItem(ss.itemId, ss.count, 0);
                            sc.storage[0].count -= result[0];
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessPowerGenerator() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.powerSystem.genPool.Length; i++)
                    {
                        PowerGeneratorComponent pgc = pf.powerSystem.genPool[i];
                        if (pgc.id <= 0) continue;
                        if (pgc.gamma) // Artificial Sun
                        {
                            if (pgc.catalystPoint + pgc.catalystIncPoint < 3600)
                            {
                                int[] result = TakeItem(ItemIds.CriticalPhoton, 3);
                                if (result[0] > 0)
                                {
                                    pf.powerSystem.genPool[i].catalystId = ItemIds.CriticalPhoton;
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

                        int fuelToUse = 0;
                        switch (pgc.fuelMask)
                        {
                            case 1: if (autoReplenishTPPFuel.Value) fuelToUse = GetBestThermalFuel(); break;
                            case 2: fuelToUse = ItemIds.AntimatterFuelRod; break;
                            case 4: fuelToUse = TakeItem(ItemIds.StrangeAnnihilationFuelRod, 1)[0] > 0 ? ItemIds.StrangeAnnihilationFuelRod : ItemIds.DeuteronFuelRod; break;
                        }

                        if (fuelToUse == 0) continue;

                        if (fuelToUse != pgc.fuelId && pgc.fuelCount == 0)
                        {
                            int[] result = TakeItem(fuelToUse, 5);
                            pf.powerSystem.genPool[i].SetNewFuel(fuelToUse, (short)result[0], (short)result[1]);
                        }
                        else if (fuelToUse == pgc.fuelId && pgc.fuelCount < 5)
                        {
                            int[] result = TakeItem(fuelToUse, 5 - pgc.fuelCount);
                            pf.powerSystem.genPool[i].fuelCount += (short)result[0];
                            pf.powerSystem.genPool[i].fuelInc += (short)result[1];
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessPowerExchanger() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.powerSystem.excPool.Length; i++)
                    {
                        PowerExchangerComponent pec = pf.powerSystem.excPool[i];
                        if (pec.targetState == -1) // Discharge
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
                        else if (pec.targetState == 1) // Charge
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
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessSilo() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.factorySystem.siloPool.Length; i++)
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
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessEjector() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.factorySystem.ejectorPool.Length; i++)
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
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessLab() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    foreach (LabComponent lc in pf.factorySystem.labPool)
                    {
                        if (lc.id <= 0) continue;
                        if (lc.recipeId > 0)
                        {
                            for (int i = 0; i < lc.products.Length; i++)
                            {
                                if (lc.produced[i] > 0)
                                {
                                    lc.produced[i] -= AddItem(lc.products[i], lc.produced[i], 0)[0];
                                }
                            }
                            for (int i = 0; i < lc.requires.Length; i++)
                            {
                                int expectCount = lc.requireCounts[i] * 3 - lc.served[i] - lc.incServed[i];
                                int[] result = TakeItem(lc.requires[i], expectCount);
                                lc.served[i] += result[0];
                                lc.incServed[i] += result[1];
                            }
                        }
                        else if (lc.researchMode)
                        {
                            for (int i = 0; i < lc.matrixPoints.Length; i++)
                            {
                                if (lc.matrixPoints[i] <= 0 || lc.matrixServed[i] >= lc.matrixPoints[i] * 3600) continue;
                                int[] result = TakeItem(LabComponent.matrixIds[i], lc.matrixPoints[i]);
                                lc.matrixServed[i] += result[0] * 3600;
                                lc.matrixIncServed[i] += result[1] * 3600;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessTurret() => Task.Run(() =>
        {
            try
            {
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.defenseSystem.turrets.buffer.Length; i++)
                    {
                        TurretComponent tc = pf.defenseSystem.turrets.buffer[i];
                        if (tc.id == 0 || tc.type == ETurretType.Laser || tc.ammoType == EAmmoType.None || tc.itemCount > 0 || tc.bulletCount > 0) continue;
                        foreach (int itemId in ammos[tc.ammoType])
                        {
                            int[] result = TakeItem(itemId, 50 - tc.itemCount);
                            if (result[0] != 0)
                            {
                                pf.defenseSystem.turrets.buffer[i].SetNewItem(itemId, (short)result[0], (short)result[1]);
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessBattleBase() => Task.Run(() =>
        {
            try
            {
                int[] fighters = { ItemIds.Destroyer, ItemIds.Corvette, ItemIds.AttackDrone };
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.defenseSystem.battleBases.buffer.Length; i++)
                    {
                        BattleBaseComponent bbc = pf.defenseSystem.battleBases.buffer[i];
                        if (bbc?.combatModule == null) continue;

                        ModuleFleet fleet = bbc.combatModule.moduleFleets[0];
                        for (int fIndex = 0; fIndex < fleet.fighters.Length; fIndex++)
                        {
                            if (fleet.fighters[fIndex].count == 0)
                            {
                                foreach (int itemId in fighters)
                                {
                                    if (TakeItem(itemId, 1)[0] != 0)
                                    {
                                        fleet.AddFighterToPort(fIndex, itemId);
                                        break;
                                    }
                                }
                            }
                        }

                        if (useStorege.Value) continue;
                        StorageComponent sc = bbc.storage;
                        if (sc.isEmpty) continue;
                        bool changed = false;
                        for (int gIndex = 0; gIndex < sc.grids.Length; gIndex++)
                        {
                            StorageComponent.GRID grid = sc.grids[gIndex];
                            if (grid.itemId <= 0 || grid.count <= 0) continue;
                            int[] result = AddItem(grid.itemId, grid.count, grid.inc);
                            if (result[0] > 0)
                            {
                                sc.grids[gIndex].count -= result[0];
                                sc.grids[gIndex].inc -= result[1];
                                if (sc.grids[gIndex].count <= 0)
                                {
                                    sc.grids[gIndex].itemId = sc.grids[gIndex].filter;
                                }
                                changed = true;
                            }
                        }
                        if (changed) sc.NotifyStorageChange();
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessPackage() => Task.Run(() =>
        {
            try
            {
                bool changed = false;
                StorageComponent package = GameMain.mainPlayer.package;
                for (int i = 0; i < package.grids.Length; i++)
                {
                    StorageComponent.GRID grid = package.grids[i];
                    if (grid.filter != 0 && grid.count < grid.stackSize)
                    {
                        int[] result = TakeItem(grid.itemId, grid.stackSize - grid.count);
                        if (result[0] != 0)
                        {
                            package.grids[i].count += result[0];
                            package.grids[i].inc += result[1];
                            changed = true;
                        }
                    }
                }
                if (changed)
                {
                    package.NotifyStorageChange();
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });
        
        /// <summary>
        /// New task to automatically move items from the main inventory to matching logistic slots.
        /// </summary>
        Task ProcessInventoryToLogistics() => Task.Run(() =>
        {
            try
            {
                var player = GameMain.mainPlayer;
                if (player == null) return;

                var mainInventory = player.package;
                var logisticsInventory = player.deliveryPackage;

                if (!logisticsInventory.unlocked) return;

                // Create a quick lookup for logistic slot items
                var logisticSlotItems = new Dictionary<int, int>(); // Key: itemId, Value: grid index
                for (int i = 0; i < logisticsInventory.gridLength; i++)
                {
                    if (logisticsInventory.grids[i].itemId > 0)
                    {
                        if (!logisticSlotItems.ContainsKey(logisticsInventory.grids[i].itemId))
                        {
                            logisticSlotItems.Add(logisticsInventory.grids[i].itemId, i);
                        }
                    }
                }

                bool changed = false;
                // Iterate through main inventory
                for (int i = 0; i < mainInventory.size; i++)
                {
                    var grid = mainInventory.grids[i];
                    if (grid.itemId <= 0 || grid.count <= 0) continue;

                    // Check if a logistic slot exists for this item
                    if (logisticSlotItems.TryGetValue(grid.itemId, out int logisticSlotIndex))
                    {
                        // Found a matching logistic slot. Move the items.
                        int amountToMove = grid.count;

                        // Add item to the logistic slot
                        logisticsInventory.grids[logisticSlotIndex].count += amountToMove;
                        logisticsInventory.grids[logisticSlotIndex].inc += grid.inc;

                        // Remove item from main inventory
                        mainInventory.TakeItem(grid.itemId, amountToMove, out int inc);

                        changed = true;
                    }
                }

                if (changed)
                {
                    // Notify the game that the inventory has changed to update the UI
                    mainInventory.NotifyStorageChange();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error in ProcessInventoryToLogistics: {ex}");
            }
        });


        #endregion

        #region Helper and Utility Methods

        /// <summary>
        /// Adds an item to the central remote storage.
        /// </summary>
        /// <param name="bypassLimit">If true, the storage item limit will be ignored. Used for player logistic slot recycling.</param>
        /// <returns>An array containing the count and inc of the item actually added.</returns>
        int[] AddItem(int itemId, int count, int inc, bool bypassLimit = false)
        {
            if (itemId <= 0 || count <= 0) return new int[] { 0, 0 };

            lock (remoteStorageLock)
            {
                if (!remoteStorage.ContainsKey(itemId))
                {
                    ItemProto itemProto = LDB.items.Select(itemId);
                    StorageCategory category = GetItemCategory(itemProto);
                    int defaultLimit = 1000000;
                    if (category == StorageCategory.BuildingsAndVehicles || category == StorageCategory.AmmunitionAndCombat)
                    {
                        defaultLimit = 1000;
                    }
                    remoteStorage[itemId] = new RemoteStorageItem { count = 0, inc = 0, limit = defaultLimit };
                }

                var storageItem = remoteStorage[itemId];
                int amountToAdd = count;

                if (!bypassLimit)
                {
                    int spaceAvailable = storageItem.limit - storageItem.count;
                    if (spaceAvailable <= 0) return new int[] { 0, 0 };
                    amountToAdd = Math.Min(count, spaceAvailable);
                }
                
                if (amountToAdd <= 0) return new int[] { 0, 0 };

                int incToAdd = SplitInc(count, inc, amountToAdd);

                storageItem.count += amountToAdd;
                storageItem.inc += incToAdd;

                return new int[] { amountToAdd, incToAdd };
            }
        }

        /// <summary>
        /// Takes an item from the central remote storage.
        /// </summary>
        /// <returns>An array containing the count and inc of the item actually taken.</returns>
        int[] TakeItem(int itemId, int count)
        {
            if (itemId <= 0 || count <= 0) return new int[] { 0, 0 };

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

        bool IsVein(int itemId)
        {
            int[] items = { ItemIds.Water, ItemIds.SulfuricAcid, ItemIds.Hydrogen, ItemIds.Deuterium };
            return items.Contains(itemId) || LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None;
        }

        int GetBestThermalFuel()
        {
            if (fuelId.Value != 0 && TakeItem(fuelId.Value, 1)[0] > 0)
            {
                return fuelId.Value;
            }

            // Fallback logic if preferred fuel is unavailable or not set
            if (TakeItem(ItemIds.RefinedOil, 1)[0] > 0) return ItemIds.RefinedOil;
            if (TakeItem(ItemIds.Hydrogen, 1)[0] > 0) return ItemIds.Hydrogen;
            if (TakeItem(ItemIds.Coal, 1)[0] > 0) return ItemIds.Coal;

            return 0;
        }

        void ClearBattleBaseBannedItems()
        {
            try
            {
                var bans = GameMain.data.trashSystem.enemyDropBans;
                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.defenseSystem.battleBases.buffer.Length; i++)
                    {
                        BattleBaseComponent bbc = pf.defenseSystem.battleBases.buffer[i];
                        if (bbc?.storage == null || bbc.storage.isEmpty) continue;
                        StorageComponent sc = bbc.storage;
                        bool changed = false;
                        for (int gIndex = 0; gIndex < sc.grids.Length; gIndex++)
                        {
                            if (bans.Contains(sc.grids[gIndex].itemId))
                            {
                                sc.grids[gIndex].count = 0;
                                sc.grids[gIndex].inc = 0;
                                sc.grids[gIndex].itemId = sc.grids[gIndex].filter;
                                changed = true;
                            }
                        }
                        if (changed) sc.NotifyStorageChange();
                    }
                }
            }
            catch (Exception ex) { Logger.LogError(ex); }
        }

        void CheckTech()
        {
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
                if (!deliveryPackage.unlocked) deliveryPackage.unlocked = true;
                deliveryPackage.NotifySizeChange();
            }

            if (GameMain.history.TechUnlocked(2307)) { if (GameMain.mainPlayer.package.size < 160) GameMain.mainPlayer.package.SetSize(160); }
            else if (GameMain.history.TechUnlocked(2306)) { if (GameMain.mainPlayer.package.size < 150) GameMain.mainPlayer.package.SetSize(150); }
            else if (GameMain.history.TechUnlocked(2305)) { if (GameMain.mainPlayer.package.size < 140) GameMain.mainPlayer.package.SetSize(140); }
            else if (GameMain.history.TechUnlocked(2304)) { if (GameMain.mainPlayer.package.size < 130) GameMain.mainPlayer.package.SetSize(130); }
            else if (GameMain.history.TechUnlocked(2303)) { if (GameMain.mainPlayer.package.size < 120) GameMain.mainPlayer.package.SetSize(120); }
            else if (GameMain.history.TechUnlocked(2302)) { if (GameMain.mainPlayer.package.size < 110) GameMain.mainPlayer.package.SetSize(110); }
            else if (GameMain.history.TechUnlocked(2301)) { if (GameMain.mainPlayer.package.size < 100) GameMain.mainPlayer.package.SetSize(100); }
            else { if (GameMain.mainPlayer.package.size < 90) GameMain.mainPlayer.package.SetSize(90); }

            if (GameMain.history.TechUnlocked(3510)) GameMain.history.remoteStationExtraStorage = 40000;
            else if (GameMain.history.TechUnlocked(3509)) GameMain.history.remoteStationExtraStorage = 15000;
        }

        private StorageCategory GetItemCategory(ItemProto itemProto)
        {
            if (itemProto == null) return StorageCategory.IntermediateProducts;
            if (itemProto.ID >= 6001 && itemProto.ID <= 6006) return StorageCategory.ScienceMatrices;
            if (itemProto.isAmmo || itemProto.isFighter) return StorageCategory.AmmunitionAndCombat;
            if (itemProto.CanBuild) return StorageCategory.BuildingsAndVehicles;
            if (IsVein(itemProto.ID)) return StorageCategory.RawResources;
            return StorageCategory.IntermediateProducts;
        }

        private (string text, Color color) GetProliferationStatus(int count, int inc, int itemId)
        {
            var tiers = new[] {
                new { Name = "Mk 3", MinPoints = 4.0, Color = new Color(0.6f, 0.7f, 1f) },
                new { Name = "Mk 2", MinPoints = 2.0, Color = new Color(0.6f, 1f, 0.6f) },
                new { Name = "Mk 1", MinPoints = 1.0, Color = new Color(1f, 0.75f, 0.5f) },
                new { Name = "None", MinPoints = 0.0, Color = Color.grey }
            };
            const double epsilon = 1e-5;

            if (count <= 0) return ("N/A", Color.grey);
            ItemProto itemProto = LDB.items.Select(itemId);
            if (itemProto == null || itemProto.CanBuild || itemProto.isFighter || (itemId >= ItemIds.ProliferatorMk1 && itemId <= ItemIds.ProliferatorMk3))
            {
                return ("N/A", Color.grey);
            }

            double pointsPerItem = (double)inc / count;

            for (int i = 0; i < tiers.Length; i++)
            {
                var currentTier = tiers[i];
                if (pointsPerItem >= currentTier.MinPoints - epsilon)
                {
                    if (i == 0) return (currentTier.Name, currentTier.Color);
                    var nextTier = tiers[i - 1];
                    double tierRange = nextTier.MinPoints - currentTier.MinPoints;
                    double progressInTier = pointsPerItem - currentTier.MinPoints;
                    double percentage = (tierRange > 0) ? (progressInTier / tierRange) * 100.0 : 100.0;
                    percentage = Math.Min(percentage, 100.0);
                    return ($"{currentTier.Name} ({percentage:F0}%)", currentTier.Color);
                }
            }
            return ("None", Color.grey);
        }

        private void RefreshStorageItemsForGUI()
        {
            lock (remoteStorageLock)
            {
                storageItemsForGUI = remoteStorage
                    .Where(pair =>
                    {
                        if (pair.Value.count <= 0) return false;
                        ItemProto itemProto = LDB.items.Select(pair.Key);
                        return itemProto != null && GetItemCategory(itemProto) == selectedStorageCategory;
                    })
                    .OrderBy(item => LDB.items.Select(item.Key)?.name ?? string.Empty)
                    .ToList();
            }

            foreach (var pair in storageItemsForGUI)
            {
                if (!limitInputStrings.ContainsKey(pair.Key))
                {
                    limitInputStrings[pair.Key] = pair.Value.limit.ToString();
                }
            }
        }

        #endregion

        #region IModCanSave Implementation
        public void Export(BinaryWriter w)
        {
            w.Write(SAVE_VERSION);
            lock (remoteStorageLock)
            {
                w.Write(remoteStorage.Count);
                foreach (var item in remoteStorage)
                {
                    w.Write(item.Key);
                    item.Value.Export(w);
                }
            }
        }

        public void Import(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version > SAVE_VERSION) return;

            lock (remoteStorageLock)
            {
                remoteStorage.Clear();
                int count = r.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int itemId = r.ReadInt32();
                    remoteStorage[itemId] = RemoteStorageItem.Import(r, version);
                }
            }
        }

        public void IntoOtherSave()
        {
            lock (remoteStorageLock)
            {
                remoteStorage.Clear();
            }
        }
        #endregion
    }
}
