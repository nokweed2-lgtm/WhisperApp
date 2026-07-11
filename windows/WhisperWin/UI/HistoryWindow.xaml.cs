using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WhisperWin.Core;

namespace WhisperWin.UI
{
    public partial class HistoryWindow : Window
    {
        private readonly string _historyPath;
        private List<HistoryEntry> _entries = new();

        public HistoryWindow(string historyPath)
        {
            InitializeComponent();
            _historyPath = historyPath ?? throw new ArgumentNullException(nameof(historyPath));

            LoadEntries();
        }

        private void LoadEntries()
        {
            _entries = HistoryStore.Load(_historyPath);
            RefreshList();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            HistoryStore.Clear(_historyPath);
            _entries = new List<HistoryEntry>();
            RefreshList();
        }

        /// <summary>
        /// Rebuilds the visible list from <see cref="_entries"/> — newest-first, filtered by the
        /// search box (case-insensitive substring), mirroring HistoryView.swift's `shown` property.
        /// </summary>
        private void RefreshList()
        {
            var filter = SearchBox.Text?.Trim() ?? "";
            var shown = _entries.AsEnumerable().Reverse()
                .Where(entry => filter.Length == 0 || entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();

            ClearAllButton.IsEnabled = _entries.Count > 0;

            EntriesList.Items.Clear();
            if (shown.Count == 0)
            {
                EmptyStateText.Text = _entries.Count == 0 ? "No history yet — dictate something!" : "No matches";
                EmptyStateText.Visibility = Visibility.Visible;
                return;
            }

            EmptyStateText.Visibility = Visibility.Collapsed;
            foreach (var entry in shown)
            {
                EntriesList.Items.Add(BuildRow(entry));
            }
        }

        /// <summary>
        /// Builds one history row: date + a "Copy" label up top, the dictated text below. Clicking
        /// anywhere on the row copies the text to the clipboard and flashes the label to "Copied"
        /// for ~1.2s, matching HistoryView.swift's `row(_:)`.
        /// </summary>
        private static UIElement BuildRow(HistoryEntry entry)
        {
            var dateText = new TextBlock
            {
                Text = entry.Date.ToLocalTime().ToString("d MMM · HH:mm"),
                FontSize = 11,
                Foreground = Brushes.Gray,
            };

            var copyLabel = new TextBlock
            {
                Text = "Copy",
                FontSize = 11,
                Foreground = Brushes.Gray,
            };
            DockPanel.SetDock(copyLabel, Dock.Right);

            var header = new DockPanel();
            header.Children.Add(copyLabel);
            header.Children.Add(dateText);

            var bodyText = new TextBlock
            {
                Text = entry.Text,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            };

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(bodyText);

            var row = new Border
            {
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 0, 6),
                Background = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand,
                Child = content,
            };
            row.MouseLeftButtonUp += (s, e) => CopyToClipboard(entry.Text, copyLabel);

            return row;
        }

        /// <summary>
        /// Clipboard.SetText can throw a transient COMException if another process momentarily
        /// holds the clipboard open — retry a couple of times, same pattern as
        /// TextInjector.SetClipboardTextWithRetry.
        /// </summary>
        private static void CopyToClipboard(string text, TextBlock copyLabel)
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Clipboard.SetText(text);
                    break;
                }
                catch (COMException) when (attempt < maxAttempts)
                {
                    System.Threading.Thread.Sleep(30);
                }
                catch (COMException)
                {
                    return; // gave up — leave the label as "Copy" since nothing was copied.
                }
            }

            copyLabel.Text = "Copied";
            copyLabel.Foreground = Brushes.Green;

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
            timer.Tick += (s, e) =>
            {
                copyLabel.Text = "Copy";
                copyLabel.Foreground = Brushes.Gray;
                timer.Stop();
            };
            timer.Start();
        }
    }
}
