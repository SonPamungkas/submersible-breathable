using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SubmarineTestbed
{
    [HarmonyPatch]
    internal static class WaterlineFix
    {
        private const float SpringK = 2f;
        private const float DampK = 2f;
        private const float AngularDampK = 1f;
        private const float SurfaceNudgeK = 0.2f;
        private const float SurfaceNudgeDampK = 0.2f;

        private static readonly HashSet<Ship> _loggedBypass = new HashSet<Ship>();
        private static readonly Dictionary<Ship, float> _lastPinLogTime = new Dictionary<Ship, float>();
        private static readonly HashSet<Ship> _loggedTorqueSuppression = new HashSet<Ship>();

        [HarmonyPatch(typeof(Ship), "CheckShipBuoyancy")]
        [HarmonyPrefix]
        private static bool CheckShipBuoyancy_Prefix(Ship __instance)
        {
            if (!SubmarineTestbedPlugin.Cfg_DisableWaterKill.Value) return true;
            if (!SubmarineTestbedPlugin.TryGetEffectiveOffset(__instance.gameObject.name, out _)) return true;

            if (_loggedBypass.Add(__instance))
            {
                SubmarineTestbedPlugin.ModLogger.LogInfo(
                    $"[SubmarineTestbed] [WaterlineFix] CheckShipBuoyancy bypass fired for '{__instance.gameObject.name}' - water-contact/capsize/nose-dive kill suppressed.");
            }

            return false;
        }

        [HarmonyPatch(typeof(Ship), "ApplyPartsForce")]
        [HarmonyPrefix]
        private static bool ApplyPartsForce_Prefix(Ship __instance)
        {
            if (!SubmarineTestbedPlugin.TryGetEffectiveOffset(__instance.gameObject.name, out float offset)) return true;

            var rb = __instance.rb;
            if (rb == null) return true;

            Vector3 forceSum = Vector3.zero;
            foreach (ShipPart part in __instance.parts)
            {
                if (part.IsDetached() || !part.JobFields.IsCreated) continue;
                forceSum += part.JobFields.Ref().force;
            }
            rb.AddForce(forceSum);

            if (_loggedTorqueSuppression.Add(__instance))
            {
                SubmarineTestbedPlugin.ModLogger.LogInfo(
                    $"[SubmarineTestbed] [WaterlineFix] ApplyPartsForce torque suppressed for '{__instance.gameObject.name}' - applying lift-only buoyancy force.");
            }

            return false;
        }

        [HarmonyPatch(typeof(Ship), "ApplyJobResults")]
        [HarmonyPostfix]
        private static void ApplyJobResults_Postfix(Ship __instance)
        {
            if (!SubmarineTestbedPlugin.TryGetEffectiveOffset(__instance.gameObject.name, out float offset)) return;

            var rb = __instance.rb;
            if (rb == null) return;

            Transform transform = __instance.transform;
            string goName = __instance.gameObject.name;
            float spawnOffsetY = __instance.definition.spawnOffset.y;

            float liveGlobalY = GlobalPositionExtensions.GlobalY(transform.position);
            float targetGlobalY = spawnOffsetY + offset;

            bool dived = SubmarineTestbedPlugin.IsDived(goName);
            bool surfaced = SubmarineTestbedPlugin.IsSurfaced(goName);

            float yError = targetGlobalY - liveGlobalY;
            float forceY = surfaced
                ? yError * SurfaceNudgeK - rb.velocity.y * SurfaceNudgeDampK
                : (yError * SpringK - rb.velocity.y * DampK);
            rb.AddForce(new Vector3(0f, forceY, 0f), ForceMode.Acceleration);

            if (surfaced && liveGlobalY >= spawnOffsetY && rb.velocity.y > 0f)
            {
                rb.velocity = new Vector3(rb.velocity.x, 0f, rb.velocity.z);
            }

            bool pulseActive = SubmarineTestbedPlugin.TryGetSecondsSinceResurface(goName, out float secondsSinceResurface)
                && secondsSinceResurface <= SubmarineTestbedPlugin.Cfg_ResurfacePulseSeconds.Value;

            bool stabilizerApplied = false;
            if (pulseActive)
            {
                rb.AddTorque(-transform.right * SubmarineTestbedPlugin.Cfg_ResurfacePitchTorque.Value, ForceMode.Acceleration);
            }
            else
            {
                rb.AddTorque(-rb.angularVelocity * AngularDampK, ForceMode.Acceleration);

                if (surfaced)
                {
                    Vector3 tiltAxis = Vector3.Cross(transform.up, Vector3.up);
                    if (tiltAxis.sqrMagnitude > 0.0001f)
                    {
                        stabilizerApplied = true;
                        rb.AddTorque(tiltAxis * SubmarineTestbedPlugin.Cfg_SurfaceStabilizeTorque.Value, ForceMode.Acceleration);
                    }
                }
            }

            float tiltAngle = Vector3.Angle(transform.up, Vector3.up);

            if (SubmarineTestbedPlugin.Cfg_VerboseLogging.Value)
            {
                float now = Time.time;
                if (!_lastPinLogTime.TryGetValue(__instance, out float lastLog) || now - lastLog >= 1f)
                {
                    _lastPinLogTime[__instance] = now;
                    SubmarineTestbedPlugin.ModLogger.LogInfo(
                        $"[SubmarineTestbed] [WaterlineFix] '{goName}' targetGlobalY={targetGlobalY:F2} (spawnOffset.y={spawnOffsetY:F2}, offset={offset:F2}) liveGlobalY={liveGlobalY:F2} yError={yError:F2} forceY={forceY:F2} dived={dived} surfaced={surfaced} pulseActive={pulseActive} stabilizerApplied={stabilizerApplied} tiltAngle={tiltAngle:F1}.");
                }
            }
        }
    }
}
