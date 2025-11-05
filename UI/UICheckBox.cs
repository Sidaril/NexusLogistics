using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI;

namespace NexusLogistics.UI
{
    public class UICheckBox : MonoBehaviour
    {
        public UIButton uiButton;
        public Image checkImage;
        public RectTransform rectTrans;
        public Text labelText;
        public ConfigEntry<bool> config;

        public static RectTransform CreateCheckBox(ConfigEntry<bool> config, string label, int fontSize = 14)
        {
            UIBuildMenu buildMenu = UIRoot.instance.uiGame.buildMenu;
            UIButton src = buildMenu.uxFacilityCheck;

            GameObject go = GameObject.Instantiate(src.gameObject);
            go.name = "my-checkbox";
            UICheckBox cb = go.AddComponent<UICheckBox>();
            cb.config = config;
            RectTransform rect = go.transform as RectTransform;
            cb.rectTrans = rect;
            rect.anchorMax = Vector2.zero;
            rect.anchorMin = Vector2.zero;
            rect.anchoredPosition3D = new Vector3(0, 0, 0);

            cb.uiButton = go.GetComponent<UIButton>();
            cb.checkImage = go.transform.Find("checked")?.GetComponent<Image>();

            Transform child = go.transform.Find("text");
            if (child != null)
            {
                GameObject.DestroyImmediate(child.GetComponent<Localizer>());
                cb.labelText = child.GetComponent<Text>();
                cb.labelText.fontSize = fontSize;
                cb.SetLabelText(label);
            }

            cb.uiButton.onClick += cb.OnClick;
            cb.SettingChanged();
            config.SettingChanged += (sender, args) =>
            {
                cb.SettingChanged();
            };

            return cb.rectTrans;
        }

        public void SetLabelText(string val)
        {
            if (labelText != null)
            {
                labelText.text = val;
            }
        }

        public void SettingChanged()
        {
            if (config.Value != checkImage.enabled)
            {
                checkImage.enabled = config.Value;
            }
        }

        public void OnClick(int obj)
        {
            config.Value = !config.Value;
        }
    }
}
