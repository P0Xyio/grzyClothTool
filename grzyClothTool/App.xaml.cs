﻿using grzyClothTool.Extensions;
using grzyClothTool.Views;
using Material.Icons;
using System;
using System.IO;
using System.Threading;
using System.Windows;
using grzyClothTool.Properties;
using static grzyClothTool.Controls.CustomMessageBox;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using grzyClothTool.Shared;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Net.Http;

namespace grzyClothTool
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static ISplashScreen splashScreen;
        private ManualResetEvent ResetSplashCreated;
        private Thread SplashThread;
        public static readonly HttpClient httpClient = new();
        private static readonly string pluginsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "grzyClothTool", "plugins");

        [ImportMany]
        public IEnumerable<Lazy<IPlugin, IPluginMetadata>> plugins;

        public static IPatreonPlugin patreonAuthPlugin = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            ResetSplashCreated = new ManualResetEvent(false);

            // Create a new thread for the splash screen to run on
            SplashThread = new Thread(ShowSplash);
            SplashThread.SetApartmentState(ApartmentState.STA);
            SplashThread.Start();

            ResetSplashCreated.WaitOne();
            base.OnStartup(e);

            CheckPluginsVersion().Wait();
            LoadPlugins();
            RunPlugins();

            //get value from settings properties
            bool isDarkTheme = Settings.Default.IsDarkMode;
            ChangeTheme(isDarkTheme);
        }

        public App()
        {
            MaterialIconDataProvider.Instance = new CustomIconProvider(); // use custom icons
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
            httpClient.DefaultRequestHeaders.Add("X-GrzyClothTool", "true");
        }

        private void LoadPlugins()
        {
            var catalog = new DirectoryCatalog(pluginsPath);
            var container = new CompositionContainer(catalog);

            container.ComposeParts(this);

            foreach (var plugin in plugins)
            {
                if (plugin.Metadata.Name == "PatreonAuth")
                {
                    patreonAuthPlugin = (IPatreonPlugin)plugin.Value;
                }
            }
        }

        private async Task CheckPluginsVersion()
        {
            Directory.CreateDirectory(pluginsPath);

            string[] plugins = Directory.GetFiles(pluginsPath, "*.dll");
            if (plugins.Length < 2)
            {
                await DownloadLatestPlugin("1").ConfigureAwait(false);
                await DownloadLatestPlugin("2").ConfigureAwait(false);
                return;
            }

            foreach (var plugin in plugins)
            {
                FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(plugin);
                string version = fileVersionInfo.FileVersion;

                if (!string.IsNullOrEmpty(version))
                {
                    var name = Path.GetFileName(plugin);
                    await DownloadLatestPlugin(name, version).ConfigureAwait(false);
                }
            }
        }

        private async Task DownloadLatestPlugin(string name, string version = null)
        {
            try
            {
                string url = $"https://grzybeek.pl/grzyClothTool/version?filename={name}";
                if (!string.IsNullOrEmpty(version))
                {
                    url += $"&version={version}";
                }

                var response = await httpClient.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Content.Headers.ContentType.MediaType == "application/octet-stream")
                    {
                        var bytes = await response.Content.ReadAsByteArrayAsync();
                        var filename = response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
                        var savePath = Path.Combine(pluginsPath, filename);
                        await File.WriteAllBytesAsync(savePath, bytes);
                    }
                    else
                    {
                        // The response does not contain a file.
                    }
                }
                else
                {
                    throw new Exception("Failed to download required file. Try again later");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Request error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private static async void RunPlugins()
        {
            if(patreonAuthPlugin != null)
            {
                await patreonAuthPlugin?.Run();
            }
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;

            Show($"An error occurred: {ex.Message}", "Error", CustomMessageBoxButtons.OKOnly);
            File.WriteAllText("error.log", ex.ToString());
            Console.WriteLine("Unhandled exception: " + ex.ToString());
        }

        private void DispatcherUnhandledExceptionHandler(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            Show($"An error occurred: {e.Exception.Message}", "Error", CustomMessageBoxButtons.OKOnly);
            File.WriteAllText("error.log", e.Exception.ToString());
            Console.WriteLine("Dispatcher unhandled exception: " + e.Exception.ToString());

            e.Handled = true;
        }

        private void ShowSplash()
        {
            Views.SplashScreen animatedSplashScreenWindow = new();
            splashScreen = animatedSplashScreenWindow;

            animatedSplashScreenWindow.Show();

            ResetSplashCreated.Set();
            System.Windows.Threading.Dispatcher.Run();
        }

        public static void ChangeTheme(bool isDarkMode)
        {
            Uri uri = isDarkMode ? new Uri("Themes/Dark.xaml", UriKind.Relative) : new Uri("Themes/Light.xaml", UriKind.Relative);
            ResourceDictionary theme = new() { Source = uri };
            ResourceDictionary shared = new() { Source = new Uri("Themes/Shared.xaml", UriKind.Relative) };

            Current.Resources.MergedDictionaries.Clear();
            Current.Resources.MergedDictionaries.Add(theme);
            Current.Resources.MergedDictionaries.Add(shared);
        }

    }
}
