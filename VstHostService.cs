using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace SonarGG;

public class VstPluginItem
{
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string Category { get; set; } = "Effet";
    public string Format { get; set; } = "VST3";
}

public class VstHostService
{
    public event Action? VstRackChanged;

    public List<VstPluginItem> ActivePlugins { get; } = new();

    public static readonly string[] StandardVstPaths = {
        @"C:\Program Files\Common Files\VST3",
        @"C:\Program Files\VSTPlugins",
        @"C:\Program Files\Steinberg\VSTPlugins",
        @"C:\Program Files (x86)\Common Files\VST3",
        @"C:\Program Files (x86)\VSTPlugins"
    };

    public void Initialize()
    {
        LoadRack();
    }

    public void AddPlugin(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;

        string name = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        string format = ext == ".vst3" ? "VST3" : "VST2";

        // Avoid duplicate by path
        if (ActivePlugins.Exists(p => p.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            return;

        ActivePlugins.Add(new VstPluginItem
        {
            Name = name,
            FilePath = filePath,
            IsEnabled = true,
            Category = "DSP / Voix",
            Format = format
        });

        SaveRack();
        VstRackChanged?.Invoke();
    }

    public void RemovePlugin(int index)
    {
        if (index >= 0 && index < ActivePlugins.Count)
        {
            ActivePlugins.RemoveAt(index);
            SaveRack();
            VstRackChanged?.Invoke();
        }
    }

    public void TogglePlugin(int index, bool enabled)
    {
        if (index >= 0 && index < ActivePlugins.Count)
        {
            ActivePlugins[index].IsEnabled = enabled;
            SaveRack();
            VstRackChanged?.Invoke();
        }
    }

    public void MovePlugin(int index, int direction)
    {
        int target = index + direction;
        if (index >= 0 && index < ActivePlugins.Count && target >= 0 && target < ActivePlugins.Count)
        {
            var item = ActivePlugins[index];
            ActivePlugins.RemoveAt(index);
            ActivePlugins.Insert(target, item);
            SaveRack();
            VstRackChanged?.Invoke();
        }
    }

    public List<string> ScanInstalledPlugins()
    {
        var found = new List<string>();
        foreach (var dir in StandardVstPaths)
        {
            if (!Directory.Exists(dir)) continue;

            try
            {
                var files = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".vst3" || ext == ".dll")
                    {
                        found.Add(f);
                    }
                }
            }
            catch { }
        }
        return found;
    }

    public void SaveRack()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\SonarGG");
            string json = JsonSerializer.Serialize(ActivePlugins);
            key?.SetValue("VstPluginsRack", json);
        }
        catch { }
    }

    public void LoadRack()
    {
        ActivePlugins.Clear();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SonarGG", false);
            if (key?.GetValue("VstPluginsRack") is string json && !string.IsNullOrEmpty(json))
            {
                var items = JsonSerializer.Deserialize<List<VstPluginItem>>(json);
                if (items != null)
                {
                    ActivePlugins.AddRange(items);
                }
            }
        }
        catch { }
    }
}
