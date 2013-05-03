﻿using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using System.Linq;
using Styx;
using Styx.Common.Helpers;
using Styx.Common;
using Styx.CommonBot;
using Styx.TreeSharp;
using System;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Rest = Singular.Helpers.Rest;
using Action = Styx.TreeSharp.Action;

namespace Singular.ClassSpecific.Warrior
{
    public class Fury
    {
		private static LocalPlayer Me { get { return StyxWoW.Me; } }
		private static WarriorSettings WarriorSettings { get { return SingularSettings.Instance.Warrior(); } }

        [Behavior(BehaviorType.Pull | BehaviorType.Combat, WoWClass.Warrior, WoWSpec.WarriorFury, WoWContext.All)]
        public static Composite CreateFuryCombat()
        {
            return new PrioritySelector(
                MainDPS()
            );
        }

		//Handle DPS Rotation - Optimized by superkhung
        private static Composite MainDPS()
        {
            return new Decorator( ret => !Me.Mounted,
                new PrioritySelector
			    (
                    Helpers.Common.CreateInterruptBehavior(),
                    Spell.Cast("Impending Victory", ret => Me.HealthPercent < 90 && Me.HasAura("Victorious")),
                    Spell.Cast("Die by the Sword", ret => Me.HealthPercent <= 30),
                    Spell.Cast("Rallying Cry", ret => Me.HealthPercent <= 20),

				    //Bloodthurst use on CD but only use to build rage on Execute phase
                    Spell.Cast("Bloodthirst", ret => Me.CurrentTarget.IsWithinMeleeRange),
                    Spell.Cast("Colossus Smash", ret => Me.CurrentTarget.IsWithinMeleeRange),
                    Spell.Cast
                    (
                        "Whirlwind", ret =>
                            //Get 3 stack Meat Cleaver for Raging Blow to hit 4 targets if more than 3 targets nearby
                        Me.CurrentRage >= 30 && Unit.NearbyUnfriendlyUnits.Count(u => u.IsWithinMeleeRange) >= 4 && !Me.HasAura("Meat Cleaver", 3) ||
                            //Get 2 stack Meat Cleaver for Raging Blow to hit 3 targets if 3 targets nearby
                        Me.CurrentRage >= 30 && Unit.NearbyUnfriendlyUnits.Count(u => u.IsWithinMeleeRange) == 3 && !Me.HasAura("Meat Cleaver", 2) ||
                            //Get 1 stack Meat Cleaver for Raging Blow to hit 2 targets if 2 targets nearby
                        Me.CurrentRage >= 30 && Unit.NearbyUnfriendlyUnits.Count(u => u.IsWithinMeleeRange) == 2 && !Me.HasAura("Meat Cleaver")
                    ),

                    Spell.Cast("Raging Blow", ret => Me.CurrentTarget.IsWithinMeleeRange),
				    //Handle selfbuff
                    Spell.BuffSelf("Berserker Rage", ret => !IsEnraged && Me.CurrentTarget.IsWithinMeleeRange),
                    Spell.BuffSelf("Bloodbath", ret => Me.CurrentTarget.IsWithinMeleeRange),
                    Spell.BuffSelf("Skull Banner", ret => WithinExecuteRange && Unit.IsBoss(Me.CurrentTarget)),
				    //Only use Recklessness on Execute phase
                    Spell.BuffSelf("Recklessness", ret => WithinExecuteRange && Unit.IsBoss(Me.CurrentTarget)),

				    //Spam Execute on Execute phase
                    Spell.Cast("Execute", ret => WithinExecuteRange),
                    Spell.Cast("Dragon Roar", ret => Me.CurrentTarget.IsWithinMeleeRange),

                    Spell.Cast("Heroic Strike", ret => NeedHeroicStrike),
                    Spell.Cast("Wild Strike", ret => !WithinExecuteRange && Me.HasAura("Bloodsurge")),
                    Spell.Cast("Battle Shout", ret => Me.CurrentRage < 60)
                )
        	);
        }

        #region Utils
        private static readonly WaitTimer InterceptTimer = new WaitTimer(TimeSpan.FromMilliseconds(2000));

        private static bool PreventDoubleIntercept
        {
            get
            {
                var tmp = InterceptTimer.IsFinished;
                if (tmp)
                    InterceptTimer.Reset();
                return tmp;
            }
        }


        #endregion

        #region Calculations

        static TimeSpan BTCD { get { return Spell.GetSpellCooldown("Bloodthirst"); } }
        static TimeSpan CSCD { get { return Spell.GetSpellCooldown("Colossus Smash"); } }

        static bool WithinExecuteRange { get { return Me.CurrentTarget.HealthPercent <= 20; } }
        static bool IsEnraged { get { return Me.HasAuraWithMechanic(WoWSpellMechanic.Enraged); } }
        private static bool TargetSmashed { get { return Me.CurrentTarget.HasMyAura("Colossus Smash"); } }


        static bool NeedHeroicStrike
        {
            get
            {
                if (Me.CurrentTarget.HealthPercent >= 20)
                {
                    var myRage = Me.RagePercent;

                    if (myRage >= 45 && TargetSmashed)
                        return true;
                    if (myRage >= 90)
                        return true;
                    if (myRage >= 40 && Me.HasAura("Deadly Calm"))
                        return true;
                }
                return false;
            }
        }

        #endregion
    }
}
