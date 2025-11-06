using System;
using UnityEngine.UI;
using UnityEngine;

namespace NexusLogistics.UI
{
    public class MyUIListView : MonoBehaviour
    {
        public RecyclingListView recyclingListView;
        public ScrollRect m_ScrollRect;

        public static MyUIListView Create(string name, int width, int height, Transform parent, UINexusStorageWindow window)
        {
            // This is the correct, robust method adapted from PlanetFinder
            UIListView src = UIRoot.instance.uiGame.tutorialWindow.entryList;
            GameObject go = GameObject.Instantiate(src.gameObject);
            go.name = name;
            go.transform.SetParent(parent, false);

            // Destroy the original UIListView component as we're replacing it
            UIListView originalListView = go.GetComponent<UIListView>();
            GameObject contentGo = originalListView.m_ContentPanel.gameObject;

            MyUIListView result = go.AddComponent<MyUIListView>();
            result.m_ScrollRect = originalListView.m_ScrollRect;

            // Add the RecyclingListView component to the content panel
            result.recyclingListView = contentGo.AddComponent<RecyclingListView>();
            RectTransform contentRect = contentGo.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(0, 1);
            contentRect.pivot = new Vector2(0, 1);
            result.recyclingListView.scrollRect = result.m_ScrollRect;

            // Configure the new list view
            result.recyclingListView.ChildPrefab = UINexusStorageItem.CreatePrefab();
            result.recyclingListView.ItemCallback = (item, rowIdx) =>
            {
                UINexusStorageItem storageItem = item.GetComponent<UINexusStorageItem>();
                if (storageItem != null)
                {
                    storageItem.window = window; // Assign the parent window reference
                    storageItem.Refresh(rowIdx);
                }
            };

            // Configure the scroll rect
            result.m_ScrollRect.horizontal = false;
            result.m_ScrollRect.horizontalScrollbar?.gameObject.SetActive(false);
            result.m_ScrollRect.vertical = true;
            result.m_ScrollRect.verticalScrollbar.gameObject.SetActive(true);

            // Clean up the template's children
            for (int i = contentGo.transform.childCount - 1; i >= 0; i--)
            {
                GameObject.Destroy(contentGo.transform.GetChild(i).gameObject);
            }
            
            // Destroy the original components we no longer need
            GameObject.Destroy(originalListView);

            // Set size
            RectTransform rect = go.GetComponent<RectTransform>();
            if (width == 0 && height == 0)
            {
                // If size is 0, fill the parent container
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.sizeDelta = Vector2.zero;
                rect.anchoredPosition = Vector2.zero;
            }
            else
            {
                rect.sizeDelta = new Vector2(width, height);
            }

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
