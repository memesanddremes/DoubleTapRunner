﻿using DoubleTapRunner;

using MelonLoader;

using BuildInfo = DoubleTapRunner.BuildInfo;

[assembly: MelonInfo(typeof(DoubleTapper), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author, BuildInfo.DownloadLink)]
[assembly: MelonGame("VRChat", "VRChat")]

namespace DoubleTapRunner
{

    using System;
    using System.Collections;
    using System.Linq;
    using System.Reflection;

    using Harmony;

    using MelonLoader;

    using UnhollowerRuntimeLib.XrefScans;

    using UnityEngine;

    using VRC.Core;
    using VRC.SDKBase;

    public class DoubleTapper : MelonMod
    {

        private const string SettingsCategory = "DoubleTapRunner";

        private static DoubleTapper instance;

        // Original Settings
        private static float walkSpeed, runSpeed, strafeSpeed;

        private static bool worldAllowed;

        private Settings activeSettings;

        private bool currentlyRunning;

        private float lastTimeClicked = 25f;

        private float previousAxis;

        private bool useAxisValues;

        public override void OnApplicationStart()
        {
            if (instance != null)
            {
                MelonLogger.LogError("There's already an instance of Double-Tap Runner. Remove the duplicate dll files");
                return;
            }

            instance = this;
            activeSettings = new Settings
                                 {
                                     Enabled = true,
                                     SpeedMultiplier = 2f,
                                     DoubleClickTime = .5f,
                                     Forward = KeyCode.W,
                                     Backward = KeyCode.S,
                                     Left = KeyCode.A,
                                     Right = KeyCode.D,
                                     AxisDeadZone = .1f,
                                     AxisClickThreshold = .6f
                                 };

            MelonPrefs.RegisterCategory(SettingsCategory, "Double-Tap Runner");
            MelonPrefs.RegisterBool(SettingsCategory, nameof(Settings.Enabled), activeSettings.Enabled, "Enabled");
            MelonPrefs.RegisterFloat(SettingsCategory, nameof(Settings.SpeedMultiplier), activeSettings.SpeedMultiplier, "Speed Multiplier");
            MelonPrefs.RegisterFloat(SettingsCategory, nameof(Settings.DoubleClickTime), activeSettings.DoubleClickTime, "Double Click Time");
            MelonPrefs.RegisterString(SettingsCategory, nameof(Settings.Forward), Enum.GetName(typeof(KeyCode), activeSettings.Forward), "Desktop Forwards");
            MelonPrefs.RegisterString(SettingsCategory, nameof(Settings.Backward), Enum.GetName(typeof(KeyCode), activeSettings.Backward), "Desktop Backwards");
            MelonPrefs.RegisterString(SettingsCategory, nameof(Settings.Left), Enum.GetName(typeof(KeyCode), activeSettings.Left), "Desktop Left");
            MelonPrefs.RegisterString(SettingsCategory, nameof(Settings.Right), Enum.GetName(typeof(KeyCode), activeSettings.Right), "Desktop Right");
            MelonPrefs.RegisterFloat(SettingsCategory, nameof(Settings.AxisDeadZone), activeSettings.AxisDeadZone, "Axis Dead Zone");
            MelonPrefs.RegisterFloat(SettingsCategory, nameof(Settings.AxisClickThreshold), activeSettings.AxisClickThreshold, "Axis Click Threshold");
            ApplySettings();

            try
            {
                MethodInfo fadeToMethod = typeof(VRCUiManager).GetMethods(BindingFlags.Public | BindingFlags.Instance).First(
                    m => m.GetParameters().Length == 3 && XrefScanner.XrefScan(m).Any(
                             xref =>
                                 {
                                     // @formatter:off
                                     if (xref.Type != XrefType.Global)
                                         return false;
                                     return xref.ReadAsObject()?.ToString().IndexOf("No fade", StringComparison.OrdinalIgnoreCase) >= 0;
                                     // @formatter:on
                                 }));
                harmonyInstance.Patch(
                    fadeToMethod,
                    null,
                    new HarmonyMethod(typeof(DoubleTapper).GetMethod(nameof(JoinedRoomPatch), BindingFlags.NonPublic | BindingFlags.Static)));
            }
            catch (Exception e)
            {
                MelonLogger.LogError("Failed to patch FadeTo: " + e.Message);
                MelonLogger.LogError("Could be a mod hooked into it as prefix");
            }
        }

        public override void OnModSettingsApplied()
        {
            ApplySettings();
        }

        public override void OnUpdate()
        {
            if (!activeSettings.Enabled
                || !worldAllowed) return;

            // Grab last used input method
            useAxisValues = Utilities.GetLastUsedInputMethod() switch
                {
                    VRCInputMethod.Keyboard => false,
                    VRCInputMethod.Mouse    => false,
                    _                       => true
                };

            // Axis
            if (useAxisValues)
            {
                // Do we want to maybe run? (●'◡'●)
                if (!currentlyRunning)
                {
                    // Clicked
                    if (!Utilities.AxisClicked("Vertical", ref previousAxis, activeSettings.AxisClickThreshold)) return;

                    // Woow, someone double clicked with a (VR)CONTROLLER!!! ╰(*°▽°*)╯
                    if (Time.time - lastTimeClicked <= activeSettings.DoubleClickTime)
                    {
                        currentlyRunning = true;
                        SetLocomotion();
                        lastTimeClicked = activeSettings.DoubleClickTime * 2f;
                    }

                    lastTimeClicked = Time.time;
                }

                // maybe we should stop?
                else
                {
                    if (Mathf.Abs(Input.GetAxis("Vertical") + Input.GetAxis("Horizontal")) < activeSettings.AxisDeadZone)
                    {
                        currentlyRunning = false;
                        SetLocomotion();
                    }
                }
            }

            // Keyboard
            else
            {
                // Do we want to maybe run? (●'◡'●)
                if (!currentlyRunning
                    && Utilities.HasDoubleClicked(activeSettings.Forward, ref lastTimeClicked, activeSettings.DoubleClickTime))
                {
                    currentlyRunning = true;
                    SetLocomotion();
                }

                // maybe we should stop?
                else if (currentlyRunning
                         && !Input.GetKey(activeSettings.Forward)
                         && !Input.GetKey(activeSettings.Backward)
                         && !Input.GetKey(activeSettings.Left)
                         && !Input.GetKey(activeSettings.Right))
                {
                    currentlyRunning = false;
                    SetLocomotion();
                }
            }
        }

        private static IEnumerator GrabCurrentLevelSettings()
        {
            // Disallow until proven otherwise
            worldAllowed = false;

            LocomotionInputController locomotion;
            while ((locomotion = Utilities.GetLocalVRCPlayer()?.GetComponent<LocomotionInputController>()) == null) yield return new WaitForSeconds(.5f);
            walkSpeed = locomotion.walkSpeed;
            runSpeed = locomotion.runSpeed;
            strafeSpeed = locomotion.strafeSpeed;

            string worldId = RoomManagerBase.field_Internal_Static_ApiWorld_0.id;

            // Check if blacklisted/whitelisted from EmmVRC - thanks Emilia and the rest of EmmVRC Staff
            WWW www = new WWW($"https://thetrueyoshifan.com/RiskyFuncsCheck.php?worldid={worldId}");
            while (!www.isDone)
                yield return new WaitForEndOfFrame();
            string result = www.text;
            www.Dispose();
            if (!string.IsNullOrWhiteSpace(result))
                switch (result.ToLower().Trim())
                {
                    case "allowed":
                        worldAllowed = true;
                        yield break;

                    case "denied":
                        worldAllowed = false;
                        yield break;
                }

            // Check tags then
            API.Fetch<ApiWorld>(
                worldId,
                new Action<ApiContainer>(
                    container =>
                        {
                            ApiWorld apiWorld = container.Model.Cast<ApiWorld>();
                            worldAllowed = true;
                            foreach (string worldTag in apiWorld.tags)
                            {
                                if (worldTag.IndexOf("game", StringComparison.OrdinalIgnoreCase) == -1) continue;
                                worldAllowed = false;
                                break;
                            }

                            instance.SetLocomotion();
                        }),
                disableCache: false);
        }

        private static void JoinedRoomPatch(string __0, float __1)
        {
            if (__0.Equals("BlackFade")
                && __1.Equals(0f)
                && RoomManagerBase.field_Internal_Static_ApiWorldInstance_0 != null) MelonCoroutines.Start(GrabCurrentLevelSettings());
        }

        private void ApplySettings()
        {
            activeSettings.Enabled = MelonPrefs.GetBool(SettingsCategory, nameof(Settings.Enabled));
            activeSettings.SpeedMultiplier = MelonPrefs.GetFloat(SettingsCategory, nameof(Settings.SpeedMultiplier));
            activeSettings.DoubleClickTime = MelonPrefs.GetFloat(SettingsCategory, nameof(Settings.DoubleClickTime));

            if (Enum.TryParse(MelonPrefs.GetString(SettingsCategory, nameof(Settings.Forward)), out KeyCode forward))
            {
                activeSettings.Forward = forward;
            }
            else
            {
                MelonLogger.LogError("Failed to parse Keycode Forward");
            }
            
            if (Enum.TryParse(MelonPrefs.GetString(SettingsCategory, nameof(Settings.Backward)), out KeyCode backward))
            {
                activeSettings.Backward = backward;
            }
            else
            {
                MelonLogger.LogError("Failed to parse Keycode Backward");
            }
            
            
            if (Enum.TryParse(MelonPrefs.GetString(SettingsCategory, nameof(Settings.Left)), out KeyCode left))
            {
                activeSettings.Left = left;
            }
            else
            {
                MelonLogger.LogError("Failed to parse Keycode Left");
            }
            
            
            if (Enum.TryParse(MelonPrefs.GetString(SettingsCategory, nameof(Settings.Right)), out KeyCode right))
            {
                activeSettings.Right = right;
            }
            else
            {
                MelonLogger.LogError("Failed to parse Keycode Right");
            }

            activeSettings.AxisDeadZone = MelonPrefs.GetFloat(SettingsCategory, nameof(Settings.AxisDeadZone));
            activeSettings.AxisClickThreshold = MelonPrefs.GetFloat(SettingsCategory, nameof(Settings.AxisClickThreshold));

            SetLocomotion();
        }

        private void SetLocomotion()
        {
            if (RoomManagerBase.field_Internal_Static_ApiWorldInstance_0 == null
                || RoomManagerBase.field_Internal_Static_ApiWorld_0 == null) return;

            if (!worldAllowed) currentlyRunning = false;

            LocomotionInputController locomotion = Utilities.GetLocalVRCPlayer()?.GetComponent<LocomotionInputController>();
            if (locomotion == null) return;

            locomotion.walkSpeed = walkSpeed * (activeSettings.Enabled && currentlyRunning ? activeSettings.SpeedMultiplier : 1f);
            locomotion.runSpeed = runSpeed * (activeSettings.Enabled && currentlyRunning ? activeSettings.SpeedMultiplier : 1f);
            locomotion.strafeSpeed = strafeSpeed * (activeSettings.Enabled && currentlyRunning ? activeSettings.SpeedMultiplier : 1f);
        }

        private struct Settings
        {

            public float DoubleClickTime;

            public bool Enabled;

            public float SpeedMultiplier;

            public KeyCode Forward, Backward, Left, Right;

            public float AxisDeadZone, AxisClickThreshold;

        }

    }

}