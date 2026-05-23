using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BepInEx.Configuration;
using NexusLogistics.UI;

namespace NexusLogistics
{
    public class UIService
    {
        private readonly StorageService _storageService;
        private readonly LogisticsEngine _logisticsEngine;
        private UINexusLogisticsWindow _window;

        // GUI State properties with custom getters/setters for backwards compatibility
        public bool ShowGUI
        {
            get { return _window != null && _window.gameObject.activeSelf && _window.ActiveTabIdx == 3; }
            set
            {
                if (value)
                {
                    ToggleWindow(3);
                }
                else if (_window != null && _window.ActiveTabIdx == 3)
                {
                    _window.Close();
                }
            }
        }

        public bool ShowStorageGUI
        {
            get { return _window != null && _window.gameObject.activeSelf && _window.ActiveTabIdx != 3; }
            set
            {
                if (value)
                {
                    ToggleWindow(1);
                }
                else if (_window != null && _window.ActiveTabIdx != 3)
                {
                    _window.Close();
                }
            }
        }

        public enum ProliferatorSelection { All, Mk1, Mk2, Mk3 }
        public enum ItemCategory { RawResources, IntermediateProducts, BuildingsAndVehicles, AmmunitionAndCombat, ScienceMatrices }

        private readonly Dictionary<int, string> marketQuantityInputs = new Dictionary<int, string>();

        // Data for GUI (updated from main plugin or engine)
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

        public StorageService StorageService => _storageService;
        public LogisticsEngine LogisticsEngine => _logisticsEngine;

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
            ItemPrices = GenerateDefaultPrices();
        }

        private Dictionary<int, long> GenerateDefaultPrices()
        {
            var prices = new Dictionary<int, long>
            {
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

            var queue = new Queue<int>(itemInDegree.Where(p => p.Value == 0).Select(p => p.Key));

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
                        currentPrice = 0;
                        break;
                    }
                }

                if (currentPrice > 0)
                {
                    double premium = recipe.Items.Length <= 2 ? 0.6 : (recipe.Items.Length <= 4 ? 0.8 : 1.0);
                    prices[itemIdToPrice] = currentPrice + (long)(currentPrice * premium);

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

        public void ToggleWindow(int tabIdx = 0)
        {
            if (_window == null)
            {
                _window = MyWindowManager.CreateWindow<UINexusLogisticsWindow>("UINexusLogisticsWindow", "Nexus Logistics");
                if (_window != null)
                {
                    _window.Init(this);
                }
            }

            if (_window != null)
            {
                if (_window.gameObject.activeSelf && _window.ActiveTabIdx == tabIdx)
                {
                    _window.Close();
                }
                else
                {
                    _window.Open();
                    _window.SetCurrentTab(tabIdx);
                }
            }
        }

        public bool IsWindowOpen()
        {
            return _window != null && _window.gameObject.activeSelf;
        }

        public void BuyItemPublic(int itemId, long price)
        {
            BuyItem(itemId, price);
        }

        public void SellItemPublic(int itemId, long price)
        {
            SellItem(itemId, price);
        }

        public void SetMarketQuantityInput(int itemId, string value)
        {
            marketQuantityInputs[itemId] = value;
        }

        private void BuyItem(int itemId, long price)
        {
            if (!marketQuantityInputs.TryGetValue(itemId, out string input) || !int.TryParse(input, out int quantity) || quantity <= 0)
            {
                return;
            }

            long totalCost = price * quantity;
            int affordableQuantity = quantity;

            if (totalCost > _logisticsEngine.PlayerBalance)
            {
                affordableQuantity = (int)(_logisticsEngine.PlayerBalance / price);
            }

            if (affordableQuantity <= 0)
            {
                return;
            }

            long finalCost = price * affordableQuantity;
            _logisticsEngine.PlayerBalance -= finalCost;
            _logisticsEngine.AddItem(itemId, affordableQuantity, 0, true);
        }

        private void SellItem(int itemId, long price)
        {
            if (!marketQuantityInputs.TryGetValue(itemId, out string input) || !int.TryParse(input, out int quantity) || quantity <= 0)
            {
                return;
            }

            int[] takenItems = _logisticsEngine.TakeItem(itemId, quantity);
            int soldQuantity = takenItems[0];

            if (soldQuantity > 0)
            {
                _logisticsEngine.PlayerBalance += price * soldQuantity;
            }
        }

        public void UpdateBottlenecks()
        {
            if (GameMain.data == null || GameMain.data.statistics == null || GameMain.data.statistics.production == null)
            {
                return;
            }

            var prodStats = GameMain.data.statistics.production;
            if (prodStats.factoryStatPool == null) return;

            var productionSpeeds = new Dictionary<int, float>();
            var consumptionSpeeds = new Dictionary<int, float>();

            foreach (var factoryStat in prodStats.factoryStatPool)
            {
                if (factoryStat == null) continue;
                for (int i = 0; i < factoryStat.productCursor; i++)
                {
                    var product = factoryStat.productPool[i];
                    if (product != null && product.itemId > 0)
                    {
                        int itemId = product.itemId;
                        float pSpeed = product.refProductSpeed * 60f;
                        float cSpeed = product.refConsumeSpeed * 60f;

                        if (productionSpeeds.ContainsKey(itemId))
                            productionSpeeds[itemId] += pSpeed;
                        else
                            productionSpeeds[itemId] = pSpeed;

                        if (consumptionSpeeds.ContainsKey(itemId))
                            consumptionSpeeds[itemId] += cSpeed;
                        else
                            consumptionSpeeds[itemId] = cSpeed;
                    }
                }
            }

            CachedBottlenecks.Clear();

            foreach (var itemId in consumptionSpeeds.Keys)
            {
                float prodSpeed = productionSpeeds.ContainsKey(itemId) ? productionSpeeds[itemId] : 0f;
                float consSpeed = consumptionSpeeds[itemId];

                if (consSpeed > prodSpeed)
                {
                    int deficit = (int)Math.Round(prodSpeed - consSpeed);
                    if (deficit < 0)
                    {
                        int stock = _storageService.GetItemCount(itemId);
                        CachedBottlenecks.Add(new BottleneckInfo
                        {
                            ItemId = itemId,
                            DeficitPerMinute = deficit,
                            CurrentStock = stock
                        });
                    }
                }
            }
        }

        public (string text, Color color) GetProliferationStatus(int count, int inc, int itemId)
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
