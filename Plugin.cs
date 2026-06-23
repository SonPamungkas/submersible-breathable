using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SubmarineTestbed
{
    [BepInPlugin("neutral.submersible.breathable", "Submersible & Breathable", "1.0")]
    public class SubmarineTestbedPlugin : BaseUnityPlugin
    {
        private const string S_SUBMARINE = "Submarine Testbed";

        internal enum ShipState
        {
            Off = 0,
            Submerged = 1,
            Surfaced = 2,
        }

        internal class ShipEntry
        {
            public ConfigEntry<float> Offset;
            public ConfigEntry<KeyCode> Hotkey;
            public ShipState State = ShipState.Off;
            public float ResurfaceTriggerTime = float.NegativeInfinity;
            public float StateEnteredTime = float.NegativeInfinity;
            public Ship ShipRef;
            public GlobalPosition? CachedSurfacePos;
        }

        public static SubmarineTestbedPlugin Instance;
        public static ManualLogSource ModLogger;

        public static ConfigEntry<bool> Cfg_DisableWaterKill;
        public static ConfigEntry<float> Cfg_ResurfacePitchTorque;
        public static ConfigEntry<float> Cfg_ResurfacePulseSeconds;
        public static ConfigEntry<float> Cfg_SurfaceStabilizeTorque;
        public static ConfigEntry<bool> Cfg_VerboseLogging;

        internal static readonly Dictionary<string, ShipEntry> ShipEntries = new Dictionary<string, ShipEntry>();

        private void Awake()
        {
            Instance = this;
            ModLogger = base.Logger;

            Cfg_DisableWaterKill = Config.Bind(S_SUBMARINE, "Disable Water Kill", true,
                "When enabled, registered ships are not killed by touching/submerging below the sea surface, capsizing, or nose-diving (Ship.CheckShipBuoyancy). Kinetic damage to the bridge still kills the ship normally.");

            Cfg_ResurfacePitchTorque = Config.Bind(S_SUBMARINE, "Resurface Pitch Torque", 0.15f,
                new ConfigDescription(
                    "Strength of the pitch-up torque applied for a few seconds right after a ship surfaces.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Cfg_ResurfacePulseSeconds = Config.Bind(S_SUBMARINE, "Resurface Pulse Duration", 1f,
                new ConfigDescription(
                    "How many seconds the pitch-up pulse torque is applied for after surfacing.",
                    new AcceptableValueRange<float>(0f, 10f)));

            Cfg_SurfaceStabilizeTorque = Config.Bind(S_SUBMARINE, "Surface Stabilize Torque", 1f,
                new ConfigDescription(
                    "Constant-magnitude righting torque continuously applied while a ship is surfaced (outside the resurface pulse window) to counter roll/list.",
                    new AcceptableValueRange<float>(0f, 50f)));

            Cfg_VerboseLogging = Config.Bind(S_SUBMARINE, "Verbose Logging", false,
                "Enable verbose per-second diagnostic logging from WaterlineFix.");

            ScanShips();
            SceneManager.sceneLoaded += (_, __) =>
            {
                ScanShips();
                ResetAllShipStates();
            };

            var harmony = new Harmony("neutral.submersible.breathable");
            harmony.PatchAll();

            ModLogger.LogInfo("Submersible & Breathable loaded.");
        }

        private void Update()
        {
            foreach (var kvp in ShipEntries)
            {
                ShipEntry entry = kvp.Value;
                if (entry.Hotkey.Value == KeyCode.None) continue;
                if (!Input.GetKeyDown(entry.Hotkey.Value)) continue;

                if (entry.State == ShipState.Off)
                {
                    entry.State = ShipState.Submerged;
                    if (entry.ShipRef != null) entry.CachedSurfacePos = GlobalPositionExtensions.GlobalPosition(entry.ShipRef.transform);
                }
                else if (entry.State == ShipState.Submerged)
                {
                    entry.State = ShipState.Surfaced;
                    entry.ResurfaceTriggerTime = Time.time;
                }
                else
                {
                    entry.State = ShipState.Submerged;
                }

                entry.StateEnteredTime = Time.time;
                ModLogger.LogInfo($"[SubmarineTestbed] '{kvp.Key}' -> {entry.State}.");
            }
        }

        private void ScanShips()
        {
            foreach (Ship ship in Resources.FindObjectsOfTypeAll<Ship>())
            {
                if (ship == null) continue;
                string goName = ship.gameObject.name;
                if (ShipEntries.TryGetValue(goName, out ShipEntry existing))
                {
                    existing.ShipRef = ship;
                    continue;
                }

                string unitName = ship.definition != null ? ship.definition.unitName : goName;
                string label = $"{unitName} ({goName})";

                var offset = Config.Bind(S_SUBMARINE, $"Waterline Offset - {label}", 0f,
                    new ConfigDescription(
                        "Shifts this ship's effective waterline relative to the sea surface while submerged. Positive rides higher out of the water, negative sits deeper/submerges.",
                        new AcceptableValueRange<float>(-50f, 50f)));

                var hotkey = Config.Bind(S_SUBMARINE, $"Dive/Surface Hotkey - {label}", KeyCode.None,
                    "Cycles this ship through Off -> Submerged -> Surfaced -> Submerged -> ... KeyCode.None disables the hotkey.");

                ShipEntries[goName] = new ShipEntry { Offset = offset, Hotkey = hotkey, ShipRef = ship };

                ModLogger.LogInfo($"[SubmarineTestbed] Registered waterline slider + hotkey for '{label}'.");
            }
        }

        private void ResetAllShipStates()
        {
            foreach (ShipEntry entry in ShipEntries.Values)
            {
                entry.State = ShipState.Off;
                entry.ResurfaceTriggerTime = float.NegativeInfinity;
                entry.StateEnteredTime = float.NegativeInfinity;
                entry.CachedSurfacePos = null;
            }
        }

        internal static bool TryGetEffectiveOffset(string goName, out float offset)
        {
            if (ShipEntries.TryGetValue(goName, out ShipEntry entry) && entry.State != ShipState.Off)
            {
                offset = entry.State == ShipState.Submerged ? entry.Offset.Value : 0f;
                return true;
            }

            offset = 0f;
            return false;
        }

        internal static bool IsDived(string goName)
        {
            return ShipEntries.TryGetValue(goName, out ShipEntry entry) && entry.State == ShipState.Submerged;
        }

        internal static bool IsSurfaced(string goName)
        {
            return ShipEntries.TryGetValue(goName, out ShipEntry entry) && entry.State == ShipState.Surfaced;
        }

        internal static bool TryGetCachedSurfaceY(string goName, out float y)
        {
            if (ShipEntries.TryGetValue(goName, out ShipEntry entry) && entry.CachedSurfacePos.HasValue)
            {
                y = GlobalPositionExtensions.LocalY(entry.CachedSurfacePos.Value);
                return true;
            }

            y = 0f;
            return false;
        }

        internal static bool TryGetSecondsSinceResurface(string goName, out float seconds)
        {
            if (ShipEntries.TryGetValue(goName, out ShipEntry entry) && entry.State == ShipState.Surfaced &&
                !float.IsNegativeInfinity(entry.ResurfaceTriggerTime))
            {
                seconds = Time.time - entry.ResurfaceTriggerTime;
                return true;
            }

            seconds = 0f;
            return false;
        }

        internal static bool TryGetSecondsSinceStateChange(string goName, out float seconds)
        {
            if (ShipEntries.TryGetValue(goName, out ShipEntry entry) &&
                !float.IsNegativeInfinity(entry.StateEnteredTime))
            {
                seconds = Time.time - entry.StateEnteredTime;
                return true;
            }

            seconds = 0f;
            return false;
        }
    }
}
