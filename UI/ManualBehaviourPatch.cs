
using HarmonyLib;
using UnityEngine;

namespace NexusLogistics.UI
{
    [HarmonyPatch(typeof(ManualBehaviour), "_Open")]
    public static class ManualBehaviour_Open_Patch
    {
        public static bool Prefix(ManualBehaviour __instance)
        {
            if (__instance is IModWindow)
            {
                NexusLogistics.Log.LogInfo($"ManualBehaviour._Open patch called for {__instance.name}. Setting active and calling lifecycle methods.");
                __instance.gameObject.SetActive(true);

                // Manually call lifecycle methods that the game is failing to call
                var traverse = Traverse.Create(__instance);
                traverse.Method("_OnRegEvent").GetValue();
                traverse.Method("_OnOpen").GetValue();

                return false; // Skip the original _Open method entirely
            }
            return true; // Run the original method for other windows
        }
    }
}
