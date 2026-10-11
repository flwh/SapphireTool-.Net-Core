using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using Microsoft.Win32;
using OSVersionExtension;
using SapphireTool.Classes;

namespace SapphireTool.Pages;

public partial class UpdatesPage : Page, IComponentConnector
{
    private CancellationTokenSource _cts;
    private readonly HttpClient _httpClient = new();
    private readonly Stopwatch _stopwatch = new();
    private readonly string _msuPackage = Path.Combine(Utils.UpdateFolder, "Update.msu");
    private bool _updateCancelled;
    private int _dotCount;
    private string _status = string.Empty;
    private readonly DispatcherTimer _timer;
    private readonly string currentBuild = OSVersion.GetOSVersion().Version.Build.ToString();

    public UpdatesPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
        _timer.Tick += (_, _) => {
            txtBlockDownload.Text = _status + new string('.', _dotCount);
            _dotCount = (_dotCount + 1) % 5;
        };
        string windowsName = App.Winver == "23H2" ? "Windows 10" : "Windows 11";
        txtBlockCurrentWinver.Text = $"OS: {windowsName} [{GetRegistryValue("Edition", "DEFAULT")}] Build {Utils.GetOSVersion()}";
        if (GetRegistryValue("Edition") == "ULTRALITE")
        {
            txtBlockUltralite.Visibility = Visibility.Visible;
            containerGrid.Visibility = Visibility.Collapsed;
            Utils.ShowDialog("抱歉", "更新功能在 UltraLite 版上不可用！");
            btnCheckForUpdates.IsEnabled = false;
        }
    }

    private string GetRegistryValue(string valueName, string defaultValue = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SapphireTool");
            return key?.GetValue(valueName)?.ToString() ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private bool CheckServicesConfiguration()
    {
        if (GetRegistryValue("Services") == "Default")
            return true;

        Utils.ShowDialog("需要更改配置", "当前不是 Default 服务配置。请先切换到 Default 再更新。");
        return false;
    }

    private async void btnCheckForUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdates();

    private async Task CheckForUpdates()
    {
        if (!CheckServicesConfiguration()) return;

        try
        {
            string[] availableVersions = (await Utils.GetLatestVersion()).Trim().Split(',');
            string latestVersionForBuild = availableVersions.FirstOrDefault(v => v.Contains(currentBuild));
            if (latestVersionForBuild == null)
            {
                await Utils.ShowDialog("已是最新版本", "当前 Windows 内部版本没有可用更新。");
                txtBlockLatestWinver.Text = $"你的内部版本 ({currentBuild}) 不受支持。";
                return;
            }

            string currentVersion = Utils.GetOSVersion();
            if (Utils.IsNewVersionAvailable(currentVersion, latestVersionForBuild))
            {
                await Utils.ShowDialog("有可用更新！", $"新版本 ({latestVersionForBuild}) 已发布！");
                txtBlockLatestWinver.Text = $"最新版本：{latestVersionForBuild}";

                if (GetRegistryValue("Edition") == "ULTRALITE")
                {
                    await Utils.ShowDialog("有可用更新", "很遗憾，你使用的是 UltraLite 版，无法更新。请等待新版本发布，或改用 Superlite。");
                    return;
                }
                btnCheckForUpdates.Visibility = Visibility.Collapsed;
                btnDownloadUpdates.Visibility = Visibility.Visible;
                containerGrid.Visibility = Visibility.Visible;
            }
            else
            {
                await Utils.ShowDialog("无可用更新！", "你正在使用最新版本。");
                txtBlockLatestWinver.Text = "已是最新版本！";
            }
        }
        catch (Exception ex)
        {
            await Utils.ShowDialog("错误", $"检查更新时出错：{ex.Message}");
        }
    }
    
    private async void btnDownloadUpdates_Click(object sender, RoutedEventArgs e)
    {
        _updateCancelled = false;
        _cts = new CancellationTokenSource();
        Directory.CreateDirectory(Utils.UpdateFolder);
        SetUIState(isDownloading: true);
        _stopwatch.Start();
        try
        {
            string url = currentBuild.Contains("22631")
                ? "https://hickos.hickdick.workers.dev/0:/Update11.msu"
                : "https://hickos.hickdick.workers.dev/0:/25H2Update.msu";

            UpdateStatus("正在下载更新");
            await DownloadFileAsync(new Uri(url), _msuPackage, _cts.Token);

            if (!_updateCancelled)
            {
                UpdateStatus("正在安装更新");
                progBarDownload.IsIndeterminate = true;
                txtBlockSpeed.Visibility = Visibility.Collapsed;
                btnCancel.IsEnabled = false;

                await InstallUpdates(_msuPackage);
                await DownloadTweakUpdates();
            }
        }
        catch (OperationCanceledException)
        {
            await Utils.ShowDialog("下载已取消", "下载已被取消！");
            ResetUpdatePage();
        }
        catch (Exception)
        {
            // idk why it's throwing an exception here when the download is fine but wtv if it works it works
        }
        _stopwatch.Reset();
    }

    private async Task DownloadFileAsync(Uri uri, string destinationFilePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        byte[] buffer = new byte[8192];
        long totalBytesRead = 0;
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalBytesRead += bytesRead;

            if (totalBytes > 0)
            {
                int progressPercentage = (int)((double)totalBytesRead / totalBytes * 100);
                txtBlockSpeed.Visibility = Visibility.Visible;
                txtBlockSpeed.Text = $"下载速度：{totalBytesRead / 1024 / _stopwatch.Elapsed.TotalSeconds:0.00} kb/s";
                progBarDownload.Value = progressPercentage;
            }
        }
    }

    private async Task InstallUpdates(string msuPath)
    {
        var psiInstall = currentBuild.Contains("26200")
            ? new ProcessStartInfo("dism.exe", $"/Online /Add-Package /PackagePath:{msuPath} /norestart") { UseShellExecute = false, CreateNoWindow = true }
            : new ProcessStartInfo("wusa.exe", $"{msuPath} /quiet /norestart") { UseShellExecute = false, CreateNoWindow = true };
        using var process = new Process { StartInfo = psiInstall };
        process.Start();
        await process.WaitForExitAsync();
        if (tsRemoveDefender.IsChecked == true) await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/BlockDefender.bat", $"{Utils.UpdateFolder}\\BlockDefender.bat"); await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/DisableDefender.ps1", $"{Utils.UpdateFolder}\\DisableDefender.ps1"); await Utils.DownloadFile("https://github.com/HickerDicker/Version/raw/refs/heads/main/NoDefender.cab", "C:\\Windows\\NoDefender.cab"); await ApplyTweak("BlockDefender.bat", Utils.TweakDisableDefender); await Utils.RunCommandAsync("powershell", $"{Utils.UpdateFolder}\\DisableDefender.ps1", true);
        if (tsRemoveEdge.IsChecked == true) await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/BlockEdge.bat", $"{Utils.UpdateFolder}\\BlockEdge.bat"); await ApplyTweak("BlockEdge.bat");
        if (tsWinSxSCleanup.IsChecked == true) await SetupUpdateCleanup();
        progBarDownload.Value = 0;
        txtBlockSpeed.Text = string.Empty;
        btnCheckForUpdates.Visibility = Visibility.Collapsed;
        txtBlockDownload.Visibility = Visibility.Visible;
        UpdateStatus("更新安装完成，正在下载更新后的优化项！");
    }

    private async Task DownloadTweakUpdates()
    {
        UpdateStatus("正在下载新的优化项");
        string url = currentBuild.Contains("22631")
    ? "https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/23H2.bat"
    : "https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/25H2.bat";
        await Utils.DownloadFile(url, $"{Utils.StartupFolder}\\Script.bat");
        await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/HKCU.reg", $"{Utils.UpdateFolder}\\HKCU.reg");
        await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/HKCR.reg", $"{Utils.UpdateFolder}\\HKCR.reg");
        await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/HKLM.reg", $"{Utils.UpdateFolder}\\HKLM.reg");
        await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/run%20regs.bat", $"{Utils.StartupFolder}\\reg.bat");
        Thread.Sleep(1000);
        SetUIState(isDownloading: false);
        UpdateStatus("更新完成，重启后应用优化");
        await Utils.ShowDialog("更新完成", "请等待批处理文件执行完毕后重启电脑，重启后优化项即可生效");
    }
    private async Task ApplyTweak(string scriptName, Action additionalAction = null)
    {
        string scriptPath = Path.Combine(Utils.UpdateFolder, scriptName);
        await Utils.RunCommandAsync("C:\\PostInstall\\Tweaks\\Nsudo.exe", $"-U:T -P:E {scriptPath}", isElevated: true);
        additionalAction?.Invoke();
    }

    private async Task SetupUpdateCleanup()
    {
        await Utils.DownloadFile("https://raw.githubusercontent.com/HickerDicker/Version/refs/heads/main/UpdateCleanup.bat", $"{Utils.StartupFolder}\\UpdateCleanup.bat");
    }

    private void UpdateStatus(string message)
    {
        _status = message;
        txtBlockDownload.Text = _status;
    }

    private void SetUIState(bool isDownloading)
    {
        txtBlockDownload.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
        progBarDownload.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
        btnDownloadUpdates.Visibility = isDownloading ? Visibility.Collapsed : Visibility.Visible;
        btnCancel.Visibility = isDownloading ? Visibility.Visible : Visibility.Collapsed;
        if (isDownloading)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
            _dotCount = 0;
            _status = string.Empty;
            txtBlockDownload.Text = _status;
            txtBlockSpeed.Text = "";
            progBarDownload.Value = 0;
            txtBlockSpeed.Visibility = Visibility.Collapsed;
            progBarDownload.IsIndeterminate = false;
        }
    }
    private void ResetUpdatePage() => SetUIState(isDownloading: false);
    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        ResetUpdatePage();
        _cts?.Cancel();
        _updateCancelled = true;
    }
}