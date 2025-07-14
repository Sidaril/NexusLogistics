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
    #region Data Structures and Constants

    /// <summary>
    /// Contains static definitions for item IDs to avoid magic numbers.
    /// </summary>
    public static class ItemIds
    {
        public const int ProliferatorMk1 = 1141;
        public const int ProliferatorMk2 = 1142;
        public const int ProliferatorMk3 = 1143;
        public const int AntimatterRod = 1804;
        public const int ArtificialSun = 1803; // Note: This is not an item ID, but used in logic
        public const int DeuteronFuelRod = 1802;
        public const int CriticalPhoton = 1209;
    }

    /// <summary>
    /// Enumeration for Proliferator selection in the config.
    /// </summary>
    public enum ProliferatorSelection
    {
        All,
        Mk1,
        Mk2,
        Mk3
    }

    /// <summary>
    /// Represents an item stored in the central remote storage.
    /// </summary>
    public class RemoteStorageItem
    {
        public int Count;
        public int Inc;
        public int Limit = 1000000;

        public void Export(BinaryWriter w)
        {
            w.Write(Count);
            w.Write(Inc);
            w.Write(Limit);
        }

        public static RemoteStorageItem Import(BinaryReader r, int saveVersion)
        {
            var item = new RemoteStorageItem
            {
                Count = r.ReadInt32(),
                Inc = r.ReadInt32(),
                Limit = 1000000 // Default for old saves
            };
            if (saveVersion >= 2)
            {
                item.Limit = r.ReadInt32();
            }
            return item;
        }
    }

    #endregion

    /// <summary>
    /// Manages the central, thread-safe remote storage for all items in the Nexus network.
    /// Handles adding, taking, and persisting items.
    /// </summary>
    public static class NexusStorage
    {
        public const int SAVE_VERSION = 2;
        private static readonly Dictionary<int, RemoteStorageItem> remoteStorage = new Dictionary<int, RemoteStorageItem>();
        private static readonly object remoteStorageLock = new object();

        public static ICollection<KeyValuePair<int, RemoteStorageItem>> GetStorageEntries()
        {
            lock (remoteStorageLock)
            {
                // Return a copy to prevent collection modification issues during enumeration.
                return new List<KeyValuePair<int, RemoteStorageItem>>(remoteStorage);
            }
        }

        public static bool TryGetItem(int itemId, out RemoteStorageItem item)
        {
            lock(remoteStorageLock)
            {
                return remoteStorage.TryGetValue(itemId, out item);
            }
        }
        
        public static void UpdateItemLimit(int itemId, int newLimit)
        {
            lock (remoteStorageLock)
            {
                if (remoteStorage.TryGetValue(itemId, out var item))
                {
                    item.Limit = newLimit;
                }
            }
        }

        public static int[] AddItem(int itemId, int count, int inc)
        {
            if (itemId <= 0 || count <= 0) return new[] { 0, 0 };

            lock (remoteStorageLock)
            {
                if (!remoteStorage.TryGetValue(itemId, out var storageItem))
                {
                    storageItem = new RemoteStorageItem();
                    remoteStorage[itemId] = storageItem;
                }

                int spaceAvailable = storageItem.Limit - storageItem.Count;
                if (spaceAvailable <= 0) return new[] { 0, 0 };

                int amountToAdd = Math.Min(count, spaceAvailable);
                int incToAdd = SplitInc(count, inc, amountToAdd);

                storageItem.Count += amountToAdd;
                storageItem.Inc += incToAdd;

                return new[] { amountToAdd, incToAdd };
            }
        }

        public static int[] TakeItem(int itemId, int count, bool isInfinite)
        {
            if (itemId <= 0 || count <= 0) return new[] { 0, 0 };

            ItemProto item = LDB.items.Select(itemId);
            if (isInfinite)
            {
                bool applyFreeSpray = NexusLogistics.AutoSpray.Value && !NexusLogistics.CostProliferator.Value && !item.CanBuild && !item.isFighter;
                int inc = applyFreeSpray ? count * 4 : 0;
                return new[] { count, inc };
            }

            lock (remoteStorageLock)
            {
                if (remoteStorage.TryGetValue(itemId, out var itemInStorage) && itemInStorage.Count > 0)
                {
                    int takenCount = Math.Min(count, itemInStorage.Count);
                    int takenInc = SplitInc(itemInStorage.Count, itemInStorage.Inc, takenCount);

                    itemInStorage.Count -= takenCount;
                    itemInStorage.Inc -= takenInc;

                    return new[] { takenCount, takenInc };
                }
            }
            return new[] { 0, 0 };
        }

        private static int SplitInc(int originalCount, int originalInc, int newCount)
        {
            if (originalCount <= 0 || originalInc <= 0) return 0;
            double incPerCount = (double)originalInc / originalCount;
            return (int)Math.Round(incPerCount * newCount);
        }

        #region Save/Load Logic
        public static void Export(BinaryWriter w)
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

        public static void Import(BinaryReader r)
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

        public static void Clear()
        {
            lock (remoteStorageLock)
            {
                remoteStorage.Clear();
            }
        }
        #endregion
    }


    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("crecheng.DSPModSave")]
    public class NexusLogistics : BaseUnityPlugin, IModCanSave
    {
        public const string GUID = "com.Sidaril.dsp.NexusLogistics";
        public const string NAME = "NexusLogistics";
        public const string VERSION = "1.2.0"; 

        #region Config Entries
        public static ConfigEntry<bool> EnableMod { get; private set; }
        public static ConfigEntry<bool> AutoReplenishPackage { get; private set; }
        public static ConfigEntry<bool> AutoReplenishTPPFuel { get; private set; }
        public static ConfigEntry<bool> UseStorage { get; private set; }
        public static ConfigEntry<int> FuelId { get; private set; }
        public static ConfigEntry<bool> AutoSpray { get; private set; }
        public static ConfigEntry<bool> CostProliferator { get; private set; }
        public static ConfigEntry<ProliferatorSelection> ProliferatorTier { get; private set; }
        public static ConfigEntry<bool> InfItems { get; private set; }
        public static ConfigEntry<bool> InfVeins { get; private set; }
        public static ConfigEntry<bool> InfBuildings { get; private set; }
        public static ConfigEntry<bool> InfSand { get; private set; }
        public static ConfigEntry<bool> InfAmmo { get; private set; }
        public static ConfigEntry<bool> InfFleet { get; private set; }
        private ConfigEntry<KeyboardShortcut> _hotKey;
        private ConfigEntry<KeyboardShortcut> _storageHotKey;
        #endregion

        private NexusGui _gui;
        private NexusProcessor _processor;
        private Thread _logicThread;
        private float _checkTechTimer; // Timer for throttling tech checks

        void Start()
        {
            Logger.LogInfo("NexusLogistics is starting up...");
            
            BindConfigs();

            _gui = new NexusGui(FuelId);
            _processor = new NexusProcessor(Logger);

            _logicThread = new Thread(() =>
            {
                while (true)
                {
                    if (!EnableMod.Value || GameMain.instance == null || GameMain.instance.isMenuDemo || GameMain.isPaused || !GameMain.isRunning || GameMain.data == null)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }

                    DateTime startTime = DateTime.Now;
                    try
                    {
                        _processor.RunAllTasks();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("A critical error occurred in the NexusLogistics logic thread.");
                        Logger.LogError(ex.ToString());
                    }
                    finally
                    {
                        double cost = (DateTime.Now - startTime).TotalMilliseconds;
                        if (cost < 50)
                        {
                            Thread.Sleep(50 - (int)cost);
                        }
                    }
                }
            })
            {
                IsBackground = true,
                Name = "NexusLogistics.Logic"
            };
            _logicThread.Start();
        }

        private void BindConfigs()
        {
            _hotKey = Config.Bind("Hotkeys", "ToggleMainWindow", new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl), "Hotkey to toggle the main settings window.");
            _storageHotKey = Config.Bind("Hotkeys", "ToggleStorageWindow", new KeyboardShortcut(KeyCode.K, KeyCode.LeftControl), "Hotkey to toggle the remote storage overview window.");
            EnableMod = Config.Bind("Behavior", "EnableMod", true, "Master switch to enable or disable the mod.");
            AutoReplenishPackage = Config.Bind("Behavior", "AutoReplenishPackage", true, "Automatically replenish items with filtering enabled in the backpack.");
            UseStorage = Config.Bind("Behavior", "UseStorage", true, "Recover items from standard storage boxes and liquid tanks into the Nexus network.");
            AutoReplenishTPPFuel = Config.Bind("Behavior", "AutoReplenishTPPFuel", true, "Automatically replenish fuel for thermal power plants.");
            FuelId = Config.Bind("Behavior", "FuelId", 0, "The specific Item ID for fuel to use in Thermal Power Plants. 0 is automatic.");
            AutoSpray = Config.Bind("Proliferation", "AutoSpray", true, "Automatically spray items added to the Nexus network.");
            CostProliferator = Config.Bind("Proliferation", "CostProliferator", true, "If Auto-Spray is enabled, this determines if proliferator points are consumed from the network.");
            ProliferatorTier = Config.Bind<ProliferatorSelection>("Proliferation", "ProliferatorTier", ProliferatorSelection.All, new ConfigDescription("Which Proliferator tier to use for automatic spraying."));
            InfItems = Config.Bind("Cheats", "InfiniteItems", false, "All non-building/mineral items are infinite. Disables achievements.");
            InfVeins = Config.Bind("Cheats", "InfiniteMinerals", false, "All mined resources are infinite.");
            InfBuildings = Config.Bind("Cheats", "InfiniteBuildings", false, "All buildable items are infinite.");
            InfSand = Config.Bind("Cheats", "InfiniteSoilPile", false, "Soil pile is fixed at 1,000,000,000.");
            InfAmmo = Config.Bind("Cheats", "InfiniteAmmo", false, "All ammunition types are infinite.");
            InfFleet = Config.Bind("Cheats", "InfiniteFleet", false, "All drone and ship types are infinite.");
        }

        void Update()
        {
            if (_hotKey.Value.IsDown()) _gui.ToggleMainWindow();
            if (_storageHotKey.Value.IsDown()) _gui.ToggleStorageWindow();
            
            _gui.Update();

            // FIX: Run tech checks on the main thread, throttled to once per second.
            _checkTechTimer += Time.deltaTime;
            if (_checkTechTimer > 1.0f)
            {
                _checkTechTimer = 0f;
                if (EnableMod.Value && GameMain.instance != null && GameMain.isRunning && GameMain.data != null)
                {
                    _processor.CheckTech();
                }
            }
        }

        void OnGUI()
        {
            _gui.OnGUI();
        }

        #region IModCanSave Implementation
        public void Export(BinaryWriter w) => NexusStorage.Export(w);
        public void Import(BinaryReader r) => NexusStorage.Import(r);
        public void IntoOtherSave() => NexusStorage.Clear();
        #endregion
    }
}
