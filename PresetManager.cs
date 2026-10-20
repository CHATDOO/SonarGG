using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SonarGG;

public class PresetInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Vad { get; set; } = "";
    public string JsonData { get; set; } = "";

    public override string ToString() => Name;
}

public static class PresetManager
{
    private static readonly string DbPath = @"C:\ProgramData\SteelSeries\GG\apps\sonar\db\database.db";

    public static int VadToInt(string channel) => channel.ToLowerInvariant() switch
    {
        "game" => 1,
        "chatrender" or "chat" => 2,
        "chatcapture" or "mic" => 3,
        "media" => 4,
        "aux" => 5,
        _ => 1
    };

    public static List<PresetInfo> GetPresets(string channel)
    {
        var list = new List<PresetInfo>();
        if (!File.Exists(DbPath)) return list;

        int vadNum = VadToInt(channel);

        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
            connection.Open();

            string query = "SELECT id, name, vad FROM configs WHERE vad = @vad ORDER BY name ASC";
            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@vad", vadNum);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new PresetInfo
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Vad = reader.GetInt32(2).ToString()
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("PresetManager error: " + ex.Message);
        }

        return list;
    }

    public static string GetPresetData(string id)
    {
        if (!File.Exists(DbPath)) return string.Empty;
        try
        {
            using var connection = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly");
            connection.Open();

            string query = "SELECT data FROM configs WHERE id = @id LIMIT 1";
            using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@id", id);

            var res = cmd.ExecuteScalar();
            return res?.ToString() ?? string.Empty;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("GetPresetData error: " + ex.Message);
            return string.Empty;
        }
    }

    public static void ApplyPreset(ApoController apo, string channel, PresetInfo preset)
    {
        string jsonData = preset.JsonData;
        if (string.IsNullOrEmpty(jsonData))
        {
            jsonData = GetPresetData(preset.Id);
            preset.JsonData = jsonData;
        }

        if (string.IsNullOrEmpty(jsonData)) return;

        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            // Parametric EQ
            if (root.TryGetProperty("parametricEQ", out var peq))
            {
                for (int i = 1; i <= 10; i++)
                {
                    if (peq.TryGetProperty($"filter{i}", out var filter))
                    {
                        bool enabled = filter.TryGetProperty("enabled", out var en) && en.GetBoolean();
                        float gain = filter.TryGetProperty("gain", out var g) ? (float)g.GetDouble() : 0.0f;
                        float freq = filter.TryGetProperty("frequency", out var f) ? (float)f.GetDouble() : 1000.0f;
                        apo.SetEqFilter(channel, i, enabled, gain, freq);
                    }
                }
            }

            // Bass Boost
            if (root.TryGetProperty("bassBoostState", out var bb))
            {
                bool enabled = bb.TryGetProperty("enabled", out var en) && en.GetBoolean();
                float val = bb.TryGetProperty("value", out var v) ? (float)v.GetDouble() : 0.0f;
                apo.SetBassBoost(channel, enabled, val);
            }

            // Voice Clarity
            if (root.TryGetProperty("voiceClarityState", out var vc))
            {
                bool enabled = vc.TryGetProperty("enabled", out var en) && en.GetBoolean();
                float val = vc.TryGetProperty("value", out var v) ? (float)v.GetDouble() : 0.0f;
                apo.SetVoiceClarity(channel, enabled, val);
            }

            // Treble Boost
            if (root.TryGetProperty("trebleBoostState", out var tb))
            {
                bool enabled = tb.TryGetProperty("enabled", out var en) && en.GetBoolean();
                float val = tb.TryGetProperty("value", out var v) ? (float)v.GetDouble() : 0.0f;
                apo.SetTrebleBoost(channel, enabled, val);
            }

            // Virtual Surround (Spatial Audio)
            if (root.TryGetProperty("virtualSurroundState", out var vss))
            {
                bool enabled = false;
                if (vss.ValueKind == JsonValueKind.True) enabled = true;
                else if (vss.ValueKind == JsonValueKind.False) enabled = false;
                else if (vss.ValueKind == JsonValueKind.Object && vss.TryGetProperty("enabled", out var en)) enabled = en.GetBoolean();

                float reverb = root.TryGetProperty("reverbGainDB", out var rg) ? (float)rg.GetDouble() : -6.0f;
                apo.SetVirtualSurround(channel, enabled, reverb);
            }

            // ClearCast AI & Impact Noise : Verrouillés à désactivé (Pure DSP / VST - Zéro IA)
            apo.SetClearCastAi(false, 0f);
            apo.SetImpactNoise(false, 0f);

            // Noise Gate
            if (root.TryGetProperty("noiseGateState", out var ng))
            {
                bool enabled = ng.TryGetProperty("enabled", out var en) && en.GetBoolean();
                float val = ng.TryGetProperty("value", out var v) ? (float)v.GetDouble() : -44.0f;
                apo.SetNoiseGate(enabled, val);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("ApplyPreset error: " + ex);
        }
    }
}
