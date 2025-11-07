using UnityEngine;
using UnityEngine.UI;
using NexusLogistics.UI; // Import the new UI utilities

namespace NexusLogistics
{
    // This class represents a single row in the storage list
    public class UINexusStorageItem : MonoBehaviour
    {
        public UINexusStorageWindow window; // Link back to the parent window
        public int itemId;

        [SerializeField] public Image itemIcon;
        [SerializeField] public Text nameText;
        [SerializeField] public Text countText;
        [SerializeField] public Text prolifText;
        [SerializeField] public InputField limitInput;

        private bool _isUpdatingLimit = false; // Prevents recursive loops on input field update

        public static UINexusStorageItem CreatePrefab()
        {
            // Create a root GameObject for the list item
            UINexusStorageItem item = UIUtil.CreateGameObject<UINexusStorageItem>("nexus-list-item", 550f, 28f);
            RectTransform baseTrans = item.transform as RectTransform;
            item.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.3f);

            // --- Configure Item Icon ---
            item.itemIcon = UIUtil.CreateImage("item-icon", baseTrans);
            UIUtil.NormalizeRectWithTopLeft(item.itemIcon, 4f, 2f, 24f, 24f);

            // --- Configure Name Text ---
            item.nameText = UIUtil.CreateText("item-name", "Item Name", 14, baseTrans);
            UIUtil.NormalizeRectWithTopLeft(item.nameText, 32f, 4f, 140f, 24f);

            // --- Configure Count Text ---
            item.countText = UIUtil.CreateText("item-count", "999.9M", 14, baseTrans);
            item.countText.alignment = TextAnchor.MiddleLeft;
            UIUtil.NormalizeRectWithTopLeft(item.countText, 180f, 4f, 100f, 24f);

            // --- Configure Proliferation Text ---
            item.prolifText = UIUtil.CreateText("prolif-text", "Mk 3 (100%)", 14, baseTrans);
            UIUtil.NormalizeRectWithTopLeft(item.prolifText, 290f, 4f, 80f, 24f);

            // --- Configure Limit InputField ---
            item.limitInput = UIUtil.CreateInputField("limit-input", "99999", 14, baseTrans);
            UIUtil.NormalizeRectWithTopLeft(item.limitInput, 380f, 2f, 100f, 24f);
            item.limitInput.textComponent.alignment = TextAnchor.MiddleRight;

            item.gameObject.SetActive(false); // Prefab should be disabled
            return item;
        }

        // Called by the list view to setup this row
        public void Refresh(int index)
        {
            _isUpdatingLimit = true; // prevent loops
            var pair = NexusLogistics.storageItemsForGUI[index];
            this.itemId = pair.Key;
            var item = pair.Value;
            var itemProto = LDB.items.Select(itemId);

            if (itemProto != null)
            {
                itemIcon.sprite = itemProto.iconSprite;
                nameText.text = itemProto.name;
            }

            countText.text = item.count.ToString("N0");

            var (prolifTextStr, prolifColor) = NexusLogistics.GetProliferationStatus(item.count, item.inc, itemId);
            prolifText.text = prolifTextStr;
            prolifText.color = prolifColor;

            // Set limit text from window's cache
            if (!window.limitInputStrings.ContainsKey(itemId))
            {
                window.limitInputStrings[itemId] = item.limit.ToString();
            }
            limitInput.text = window.limitInputStrings[itemId];

            // Add listeners for the input field
            limitInput.onValueChanged.RemoveAllListeners();
            limitInput.onEndEdit.RemoveAllListeners();
            limitInput.onValueChanged.AddListener(OnLimitInputValueChanged);
            limitInput.onEndEdit.AddListener(OnLimitInputEndEdit);

            _isUpdatingLimit = false;
        }

        // Update the cached string as the user types
        private void OnLimitInputValueChanged(string newText)
        {
            if (_isUpdatingLimit) return;
            window.limitInputStrings[itemId] = newText;
        }

        // When user presses Enter or clicks away, try to parse and save the value
        private void OnLimitInputEndEdit(string newText)
        {
            if (_isUpdatingLimit) return;

            if (int.TryParse(newText, out int newLimit) && newLimit >= 0)
            {
                // Valid number, save it
                NexusLogistics.SetItemLimit(itemId, newLimit);
                window.limitInputStrings[itemId] = newLimit.ToString();
            }
            else
            {
                // Invalid number, reset to the last saved value
                _isUpdatingLimit = true;
                limitInput.text = NexusLogistics.GetItemLimit(itemId).ToString();
                window.limitInputStrings[itemId] = limitInput.text;
                _isUpdatingLimit = false;
            }
        }
    }
}
