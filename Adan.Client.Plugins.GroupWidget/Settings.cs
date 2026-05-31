namespace Adan.Client.Plugins.GroupWidget.Properties {
    
    using System.Collections.Generic;
    using System.Linq;
    
    // Этот класс позволяет обрабатывать определенные события в классе параметров:
    //  Событие SettingChanging возникает перед изменением значения параметра.
    //  Событие PropertyChanged возникает после изменения значения параметра.
    //  Событие SettingsLoaded возникает после загрузки значений параметров.
    //  Событие SettingsSaving возникает перед сохранением значений параметров.
    internal sealed partial class Settings {
        
        public Settings() {
            // // Для добавления обработчиков событий для сохранения и изменения параметров раскомментируйте приведенные ниже строки:
            //
            // this.SettingChanging += this.SettingChangingEventHandler;
            //
            // this.SettingsSaving += this.SettingsSavingEventHandler;
            //
        }

        internal void EnsureAllKnownAffectsSelected() {
            bool groupWidgetChanged;
            var groupWidgetAffects = NormalizeAffects(GroupWidgetAffects, out groupWidgetChanged);

            bool monsterChanged;
            var monsterAffects = NormalizeAffects(MonsterAffects, out monsterChanged);

            if (!groupWidgetChanged && !monsterChanged) {
                return;
            }

            GroupWidgetAffects = groupWidgetAffects;
            MonsterAffects = monsterAffects;
            Save();
        }

        private static string[] NormalizeAffects(string[] affects, out bool changed) {
            var allAffectNames = Constants.AllAffects.Select(affect => affect.Name).ToList();
            var knownAffectNames = new HashSet<string>(allAffectNames);
            var selectedAffectNames = new HashSet<string>();
            var normalizedAffects = new List<string>();

            if (affects != null) {
                foreach (var affect in affects) {
                    if (knownAffectNames.Contains(affect) && selectedAffectNames.Add(affect)) {
                        normalizedAffects.Add(affect);
                    }
                }
            }

            foreach (var affect in allAffectNames) {
                if (selectedAffectNames.Add(affect)) {
                    normalizedAffects.Add(affect);
                }
            }

            changed = affects == null || !affects.SequenceEqual(normalizedAffects);
            return normalizedAffects.ToArray();
        }
        
        private void SettingChangingEventHandler(object sender, System.Configuration.SettingChangingEventArgs e) {
            // Добавьте здесь код для обработки события SettingChangingEvent.
        }
        
        private void SettingsSavingEventHandler(object sender, System.ComponentModel.CancelEventArgs e) {
            // Добавьте здесь код для обработки события SettingsSaving.
        }
    }
}
