using BepInEx;
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
        public const string VERSION = "1.9.0";

        private const int SAVE_VERSION = 7;

        // Player Data
        private long playerBalance = 0;
        private readonly HashSet<int> unlockedItems = new HashSet<int>();

        private enum ProliferatorSelection { All, Mk1, Mk2, Mk3 }
        private enum StorageCategory { Dashboard, Storage, Market, Contracts }

        // Market Data
        private readonly Dictionary<int, long> itemPrices = new Dictionary<int, long>();
        private readonly Dictionary<int, string> marketQuantityInputs = new Dictionary<int, string>();
        private List<ItemProto> allItemsForMarket = new List<ItemProto>();
        private int marketSubTab = 0; // 0 for Main, 1 for Orders

        // Market Order Data
        private readonly Dictionary<int, MarketOrder> marketOrders = new Dictionary<int, MarketOrder>();
        private readonly Dictionary<int, string> buyThresholdInputs = new Dictionary<int, string>();
        private readonly Dictionary<int, string> sellThresholdInputs = new Dictionary<int, string>();

        // Automated Trade Route Data
        private int tradeRoutesTier1 = 0;
        private int tradeRoutesTier2 = 0;
        private int tradeRoutesTier3 = 0;

        // Remote Storage Data
        private readonly Dictionary<int, RemoteStorageItem> remoteStorage = new Dictionary<int, RemoteStorageItem>();
        private readonly object remoteStorageLock = new object();

        // Configuration Entries
        private ConfigEntry<bool> autoSpray, costProliferator, infVeins, infItems, infSand, infBuildings, useStorege, autoCleanInventory;
        private ConfigEntry<bool> enableMod, autoReplenishPackage, autoReplenishTPPFuel, autoReplenishFPPFuel, infFleet, infAmmo;
        private ConfigEntry<KeyboardShortcut> hotKey, storageHotKey;
        private ConfigEntry<ProliferatorSelection> proliferatorSelection;
        private ConfigEntry<int> fuelId;
        private ConfigEntry<int> starFuelId;

        // Proliferation and Item Data
        private readonly List<(int, int)> proliferators = new List<(int, int)>();
        private readonly Dictionary<int, int> incPool = new Dictionary<int, int>();
        private Dictionary<EAmmoType, List<int>> ammos = new Dictionary<EAmmoType, List<int>>();

        // GUI State
        private GUIStyle windowStyle, labelStyle, buttonStyle, toggleStyle, toolbarStyle, textFieldStyle, scrollViewStyle;
        private bool guiStylesInitialized = false;
        private bool showGUI = false;
        private bool showStorageGUI = false;
        private Rect windowRect = new Rect(700, 250, 600, 500);
        private Rect storageWindowRect = new Rect(100, 250, 900, 500);
        private Vector2 storageScrollPosition, mainPanelScrollPosition;
        private int selectedPanel = 0;
                private readonly Dictionary<int, string> fuelOptions = new Dictionary<int, string>();
                private readonly Dictionary<int, string> starFuelOptions = new Dictionary<int, string>();
                private int selectedFuelIndex;
                private int selectedStarFuelIndex;
        
                private enum ItemCategory { RawResources, IntermediateProducts, BuildingsAndVehicles, AmmunitionAndCombat, ScienceMatrices }
        private StorageCategory selectedStorageCategory = StorageCategory.Dashboard;
        private ItemCategory selectedItemCategory = ItemCategory.RawResources;
        private readonly Dictionary<int, string> limitInputStrings = new Dictionary<int, string>();
        private readonly List<int> thermalFuelsByPriority = new List<int>
        {
            ItemIds.HydrogenFuelRod,
            ItemIds.EnergyShard,
            ItemIds.CombustionUnit,
            ItemIds.FireIce,
            ItemIds.Graphite,
            ItemIds.RefinedOil,
            ItemIds.Hydrogen,
            ItemIds.Coal,
            ItemIds.PlantFuel,
            ItemIds.Wood
        };
        private List<KeyValuePair<int, RemoteStorageItem>> storageItemsForGUI = new List<KeyValuePair<int, RemoteStorageItem>>();
        private List<ItemProto> marketItemsForGUI = new List<ItemProto>();
        private float marketGUIRefreshTimer = 0f;
        private const float MarketGUIRefreshInterval = 0.25f; // 4 times per second
        private ItemCategory lastMarketItemCategory = (ItemCategory)(-1); // Invalid category to force initial refresh

        private static readonly (string Name, double MinPoints, Color Color)[] proliferationTiers = {
            ("Mk 3", 4.0, new Color(0.6f, 0.7f, 1f)),
            ("Mk 2", 2.0, new Color(0.6f, 1f, 0.6f)),
            ("Mk 1", 1.0, new Color(1f, 0.75f, 0.5f)),
            ("None", 0.0, Color.grey)
        };

        // Dashboard Data Cache
        private struct BottleneckInfo
        {
            public int ItemId;
            public int DeficitPerMinute;
            public int CurrentStock;
        }
        private List<BottleneckInfo> cachedBottlenecks = new List<BottleneckInfo>();
        private float dashboardRefreshTimer = 0f;
        private const float DashboardRefreshInterval = 1.0f; // 1 second
        private float storageGUIRefreshTimer = 0f;
        private const float StorageGUIRefreshInterval = 0.25f; // 4 times per second
        private Dictionary<int, int> bottleneckCounters = new Dictionary<int, int>();
        private const int BottleneckPersistenceThreshold = 3; // 3 seconds

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

        private class MarketOrder
        {
            public int BuyThreshold; // Buy if item count is BELOW this
            public int SellThreshold; // Sell if item count is ABOVE this

            public void Export(BinaryWriter w)
            {
                w.Write(BuyThreshold);
                w.Write(SellThreshold);
            }

            public static MarketOrder Import(BinaryReader r, int saveVersion)
            {
                var order = new MarketOrder
                {
                    BuyThreshold = r.ReadInt32(),
                    SellThreshold = r.ReadInt32(),
                };

                // In saves older than version 6 (specifically, version 5), there was a SellAmount integer here.
                // We need to read it to advance the stream, but we don't use it.
                if (saveVersion == 5)
                {
                    r.ReadInt32(); // Read and discard the old SellAmount
                }

                return order;
            }
        }
        #endregion

        #region Statistics Tracking

        private class ItemStats
        {
            public readonly Queue<DataPoint> AddedHistory = new Queue<DataPoint>();
            public readonly Queue<DataPoint> TakenHistory = new Queue<DataPoint>();
            public int TotalAddedInWindow = 0;
            public int TotalTakenInWindow = 0;
        }

        private struct DataPoint
        {
            public DateTime Timestamp;
            public int Amount;
        }

        private readonly Dictionary<int, ItemStats> itemStats = new Dictionary<int, ItemStats>();
        private readonly object itemStatsLock = new object();
        private const int HistoryMinutes = 30;

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

        /// <summary>
        /// Creates and configures all the custom GUIStyles for the mod's windows.
        /// </summary>
        private Texture2D borderTexture;

        private void InitializeGUIStyles()
        {
            // Load Fonts from Resources
            Font boldFont = Resources.Load<Font>("fonts/Vipnagorgialla Bd");
            Font regularFont = Resources.Load<Font>("fonts/Vipnagorgialla Rg");

            // Define Colors
            Color backgroundColor = new Color(0.05f, 0.1f, 0.15f, 0.85f);
            Color borderColor = new Color(0.3f, 0.8f, 1.0f, 0.5f);
            Color textColor = new Color(0.8f, 0.9f, 1.0f, 1.0f);
            Color highlightColor = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            // Create Texture for Window Background
            Texture2D windowBackground = new Texture2D(1, 1);
            windowBackground.SetPixel(0, 0, backgroundColor);
            windowBackground.Apply();

            // Create Texture for Border
            borderTexture = new Texture2D(1, 1);
            borderTexture.SetPixel(0, 0, borderColor);
            borderTexture.Apply();

            // Window Style
            windowStyle = new GUIStyle(GUI.skin.window);
            windowStyle.font = boldFont;
            windowStyle.fontSize = 16;
            windowStyle.normal.background = windowBackground;
            windowStyle.normal.textColor = highlightColor;
            windowStyle.onNormal.background = windowBackground;
            windowStyle.border = new RectOffset(1, 1, 1, 1);
            windowStyle.padding = new RectOffset(10, 10, 25, 10);

            // Label Style
            labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.font = regularFont;
            labelStyle.fontSize = 14;
            labelStyle.normal.textColor = textColor;

            // Button Style
            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.font = regularFont;
            buttonStyle.fontSize = 14;
            buttonStyle.normal.textColor = textColor;
            buttonStyle.hover.textColor = highlightColor;

            // Toggle Style
            toggleStyle = new GUIStyle(GUI.skin.toggle);
            toggleStyle.font = regularFont;
            toggleStyle.fontSize = 14;
            toggleStyle.normal.textColor = textColor;
            toggleStyle.onNormal.textColor = highlightColor;
            toggleStyle.hover.textColor = highlightColor;

            // Toolbar Style
            toolbarStyle = new GUIStyle(GUI.skin.button);
            toolbarStyle.font = regularFont;
            toolbarStyle.fontSize = 14;
            toolbarStyle.normal.textColor = textColor;
            toolbarStyle.hover.textColor = highlightColor;
            toolbarStyle.active.textColor = highlightColor;
            toolbarStyle.onNormal.textColor = highlightColor;

            // TextField Style
            textFieldStyle = new GUIStyle(GUI.skin.textField);
            textFieldStyle.font = regularFont;
            textFieldStyle.fontSize = 14;
            textFieldStyle.normal.textColor = textColor;

            // ScrollView Style
            scrollViewStyle = new GUIStyle(GUI.skin.scrollView);
        }

        void Update()
        {
            // Periodically update the item list if the storage window is open.
            if (showStorageGUI)
            {
                storageGUIRefreshTimer += Time.deltaTime;
                if (storageGUIRefreshTimer >= StorageGUIRefreshInterval)
                {
                    storageGUIRefreshTimer = 0f;
                    RefreshStorageItemsForGUI();
                }

                if (selectedStorageCategory == StorageCategory.Market)
                {
                    marketGUIRefreshTimer += Time.deltaTime;
                    if (marketGUIRefreshTimer >= MarketGUIRefreshInterval)
                    {
                        marketGUIRefreshTimer = 0f;
                        RefreshMarketItemsForGUI();
                    }
                }
            }

            // Refresh dashboard data periodically
            dashboardRefreshTimer += Time.deltaTime;
            if (dashboardRefreshTimer >= DashboardRefreshInterval)
            {
                dashboardRefreshTimer = 0f;
                var currentBottlenecks = GetPotentialBottlenecks();
                var bottleneckIds = new HashSet<int>(currentBottlenecks.Select(b => b.ItemId));

                foreach (var item in currentBottlenecks)
                {
                    if (!bottleneckCounters.ContainsKey(item.ItemId))
                    {
                        bottleneckCounters[item.ItemId] = 0;
                    }
                    bottleneckCounters[item.ItemId]++;
                }

                var itemsToRemove = bottleneckCounters.Keys.Where(key => !bottleneckIds.Contains(key)).ToList();
                foreach (var key in itemsToRemove)
                {
                    bottleneckCounters.Remove(key);
                }

                cachedBottlenecks = currentBottlenecks
                    .Where(b => bottleneckCounters.ContainsKey(b.ItemId) && bottleneckCounters[b.ItemId] >= BottleneckPersistenceThreshold)
                    .ToList();
            }
        }

        void OnGUI()
        {
            if (!guiStylesInitialized)
            {
                InitializeGUIStyles();
                guiStylesInitialized = true;
            }

            // Single Source of Truth Input Handler: Process hotkeys here and only here.
            if (Event.current.type == EventType.KeyDown)
            {
                // Check for main window hotkey
                if (Event.current.keyCode == hotKey.Value.MainKey && Event.current.keyCode != KeyCode.None)
                {
                    bool wantsCtrl = false, wantsShift = false, wantsAlt = false;
                    foreach (var m in hotKey.Value.Modifiers)
                    {
                        if (m == KeyCode.LeftControl || m == KeyCode.RightControl) wantsCtrl = true;
                        if (m == KeyCode.LeftShift || m == KeyCode.RightShift) wantsShift = true;
                        if (m == KeyCode.LeftAlt || m == KeyCode.RightAlt) wantsAlt = true;
                    }
                    bool ctrl = (Event.current.modifiers & EventModifiers.Control) != 0;
                    bool shift = (Event.current.modifiers & EventModifiers.Shift) != 0;
                    bool alt = (Event.current.modifiers & EventModifiers.Alt) != 0;

                    if (wantsCtrl == ctrl && wantsShift == shift && wantsAlt == alt)
                    {
                        showGUI = !showGUI;
                        Event.current.Use(); // Consume the event to prevent double processing
                    }
                }

                // Check for storage window hotkey
                if (Event.current.keyCode == storageHotKey.Value.MainKey && Event.current.keyCode != KeyCode.None)
                {
                    bool wantsCtrl = false, wantsShift = false, wantsAlt = false;
                    foreach (var m in storageHotKey.Value.Modifiers)
                    {
                        if (m == KeyCode.LeftControl || m == KeyCode.RightControl) wantsCtrl = true;
                        if (m == KeyCode.LeftShift || m == KeyCode.RightShift) wantsShift = true;
                        if (m == KeyCode.LeftAlt || m == KeyCode.RightAlt) wantsAlt = true;
                    }
                    bool ctrl = (Event.current.modifiers & EventModifiers.Control) != 0;
                    bool shift = (Event.current.modifiers & EventModifiers.Shift) != 0;
                    bool alt = (Event.current.modifiers & EventModifiers.Alt) != 0;

                    if (wantsCtrl == ctrl && wantsShift == shift && wantsAlt == alt)
                    {
                        showStorageGUI = !showStorageGUI;
                        if (showStorageGUI)
                        {
                            RefreshStorageItemsForGUI();
                        }
                        Event.current.Use(); // Consume the event to prevent double processing
                    }
                }
            }

            if (showGUI)
            {
                windowRect = GUI.Window(0, windowRect, WindowFunction, $"{NAME} {VERSION}", windowStyle);
                DrawWindowBorder(windowRect);
            }
            if (showStorageGUI)
            {
                storageWindowRect = GUI.Window(1, storageWindowRect, StorageWindowFunction, "Logistics", windowStyle);
                DrawWindowBorder(storageWindowRect);
            }

            // Prevent click-through to the game world when GUI is active.
            if ((showGUI && windowRect.Contains(Event.current.mousePosition)) || (showStorageGUI && storageWindowRect.Contains(Event.current.mousePosition)))
            {
                Input.ResetInputAxes();
            }
        }

        /// <summary>
        /// Draws a border around the given rectangle using the borderTexture.
        /// </summary>
        private void DrawWindowBorder(Rect rect)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), borderTexture); // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), borderTexture); // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), borderTexture); // Left
            GUI.DrawTexture(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), borderTexture); // Right
        }

        #endregion

        #region Market Transaction Methods

        private void BuyItem(int itemId, long price)
        {
            if (!marketQuantityInputs.TryGetValue(itemId, out string input) || !int.TryParse(input, out int quantity) || quantity <= 0)
            {
                return; // Invalid quantity
            }

            long totalCost = price * quantity;
            int affordableQuantity = quantity;

            if (totalCost > playerBalance)
            {
                affordableQuantity = (int)(playerBalance / price);
            }

            if (affordableQuantity <= 0)
            {
                return; // Can't afford any
            }

            long finalCost = price * affordableQuantity;
            playerBalance -= finalCost;
            AddItem(itemId, affordableQuantity, 0, true); // Bypass storage limit for purchases
        }

        private void SellItem(int itemId, long price)
        {
            if (!marketQuantityInputs.TryGetValue(itemId, out string input) || !int.TryParse(input, out int quantity) || quantity <= 0)
            {
                return; // Invalid quantity
            }

            int[] takenItems = TakeItem(itemId, quantity);
            int soldQuantity = takenItems[0];

            if (soldQuantity > 0)
            {
                playerBalance += price * soldQuantity;
            }
        }

        private long GetSellPrice(int itemId)
        {
            if (itemPrices.TryGetValue(itemId, out long basePrice))
            {
                return basePrice / 5;
            }
            return 0;
        }

        private RecipeProto GetRecipe(int itemId)
        {
            return LDB.recipes.dataArray.FirstOrDefault(r => r.Results.Length > 0 && r.Results[0] == itemId);
        }


        private Dictionary<int, long> GenerateDefaultPrices()
        {
            var prices = new Dictionary<int, long>
            {
                // Raw Materials - these are the starting point of our graph
                { 1001, 10 }, { 1002, 10 }, { 1003, 20 }, { 1004, 20 }, { 1005, 30 }, { 1006, 15 },
                { 1007, 25 }, { 1011, 100 }, { 1012, 100 }, { 1013, 100 }, { 1014, 100 }, { 1015, 150 },
                { 1016, 200 }, { 1017, 250 }, { 1030, 5 }, { 1031, 10 }, { 1120, 30 }, { 1121, 60 },
                { 1122, 1000 },
            };

            var recipes = LDB.recipes.dataArray.Where(r => r?.Results != null && r.Results.Length > 0).ToList();
            var recipeDict = recipes
                .GroupBy(r => r.Results[0])
                .ToDictionary(g => g.Key, g => g.First());

            var itemInDegree = new Dictionary<int, int>();
            var recipeDependents = new Dictionary<int, List<int>>();

            // Build the dependency graph and in-degrees
            foreach (var recipe in recipes)
            {
                int resultItemId = recipe.Results[0];
                if (prices.ContainsKey(resultItemId)) continue;

                int degree = 0;
                foreach (var ingredientId in recipe.Items)
                {
                    if (!prices.ContainsKey(ingredientId))
                    {
                        degree++;
                        if (!recipeDependents.ContainsKey(ingredientId))
                        {
                            recipeDependents[ingredientId] = new List<int>();
                        }
                        recipeDependents[ingredientId].Add(resultItemId);
                    }
                }
                itemInDegree[resultItemId] = degree;
            }

            // Initialize the queue with items that have all their ingredients priced
            var queue = new Queue<int>(itemInDegree.Where(p => p.Value == 0).Select(p => p.Key));

            // Process the queue (topological sort)
            while (queue.Count > 0)
            {
                int itemIdToPrice = queue.Dequeue();
                if (!recipeDict.TryGetValue(itemIdToPrice, out var recipe) || prices.ContainsKey(itemIdToPrice))
                {
                    continue;
                }

                long currentPrice = 0;
                for (int j = 0; j < recipe.Items.Length; j++)
                {
                    if (prices.TryGetValue(recipe.Items[j], out long ingredientPrice))
                    {
                        currentPrice += ingredientPrice * recipe.ItemCounts[j];
                    }
                    else
                    {
                        // This should not happen in a correct topological sort
                        currentPrice = 0;
                        break;
                    }
                }

                if (currentPrice > 0)
                {
                    double premium = recipe.Items.Length <= 2 ? 0.6 : (recipe.Items.Length <= 4 ? 0.8 : 1.0);
                    prices[itemIdToPrice] = currentPrice + (long)(currentPrice * premium);

                    // Decrement the in-degree of dependent items
                    if (recipeDependents.TryGetValue(itemIdToPrice, out var dependents))
                    {
                        foreach (var dependentId in dependents)
                        {
                            if (itemInDegree.ContainsKey(dependentId))
                            {
                                itemInDegree[dependentId]--;
                                if (itemInDegree[dependentId] == 0)
                                {
                                    queue.Enqueue(dependentId);
                                }
                            }
                        }
                    }
                }
            }

            return prices;
        }

        #endregion

        #region Initialization

        /// <summary>
        /// Binds all configuration settings from the config file.
        /// </summary>
        private void BindConfigs()
        {
            hotKey = Config.Bind("Window Shortcut Key", "Key", new KeyboardShortcut(KeyCode.R, KeyCode.LeftControl));
            storageHotKey = Config.Bind("Window Shortcut Key", "Storage_Key", new KeyboardShortcut(KeyCode.R, KeyCode.LeftShift));
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
            autoReplenishFPPFuel = Config.Bind("Configuration", "autoReplenishFPPFuel", true, "Automatically replenish fuel for fusion power plants");
            fuelId = Config.Bind("Configuration", "fuelId", 0, "Thermal Power Plant Fuel ID\n0: Auto-select...");
            infAmmo = Config.Bind("Configuration", "InfAmmo", false, "Infinite Ammo. Ammo in the logistics backpack and interstellar logistics stations have infinite quantity");
            infFleet = Config.Bind("Configuration", "infFleet", false, "Infinite Fleet. Drones and warships in the logistics backpack and interstellar logistics stations have infinite quantity");
            starFuelId = Config.Bind("Configuration", "starFuelId", 0, "Artificial Star Fuel ID\n0: Auto-select...");
        }

        /// <summary>
        /// Initializes data structures and other resources needed by the mod.
        /// </summary>
        private void InitializeData()
        {
            allItemsForMarket = LDB.items.dataArray.Where(p => p != null && p.ID > 0).ToList();
            LoadItemPrices();

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
            fuelOptions.Add(ItemIds.PlantFuel, "PlantFuel");
            selectedFuelIndex = fuelOptions.Keys.ToList().FindIndex(id => id == fuelId.Value);

            starFuelOptions.Add(0, "Auto");
            starFuelOptions.Add(ItemIds.StrangeAnnihilationFuelRod, "Strange Rod");
            starFuelOptions.Add(ItemIds.AntimatterFuelRod, "Antimatter Rod");
            selectedStarFuelIndex = starFuelOptions.Keys.ToList().FindIndex(id => id == starFuelId.Value);

            proliferators.Add((ItemIds.ProliferatorMk3, 4));
            proliferators.Add((ItemIds.ProliferatorMk2, 2));
            proliferators.Add((ItemIds.ProliferatorMk1, 1));

            incPool.Add(ItemIds.ProliferatorMk1, 0);
            incPool.Add(ItemIds.ProliferatorMk2, 0);
            incPool.Add(ItemIds.ProliferatorMk3, 0);

            ammos.Add(EAmmoType.Bullet, new List<int> { ItemIds.GravitonBullet, ItemIds.SuperalloyBullet, ItemIds.TitaniumBullet });
            ammos.Add(EAmmoType.Missile, new List<int> { ItemIds.PrecisionDroney, ItemIds.GravitonMissileSet, ItemIds.SupersonicMissileSet });
            ammos.Add(EAmmoType.Cannon, new List<int> { ItemIds.CrystalShellSet, ItemIds.HighExplosiveShellSet, ItemIds.ShellSet });
            ammos.Add(EAmmoType.Plasma, new List<int> { ItemIds.AntimatterCapsule, ItemIds.PlasmaCapsule });
            ammos.Add(EAmmoType.EMCapsule, new List<int> { ItemIds.SuppressionCapsule, ItemIds.JammingCapsule });
        }

        /// <summary>
        /// Loads item prices from a custom configuration file, and adds any missing items from the default list.
        /// </summary>
        private void LoadItemPrices()
        {
            string configPath = Path.Combine(Paths.ConfigPath, "nexus-logistics-item-prices.cfg");
            var defaultPrices = GenerateDefaultPrices();

            // Load existing prices from the config file
            if (File.Exists(configPath))
            {
                foreach (string line in File.ReadAllLines(configPath))
                {
                    if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;

                    string[] parts = line.Split('#')[0].Split('=');
                    if (parts.Length == 2)
                    {
                        if (int.TryParse(parts[0].Trim(), out int itemId) && long.TryParse(parts[1].Trim(), out long price))
                        {
                            if (!itemPrices.ContainsKey(itemId))
                            {
                                itemPrices.Add(itemId, price);
                            }
                            else
                            {
                                itemPrices[itemId] = price; // Overwrite with user's value
                            }
                        }
                    }
                }
            }

            // Add missing default prices
            bool needsSave = !File.Exists(configPath);
            foreach (var defaultPrice in defaultPrices)
            {
                if (!itemPrices.ContainsKey(defaultPrice.Key))
                {
                    itemPrices.Add(defaultPrice.Key, defaultPrice.Value);
                    needsSave = true;
                }
            }

            // Save the updated file if new items were added or if the file didn't exist
            if (needsSave)
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(configPath, false)) // Overwrite the file
                    {
                        sw.WriteLine("# This file contains the base prices for items in the market.");
                        sw.WriteLine("# Format: ItemID = Price");
                        sw.WriteLine("# You can modify these values. The mod will update this file with new items, but will preserve your custom prices.");

                        // Write all items (user-modified and new defaults) to the file, sorted by ID
                        foreach (var item in allItemsForMarket.OrderBy(i => i.ID))
                        {
                            if (itemPrices.TryGetValue(item.ID, out long price))
                            {
                                sw.WriteLine($"{item.ID} = {price} # {item.name}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to save item prices configuration: {ex}");
                }
            }
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
                        ProcessMarketOrders().Wait(); // Run this synchronously after all other item movements
                        ProcessTradeRoutes(); // Run this synchronously at the end of the tick
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
            selectedPanel = GUILayout.Toolbar(selectedPanel, panels, toolbarStyle);
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
            string[] categories = { "Dashboard", "Storage", "Market", "Contracts" };
            var newCategory = (StorageCategory)GUILayout.Toolbar((int)selectedStorageCategory, categories, toolbarStyle);

            if (newCategory != selectedStorageCategory)
            {
                selectedStorageCategory = newCategory;
                lastMarketItemCategory = (ItemCategory)(-1); // Invalidate market cache
                // Reset sub-tabs when changing main tabs for a clean state
                if (selectedStorageCategory == StorageCategory.Market)
                {
                    selectedItemCategory = ItemCategory.RawResources;
                }
                else if (selectedStorageCategory == StorageCategory.Storage)
                {
                    selectedItemCategory = ItemCategory.RawResources;
                }
            }

            if (selectedStorageCategory == StorageCategory.Dashboard)
            {
                DashboardPanel();
            }
            else if (selectedStorageCategory == StorageCategory.Market)
            {
                MarketPanel();
            }
            else if (selectedStorageCategory == StorageCategory.Contracts)
            {
                ContractsPanel();
            }
            else if (selectedStorageCategory == StorageCategory.Storage)
            {
                // Item Category Sub-Tabs
                string[] itemCategories = { "Raw", "Intermeds", "Buildings", "Combat", "Science" };
                selectedItemCategory = (ItemCategory)GUILayout.Toolbar((int)selectedItemCategory, itemCategories, toolbarStyle);

                GUILayout.BeginVertical();
                GUILayout.Space(5);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Item Name", labelStyle, GUILayout.Width(150));
                GUILayout.Label("Count", labelStyle, GUILayout.Width(100));
                GUILayout.Label("Proliferation", labelStyle, GUILayout.Width(100));
                GUILayout.Label("Limit", labelStyle, GUILayout.Width(100));
                GUILayout.EndHorizontal();

                storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);

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
                        GUILayout.Label(itemName, labelStyle, GUILayout.Width(150));
                        GUILayout.Label(item.count.ToString("N0"), labelStyle, GUILayout.Width(100));

                        var (prolifText, prolifColor) = GetProliferationStatus(item.count, item.inc, itemId);
                        GUI.contentColor = prolifColor;
                        GUILayout.Label(prolifText, labelStyle, GUILayout.Width(100));
                        GUI.contentColor = originalContentColor;

                        string currentInput = limitInputStrings[itemId];
                        string newInput = GUILayout.TextField(currentInput, textFieldStyle, GUILayout.Width(100));

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
            }
            GUI.DragWindow();
        }

        void MarketPanel()
        {
            // Sub-tab toolbar for Main/Orders
            string[] marketSubTabs = { "Main", "Orders" };
            marketSubTab = GUILayout.Toolbar(marketSubTab, marketSubTabs, toolbarStyle);
            GUILayout.Space(5);

            // Item category sub-tabs
            string[] itemCategories = { "Raw", "Intermeds", "Buildings", "Combat", "Science" };
            var newSelectedItemCategory = (ItemCategory)GUILayout.Toolbar((int)selectedItemCategory, itemCategories, toolbarStyle);
            if (newSelectedItemCategory != selectedItemCategory)
            {
                selectedItemCategory = newSelectedItemCategory;
                lastMarketItemCategory = (ItemCategory)(-1); // Invalidate cache
            }
            GUILayout.Space(10);

            // Balance display moved here
            GUILayout.Label($"Balance: ${playerBalance:N0}", labelStyle);
            GUILayout.Space(10);


            if (marketSubTab == 0) // 0 for Main
            {
                // Header
                GUILayout.BeginHorizontal();
                GUILayout.Label("Item", labelStyle, GUILayout.Width(150));
                GUILayout.Label("In Storage", labelStyle, GUILayout.Width(100));
                GUILayout.Label("Buy Price", labelStyle, GUILayout.Width(100));
                GUILayout.Label("Sell Price", labelStyle, GUILayout.Width(100));
                GUILayout.Label("Quantity", labelStyle, GUILayout.Width(100));
                GUILayout.Label("Actions", labelStyle, GUILayout.Width(200));
                GUILayout.EndHorizontal();

                storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);

                foreach (var itemProto in marketItemsForGUI)
                {
                    if (!itemPrices.TryGetValue(itemProto.ID, out long basePrice))
                    {
                        continue;
                    }

                    long sellPrice = GetSellPrice(itemProto.ID);

                    int currentStock = 0;
                    lock (remoteStorageLock)
                    {
                        if (remoteStorage.ContainsKey(itemProto.ID))
                        {
                            currentStock = remoteStorage[itemProto.ID].count;
                        }
                    }

                    if (!marketQuantityInputs.ContainsKey(itemProto.ID))
                    {
                        marketQuantityInputs[itemProto.ID] = "1";
                    }

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(itemProto.name, labelStyle, GUILayout.Width(150));
                    GUILayout.Label(currentStock.ToString("N0"), labelStyle, GUILayout.Width(100));
                    GUILayout.Label($"${basePrice:N0}", labelStyle, GUILayout.Width(100));
                    GUILayout.Label($"${sellPrice:N0}", labelStyle, GUILayout.Width(100));

                    string currentInput = marketQuantityInputs[itemProto.ID];
                    string newInput = GUILayout.TextField(currentInput, textFieldStyle, GUILayout.Width(100));
                    if (newInput != currentInput)
                    {
                        marketQuantityInputs[itemProto.ID] = newInput;
                    }

                    if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(80)))
                    {
                        BuyItem(itemProto.ID, basePrice);
                    }
                    if (GUILayout.Button("Sell", buttonStyle, GUILayout.Width(80)))
                    {
                        SellItem(itemProto.ID, sellPrice);
                    }

                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }
            else if (marketSubTab == 1) // 1 for Orders
            {
                MarketOrdersPanel();
            }
        }

        void MarketOrdersPanel()
        {
            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item", labelStyle, GUILayout.Width(150));
            GUILayout.Label("In Storage", labelStyle, GUILayout.Width(100));
            GUILayout.Label("Buy Below", labelStyle, GUILayout.Width(100));
            GUILayout.Label("Sell Above", labelStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);

            foreach (var itemProto in allItemsForMarket)
            {
                // Filter by selected item category
                if (GetItemCategory(itemProto) != selectedItemCategory)
                {
                    continue;
                }

                if (!unlockedItems.Contains(itemProto.ID))
                {
                    continue;
                }

                int itemId = itemProto.ID;
                marketOrders.TryGetValue(itemId, out MarketOrder order);

                // Initialize input strings if they don't exist
                if (!buyThresholdInputs.ContainsKey(itemId)) buyThresholdInputs[itemId] = order?.BuyThreshold.ToString() ?? "0";
                if (!sellThresholdInputs.ContainsKey(itemId)) sellThresholdInputs[itemId] = order?.SellThreshold.ToString() ?? "0";

                int currentStock = 0;
                lock (remoteStorageLock)
                {
                    if (remoteStorage.ContainsKey(itemId))
                    {
                        currentStock = remoteStorage[itemId].count;
                    }
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label(itemProto.name, labelStyle, GUILayout.Width(150));
                GUILayout.Label(currentStock.ToString("N0"), labelStyle, GUILayout.Width(100));

                // Input fields
                buyThresholdInputs[itemId] = GUILayout.TextField(buyThresholdInputs[itemId], textFieldStyle, GUILayout.Width(100));
                sellThresholdInputs[itemId] = GUILayout.TextField(sellThresholdInputs[itemId], textFieldStyle, GUILayout.Width(100));

                // Update logic
                if (int.TryParse(buyThresholdInputs[itemId], out int buyThreshold) &&
                    int.TryParse(sellThresholdInputs[itemId], out int sellThreshold))
                {
                    if (order == null)
                    {
                        if (buyThreshold > 0 || sellThreshold > 0)
                        {
                            marketOrders[itemId] = new MarketOrder { BuyThreshold = buyThreshold, SellThreshold = sellThreshold };
                        }
                    }
                    else
                    {
                        order.BuyThreshold = buyThreshold;
                        order.SellThreshold = sellThreshold;
                        if (buyThreshold == 0 && sellThreshold == 0)
                        {
                            marketOrders.Remove(itemId);
                        }
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        void ContractsPanel()
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);

            // --- Total Income Display ---
            long totalIncome = (tradeRoutesTier1 * 1000L) + (tradeRoutesTier2 * 12500L) + (tradeRoutesTier3 * 150000L);
            GUILayout.Label($"Total Passive Income: ${totalIncome:N0} / second", windowStyle);
            GUILayout.Label($"Current Balance: ${playerBalance:N0}", labelStyle);

            GUILayout.Space(20);

            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);

            // --- Tier 1 ---
            GUILayout.Label("Planetary Trade Route", labelStyle);
            GUILayout.Label($"  Cost: ${10000000:N0}", labelStyle);
            GUILayout.Label($"  Income: ${1000:N0} / second", labelStyle);
            GUILayout.Label($"  Owned: {tradeRoutesTier1}", labelStyle);
            if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(100)))
            {
                if (playerBalance >= 10000000)
                {
                    playerBalance -= 10000000;
                    tradeRoutesTier1++;
                }
            }
            GUILayout.Space(15);

            // --- Tier 2 ---
            GUILayout.Label("Interstellar Trade Route", labelStyle);
            GUILayout.Label($"  Cost: ${100000000:N0}", labelStyle);
            GUILayout.Label($"  Income: ${12500:N0} / second", labelStyle);
            GUILayout.Label($"  Owned: {tradeRoutesTier2}", labelStyle);
            if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(100)))
            {
                if (playerBalance >= 100000000)
                {
                    playerBalance -= 100000000;
                    tradeRoutesTier2++;
                }
            }
            GUILayout.Space(15);

            // --- Tier 3 ---
            GUILayout.Label("Galactic Trade Route", labelStyle);
            GUILayout.Label($"  Cost: ${1000000000:N0}", labelStyle);
            GUILayout.Label($"  Income: ${150000:N0} / second", labelStyle);
            GUILayout.Label($"  Owned: {tradeRoutesTier3}", labelStyle);
            if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(100)))
            {
                if (playerBalance >= 1000000000)
                {
                    playerBalance -= 1000000000;
                    tradeRoutesTier3++;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        void DashboardPanel()
        {
            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);
            GUILayout.BeginVertical();

            // Bottlenecks Section
            GUILayout.Label("Bottlenecks", windowStyle);
            if (cachedBottlenecks.Any())
            {
                foreach (var bottleneck in cachedBottlenecks)
                {
                    string itemName = LDB.items.Select(bottleneck.ItemId).name;
                    string deficitText = $"{Math.Abs(bottleneck.DeficitPerMinute)}/min deficit";

                    string etaText = "";
                    if (bottleneck.DeficitPerMinute < 0)
                    {
                        double minutesToDepletion = (double)bottleneck.CurrentStock / Math.Abs(bottleneck.DeficitPerMinute);
                        etaText = $" (ETA: {FormatDuration(minutesToDepletion)})";
                    }

                    GUILayout.Label($"{itemName}: {deficitText}{etaText}", labelStyle);
                }
            }
            else
            {
                GUILayout.Label("No potential bottlenecks detected.", labelStyle);
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void RefreshMarketItemsForGUI()
        {
            if (selectedItemCategory == lastMarketItemCategory && marketItemsForGUI.Count > 0)
            {
                // No need to refresh if category hasn't changed, unless the list is empty
                return;
            }

            marketItemsForGUI = allItemsForMarket
                .Where(itemProto =>
                    GetItemCategory(itemProto) == selectedItemCategory &&
                    itemPrices.ContainsKey(itemProto.ID) &&
                    unlockedItems.Contains(itemProto.ID))
                .OrderBy(item => item.name)
                .ToList();

            lastMarketItemCategory = selectedItemCategory;
        }

        #region Dashboard Calculations

        private string FormatDuration(double minutes)
        {
            if (double.IsInfinity(minutes) || minutes > 60 * 24 * 30) // Cap at 30 days for readability
            {
                return ">30d";
            }
            if (minutes < 1)
            {
                return "<1m";
            }
            if (minutes < 60)
            {
                return $"{minutes:F0}m";
            }

            double hours = minutes / 60.0;
            if (hours < 24)
            {
                int h = (int)hours;
                int m = (int)Math.Round((hours - h) * 60);
                return $"{h}h{m:D2}m";
            }

            double days = hours / 24.0;
            return $"{days:F1}d";
        }

        private List<BottleneckInfo> GetPotentialBottlenecks()
        {
            var bottlenecks = new List<BottleneckInfo>();
            const int sampleMinutes = HistoryMinutes; 

            // Create a snapshot of the remote storage to avoid locking it for too long.
            Dictionary<int, int> currentStockSnapshot;
            lock (remoteStorageLock)
            {
                currentStockSnapshot = remoteStorage.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.count);
            }

            lock (itemStatsLock)
            {
                foreach (var pair in itemStats)
                {
                    var stats = pair.Value;
                    // Prune data here to ensure totals are up-to-date before calculation
                    PruneOldData(stats.AddedHistory, stats, true);
                    PruneOldData(stats.TakenHistory, stats, false);

                    float productionRate = stats.TotalAddedInWindow / (float)sampleMinutes;
                    float consumptionRate = stats.TotalTakenInWindow / (float)sampleMinutes;

                    if (consumptionRate > productionRate)
                    {
                        int deficitPerMinute = (int)Math.Round(productionRate - consumptionRate);
                        int currentStock = currentStockSnapshot.ContainsKey(pair.Key) ? currentStockSnapshot[pair.Key] : 0;

                        bottlenecks.Add(new BottleneckInfo
                        {
                            ItemId = pair.Key,
                            DeficitPerMinute = deficitPerMinute,
                            CurrentStock = currentStock
                        });
                    }
                }
            }
            return bottlenecks.OrderBy(b => b.DeficitPerMinute).ToList();
        }

        #endregion

        void MainPanel()
        {
            mainPanelScrollPosition = GUILayout.BeginScrollView(mainPanelScrollPosition, false, true, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, scrollViewStyle);
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            enableMod.Value = GUILayout.Toggle(enableMod.Value, "Enable MOD", toggleStyle);
            autoReplenishPackage.Value = GUILayout.Toggle(autoReplenishPackage.Value, "Auto Replenish Filtered Items", toggleStyle);
            autoCleanInventory.Value = GUILayout.Toggle(autoCleanInventory.Value, "Auto Clean Inventory to Logistic Slots", toggleStyle);

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            autoSpray.Value = GUILayout.Toggle(autoSpray.Value, "Auto Spray", toggleStyle);
            if (autoSpray.Value)
            {
                costProliferator.Value = GUILayout.Toggle(costProliferator.Value, "Consume Proliferator", toggleStyle);
            }
            GUILayout.EndHorizontal();

            if (autoSpray.Value)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Proliferator Tier:", labelStyle, GUILayout.Width(120));
                proliferatorSelection.Value = (ProliferatorSelection)GUILayout.Toolbar((int)proliferatorSelection.Value, new string[] { "All", "MK.I", "MK.II", "MK.III" }, toolbarStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(15);
            useStorege.Value = GUILayout.Toggle(useStorege.Value, "Recover from storage boxes/tanks", toggleStyle);

            GUILayout.Space(15);
            autoReplenishTPPFuel.Value = GUILayout.Toggle(autoReplenishTPPFuel.Value, "Auto-refuel Thermal Power Plants", toggleStyle);
            if (autoReplenishTPPFuel.Value)
            {
                selectedFuelIndex = GUILayout.SelectionGrid(selectedFuelIndex, fuelOptions.Values.ToArray(), 3, toggleStyle);
                fuelId.Value = fuelOptions.Keys.ToArray()[selectedFuelIndex];
            }
            autoReplenishFPPFuel.Value = GUILayout.Toggle(autoReplenishFPPFuel.Value, "Auto-refuel Fusion Power Plants", toggleStyle);
            if (autoReplenishFPPFuel.Value)
            {
                GUILayout.Label("Artificial Star Fuel:", labelStyle);
                selectedStarFuelIndex = GUILayout.SelectionGrid(selectedStarFuelIndex, starFuelOptions.Values.ToArray(), 3, toggleStyle);
                starFuelId.Value = starFuelOptions.Keys.ToArray()[selectedStarFuelIndex];
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        void ItemPanel()
        {
            GUILayout.BeginVertical();
            infBuildings.Value = GUILayout.Toggle(infBuildings.Value, "Infinite Buildings", toggleStyle);
            infVeins.Value = GUILayout.Toggle(infVeins.Value, "Infinite Minerals", toggleStyle);
            infItems.Value = GUILayout.Toggle(infItems.Value, "Infinite Items (Disables Achievements)", toggleStyle);
            infSand.Value = GUILayout.Toggle(infSand.Value, "Infinite Soil Pile", toggleStyle);
            GUILayout.EndVertical();
        }

        void FightPanel()
        {
            GUILayout.BeginVertical();
            infAmmo.Value = GUILayout.Toggle(infAmmo.Value, "Infinite Ammo", toggleStyle);
            infFleet.Value = GUILayout.Toggle(infFleet.Value, "Infinite Fleet", toggleStyle);
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("Clear Banned Items from Battle Bases", "Removes items from Battlefield Analysis Bases that you have marked not to be picked up."), buttonStyle, GUILayout.ExpandWidth(true)))
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

                // Step 1: Create a snapshot of the data needed for calculation under a lock.
                Dictionary<int, (int count, int inc)> storageSnapshot = new Dictionary<int, (int count, int inc)>();
                Dictionary<int, int> incPoolSnapshot = new Dictionary<int, int>();
                bool costProliferatorSnapshot = costProliferator.Value;
                ProliferatorSelection proliferatorSelectionSnapshot = proliferatorSelection.Value;

                lock (remoteStorageLock)
                {
                    foreach (var pair in remoteStorage)
                    {
                        storageSnapshot[pair.Key] = (pair.Value.count, pair.Value.inc);
                    }
                    foreach (var pair in incPool)
                    {
                        incPoolSnapshot[pair.Key] = pair.Value;
                    }
                }

                // Step 2: Perform all calculations on the snapshot, without holding the lock.
                var activeProliferators = new List<(int, int)>();
                switch (proliferatorSelectionSnapshot)
                {
                    case ProliferatorSelection.Mk1: activeProliferators.Add((ItemIds.ProliferatorMk1, 1)); break;
                    case ProliferatorSelection.Mk2: activeProliferators.Add((ItemIds.ProliferatorMk2, 2)); break;
                    case ProliferatorSelection.Mk3: activeProliferators.Add((ItemIds.ProliferatorMk3, 4)); break;
                    case ProliferatorSelection.All: default: activeProliferators.AddRange(proliferators); break;
                }

                if (costProliferatorSnapshot)
                {
                    foreach (var (proliferatorId, sprayLevel) in activeProliferators)
                    {
                        int factor = ProliferatorBonus.GetSprayInc(proliferatorId);
                        if (factor > 0 && storageSnapshot.ContainsKey(proliferatorId))
                        {
                            int p_count = storageSnapshot[proliferatorId].count;
                            if (p_count > 0)
                            {
                                incPoolSnapshot[proliferatorId] += p_count * factor;
                                storageSnapshot[proliferatorId] = (0, storageSnapshot[proliferatorId].inc); // Proliferator is consumed
                            }
                        }
                    }
                }

                var updatedIncs = new Dictionary<int, int>();
                var itemIds = new List<int>(storageSnapshot.Keys);

                foreach (int itemId in itemIds)
                {
                    var (count, inc) = storageSnapshot[itemId];
                    if (itemId <= 0 || count <= 0) continue;

                    if (itemId >= ItemIds.ProliferatorMk1 && itemId <= ItemIds.ProliferatorMk3)
                    {
                        updatedIncs[itemId] = count * 4;
                        continue;
                    }

                    ItemProto itemProto = LDB.items.Select(itemId);
                    if (itemProto.CanBuild || itemProto.isFighter) continue;

                    if (!costProliferatorSnapshot)
                    {
                        int maxSprayLevel = activeProliferators.Count > 0 ? activeProliferators.Max(p => p.Item2) : 4;
                        if (inc < count * maxSprayLevel)
                        {
                            updatedIncs[itemId] = count * maxSprayLevel;
                        }
                        continue;
                    }

                    int currentInc = inc;
                    foreach (var (proliferatorId, sprayLevel) in activeProliferators)
                    {
                        int expectedInc = count * sprayLevel - currentInc;
                        if (expectedInc <= 0) break;

                        int pointsToTake = expectedInc;
                        int availablePoints = incPoolSnapshot.ContainsKey(proliferatorId) ? incPoolSnapshot[proliferatorId] : 0;
                        int actualPoints = Math.Min(pointsToTake, availablePoints);

                        if (actualPoints > 0)
                        {
                            currentInc += actualPoints;
                            incPoolSnapshot[proliferatorId] -= actualPoints;
                        }
                    }
                    if (currentInc > inc)
                    {
                        updatedIncs[itemId] = currentInc;
                    }
                }

                // Step 3: Apply the calculated changes back to the original data under a lock.
                lock (remoteStorageLock)
                {
                    if (costProliferatorSnapshot)
                    {
                        // Apply consumed proliferator counts
                        foreach (var (proliferatorId, sprayLevel) in activeProliferators)
                        {
                            if (remoteStorage.ContainsKey(proliferatorId) && remoteStorage[proliferatorId].count > 0)
                            {
                                remoteStorage[proliferatorId].count = 0;
                            }
                        }
                    }

                    // Apply updated incs
                    foreach (var pair in updatedIncs)
                    {
                        if (remoteStorage.ContainsKey(pair.Key))
                        {
                            remoteStorage[pair.Key].inc = pair.Value;
                        }
                    }

                    // Update incPool
                    foreach (var pair in incPoolSnapshot)
                    {
                        incPool[pair.Key] = pair.Value;
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

                lock (remoteStorageLock)
                {
                    for (int i = 0; i < deliveryPackage.gridLength; i++)
                    {
                        var grid = deliveryPackage.grids[i];
                        if (grid.itemId <= 0) continue;

                        if (grid.requireCount > grid.count)
                        {
                            int needCount = grid.requireCount - grid.count;
                            int[] result = UnsafeTakeItem(grid.itemId, needCount);
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
                            int[] result = UnsafeAddItem(grid.itemId, supplyCount, supplyInc, true);
                            if (result[0] > 0)
                            {
                                deliveryPackage.grids[i].count -= result[0];
                                deliveryPackage.grids[i].inc -= result[1];
                            }
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
                    lock (remoteStorageLock)
                    {
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
                                    int[] result = UnsafeAddItem(ss.itemId, ss.count, ss.inc);
                                    sc.storage[i].count -= result[0];
                                    sc.storage[i].inc -= result[1];
                                }
                                else if (logic == ELogisticStorage.Demand)
                                {
                                    int expectCount = ss.max - order - ss.count;
                                    if (expectCount <= 0) continue;
                                    int[] result = UnsafeTakeItem(ss.itemId, expectCount);
                                    sc.storage[i].count += result[0];
                                    sc.storage[i].inc += result[1];
                                }
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

                    lock (remoteStorageLock)
                    {
                        foreach (StorageComponent sc in pf.factoryStorage.storagePool)
                        {
                            if (sc == null || sc.isEmpty) continue;
                            bool changed = false;
                            for (int i = 0; i < sc.grids.Length; i++)
                            {
                                StorageComponent.GRID grid = sc.grids[i];
                                if (grid.itemId <= 0 || grid.count <= 0) continue;
                                int[] result = UnsafeAddItem(grid.itemId, grid.count, grid.inc);
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
                            int[] result = UnsafeAddItem(tc.fluidId, tc.fluidCount, tc.fluidInc);
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
                    lock (remoteStorageLock)
                    {
                        foreach (AssemblerComponent ac in pf.factorySystem.assemblerPool)
                        {
                            if (ac.id <= 0 || ac.recipeId <= 0) continue;
                            for (int i = 0; i < ac.products.Length; i++)
                            {
                                if (ac.produced[i] > 0)
                                    ac.produced[i] -= UnsafeAddItem(ac.products[i], ac.produced[i], 0)[0];
                            }
                            for (int i = 0; i < ac.requires.Length; i++)
                            {
                                int expectCount = Math.Max(ac.requireCounts[i] * 5 - ac.served[i], 0);
                                if (expectCount > 0)
                                {
                                    int[] result = UnsafeTakeItem(ac.requires[i], expectCount);
                                    ac.served[i] += result[0];
                                    ac.incServed[i] += result[1];
                                }
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

                    lock (remoteStorageLock)
                    {
                        for (int i = 0; i < pf.factorySystem.minerPool.Length; i++)
                        {
                            MinerComponent mc = pf.factorySystem.minerPool[i];
                            if (mc.id <= 0 || mc.productId <= 0 || mc.productCount <= 0) continue;
                            int[] result = UnsafeAddItem(mc.productId, mc.productCount, 0);
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
                                    int[] result = UnsafeAddItem(ss.itemId, ss.count, 0);
                                    sc.storage[i].count -= result[0];
                                }
                            }
                            else if (sc.isVeinCollector)
                            {
                                StationStore ss = sc.storage[0];
                                if (ss.itemId <= 0 || ss.count <= 0 || ss.localLogic != ELogisticStorage.Supply) continue;
                                int[] result = UnsafeAddItem(ss.itemId, ss.count, 0);
                                sc.storage[0].count -= result[0];
                            }
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
                // This task is now driven by a unified, data-driven approach.
                // For each generator, we determine its valid fuel types and capacity.
                // Then, we use a pessimistic try-take loop to ensure the highest-priority fuel is always taken,
                // which is robust against race conditions.

                foreach (var pf in GameMain.data.factories)
                {
                    if (pf == null) continue;
                    for (int i = 0; i < pf.powerSystem.genPool.Length; i++)
                    {
                        PowerGeneratorComponent pgc = pf.powerSystem.genPool[i];
                        if (pgc.id <= 0) continue;

                        if (pgc.gamma) // Artificial Sun logic remains separate
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

                        List<int> fuelPriority = null;
                        int fuelCapacity = 0;

                        switch (pgc.fuelMask)
                        {
                            case 1: // Thermal
                                if (autoReplenishTPPFuel.Value)
                                {
                                    fuelPriority = thermalFuelsByPriority;
                                    fuelCapacity = 50;
                                }
                                break;
                            case 2: // Artificial Star
                                if (autoReplenishFPPFuel.Value)
                                {
                                    if (starFuelId.Value != 0)
                                    {
                                        fuelPriority = new List<int> { starFuelId.Value };
                                    }
                                    else
                                    {
                                        fuelPriority = new List<int> { ItemIds.StrangeAnnihilationFuelRod, ItemIds.AntimatterFuelRod };
                                    }
                                    fuelCapacity = 5;
                                }
                                break;
                            case 4: // Mini Fusion
                                if (autoReplenishFPPFuel.Value)
                                {
                                    fuelPriority = new List<int> { ItemIds.DeuteronFuelRod };
                                    fuelCapacity = 5;
                                }
                                break;
                        }

                        // Refuel if empty
                        if (fuelPriority != null && pgc.fuelCount == 0)
                        {
                            foreach (var fuelIdToTry in fuelPriority)
                            {
                                int[] result = TakeItem(fuelIdToTry, fuelCapacity);
                                if (result[0] > 0)
                                {
                                    pf.powerSystem.genPool[i].SetNewFuel(fuelIdToTry, (short)result[0], (short)result[1]);
                                    break; // Fueling successful, stop trying other fuels
                                }
                            }
                        }
                        // Top-up if not full
                        else if (fuelPriority != null && pgc.fuelId > 0 && pgc.fuelCount < fuelCapacity)
                        {
                            // Only top-up with a fuel type that is valid for this generator
                            if (fuelPriority.Contains(pgc.fuelId))
                            {
                                int[] result = TakeItem(pgc.fuelId, fuelCapacity - pgc.fuelCount);
                                if (result[0] > 0)
                                {
                                    pf.powerSystem.genPool[i].fuelCount += (short)result[0];
                                    pf.powerSystem.genPool[i].fuelInc += (short)result[1];
                                }
                            }
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
                    lock (remoteStorageLock)
                    {
                        for (int i = 0; i < pf.powerSystem.excPool.Length; i++)
                        {
                            PowerExchangerComponent pec = pf.powerSystem.excPool[i];
                            if (pec.targetState == -1) // Discharge
                            {
                                if (pec.fullCount < 3)
                                {
                                    int[] result = UnsafeTakeItem(pec.fullId, 3 - pec.fullCount);
                                    pf.powerSystem.excPool[i].fullCount += (short)result[0];
                                }
                                if (pec.emptyCount > 0)
                                {
                                    int[] result = UnsafeAddItem(pec.emptyId, pec.emptyCount, 0);
                                    pf.powerSystem.excPool[i].emptyCount -= (short)result[0];
                                }
                            }
                            else if (pec.targetState == 1) // Charge
                            {
                                if (pec.emptyCount < 5)
                                {
                                    int[] result = UnsafeTakeItem(pec.emptyId, 5 - pec.emptyCount);
                                    pf.powerSystem.excPool[i].emptyCount += (short)result[0];
                                }
                                if (pec.fullCount > 0)
                                {
                                    int[] result = UnsafeAddItem(pec.fullId, pec.fullCount, 0);
                                    pf.powerSystem.excPool[i].fullCount -= (short)result[0];
                                }
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
                    lock (remoteStorageLock)
                    {
                        for (int i = 0; i < pf.factorySystem.siloPool.Length; i++)
                        {
                            SiloComponent sc = pf.factorySystem.siloPool[i];
                            if (sc.id > 0 && sc.bulletCount <= 3)
                            {
                                int[] result = UnsafeTakeItem(sc.bulletId, 10);
                                pf.factorySystem.siloPool[i].bulletCount += result[0];
                                pf.factorySystem.siloPool[i].bulletInc += result[1];
                            }
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
                    lock (remoteStorageLock)
                    {
                        for (int i = 0; i < pf.factorySystem.ejectorPool.Length; i++)
                        {
                            EjectorComponent ec = pf.factorySystem.ejectorPool[i];
                            if (ec.id > 0 && ec.bulletCount <= 5)
                            {
                                int[] result = UnsafeTakeItem(ec.bulletId, 15);
                                pf.factorySystem.ejectorPool[i].bulletCount += result[0];
                                pf.factorySystem.ejectorPool[i].bulletInc += result[1];
                            }
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
                    lock (remoteStorageLock)
                    {
                        foreach (LabComponent lc in pf.factorySystem.labPool)
                        {
                            if (lc.id <= 0) continue;
                            if (lc.recipeId > 0)
                            {
                                for (int i = 0; i < lc.products.Length; i++)
                                {
                                    if (lc.produced[i] > 0)
                                    {
                                        lc.produced[i] -= UnsafeAddItem(lc.products[i], lc.produced[i], 0)[0];
                                    }
                                }
                                for (int i = 0; i < lc.requires.Length; i++)
                                {
                                    int expectCount = lc.requireCounts[i] * 3 - lc.served[i] - lc.incServed[i];
                                    int[] result = UnsafeTakeItem(lc.requires[i], expectCount);
                                    lc.served[i] += result[0];
                                    lc.incServed[i] += result[1];
                                }
                            }
                            else if (lc.researchMode)
                            {
                                for (int i = 0; i < lc.matrixPoints.Length; i++)
                                {
                                    if (lc.matrixPoints[i] <= 0 || lc.matrixServed[i] >= lc.matrixPoints[i] * 3600) continue;
                                    int[] result = UnsafeTakeItem(LabComponent.matrixIds[i], lc.matrixPoints[i]);
                                    lc.matrixServed[i] += result[0] * 3600;
                                    lc.matrixIncServed[i] += result[1] * 3600;
                                }
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
                    lock (remoteStorageLock)
                    {
                        for (int i = 0; i < pf.defenseSystem.turrets.buffer.Length; i++)
                        {
                            TurretComponent tc = pf.defenseSystem.turrets.buffer[i];
                            if (tc.id == 0 || tc.type == ETurretType.Laser || tc.ammoType == EAmmoType.None || tc.itemCount > 0 || tc.bulletCount > 0) continue;
                            foreach (int itemId in ammos[tc.ammoType])
                            {
                                int[] result = UnsafeTakeItem(itemId, 50 - tc.itemCount);
                                if (result[0] != 0)
                                {
                                    pf.defenseSystem.turrets.buffer[i].SetNewItem(itemId, (short)result[0], (short)result[1]);
                                    break;
                                }
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
                    lock (remoteStorageLock)
                    {
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
                                        if (UnsafeTakeItem(itemId, 1)[0] != 0)
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
                                int[] result = UnsafeAddItem(grid.itemId, grid.count, grid.inc);
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
            }
            catch (Exception ex) { Logger.LogError(ex); }
        });

        Task ProcessMarketOrders() => Task.Run(() =>
        {
            try
            {
                if (marketOrders.Count == 0) return;

                var orders = new Dictionary<int, MarketOrder>(marketOrders);

                foreach (var pair in orders)
                {
                    int itemId = pair.Key;
                    MarketOrder order = pair.Value;

                    int currentStock = 0;
                    lock (remoteStorageLock)
                    {
                        if (remoteStorage.ContainsKey(itemId))
                        {
                            currentStock = remoteStorage[itemId].count;
                        }
                    }

                    // Sell Logic
                    if (order.SellThreshold > 0 && currentStock > order.SellThreshold)
                    {
                        long sellPrice = GetSellPrice(itemId);
                        if (sellPrice > 0)
                        {
                            // "Sell" by removing items from storage.
                            // Sell all items above the sell threshold.
                            int amountToSell = currentStock - order.SellThreshold;
                            int[] soldItems = TakeItem(itemId, amountToSell);
                            int soldQuantity = soldItems[0];

                            if (soldQuantity > 0)
                            {
                                long earnings = sellPrice * soldQuantity;
                                Interlocked.Add(ref playerBalance, earnings);
                            }
                        }
                    }

                    // Buy Logic
                    if (order.BuyThreshold > 0 && currentStock < order.BuyThreshold)
                    {
                        int amountToBuy = order.BuyThreshold - currentStock;
                        if (amountToBuy > 0 && itemPrices.TryGetValue(itemId, out long basePrice) && basePrice > 0)
                        {
                            long currentBalance = Interlocked.Read(ref playerBalance);
                            int affordableQuantity = amountToBuy;
                            if (basePrice * affordableQuantity > currentBalance)
                            {
                                affordableQuantity = (int)(currentBalance / basePrice);
                            }

                            if (affordableQuantity > 0)
                            {
                                long finalCost = basePrice * affordableQuantity;
                                Interlocked.Add(ref playerBalance, -finalCost);
                                AddItem(itemId, affordableQuantity, 0, true); // Bypass limit to ensure it can be bought
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Logger.LogError($"Error in ProcessMarketOrders: {ex}"); }
        });

        private float _tradeRouteIncomeTimer = 0f;
        private const float TradeRouteIncomeInterval = 1.0f; // 1 second

        void ProcessTradeRoutes()
        {
            // This method is called every 50ms, so we use a timer to only add income once per second.
            _tradeRouteIncomeTimer += 0.05f; // 50ms tick
            if (_tradeRouteIncomeTimer >= TradeRouteIncomeInterval)
            {
                _tradeRouteIncomeTimer -= TradeRouteIncomeInterval;

                long incomeThisSecond = 0;
                incomeThisSecond += tradeRoutesTier1 * 1000L;
                incomeThisSecond += tradeRoutesTier2 * 12500L;
                incomeThisSecond += tradeRoutesTier3 * 150000L;

                if (incomeThisSecond > 0)
                {
                    Interlocked.Add(ref playerBalance, incomeThisSecond);
                }
            }
        }

        Task ProcessPackage() => Task.Run(() =>
        {
            try
            {
                bool changed = false;
                StorageComponent package = GameMain.mainPlayer.package;
                lock (remoteStorageLock)
                {
                    for (int i = 0; i < package.grids.Length; i++)
                    {
                        StorageComponent.GRID grid = package.grids[i];
                        if (grid.filter != 0 && grid.count < grid.stackSize)
                        {
                            int[] result = UnsafeTakeItem(grid.itemId, grid.stackSize - grid.count);
                            if (result[0] != 0)
                            {
                                package.grids[i].count += result[0];
                                package.grids[i].inc += result[1];
                                changed = true;
                            }
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
                return UnsafeAddItem(itemId, count, inc, bypassLimit);
            }
        }

        /// <summary>
        /// Adds an item to the central remote storage without acquiring a lock. Caller must handle thread safety.
        /// </summary>
        private int[] UnsafeAddItem(int itemId, int count, int inc, bool bypassLimit = false)
        {
            if (!remoteStorage.ContainsKey(itemId))
            {
                ItemProto itemProto = LDB.items.Select(itemId);
                ItemCategory category = GetItemCategory(itemProto);
                int defaultLimit = 1000000;
                if (category == ItemCategory.BuildingsAndVehicles || category == ItemCategory.AmmunitionAndCombat)
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

            if (amountToAdd > 0)
            {
                unlockedItems.Add(itemId);
                RecordAdd(itemId, amountToAdd);
            }

            return new int[] { amountToAdd, incToAdd };
        }

        /// <summary>
        /// Takes an item from the central remote storage.
        /// </summary>
        /// <returns>An array containing the count and inc of the item actually taken.</returns>
        public int[] TakeItem(int itemId, int count)
        {
            lock (remoteStorageLock)
            {
                return UnsafeTakeItem(itemId, count);
            }
        }

        /// <summary>
        /// Takes an item from the central remote storage without acquiring a lock. Caller must handle thread safety.
        /// </summary>
        private int[] UnsafeTakeItem(int itemId, int count)
        {
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

                    if (takenCount > 0)
                    {
                        RecordTake(itemId, takenCount);
                    }

                    return new int[] { takenCount, takenInc };
                }
            }
            return new int[] { 0, 0 };
        }

        void RecordAdd(int itemId, int amount)
        {
            lock (itemStatsLock)
            {
                if (!itemStats.ContainsKey(itemId))
                {
                    itemStats[itemId] = new ItemStats();
                }
                var stats = itemStats[itemId];
                stats.AddedHistory.Enqueue(new DataPoint { Timestamp = DateTime.Now, Amount = amount });
                stats.TotalAddedInWindow += amount;
            }
        }

        void RecordTake(int itemId, int amount)
        {
            lock (itemStatsLock)
            {
                if (!itemStats.ContainsKey(itemId))
                {
                    itemStats[itemId] = new ItemStats();
                }
                var stats = itemStats[itemId];
                stats.TakenHistory.Enqueue(new DataPoint { Timestamp = DateTime.Now, Amount = amount });
                stats.TotalTakenInWindow += amount;
            }
        }

        void PruneOldData(Queue<DataPoint> history, ItemStats stats, bool isAdd)
        {
            DateTime cutoff = DateTime.Now.AddMinutes(-HistoryMinutes);
            while (history.Count > 0 && history.Peek().Timestamp < cutoff)
            {
                var removedPoint = history.Dequeue();
                if (isAdd)
                {
                    stats.TotalAddedInWindow -= removedPoint.Amount;
                }
                else
                {
                    stats.TotalTakenInWindow -= removedPoint.Amount;
                }
            }
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

        /// <summary>
        /// Checks if a given quantity of an item is available in remote storage without consuming it.
        /// </summary>
        bool HasItem(int itemId, int count = 1)
        {
            if (itemId <= 0 || count <= 0) return false;

            lock (remoteStorageLock)
            {
                return UnsafeHasItem(itemId, count);
            }
        }

        private bool UnsafeHasItem(int itemId, int count = 1)
        {
            ItemProto item = LDB.items.Select(itemId);
            bool isInfinite = (infItems.Value) ||
                              (infVeins.Value && IsVein(itemId)) ||
                              (infBuildings.Value && item.CanBuild) ||
                              (infAmmo.Value && item.isAmmo) ||
                              (infFleet.Value && item.isFighter);

            if (isInfinite)
            {
                return true;
            }

            if (remoteStorage.ContainsKey(itemId))
            {
                return remoteStorage[itemId].count >= count;
            }
            return false;
        }

        int GetBestThermalFuel()
        {
            // 1. Check for user's preferred fuel first.
            if (fuelId.Value != 0)
            {
                if (HasItem(fuelId.Value))
                {
                    return fuelId.Value;
                }
                // If preferred fuel is set but unavailable, do not fall back. Wait for it.
                return 0;
            }

            // 2. If no preferred fuel ("Auto" mode), iterate through the priority list.
            foreach (int fuel in thermalFuelsByPriority)
            {
                if (HasItem(fuel))
                {
                    return fuel;
                }
            }

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

        private ItemCategory GetItemCategory(ItemProto itemProto)
        {
            if (itemProto == null) return ItemCategory.IntermediateProducts;
            if (itemProto.ID >= 6001 && itemProto.ID <= 6006) return ItemCategory.ScienceMatrices;
            if (itemProto.isAmmo || itemProto.isFighter) return ItemCategory.AmmunitionAndCombat;
            if (itemProto.CanBuild) return ItemCategory.BuildingsAndVehicles;
            if (IsVein(itemProto.ID)) return ItemCategory.RawResources;
            return ItemCategory.IntermediateProducts;
        }

        private (string text, Color color) GetProliferationStatus(int count, int inc, int itemId)
        {
            const double epsilon = 1e-5;

            if (count <= 0) return ("N/A", Color.grey);
            ItemProto itemProto = LDB.items.Select(itemId);
            if (itemProto == null || itemProto.CanBuild || itemProto.isFighter || (itemId >= ItemIds.ProliferatorMk1 && itemId <= ItemIds.ProliferatorMk3))
            {
                return ("N/A", Color.grey);
            }

            double pointsPerItem = (double)inc / count;

            for (int i = 0; i < proliferationTiers.Length; i++)
            {
                var currentTier = proliferationTiers[i];
                if (pointsPerItem >= currentTier.MinPoints - epsilon)
                {
                    if (i == 0) return (currentTier.Name, currentTier.Color);
                    var nextTier = proliferationTiers[i - 1];
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
            // Only populate the list if the Storage tab is active.
            if (selectedStorageCategory != StorageCategory.Storage)
            {
                storageItemsForGUI.Clear();
                return;
            }

            lock (remoteStorageLock)
            {
                storageItemsForGUI = remoteStorage
                    .Where(pair =>
                    {
                        if (pair.Value.count <= 0) return false;
                        ItemProto itemProto = LDB.items.Select(pair.Key);
                        return itemProto != null && GetItemCategory(itemProto) == selectedItemCategory;
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
            w.Write(playerBalance);

            w.Write(unlockedItems.Count);
            foreach (int itemId in unlockedItems)
            {
                w.Write(itemId);
            }

            w.Write(marketOrders.Count);
            foreach (var order in marketOrders)
            {
                w.Write(order.Key);
                order.Value.Export(w);
            }

            // Save Trade Route Data (version 7+)
            w.Write(tradeRoutesTier1);
            w.Write(tradeRoutesTier2);
            w.Write(tradeRoutesTier3);

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

            if (version >= 3)
            {
                playerBalance = r.ReadInt64();
            }
            else
            {
                playerBalance = 0;
            }

            unlockedItems.Clear();
            if (version >= 4)
            {
                int unlockedCount = r.ReadInt32();
                for (int i = 0; i < unlockedCount; i++)
                {
                    unlockedItems.Add(r.ReadInt32());
                }
            }

            marketOrders.Clear();
            if (version >= 5)
            {
                int marketOrderCount = r.ReadInt32();
                for (int i = 0; i < marketOrderCount; i++)
                {
                    int itemId = r.ReadInt32();
                    marketOrders[itemId] = MarketOrder.Import(r, version);
                }
            }

            if (version >= 7)
            {
                tradeRoutesTier1 = r.ReadInt32();
                tradeRoutesTier2 = r.ReadInt32();
                tradeRoutesTier3 = r.ReadInt32();
            }
            else
            {
                tradeRoutesTier1 = 0;
                tradeRoutesTier2 = 0;
                tradeRoutesTier3 = 0;
            }

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
            playerBalance = 0;
            unlockedItems.Clear();
            marketOrders.Clear();
            lock (remoteStorageLock)
            {
                remoteStorage.Clear();
            }
        }
        #endregion
    }
}


