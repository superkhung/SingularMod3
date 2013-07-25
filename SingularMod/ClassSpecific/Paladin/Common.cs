﻿using System.Collections.Generic;
using System.Linq;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;

using Styx;

using Styx.CommonBot;
using Styx.WoWInternals.WoWObjects;
using Styx.TreeSharp;
using System;
using Styx.WoWInternals;

using Action = Styx.TreeSharp.Action;
using Rest = Singular.Helpers.Rest;

namespace Singular.ClassSpecific.Paladin
{
    public class Common
    {
        private static PaladinSettings PaladinSettings { get { return SingularSettings.Instance.Paladin(); } }
        private static LocalPlayer Me { get { return StyxWoW.Me; } }

        [Behavior(BehaviorType.PreCombatBuffs, WoWClass.Paladin)]
        public static Composite CreatePaladinPreCombatBuffs()
        {
            return new Decorator(
                ret => !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(),
                new PrioritySelector(
                    CreatePaladinBlessBehavior(),
                    CreatePaladinSealBehavior(),
                    new Decorator(
                        ret => TalentManager.CurrentSpec != WoWSpec.PaladinHoly,
                        new PrioritySelector(
                            Spell.BuffSelf("Righteous Fury", ret => TalentManager.CurrentSpec == WoWSpec.PaladinProtection && StyxWoW.Me.GroupInfo.IsInParty)
                            )
                        )
                    )
                );
        }

        [Behavior(BehaviorType.LossOfControl, WoWClass.Paladin)]
        public static Composite CreatePaladinLossOfControlBehavior()
        {
            return new Decorator(
                ret => !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(),
                new PrioritySelector(
                    Spell.BuffSelf("Hand of Freedom")
                    )
                );
        }

        [Behavior(BehaviorType.CombatBuffs, WoWClass.Paladin, (WoWSpec)int.MaxValue, WoWContext.Normal, 1)]
        public static Composite CreatePaladinCombatBuffs()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(
                        Spell.Cast("Repentance", 
                            onUnit => 
                            Unit.NearbyUnfriendlyUnits
                            .Where(u => (u.IsPlayer || u.IsDemon || u.IsHumanoid || u.IsDragon || u.IsGiant || u.IsUndead)
                                    && Me.CurrentTargetGuid != u.Guid
                                    && (u.Aggro || u.PetAggro || (u.Combat && u.IsTargetingMeOrPet))
                                    && !u.IsCrowdControlled()
                                    && u.Distance.Between(10, 30) && Me.IsSafelyFacing(u) && u.InLineOfSpellSight && (!Me.GotTarget || u.Location.Distance(Me.CurrentTarget.Location) > 10))
                            .OrderByDescending(u => u.Distance)
                            .FirstOrDefault())
                        )
                    )
                );
        }



        /// <summary>
        /// cast Blessing of Kings or Blessing of Might based upon configuration setting.
        /// 
        /// </summary>
        /// <returns></returns>
        private static Composite CreatePaladinBlessBehavior()
        {
            return
                new PrioritySelector(
                    /*
                        PartyBuff.BuffGroup( 
                            "Blessing of Kings", 
                            ret => PaladinSettings.Blessings == PaladinBlessings.Auto || PaladinSettings.Blessings == PaladinBlessings.Kings,
                            "Blessing of Might"),

                        PartyBuff.BuffGroup(
                            "Blessing of Might",
                            ret => PaladinSettings.Blessings == PaladinBlessings.Auto || PaladinSettings.Blessings == PaladinBlessings.Might, 
                            "Blessing of Kings")*/
                    );
        }

        /// <summary>
        /// behavior to cast appropriate seal 
        /// </summary>
        /// <returns></returns>
        public static Composite CreatePaladinSealBehavior()
        {
            return new Throttle( TimeSpan.FromMilliseconds(500),
                new Sequence(
                    new Action( ret => _seal = GetBestSeal() ),
                    new Decorator(
                        ret => _seal != PaladinSeal.None
                            && !Me.HasMyAura(SealSpell(_seal))
                            && SpellManager.CanCast(SealSpell(_seal), Me),
                        Spell.Cast( s => SealSpell(_seal), on => Me, ret => !Me.HasAura(SealSpell(_seal)))
                        )
                    )
                );
        }

        static PaladinSeal _seal;

        static string SealSpell( PaladinSeal s)
        { 
            return "Seal of " + s.ToString(); 
        }

        /// <summary>
        /// determines the best PaladinSeal value to use.  Attempts to use 
        /// user setting first, but defaults to something reasonable otherwise
        /// </summary>
        /// <returns>PaladinSeal to use</returns>
        public static PaladinSeal GetBestSeal()
        {
            if (PaladinSettings.Seal == PaladinSeal.None)
                return PaladinSeal.None;

            if (StyxWoW.Me.Specialization == WoWSpec.None)
                return SpellManager.HasSpell("Seal of Command") ? PaladinSeal.Command : PaladinSeal.None;

            PaladinSeal bestSeal = Settings.PaladinSeal.Truth;
            if (PaladinSettings.Seal != Settings.PaladinSeal.Auto )
                bestSeal = PaladinSettings.Seal;
            else
            {
                switch (Me.Specialization)
                {
                    case WoWSpec.PaladinHoly:
                        if (Me.IsInGroup())
                            bestSeal = Settings.PaladinSeal.Insight;
                        break;

                    // Seal Twisting.  fixed bug in prior implementation that would cause it
                    // .. to flip seal too quickly.  When we have Insight and go above 5%
                    // .. would cause casting another seal, which would take back below 5% and
                    // .. and recast Insight.  Wait till we build up to 30% if we do this to 
                    // .. avoid wasting mana and gcd's
                    case WoWSpec.PaladinRetribution:
                    case WoWSpec.PaladinProtection:
                        if (Me.ManaPercent < 5 || (Me.ManaPercent < 30 && Me.HasMyAura("Seal of Insight")))
                            bestSeal = Settings.PaladinSeal.Insight;
                        else if (SingularRoutine.CurrentWoWContext == WoWContext.Battlegrounds)
                            bestSeal = Settings.PaladinSeal.Truth;
                        else if (Unit.NearbyUnfriendlyUnits.Count(u => u.Distance <= 8) >= 8)
                            bestSeal = Settings.PaladinSeal.Righteousness;
                        break;
                }
            }

            if (!SpellManager.HasSpell(SealSpell(bestSeal)))
                bestSeal = Settings.PaladinSeal.Command;

            if (bestSeal == Settings.PaladinSeal.Command && SpellManager.HasSpell("Seal of Truth"))
                bestSeal = Settings.PaladinSeal.Truth;

            return bestSeal;
        }

        public static bool HasTalent(PaladinTalents tal)
        {
            return TalentManager.IsSelected((int)tal);
        }
    }

    public enum PaladinTalents
    {
        SpeedOfLight = 1,
        LongArmOfTheLaw,
        PursuitOfJustice,
        FistOfJustice,
        Repentance,
        BurdenOfGuilt,
        SelflessHealer,
        EternalFlame,
        SacredShield,
        HandOfPurity,
        UnbreakableSpirit,
        Clemency,
        HolyAvenger,
        SanctifiedWrath,
        DivinePurpose,
        HolyPrism,
        LightsHammer,
        ExecutionSentence
    }
}
