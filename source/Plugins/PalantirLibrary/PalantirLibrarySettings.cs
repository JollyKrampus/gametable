using Playnite.SDK;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PalantirLibrary
{
    /// <summary>The one setting: where the house is. The tailnet is the authentication.</summary>
    public class PalantirLibrarySettings : ObservableObject
    {
        private string houseUrl = string.Empty;

        /// <summary>The house's address, the same one the desktop's remote.json carries.</summary>
        public string HouseUrl { get => houseUrl; set => SetValue(ref houseUrl, value); }
    }

    public class PalantirLibrarySettingsViewModel : ObservableObject, ISettings
    {
        private readonly PalantirLibrary plugin;
        private PalantirLibrarySettings editing;
        private PalantirLibrarySettings settings;

        public PalantirLibrarySettings Settings { get => settings; set => SetValue(ref settings, value); }

        public PalantirLibrarySettingsViewModel(PalantirLibrary plugin)
        {
            this.plugin = plugin;
            Settings = plugin.LoadPluginSettings<PalantirLibrarySettings>() ?? new PalantirLibrarySettings();
        }

        public void BeginEdit()
        {
            editing = new PalantirLibrarySettings { HouseUrl = Settings.HouseUrl };
        }

        public void CancelEdit()
        {
            Settings = editing;
        }

        public void EndEdit()
        {
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            if (string.IsNullOrWhiteSpace(Settings.HouseUrl))
            {
                return true;
            }

            try
            {
                Api.PalantirClient.ParseHouseUrl(Settings.HouseUrl);
            }
            catch (System.ArgumentException e)
            {
                errors.Add(e.Message);
            }

            return errors.Count == 0;
        }
    }

    /// <summary>The settings page, built in code: one box and the sentence that explains it.</summary>
    public class PalantirLibrarySettingsView : UserControl
    {
        public PalantirLibrarySettingsView()
        {
            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = "House address",
                Margin = new Thickness(0, 0, 0, 5),
            });

            var box = new TextBox { MinWidth = 400, HorizontalAlignment = HorizontalAlignment.Left };
            box.SetBinding(TextBox.TextProperty, new Binding("Settings.HouseUrl")
            {
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
            panel.Children.Add(box);

            panel.Children.Add(new TextBlock
            {
                Text = "The address of the house on your tailnet, for example https://hoo-ville.<tailnet>.ts.net. "
                     + "Your Palantír play queue is imported from it, and status, score, tags, notes and the "
                     + "time you play are written back to it. Nothing is ever deleted there.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 600,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0),
            });

            Content = panel;
        }
    }
}
