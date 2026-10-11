using SapphireTool.Classes;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace SapphireTool.Pages
{

    public partial class BrowsersPage
    {
        private readonly Stopwatch sw = new Stopwatch();
        private bool cancelled;
        private string downloadLog = string.Empty;
        private int count;
        private int downloadCount;
        // Sapphire Premium V2 source code right here chat enjoy!
        public ObservableCollection<ApplicationInfo> Applications { get; set; } = new ObservableCollection<ApplicationInfo>();
        private List<ApplicationInfo> AllApplications { get; set; } = new List<ApplicationInfo>();

        private readonly string dl_location = Utils.DownloadsFolder;

        public BrowsersPage()
        {
            InitializeComponent();
            AppList.ItemsSource = Applications;
            SpeedLabel.Visibility = Visibility.Collapsed;
            DownloadedLabel.Visibility = Visibility.Collapsed;
            LoadApplications();
        }

        private async void LoadApplications()
        {
            try
            {
                string jsonUrl = "https://hickos.hickdick.workers.dev/0:/Downloads.json";

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                    string jsonString = await client.GetStringAsync(jsonUrl);

                    var appList = JsonConvert.DeserializeObject<List<ApplicationInfo>>(jsonString);

                    AllApplications = appList.ToList();
                    Applications.Clear();

                    foreach (var app in appList)
                    {
                        Applications.Add(app);
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            sw.Start();
            count = 0;
            downloadCount = 0;
            cancelled = false;
            downloadLog = string.Empty;
            DownloadedLabel.Visibility = Visibility.Visible;
            SpeedLabel.Visibility = Visibility.Visible;

            if (!Directory.Exists(dl_location))
            {
                Directory.CreateDirectory(dl_location);
            }

            DownloadButton.IsEnabled = false;

            foreach (var app in Applications)
            {
                if (app.IsSelected && !string.IsNullOrEmpty(app.Link))
                {
                    string fileExtension = GetFileExtension(app.Link);
                    string filename = app.Title + app.Version.Replace("| Version : ", " ") + fileExtension;

                    count++;

                    if (!cancelled)
                    {
                        try
                        {
                            using (HttpClient client = new HttpClient())
                            {
                                client.Timeout = TimeSpan.FromMinutes(30);
                                var response = await client.GetAsync(app.Link, HttpCompletionOption.ResponseHeadersRead);
                                response.EnsureSuccessStatusCode();

                                string filePath = Path.Combine(dl_location, filename);
                                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                {
                                    await CopyToWithProgressAsync(contentStream, fileStream, response.Content.Headers.ContentLength ?? -1);
                                }

                                downloadLog += $"\u2022 {app.Title}: 下载成功！\n\n";
                                downloadCount++;
                            }
                        }
                        catch (Exception ex)
                        {
                            downloadLog += $"\u2022 {app.Title}：下载失败 - {ex.Message}\n\n";
                        }
                    }
                }
            }

            DownloadButton.IsEnabled = true;
            sw.Stop();
            ResetUI();
            await Utils.ShowDialog("下载完成", "文件已保存在你的下载文件夹中");
        }

        private string GetFileExtension(string link)
        {
            if (link.Contains(".7z") || link.Contains(".bin")) return ".7z";
            if (link.Contains(".msi")) return ".msi";
            if (link.Contains(".zip")) return ".zip";
            if (link.Contains(".img")) return ".img";
            if (link.Contains(".rar")) return ".rar";
            if (link.Contains(".bat")) return ".bat";
            if (link.Contains(".iso")) return ".iso";
            if (link.Contains(".exe")) return ".exe";
            return ".exe";
        }

        private async Task CopyToWithProgressAsync(Stream source, Stream destination, long totalBytes)
        {
            byte[] buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await destination.WriteAsync(buffer, 0, bytesRead);
                totalRead += bytesRead;

                Dispatcher.Invoke(() =>
                {
                    ProgressBar.Value = totalBytes > 0 ? (totalRead * 100.0 / totalBytes) : 0;
                    DownloadedLabel.Text = $"{totalRead / 1024 / 1024} MB / {(totalBytes > 0 ? totalBytes / 1024 / 1024 : "未知")} MB";
                    double speed = (totalRead / 1024d) / sw.Elapsed.TotalSeconds;
                    SpeedLabel.Text = string.Format("{0} kb/s", speed.ToString("0.00"));
                });
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            cancelled = true;
        }

        private void ResetUI()
        {
            ProgressBar.Value = 0;
            DownloadedLabel.Visibility = Visibility.Collapsed;
            SpeedLabel.Visibility = Visibility.Collapsed;
            foreach (var app in Applications)
            {
                app.IsSelected = false;
            }
            downloadLog = string.Empty;
            count = 0;
            downloadCount = 0;
            sw.Reset();
        }

        public class ApplicationInfo
        {
            public bool IsSelected { get; set; }
            public string Title { get; set; }
            public string Version { get; set; }
            public string Type { get; set; }
            public string Size { get; set; }
            public string Link { get; set; }
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            string searchText = SearchBox.Text.ToLower();

            Applications.Clear();

            foreach (var app in AllApplications)
            {
                if (app.Title.ToLower().Contains(searchText))
                {
                    Applications.Add(app);
                }
            }
        }
    }
}