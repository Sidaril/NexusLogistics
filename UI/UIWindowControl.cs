using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NexusLogistics.UI
{
    public static class UIWindowControl
    {
        public static List<IModWindow> allWindows = new List<IModWindow>();
        internal static bool _inited = false;

        public static T CreateWindow<T>(string name, string title) where T : ManualBehaviour, IModWindow
        {
            NexusLogistics.Log.LogInfo($"Attempting to create window: {name} of type {typeof(T).Name}");
            var srcWin = UIRoot.instance.uiGame.inserterWindow;
            if (srcWin == null)
            {
                NexusLogistics.Log.LogError("Source window (inserterWindow) is null! Cannot create new windows.");
                return null;
            }

            GameObject src = srcWin.gameObject;
            GameObject go = GameObject.Instantiate(src, srcWin.transform.parent);
            go.name = name;
            go.SetActive(false);
            GameObject.Destroy(go.GetComponent<UIInserterWindow>());
            NexusLogistics.Log.LogInfo($"Adding component {typeof(T).Name} to {go.name}");
            ManualBehaviour win = go.AddComponent<T>();
            if (win == null)
            {
                NexusLogistics.Log.LogError($"AddComponent<{typeof(T).Name}> returned null!");
                return null;
            }
            win._Create();

            // Iterate backwards when destroying children to avoid index issues
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                if (child.name == "panel-bg")
                {
                    // Find and hook up the close button
                    Button btn = child.GetComponentInChildren<Button>();
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => { (win as IModWindow).TryClose(); });
                    }
                    // Set the title
                    Text txt = child.GetComponentInChildren<Text>();
                    if (txt != null)
                    {
                        txt.text = title;
                    }
                }
                else
                {
                    // Destroy all other children that are not the panel-bg
                    GameObject.Destroy(child);
                }
            }
            allWindows.Add(win as IModWindow);
            NexusLogistics.Log.LogInfo($"Successfully created window: {name}");
            return win as T;
        }

        public static RectTransform GetRectTransform(ManualBehaviour win)
        {
            return win.transform as RectTransform;
        }

        public static void OpenWindow(ManualBehaviour win)
        {
            if (win == null)
            {
                NexusLogistics.Log.LogError("OpenWindow was called with a null window object!");
                return;
            }
            NexusLogistics.Log.LogInfo($"UIWindowControl.OpenWindow called for {win.name}");
            win._Open();
        }
    }
}
