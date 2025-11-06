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
            var srcWin = UIRoot.instance.uiGame.inserterWindow;
            GameObject src = srcWin.gameObject;
            GameObject go = GameObject.Instantiate(src, srcWin.transform.parent);
            go.name = name;
            go.SetActive(false);
            GameObject.Destroy(go.GetComponent<UIInserterWindow>());
            NexusLogistics.Log.LogInfo($"Adding component {typeof(T).Name} to {go.name}");
            ManualBehaviour win = go.AddComponent<T>();
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
            return win as T;
        }

        public static RectTransform GetRectTransform(ManualBehaviour win)
        {
            return win.transform as RectTransform;
        }

        public static void OpenWindow(ManualBehaviour win)
        {
            NexusLogistics.Log.LogInfo($"UIWindowControl.OpenWindow called for {win.name}");
            win._Open();
        }

        public static class Patch
        {
            [HarmonyPostfix]
            [HarmonyPatch(typeof(UIGame), "_OnCreate")]
            public static void UIGame__OnCreate_Postfix()
            {
                _inited = true;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(UIGame), "_OnDestroy")]
            public static void UIGame__OnDestroy_Postfix()
            {
                for (int i = 0; i < allWindows.Count; i++)
                {
                    allWindows[i].TryClose();
                }
                allWindows.Clear();
                _inited = false;
            }

            [HarmonyPostfix]
            [HarmonyPatch(typeof(UIGame), "_OnUpdate")]
            public static void UIGame_Update_Postfix()
            {
                if (VFInput.escape && !VFInput.inputing)
                {
                    for (int i = 0; i < allWindows.Count; i++)
                    {
                        if (allWindows[i].isFunctionWindow())
                        {
                            allWindows[i].TryClose();
                        }
                    }
                }
            }
        }
    }
}
