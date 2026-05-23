using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using BepInEx.Configuration;
using NexusLogistics.UI;

namespace NexusLogistics.UI
{
    public class UINexusLogisticsWindow : MyWindowWithTabs
    {
        private UIService _uiService;
        private StorageService _storageService;
        private LogisticsEngine _logisticsEngine;

        // Tab Panels
        private RectTransform _dashboardPanel;
        private RectTransform _storagePanel;
        private RectTransform _marketPanel;
        private RectTransform _optionsPanel;

        // Category/Sub-tab Buttons
        private readonly List<UIButton> _storageCategoryButtons = new List<UIButton>();
        private readonly List<UIButton> _marketSubTabButtons = new List<UIButton>();
        private readonly List<UIButton> _marketCategoryButtons = new List<UIButton>();

        // Active selection states
        private int _storageCategoryIdx = 0;
        private int _marketSubTabIdx = 0;
        private int _marketCategoryIdx = 0;

        // Scroll contents & ScrollView GameObjects
        private Transform _dashboardScrollContent;
        private Transform _storageScrollContent;
        private Transform _marketScrollContent;

        private GameObject _dashboardScrollViewGo;
        private GameObject _storageScrollViewGo;
        private GameObject _marketScrollViewGo;

        // Headers
        private GameObject _marketHeadersGo;
        private GameObject _ordersHeadersGo;

        // Trade Routes elements
        private GameObject _routesPanelGo;
        private Text _routesPassiveIncomeText;
        private Text _routesBalanceText;
        private Text _routesT1CountText;
        private Text _routesT2CountText;
        private Text _routesT3CountText;

        // Balance text in Market
        private Text _marketBalanceText;

        // Row components caching to avoid GC stutter
        private class StorageRow
        {
            public GameObject rowGo;
            public Text countText;
            public Text prolifText;
            public InputField limitInput;
        }

        private class MarketRow
        {
            public GameObject rowGo;
            public Text countText;
            public InputField quantityInput;
        }

        private class OrderRow
        {
            public GameObject rowGo;
            public Text countText;
            public InputField buyBelowInput;
            public InputField sellAboveInput;
        }

        private class DashboardRow
        {
            public GameObject rowGo;
            public Text descText;
        }

        private readonly Dictionary<int, StorageRow> _cachedStorageRows = new Dictionary<int, StorageRow>();
        private readonly Dictionary<int, MarketRow> _cachedMarketRows = new Dictionary<int, MarketRow>();
        private readonly Dictionary<int, OrderRow> _cachedOrderRows = new Dictionary<int, OrderRow>();
        private readonly List<DashboardRow> _cachedDashboardRows = new List<DashboardRow>();

        // Update timer
        private float _updateTimer = 0f;
        private const float UpdateInterval = 0.25f;

        public void Init(UIService uiService)
        {
            _uiService = uiService;
            _storageService = uiService.StorageService;
            _logisticsEngine = uiService.LogisticsEngine;

            BuildUI();
        }

        private void BuildUI()
        {
            var windowRt = GetComponent<RectTransform>();

            // Force set standard window size
            windowRt.sizeDelta = new Vector2(800f, 620f);

            // Add tab panel areas
            _dashboardPanel = AddTab(windowRt, "Dashboard");
            _storagePanel = AddTab(windowRt, "Storage");
            _marketPanel = AddTab(windowRt, "Market");
            _optionsPanel = AddTab(windowRt, "Options");

            // Build individual panels
            BuildDashboardPanel(_dashboardPanel);
            BuildStoragePanel(_storagePanel);
            BuildMarketPanel(_marketPanel);
            BuildOptionsPanel(_optionsPanel);

            // Set current tab to Dashboard
            SetCurrentTab(0);
        }

        #region Panel Builders
        private void BuildDashboardPanel(RectTransform panel)
        {
            // Title
            var title = CreateText(10f, 10f, 400f, 24f, panel, "Logistics Bottlenecks & Deficits", 16, "title");
            title.fontStyle = FontStyle.Bold;
            title.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            // Scroll View
            var scroll = CreateScrollView(panel, 10f, 40f, 605f, 502f);
            _dashboardScrollContent = scroll.content;
            _dashboardScrollViewGo = scroll.gameObject;
        }

        private void BuildStoragePanel(RectTransform panel)
        {
            // Category Buttons
            string[] categoryNames = { "Raw", "Intermediates", "Buildings", "Combat", "Science" };
            for (int i = 0; i < categoryNames.Length; i++)
            {
                int idx = i;
                var btn = CreateTabButton(10f + i * 115f, 10f, 110f, 26f, panel, categoryNames[i], 13, () => {
                    SelectStorageCategory(idx);
                });
                _storageCategoryButtons.Add(btn);
            }

            // Headers
            BuildStorageHeaders(panel);

            // Scroll View
            var scroll = CreateScrollView(panel, 10f, 70f, 605f, 472f);
            _storageScrollContent = scroll.content;
            _storageScrollViewGo = scroll.gameObject;
        }

        private void BuildStorageHeaders(RectTransform parent)
        {
            var headerGo = new GameObject("StorageHeaders", typeof(RectTransform));
            headerGo.transform.SetParent(parent, false);
            var rect = headerGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(590f, 20f);
            Util.NormalizeRectWithTopLeft(rect, 10f, 40f, parent);

            CreateText(10f, 0f, 180f, 20f, rect, "Item", 13, "h_item").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(220f, 0f, 100f, 20f, rect, "Stock", 13, "h_stock").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(320f, 0f, 120f, 20f, rect, "Proliferation", 13, "h_prolif").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(470f, 0f, 120f, 20f, rect, "Storage Limit", 13, "h_limit").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
        }

        private void BuildMarketPanel(RectTransform panel)
        {
            // Sub-tabs (narrowed to 115 width with 120 spacing to make space for balance)
            string[] subTabNames = { "Manual Trading", "Auto Orders", "Trade Routes" };
            for (int i = 0; i < subTabNames.Length; i++)
            {
                int idx = i;
                var btn = CreateTabButton(10f + i * 120f, 10f, 115f, 26f, panel, subTabNames[i], 13, () => {
                    SelectMarketSubTab(idx);
                });
                _marketSubTabButtons.Add(btn);
            }

            // Category Buttons
            string[] categoryNames = { "Raw", "Intermediates", "Buildings", "Combat", "Science" };
            for (int i = 0; i < categoryNames.Length; i++)
            {
                int idx = i;
                var btn = CreateTabButton(10f + i * 115f, 40f, 110f, 26f, panel, categoryNames[i], 13, () => {
                    SelectMarketCategory(idx);
                });
                _marketCategoryButtons.Add(btn);
            }

            // Balance Display (repositioned to top row y = 10f, right-aligned using NormalizeRectWithTopRight with width 170 to prevent layout issues)
            _marketBalanceText = CreateText(0f, 0f, 170f, 26f, panel, "Nexus Credits: $0", 14, "balance");
            Util.NormalizeRectWithTopRight(_marketBalanceText.rectTransform, 110f, 10f, panel);
            _marketBalanceText.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            _marketBalanceText.alignment = TextAnchor.MiddleRight;

            // Headers
            BuildMarketHeaders(panel);

            // Scroll View
            var scroll = CreateScrollView(panel, 10f, 100f, 605f, 442f);
            _marketScrollContent = scroll.content;
            _marketScrollViewGo = scroll.gameObject;

            // Routes Panel
            BuildRoutesPanel(panel);
        }

        private void BuildMarketHeaders(RectTransform parent)
        {
            // Trading headers
            _marketHeadersGo = new GameObject("TradingHeaders", typeof(RectTransform));
            _marketHeadersGo.transform.SetParent(parent, false);
            var rectT = _marketHeadersGo.GetComponent<RectTransform>();
            rectT.sizeDelta = new Vector2(590f, 20f);
            Util.NormalizeRectWithTopLeft(rectT, 10f, 75f, parent);

            CreateText(10f, 0f, 160f, 20f, rectT, "Item", 13, "h_item").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(180f, 0f, 70f, 20f, rectT, "In Storage", 13, "h_stock").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(260f, 0f, 60f, 20f, rectT, "Buy Price", 13, "h_buy").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(320f, 0f, 60f, 20f, rectT, "Sell Price", 13, "h_sell").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(390f, 0f, 60f, 20f, rectT, "Quantity", 13, "h_qty").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(460f, 0f, 115f, 20f, rectT, "Actions", 13, "h_act").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            // Auto Orders headers
            _ordersHeadersGo = new GameObject("OrdersHeaders", typeof(RectTransform));
            _ordersHeadersGo.transform.SetParent(parent, false);
            var rectO = _ordersHeadersGo.GetComponent<RectTransform>();
            rectO.sizeDelta = new Vector2(590f, 20f);
            Util.NormalizeRectWithTopLeft(rectO, 10f, 75f, parent);

            CreateText(10f, 0f, 160f, 20f, rectO, "Item", 13, "h_item").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(180f, 0f, 70f, 20f, rectO, "In Storage", 13, "h_stock").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(260f, 0f, 150f, 20f, rectO, "Buy Below", 13, "h_buy").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            CreateText(420f, 0f, 150f, 20f, rectO, "Sell Above", 13, "h_sell").color = new Color(0.3f, 0.8f, 1.0f, 1.0f);
            
            _ordersHeadersGo.SetActive(false);
        }

        private void BuildRoutesPanel(RectTransform parent)
        {
            _routesPanelGo = new GameObject("RoutesPanel", typeof(RectTransform));
            _routesPanelGo.transform.SetParent(parent, false);
            var rect = _routesPanelGo.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(605f, 502f);
            Util.NormalizeRectWithTopLeft(rect, 10f, 40f, parent);
            _routesPanelGo.SetActive(false);

            // Title / Income Text
            _routesPassiveIncomeText = CreateText(10f, 10f, 600f, 24f, rect, "Total Passive Income: $0 / second", 16, "income_txt");
            _routesPassiveIncomeText.fontStyle = FontStyle.Bold;
            _routesPassiveIncomeText.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            _routesBalanceText = CreateText(10f, 35f, 600f, 20f, rect, "Current Balance: $0", 14, "bal_txt");
            _routesBalanceText.color = Color.white;

            // Tier 1 Planetary Route Card
            BuildRouteCard(rect, 80f, "Planetary Trade Route", 10000000L, 1000L,
                (txt) => _routesT1CountText = txt,
                () => {
                    if (_logisticsEngine.PlayerBalance >= 10000000)
                    {
                        _logisticsEngine.PlayerBalance -= 10000000;
                        _logisticsEngine.TradeRoutesTier1++;
                        UpdateRoutesPanel();
                    }
                });

            // Tier 2 Interstellar Route Card
            BuildRouteCard(rect, 140f, "Interstellar Trade Route", 100000000L, 12500L,
                (txt) => _routesT2CountText = txt,
                () => {
                    if (_logisticsEngine.PlayerBalance >= 100000000)
                    {
                        _logisticsEngine.PlayerBalance -= 100000000;
                        _logisticsEngine.TradeRoutesTier2++;
                        UpdateRoutesPanel();
                    }
                });

            // Tier 3 Galactic Route Card
            BuildRouteCard(rect, 200f, "Galactic Trade Route", 1000000000L, 150000L,
                (txt) => _routesT3CountText = txt,
                () => {
                    if (_logisticsEngine.PlayerBalance >= 1000000000)
                    {
                        _logisticsEngine.PlayerBalance -= 1000000000;
                        _logisticsEngine.TradeRoutesTier3++;
                        UpdateRoutesPanel();
                    }
                });
        }

        private void BuildRouteCard(RectTransform parent, float y, string title, long cost, long income, Action<Text> registerText, UnityEngine.Events.UnityAction buyAction)
        {
            var desc = CreateText(15f, y, 330f, 40f, parent, string.Format("{0}\n  Cost: ${1:N0} | Income: +${2:N0}/sec", title, cost, income), 14, "desc");
            desc.color = Color.white;

            var owned = CreateText(350f, y + 10f, 100f, 20f, parent, "Owned: 0", 14, "owned");
            owned.color = new Color(0.8f, 0.8f, 0.8f, 1f);
            registerText(owned);

            CreateRowButton(460f, y + 6f, 120f, 28f, parent, "Buy Route", 14, "buy_btn", buyAction);
        }

        private void BuildOptionsPanel(RectTransform parent)
        {
            // Column 1: Core Logistics & Spraying
            var coreHeader = CreateText(20f, 15f, 300f, 20f, parent, "Core Logistics Settings", 15, "h_core");
            coreHeader.fontStyle = FontStyle.Bold;
            coreHeader.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            AddCheckBox(20f, 40f, parent, _uiService.EnableMod, "Enable Nexus Logistics MOD");
            AddCheckBox(20f, 70f, parent, _uiService.AutoReplenishPackage, "Auto Replenish filtered items in player backpack");
            AddCheckBox(20f, 100f, parent, _uiService.AutoCleanInventory, "Auto Clean inventory to active logistic slots");
            AddCheckBox(20f, 130f, parent, _uiService.UseStorage, "Recover items from world storage boxes & liquid tanks");

            var sprayHeader = CreateText(20f, 175f, 300f, 20f, parent, "Auto Spraying Options", 15, "h_spray");
            sprayHeader.fontStyle = FontStyle.Bold;
            sprayHeader.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            AddCheckBox(20f, 200f, parent, _uiService.AutoSpray, "Enable Automatic Item Spraying");
            AddCheckBox(20f, 230f, parent, _uiService.CostProliferator, "Consume Proliferator during spraying");

            CreateText(20f, 260f, 300f, 20f, parent, "Allowed Proliferator Tier Limit:", 14, "lbl_prolif_limit").color = Color.white;
            var prolifCombo = AddComboBox(20f, 285f, parent);
            prolifCombo.SetItems("All Tiers", "MK.I Only", "MK.II Only", "MK.III Only");
            prolifCombo.SetIndex((int)_uiService.ProliferatorSelectionEntry.Value);
            prolifCombo.AddOnSelChanged(idx => {
                _uiService.ProliferatorSelectionEntry.Value = (UIService.ProliferatorSelection)idx;
            });

            // Column 2: Auto-refuel & Sandbox Cheats
            var refuelHeader = CreateText(350f, 15f, 300f, 20f, parent, "Auto-refuel Power Plants", 15, "h_refuel");
            refuelHeader.fontStyle = FontStyle.Bold;
            refuelHeader.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            AddCheckBox(350f, 40f, parent, _uiService.AutoReplenishTPPFuel, "Enable auto-refuel for Thermal Plants");
            CreateText(350f, 70f, 300f, 20f, parent, "Thermal Power Plant Fuel selection:", 13, "lbl_fuel").color = Color.white;
            
            var thermalCombo = AddComboBox(350f, 95f, parent);
            var fuelIds = _uiService.FuelOptions.Keys.ToList();
            thermalCombo.SetItems(_uiService.FuelOptions.Values.ToArray());
            int curFuelIdx = fuelIds.IndexOf(_uiService.FuelId.Value);
            thermalCombo.SetIndex(curFuelIdx < 0 ? 0 : curFuelIdx);
            thermalCombo.AddOnSelChanged(idx => {
                if (idx >= 0 && idx < fuelIds.Count) _uiService.FuelId.Value = fuelIds[idx];
            });

            AddCheckBox(350f, 135f, parent, _uiService.AutoReplenishFPPFuel, "Enable auto-refuel for Fusion/Stars");
            CreateText(350f, 165f, 300f, 20f, parent, "Artificial Star Fuel selection:", 13, "lbl_star_fuel").color = Color.white;

            var starCombo = AddComboBox(350f, 190f, parent);
            var starFuelIds = _uiService.StarFuelOptions.Keys.ToList();
            starCombo.SetItems(_uiService.StarFuelOptions.Values.ToArray());
            int curStarFuelIdx = starFuelIds.IndexOf(_uiService.StarFuelId.Value);
            starCombo.SetIndex(curStarFuelIdx < 0 ? 0 : curStarFuelIdx);
            starCombo.AddOnSelChanged(idx => {
                if (idx >= 0 && idx < starFuelIds.Count) _uiService.StarFuelId.Value = starFuelIds[idx];
            });

            var cheatHeader = CreateText(350f, 235f, 300f, 20f, parent, "Infinite Settings (Sandbox Cheats)", 15, "h_cheats");
            cheatHeader.fontStyle = FontStyle.Bold;
            cheatHeader.color = new Color(0.3f, 0.8f, 1.0f, 1.0f);

            AddCheckBox(350f, 260f, parent, _uiService.InfBuildings, "Infinite Buildings placement");
            AddCheckBox(350f, 290f, parent, _uiService.InfVeins, "Infinite Veins and Minerals harvesting");
            AddCheckBox(350f, 320f, parent, _uiService.InfItems, "Infinite Storage Items");

            var warningText = CreateText(350f, 350f, 280f, 20f, parent, "* Warning: Infinite Items disables achievements in current run.", 12, "warning");
            warningText.color = new Color(0.9f, 0.3f, 0.2f, 1f);
            warningText.fontStyle = FontStyle.Bold;

            AddCheckBox(350f, 375f, parent, _uiService.InfSand, "Infinite Soil Pile (Soil cheat)");
            AddCheckBox(350f, 405f, parent, _uiService.InfAmmo, "Infinite Defense Ammo");
            AddCheckBox(350f, 435f, parent, _uiService.InfFleet, "Infinite Fleet replenishment");
        }
        #endregion

        #region Tab Handlers
        protected override void OnTabButtonClick(int index)
        {
            base.OnTabButtonClick(index);

            if (index == 1)
            {
                SelectStorageCategory(0);
            }
            else if (index == 2)
            {
                SelectMarketSubTab(0);
            }

            UpdateActivePanel(true);
        }

        private void SelectStorageCategory(int index)
        {
            _storageCategoryIdx = index;
            for (int i = 0; i < _storageCategoryButtons.Count; i++)
            {
                _storageCategoryButtons[i].highlighted = (i == index);
                _storageCategoryButtons[i].RefreshTransitionsImmediately();
            }

            foreach (var row in _cachedStorageRows.Values)
            {
                Destroy(row.rowGo);
            }
            _cachedStorageRows.Clear();

            UpdateStorageData();
        }

        private void SelectMarketSubTab(int index)
        {
            _marketSubTabIdx = index;
            for (int i = 0; i < _marketSubTabButtons.Count; i++)
            {
                _marketSubTabButtons[i].highlighted = (i == index);
                _marketSubTabButtons[i].RefreshTransitionsImmediately();
            }

            if (index == 2) // Trade Routes
            {
                for (int i = 0; i < _marketCategoryButtons.Count; i++)
                {
                    _marketCategoryButtons[i].gameObject.SetActive(false);
                }
                _marketBalanceText.gameObject.SetActive(false);
                _marketHeadersGo.SetActive(false);
                _ordersHeadersGo.SetActive(false);
                _marketScrollViewGo.SetActive(false);
                _routesPanelGo.SetActive(true);
            }
            else
            {
                for (int i = 0; i < _marketCategoryButtons.Count; i++)
                {
                    _marketCategoryButtons[i].gameObject.SetActive(true);
                }
                _marketBalanceText.gameObject.SetActive(true);
                _marketScrollViewGo.SetActive(true);
                _routesPanelGo.SetActive(false);

                _marketHeadersGo.SetActive(index == 0);
                _ordersHeadersGo.SetActive(index == 1);

                SelectMarketCategory(0);
            }
        }

        private void SelectMarketCategory(int index)
        {
            _marketCategoryIdx = index;
            for (int i = 0; i < _marketCategoryButtons.Count; i++)
            {
                _marketCategoryButtons[i].highlighted = (i == index);
                _marketCategoryButtons[i].RefreshTransitionsImmediately();
            }

            foreach (var row in _cachedMarketRows.Values) Destroy(row.rowGo);
            _cachedMarketRows.Clear();

            foreach (var row in _cachedOrderRows.Values) Destroy(row.rowGo);
            _cachedOrderRows.Clear();

            UpdateMarketData(true);
        }
        #endregion

        #region Data Updates
        private void UpdateActivePanel(bool forceRebuild = false)
        {
            if (ActiveTabIdx == 0)
            {
                UpdateDashboardData();
            }
            else if (ActiveTabIdx == 1)
            {
                UpdateStorageData();
            }
            else if (ActiveTabIdx == 2)
            {
                UpdateMarketData(forceRebuild);
            }
            else if (ActiveTabIdx == 3)
            {
                // Config entries automatically bind/update, so nothing needed here.
            }
        }

        private void UpdateDashboardData()
        {
            foreach (var row in _cachedDashboardRows)
            {
                Destroy(row.rowGo);
            }
            _cachedDashboardRows.Clear();

            if (_uiService.CachedBottlenecks == null || !_uiService.CachedBottlenecks.Any())
            {
                var rowGo = new GameObject("EmptyRow", typeof(RectTransform));
                var rect = rowGo.GetComponent<RectTransform>();
                rowGo.transform.SetParent(_dashboardScrollContent, false);
                NormalizeRowRect(rect, 25f);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = 25f;

                var txt = CreateText(10f, 2f, 600f, 20f, rect, "No logistics bottlenecks or deficits detected.", 14, "empty");
                txt.color = Color.gray;

                _cachedDashboardRows.Add(new DashboardRow { rowGo = rowGo, descText = txt });
                return;
            }

            foreach (var bottleneck in _uiService.CachedBottlenecks)
            {
                ItemProto item = LDB.items.Select(bottleneck.ItemId);
                if (item == null) continue;

                var rowGo = new GameObject("DeficitRow", typeof(RectTransform));
                var rect = rowGo.GetComponent<RectTransform>();
                rowGo.transform.SetParent(_dashboardScrollContent, false);
                NormalizeRowRect(rect, 36f);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = 36f;
                le.minHeight = 36f;

                AddItemIconAndName(10f, 0f, rect, item.iconSprite, item.name);

                string deficitText = string.Format("{0}/min deficit", Math.Abs(bottleneck.DeficitPerMinute));
                string etaText = "";
                if (bottleneck.DeficitPerMinute < 0)
                {
                    double minutesToDepletion = (double)bottleneck.CurrentStock / Math.Abs(bottleneck.DeficitPerMinute);
                    etaText = string.Format(" (ETA: {0})", FormatDuration(minutesToDepletion));
                }

                var label = string.Format("{0}{1}  [Current Stock: {2:N0}]", deficitText, etaText, bottleneck.CurrentStock);
                var txt = CreateText(240f, 8f, 380f, 20f, rect, label, 14, "deficit");
                txt.color = Color.red;

                _cachedDashboardRows.Add(new DashboardRow { rowGo = rowGo, descText = txt });
            }
        }

        private void UpdateStorageData()
        {
            if (_uiService.StorageItemsForGUI == null) return;

            UIService.ItemCategory targetCategory = (UIService.ItemCategory)_storageCategoryIdx;

            var filteredItems = _uiService.StorageItemsForGUI
                .Where(p => {
                    var proto = LDB.items.Select(p.Key);
                    return proto != null && GetItemCategory(proto) == targetCategory;
                })
                .ToList();

            var toRemove = _cachedStorageRows.Keys.Where(k => !filteredItems.Any(p => p.Key == k)).ToList();
            foreach (var key in toRemove)
            {
                Destroy(_cachedStorageRows[key].rowGo);
                _cachedStorageRows.Remove(key);
            }

            foreach (var pair in filteredItems)
            {
                int itemId = pair.Key;
                var item = pair.Value;
                var itemProto = LDB.items.Select(itemId);
                if (itemProto == null) continue;

                var (pText, pColor) = _uiService.GetProliferationStatus(item.count, item.inc, itemId);

                StorageRow row;
                if (!_cachedStorageRows.TryGetValue(itemId, out row))
                {
                    row = CreateStorageRow(itemId, itemProto, item.count, pText, pColor, item.limit);
                    _cachedStorageRows.Add(itemId, row);
                }
                else
                {
                    row.countText.text = item.count.ToString("N0");
                    row.prolifText.text = pText;
                    row.prolifText.color = pColor;
                    if (!row.limitInput.isFocused)
                    {
                        row.limitInput.text = item.limit.ToString();
                    }
                }
            }
        }

        private void UpdateMarketData(bool forceRebuild = false)
        {
            _marketBalanceText.text = string.Format("Nexus Credits: ${0:N0}", _logisticsEngine.PlayerBalance);

            if (_marketSubTabIdx == 0)
            {
                UpdateTradingList();
            }
            else if (_marketSubTabIdx == 1)
            {
                UpdateOrdersList();
            }
            else if (_marketSubTabIdx == 2)
            {
                UpdateRoutesPanel();
            }
        }

        private void UpdateTradingList()
        {
            UIService.ItemCategory targetCategory = (UIService.ItemCategory)_marketCategoryIdx;

            var items = _uiService.MarketItemsForGUI
                .Where(p => GetItemCategory(p) == targetCategory)
                .ToList();

            var toRemove = _cachedMarketRows.Keys.Where(k => !items.Any(p => p.ID == k)).ToList();
            foreach (var key in toRemove)
            {
                Destroy(_cachedMarketRows[key].rowGo);
                _cachedMarketRows.Remove(key);
            }

            foreach (var itemProto in items)
            {
                int itemId = itemProto.ID;
                int count = _storageService.GetItemCount(itemId);
                long price = _uiService.ItemPrices.TryGetValue(itemId, out long p) ? p : 10;

                MarketRow row;
                if (!_cachedMarketRows.TryGetValue(itemId, out row))
                {
                    row = CreateMarketRow(itemId, itemProto, count, price);
                    _cachedMarketRows.Add(itemId, row);
                }
                else
                {
                    row.countText.text = count.ToString("N0");
                }
            }
        }

        private void UpdateOrdersList()
        {
            UIService.ItemCategory targetCategory = (UIService.ItemCategory)_marketCategoryIdx;

            var items = _uiService.MarketItemsForGUI
                .Where(p => GetItemCategory(p) == targetCategory)
                .ToList();

            var toRemove = _cachedOrderRows.Keys.Where(k => !items.Any(p => p.ID == k)).ToList();
            foreach (var key in toRemove)
            {
                Destroy(_cachedOrderRows[key].rowGo);
                _cachedOrderRows.Remove(key);
            }

            foreach (var itemProto in items)
            {
                int itemId = itemProto.ID;
                int count = _storageService.GetItemCount(itemId);
                var order = _storageService.GetMarketOrder(itemId);

                OrderRow row;
                if (!_cachedOrderRows.TryGetValue(itemId, out row))
                {
                    row = CreateOrderRow(itemId, itemProto, count, order.BuyThreshold, order.SellThreshold);
                    _cachedOrderRows.Add(itemId, row);
                }
                else
                {
                    row.countText.text = count.ToString("N0");
                    if (!row.buyBelowInput.isFocused)
                        row.buyBelowInput.text = order.BuyThreshold.ToString();
                    if (!row.sellAboveInput.isFocused)
                        row.sellAboveInput.text = order.SellThreshold.ToString();
                }
            }
        }

        private void UpdateRoutesPanel()
        {
            if (_routesPassiveIncomeText == null) return;

            long totalPassiveIncome = (_logisticsEngine.TradeRoutesTier1 * 1000L) +
                                      (_logisticsEngine.TradeRoutesTier2 * 12500L) +
                                      (_logisticsEngine.TradeRoutesTier3 * 150000L);

            _routesPassiveIncomeText.text = string.Format("Total Passive Income: ${0:N0} / second", totalPassiveIncome);
            _routesBalanceText.text = string.Format("Current Balance: ${0:N0}", _logisticsEngine.PlayerBalance);

            _routesT1CountText.text = "Owned: " + _logisticsEngine.TradeRoutesTier1;
            _routesT2CountText.text = "Owned: " + _logisticsEngine.TradeRoutesTier2;
            _routesT3CountText.text = "Owned: " + _logisticsEngine.TradeRoutesTier3;
        }
        #endregion

        #region Row Creators
        private StorageRow CreateStorageRow(int itemId, ItemProto itemProto, int count, string pText, Color pColor, int limit)
        {
            GameObject rowGo = new GameObject("StorageRow_" + itemId, typeof(RectTransform));
            var rect = rowGo.GetComponent<RectTransform>();
            rowGo.transform.SetParent(_storageScrollContent, false);
            NormalizeRowRect(rect, 36f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.minHeight = 36f;

            AddItemIconAndName(10f, 0f, rect, itemProto.iconSprite, itemProto.name);

            var countText = CreateText(220f, 8f, 100f, 20f, rect, count.ToString("N0"), 14, "stock");
            countText.color = Color.white;

            var prolifText = CreateText(320f, 8f, 120f, 20f, rect, pText, 14, "prolif");
            prolifText.color = pColor;

            var limitInput = CreateInputField(470f, 4f, 110f, rect, limit.ToString(), 14, "limit");
            limitInput.onEndEdit.AddListener((string s) => {
                if (int.TryParse(s, out int newLimit) && newLimit >= 0)
                {
                    _storageService.SetItemLimit(itemId, newLimit);
                }
            });

            return new StorageRow
            {
                rowGo = rowGo,
                countText = countText,
                prolifText = prolifText,
                limitInput = limitInput
            };
        }

        private MarketRow CreateMarketRow(int itemId, ItemProto itemProto, int count, long price)
        {
            GameObject rowGo = new GameObject("MarketRow_" + itemId, typeof(RectTransform));
            var rect = rowGo.GetComponent<RectTransform>();
            rowGo.transform.SetParent(_marketScrollContent, false);
            NormalizeRowRect(rect, 36f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.minHeight = 36f;

            AddItemIconAndName(10f, 0f, rect, itemProto.iconSprite, itemProto.name);

            var countText = CreateText(180f, 8f, 70f, 20f, rect, count.ToString("N0"), 14, "stock");
            countText.color = Color.white;

            var buyText = CreateText(260f, 8f, 60f, 20f, rect, "$" + price, 14, "buy_price");
            buyText.color = Color.green;

            var sellText = CreateText(320f, 8f, 60f, 20f, rect, "$" + price, 14, "sell_price");
            sellText.color = Color.yellow;

            _uiService.SetMarketQuantityInput(itemId, "10");
            var quantityInput = CreateInputField(390f, 4f, 60f, rect, "10", 14, "qty");
            quantityInput.onValueChanged.AddListener((string s) => {
                _uiService.SetMarketQuantityInput(itemId, s);
            });

            CreateRowButton(460f, 4f, 55f, 28f, rect, "Buy", 13, "buy_btn", () => {
                _uiService.BuyItemPublic(itemId, price);
                UpdateMarketData(false);
            });

            CreateRowButton(520f, 4f, 55f, 28f, rect, "Sell", 13, "sell_btn", () => {
                _uiService.SellItemPublic(itemId, price);
                UpdateMarketData(false);
            });

            return new MarketRow
            {
                rowGo = rowGo,
                countText = countText,
                quantityInput = quantityInput
            };
        }

        private OrderRow CreateOrderRow(int itemId, ItemProto itemProto, int count, int buyThresh, int sellThresh)
        {
            GameObject rowGo = new GameObject("OrderRow_" + itemId, typeof(RectTransform));
            var rect = rowGo.GetComponent<RectTransform>();
            rowGo.transform.SetParent(_marketScrollContent, false);
            NormalizeRowRect(rect, 36f);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = 36f;
            le.minHeight = 36f;

            AddItemIconAndName(10f, 0f, rect, itemProto.iconSprite, itemProto.name);

            var countText = CreateText(180f, 8f, 70f, 20f, rect, count.ToString("N0"), 14, "stock");
            countText.color = Color.white;

            var buyBelowInput = CreateInputField(260f, 4f, 150f, rect, buyThresh.ToString(), 14, "buy_thresh");
            buyBelowInput.onEndEdit.AddListener((string s) => {
                if (int.TryParse(s, out int val) && val >= 0)
                {
                    var ord = _storageService.GetMarketOrder(itemId);
                    _storageService.SetMarketOrder(itemId, val, ord.SellThreshold);
                }
            });

            var sellAboveInput = CreateInputField(420f, 4f, 150f, rect, sellThresh.ToString(), 14, "sell_thresh");
            sellAboveInput.onEndEdit.AddListener((string s) => {
                if (int.TryParse(s, out int val) && val >= 0)
                {
                    var ord = _storageService.GetMarketOrder(itemId);
                    _storageService.SetMarketOrder(itemId, ord.BuyThreshold, val);
                }
            });

            return new OrderRow
            {
                rowGo = rowGo,
                countText = countText,
                buyBelowInput = buyBelowInput,
                sellAboveInput = sellAboveInput
            };
        }
        #endregion

        #region Helper Creators & Core overrides
        public override void AutoFitWindowSize()
        {
            var trans = GetComponent<RectTransform>();
            trans.sizeDelta = new Vector2(800f, 620f);
        }

        protected override void _OnOpen()
        {
            base._OnOpen();
            var trans = GetComponent<RectTransform>();
            trans.sizeDelta = new Vector2(800f, 620f);
        }

        protected override void _OnFree()
        {
            base._OnFree();
            _storageCategoryButtons.Clear();
            _marketSubTabButtons.Clear();
            _marketCategoryButtons.Clear();
            _cachedStorageRows.Clear();
            _cachedMarketRows.Clear();
            _cachedOrderRows.Clear();
            _cachedDashboardRows.Clear();
        }

        protected override void _OnUpdate()
        {
            base._OnUpdate();

            // Snap window position to nearest integers to prevent blurry rendering
            var rectTrans = GetComponent<RectTransform>();
            if (rectTrans != null)
            {
                var pos = rectTrans.anchoredPosition;
                rectTrans.anchoredPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
            }

            // Snap scroll view content offsets to nearest integers
            SnapScrollContent(_dashboardScrollContent);
            SnapScrollContent(_storageScrollContent);
            SnapScrollContent(_marketScrollContent);

            if (VFInput.escape && !VFInput.inputing)
            {
                VFInput.UseEscape();
                _Close();
                return;
            }

            _updateTimer += Time.deltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                UpdateActivePanel(false);
            }
        }

        private void SnapScrollContent(Transform content)
        {
            if (content != null && content.gameObject.activeInHierarchy)
            {
                var rect = content as RectTransform;
                if (rect != null)
                {
                    var pos = rect.anchoredPosition;
                    rect.anchoredPosition = new Vector2(Mathf.Round(pos.x), Mathf.Round(pos.y));
                }
            }
        }

        private static Text CreateText(float x, float y, float width, float height, RectTransform parent, string label, int fontSize = 14, string objName = "label")
        {
            var src = UIRoot.instance.uiGame.assemblerWindow.stateText;
            var txt = Instantiate(src);
            Util.CleanText(txt);

            Util.RemoveTextEventTriggers(txt.gameObject);
            txt.gameObject.name = objName;
            txt.text = label.Translate();
            txt.color = new Color(1f, 1f, 1f, 0.75f);
            txt.alignment = TextAnchor.MiddleLeft;
            txt.fontSize = fontSize;
            var rect = Util.NormalizeRectWithTopLeft(txt.rectTransform, x, y, parent);
            rect.sizeDelta = new Vector2(width, height);
            txt.maskable = true;
            return txt;
        }

        private static UIButton CreateRowButton(float x, float y, float width, float height, RectTransform parent, string text, int fontSize = 14, string objName = "button", UnityEngine.Events.UnityAction onClick = null)
        {
            var panel = UIRoot.instance.uiGame.statWindow.performancePanelUI;
            var btn = Instantiate(panel.cpuActiveButton);
            Util.RemoveTextEventTriggers(btn.gameObject);
            btn.gameObject.name = objName;
            var rect = Util.NormalizeRectWithTopLeft(btn, x, y, parent);
            rect.sizeDelta = new Vector2(width, height);
            
            var images = btn.GetComponentsInChildren<Image>(true);
            foreach (var img in images) img.maskable = true;
            
            var l = btn.gameObject.transform.Find("button-text").GetComponent<Localizer>();
            var t = btn.gameObject.transform.Find("button-text").GetComponent<Text>();
            if (l != null)
            {
                l.stringKey = text;
                l.translation = text.Translate();
            }

            if (t != null)
            {
                Util.CleanText(t);
                t.text = text.Translate();
                t.maskable = true;
            }

            t.fontSize = fontSize;
            typeof(UIButton).GetField("tip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(btn, null);
            btn.tips = new UIButton.TipSettings();
            btn.button.onClick.RemoveAllListeners();
            if (onClick != null) btn.button.onClick.AddListener(onClick);
            return btn;
        }

        private static InputField CreateInputField(float x, float y, float width, RectTransform parent, string text = "", int fontSize = 16, string objName = "input", UnityEngine.Events.UnityAction<string> onChanged = null, UnityEngine.Events.UnityAction<string> onEditEnd = null)
        {
            var stationWindow = UIRoot.instance.uiGame.stationWindow;
            var inputField = Instantiate(stationWindow.nameInput);
            Util.RemoveTextEventTriggers(inputField.gameObject);
            inputField.gameObject.name = objName;
            Destroy(inputField.GetComponent<UIButton>());
            inputField.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
            var rect = Util.NormalizeRectWithTopLeft(inputField, x, y, parent);
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
            inputField.text = text;
            
            if (inputField.textComponent != null)
            {
                Util.CleanText(inputField.textComponent);
                inputField.textComponent.fontSize = fontSize;
                inputField.textComponent.maskable = true;
            }

            if (inputField.placeholder != null && inputField.placeholder is Text pText)
            {
                Util.CleanText(pText);
            }
            var img = inputField.GetComponent<Image>();
            if (img != null) img.maskable = true;

            inputField.onValueChanged.RemoveAllListeners();
            if (onChanged != null) inputField.onValueChanged.AddListener(onChanged);
            inputField.onEndEdit.RemoveAllListeners();
            if (onEditEnd != null) inputField.onEndEdit.AddListener(onEditEnd);
            return inputField;
        }

        private static UIButton CreateTabButton(float x, float y, float width, float height, RectTransform parent, string label, int fontSize = 14, UnityEngine.Events.UnityAction onClick = null)
        {
            var swarmPanel = UIRoot.instance.uiGame.dysonEditor.controlPanel.hierarchy.swarmPanel;
            var src = swarmPanel.orbitButtons[0];
            var btn = Instantiate(src);
            Util.RemoveTextEventTriggers(btn.gameObject);
            btn.gameObject.GetComponent<Image>().sprite = swarmPanel.buttonDefaultSprite;
            btn.name = "tab-btn";
            btn.highlighted = false;

            var btnRect = Util.NormalizeRectWithTopLeft(btn, x, y, parent);
            btnRect.sizeDelta = new Vector2(width, height);
            btn.transform.Find("frame").gameObject.SetActive(false);
            if (btn.transitions.Length >= 3)
            {
                btn.transitions[0].normalColor = new Color(0.1f, 0.1f, 0.1f, 0.68f);
                btn.transitions[0].highlightColorOverride = new Color(0.9906f, 0.5897f, 0.3691f, 0.4f);
                btn.transitions[1].normalColor = new Color(1f, 1f, 1f, 0.8f);
                btn.transitions[1].highlightColorOverride = new Color(0.2f, 0.2f, 0.2f, 1f);
            }

            var textTrans = btn.transform.Find("Text");
            var btnText = textTrans.GetComponent<Text>();
            if (btnText != null)
            {
                Util.CleanText(btnText);
                btnText.text = label.Translate();
                btnText.fontSize = fontSize;
            }

            btn.button.onClick.RemoveAllListeners();
            if (onClick != null) btn.button.onClick.AddListener(onClick);
            return btn;
        }

        private static void AddItemIconAndName(float x, float y, RectTransform parent, Sprite iconSprite, string nameText)
        {
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            var img = iconGo.AddComponent<Image>();
            img.sprite = iconSprite;
            img.maskable = true;
            Util.NormalizeRectWithTopLeft(img, x, y + 2f, parent);
            ((RectTransform)iconGo.transform).sizeDelta = new Vector2(32f, 32f);

            var txt = CreateText(x + 38f, y + 8f, 145f, 20f, parent, nameText, 14, "name");
            txt.color = Color.white;
        }

        private ScrollRect CreateScrollView(RectTransform parent, float x, float y, float width, float height)
        {
            var statWin = UIRoot.instance.uiGame.statWindow;
            var go = Instantiate(statWin.scrollRect.gameObject, parent, false);
            Util.RemoveTextEventTriggers(go);
            go.name = "scroll-view";
            go.SetActive(true);
            var rect = go.GetComponent<RectTransform>();
            Util.NormalizeRectWithTopLeft(rect, x, y, parent);
            rect.sizeDelta = new Vector2(width, height);

            var scrollRect = go.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                go.AddComponent<UIScrollZone>();
                scrollRect.onValueChanged.RemoveAllListeners();
                scrollRect.onValueChanged.AddListener(_ => {
                    if (scrollRect.content != null)
                    {
                        var pos = scrollRect.content.anchoredPosition;
                        var roundedX = Mathf.Round(pos.x);
                        var roundedY = Mathf.Round(pos.y);
                        if (Mathf.Abs(pos.x - roundedX) > 0.0001f || Mathf.Abs(pos.y - roundedY) > 0.0001f)
                        {
                            scrollRect.content.anchoredPosition = new Vector2(roundedX, roundedY);
                        }
                    }
                });

                if (scrollRect.viewport != null)
                {
                    scrollRect.viewport.localScale = Vector3.one;
                    
                    var vpRect = scrollRect.viewport;
                    vpRect.anchorMin = Vector2.zero;
                    vpRect.anchorMax = Vector2.one;
                    vpRect.pivot = new Vector2(0f, 1f);
                    vpRect.offsetMin = Vector2.zero;
                    vpRect.offsetMax = new Vector2(-18f, 0f);
                    
                    var mask = scrollRect.viewport.GetComponent<Mask>();
                    if (mask != null) DestroyImmediate(mask);
                    var img = scrollRect.viewport.GetComponent<Image>();
                    if (img != null) DestroyImmediate(img);
                    var mask2D = scrollRect.viewport.GetComponent<RectMask2D>();
                    if (mask2D == null) mask2D = scrollRect.viewport.gameObject.AddComponent<RectMask2D>();
                    mask2D.enabled = true;
                }
                if (scrollRect.content != null)
                {
                    scrollRect.content.localScale = Vector3.one;
                    
                    var cRect = scrollRect.content;
                    cRect.anchorMin = new Vector2(0f, 1f);
                    cRect.anchorMax = new Vector2(1f, 1f);
                    cRect.pivot = new Vector2(0f, 1f);
                    cRect.offsetMin = Vector2.zero;
                    cRect.offsetMax = Vector2.zero;

                    for (int i = scrollRect.content.childCount - 1; i >= 0; i--)
                    {
                        Destroy(scrollRect.content.GetChild(i).gameObject);
                    }
                    
                    var layout = scrollRect.content.gameObject.GetComponent<VerticalLayoutGroup>();
                    if (layout == null) layout = scrollRect.content.gameObject.AddComponent<VerticalLayoutGroup>();
                    layout.spacing = 4f;
                    layout.childControlHeight = true;
                    layout.childControlWidth = true;
                    layout.childForceExpandHeight = false;
                    layout.childForceExpandWidth = true;

                    var fitter = scrollRect.content.gameObject.GetComponent<ContentSizeFitter>();
                    if (fitter == null) fitter = scrollRect.content.gameObject.AddComponent<ContentSizeFitter>();
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                    fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                }
            }
            return scrollRect;
        }

        private static void NormalizeRowRect(RectTransform rect, float height)
        {
            if (rect == null) return;
            rect.localScale = Vector3.one;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
        }

        private UIService.ItemCategory GetItemCategory(ItemProto itemProto)
        {
            if (itemProto == null) return UIService.ItemCategory.IntermediateProducts;
            if (itemProto.ID >= 6001 && itemProto.ID <= 6006) return UIService.ItemCategory.ScienceMatrices;
            if (itemProto.isAmmo || itemProto.isFighter) return UIService.ItemCategory.AmmunitionAndCombat;
            if (itemProto.CanBuild) return UIService.ItemCategory.BuildingsAndVehicles;
            if (IsVein(itemProto.ID)) return UIService.ItemCategory.RawResources;
            return UIService.ItemCategory.IntermediateProducts;
        }

        private bool IsVein(int itemId)
        {
            int[] items = { ItemIds.Water, ItemIds.SulfuricAcid, ItemIds.Hydrogen, ItemIds.Deuterium };
            return items.Contains(itemId) || LDB.veins.GetVeinTypeByItemId(itemId) != EVeinType.None;
        }

        private string FormatDuration(double minutes)
        {
            if (double.IsInfinity(minutes) || minutes > 60 * 24 * 30)
            {
                return ">30d";
            }
            if (minutes < 1)
            {
                return "<1m";
            }
            if (minutes < 60)
            {
                return string.Format("{0:F0}m", minutes);
            }

            double hours = minutes / 60.0;
            if (hours < 24)
            {
                int h = (int)hours;
                int m = (int)Math.Round((hours - h) * 60);
                return string.Format("{0}h{1:D2}m", h, m);
            }

            double days = hours / 24.0;
            return string.Format("{0:F1}d", days);
        }
        #endregion
    }
}
