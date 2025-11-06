
using HarmonyLib;
using UnityEngine;

namespace NexusLogistics.UI
{
    [HarmonyPatch(typeof(ManualBehaviour), "_Close")]
    public static class ManualBehaviour_Close_Patch
    {
        public static void Prefix(ManualBehaviour __instance)
        {
            if (__instance is IModWindow)
            {
                NexusLogistics.Log.LogInfo($"ManualBehaviour._Close patch called for {__instance.name}. Setting inactive.");
                __instance.gameObject.SetActive(false);
                // Manually call _OnClose because the base method is not reliable
                Traverse.Create(__instance).Method("_OnClose").GetValue();
            }
        }
    }
}
