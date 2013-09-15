﻿
using System;
using System.Reflection;
using System.Linq;
using System.Windows.Forms;
using Singular.GUI;
using Singular.Helpers;
using Singular.Managers;
using Singular.Utilities;
using Styx;
using Styx.CommonBot;
using Styx.CommonBot.Routines;
using Styx.Helpers;
using Styx.WoWInternals.WoWObjects;
using System.Drawing;
using Styx.WoWInternals;
using System.IO;
using System.Collections.Generic;
using Styx.Common;
using Singular.Settings;

using Styx.Common.Helpers;

namespace Singular
{
    public partial class SingularRoutine : CombatRoutine
    {
        private static LogLevel _lastLogLevel = LogLevel.None;

        public static SingularRoutine Instance { get; private set; }

        public override string Name { get { return GetSingularRoutineName(); } }

        public override WoWClass Class { get { return StyxWoW.Me.Class; } }

        public override bool WantButton { get { return true; } }

        private static LocalPlayer Me { get { return StyxWoW.Me; } }

        public SingularRoutine()
        {
            Instance = this;
        }

        public override void Initialize()
        {
            WriteSupportInfo();

            _lastLogLevel = GlobalSettings.Instance.LogLevel;

            // When we actually need to use it, we will.
            Spell.GcdInitialize();

            TalentManager.Init();
            EventHandlers.Init();
            MountManager.Init();
            HotkeyDirector.Init();
            MovementManager.Init();
             // SoulstoneManager.Init();   // switch to using Death behavior
            Dispelling.Init();
            Singular.Lists.BossList.Init();

            //Logger.Write("Combat log event handler started.");
            // Do this now, so we ensure we update our context when needed.
            BotEvents.Player.OnMapChanged += e =>
            {
                // Don't run this handler if we're not the current routine!
                if (!WeAreTheCurrentCombatRoutine)
                    return;

                // Only ever update the context. All our internal handlers will use the context changed event
                // so we're not reliant on anything outside of ourselves for updates.
                UpdateContext();
            };

            TreeHooks.Instance.HooksCleared += () =>
            {
                // Don't run this handler if we're not the current routine!
                if (!WeAreTheCurrentCombatRoutine)
                    return;

                Logger.Write(Color.White, "Hooks cleared, re-creating behaviors");
                RebuildBehaviors(silent: true);
                Spell.GcdInitialize();   // probably not needed, but quick
            };

            GlobalSettings.Instance.PropertyChanged += (sender, e) =>
            {
                // Don't run this handler if we're not the current routine!
                if (!WeAreTheCurrentCombatRoutine)
                    return;

                // only LogLevel change will impact our behav trees
                // .. as we conditionally include/omit some diagnostic nodes if debugging
                // also need to keep a cached copy of prior value as the event
                // .. fires on the settor, not when the value is different
                if (e.PropertyName == "LogLevel" && _lastLogLevel != GlobalSettings.Instance.LogLevel)
                {
                    _lastLogLevel = GlobalSettings.Instance.LogLevel;
                    Logger.Write(Color.White, "HonorBuddy {0} setting changed to {1}, re-creating behaviors", e.PropertyName, _lastLogLevel.ToString());
                    RebuildBehaviors();
                    Spell.GcdInitialize();   // probably not needed, but quick
                }
            };

            // install botevent handler so we can consolidate validation on whether 
            // .. local botevent handlers should be called or not
            SingularBotEventInitialize();

            Logger.Write("Determining talent spec.");
            try
            {
                TalentManager.Update();
            }
            catch (Exception e)
            {
                StopBot(e.ToString());
            }
            Logger.Write("Current spec is " + TalentManager.CurrentSpec.ToString().CamelToSpaced());

            // write current settings to log file... only written at startup and when Save press in Settings UI
            SingularSettings.Instance.LogSettings();

            // Update the current WoWContext, and fire an event for the change.
            UpdateContext();

            // NOTE: Hook these events AFTER the context update.
            OnWoWContextChanged += (orig, ne) =>
                {
                    Logger.Write(Color.White, "Context changed, re-creating behaviors");
                    RebuildBehaviors();
                    Spell.GcdInitialize();
                    Singular.Lists.BossList.Init();
                };
            RoutineManager.Reloaded += (s, e) =>
                {
                    Logger.Write(Color.White, "Routines were reloaded, re-creating behaviors");
                    RebuildBehaviors(silent:true);
                    Spell.GcdInitialize();
                };


            // create silently since Start button will create a context change (at least first Start)
            // .. which will build behaviors again
            if (!Instance.RebuildBehaviors(true))
            {
                return;
            }

            Logger.WriteDebug(Color.White, "Verified behaviors can be created!");
            Logger.Write("Initialization complete!");
        }

        private static void WriteSupportInfo()
        {
            string singularName = GetSingularRoutineName();  // "Singular v" + GetSingularVersion();
            Logger.Write("Starting " + singularName);

            // save some support info in case we need
            Logger.WriteFile("{0:F1} days since Windows was restarted", TimeSpan.FromMilliseconds(Environment.TickCount).TotalHours / 24.0);
            Logger.WriteFile("{0} FPS currently in WOW", GetFPS());
            Logger.WriteFile("{0} ms of Latency in WOW", StyxWoW.WoWClient.Latency);
            Logger.WriteFile("{0} local system time", DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss"));

            // verify installed source integrity 
            try
            {
                string singularFolder = GetSingularSourcePath();

                FileCheckList actual = new FileCheckList();
                actual.Generate(singularFolder);

                FileCheckList certified = new FileCheckList();
                certified.Load(Path.Combine(singularFolder, "singular.xml"));

                List<FileCheck> err = certified.Compare(actual);

                List<FileCheck> fcerrors = FileCheckList.Test(GetSingularSourcePath());
                if (!fcerrors.Any())
                    Logger.Write("Installation: integrity verified for {0}", GetSingularVersion());
                else
                {
                    Logger.Write(Color.HotPink, "Installation: modified by user - forum support may not available", singularName);
                    Logger.WriteFile("=== following {0} files with issues ===", fcerrors.Count);
                    foreach (var fc in fcerrors)
                    {
                        if ( !File.Exists( fc.Name ))
                            Logger.WriteFile(Styx.Common.LogLevel.Diagnostic, "   deleted: {0} {1}", fc.Size, fc.Name);
                        else if ( certified.Filelist.Any( f => 0 == String.Compare( f.Name, fc.Name, true)))
                            Logger.WriteFile(Styx.Common.LogLevel.Diagnostic, "   changed: {0} {1}", fc.Size, fc.Name);
                        else
                            Logger.WriteFile(Styx.Common.LogLevel.Diagnostic, "   inserted {0} {1}", fc.Size, fc.Name);
                    }
                    Logger.WriteFile(Styx.Common.LogLevel.Diagnostic, "");
                }
            }
            catch (FileNotFoundException e)
            {
                Logger.Write(Color.HotPink, "Installation: file missing - forum support not available");
                Logger.Write(Color.HotPink, "missing file: {0}", e.FileName );
            }
            catch (Exception e)
            {
                Logger.Write(Color.HotPink, "Installation: verification error - forum support not available");
                Logger.WriteFile(e.ToString());
            }
        }

        /// <summary>
        /// Stop the Bot writing a reason to the log file.  
        /// Revised to account for TreeRoot.Stop() now 
        /// throwing an exception if called too early 
        /// before tree is run
        /// </summary>
        /// <param name="reason">text to write to log as reason for Bot Stop request</param>
        private static void StopBot(string reason)
        {
            if (!TreeRoot.IsRunning)
                reason = "Bot Cannot Run: " + reason;
            else
            {
                reason = "Stopping Bot: " + reason;

                if (countRentrancyStopBot == 0)
                {
                    countRentrancyStopBot++;
                    if (TreeRoot.Current != null)
                        TreeRoot.Current.Stop();

                    TreeRoot.Stop();
                }
            }

            Logger.Write(Color.HotPink,reason);
        }

        static int countRentrancyStopBot = 0;

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        private static uint GetFPS()
        {
            try
            {
                return (uint)Lua.GetReturnVal<float>("return GetFramerate()", 0);
            }
            catch
            {

            }

            return 0;
        }

        public static string GetSingularRoutineName()
        {
            return "Singular v" + GetSingularVersion();
        }

        public static string GetSingularSourcePath()
        {
            // bit of a hack, but source code folder for the assembly is only 
            // .. available from the filename of the .dll
            FileCheck fc = new FileCheck();
            Assembly singularDll = Assembly.GetExecutingAssembly();
            FileInfo fi = new FileInfo(singularDll.Location);
            int len = fi.Name.LastIndexOf("_");
            string folderName = fi.Name.Substring(0, len);

            folderName = Path.Combine(Styx.Helpers.GlobalSettings.Instance.CombatRoutinesPath, folderName);

            // now check if relative path and if so, append to honorbuddy folder
            if (!Path.IsPathRooted(folderName))
                folderName = Path.Combine(GetHonorBuddyFolder(), folderName);

            return folderName;
        }

        public static string GetHonorBuddyFolder()
        {
            string hbpath = Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly().Location);
            return hbpath;
        }

        public static bool WeAreTheCurrentCombatRoutine
        {
            get
            {
                return RoutineManager.Current.Name == SingularRoutine.Instance.Name;
            }
        }

        private static bool IsMounted
        {
            get
            {
                if (StyxWoW.Me.Class == WoWClass.Druid)
                {
                    switch (StyxWoW.Me.Shapeshift)
                    {
                        case ShapeshiftForm.FlightForm:
                        case ShapeshiftForm.EpicFlightForm:
                            return true;
                    }
                }

                return StyxWoW.Me.Mounted;
            }
        }

        private ConfigurationForm _configForm;
        public override void OnButtonPress()
        {
            if (_configForm == null || _configForm.IsDisposed || _configForm.Disposing)
            {
                _configForm = new ConfigurationForm();
                _configForm.Height = SingularSettings.Instance.FormHeight;
                _configForm.Width = SingularSettings.Instance.FormWidth;
                TabControl tab = (TabControl)_configForm.Controls["tabControl1"];
                tab.SelectedIndex = SingularSettings.Instance.FormTabIndex;
            }

            _configForm.Show();
        }

        static DateTime _nextNoCallMsgAllowed = DateTime.MinValue;

        public override void Pulse()
        {
            // No pulsing if we're loading or out of the game.
            if (!StyxWoW.IsInGame || !StyxWoW.IsInWorld)
                return;

            // check time since last call and be sure user knows if Singular isn't being called
            if (SingularSettings.Debug)
            {
                TimeSpan since = CallWatch.SinceLast;
                if (since.TotalSeconds > (4 * CallWatch.WarnTime))
                {
                    if (!Me.IsGhost && !Me.Mounted && !Me.IsFlying && DateTime.Now > _nextNoCallMsgAllowed)
                    {
                        Logger.WriteDebug(Color.HotPink, "warning: {0:F0} seconds since {1} BotBase last called Singular", since.TotalSeconds, GetBotName());
                        _nextNoCallMsgAllowed = DateTime.Now.AddSeconds(4 * CallWatch.WarnTime);
                    }
                }
            }

            // talentmanager.Pulse() intense if does work, so return if true
            if (TalentManager.Pulse())
                return;

            // check and output casting state information
            UpdateDiagnosticCastingState();

            // Update the current context, check if we need to rebuild any behaviors.
            UpdateContext();

            // Double cast shit
            Spell.DoubleCastPreventionDict.RemoveAll(t => DateTime.UtcNow > t);

            MonitorPullDistance();

            // Output if Target changed 
            CheckCurrentTarget();

            // Pulse our StopAt manager
            StopMoving.Pulse();

            //Only pulse for classes with pets
            switch (StyxWoW.Me.Class)
            {
                case WoWClass.Hunter:
                case WoWClass.DeathKnight:
                case WoWClass.Warlock:
                case WoWClass.Mage:
                    PetManager.Pulse();
                    break;
            }

            if (Me.IsInGroup())
            {
                if (CurrentWoWContext != WoWContext.Normal)
                    HealerManager.Instance.Pulse();

                if (Group.MeIsTank && CurrentWoWContext == WoWContext.Instances)
                    TankManager.Instance.Pulse();
            }

            HotkeyDirector.Pulse();
        }

        private static ulong _lastCheckCurrTargetGuid = 0;
        private static ulong _lastCheckPetsTargetGuid = 0;

        private void CheckCurrentTarget()
        {
            if (!SingularSettings.Debug)
                return;

            CheckTarget(Me.CurrentTarget, ref _lastCheckCurrTargetGuid, "YourCurrentTarget");
            if (Me.GotAlivePet)
                CheckTarget(Me.Pet.CurrentTarget, ref _lastCheckPetsTargetGuid, "PetsCurrentTarget");
        }


        private void CheckTarget(WoWUnit unit, ref ulong prevGuid, string description)
        {
            // there are moments where CurrentTargetGuid != 0 but CurrentTarget == null. following
            // .. tries to handle by only checking CurrentTarget reference and treating null as guid = 0
            if (unit == null)
            {
                if (prevGuid != 0)
                {
                    prevGuid = 0;
                    Logger.WriteDebug(description + ": changed to: (null)");
                    HandleTrainingDummy(unit);
                }
            }
            else if (unit.Guid != prevGuid)
            {
                prevGuid = unit.Guid;

                HandleTrainingDummy(unit);

                string info = "";
                if (Styx.CommonBot.POI.BotPoi.Current.Guid == Me.CurrentTargetGuid)
                    info += string.Format(", IsBotPoi={0}", Styx.CommonBot.POI.BotPoi.Current.Type);

                if (Styx.CommonBot.Targeting.Instance.TargetList.Contains(Me.CurrentTarget))
                    info += string.Format(", TargetIndex={0}", Styx.CommonBot.Targeting.Instance.TargetList.IndexOf(Me.CurrentTarget) + 1);

                string playerInfo = "N";
                if (unit.IsPlayer)
                {
                    WoWPlayer p = unit.ToPlayer();
                    playerInfo = string.Format("Y, Friend={0}, IsPvp={1}, CtstPvp={2}, FfaPvp={3}", Me.IsHorde == p.IsHorde, p.IsPvPFlagged, p.ContestedPvPFlagged, p.IsFFAPvPFlagged);
                }

                Logger.WriteDebug(description + ": changed to: {0} h={1:F1}%, maxh={2}, d={3:F1} yds, box={4:F1}, trivial={5}, player={6}, attackable={7}, neutral={8}, hostile={9}, entry={10}, faction={11}, loss={12}, facing={13}, blacklist={14}, combat={15}" + info,
                    unit.SafeName(),
                    unit.HealthPercent,
                    unit.MaxHealth,
                    unit.Distance,
                    unit.CombatReach,
                    unit.IsTrivial(),
                    playerInfo,
                     
                    unit.Attackable.ToYN(),
                    unit.IsNeutral().ToYN(),
                    unit.IsHostile.ToYN(),
                    unit.Entry,
                    unit.FactionId,
                    unit.InLineOfSpellSight.ToYN(),
                    Me.IsSafelyFacing(unit).ToYN(),
                    Blacklist.Contains(unit.Guid, BlacklistFlags.Combat).ToYN(),
                    unit.Combat.ToYN()
                    );
            }
        }

        private static void HandleTrainingDummy(WoWUnit unit)
        {
            bool trainingDummy = unit == null ? false : unit.IsTrainingDummy();

            if (trainingDummy && ForcedContext == WoWContext.None)
            {
                ForcedContext = WoWContext.Instances;
                // ForcedContext = WoWContext.Battlegrounds; 
                Logger.Write(Color.White, "Detected Training Dummy -- forcing {0} behaviors", CurrentWoWContext.ToString());
            }
            else if (!trainingDummy && ForcedContext != WoWContext.None)
            {
                ForcedContext = WoWContext.None;
                Logger.Write(Color.White, "Detected Training Dummy no longer target -- reverting to {0} behaviors", CurrentWoWContext.ToString());
            }
        }

        private static bool _lastIsGCD = false;
        private static bool _lastIsCasting = false;
        private static bool _lastIsChanneling = false;

        public static bool UpdateDiagnosticCastingState( bool retVal = false)
        {
            if (SingularSettings.Debug && SingularSettings.Instance.EnableDebugLoggingGCD)
            {
                if (_lastIsGCD != Spell.IsGlobalCooldown())
                {
                    _lastIsGCD = Spell.IsGlobalCooldown();
                    Logger.WriteDebug("CastingState:  GCD={0} GCDTimeLeft={1}", _lastIsGCD, (int)Spell.GcdTimeLeft.TotalMilliseconds);
                }
                if (_lastIsCasting != Spell.IsCasting())
                {
                    _lastIsCasting = Spell.IsCasting();
                    Logger.WriteDebug("CastingState:  Casting={0} CastTimeLeft={1}", _lastIsCasting, (int)Me.CurrentCastTimeLeft.TotalMilliseconds);
                }
                if (_lastIsChanneling != Spell.IsChannelling())
                {
                    _lastIsChanneling = Spell.IsChannelling();
                    Logger.WriteDebug("ChannelingState:  Channeling={0} ChannelTimeLeft={1}", _lastIsChanneling, (int)Me.CurrentChannelTimeLeft.TotalMilliseconds);
                }
            }
            return retVal;
        }
    }
}