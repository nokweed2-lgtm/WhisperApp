using System;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WhisperWin.Core;

namespace WhisperWin.UI
{
    public partial class SettingsWindow : Window
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunKeyValueName = "WhisperWin";

        private readonly ConfigStore _configStore;
        private readonly CredentialStore _credentialStore;
        private readonly HttpClient _httpClient;
        private readonly string? _dictionaryPath;

        public SettingsWindow(ConfigStore configStore, CredentialStore credentialStore, HttpClient httpClient, string? dictionaryPath)
        {
            InitializeComponent();
            _configStore = configStore;
            _credentialStore = credentialStore;
            _httpClient = httpClient;
            _dictionaryPath = dictionaryPath;

            LoadCurrentSettings();
            LoadDictionary();
        }

        private void LoadCurrentSettings()
        {
            var config = _configStore.Load();
            UseCorrectionCheck.IsChecked = config.UseCorrection;
            LaunchAtLoginCheck.IsChecked = config.LaunchAtLogin;

            var existingKey = _credentialStore.ReadApiKey();
            if (!string.IsNullOrEmpty(existingKey))
            {
                ApiKeyBox.Password = existingKey;
            }
        }

        private async void TestKeyButton_Click(object sender, RoutedEventArgs e)
        {
            var key = ApiKeyBox.Password;
            if (string.IsNullOrWhiteSpace(key))
            {
                TestKeyResult.Text = "Enter an API key first.";
                return;
            }

            TestKeyButton.IsEnabled = false;
            TestKeyResult.Text = "Testing...";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.groq.com/openai/v1/models");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);
                using var response = await _httpClient.SendAsync(request);
                TestKeyResult.Text = response.IsSuccessStatusCode
                    ? "Key is valid."
                    : $"Key test failed ({(int)response.StatusCode}).";
            }
            catch (HttpRequestException ex)
            {
                TestKeyResult.Text = $"Network error: {ex.Message}";
            }
            finally
            {
                TestKeyButton.IsEnabled = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var config = _configStore.Load();
            config.UseCorrection = UseCorrectionCheck.IsChecked ?? true;
            config.LaunchAtLogin = LaunchAtLoginCheck.IsChecked ?? false;
            _configStore.Save(config);

            if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            {
                _credentialStore.SaveApiKey(ApiKeyBox.Password);
            }

            ApplyLaunchAtLogin(config.LaunchAtLogin);

            DialogResult = true;
            Close();
        }

        private static void ApplyLaunchAtLogin(bool enabled)
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                return;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(RunKeyValueName, $"\"{exePath}\"");
                }
            }
            else
            {
                if (key.GetValue(RunKeyValueName) != null)
                {
                    key.DeleteValue(RunKeyValueName, throwOnMissingValue: false);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Dictionary editor ──
        // Unlike the settings above, dictionary edits write to shared/dictionary.json immediately
        // (via DictionaryStore) rather than waiting for the Save button — same behavior as the Mac
        // app's Settings UI, since the file is also read live by the correction pipeline.

        private void LoadDictionary()
        {
            if (_dictionaryPath == null)
            {
                DictionaryPanel.IsEnabled = false;
                PairsPanel.IsEnabled = false;
                AddPairButton.IsEnabled = false;
                DictionaryUnavailableText.Visibility = Visibility.Visible;
                return;
            }

            try
            {
                RefreshEntriesList();
                RefreshPairsList();
            }
            catch (Exception)
            {
                // File could be locked/deleted between resolving the path and reading it here —
                // don't let a Settings window open crash the app over a dictionary hiccup.
                DictionaryPanel.IsEnabled = false;
                PairsPanel.IsEnabled = false;
                AddPairButton.IsEnabled = false;
                DictionaryUnavailableText.Visibility = Visibility.Visible;
            }
        }

        private void RefreshEntriesList()
        {
            var file = DictionaryStore.Load(_dictionaryPath!);
            EntriesList.Items.Clear();
            foreach (var entry in file.Entries)
            {
                EntriesList.Items.Add(BuildRow(entry, () => RemoveEntry(entry)));
            }
        }

        private void RefreshPairsList()
        {
            var file = DictionaryStore.Load(_dictionaryPath!);
            PairsList.Items.Clear();
            foreach (var pair in file.Pairs)
            {
                PairsList.Items.Add(BuildRow($"{pair.ToReplace}  →  {pair.ReplaceWith}", () => RemovePair(pair)));
            }
        }

        /// <summary>Builds a "text ... [x]" row used for both the entries list and the pairs list.</summary>
        private static UIElement BuildRow(string text, Action onRemove)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };

            var removeButton = new Button
            {
                Content = "x",
                Width = 22,
                Height = 22,
                Padding = new Thickness(0),
            };
            removeButton.Click += (s, e) => onRemove();
            DockPanel.SetDock(removeButton, Dock.Right);

            var label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                TextWrapping = TextWrapping.Wrap,
            };

            row.Children.Add(removeButton);
            row.Children.Add(label);
            return row;
        }

        private void NewEntryBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddEntry();
            }
        }

        private void AddEntryButton_Click(object sender, RoutedEventArgs e) => AddEntry();

        private void AddEntry()
        {
            var text = NewEntryBox.Text.Trim();
            if (string.IsNullOrEmpty(text) || _dictionaryPath == null)
            {
                return;
            }

            var file = DictionaryStore.Load(_dictionaryPath);
            if (file.Entries.Contains(text))
            {
                NewEntryBox.Clear();
                return;
            }

            file.Entries.Add(text);
            DictionaryStore.SaveEntries(_dictionaryPath, file.Entries);
            NewEntryBox.Clear();
            RefreshEntriesList();
        }

        private void RemoveEntry(string entry)
        {
            if (_dictionaryPath == null)
            {
                return;
            }

            var file = DictionaryStore.Load(_dictionaryPath);
            file.Entries.Remove(entry);
            DictionaryStore.SaveEntries(_dictionaryPath, file.Entries);
            RefreshEntriesList();
        }

        private void NewPairRightBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                AddPair();
            }
        }

        private void AddPairButton_Click(object sender, RoutedEventArgs e) => AddPair();

        private void AddPair()
        {
            var wrong = NewPairWrongBox.Text.Trim();
            var right = NewPairRightBox.Text.Trim();
            if (string.IsNullOrEmpty(wrong) || string.IsNullOrEmpty(right) || _dictionaryPath == null)
            {
                return;
            }

            var file = DictionaryStore.Load(_dictionaryPath);
            var isDuplicate = file.Pairs.Any(p => p.ToReplace == wrong && p.ReplaceWith == right);
            if (isDuplicate)
            {
                NewPairWrongBox.Clear();
                NewPairRightBox.Clear();
                return;
            }

            file.Pairs.Add(new DictionaryPair { ToReplace = wrong, ReplaceWith = right });
            DictionaryStore.SavePairs(_dictionaryPath, file.Pairs);
            NewPairWrongBox.Clear();
            NewPairRightBox.Clear();
            RefreshPairsList();
        }

        private void RemovePair(DictionaryPair pair)
        {
            if (_dictionaryPath == null)
            {
                return;
            }

            var file = DictionaryStore.Load(_dictionaryPath);
            var match = file.Pairs.FirstOrDefault(p => p.ToReplace == pair.ToReplace && p.ReplaceWith == pair.ReplaceWith);
            if (match != null)
            {
                file.Pairs.Remove(match);
            }
            DictionaryStore.SavePairs(_dictionaryPath, file.Pairs);
            RefreshPairsList();
        }
    }
}
