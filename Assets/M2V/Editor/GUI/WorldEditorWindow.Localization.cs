using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using M2V.Editor.Bakery.Meshing;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M2V.Editor.GUI
{
    public partial class WorldEditorWindow
    {
        private void ConfigureLanguageDropdown()
        {
            if (_languageDropdown == null && _languageHost != null)
            {
                _languageDropdown = new DropdownField();
                _languageDropdown.AddToClassList("m2v-topbar-dropdown");
                _languageHost.Add(_languageDropdown);
            }

            if (_languageDropdown == null)
            {
                return;
            }

            _languageDropdown.choices = new List<string> { "English", "日本語" };
            _languageDropdown.value = _state.Language == Language.Japanese ? "日本語" : "English";
            _languageDropdown.label = string.Empty;
            _languageDropdown.RegisterValueChangedCallback(evt =>
            {
                _state.Language = evt.newValue == "日本語" ? Language.Japanese : Language.English;
                ApplyLocalization();
                UpdateValidation();
            });
        }
        private void ApplyLocalization()
        {
            var lang = _state.Language;
            if (_rootElement != null)
            {
                _rootElement.EnableInClassList("lang-ja", lang == Language.Japanese);
            }
            SetLabel(_topbarUser, Localization.Keys.TopbarUser, lang);
            SetLabel(_topbarHelp, Localization.Keys.TopbarHelp, lang);
            SetLabel(_titleLabel, Localization.Keys.Title, lang);
            SetLabel(_subtitleLabel, Localization.Keys.Subtitle, lang);
            SetLabel(_stepWorld, Localization.Keys.TabWorlds, lang);
            SetLabel(_stepRange, Localization.Keys.TabRange, lang);
            SetLabel(_stepGenerate, Localization.Keys.TabGenerate, lang);
            SetLabel(_worldsTitle, Localization.Keys.WorldsTitle, lang);
            SetLabel(_worldTipLabel, Localization.Keys.WorldTip, lang);
            SetLabel(_rangeTitle, Localization.Keys.RangeTitle, lang);
            SetLabel(_rangeHint, Localization.Keys.RangeHint, lang);
            SetLabel(_rangeMinLabel, Localization.Keys.RangeMin, lang);
            SetLabel(_rangeMaxLabel, Localization.Keys.RangeMax, lang);
            SetLabel(_rangeDimensionLabel, Localization.Keys.RangeDimension, lang);
            SetLabel(_dimensionOverworldLabel, Localization.Keys.DimensionOverworld, lang);
            SetLabel(_dimensionNetherLabel, Localization.Keys.DimensionNether, lang);
            SetLabel(_dimensionEndLabel, Localization.Keys.DimensionEnd, lang);
            SetLabel(_generateTitle, Localization.Keys.GenerateTitle, lang);
            SetLabel(_generateHint, Localization.Keys.GenerateHint, lang);
            SetLabel(_playCaption, Localization.Keys.PlayCaption, lang);
            if (_loadingTitle != null)
            {
                _loadingTitle.text = Localization.Get(lang, Localization.Keys.LoadingTitle);
            }
            if (_loadingStatusLabel != null)
            {
                _loadingStatusLabel.text = Localization.Get(lang, Localization.Keys.LoadingPreparing);
            }

            SetButton(_reloadButton, Localization.Keys.Reload, lang);
            SetButton(_customImportButton, Localization.Keys.SelectCustomFolder, lang);
            SetButton(_openButton, Localization.Keys.OpenFolder, lang);
            SetButton(_clearButton, Localization.Keys.Clear, lang);
            SetButton(_meshButton, Localization.Keys.GenerateButton, lang);
            SetButton(_nextWorldButton, Localization.Keys.Next, lang);
            SetButton(_nextRangeButton, Localization.Keys.Next, lang);
            SetButton(_backRangeButton, Localization.Keys.Back, lang);
            SetButton(_backGenerateButton, Localization.Keys.Back, lang);
            UpdateSummary();
        }
        private static void SetLabel(Label label, string key, Language language)
        {
            if (label == null)
            {
                return;
            }

            label.text = Localization.Get(language, key);
        }
        private static void SetButton(Button button, string key, Language language)
        {
            if (button == null)
            {
                return;
            }

            button.text = Localization.Get(language, key);
        }
    }
}
