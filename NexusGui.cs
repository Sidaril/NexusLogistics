using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace NexusLogistics
{
    /// <summary>
    /// Handles all GUI rendering and state for the mod.
    /// </summary>
    public class NexusGui
    {
        // GUI State
        private bool _showMainWindow;
        private bool _showStorageWindow;
        private Rect _mainWindowRect = new Rect(700, 250, 600, 500);
        private Rect _storageWindowRect = new Rect(100, 250, 600, 500);
        private Vector2 _mainScrollPos;
        private Vector2 _storageScrollPos;
        
        private int _selectedPanel = 0;
        private StorageCategory _selectedStorageCategory = StorageCategory.RawResources;

        // Data for GUI
        private List<KeyValuePair<int, RemoteStorageItem>> _storageItemsForGui = new List<KeyValuePair<int, RemoteStorageItem>>();
        private readonly Dictionary<int, string> _limitInputStrings = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _fuelOptions = new Dictionary<int, string>();
        private int _selectedFuelIndex;
        private ConfigEntry<int> _fuelIdConfig;

        // Caching for performance
        private readonly Texture2D _windowTexture;
        private readonly GUIContent _clearBattleBaseButton;

        private enum StorageCategory { RawResources, IntermediateProducts, BuildingsAndVehicles, AmmunitionAndCombat, ScienceMatrices }

        public NexusGui(ConfigEntry<int> fuelIdConfig)
        {
            _fuelIdConfig = fuelIdConfig;

            // Initialize GUI resources once to avoid creating them in OnGUI
            _windowTexture = new Texture2D(1, 1);
            _windowTexture.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.1f, 0.9f));
            _windowTexture.Apply();

            _clearBattleBaseButton = new GUIContent("Clear Battlefield Analysis Base", "Items set not to drop will be discarded");

            InitializeFuelOptions();
        }

        private void InitializeFuelOptions()
        {
            _fuelOptions.Add(0, "Auto");
            _fuelOptions.Add(1006, "Coal");
            _fuelOptions.Add(1109, "Graphite");
            _fuelOptions.Add(1007, "Crude Oil");
            _fuelOptions.Add(1114, "Refined Oil");
            _fuelOptions.Add(1120, "Hydrogen");
            _fuelOptions.Add(1801, "Hydrogen Fuel Rod");
            _fuelOptions.Add(1011, "Fire Ice");
            _fuelOptions.Add(5206, "Energy Shard");
            _fuelOptions.Add(1128, "Combustion Unit");
            _fuelOptions.Add(1030, "Wood");
            _fuelOptions.Add(1031, "Plant Fuel");
            
            _selectedFuelIndex = _fuelOptions.Keys.ToList().FindIndex(id => id == _fuelIdConfig.Value);
            if (_selectedFuelIndex == -1) _selectedFuelIndex = 0;
        }
        
        public void ToggleMainWindow() => _showMainWindow = !_showMainWindow;
        public void ToggleStorageWindow() => _showStorageWindow = !_showStorageWindow;

        public void Update()
        {
            // Prepare data for the storage window if it's visible.
            // This reduces work done inside the OnGUI loop.
            if (_showStorageWindow)
            {
                _storageItemsForGui = NexusStorage.GetStorageEntries()
                    .Where(pair =>
                    {
                        if (pair.Value.Count <= 0) return false;
                        ItemProto itemProto = LDB.items.Select(pair.Key);
                        return itemProto != null && GetItemCategory(itemProto) == _selectedStorageCategory;
                    })
                    .OrderBy(item => LDB.items.Select(item.Key)?.name ?? string.Empty)
                    .ToList();

                // Ensure input strings are initialized for visible items
                foreach (var pair in _storageItemsForGui)
                {
                    if (!_limitInputStrings.ContainsKey(pair.Key))
                    {
                        _limitInputStrings[pair.Key] = pair.Value.Limit.ToString();
                    }
                }
            }
        }
        
        public void OnGUI()
        {
            // FIX: Apply the custom dark background to both normal and focused states
            // This ensures the window color doesn't change on click.
            if (_showMainWindow || _showStorageWindow)
            {
                GUI.skin.window.normal.background = _windowTexture;
                GUI.skin.window.onNormal.background = _windowTexture;
            }

            if (_showMainWindow)
            {
                _mainWindowRect = GUI.Window(0, _mainWindowRect, MainWindow, $"NexusLogistics v{NexusLogistics.VERSION}");
            }
            if (_showStorageWindow)
            {
                _storageWindowRect = GUI.Window(1, _storageWindowRect, StorageWindow, "Remote Storage Contents");
            }

            // Prevent click-through
            if ((_showMainWindow && _mainWindowRect.Contains(Event.current.mousePosition)) || 
                (_showStorageWindow && _storageWindowRect.Contains(Event.current.mousePosition)))
            {
                Input.ResetInputAxes();
            }
        }

        private void MainWindow(int windowID)
        {
            string[] panels = { "Main Options", "Items", "Combat" };
            _selectedPanel = GUILayout.Toolbar(_selectedPanel, panels);
            
            _mainScrollPos = GUILayout.BeginScrollView(_mainScrollPos, false, true);
            switch (_selectedPanel)
            {
                case 0: MainPanel(); break;
                case 1: ItemPanel(); break;
                case 2: CombatPanel(); break;
            }
            GUILayout.EndScrollView();

            GUI.DragWindow();
        }

        private void StorageWindow(int windowID)
        {
            string[] categories = { "Raw", "Intermeds", "Buildings", "Combat", "Science" };
            _selectedStorageCategory = (StorageCategory)GUILayout.Toolbar((int)_selectedStorageCategory, categories);

            GUILayout.Space(5);
            
            // Header
            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Name", GUILayout.Width(150));
            GUILayout.Label("Count", GUILayout.Width(100));
            GUILayout.Label("Proliferation", GUILayout.Width(100));
            GUILayout.Label("Limit", GUILayout.Width(100));
            GUILayout.EndHorizontal();

            _storageScrollPos = GUILayout.BeginScrollView(_storageScrollPos);
            
            foreach (var pair in _storageItemsForGui)
            {
                DrawStorageItem(pair.Key, pair.Value);
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private void DrawStorageItem(int itemId, RemoteStorageItem item)
        {
            ItemProto itemProto = LDB.items.Select(itemId);
            if (itemProto == null) return;

            // --- MODIFICATION: Proliferation Display Logic with Percentage ---
            float avgInc = 0;
            if (item.Count > 0)
            {
                avgInc = (float)item.Inc / item.Count;
            }
            string proliferationText;
            Color proliferationColor;

            // Determine text, color, and percentage based on average proliferation level
            // avgInc represents sprays per item: Mk1=1, Mk2=2, Mk3=4
            if (avgInc >= 3.999f) // Maxed at Mk.III
            {
                proliferationText = "Mk.III (Max)";
                proliferationColor = new Color(0.5f, 0.8f, 1f); // Blue
            }
            else if (avgInc >= 2.0f) // Tier is Mk.II, progressing to Mk.III
            {
                // Progress from Mk.II (2 sprays) to Mk.III (4 sprays)
                int percentage = (int)(((avgInc - 2.0f) / 2.0f) * 100);
                proliferationText = $"Mk.II ({percentage}%)";
                proliferationColor = new Color(0.4f, 0.9f, 0.4f); // Green
            }
            else if (avgInc >= 1.0f) // Tier is Mk.I, progressing to Mk.II
            {
                // Progress from Mk.I (1 spray) to Mk.II (2 sprays)
                int percentage = (int)((avgInc - 1.0f) * 100);
                proliferationText = $"Mk.I ({percentage}%)";
                proliferationColor = new Color(1f, 0.8f, 0.3f); // Orange/Gold
            }
            else if (avgInc > 0) // Tier is None/Mixed, progressing to Mk.I
            {
                // Progress from None (0 sprays) to Mk.I (1 spray)
                int percentage = (int)(avgInc * 100);
                proliferationText = $"Mixed ({percentage}%)";
                proliferationColor = Color.yellow;
            }
            else // None
            {
                proliferationText = "None";
                proliferationColor = Color.gray;
            }
            // --- END: Proliferation Display Logic ---

            GUILayout.BeginHorizontal();
            GUILayout.Label(itemProto.name, GUILayout.Width(150));
            GUILayout.Label(item.Count.ToString("N0"), GUILayout.Width(100));

            var originalColor = GUI.contentColor;
            GUI.contentColor = proliferationColor;
            // Display the status, with a tooltip showing the precise average spray count
            GUILayout.Label(new GUIContent(proliferationText, $"Avg. Sprays: {avgInc:F2}"), GUILayout.Width(100));
            GUI.contentColor = originalColor;

            string currentInput = _limitInputStrings[itemId];
            string newInput = GUILayout.TextField(currentInput, GUILayout.Width(100));

            if (newInput != currentInput)
            {
                _limitInputStrings[itemId] = newInput;
                if (int.TryParse(newInput, out int newLimit) && newLimit >= 0)
                {
                    NexusStorage.UpdateItemLimit(itemId, newLimit);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void MainPanel()
        {
            GUILayout.BeginVertical("box");
            NexusLogistics.EnableMod.Value = GUILayout.Toggle(NexusLogistics.EnableMod.Value, "Enable Mod");
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            NexusLogistics.AutoReplenishPackage.Value = GUILayout.Toggle(NexusLogistics.AutoReplenishPackage.Value, "Auto Replenish Filtered Inventory Slots");
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            NexusLogistics.AutoSpray.Value = GUILayout.Toggle(NexusLogistics.AutoSpray.Value, "Auto Spray Items");
            if (NexusLogistics.AutoSpray.Value)
            {
                NexusLogistics.CostProliferator.Value = GUILayout.Toggle(NexusLogistics.CostProliferator.Value, "Consume Proliferator");
                GUILayout.Label("Proliferator Tier to Use:");
                NexusLogistics.ProliferatorTier.Value = (ProliferatorSelection)GUILayout.Toolbar((int)NexusLogistics.ProliferatorTier.Value, Enum.GetNames(typeof(ProliferatorSelection)));
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            NexusLogistics.UseStorage.Value = GUILayout.Toggle(NexusLogistics.UseStorage.Value, "Recover Items from Storage Boxes & Tanks");
            GUILayout.EndVertical();

            GUILayout.BeginVertical("box");
            NexusLogistics.AutoReplenishTPPFuel.Value = GUILayout.Toggle(NexusLogistics.AutoReplenishTPPFuel.Value, "Auto Replenish Thermal Power Plant Fuel");
            if (NexusLogistics.AutoReplenishTPPFuel.Value)
            {
                int newIndex = GUILayout.SelectionGrid(_selectedFuelIndex, _fuelOptions.Values.ToArray(), 3, GUI.skin.toggle);
                if (newIndex != _selectedFuelIndex)
                {
                    _selectedFuelIndex = newIndex;
                    _fuelIdConfig.Value = _fuelOptions.Keys.ToArray()[_selectedFuelIndex];
                }
            }
            GUILayout.EndVertical();
        }

        private void ItemPanel()
        {
            GUILayout.Label("Warning: Using these options may disable achievements.");
            GUILayout.BeginVertical("box");
            NexusLogistics.InfBuildings.Value = GUILayout.Toggle(NexusLogistics.InfBuildings.Value, "Infinite Buildings");
            NexusLogistics.InfVeins.Value = GUILayout.Toggle(NexusLogistics.InfVeins.Value, "Infinite Minerals");
            NexusLogistics.InfItems.Value = GUILayout.Toggle(NexusLogistics.InfItems.Value, "Infinite Items");
            NexusLogistics.InfSand.Value = GUILayout.Toggle(NexusLogistics.InfSand.Value, "Infinite Soil Pile");
            GUILayout.EndVertical();
        }

        private void CombatPanel()
        {
            GUILayout.Label("Warning: Using these options may disable achievements.");
            GUILayout.BeginVertical("box");
            NexusLogistics.InfAmmo.Value = GUILayout.Toggle(NexusLogistics.InfAmmo.Value, "Infinite Ammo");
            NexusLogistics.InfFleet.Value = GUILayout.Toggle(NexusLogistics.InfFleet.Value, "Infinite Fleet");
            GUILayout.EndVertical();
            
            GUILayout.Space(15);
            if (GUILayout.Button(_clearBattleBaseButton, GUILayout.Width(250)))
            {
                NexusProcessor.ClearBattleBase();
            }
        }

        private StorageCategory GetItemCategory(ItemProto itemProto)
        {
            if (itemProto.ID >= 6001 && itemProto.ID <= 6006) return StorageCategory.ScienceMatrices;
            if (itemProto.isAmmo || itemProto.isFighter) return StorageCategory.AmmunitionAndCombat;
            if (itemProto.CanBuild) return StorageCategory.BuildingsAndVehicles;
            if (LDB.veins.GetVeinTypeByItemId(itemProto.ID) != EVeinType.None || itemProto.ID == 1000) return StorageCategory.RawResources;
            return StorageCategory.IntermediateProducts;
        }
    }
}
