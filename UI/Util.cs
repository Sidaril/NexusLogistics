using UnityEngine;
using UnityEngine.UI;

namespace NexusLogistics.UI
{

public static class Util
{
    public static void CleanText(Text txt)
    {
        if (txt == null) return;
        txt.material = Graphic.defaultGraphicMaterial;

        // Destroy all child GameObjects (e.g. shadow, outline sub-objects)
        for (int i = txt.transform.childCount - 1; i >= 0; i--)
        {
            UnityEngine.Object.DestroyImmediate(txt.transform.GetChild(i).gameObject);
        }

        // Remove unwanted components
        var shadow = txt.GetComponent<Shadow>();
        if (shadow != null) UnityEngine.Object.DestroyImmediate(shadow);
        var outline = txt.GetComponent<Outline>();
        if (outline != null) UnityEngine.Object.DestroyImmediate(outline);
        var localizer = txt.GetComponent<Localizer>();
        if (localizer != null) UnityEngine.Object.DestroyImmediate(localizer);

        var comps = txt.GetComponents<Component>();
        foreach (var comp in comps)
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;
            if (typeName != "RectTransform" && 
                typeName != "CanvasRenderer" && 
                typeName != "Text" && 
                typeName != "Shadow" &&
                typeName != "Outline")
            {
                UnityEngine.Object.DestroyImmediate(comp);
            }
        }

        // Add clean drop shadow
        var newShadow = txt.gameObject.AddComponent<Shadow>();
        newShadow.effectColor = new Color(0f, 0f, 0f, 0.5f);
        newShadow.effectDistance = new Vector2(1f, -1f);
    }

    public static RectTransform NormalizeRectWithTopLeft(Component cmp, float left, float top, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.localScale = Vector3.one;
        rect.anchorMax = new Vector2(0f, 1f);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition3D = new Vector3(Mathf.Round(left), Mathf.Round(-top), 0f);
        return rect;
    }

    public static RectTransform NormalizeRectWithTopRight(Component cmp, float right, float top, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.localScale = Vector3.one;
        rect.anchorMax = new Vector2(1f, 1f);
        rect.anchorMin = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition3D = new Vector3(Mathf.Round(-right), Mathf.Round(-top), 0f);
        return rect;
    }

    public static RectTransform NormalizeRectWithBottomLeft(Component cmp, float left, float bottom, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.localScale = Vector3.one;
        rect.anchorMax = new Vector2(0f, 0f);
        rect.anchorMin = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition3D = new Vector3(Mathf.Round(left), Mathf.Round(bottom), 0f);
        return rect;
    }

    public static RectTransform NormalizeRectWithMargin(Component cmp, float top, float left, float bottom, float right, Transform parent = null)
    {
        if (cmp.transform is not RectTransform rect) return null;
        if (parent != null)
        {
            rect.SetParent(parent, false);
        }
        rect.anchoredPosition3D = Vector3.zero;
        rect.localScale = Vector3.one;
        rect.anchorMax = Vector2.one;
        rect.anchorMin = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMax = new Vector2(Mathf.Round(-right), Mathf.Round(-top));
        rect.offsetMin = new Vector2(Mathf.Round(left), Mathf.Round(bottom));
        return rect;
    }

    public static RectTransform NormalizeRectCenter(GameObject go, float width = 0, float height = 0)
    {
        if (go.transform is not RectTransform rect) return null;
        rect.localScale = Vector3.one;
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        if (width > 0 && height > 0)
        {
            rect.sizeDelta = new Vector2(Mathf.Round(width), Mathf.Round(height));
        }
        return rect;
    }

    public static Color RGBMultiplied(this Color color, float multiplier)
    {
        return new Color(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
    }

    public static void RemoveTextEventTriggers(GameObject go)
    {
        if (go == null) return;
        var comps = go.GetComponentsInChildren<Component>(true);
        for (int i = 0; i < comps.Length; i++)
        {
            var comp = comps[i];
            if (comp != null && comp.GetType().Name == "TextEventTrigger")
            {
                UnityEngine.Object.DestroyImmediate(comp);
            }
        }
    }
}

}

