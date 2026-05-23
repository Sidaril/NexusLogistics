using BepInEx;
using BepInEx.Configuration;
using crecheng.DSPModSave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using NexusLogistics.UI;

namespace NexusLogistics
{
    #region Helper Classes
    
    public static class ItemIds
    {
        public const int ProliferatorMk1 = 1141;
        public const int ProliferatorMk2 = 1142;
        public const int ProliferatorMk3 = 1143;

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

        public const int AntimatterFuelRod = 1803;
        public const int DeuteronFuelRod = 1802;
        public const int StrangeAnnihilationFuelRod = 1804;
        public const int CriticalPhoton = 1209;

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

        public const int AttackDrone = 5101;
        public const int Corvette = 5102;
        public const int Destroyer = 5103;

        public const int Water = 1000;
        public const int SulfuricAcid = 1116;
        public const int Deuterium = 1121;
    }

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
        public const string GUID = "com.Sidaril.dsp.NexusLogistics";
        public const string NAME = "NexusLogistics";
        public const string VERSION = "2.1.0";
        private const int SAVE_VERSION = 7;

        private StorageService _storageService;
        private FactoryProvider _factoryProvider;
        private LogisticsEngine _logisticsEngine;
        private UIService _uiService;

        // Configuration Entries
        private ConfigEntry<bool> autoSpray, costProliferator, infVeins, infItems, infSand, infBuildings, useStorege, autoCleanInventory;
        private ConfigEntry<bool> enableMod, autoReplenishPackage, autoReplenishTPPFuel, autoReplenishFPPFuel, infFleet, infAmmo;
        private ConfigEntry<KeyboardShortcut> hotKey;
        private ConfigEntry<UIService.ProliferatorSelection> proliferatorSelection;
        private ConfigEntry<int> fuelId;
        private ConfigEntry<int> starFuelId;

        // Refresh Timers
        private float _storageRefreshTimer = 0f;
        private const float StorageRefreshInterval = 0.25f;

        void Start()
        {
            BindConfigs();
            
            _storageService = new StorageService();
            _factoryProvider = new FactoryProvider();
            _logisticsEngine = new LogisticsEngine(_storageService, _factoryProvider, Logger);
            _uiService = new UIService(_storageService, _logisticsEngine);

            InitializeUIService();
            MyWindowManager.Enable(true);
            
            Logger.LogInfo("NexusLogistics initialized.");
        }

        private void BindConfigs()
        {
            hotKey = Config.Bind("Window Shortcut Key", "Key", new KeyboardShortcut(KeyCode.R, KeyCode.LeftShift));
            enableMod = Config.Bind("Configuration", "EnableMod", true, "Enable MOD");
            autoReplenishPackage = Config.Bind("Configuration", "autoReplenishPackage", true, "Automatically replenish items with filtering enabled in the backpack");
            autoCleanInventory = Config.Bind("Configuration", "AutoCleanInventory", true, "Automatically move items from main inventory to matching logistic slots.");
            autoSpray = Config.Bind("Configuration", "AutoSpray", true, "Automatic Spraying.");
            costProliferator = Config.Bind("Configuration", "CostProliferator", true, "Consume Proliferator.");
            proliferatorSelection = Config.Bind("Configuration", "ProliferatorSelection", UIService.ProliferatorSelection.All, "Which Proliferator tier to use.");
            infItems = Config.Bind("Configuration", "InfItems", false, "Infinite Items.");
            infVeins = Config.Bind("Configuration", "InfVeins", false, "Infinite Minerals.");
            infBuildings = Config.Bind("Configuration", "InfBuildings", false, "Infinite Buildings.");
            infSand = Config.Bind("Configuration", "InfSand", false, "Infinite Soil Pile.");
            useStorege = Config.Bind("Configuration", "useStorege", true, "Recover items from storage boxes and liquid tanks");
            autoReplenishTPPFuel = Config.Bind("Configuration", "autoReplenishTPPFuel", true, "Automatically replenish fuel for thermal power plants");
            autoReplenishFPPFuel = Config.Bind("Configuration", "autoReplenishFPPFuel", true, "Automatically replenish fuel for fusion power plants");
            fuelId = Config.Bind("Configuration", "fuelId", 0, "Thermal Power Plant Fuel ID");
            infAmmo = Config.Bind("Configuration", "InfAmmo", false, "Infinite Ammo.");
            infFleet = Config.Bind("Configuration", "infFleet", false, "Infinite Fleet.");
            starFuelId = Config.Bind("Configuration", "starFuelId", 0, "Artificial Star Fuel ID");
        }

        private void InitializeUIService()
        {
            _uiService.EnableMod = enableMod;
            _uiService.AutoReplenishPackage = autoReplenishPackage;
            _uiService.AutoCleanInventory = autoCleanInventory;
            _uiService.AutoSpray = autoSpray;
            _uiService.CostProliferator = costProliferator;
            _uiService.ProliferatorSelectionEntry = proliferatorSelection;
            _uiService.UseStorage = useStorege;
            _uiService.AutoReplenishTPPFuel = autoReplenishTPPFuel;
            _uiService.AutoReplenishFPPFuel = autoReplenishFPPFuel;
            _uiService.FuelId = fuelId;
            _uiService.StarFuelId = starFuelId;
            _uiService.InfBuildings = infBuildings;
            _uiService.InfVeins = infVeins;
            _uiService.InfItems = infItems;
            _uiService.InfSand = infSand;
            _uiService.InfAmmo = infAmmo;
            _uiService.InfFleet = infFleet;

            _uiService.Initialize();
        }

        void Update()
        {
            SyncConfigs();
            HandleHotkeys();
            RefreshUIData();
            _logisticsEngine.OnUpdate();
        }

        private void SyncConfigs()
        {
            _logisticsEngine.EnableMod = enableMod.Value;
            _logisticsEngine.InfSand = infSand.Value;
            _logisticsEngine.UseStorage = useStorege.Value;
            _logisticsEngine.AutoReplenishPackage = autoReplenishPackage.Value;
            _logisticsEngine.AutoCleanInventory = autoCleanInventory.Value;
            _logisticsEngine.AutoSpray = autoSpray.Value;
            _logisticsEngine.CostProliferator = costProliferator.Value;
            _logisticsEngine.ProliferatorSelectionValue = (int)proliferatorSelection.Value;
            _logisticsEngine.InfItems = infItems.Value;
            _logisticsEngine.InfVeins = infVeins.Value;
            _logisticsEngine.InfBuildings = infBuildings.Value;
            _logisticsEngine.InfAmmo = infAmmo.Value;
            _logisticsEngine.InfFleet = infFleet.Value;
            _logisticsEngine.AutoReplenishTPPFuel = autoReplenishTPPFuel.Value;
            _logisticsEngine.AutoReplenishFPPFuel = autoReplenishFPPFuel.Value;
            _logisticsEngine.FuelId = fuelId.Value;
            _logisticsEngine.StarFuelId = starFuelId.Value;
        }

        private void HandleHotkeys()
        {
            if (hotKey.Value.IsDown())
            {
                _uiService.ToggleWindow(0);
            }
        }

        private void RefreshUIData()
        {
            if (_uiService.IsWindowOpen())
            {
                _storageRefreshTimer += Time.deltaTime;
                if (_storageRefreshTimer >= StorageRefreshInterval)
                {
                    _storageRefreshTimer = 0f;
                    _uiService.StorageItemsForGUI = _storageService.GetAllItems();
                    _uiService.UpdateBottlenecks();
                }
            }
        }

        void OnDestroy()
        {
            MyWindowManager.Enable(false);
        }

        #region IModCanSave Implementation

        public void Export(BinaryWriter w)
        {
            w.Write(SAVE_VERSION);
            w.Write(_logisticsEngine.PlayerBalance);

            w.Write(_logisticsEngine.UnlockedItems.Count);
            foreach (int itemId in _logisticsEngine.UnlockedItems)
            {
                w.Write(itemId);
            }

            // Market Orders
            var orders = _storageService.GetAllMarketOrders().ToList();
            w.Write(orders.Count);
            foreach (var order in orders)
            {
                w.Write(order.Key);
                order.Value.Export(w);
            }

            // Trade Route Data
            w.Write(_logisticsEngine.TradeRoutesTier1);
            w.Write(_logisticsEngine.TradeRoutesTier2);
            w.Write(_logisticsEngine.TradeRoutesTier3);

            // Remote Storage
            var items = _storageService.GetAllItems().ToList();
            w.Write(items.Count);
            foreach (var item in items)
            {
                w.Write(item.Key);
                item.Value.Export(w);
            }
        }

        public void Import(BinaryReader r)
        {
            int version = r.ReadInt32();
            if (version > SAVE_VERSION) return;

            if (version >= 3)
                _logisticsEngine.PlayerBalance = r.ReadInt64();

            _logisticsEngine.UnlockedItems.Clear();
            if (version >= 4)
            {
                int unlockedCount = r.ReadInt32();
                for (int i = 0; i < unlockedCount; i++)
                {
                    _logisticsEngine.UnlockedItems.Add(r.ReadInt32());
                }
            }

            if (version >= 5)
            {
                int marketOrderCount = r.ReadInt32();
                for (int i = 0; i < marketOrderCount; i++)
                {
                    int itemId = r.ReadInt32();
                    var order = MarketOrder.Import(r, version);
                    _storageService.SetMarketOrder(itemId, order.BuyThreshold, order.SellThreshold);
                }
            }

            if (version >= 7)
            {
                _logisticsEngine.TradeRoutesTier1 = r.ReadInt32();
                _logisticsEngine.TradeRoutesTier2 = r.ReadInt32();
                _logisticsEngine.TradeRoutesTier3 = r.ReadInt32();
            }

            int storageCount = r.ReadInt32();
            for (int i = 0; i < storageCount; i++)
            {
                int itemId = r.ReadInt32();
                var item = RemoteStorageItem.Import(r, version);
                _storageService.AddOrUpdateStorage(itemId, item.count, item.inc, item.limit);
            }
        }

        public void IntoOtherSave()
        {
            _logisticsEngine.PlayerBalance = 0;
            _logisticsEngine.UnlockedItems.Clear();
            _storageService.ClearStorage();
            _storageService.ClearMarketOrders();
            _logisticsEngine.TradeRoutesTier1 = 0;
            _logisticsEngine.TradeRoutesTier2 = 0;
            _logisticsEngine.TradeRoutesTier3 = 0;
        }

        #endregion
    }
}
