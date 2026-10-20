using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace SonarGG;

public class UpdateInfo
{
    public Version Version { get; set; } = new(1, 0, 0);
    public string TagName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Changelog { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string HtmlUrl { get; set; } = "";
    public bool HasAsset => !string.IsNullOrEmpty(DownloadUrl);
}

/// <summary>
/// Service de mise à jour automatique basé sur les GitHub Releases.
/// Interroge l'API GitHub, télécharge les nouveaux binaires et remplace l'exécutable.
/// </summary>
public static class UpdateService
{
    public static readonly Version CurrentVersion = new(1, 0, 0);
    public static readonly string RepoOwner = "CHATDOO";
    public static readonly string RepoName = "SonarGG";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    static UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SonarGG-Updater", "1.0"));
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3+json"));
    }

    /// <summary>
    /// Vérifie si une version plus récente est disponible sur GitHub Releases.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            string url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tagName = root.TryGetProperty("tag_name", out var tProp) ? tProp.GetString() ?? "" : "";
            string title = root.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            string body = root.TryGetProperty("body", out var bProp) ? bProp.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var hProp) ? hProp.GetString() ?? "" : "";

            string versionStr = tagName.TrimStart('v', 'V').Trim();
            if (!Version.TryParse(versionStr, out var remoteVersion))
            {
                // Si le tag est sous forme 1.0 (sans build), normaliser
                if (Version.TryParse(versionStr + ".0", out var normVersion))
                {
                    remoteVersion = normVersion;
                }
                else
                {
                    return null;
                }
            }

            string exeUrl = "";
            string zipUrl = "";
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    string aName = asset.TryGetProperty("name", out var aNameProp) ? aNameProp.GetString() ?? "" : "";
                    if (asset.TryGetProperty("browser_download_url", out var dlProp))
                    {
                        string dl = dlProp.GetString() ?? "";
                        if (aName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            exeUrl = dl;
                            break;
                        }
                        else if (aName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(zipUrl))
                        {
                            zipUrl = dl;
                        }
                    }
                }
            }

            string downloadUrl = !string.IsNullOrEmpty(exeUrl) ? exeUrl : zipUrl;

            if (remoteVersion > CurrentVersion)
            {
                return new UpdateInfo
                {
                    Version = remoteVersion,
                    TagName = tagName,
                    Title = string.IsNullOrWhiteSpace(title) ? tagName : title,
                    Changelog = body,
                    DownloadUrl = downloadUrl,
                    HtmlUrl = htmlUrl
                };
            }
        }
        catch (Exception ex)
        {
            App.Log("CheckForUpdates error: " + ex.Message);
        }

        return null;
    }

    /// <summary>
    /// Télécharge la mise à jour et applique le remplacement automatique.
    /// Supporte à la fois les exécutables directs (.exe) et les archives (.zip).
    /// </summary>
    public static async Task<bool> DownloadAndApplyUpdateAsync(string downloadUrl, IProgress<int>? progress = null)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "SonarGG_Update");
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
            Directory.CreateDirectory(tempDir);

            bool isZip = downloadUrl.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            string downloadedFile = Path.Combine(tempDir, isZip ? "update.zip" : "SonarGG_new.exe");

            using (var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fs = new FileStream(downloadedFile, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, read);
                    readTotal += read;
                    if (totalBytes > 0 && progress != null)
                    {
                        int pct = (int)((readTotal * 100) / totalBytes);
                        progress.Report(pct);
                    }
                }
            }

            string finalExePath = downloadedFile;

            // Si c'est un zip, extraire l'archive et localiser SonarGG.exe
            if (isZip)
            {
                string extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(downloadedFile, extractDir);

                string[] foundExes = Directory.GetFiles(extractDir, "SonarGG.exe", SearchOption.AllDirectories);
                if (foundExes.Length > 0)
                {
                    finalExePath = foundExes[0];
                }
                else
                {
                    // Fallback: chercher n'importe quel .exe
                    string[] anyExes = Directory.GetFiles(extractDir, "*.exe", SearchOption.AllDirectories);
                    if (anyExes.Length > 0)
                        finalExePath = anyExes[0];
                    else
                        return false;
                }
            }

            string currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe) || !File.Exists(currentExe))
            {
                return false;
            }

            // Script de remplacement autonome
            string batchScript = Path.Combine(tempDir, "apply_update.cmd");
            int pid = Environment.ProcessId;

            string scriptContent = $@"@echo off
timeout /t 1 /nobreak >nul
taskkill /f /pid {pid} >nul 2>&1
timeout /t 1 /nobreak >nul
copy /y ""{finalExePath}"" ""{currentExe}"" >nul
start """" ""{currentExe}""
del /f /q ""{finalExePath}"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
";
            await File.WriteAllTextAsync(batchScript, scriptContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchScript}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);

            Environment.Exit(0);
            return true;
        }
        catch (Exception ex)
        {
            App.Log("DownloadAndApplyUpdate Error: " + ex.Message);
            return false;
        }
    }
}
