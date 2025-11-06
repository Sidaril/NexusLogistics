using System.Collections.Generic;
using System.Linq;
using BepInEx;
using UnityEngine;
using NexusLogistics.UI; // Import the new UI utilities
using UnityEngine.UI;

namespace NexusLogistics
{
    public class UINexusStorageWindow : ManualBehaviour, IModWindow
    {
        public static UINexusStorageWindow instance;
        private RectTransform windowTrans;
        private RectTransform contentTrans;

        // UI Elements
        private List<UIButton> categoryButtons = new List<UIButton>();
        private MyUIListView storageListView;
        private RectTransform dashboardPanel;
        private Text dashboardBottleneckText;

        // State
        public NexusLogistics.StorageCategory selectedStorageCategory = NexusLogistics.StorageCategory.Dashboard;
        public Dictionary<int, string> limitInputStrings = new Dictionary<int, string>();
        private float refreshTimer = 0f;

        public static void CreateInstance()
        {
            NexusLogistics.Log.LogInfo("UINexusStorageWindow.CreateInstance called.");
            if (instance != null)
            {
                NexusLogistics.Log.LogInfo("Instance already exists.");
                return;
            }
            instance = UIWindowControl.CreateWindow<UINexusStorageWindow>("NexusStorageWindow", "Remote Storage");
            if (instance == null)
            {
                NexusLogistics.Log.LogError("UIWindowControl.CreateWindow returned null for UINexusStorageWindow!");
            }
        }

        public void TryClose() { _Close(); }
        public bool isFunctionWindow() { return true; }

        protected override void _OnCreate()
        {
            NexusLogistics.Log.LogInfo("UINexusStorageWindow._OnCreate called.");
            windowTrans = UIWindowControl.GetRectTransform(this);
            windowTrans.sizeDelta = new Vector2(900, 500);

            // --- CHANGED ---
            // Find the panel-bg to parent new UI elements to
            RectTransform panelBg = windowTrans.Find("panel-bg") as RectTransform;
            if (panelBg == null)
            {
                NexusLogistics.Log.LogError("NexusLogistics: Could not find 'panel-bg' in UINexusStorageWindow!");
                return;
            }
            // --- END CHANGED ---

            // Create a content area that fills the panel below the title
            contentTrans = UIUtil.CreateGameObject<RectTransform>("content");
            UIUtil.NormalizeRectWithMargin(contentTrans, 60f, 0f, 0f, 0f, panelBg);

            // --- Create Category Tabs ---
            float x_toolbar = 36f;
            float y_toolbar = 0f;
            float padding = 4f;
            categoryButtons.Clear();

            string[] categories = { "Dashboard", "RawResources", "IntermediateProducts", "BuildingsAndVehicles", "AmmunitionAndCombat", "ScienceMatrices" };
            foreach (var categoryName in categories)
            {
                NexusLogistics.StorageCategory category = (NexusLogistics.StorageCategory)System.Enum.Parse(typeof(NexusLogistics.StorageCategory), categoryName);

                UIButton btn = UIUtil.MakeHiliteTextButton(categoryName);
                btn.data = (int)category;
                UIUtil.NormalizeRectWithTopLeft(btn, x_toolbar, y_toolbar, contentTrans);

                categoryButtons.Add(btn);
                x_toolbar += btn.GetComponent<RectTransform>().sizeDelta.x + padding;
            }

            // --- Create Dashboard Panel ---
            dashboardPanel = UIUtil.CreateGameObject<RectTransform>("dashboard-panel");
            UIUtil.NormalizeRectWithMargin(dashboardPanel, 24f, 36f, 10f, 36f, contentTrans);
            dashboardPanel.gameObject.SetActive(true); // Active by default

            dashboardBottleneckText = UIUtil.CreateText("Loading dashboard...", 14);
            UIUtil.NormalizeRectWithTopLeft(dashboardBottleneckText, 0, 0, dashboardPanel);
            dashboardBottleneckText.rectTransform.sizeDelta = new Vector2(500, 300);
            dashboardBottleneckText.alignment = TextAnchor.UpperLeft;

            // --- Create List View Panel ---
            // Create a container for the list view and position it exactly like the dashboard panel
            RectTransform listViewContainer = UIUtil.CreateGameObject<RectTransform>("list-view-container");
            UIUtil.NormalizeRectWithMargin(listViewContainer, 24f, 36f, 10f, 36f, contentTrans);

            // Create the list view inside the container, telling it to fill the container
            storageListView = MyUIListView.Create("nexus-storage-list", 0, 0, listViewContainer, this);

            // FIX: Make the list view fill its container to resolve horizontal alignment issues
            RectTransform rclv = storageListView.GetComponent<RectTransform>();
            rclv.anchorMin = Vector2.zero; // Stretch from bottom-left
            rclv.anchorMax = Vector2.one;  // Stretch to top-right
            rclv.sizeDelta = Vector2.zero; // No size offset
            rclv.anchoredPosition = Vector2.zero; // Centered

            storageListView.gameObject.SetActive(false); // Inactive by default
        }

        private void PopulateItem(MonoBehaviour item, int rowIndex)
        {
            UINexusStorageItem storageItem = item.GetComponent<UINexusStorageItem>();
            storageItem.window = this;
            storageItem.Refresh(rowIndex);
        }

        protected override bool _OnInit()
        {
            windowTrans.anchoredPosition = new Vector2(-200, -400); // Set initial position
            return true;
        }

        protected override void _OnFree() { }
        protected override void _OnDestroy() { instance = null; }

        protected override void _OnRegEvent()
        {
            NexusLogistics.Log.LogInfo("Registering category button events.");
            foreach (var btn in categoryButtons)
            {
                btn.onClick += OnCategoryButtonClick;
            }
        }
        protected override void _OnUnregEvent()
        {
            foreach (var btn in categoryButtons)
            {
                btn.onClick -= OnCategoryButtonClick;
            }
        }

        protected override void _OnOpen()
        {
            NexusLogistics.isStorageWindowOpen = true;
            NexusLogistics.Log.LogInfo($"UINexusStorageWindow._OnOpen: isStorageWindowOpen = {NexusLogistics.isStorageWindowOpen}");
        }

        protected override void _OnClose()
        {
            NexusLogistics.isStorageWindowOpen = false;
            NexusLogistics.Log.LogInfo($"UINexusStorageWindow._OnClose: isStorageWindowOpen = {NexusLogistics.isStorageWindowOpen}");
        }

        protected override void _OnUpdate()
        {
            if (VFInput.escape && !VFInput.inputing)
            {
                VFInput.UseEscape();
                _Close();
            }

            // Refresh dashboard or list content periodically
            refreshTimer += Time.deltaTime;
            if (refreshTimer > 0.5f) // Refresh twice a second
            {
                refreshTimer = 0f;
                RefreshPanel();
            }
        }

        private void OnCategoryButtonClick(int data)
        {
            NexusLogistics.Log.LogInfo($"Category button clicked: {data}");
            selectedStorageCategory = (NexusLogistics.StorageCategory)data;
            RefreshCategoryButtons();
            RefreshPanel();
        }

        private void RefreshCategoryButtons()
        {
            foreach (var btn in categoryButtons)
            {
                btn.highlighted = (btn.data == (int)selectedStorageCategory);
            }
        }

        private void RefreshPanel()
        {
            NexusLogistics.Log.LogInfo($"Refreshing panel. Selected category: {selectedStorageCategory}");
            if (selectedStorageCategory == NexusLogistics.StorageCategory.Dashboard)
            {
                // Show dashboard, hide list
                if (dashboardPanel != null) dashboardPanel.gameObject.SetActive(true);
                if (storageListView != null) storageListView.gameObject.SetActive(false);
                RefreshDashboard();
            }
            else
            {
                // Show list, hide dashboard
                if (dashboardPanel != null) dashboardPanel.gameObject.SetActive(false);
                if (storageListView != null)
                {
                    storageListView.gameObject.SetActive(true);
                    RefreshList();
                }
            }
        }

        private void RefreshList()
        {
            if (storageListView == null) return; // Extra safety check
            NexusLogistics.RefreshStorageItemsForGUI(selectedStorageCategory);
            storageListView.SetItemCount(NexusLogistics.storageItemsForGUI.Count);
            // Reset scroll position by setting the content panel's local position
            if (storageListView.recyclingListView != null)
            {
                storageListView.recyclingListView.transform.localPosition = Vector3.zero;
            }
        }

        private void RefreshDashboard()
        {
            // This re-uses your existing logic
            var bottlenecks = NexusLogistics.cachedBottlenecks;
            if (bottlenecks.Any())
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine("Bottlenecks:\n");
                foreach (var bottleneck in bottlenecks)
                {
                    string itemName = LDB.items.Select(bottleneck.ItemId)?.name ?? "Unknown";
                    string deficitText = $"{Mathf.Abs(bottleneck.DeficitPerMinute)}/min deficit";
                    string etaText = "";
                    if (bottleneck.DeficitPerMinute < 0 && bottleneck.CurrentStock > 0)
                    {
                        double minutesToDepletion = (double)bottleneck.CurrentStock / Mathf.Abs(bottleneck.DeficitPerMinute);
                        etaText = $" (ETA: {NexusLogistics.FormatDuration(minutesToDepletion)})";
                    }
                    sb.AppendLine($"- {itemName}: {deficitText}{etaText}");
                }
                dashboardBottleneckText.text = sb.ToString();
            }
            else
            {
                dashboardBottleneckText.text = "No potential bottlenecks detected.";
            }
        }
    }
}
