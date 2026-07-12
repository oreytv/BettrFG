using System;
using System.Collections.Generic;
using BetterFG.Services;

namespace BetterFG.Features
{
    public class featuresetting
    {
        public string id;
        public string label;
        public bool defaultOn;
    }

    // a multi-option setting that renders as a dropdown in the features tab (instead of an ON/OFF
    // toggle). optionIds are what get stored; optionLabels are what the dropdown shows. keep the two
    // lists the same length and order. defaultId is the option selected when nothing's saved yet.
    public class featurechoice
    {
        public string id;
        public string label;
        public List<string> optionIds;
        public List<string> optionLabels;
        public string defaultId;
        // optional explanation shown as a tooltip on hover over the label. when non-empty the label
        // also gets a faint hover background so the user knows it's hoverable.
        public string hint;
    }

    public class bfgfeature
    {
        public readonly string id;
        public readonly string title;
        public readonly string note;
        public readonly bool defaultOn;
        public readonly List<featuresetting> settings;
        public readonly List<featurechoice> choices;
        readonly Action _onOpen;
        readonly Action _onClosed;
        readonly Action<string, bool> _onSettingChanged;
        readonly Action<string, string> _onChoiceChanged;

        public bfgfeature(string id, string title, bool defaultOn = true, List<featuresetting> settings = null, string note = "", Action onOpen = null, Action onClosed = null, Action<string, bool> onSettingChanged = null, List<featurechoice> choices = null, Action<string, string> onChoiceChanged = null)
        {
            this.id = id;
            this.title = title;
            this.defaultOn = defaultOn;
            this.settings = settings;
            this.choices = choices;
            this.note = note;
            _onOpen = onOpen;
            _onClosed = onClosed;
            _onSettingChanged = onSettingChanged;
            _onChoiceChanged = onChoiceChanged;
        }

        string key => "feature." + id;

        public bool enabled => SettingsService.Get(key, defaultOn ? "true" : "false") == "true";

        public void SetEnabled(bool on)
        {
            if (enabled == on) return;
            SettingsService.Set(key, on ? "true" : "false");
            if (on) OnOpen();
            else OnClosed();
        }

        public bool Get(string settingId)
        {
            var s = Find(settingId);
            bool def = s == null || s.defaultOn;
            return enabled && SettingsService.Get(key + "." + settingId, def ? "true" : "false") == "true";
        }

        // raw stored value of a setting, ignoring whether the parent feature is enabled. used by the
        // UI so a sub-toggle still reflects its saved state (and updates on click) when the feature
        // itself is off.
        public bool GetRaw(string settingId)
        {
            var s = Find(settingId);
            bool def = s == null || s.defaultOn;
            return SettingsService.Get(key + "." + settingId, def ? "true" : "false") == "true";
        }

        public void Set(string settingId, bool on)
        {
            var s = Find(settingId);
            if (s == null) return;
            SettingsService.Set(key + "." + settingId, on ? "true" : "false");
            OnSettingChanged(settingId, on);
        }

        featuresetting Find(string settingId)
        {
            var list = settings;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == settingId) return list[i];
            return null;
        }

        // current option id of a choice (the stored value, or its default). unlike toggles this
        // doesn't gate on the feature being enabled — the saved pick is always the truth.
        public string GetChoice(string choiceId)
        {
            var c = FindChoice(choiceId);
            string def = c?.defaultId ?? "";
            return SettingsService.Get(key + "." + choiceId, def);
        }

        public void SetChoice(string choiceId, string optionId)
        {
            var c = FindChoice(choiceId);
            if (c == null) return;
            SettingsService.Set(key + "." + choiceId, optionId);
            OnChoiceChanged(choiceId, optionId);
        }

        featurechoice FindChoice(string choiceId)
        {
            var list = choices;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].id == choiceId) return list[i];
            return null;
        }

        public virtual void OnOpen() { _onOpen?.Invoke(); }
        public virtual void OnClosed() { _onClosed?.Invoke(); }
        public virtual void OnSettingChanged(string settingId, bool value) { _onSettingChanged?.Invoke(settingId, value); }
        public virtual void OnChoiceChanged(string choiceId, string optionId) { _onChoiceChanged?.Invoke(choiceId, optionId); }
    }
}
