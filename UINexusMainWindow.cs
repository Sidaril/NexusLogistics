using BepInEx;
using UnityEngine;
using UnityEngine.UI;
using NexusLogistics.UI; // Import the new UI utilities
using BepInEx.Configuration; // Needed for ConfigEntry
using UnityEngine.UI;
using System.Collections.Generic; // Needed for List

namespace NexusLogistics
{
    public class UINexusMainWindow : ManualBehaviour, IModWindow
    {
        public static UINexusMainWindow instance;
        private RectTransform windowTrans;

        // Lists to hold our new buttons
        private List<UIButton> proliferatorButtons = new List<UIButton>();
        private List<UIButton> fuelButtons = new List<UIButton>();

        // Define an internal enum that matches your Config's ProliferatorSelection
        // This makes the button .data field cleaner (0, 1, 2, 3)
        private enum ProliferatorSelection { All, Mk1, Mk2, Mk3 }


        public static void CreateInstance()
        {
            if (instance != null) return;
            instance = UIWindowControl.CreateWindow<UINexusMainWindow>("NexusMainWindow", "Nexus Logistics Settings");
        }

        public void TryClose() { _Close(); }
        public bool isFunctionWindow() { return true; }

        protected override void _OnCreate()
        {
            windowTrans = UIWindowControl.GetRectTransform(this);
            // Increased height slightly for the two new toggles
            windowTrans.sizeDelta = new Vector2(480, 550);

            float y_ = 60f;
            float x_ = 36f;
            float padding = 4f; // Padding between buttons
            float buttonWidth = 80f; // Width for proliferator buttons
            float fuelButtonWidth = 120f; // Width for fuel buttons
            int fuelColumnCount = 3;

            // Helper function to add elements
            void AddElement(RectTransform rect, float new_x, float new_y)
            {
                if (rect != null)
                {
                    UIUtil.NormalizeRectWithTopLeft(rect, new_x, new_y, windowTrans);
                }
            }

            RectTransform rect;

            // --- Main Options ---
            // Note: Make sure these ConfigEntry fields in NexusLogistics.cs are 'public static'

            rect = UICheckBox.CreateCheckBox(NexusLogistics.enableMod, "Enable MOD");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.autoReplenishPackage, "Auto Replenish Filtered Items");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.autoCleanInventory, "Auto Clean Inventory to Logistic Slots");
            AddElement(rect, x_, y_); y_ += 26f;

            y_ += 15f; // Add space

            rect = UICheckBox.CreateCheckBox(NexusLogistics.autoSpray, "Auto Spray");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.costProliferator, "Consume Proliferator");
            AddElement(rect, x_, y_); y_ += 26f;

            // --- Proliferator Tier Toolbar ---
            float x_toolbar = x_;
            proliferatorButtons.Clear();

            UIButton allBtn = UIUtil.MakeHiliteTextButton("All", buttonWidth, 24f);
            allBtn.data = (int)ProliferatorSelection.All;
            AddElement(allBtn.transform as RectTransform, x_toolbar, y_);
            proliferatorButtons.Add(allBtn);
            x_toolbar += buttonWidth + padding;

            UIButton mk1Btn = UIUtil.MakeHiliteTextButton("MK.I", buttonWidth, 24f);
            mk1Btn.data = (int)ProliferatorSelection.Mk1;
            AddElement(mk1Btn.transform as RectTransform, x_toolbar, y_);
            proliferatorButtons.Add(mk1Btn);
            x_toolbar += buttonWidth + padding;

            UIButton mk2Btn = UIUtil.MakeHiliteTextButton("MK.II", buttonWidth, 24f);
            mk2Btn.data = (int)ProliferatorSelection.Mk2;
            AddElement(mk2Btn.transform as RectTransform, x_toolbar, y_);
            proliferatorButtons.Add(mk2Btn);
            x_toolbar += buttonWidth + padding;

            UIButton mk3Btn = UIUtil.MakeHiliteTextButton("MK.III", buttonWidth, 24f);
            mk3Btn.data = (int)ProliferatorSelection.Mk3;
            AddElement(mk3Btn.transform as RectTransform, x_toolbar, y_);
            proliferatorButtons.Add(mk3Btn);

            y_ += 30f; // Move down past the toolbar

            // --- Fuel Selection Grid ---
            rect = UICheckBox.CreateCheckBox(NexusLogistics.useStorege, "Recover from storage boxes/tanks");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.autoReplenishTPPFuel, "Auto-refuel Thermal Power Plants");
            AddElement(rect, x_, y_); y_ += 26f;

            fuelButtons.Clear();
            int currentFuelColumn = 0;
            float x_fuelgrid = x_;

            foreach (var pair in NexusLogistics.fuelOptions)
            {
                UIButton fuelBtn = UIUtil.MakeHiliteTextButton(pair.Value, fuelButtonWidth, 24f);
                fuelBtn.data = pair.Key; // The Item ID
                AddElement(fuelBtn.transform as RectTransform, x_fuelgrid, y_);
                fuelButtons.Add(fuelBtn);

                currentFuelColumn++;
                if (currentFuelColumn >= fuelColumnCount)
                {
                    // Move to next row
                    currentFuelColumn = 0;
                    x_fuelgrid = x_;
                    y_ += 24f + padding;
                }
                else
                {
                    // Move to next column
                    x_fuelgrid += fuelButtonWidth + padding;
                }
            }
            // Move y_ down past the last row (if it wasn't a full row)
            if (currentFuelColumn != 0) y_ += 24f + padding;

            // --- Other Toggles (Items/Combat) ---
            y_ += 15f; // Space for next section

            rect = UICheckBox.CreateCheckBox(NexusLogistics.infBuildings, "Infinite Buildings");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.infVeins, "Infinite Minerals");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.infItems, "Infinite Items (Disables Achievements)");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.infSand, "Infinite Soil Pile"); // ADDED THIS
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.infAmmo, "Infinite Ammo");
            AddElement(rect, x_, y_); y_ += 26f;

            rect = UICheckBox.CreateCheckBox(NexusLogistics.infFleet, "Infinite Fleet");
            AddElement(rect, x_, y_); y_ += 26f;
        }

        protected override bool _OnInit()
        {
            windowTrans.anchoredPosition = new Vector2(300, -400); // Set initial position
            return true;
        }

        protected override void _OnFree() { }
        protected override void _OnDestroy() { instance = null; }

        protected override void _OnRegEvent()
        {
            // Add listeners for our buttons
            foreach (var btn in proliferatorButtons)
            {
                btn.onClick += OnProliferatorButtonClick;
            }
            foreach (var btn in fuelButtons)
            {
                btn.onClick += OnFuelButtonClick;
            }
        }
        protected override void _OnUnregEvent()
        {
            // Remove listeners to prevent errors
            foreach (var btn in proliferatorButtons)
            {
                btn.onClick -= OnProliferatorButtonClick;
            }
            foreach (var btn in fuelButtons)
            {
                btn.onClick -= OnFuelButtonClick;
            }
        }

        protected override void _OnOpen()
        {
            // Refresh button states every time window is opened
            RefreshProliferatorButtons();
            RefreshFuelButtons();
        }

        protected override void _OnClose() { }

        protected override void _OnUpdate()
        {
            if (VFInput.escape && !VFInput.inputing)
            {
                VFInput.UseEscape();
                _Close();
            }
        }

        // --- Button Click Handlers ---

        private void OnProliferatorButtonClick(int data)
        {
            // Set the config value to the enum value that matches the button's data
            NexusLogistics.proliferatorSelection.Value = (NexusLogistics.ProliferatorSelection)data;
            RefreshProliferatorButtons();
        }

        private void RefreshProliferatorButtons()
        {
            int currentSelection = (int)NexusLogistics.proliferatorSelection.Value;
            foreach (var btn in proliferatorButtons)
            {
                btn.highlighted = (btn.data == currentSelection);
            }
        }

        private void OnFuelButtonClick(int data)
        {
            // Set the config value to the Item ID stored in the button's data
            NexusLogistics.fuelId.Value = data;
            RefreshFuelButtons();
        }

        private void RefreshFuelButtons()
        {
            int currentFuelId = NexusLogistics.fuelId.Value;
            foreach (var btn in fuelButtons)
            {
                btn.highlighted = (btn.data == currentFuelId);
            }
        }
    }
}
