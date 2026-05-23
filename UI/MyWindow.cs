using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using NexusLogistics.Common;

namespace NexusLogistics.UI
{

// MyWindow modified from LSTM: https://github.com/hetima/DSP_LSTM/blob/main/LSTM/MyWindowCtl.cs

public class MyWindow : ManualBehaviour
{
    private float _maxX;
    protected float MaxY;
    protected const float TitleHeight = 48f;
    protected const float TabWidth = 105f;
    protected const float TabHeight = 27f;
    protected const float Margin = 30f;
    protected const float Spacing = 10f;
    public event Action OnFree;
    private static GameObject _baseObject;

    public static void InitBaseObject()
    {
        if (_baseObject) return;
        var go = Instantiate(UIRoot.instance.uiGame.inserterWindow.gameObject);
        go.SetActive(false);
        go.name = "my-window";

        // Grab UIInserterWindow before destroying it so we can access its field references
        // to clean up filter/status UI children that live inside panel-bg
        var inserterWin = go.GetComponent<UIInserterWindow>();
        if (inserterWin != null)
        {
            var toDestroy = new System.Collections.Generic.List<GameObject>();
            
            if (inserterWin.filterButton != null) toDestroy.Add(inserterWin.filterButton.gameObject);
            if (inserterWin.resetFilterButton != null) toDestroy.Add(inserterWin.resetFilterButton.gameObject);
            if (inserterWin.takeBackButton != null) toDestroy.Add(inserterWin.takeBackButton.gameObject);
            if (inserterWin.powerIcon != null) toDestroy.Add(inserterWin.powerIcon.gameObject);
            if (inserterWin.powerText != null) toDestroy.Add(inserterWin.powerText.gameObject);
            if (inserterWin.stateText != null) toDestroy.Add(inserterWin.stateText.gameObject);
            if (inserterWin.lengthText != null) toDestroy.Add(inserterWin.lengthText.gameObject);
            if (inserterWin.rttText != null) toDestroy.Add(inserterWin.rttText.gameObject);
            if (inserterWin.filterIcon != null) toDestroy.Add(inserterWin.filterIcon.gameObject);
            if (inserterWin.filterText != null) toDestroy.Add(inserterWin.filterText.gameObject);
            if (inserterWin.stackCountText != null) toDestroy.Add(inserterWin.stackCountText.gameObject);
            
            if (inserterWin.filterIncs != null)
            {
                foreach (var img in inserterWin.filterIncs)
                {
                    if (img != null) toDestroy.Add(img.gameObject);
                }
            }

            foreach (var obj in toDestroy)
            {
                if (obj != null)
                {
                    DestroyImmediate(obj);
                }
            }

            DestroyImmediate(inserterWin);
        }

        // Destroy all top-level children except panel-bg and shadow
        for (var i = go.transform.childCount - 1; i >= 0; i--)
        {
            var child = go.transform.GetChild(i).gameObject;
            if (child.name != "panel-bg" && child.name != "shadow")
            {
                DestroyImmediate(child);
            }
        }

        var panelBg = go.transform.Find("panel-bg");
        if (panelBg != null)
        {
            // Make panel-bg stretch to fill the parent window
            var panelBgRect = panelBg.GetComponent<RectTransform>();
            if (panelBgRect != null)
            {
                panelBgRect.anchorMin = Vector2.zero;
                panelBgRect.anchorMax = Vector2.one;
                panelBgRect.offsetMin = Vector2.zero;
                panelBgRect.offsetMax = Vector2.zero;
            }

            // Clean up static text labels under panel-bg
            var closeBtn = panelBg.gameObject.GetComponentInChildren<Button>();
            var closeBtnGo = closeBtn != null ? closeBtn.gameObject : null;
            for (var i = panelBg.childCount - 1; i >= 0; i--)
            {
                var child = panelBg.GetChild(i).gameObject;
                if (child.name == "title-text" || child == closeBtnGo)
                {
                    continue;
                }
                if (closeBtnGo != null && child.transform.IsChildOf(closeBtnGo.transform))
                {
                    continue;
                }
                if (child.GetComponentInChildren<Text>() != null)
                {
                    DestroyImmediate(child);
                }
            }
        }

        // Strip Youthcat TextEventTrigger components immediately
        Util.RemoveTextEventTriggers(go);

        // Fix UIWindowDrag NRE when dragTrigger reference is destroyed/null
        FixWindowDrag(go);

        _baseObject = go;
    }

    public static void FixWindowDrag(GameObject go)
    {
        if (go == null) return;
        var drag = go.GetComponent<UIWindowDrag>();
        if (drag != null)
        {
            var field = typeof(UIWindowDrag).GetField("dragTrigger", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (field != null)
            {
                var dragTarget = go.transform.Find("panel-bg/title-text") ?? go.transform.Find("panel-bg") ?? go.transform;
                if (dragTarget != null)
                {
                    var oldComponents = dragTarget.gameObject.GetComponents<Component>();
                    foreach (var comp in oldComponents)
                    {
                        if (comp != null && comp.GetType().Name == "TextEventTrigger")
                        {
                            DestroyImmediate(comp);
                        }
                    }

                    var trigger = dragTarget.gameObject.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                    if (trigger != null && trigger.GetType() != typeof(UnityEngine.EventSystems.EventTrigger))
                    {
                        DestroyImmediate(trigger);
                        trigger = null;
                    }
                    if (trigger == null)
                    {
                        trigger = dragTarget.gameObject.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                    }
                    field.SetValue(drag, trigger);
                }
            }
        }
    }

    public static T Create<T>(string name, string title = "") where T : MyWindow
    {
        if (_baseObject == null) MyWindowManager.InitBaseObjects();
        var go = Instantiate(_baseObject, UIRoot.instance.uiGame.transform.parent);
        go.name = name;
        go.SetActive(false);

        // Defensive: strip TextEventTriggers from the clone first!
        Util.RemoveTextEventTriggers(go);

        // Fix UIWindowDrag NRE on the cloned object
        FixWindowDrag(go);
        MyWindow win = go.AddComponent<T>();
        if (!win) return null;

        var btn = go.transform.Find("panel-bg")?.gameObject.GetComponentInChildren<Button>();
        if (btn) btn.onClick.AddListener(win._Close);

        win.SetTitle(title);
        win._Create();
        if (MyWindowManager.Initialized)
        {
            win._Init(win.data);
        }
        return (T)win;
    }

    protected override void _OnOpen()
    {
        // Catch-all: strip TextEventTriggers from any UI elements that were
        // cloned after Create<T> (e.g. by UINexusLogisticsWindow.BuildUI)
        Util.RemoveTextEventTriggers(gameObject);
        AutoFitWindowSize();
    }

    protected override void _OnFree()
    {
        OnFree?.Invoke();
    }

    public virtual void TryClose()
    {
        _Close();
    }

    public virtual bool IsWindowFunctional()
    {
        return true;
    }

    public void Open()
    {
        FixWindowDrag(gameObject);
        _Open();
        transform.SetAsLastSibling();
    }

    public void Close() => _Close();

    public void SetTitle(string title)
    {
        var txt = gameObject.transform.Find("panel-bg/title-text")?.gameObject.GetComponent<Text>();
        if (txt)
        {
            txt.text = title.Translate();
        }
    }

    public virtual void AutoFitWindowSize()
    {
        var trans = GetComponent<RectTransform>();
        trans.sizeDelta = new Vector2(_maxX + Margin + TabWidth + Spacing + Margin, MaxY + TitleHeight + Margin);
        // var panelBgTrans = trans.Find("panel-bg")?.GetComponent<RectTransform>();
        // if (panelBgTrans != null)
        // {
        //     panelBgTrans.anchorMax = new Vector2(0f, 1f);
        //     panelBgTrans.anchorMin = new Vector2(0f, 1f);
        //     panelBgTrans.pivot = new Vector2(0f, 1f);
        //     panelBgTrans.localPosition = new Vector3(0f, 0f, 0f);
        //     panelBgTrans.sizeDelta = new Vector2(_maxX + Margin + TabWidth + Spacing + Margin, MaxY + TitleHeight + Margin);
        // }
    }

    private static void AddElement(float x, float y, RectTransform rect, RectTransform parent = null)
    {
        if (rect != null)
        {
            Util.NormalizeRectWithTopLeft(rect, x, y, parent);
        }
    }

    public static Text AddText(float x, float y, RectTransform parent, string label, int fontSize = 14, string objName = "label")
    {
        var src = UIRoot.instance.uiGame.assemblerWindow.stateText;
        var txt = Instantiate(src);
        Util.CleanText(txt);

        txt.gameObject.name = objName;
        txt.text = label.Translate();
        txt.color = new Color(1f, 1f, 1f, 0.4f);
        txt.alignment = TextAnchor.MiddleLeft;
        txt.fontSize = fontSize;
        txt.rectTransform.sizeDelta = new Vector2(txt.preferredWidth + 8f, txt.preferredHeight + 8f);
        AddElement(x, y, txt.rectTransform, parent);
        txt.maskable = true;
        return txt;
    }

    public Text AddText2(float x, float y, RectTransform parent, string label, int fontSize = 14, string objName = "label")
    {
        var text = AddText(x, y, parent, label, fontSize, objName);
        _maxX = Math.Max(_maxX, x + text.rectTransform.sizeDelta.x);
        MaxY = Math.Max(MaxY, y + text.rectTransform.sizeDelta.y);
        return text;
    }

    public static UIButton AddTipsButton(float x, float y, RectTransform parent, string label, string tip, string content, string objName = "tips-button")
    {
        var src = UIRoot.instance.galaxySelect.sandboxToggle.gameObject.transform.parent.Find("tip-button");
        var dst = Instantiate(src);
        dst.gameObject.name = objName;
        var btn = dst.GetComponent<UIButton>();
        Util.NormalizeRectWithTopLeft(btn, x, y, parent);
        btn.tips.topLevel = true;
        btn.tips.tipTitle = label;
        btn.tips.tipText = tip;
        btn.UpdateTip();
        return btn;
    }

    public UIButton AddTipsButton2(float x, float y, RectTransform parent, string label, string tip, string content, string objName = "tips-button")
    {
        var tipsButton = AddTipsButton(x, y, parent, label, tip, content, objName);
        var rect = tipsButton.transform as RectTransform;
        if (rect != null)
        {
            _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
            MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        }

        return tipsButton;
    }

    public UIButton AddButton(float x, float y, RectTransform parent, string text = "", int fontSize = 16, string objName = "button", UnityAction onClick = null)
    {
        return AddButton(x, y, 150f, parent, text, fontSize, objName, onClick);
    }

    public UIButton AddButton(float x, float y, float width, RectTransform parent, string text = "", int fontSize = 16, string objName = "button", UnityAction onClick = null)
    {
        var panel = UIRoot.instance.uiGame.statWindow.performancePanelUI;
        var btn = Instantiate(panel.cpuActiveButton);
        btn.gameObject.name = objName;
        var rect = Util.NormalizeRectWithTopLeft(btn, x, y, parent);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        
        var images = btn.GetComponentsInChildren<Image>(true);
        foreach (var img in images) img.maskable = true;
        
        var l = btn.gameObject.transform.Find("button-text").GetComponent<Localizer>();
        var t = btn.gameObject.transform.Find("button-text").GetComponent<Text>();
        if (l != null)
        {
            l.stringKey = text;
            l.translation = text.Translate();
        }

        if (t != null)
        {
            Util.CleanText(t);
            t.text = text.Translate();
            t.maskable = true;
        }

        t.fontSize = fontSize;
        typeof(UIButton).GetField("tip", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(btn, null);
        btn.tips = new UIButton.TipSettings();
        btn.button.onClick.RemoveAllListeners();
        if (onClick != null) btn.button.onClick.AddListener(onClick);

        _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
        MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        return btn;
    }

    public MyFlatButton AddFlatButton(float x, float y, RectTransform parent, string text = "", int fontSize = 12, string objName = "button", UnityAction onClick = null)
    {
        var btn = MyFlatButton.CreateFlatButton(x, y, parent, text, fontSize, _ => onClick());
        btn.gameObject.name = objName;

        _maxX = Math.Max(_maxX, x + btn.Width);
        MaxY = Math.Max(MaxY, y + btn.Height);
        return btn;
    }

    public MyCheckBox AddCheckBox(float x, float y, RectTransform parent, ConfigEntry<bool> config, string label = "", int fontSize = 15)
    {
        var cb = MyCheckBox.CreateCheckBox(x, y, parent, config, label, fontSize);
        _maxX = Math.Max(_maxX, x + cb.Width);
        MaxY = Math.Max(MaxY, y + cb.Height);
        return cb;
    }

    public MyComboBox AddComboBox(float x, float y, RectTransform parent, int fontSize = 15)
    {
        var comboBox = MyComboBox.CreateComboBox(x, y, parent).WithFontSize(fontSize);
        _maxX = Math.Max(_maxX, x + comboBox.Width);
        MaxY = Math.Max(MaxY, y + comboBox.Height);
        return comboBox;
    }

    public MyCornerComboBox AddCornerComboBox(float x, float y, RectTransform parent, int fontSize = 15)
    {
        var comboBox = MyCornerComboBox.CreateComboBox(x, y, parent).WithFontSize(fontSize);
        _maxX = Math.Max(_maxX, x + comboBox.Width);
        MaxY = Math.Max(MaxY, y + comboBox.Height);
        return comboBox;
    }

    #region Slider
    public class ValueMapper<T>
    {
        public virtual int Min => 1;
        public virtual int Max => 100;
        public virtual int ValueToIndex(T value) => (int)Convert.ChangeType(value, typeof(int), CultureInfo.InvariantCulture);
        public virtual T IndexToValue(int index) => (T)Convert.ChangeType(index, typeof(T), CultureInfo.InvariantCulture);

        public virtual string FormatValue(string format, T value)
        {
            return string.Format($"{{0:{format}}}", value);
        }
    }

    public class RangeValueMapper<T> : ValueMapper<T>
    {
        private int _min;
        private int _max;
        public RangeValueMapper(int min, int max)
        {
            _min = min;
            _max = max;
        }
        public override int Min => _min;
        public override int Max => _max;
    }

    public class RangeValueWithMultiplierMapper<T> : ValueMapper<T>
    {
        private int _min;
        private int _max;
        private T _multiplier;
        public RangeValueWithMultiplierMapper(int min, int max, T multiplier)
        {
            _min = min;
            _max = max;
            _multiplier = multiplier;
        }
        public override int Min => _min;
        public override int Max => _max;

        public override T IndexToValue(int index)
        {
            return (T)Convert.ChangeType((float)index * (float)Convert.ChangeType(_multiplier, typeof(float)), typeof(T));
        }

        public override int ValueToIndex(T value)
        {
            return Mathf.RoundToInt((float)Convert.ChangeType(value, typeof(float)) / (float)Convert.ChangeType(_multiplier, typeof(float)));
        }
    }

    private class ArrayMapper<T> : ValueMapper<T>
    {
        private readonly T[] _values;

        public ArrayMapper(T[] values)
        {
            Array.Sort(values);
            _values = values;
        }

        public override int Min => 0;
        public override int Max => _values.Length - 1;

        public override int ValueToIndex(T value)
        {
            return Array.BinarySearch(_values, value);
        }

        public override T IndexToValue(int index)
        {
            return _values[index >= 0 && index < _values.Length ? index : 0];
        }
    }

    public MySlider AddSlider(float x, float y, RectTransform parent, float value, float minValue, float maxValue, string format = "G", float width = 0f)
    {
        var slider = MySlider.CreateSlider(x, y, parent, value, minValue, maxValue, format, width);
        var rect = slider.rectTrans;
        if (rect != null)
        {
            _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
            MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        }

        return slider;
    }

    public MySideSlider AddSideSlider(float x, float y, RectTransform parent, float value, float minValue, float maxValue, string format = "G", float width = 0f, float textWidth = 0f)
    {
        var slider = MySideSlider.CreateSlider(x, y, parent, value, minValue, maxValue, format, width, textWidth);
        var rect = slider.rectTrans;
        if (rect != null)
        {
            _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
            MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        }

        return slider;
    }

    public MySlider AddSlider<T>(float x, float y, RectTransform parent, ConfigEntry<T> config, ValueMapper<T> valueMapper, string format = "G", float width = 0f)
    {
        var slider = MySlider.CreateSlider(x, y, parent, OnConfigValueChanged(config), valueMapper.Min, valueMapper.Max, format, width);
        slider.SetLabelText(valueMapper.FormatValue(format, config.Value));
        config.SettingChanged += SettingsChanged;
        OnFree += () => config.SettingChanged -= SettingsChanged;
        slider.OnValueChanged += () =>
        {
            var index = Mathf.RoundToInt(slider.Value);
            config.Value = valueMapper.IndexToValue(index);
            slider.SetLabelText(valueMapper.FormatValue(format, config.Value));
        };

        var rect = slider.rectTrans;
        if (rect != null)
        {
            _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
            MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        }

        return slider;

        void SettingsChanged(object o, EventArgs a)
        {
            var index = OnConfigValueChanged(config);
            slider.Value = index;
            slider.SetLabelText(valueMapper.FormatValue(format, config.Value));
        }

        int OnConfigValueChanged(ConfigEntry<T> conf)
        {
            var index = valueMapper.ValueToIndex(conf.Value);
            if (index >= 0) return index;
            index = ~index;
            index = Math.Max(0, Math.Min(valueMapper.Max, index));
            conf.Value = valueMapper.IndexToValue(index);
            return index;
        }
    }

    public MySideSlider AddSideSlider<T>(float x, float y, RectTransform parent, ConfigEntry<T> config, ValueMapper<T> valueMapper, string format = "G", float width = 0f, float textWidth = 0f)
    {
        var slider = MySideSlider.CreateSlider(x, y, parent, OnConfigValueChanged(config), valueMapper.Min, valueMapper.Max, format, width, textWidth);
        slider.SetLabelText(valueMapper.FormatValue(format, config.Value));
        config.SettingChanged += SettingsChanged;
        OnFree += () => config.SettingChanged -= SettingsChanged;
        slider.OnValueChanged += () =>
        {
            var index = Mathf.RoundToInt(slider.Value);
            config.Value = valueMapper.IndexToValue(index);
            slider.SetLabelText(valueMapper.FormatValue(format, config.Value));
        };

        var rect = slider.rectTrans;
        if (rect != null)
        {
            _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
            MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        }

        return slider;

        void SettingsChanged(object o, EventArgs a)
        {
            var index = OnConfigValueChanged(config);
            slider.Value = index;
            slider.SetLabelText(valueMapper.FormatValue(format, config.Value));
        }
        int OnConfigValueChanged(ConfigEntry<T> conf)
        {
            var index = valueMapper.ValueToIndex(conf.Value);
            if (index >= 0) return index;
            index = ~index;
            index = Math.Max(0, Math.Min(valueMapper.Max, index));
            conf.Value = valueMapper.IndexToValue(index);
            return index;
        }
    }

    public MySlider AddSlider<T>(float x, float y, RectTransform parent, ConfigEntry<T> config, T[] valueList, string format = "G", float width = 0f)
    {
        return AddSlider(x, y, parent, config, new ArrayMapper<T>(valueList), format, width);
    }

    public MySideSlider AddSideSlider<T>(float x, float y, RectTransform parent, ConfigEntry<T> config, T[] valueList, string format = "G", float width = 0f)
    {
        return AddSideSlider(x, y, parent, config, new ArrayMapper<T>(valueList), format, width);
    }
    #endregion

    public InputField AddInputField(float x, float y, RectTransform parent, string text = "", int fontSize = 16, string objName = "input", UnityAction<string> onChanged = null,
        UnityAction<string> onEditEnd = null)
    {
        var stationWindow = UIRoot.instance.uiGame.stationWindow;
        var inputField = Instantiate(stationWindow.nameInput);
        inputField.gameObject.name = objName;
        Destroy(inputField.GetComponent<UIButton>());
        inputField.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        var rect = Util.NormalizeRectWithTopLeft(inputField, x, y, parent);
        rect.sizeDelta = new Vector2(210, rect.sizeDelta.y);
        inputField.text = text;
        
        if (inputField.textComponent != null)
        {
            Util.CleanText(inputField.textComponent);
            inputField.textComponent.fontSize = fontSize;
            inputField.textComponent.maskable = true;
        }
        if (inputField.placeholder != null && inputField.placeholder is Text pText)
        {
            Util.CleanText(pText);
        }
        var img = inputField.GetComponent<Image>();
        if (img != null) img.maskable = true;

        inputField.onValueChanged.RemoveAllListeners();
        if (onChanged != null) inputField.onValueChanged.AddListener(onChanged);
        inputField.onEndEdit.RemoveAllListeners();
        if (onEditEnd != null) inputField.onEndEdit.AddListener(onEditEnd);

        _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
        MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        return inputField;
    }

    public InputField AddInputField(float x, float y, float width, RectTransform parent, ConfigEntry<string> config, int fontSize = 16, string objName = "input")
    {
        var stationWindow = UIRoot.instance.uiGame.stationWindow;
        var inputField = Instantiate(stationWindow.nameInput);
        inputField.gameObject.name = objName;
        Destroy(inputField.GetComponent<UIButton>());
        inputField.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);
        var rect = Util.NormalizeRectWithTopLeft(inputField, x, y, parent);
        rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
        inputField.text = config.Value;
        
        if (inputField.textComponent != null)
        {
            Util.CleanText(inputField.textComponent);
            inputField.textComponent.fontSize = fontSize;
            inputField.textComponent.maskable = true;
        }
        if (inputField.placeholder != null && inputField.placeholder is Text pText)
        {
            Util.CleanText(pText);
        }
        var img = inputField.GetComponent<Image>();
        if (img != null) img.maskable = true;

        inputField.onValueChanged.RemoveAllListeners();
        inputField.onEndEdit.RemoveAllListeners();
        inputField.onEndEdit.AddListener(value => config.Value = value);

        _maxX = Math.Max(_maxX, x + rect.sizeDelta.x);
        MaxY = Math.Max(MaxY, y + rect.sizeDelta.y);
        return inputField;
    }
}

public class MyWindowWithTabs : MyWindow
{
    private readonly List<Tuple<RectTransform, UIButton>> _tabs = new List<Tuple<RectTransform, UIButton>>();
    private float _tabY = 54f;

    public override void TryClose()
    {
        _Close();
    }

    public override bool IsWindowFunctional()
    {
        return true;
    }

    private RectTransform AddTabInternal(float y, int index, RectTransform parent, string label)
    {
        var tab = new GameObject();
        var tabRect = tab.AddComponent<RectTransform>();
        Util.NormalizeRectWithMargin(tabRect, TitleHeight, Margin + TabWidth + Spacing, 0f, 0f, parent);
        tab.name = "tab-" + index;
        var swarmPanel = UIRoot.instance.uiGame.dysonEditor.controlPanel.hierarchy.swarmPanel;
        var src = swarmPanel.orbitButtons[0];
        var btn = Instantiate(src);
        btn.gameObject.GetComponent<Image>().sprite = swarmPanel.buttonDefaultSprite;
        btn.name = "tab-btn-" + index;
        btn.highlighted = false;

        var btnRect = Util.NormalizeRectWithTopLeft(btn, Margin, y, parent);
        btnRect.sizeDelta = new Vector2(TabWidth, TabHeight);
        btn.transform.Find("frame").gameObject.SetActive(false);
        if (btn.transitions.Length >= 3)
        {
            btn.transitions[0].normalColor = new Color(0.1f, 0.1f, 0.1f, 0.68f);
            btn.transitions[0].highlightColorOverride = new Color(0.9906f, 0.5897f, 0.3691f, 0.4f);
            btn.transitions[1].normalColor = new Color(1f, 1f, 1f, 0.8f);
            btn.transitions[1].highlightColorOverride = new Color(0.2f, 0.2f, 0.2f, 1f);
        }

        var textTrans = btn.transform.Find("Text");
        var btnText = textTrans.GetComponent<Text>();
        if (btnText != null)
        {
            Util.CleanText(btnText);
            btnText.text = label.Translate();
            btnText.fontSize = 16;
        }
        btn.data = index;

        _tabs.Add(Tuple.Create(tabRect, btn));
        btn.onClick += OnTabButtonClick;

        MaxY = Math.Max(MaxY, y + TabHeight);
        return tabRect;
    }

    public RectTransform AddTab(RectTransform parent, string label)
    {
        var result = AddTabInternal(_tabY, _tabs.Count, parent, label);
        _tabY += 28f;
        return result;
    }

    public void AddSplitter(RectTransform parent, float spacing)
    {
        var img = Instantiate(UIRoot.instance.optionWindow.transform.Find("tab-line").Find("bar"));
        Destroy(img.Find("tri").gameObject);
        _tabY += spacing;
        var rect = Util.NormalizeRectWithTopLeft(img, 28, _tabY, parent);
        rect.sizeDelta = new Vector2(107, 2);
        _tabY += 2;
    }

    public void AddTabGroup(RectTransform parent, string label, string objName = "tabl-group-label")
    {
        AddText(28, _tabY - 2, parent, label, 16, objName);
        _tabY += 28f;
    }

    public int ActiveTabIdx { get; private set; } = 0;

    public void SetCurrentTab(int index) => OnTabButtonClick(index);

    protected virtual void OnTabButtonClick(int index)
    {
        ActiveTabIdx = index;
        foreach (var (rectTransform, btn) in _tabs)
        {
            if (btn.data != index)
            {
                btn.highlighted = false;
                rectTransform.gameObject.SetActive(false);
                continue;
            }

            btn.highlighted = true;
            rectTransform.gameObject.SetActive(true);
        }
    }
}

public abstract class MyWindowManager
{
    private static readonly List<ManualBehaviour> Windows = new List<ManualBehaviour>(4);

    public static bool Initialized { get; private set; }

    public static void Enable(bool on)
    {
        Patch.Enable(on);
    }

    public static void InitBaseObjects()
    {
        MyWindow.InitBaseObject();
        MyCheckButton.InitBaseObject();
        MyCheckBox.InitBaseObject();
        MyComboBox.InitBaseObject();
        MyCornerComboBox.InitBaseObject();
        MyFlatButton.InitBaseObject();
    }

    public static T CreateWindow<T>(string name, string title = "") where T : MyWindow
    {
        var win = MyWindow.Create<T>(name, title);
        if (win) Windows.Add(win);
        return win;
    }

    public static void DestroyWindow(ManualBehaviour win)
    {
        if (win == null) return;
        Windows.Remove(win);
        win._Free();
        win._Destroy();
    }

    /*
    public static void SetRect(ManualBehaviour win, RectTransform rect)
    {
        var rectTransform = win.GetComponent<RectTransform>();
        //rectTransform.position =
        //rectTransform.sizeDelta = rect;
    }
    */

    public class Patch : PatchImpl<Patch>
    {
        protected override void OnEnable()
        {
            InitAllWindows();
        }

        private static void InitAllWindows()
        {
            if (Initialized) return;
            if (!UIRoot.instance) return;
            foreach (var win in Windows)
            {
                win._Init(win.data);
            }
            Initialized = true;
        }

        /*
        //_Create -> _Init
        [HarmonyPostfix, HarmonyPatch(typeof(UIGame), nameof(UIGame._OnCreate))]
        public static void UIGame__OnCreate_Postfix()
        {
        }
        */

        [HarmonyPostfix, HarmonyPatch(typeof(UIRoot), "_OnDestroy")]
        public static void UIRoot__OnDestroy_Postfix()
        {
            foreach (var win in Windows)
            {
                win._Free();
                win._Destroy();
            }

            Windows.Clear();
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIRoot), "_OnOpen")]
        public static void UIRoot__OnOpen_Postfix()
        {
            InitAllWindows();
        }

        /*
        [HarmonyPostfix, HarmonyPatch(typeof(UIGame), nameof(UIGame._OnFree))]
        public static void UIGame__OnFree_Postfix()
        {
            foreach (var win in Windows)
            {
                win._Free();
            }
        }
        */

        [HarmonyPostfix, HarmonyPatch(typeof(UIRoot), "_OnUpdate")]
        public static void UIRoot__OnUpdate_Postfix()
        {
            if (GameMain.isPaused || !GameMain.isRunning)
            {
                return;
            }

            foreach (var win in Windows)
            {
                win._Update();
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(UIGame), nameof(UIGame.ShutAllFunctionWindow))]
        public static void UIGame_ShutAllFunctionWindow_Postfix()
        {
            foreach (var win in Windows)
            {
                if (win is MyWindow theWin && theWin.IsWindowFunctional())
                {
                    theWin.TryClose();
                }
            }
        }
    }
}
}
