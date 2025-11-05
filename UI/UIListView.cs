using System;
using UnityEngine.UI;
using UnityEngine;
using UnityEngine.UI;

namespace NexusLogistics.UI
{
    public class UIListView : MonoBehaviour
    {
        public RecyclingListView recyclingListView;
        public ScrollRect m_ScrollRect;

        public static UIListView Create(MonoBehaviour preFab, RecyclingListView.ItemDelegate dlgt, string goName = "", Transform parent = null, float vsWidth = 16f)
        {
            UIListView result;
            UIListView uiListView;
            UIListView src = UIRoot.instance.uiGame.tutorialWindow.entryList;
            GameObject go = GameObject.Instantiate(src.gameObject);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            if (!string.IsNullOrEmpty(goName))
            {
                go.name = goName;
            }

            uiListView = go.GetComponent<UIListView>();
            GameObject contentGo = uiListView.m_ContentPanel.gameObject;

            result = go.AddComponent<UIListView>();
            result.recyclingListView = contentGo.AddComponent<RecyclingListView>();

            // Make a prefab out of this child panel
            if (contentGo.transform.childCount > 0)
            {
                Destroy(contentGo.transform.GetChild(0).gameObject);
                contentGo.transform.DetachChildren();
            }
            result.recyclingListView.ChildPrefab = preFab;
            result.recyclingListView.ItemCallback = dlgt;

            result.m_ScrollRect = uiListView.m_ScrollRect;
            result.recyclingListView.scrollRect = result.m_ScrollRect;

            result.recyclingListView.scrollRect.horizontalScrollbar.gameObject.SetActive(false);
            result.recyclingListView.scrollRect.verticalScrollbar.gameObject.SetActive(true);
            result.recyclingListView.scrollRect.vertical = true;
            result.recyclingListView.scrollRect.horizontal = false;
            result.recyclingListView.RowPadding = 4f;
            Image barBg = result.recyclingListView.scrollRect.verticalScrollbar.GetComponent<Image>();
            if (barBg != null)
            {
                barBg.color = new Color(0f, 0f, 0f, 0.62f);
            }

            RectTransform vsRect = result.recyclingListView.scrollRect.verticalScrollbar.transform as RectTransform;
            vsRect.sizeDelta = new Vector2(vsWidth, vsRect.sizeDelta.y);
            Destroy(uiListView.m_ContentPanel);
            Destroy(uiListView);

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
