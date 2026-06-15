using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using KrokoshaCasualtiesMP;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KrokoshaRunSettingsCompat;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("KrokoshaCasualtiesMP", BepInDependency.DependencyFlags.HardDependency)]
[BepInProcess("CasualtiesUnknown.exe")]
public sealed class KrokoshaRunSettingsCompatPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "kef.casualtiesunknown.mprunsettingscompat";
    public const string PluginName = "CU MP Run Settings Compat";
    public const string PluginVersion = "0.1.0";

    internal static ConfigEntry<bool> SyncRunSettingsFromMenuControls;
    internal static ConfigEntry<bool> PreserveVanillaRunSettingsAfterMPRules;
    internal static ConfigEntry<string> ServerRunSettingOverrides;
    internal static ManualLogSource Log;

    private Harmony harmony;

    private void Awake()
    {
        Log = Logger;

        SyncRunSettingsFromMenuControls = Config.Bind(
            "Run Settings",
            "SyncRunSettingsFromMenuControls",
            true,
            "When true, the host/server snapshots the visible vanilla RunSettings controls into the runSettings dictionary before MP serializes or generates the world. With normal sliders this is a no-op; with unlocked sliders it captures values beyond vanilla limits.");

        PreserveVanillaRunSettingsAfterMPRules = Config.Bind(
            "Run Settings",
            "PreserveVanillaRunSettingsAfterMPRules",
            true,
            "When true, preserves vanilla RunSettings that Krokosha MP rewrites from MP rules. Currently this restores xpgain after MP applies XPGainMultiplier.");

        ServerRunSettingOverrides = Config.Bind(
            "Run Settings",
            "ServerRunSettingOverrides",
            "",
            "Server-only semicolon/comma/newline-separated vanilla RunSettings overrides, e.g. oreamount=3;basetrapdensity=0;debugworld=true;xpgain=8. These are applied before MP serializes and generates the world.");

        harmony = new Harmony(PluginGuid);
        harmony.PatchAll();
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        harmony?.UnpatchSelf();
    }
}

[HarmonyPatch(typeof(KrokoshaScavMultiplayer), "ApplyGameRules")]
internal static class ApplyGameRulesRunSettingsPatch
{
    private static object preservedXPGain;
    private static bool hadXPGain;

    private static void Prefix()
    {
        RunSettingsAuthority.SnapshotMenuControlsToRunSettings("before-ApplyGameRules");

        preservedXPGain = null;
        hadXPGain = false;

        if (!KrokoshaRunSettingsCompatPlugin.PreserveVanillaRunSettingsAfterMPRules.Value)
        {
            return;
        }

        Dictionary<string, object> settings = RunSettingsAuthority.GetActiveRunSettings();
        if (settings != null && settings.TryGetValue("xpgain", out object value))
        {
            preservedXPGain = value;
            hadXPGain = true;
        }
    }

    private static void Postfix()
    {
        if (KrokoshaRunSettingsCompatPlugin.PreserveVanillaRunSettingsAfterMPRules.Value && hadXPGain)
        {
            RunSettingsAuthority.SetRunSetting("xpgain", preservedXPGain, "preserve-after-ApplyGameRules");
        }

        RunSettingsAuthority.ApplyConfiguredServerOverrides("after-ApplyGameRules");
    }
}

[HarmonyPatch(typeof(ServerMain), "Server_Announce_GAME_START")]
internal static class ServerAnnounceGameStartRunSettingsPatch
{
    private static void Prefix()
    {
        RunSettingsAuthority.SnapshotMenuControlsToRunSettings("before-Server_Announce_GAME_START");
        RunSettingsAuthority.ApplyConfiguredServerOverrides("before-Server_Announce_GAME_START");
    }
}

[HarmonyPatch(typeof(WorldgenPatches), "CompileRunSettings")]
internal static class CompileRunSettingsPatch
{
    private static void Prefix()
    {
        RunSettingsAuthority.SnapshotMenuControlsToRunSettings("before-CompileRunSettings");
        RunSettingsAuthority.ApplyConfiguredServerOverrides("before-CompileRunSettings");
    }
}

[HarmonyPatch(typeof(WorldgenPatches), "Patched_GenerateWorld")]
internal static class PatchedGenerateWorldRunSettingsPatch
{
    private static void Prefix()
    {
        RunSettingsAuthority.SnapshotMenuControlsToRunSettings("before-Patched_GenerateWorld");
        RunSettingsAuthority.ApplyConfiguredServerOverrides("before-Patched_GenerateWorld");
    }
}

internal static class RunSettingsAuthority
{
    private static readonly char[] EntrySeparators = { ';', ',', '\n', '\r' };

    public static void SnapshotMenuControlsToRunSettings(string reason)
    {
        if (!KrokoshaRunSettingsCompatPlugin.SyncRunSettingsFromMenuControls.Value)
        {
            return;
        }

        PreRunScript preRun = PreRunScript.instance;
        if (preRun == null || preRun.runSettings == null || preRun.runSettingObjects == null)
        {
            return;
        }

        foreach (RunSettingDisplay display in preRun.runSettingObjects)
        {
            if (display == null || display.associated == null || display.transform == null || display.transform.childCount < 2)
            {
                continue;
            }

            if (!TryReadDisplayValue(display, out object value))
            {
                continue;
            }

            SetRunSetting(display.associated.name, value, reason);
        }

        preRun.currentPreset = 6;
    }

    public static Dictionary<string, object> GetActiveRunSettings()
    {
        if (PreRunScript.instance != null && PreRunScript.instance.runSettings != null)
        {
            return PreRunScript.instance.runSettings;
        }

        return WorldGeneration.runSettings;
    }

    private static bool TryReadDisplayValue(RunSettingDisplay display, out object value)
    {
        value = null;
        Transform valueControl = display.transform.GetChild(1);

        if (display.isFloat)
        {
            Slider slider = valueControl.GetComponent<Slider>();
            if (slider == null)
            {
                return false;
            }

            value = slider.wholeNumbers ? Mathf.Round(slider.value) : slider.value;
            return true;
        }

        if (display.isBool)
        {
            Toggle toggle = valueControl.GetComponent<Toggle>();
            if (toggle == null)
            {
                return false;
            }

            value = toggle.isOn;
            return true;
        }

        if (display.isDropdown)
        {
            TMP_Dropdown dropdown = valueControl.GetComponent<TMP_Dropdown>();
            if (dropdown == null)
            {
                return false;
            }

            value = dropdown.value;
            return true;
        }

        return false;
    }

    public static void SetRunSetting(string key, object value, string reason)
    {
        Dictionary<string, object> settings = GetActiveRunSettings();
        if (settings == null)
        {
            return;
        }

        settings[key] = value;
        if (PreRunScript.instance != null && PreRunScript.instance.runSettings != settings)
        {
            PreRunScript.instance.runSettings[key] = value;
        }

        if (WorldGeneration.runSettings != null && WorldGeneration.runSettings != settings)
        {
            WorldGeneration.runSettings[key] = value;
        }
    }

    public static void ApplyConfiguredServerOverrides(string reason)
    {
        if (!ShouldApplyServerRunSettings())
        {
            return;
        }

        string raw = KrokoshaRunSettingsCompatPlugin.ServerRunSettingOverrides.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        Dictionary<string, object> settings = GetActiveRunSettings();
        if (settings == null)
        {
            return;
        }

        foreach (string entry in raw.Split(EntrySeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = entry.Trim();
            int equals = trimmed.IndexOf('=');
            if (equals <= 0 || equals >= trimmed.Length - 1)
            {
                KrokoshaRunSettingsCompatPlugin.Log.LogWarning($"Ignoring malformed RunSetting override '{trimmed}'. Expected name=value.");
                continue;
            }

            string key = trimmed.Substring(0, equals).Trim();
            string valueText = trimmed.Substring(equals + 1).Trim();
            if (!TryParseRunSettingValue(key, valueText, settings, out object value, out string error))
            {
                KrokoshaRunSettingsCompatPlugin.Log.LogWarning($"Ignoring RunSetting override '{trimmed}': {error}");
                continue;
            }

            SetRunSetting(key, value, reason);
        }
    }

    private static bool ShouldApplyServerRunSettings()
    {
        return !KrokoshaScavMultiplayer.network_system_is_running || KrokoshaScavMultiplayer.is_server;
    }

    private static bool TryParseRunSettingValue(
        string key,
        string valueText,
        Dictionary<string, object> settings,
        out object value,
        out string error)
    {
        value = null;
        error = null;

        RunSetting setting = RunSettings.settingTypes.FirstOrDefault(candidate => candidate.name == key);
        if (setting == null && !settings.ContainsKey(key))
        {
            error = "unknown RunSetting name";
            return false;
        }

        Type valueType = setting != null
            ? GetRunSettingValueType(setting)
            : settings[key].GetType();

        if (valueType == typeof(bool))
        {
            if (TryParseBool(valueText, out bool boolValue))
            {
                value = boolValue;
                return true;
            }

            error = "expected bool (true/false/1/0/on/off)";
            return false;
        }

        if (valueType == typeof(int))
        {
            if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intValue))
            {
                value = intValue;
                return true;
            }

            error = "expected integer";
            return false;
        }

        if (float.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            value = floatValue;
            return true;
        }

        error = "expected number";
        return false;
    }

    private static Type GetRunSettingValueType(RunSetting setting)
    {
        if (setting is RunSettingBool)
        {
            return typeof(bool);
        }

        if (setting is RunSettingDropdown)
        {
            return typeof(int);
        }

        return typeof(float);
    }

    private static bool TryParseBool(string valueText, out bool value)
    {
        if (bool.TryParse(valueText, out value))
        {
            return true;
        }

        switch (valueText.Trim().ToLowerInvariant())
        {
            case "1":
            case "yes":
            case "on":
                value = true;
                return true;
            case "0":
            case "no":
            case "off":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }
}
