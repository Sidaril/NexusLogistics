using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI;

namespace NexusLogistics.UI
{
    // based on https://github.com/sinbad/UnityRecyclingListView // Copyright (c) 2019 Steve Streeting
    // MIT License
    public class RecyclingListView : MonoBehaviour
    {
        public MonoBehaviour ChildPrefab;
        public float RowPadding = 4f;
        public float PreAllocHeight = 100;

        public float VerticalNormalizedPosition
        {
            get => scrollRect.verticalNormalizedPosition;
            set => scrollRect.verticalNormalizedPosition = value;
        }

        protected int rowCount;
        public int RowCount
        {
            get => rowCount;
            set
            {
                if (rowCount != value)
                {
                    rowCount = value;
                    ignoreScrollChange = true;
                    UpdateContentHeight();
                    ignoreScrollChange = false;
                    ReorganiseContent(true);
                }
            }
        }

        public delegate void ItemDelegate(MonoBehaviour item, int rowIndex);
        public ItemDelegate ItemCallback;
        public ScrollRect scrollRect;
        public MonoBehaviour[] childItems;
        protected int childBufferStart = 0;
        protected int sourceDataRowStart;
        protected bool ignoreScrollChange = false;
        protected float previousBuildHeight = 0;
        protected const int rowsAboveBelow = 1;

        public virtual void Refresh()
        {
            ReorganiseContent(true);
        }

        public virtual void Refresh(int rowStart, int count)
        {
            int sourceDataLimit = sourceDataRowStart + childItems.Length;
            for (int i = 0; i < count; ++i)
            {
                int row = rowStart + i;
                if (row < sourceDataRowStart || row >= sourceDataLimit)
                    continue;

                int bufIdx = WrapChildIndex(childBufferStart + row - sourceDataRowStart);
                if (childItems[bufIdx] != null)
                {
                    UpdateChild(childItems[bufIdx], row);
                }
            }
        }

        public virtual void Clear()
        {
            RowCount = 0;
        }

        public virtual void ScrollToRow(int row)
        {
            scrollRect.verticalNormalizedPosition = GetRowScrollPosition(row);
        }

        public float GetRowScrollPosition(int row)
        {
            float rowCentre = (row + 0.5f) * RowHeight();
            float vpHeight = ViewportHeight();
            float halfVpHeight = vpHeight * 0.5f;
            float vpTop = Mathf.Max(0, rowCentre - halfVpHeight);
            float vpBottom = vpTop + vpHeight;
            float contentHeight = scrollRect.content.sizeDelta.y;
            if (vpBottom > contentHeight)
                vpTop = Mathf.Max(0, vpTop - (vpBottom - contentHeight));
            return Mathf.InverseLerp(contentHeight - vpHeight, 0, vpTop);
        }

        protected virtual bool CheckChildItems()
        {
            float vpHeight = ViewportHeight();
            float buildHeight = Mathf.Max(vpHeight, PreAllocHeight);
            bool rebuild = childItems == null || buildHeight > previousBuildHeight;
            if (rebuild)
            {
                int childCount = Mathf.RoundToInt(0.5f + buildHeight / RowHeight());
                childCount += rowsAboveBelow * 2;

                if (childItems == null)
                    childItems = new MonoBehaviour[childCount];
                else if (childCount > childItems.Length)
                    Array.Resize(ref childItems, childCount);

                for (int i = 0; i < childItems.Length; ++i)
                {
                    if (childItems[i] == null)
                    {
                        childItems[i] = Instantiate(ChildPrefab);
                    }
                    childItems[i].GetComponent<RectTransform>().SetParent(scrollRect.content, false);
                    childItems[i].gameObject.SetActive(false);
                }

                previousBuildHeight = buildHeight;
            }

            return rebuild;
        }

        protected virtual void OnEnable()
        {
            scrollRect?.onValueChanged.AddListener(OnScrollChanged);
            ignoreScrollChange = false;
        }

        protected virtual void OnDisable()
        {
            scrollRect?.onValueChanged.RemoveListener(OnScrollChanged);
        }

        protected virtual void OnScrollChanged(Vector2 normalisedPos)
        {
            if (!ignoreScrollChange)
            {
                ReorganiseContent(false);
            }
        }

        protected virtual void ReorganiseContent(bool clearContents)
        {
            if (clearContents)
            {
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = 1;
            }

            bool childrenChanged = CheckChildItems();
            bool populateAll = childrenChanged || clearContents;

            float ymin = scrollRect.content.localPosition.y;
            ymin -= ((scrollRect.transform as RectTransform).rect.height / 2.0f);

            int firstVisibleIndex = (int)(ymin / RowHeight());
            int newRowStart = firstVisibleIndex - rowsAboveBelow;

            int diff = newRowStart - sourceDataRowStart;
            if (populateAll || Mathf.Abs(diff) >= childItems.Length)
            {
                sourceDataRowStart = newRowStart;
                childBufferStart = 0;
                int rowIdx = newRowStart;
                foreach (var item in childItems)
                {
                    UpdateChild(item, rowIdx++);
                }
            }
            else if (diff != 0)
            {
                int newBufferStart = (childBufferStart + diff) % childItems.Length;
                if (diff < 0)
                {
                    for (int i = 1; i <= -diff; ++i)
                    {
                        int bufi = WrapChildIndex(childBufferStart - i);
                        int rowIdx = sourceDataRowStart - i;
                        UpdateChild(childItems[bufi], rowIdx);
                    }
                }
                else
                {
                    int prevLastBufIdx = childBufferStart + childItems.Length - 1;
                    int prevLastRowIdx = sourceDataRowStart + childItems.Length - 1;
                    for (int i = 1; i <= diff; ++i)
                    {
                        int bufi = WrapChildIndex(prevLastBufIdx + i);
                        int rowIdx = prevLastRowIdx + i;
                        UpdateChild(childItems[bufi], rowIdx);
                    }
                }
                sourceDataRowStart = newRowStart;
                childBufferStart = newBufferStart;
            }
        }

        private int WrapChildIndex(int idx)
        {
            while (idx < 0)
                idx += childItems.Length;
            return idx % childItems.Length;
        }

        private float RowHeight()
        {
            return RowPadding + ChildPrefab.GetComponent<RectTransform>().rect.height;
        }

        private float ViewportHeight()
        {
            return (scrollRect.transform as RectTransform).rect.height * 1.5f;
        }

        protected virtual void UpdateChild(MonoBehaviour child, int rowIdx)
        {
            if (rowIdx < 0 || rowIdx >= rowCount)
            {
                child.gameObject.SetActive(false);
            }
            else
            {
                if (ItemCallback == null) return;

                var childRect = ChildPrefab.GetComponent<RectTransform>().rect;
                Vector2 pivot = ChildPrefab.GetComponent<RectTransform>().pivot;
                float ytoppos = RowHeight() * rowIdx;
                float ypos = ytoppos + (1f - pivot.y) * childRect.height;
                float xpos = 0 + pivot.x * childRect.width;
                child.GetComponent<RectTransform>().anchoredPosition = new Vector2(xpos, -ypos);

                ItemCallback(child, rowIdx);
                child.gameObject.SetActive(true);
            }
        }

        protected virtual void UpdateContentHeight()
        {
            float height = ChildPrefab.GetComponent<RectTransform>().rect.height * rowCount + (rowCount - 1) * RowPadding;
            var sz = scrollRect.content.sizeDelta;
            scrollRect.content.sizeDelta = new Vector2(sz.x, height);
        }
    }
}
