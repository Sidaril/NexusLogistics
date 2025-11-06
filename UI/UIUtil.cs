using UnityEngine;
using UnityEngine.UI;

namespace NexusLogistics.UI
{
    public class UIUtil
    {
        // Helper to create a new, empty GameObject with a RectTransform
        public static T CreateGameObject<T>(string name, float width = 0f, float height = 0f) where T : Component
        {
            GameObject go = new GameObject(name);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.localScale = Vector3.one;
            if (width > 0f || height > 0)
            {
                rect.sizeDelta = new Vector2(width, height);
            }
            T item = go.GetComponent<T>();
            if (item == null)
            {
                item = go.AddComponent<T>();
            }
            return item;
        }

        // Positions a UI element relative to its parent's Top-Left corner
        public static RectTransform NormalizeRectWithTopLeft(Component cmp, float left, float top, Transform parent = null)
        {
            RectTransform rect = cmp.transform as RectTransform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }
            rect.anchorMax = new Vector2(0f, 1f);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition3D = new Vector3(left, -top, 0f);
            return rect;
        }

        // Positions a UI element to fill its parent with margins
        public static RectTransform NormalizeRectWithMargin(Component cmp, float top, float left, float bottom, float right, Transform parent = null)
        {
            RectTransform rect = cmp.transform as RectTransform;
            if (parent != null)
            {
                rect.SetParent(parent, false);
            }
            rect.anchoredPosition3D = Vector3.zero;
            rect.localScale = Vector3.one;
            rect.anchorMax = Vector2.one;
            rect.anchorMin = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMax = new Vector2(-right, -top);
            rect.offsetMin = new Vector2(left, bottom);
            return rect;
        }

        // Creates a standard Text label by cloning a game prefab
        public static Text CreateText(string label, int fontSize = 14, string objectName = "text")
        {
            Text stateText = UIRoot.instance.uiGame.assemblerWindow.stateText;
            Text txt_ = GameObject.Instantiate<Text>(stateText);
            txt_.gameObject.name = objectName;
            txt_.text = label;
            txt_.color = new Color(1f, 1f, 1f, 0.4f);
            txt_.alignment = TextAnchor.MiddleLeft;
            txt_.fontSize = fontSize;
            return txt_;
        }

        // Creates a button with a highlightable text (like a tab)
        public static UIButton MakeHiliteTextButton(string label, float width = 0f, float height = 0f)
        {
            UIDESwarmPanel swarmPanel = UIRoot.instance.uiGame.dysonEditor.controlPanel.hierarchy.swarmPanel;
            UIButton src = swarmPanel.orbitButtons[0];
            UIButton btn = GameObject.Instantiate<UIButton>(src);

            if (btn.transitions.Length >= 2)
            {
                btn.transitions[0].normalColor = new Color(0.1f, 0.1f, 0.1f, 0.68f);
                btn.transitions[0].highlightColorOverride = new Color(0.9906f, 0.5897f, 0.3691f, 0.4f);
                btn.transitions[1].normalColor = new Color(1f, 1f, 1f, 0.6f);
                btn.transitions[1].highlightColorOverride = new Color(0.2f, 0.1f, 0.1f, 0.9f);
            }
            Text btnText = btn.transform.Find("Text").GetComponent<Text>();
            btnText.text = label;
            btnText.fontSize = 14;

            btn.transform.Find("frame")?.gameObject.SetActive(false);
            RectTransform btnRect = btn.transform as RectTransform;
            if (width == 0f || height == 0f)
            {
                btnRect.sizeDelta = new Vector2(btnText.preferredWidth + 14f, 22f);
            }
            else
            {
                btnRect.sizeDelta = new Vector2(width, height);
            }
            return btn;
        }
    }
}
