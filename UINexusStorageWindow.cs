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
            if (instance != null) return;
            instance = UIWindowControl.CreateWindow<UINexusStorageWindow>("NexusStorageWindow", "Remote Storage");
        }

        public void TryClose() { _Close(); }
        public bool isFunctionWindow() { return true; }

        protected override void _OnCreate()
        {
            windowTrans = UIWindowControl.GetRectTransform(this);
            windowTrans.sizeDelta = new Vector2(600, 500);

            // --- CHANGED ---
            // Find the panel-bg to parent new UI elements to
            RectTransform panelBg = windowTrans.Find("panel-bg") as RectTransform;
            if (panelBg == null)
            {
                Logger.LogError("NexusLogistics: Could not find 'panel-bg' in UINexusStorageWindow!");
                return;
            }
            // --- END CHANGED ---

            // Create a content area
            contentTrans = UIUtil.CreateGameObject<RectTransform>("content", 600, 440);
            // --- CHANGED ---
            // Parent it to panelBg, offset from its top-left for the title bar
            UIUtil.NormalizeRectWithTopLeft(contentTrans, 0, 60, panelBg);
            // --- END CHANGED ---

            // --- Create Category Tabs ---
            float x_toolbar = 36f;
            float y_toolbar = 0f;
            float padding = 4f;
            categoryButtons.Clear();

            string[] categories = { "Dashboard", "Raw", "Intermeds", "Buildings", "Combat", "Science" };
            foreach (var categoryName in categories)
            {
                NexusLogistics.StorageCategory category = (NexusLogistics.StorageCategory)System.Enum.Parse(typeof(NexusLogistics.StorageCategory), categoryName.Replace("Intermeds", "IntermediateProducts"));

                UIButton btn = UIUtil.MakeHiliteTextButton(categoryName, 80f, 24f);
                btn.data = (int)category;
                UIUtil.NormalizeRectWithTopLeft(btn, x_toolbar, y_toolbar, contentTrans);

                categoryButtons.Add(btn);
                x_toolbar += 80f + padding;
            }

            // --- Create Dashboard Panel ---
            dashboardPanel = UIUtil.CreateGameObject<RectTransform>("dashboard-panel");
            UIUtil.NormalizeRectWithMargin(dashboardPanel, 30f, 36f, 10f, 36f, contentTrans);
            dashboardPanel.gameObject.SetActive(true); // Active by default

            dashboardBottleneckText = UIUtil.CreateText("Loading dashboard...", 14);
            UIUtil.NormalizeRectWithTopLeft(dashboardBottleneckText, 0, 0, dashboardPanel);
            dashboardBottleneckText.rectTransform.sizeDelta = new Vector2(500, 300);
            dashboardBottleneckText.verticalOverflow = VerticalWrapMode.Truncate;

            // --- Create List View Panel ---
            Image bg = UIUtil.CreateGameObject<Image>("list-bg", 100f, 100f);
            bg.color = new Color(0f, 0f, 0f, 0.56f);
            UIUtil.NormalizeRectWithMargin(bg, 30f, 36f, 10f, 20f, contentTrans);

            storageListView = MyUIListView.Create(UINexusStorageItem.CreatePrefab(), PopulateItem, "storage-list-view", bg.transform);
            UIUtil.NormalizeRectWithMargin(storageListView.transform, 0f, 0f, 0f, 0f, bg.transform);
            storageListView.m_ScrollRect.scrollSensitivity = 28f;
            storageListView.gameObject.SetActive(false); // Inactive by default
        }

        private void PopulateItem(MonoBehaviour item, int rowIndex)
        {
            (item as UINexusStorageItem).window = this;
            (item as UINexusStorageItem).Refresh(rowIndex);
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
            limitInputStrings.Clear(); // Clear cache
            RefreshCategoryButtons();
            RefreshPanel();
        }

        protected override void _OnClose() { }

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
            if (selectedStorageCategory == NexusLogistics.StorageCategory.Dashboard)
            {
                // Show dashboard, hide list
                dashboardPanel.gameObject.SetActive(true);
                storageListView.gameObject.SetActive(false);
                RefreshDashboard();
            }
            else
            {
                // Show list, hide dashboard
                dashboardPanel.gameObject.SetActive(false);
                storageListView.gameObject.SetActive(true);
                RefreshList();
            }
        }

        private void RefreshList()
        {
            NexusLogistics.RefreshStorageItemsForGUI(selectedStorageCategory);
            storageListView.SetItemCount(NexusLogistics.storageItemsForGUI.Count);
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
