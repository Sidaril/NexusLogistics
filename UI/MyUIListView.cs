using System;
using UnityEngine.UI;
using UnityEngine;

namespace NexusLogistics.UI
{
    public class MyUIListView : MonoBehaviour
    {
        public RecyclingListView recyclingListView;
        public ScrollRect m_ScrollRect;

        public static MyUIListView Create(string name, int width, int height, Transform parent)
        {
            GameObject go = new GameObject(name);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = Vector2.zero;

            MyUIListView result = go.AddComponent<MyUIListView>();
            result.recyclingListView = go.AddComponent<RecyclingListView>();
            result.recyclingListView.ItemCallback = (item, rowIdx) =>
            {
                UINexusStorageItem storageItem = item.GetComponent<UINexusStorageItem>();
                if (storageItem != null)
                {
                    // storageItem.window = window; // Removed
                    storageItem.Refresh(rowIdx);
                }
            };
            result.recyclingListView.ChildPrefab = UINexusStorageItem.CreatePrefab();
            // result.recyclingListView.CellSize = new Vector2(width, 30); // Removed
            // result.recyclingListView.Direction = ScrollDirection.Vertical; // Removed

            return result;
        }
        public void Clear()
        {
            recyclingListView.Clear();
        }

        public void SetItemCount(int num)
        {
            recyclingListView.RowCount = num;
        }
    }
}
