using HarmonyLib;
using System;

namespace NexusLogistics.Common
{
    public abstract class PatchImpl<T> where T : PatchImpl<T>, new()
    {
        private static Harmony _harmonyInstance;
        private static bool _isEnabled;

        protected abstract void OnEnable();
        protected virtual void OnDisable() {}

        public static void Enable(bool on)
        {
            if (_isEnabled == on) return;
            _isEnabled = on;

            if (on)
            {
                if (_harmonyInstance == null)
                {
                    _harmonyInstance = new Harmony("NexusLogistics.UI.Patch." + typeof(T).Name);
                }
                _harmonyInstance.PatchAll(typeof(T));
                
                var instance = new T();
                instance.OnEnable();
            }
            else
            {
                if (_harmonyInstance != null)
                {
                    _harmonyInstance.UnpatchSelf();
                }
                var instance = new T();
                instance.OnDisable();
            }
        }
    }
}
