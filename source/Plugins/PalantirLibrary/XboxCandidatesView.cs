using Playnite.SDK;
using PalantirLibrary.Sync;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace PalantirLibrary
{
    /// <summary>
    /// "Xbox games not on the play list": a row per game and an Add button on each. Built in code, as
    /// the settings page is. Nothing goes onto the list except by a press.
    /// </summary>
    public class XboxCandidatesView : UserControl
    {
        private readonly Func<List<XboxCandidate>> read;
        private readonly Func<XboxCandidate, string> add;
        private readonly TextBlock said = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        private readonly StackPanel rows = new StackPanel();
        private readonly List<Button> buttons = new List<Button>();
        private bool started;

        /// <param name="read">The games to offer, read fresh; throws when the house cannot be read.</param>
        /// <param name="add">Adds one; null once its row is written, or the sentence saying why not.</param>
        public XboxCandidatesView(Func<List<XboxCandidate>> read, Func<XboxCandidate, string> add)
        {
            this.read = read;
            this.add = add;

            var top = new StackPanel();
            top.Children.Add(new TextBlock
            {
                Text = "Xbox games GameTable has that are not on Palantír's play list. Add puts one there as "
                     + "Xbox (played on the console) or Xbox/PC (Game Pass or the Microsoft Store). A game whose "
                     + "row you threw away in Palantír is not offered again, and a game you hide in GameTable "
                     + "leaves this list.",
                TextWrapping = TextWrapping.Wrap,
            });
            top.Children.Add(said);
            DockPanel.SetDock(top, Dock.Top);

            var page = new DockPanel { Margin = new Thickness(20) };
            page.Children.Add(top);
            page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows });
            Content = page;

            Loaded += (_, __) => Start();
        }

        /// <summary>Opens the list over GameTable's own window.</summary>
        public static void Show(IPlayniteAPI api, Func<List<XboxCandidate>> read, Func<XboxCandidate, string> add)
        {
            var window = api.Dialogs.CreateWindow(new WindowCreationOptions { ShowMinimizeButton = false });
            window.Title = "Xbox games not on the play list";
            window.Width = 760;
            window.Height = 560;
            window.Owner = api.Dialogs.GetCurrentAppWindow();
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            window.Content = new XboxCandidatesView(read, add);
            window.ShowDialog();
        }

        private async void Start()
        {
            if (started)
            {
                return;
            }

            started = true;
            said.Text = "Reading Palantír…";
            try
            {
                Fill(await Task.Run(read));
            }
            catch (Exception e)
            {
                said.Text = $"Palantír could not be read: {e.Message}";
            }
        }

        private void Fill(List<XboxCandidate> candidates)
        {
            rows.Children.Clear();
            buttons.Clear();
            if (candidates.Count == 0)
            {
                said.Text = "Every Xbox game GameTable has is on the play list, or was thrown away there.";
                return;
            }

            said.Text = candidates.Count == 1 ? "One game." : $"{candidates.Count} games.";
            foreach (var candidate in candidates)
            {
                rows.Children.Add(Row(candidate));
            }
        }

        private Grid Row(XboxCandidate candidate)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Cell(row, 0, candidate.Title);
            Cell(row, 1, candidate.Platform);
            Cell(row, 2, Played(candidate.PlayedMinutes));

            var button = new Button { Content = "Add", Tag = candidate, MinWidth = 130 };
            button.Click += (_, __) => Add(button);
            Grid.SetColumn(button, 3);
            row.Children.Add(button);
            buttons.Add(button);
            return row;
        }

        private static void Cell(Grid row, int column, string text)
        {
            var cell = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 10, 0),
            };
            Grid.SetColumn(cell, column);
            row.Children.Add(cell);
        }

        private async void Add(Button button)
        {
            var candidate = (XboxCandidate)button.Tag;
            button.IsEnabled = false;
            button.Content = "Adding…";

            string refused;
            try
            {
                refused = await Task.Run(() => add(candidate));
            }
            catch (Exception e)
            {
                refused = e.Message;
            }

            if (refused != null)
            {
                button.Content = "Add";
                button.IsEnabled = true;
                said.Text = refused;
                return;
            }

            // A second Xbox entry under the same title is that row now, and the house would refuse it.
            foreach (var same in buttons.Where(b => GameMapping.SameTitle(((XboxCandidate)b.Tag).Title, candidate.Title)))
            {
                same.IsEnabled = false;
                same.Content = "On the play list";
            }

            said.Text = $"{candidate.Title} is on the play list, as {candidate.Platform}.";
        }

        private static string Played(int? minutes)
        {
            if (minutes == null)
            {
                return string.Empty;
            }

            var hours = minutes.Value / 60;
            return hours == 0 ? $"{minutes.Value} min played" : $"{hours} h {minutes.Value % 60} min played";
        }
    }
}
