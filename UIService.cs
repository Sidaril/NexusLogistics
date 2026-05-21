using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BepInEx.Configuration;

namespace NexusLogistics
{
    public class UIService
    {
        private readonly StorageService _storageService;
        private readonly LogisticsEngine _logisticsEngine;

        // GUI Styles
        private GUIStyle windowStyle, labelStyle, buttonStyle, toggleStyle, toolbarStyle, textFieldStyle, scrollViewStyle;
        private Texture2D borderTexture;
        private bool guiStylesInitialized = false;

        // GUI State
        public bool ShowGUI { get; set; }
        public bool ShowStorageGUI { get; set; }
        private Rect windowRect = new Rect(700, 250, 600, 500);
        private Rect storageWindowRect = new Rect(100, 250, 900, 500);
        private Vector2 storageScrollPosition, mainPanelScrollPosition;
        private int selectedPanel = 0;

        public enum StorageCategory { Dashboard, Storage, Market, Contracts }
        public enum ItemCategory { RawResources, IntermediateProducts, BuildingsAndVehicles, AmmunitionAndCombat, ScienceMatrices }
        public enum ProliferatorSelection { All, Mk1, Mk2, Mk3 }

        private StorageCategory selectedStorageCategory = StorageCategory.Dashboard;
        private ItemCategory selectedItemCategory = ItemCategory.RawResources;
        private int marketSubTab = 0; // 0 for Main, 1 for Orders

        private readonly Dictionary<int, string> limitInputStrings = new Dictionary<int, string>();
        private readonly Dictionary<int, string> marketQuantityInputs = new Dictionary<int, string>();
        private readonly Dictionary<int, string> buyThresholdInputs = new Dictionary<int, string>();
        private readonly Dictionary<int, string> sellThresholdInputs = new Dictionary<int, string>();

        // Data for GUI (to be updated from main plugin or engine)
        public IEnumerable<KeyValuePair<int, RemoteStorageItem>> StorageItemsForGUI { get; set; }
        public List<ItemProto> MarketItemsForGUI { get; set; } = new List<ItemProto>();
        public List<ItemProto> AllItemsForMarket { get; set; } = new List<ItemProto>();
        public Dictionary<int, long> ItemPrices { get; set; } = new Dictionary<int, long>();
        public List<BottleneckInfo> CachedBottlenecks { get; set; } = new List<BottleneckInfo>();

        // Configs (passed from main plugin)
        public ConfigEntry<bool> EnableMod { get; set; }
        public ConfigEntry<bool> AutoReplenishPackage { get; set; }
        public ConfigEntry<bool> AutoCleanInventory { get; set; }
        public ConfigEntry<bool> AutoSpray { get; set; }
        public ConfigEntry<bool> CostProliferator { get; set; }
        public ConfigEntry<ProliferatorSelection> ProliferatorSelectionEntry { get; set; }
        public ConfigEntry<bool> UseStorage { get; set; }
        public ConfigEntry<bool> AutoReplenishTPPFuel { get; set; }
        public ConfigEntry<bool> AutoReplenishFPPFuel { get; set; }
        public ConfigEntry<int> FuelId { get; set; }
        public ConfigEntry<int> StarFuelId { get; set; }
        public ConfigEntry<bool> InfBuildings { get; set; }
        public ConfigEntry<bool> InfVeins { get; set; }
        public ConfigEntry<bool> InfItems { get; set; }
        public ConfigEntry<bool> InfSand { get; set; }
        public ConfigEntry<bool> InfAmmo { get; set; }
        public ConfigEntry<bool> InfFleet { get; set; }

        // Fuel Options (for UI display)
        public Dictionary<int, string> FuelOptions { get; set; } = new Dictionary<int, string>();
        public Dictionary<int, string> StarFuelOptions { get; set; } = new Dictionary<int, string>();
        public int SelectedFuelIndex { get; set; }
        public int SelectedStarFuelIndex { get; set; }

        private static readonly (string Name, double MinPoints, Color Color)[] proliferationTiers = {
            ("Mk 3", 4.0, new Color(0.6f, 0.7f, 1f)),
            ("Mk 2", 2.0, new Color(0.6f, 1f, 0.6f)),
            ("Mk 1", 1.0, new Color(1f, 0.75f, 0.5f)),
            ("None", 0.0, Color.grey)
        };

        public UIService(StorageService storageService, LogisticsEngine logisticsEngine)
        {
            _storageService = storageService;
            _logisticsEngine = logisticsEngine;
        }

        public void Initialize()
        {
            AllItemsForMarket = LDB.items.dataArray.Where(p => p != null && p.ID > 0).ToList();
            MarketItemsForGUI = AllItemsForMarket;
            LoadItemPrices();

            FuelOptions.Clear();
            FuelOptions.Add(0, "Auto");
            FuelOptions.Add(ItemIds.Coal, "Coal");
            FuelOptions.Add(ItemIds.Graphite, "Graphite");
            FuelOptions.Add(ItemIds.CrudeOil, "Crude Oil");
            FuelOptions.Add(ItemIds.RefinedOil, "Refined Oil");
            FuelOptions.Add(ItemIds.Hydrogen, "Hydrogen");
            FuelOptions.Add(ItemIds.HydrogenFuelRod, "Hydrogen Fuel Rod");
            FuelOptions.Add(ItemIds.FireIce, "Fire Ice");
            FuelOptions.Add(ItemIds.EnergyShard, "Energy Shard");
            FuelOptions.Add(ItemIds.CombustionUnit, "Combustion Unit");
            FuelOptions.Add(ItemIds.Wood, "Wood");
            FuelOptions.Add(ItemIds.PlantFuel, "PlantFuel");
            
            if (FuelId != null)
                SelectedFuelIndex = FuelOptions.Keys.ToList().FindIndex(id => id == FuelId.Value);

            StarFuelOptions.Clear();
            StarFuelOptions.Add(0, "Auto");
            StarFuelOptions.Add(ItemIds.StrangeAnnihilationFuelRod, "Strange Rod");
            StarFuelOptions.Add(ItemIds.AntimatterFuelRod, "Antimatter Rod");
            
            if (StarFuelId != null)
                SelectedStarFuelIndex = StarFuelOptions.Keys.ToList().FindIndex(id => id == StarFuelId.Value);
        }

        private void LoadItemPrices()
        {
            // Simplified price loading for the refactor
            // In a real scenario, we'd use the same file logic as before
            ItemPrices = GenerateDefaultPrices();
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

        public void OnGUI()
        {
            if (!guiStylesInitialized)
            {
                InitializeGUIStyles();
                guiStylesInitialized = true;
            }

            if (ShowGUI)
            {
                windowRect = GUI.Window(0, windowRect, WindowFunction, "Nexus Logistics", windowStyle);
                DrawWindowBorder(windowRect);
            }
            if (ShowStorageGUI)
            {
                storageWindowRect = GUI.Window(1, storageWindowRect, StorageWindowFunction, "Logistics", windowStyle);
                DrawWindowBorder(storageWindowRect);
            }

            // Prevent click-through to the game world when GUI is active.
            if ((ShowGUI && windowRect.Contains(Event.current.mousePosition)) || (ShowStorageGUI && storageWindowRect.Contains(Event.current.mousePosition)))
            {
                Input.ResetInputAxes();
            }
        }

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

        private void DrawWindowBorder(Rect rect)
        {
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1), borderTexture); // Top
            GUI.DrawTexture(new Rect(rect.x, rect.y + rect.height - 1, rect.width, 1), borderTexture); // Bottom
            GUI.DrawTexture(new Rect(rect.x, rect.y, 1, rect.height), borderTexture); // Left
            GUI.DrawTexture(new Rect(rect.x + rect.width - 1, rect.y, 1, rect.height), borderTexture); // Right
        }

        private void WindowFunction(int windowID)
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

        private void StorageWindowFunction(int windowID)
        {
            string[] categories = { "Dashboard", "Storage", "Market", "Contracts" };
            var newCategory = (StorageCategory)GUILayout.Toolbar((int)selectedStorageCategory, categories, toolbarStyle);

            if (newCategory != selectedStorageCategory)
            {
                selectedStorageCategory = newCategory;
                // Reset sub-tabs when changing main tabs for a clean state
                if (selectedStorageCategory == StorageCategory.Market || selectedStorageCategory == StorageCategory.Storage)
                {
                    selectedItemCategory = ItemCategory.RawResources;
                }
            }

            switch (selectedStorageCategory)
            {
                case StorageCategory.Dashboard: DashboardPanel(); break;
                case StorageCategory.Storage: StoragePanel(); break;
                case StorageCategory.Market: MarketPanel(); break;
                case StorageCategory.Contracts: ContractsPanel(); break;
            }
            GUI.DragWindow();
        }

        private void MainPanel()
        {
            mainPanelScrollPosition = GUILayout.BeginScrollView(mainPanelScrollPosition, false, true, GUI.skin.horizontalScrollbar, GUI.skin.verticalScrollbar, scrollViewStyle);
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            
            if (EnableMod != null) EnableMod.Value = GUILayout.Toggle(EnableMod.Value, "Enable MOD", toggleStyle);
            if (AutoReplenishPackage != null) AutoReplenishPackage.Value = GUILayout.Toggle(AutoReplenishPackage.Value, "Auto Replenish Filtered Items", toggleStyle);
            if (AutoCleanInventory != null) AutoCleanInventory.Value = GUILayout.Toggle(AutoCleanInventory.Value, "Auto Clean Inventory to Logistic Slots", toggleStyle);

            GUILayout.Space(15);
            GUILayout.BeginHorizontal();
            if (AutoSpray != null) AutoSpray.Value = GUILayout.Toggle(AutoSpray.Value, "Auto Spray", toggleStyle);
            if (AutoSpray != null && AutoSpray.Value && CostProliferator != null)
            {
                CostProliferator.Value = GUILayout.Toggle(CostProliferator.Value, "Consume Proliferator", toggleStyle);
            }
            GUILayout.EndHorizontal();

            if (AutoSpray != null && AutoSpray.Value && ProliferatorSelectionEntry != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Proliferator Tier:", labelStyle, GUILayout.Width(120));
                ProliferatorSelectionEntry.Value = (ProliferatorSelection)GUILayout.Toolbar((int)ProliferatorSelectionEntry.Value, new string[] { "All", "MK.I", "MK.II", "MK.III" }, toolbarStyle);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(15);
            if (UseStorage != null) UseStorage.Value = GUILayout.Toggle(UseStorage.Value, "Recover from storage boxes/tanks", toggleStyle);

            GUILayout.Space(15);
            if (AutoReplenishTPPFuel != null) AutoReplenishTPPFuel.Value = GUILayout.Toggle(AutoReplenishTPPFuel.Value, "Auto-refuel Thermal Power Plants", toggleStyle);
            if (AutoReplenishTPPFuel != null && AutoReplenishTPPFuel.Value && FuelOptions.Count > 0)
            {
                SelectedFuelIndex = GUILayout.SelectionGrid(SelectedFuelIndex, FuelOptions.Values.ToArray(), 3, toggleStyle);
                if (FuelId != null) FuelId.Value = FuelOptions.Keys.ToArray()[SelectedFuelIndex];
            }
            
            if (AutoReplenishFPPFuel != null) AutoReplenishFPPFuel.Value = GUILayout.Toggle(AutoReplenishFPPFuel.Value, "Auto-refuel Fusion Power Plants", toggleStyle);
            if (AutoReplenishFPPFuel != null && AutoReplenishFPPFuel.Value && StarFuelOptions.Count > 0)
            {
                GUILayout.Label("Artificial Star Fuel:", labelStyle);
                SelectedStarFuelIndex = GUILayout.SelectionGrid(SelectedStarFuelIndex, StarFuelOptions.Values.ToArray(), 3, toggleStyle);
                if (StarFuelId != null) StarFuelId.Value = StarFuelOptions.Keys.ToArray()[SelectedStarFuelIndex];
            }

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void ItemPanel()
        {
            GUILayout.BeginVertical();
            if (InfBuildings != null) InfBuildings.Value = GUILayout.Toggle(InfBuildings.Value, "Infinite Buildings", toggleStyle);
            if (InfVeins != null) InfVeins.Value = GUILayout.Toggle(InfVeins.Value, "Infinite Minerals", toggleStyle);
            if (InfItems != null) InfItems.Value = GUILayout.Toggle(InfItems.Value, "Infinite Items (Disables Achievements)", toggleStyle);
            if (InfSand != null) InfSand.Value = GUILayout.Toggle(InfSand.Value, "Infinite Soil Pile", toggleStyle);
            GUILayout.EndVertical();
        }

        private void FightPanel()
        {
            GUILayout.BeginVertical();
            if (InfAmmo != null) InfAmmo.Value = GUILayout.Toggle(InfAmmo.Value, "Infinite Ammo", toggleStyle);
            if (InfFleet != null) InfFleet.Value = GUILayout.Toggle(InfFleet.Value, "Infinite Fleet", toggleStyle);
            GUILayout.Space(15);
            if (GUILayout.Button(new GUIContent("Clear Banned Items from Battle Bases", "Removes items from Battlefield Analysis Bases that you have marked not to be picked up."), buttonStyle, GUILayout.ExpandWidth(true)))
            {
                // This action should probably be handled by a service, but for now we'll leave it as a placeholder
                // or call a method on the main plugin if we have a reference.
            }
            GUILayout.EndVertical();
        }

        private void DashboardPanel()
        {
            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);
            GUILayout.BeginVertical();

            // Bottlenecks Section
            GUILayout.Label("Bottlenecks", windowStyle);
            if (CachedBottlenecks != null && CachedBottlenecks.Any())
            {
                foreach (var bottleneck in CachedBottlenecks)
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

        private void StoragePanel()
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
                foreach (var pair in StorageItemsForGUI)
                {
                    int itemId = pair.Key;
                    RemoteStorageItem item = pair.Value;
                    ItemProto itemProto = LDB.items.Select(itemId);
                    if (itemProto == null) continue;
                    
                    if (GetItemCategory(itemProto) != selectedItemCategory) continue;

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
                            _storageService.SetItemLimit(itemId, newLimit);
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

        private void MarketPanel()
        {
            // Sub-tab toolbar for Main/Orders
            string[] marketSubTabs = { "Main", "Orders" };
            marketSubTab = GUILayout.Toolbar(marketSubTab, marketSubTabs, toolbarStyle);
            GUILayout.Space(5);

            // Item category sub-tabs
            string[] itemCategories = { "Raw", "Intermeds", "Buildings", "Combat", "Science" };
            selectedItemCategory = (ItemCategory)GUILayout.Toolbar((int)selectedItemCategory, itemCategories, toolbarStyle);
            GUILayout.Space(10);

            // Balance display
            GUILayout.Label($"Balance: ${_logisticsEngine.PlayerBalance:N0}", labelStyle);
            GUILayout.Space(10);

            if (marketSubTab == 0) // 0 for Main
            {
                MarketMainPanel();
            }
            else if (marketSubTab == 1) // 1 for Orders
            {
                MarketOrdersPanel();
            }
        }

        private void MarketMainPanel()
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

            foreach (var itemProto in MarketItemsForGUI)
            {
                if (GetItemCategory(itemProto) != selectedItemCategory) continue;
                
                if (!ItemPrices.TryGetValue(itemProto.ID, out long basePrice)) continue;

                long sellPrice = basePrice / 5;
                int currentStock = _storageService.GetItemCount(itemProto.ID);

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

        private void MarketOrdersPanel()
        {
            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item", labelStyle, GUILayout.Width(150));
            GUILayout.Label("In Storage", labelStyle, GUILayout.Width(100));
            GUILayout.Label("Buy Below", labelStyle, GUILayout.Width(100));
            GUILayout.Label("Sell Above", labelStyle, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);

            foreach (var itemProto in AllItemsForMarket)
            {
                if (GetItemCategory(itemProto) != selectedItemCategory) continue;
                if (!_logisticsEngine.UnlockedItems.Contains(itemProto.ID)) continue;

                int itemId = itemProto.ID;
                _storageService.TryGetMarketOrder(itemId, out MarketOrder order);

                // Initialize input strings if they don't exist
                if (!buyThresholdInputs.ContainsKey(itemId)) buyThresholdInputs[itemId] = order?.BuyThreshold.ToString() ?? "0";
                if (!sellThresholdInputs.ContainsKey(itemId)) sellThresholdInputs[itemId] = order?.SellThreshold.ToString() ?? "0";

                int currentStock = _storageService.GetItemCount(itemId);

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
                    _storageService.SetMarketOrder(itemId, buyThreshold, sellThreshold);
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
        }

        private void ContractsPanel()
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);

            // --- Total Income Display ---
            long totalIncome = (_logisticsEngine.TradeRoutesTier1 * 1000L) + (_logisticsEngine.TradeRoutesTier2 * 12500L) + (_logisticsEngine.TradeRoutesTier3 * 150000L);
            GUILayout.Label($"Total Passive Income: ${totalIncome:N0} / second", windowStyle);
            GUILayout.Label($"Current Balance: ${_logisticsEngine.PlayerBalance:N0}", labelStyle);

            GUILayout.Space(20);

            storageScrollPosition = GUILayout.BeginScrollView(storageScrollPosition, scrollViewStyle);

            // --- Tier 1 ---
            GUILayout.Label("Planetary Trade Route", labelStyle);
            GUILayout.Label($"  Cost: ${10000000:N0}", labelStyle);
            GUILayout.Label($"  Income: ${1000:N0} / second", labelStyle);
            GUILayout.Label($"  Owned: {_logisticsEngine.TradeRoutesTier1}", labelStyle);
            if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(100)))
            {
                if (_logisticsEngine.PlayerBalance >= 10000000)
                {
                    _logisticsEngine.PlayerBalance -= 10000000;
                    _logisticsEngine.TradeRoutesTier1++;
                }
            }
            GUILayout.Space(15);

            // --- Tier 2 ---
            GUILayout.Label("Interstellar Trade Route", labelStyle);
            GUILayout.Label($"  Cost: ${100000000:N0}", labelStyle);
            GUILayout.Label($"  Income: ${12500:N0} / second", labelStyle);
            GUILayout.Label($"  Owned: {_logisticsEngine.TradeRoutesTier2}", labelStyle);
            if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(100)))
            {
                if (_logisticsEngine.PlayerBalance >= 100000000)
                {
                    _logisticsEngine.PlayerBalance -= 100000000;
                    _logisticsEngine.TradeRoutesTier2++;
                }
            }
            GUILayout.Space(15);

            // --- Tier 3 ---
            GUILayout.Label("Galactic Trade Route", labelStyle);
            GUILayout.Label($"  Cost: ${1000000000:N0}", labelStyle);
            GUILayout.Label($"  Income: ${150000:N0} / second", labelStyle);
            GUILayout.Label($"  Owned: {_logisticsEngine.TradeRoutesTier3}", labelStyle);
            if (GUILayout.Button("Buy", buttonStyle, GUILayout.Width(100)))
            {
                if (_logisticsEngine.PlayerBalance >= 1000000000)
                {
                    _logisticsEngine.PlayerBalance -= 1000000000;
                    _logisticsEngine.TradeRoutesTier3++;
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void BuyItem(int itemId, long price)
        {
            if (!marketQuantityInputs.TryGetValue(itemId, out string input) || !int.TryParse(input, out int quantity) || quantity <= 0)
            {
                return; // Invalid quantity
            }

            long totalCost = price * quantity;
            int affordableQuantity = quantity;

            if (totalCost > _logisticsEngine.PlayerBalance)
            {
                affordableQuantity = (int)(_logisticsEngine.PlayerBalance / price);
            }

            if (affordableQuantity <= 0)
            {
                return; // Can't afford any
            }

            long finalCost = price * affordableQuantity;
            _logisticsEngine.PlayerBalance -= finalCost;
            _logisticsEngine.AddItem(itemId, affordableQuantity, 0, true); // Bypass storage limit for purchases
        }

        private void SellItem(int itemId, long price)
        {
            if (!marketQuantityInputs.TryGetValue(itemId, out string input) || !int.TryParse(input, out int quantity) || quantity <= 0)
            {
                return; // Invalid quantity
            }

            int[] takenItems = _logisticsEngine.TakeItem(itemId, quantity);
            int soldQuantity = takenItems[0];

            if (soldQuantity > 0)
            {
                _logisticsEngine.PlayerBalance += price * soldQuantity;
            }
        }

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

        private ItemCategory GetItemCategory(ItemProto itemProto)
        {
            if (itemProto == null) return ItemCategory.IntermediateProducts;
            if (itemProto.ID >= 6001 && itemProto.ID <= 6006) return ItemCategory.ScienceMatrices;
            if (itemProto.isAmmo || itemProto.isFighter) return ItemCategory.AmmunitionAndCombat;
            if (itemProto.CanBuild) return ItemCategory.BuildingsAndVehicles;
            if (IsVein(itemProto.ID)) return ItemCategory.RawResources;
            return ItemCategory.IntermediateProducts;
        }

        private bool IsVein(int itemId)
        {
            int[] items = { ItemIds.Water, ItemIds.SulfuricAcid, ItemIds.Hydrogen, ItemIds.Deuterium };
            return items.Contains(itemId) || LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None;
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
    }

    public struct BottleneckInfo
    {
        public int ItemId;
        public int DeficitPerMinute;
        public int CurrentStock;
    }
}
