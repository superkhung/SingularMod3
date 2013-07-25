﻿using System.Linq;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;

using Styx.CommonBot;
using Styx.TreeSharp;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Rest = Singular.Helpers.Rest;
using System.Drawing;

namespace Singular.ClassSpecific.Paladin
{
    public class Retribution
    {

        #region Properties & Fields

        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static PaladinSettings PaladinSettings { get { return SingularSettings.Instance.Paladin(); } }

        private const int RET_T13_ITEM_SET_ID = 1064;

        private static int NumTier13Pieces
        {
            get
            {
                return StyxWoW.Me.CarriedItems.Count(i => i.ItemInfo.ItemSetId == RET_T13_ITEM_SET_ID);
            }
        }

        private static bool Has2PieceTier13Bonus { get { return NumTier13Pieces >= 2; } }

        private static int _mobCount;

        #endregion

        #region Heal
        [Behavior(BehaviorType.Heal, WoWClass.Paladin, WoWSpec.PaladinRetribution)]
        public static Composite CreatePaladinRetributionHeal()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(                       
                        Spell.Cast("Lay on Hands",
                            mov => false,
                            on => Me,
                            req => Me.GetPredictedHealthPercent(true) <= PaladinSettings.LayOnHandsHealth),
                        Spell.Cast("Word of Glory",
                            mov => false,
                            on => Me,
                            req => Me.GetPredictedHealthPercent(true) <= PaladinSettings.WordOfGloryHealth && Me.CurrentHolyPower >= 3,
                            cancel => Me.HealthPercent > PaladinSettings.WordOfGloryHealth),
                        Spell.Cast("Flash of Light",
                            mov => false,
                            on => Me,
                            req => Me.GetPredictedHealthPercent(true) <= PaladinSettings.RetributionHealHealth,
                            cancel => Me.HealthPercent > PaladinSettings.RetributionHealHealth)
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Rest, WoWClass.Paladin, WoWSpec.PaladinRetribution)]
        public static Composite CreatePaladinRetributionRest()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(
                        Rest.CreateDefaultRestBehaviour( "Flash of Light", "Redemption")
                        )
                    )
                );
        }
        #endregion

        #region Normal Rotation

        [Behavior(BehaviorType.Pull | BehaviorType.Combat, WoWClass.Paladin, WoWSpec.PaladinRetribution, WoWContext.Battlegrounds )]
        public static Composite CreatePaladinRetributionNormalPullAndCombat()
        {
            return new PrioritySelector(

                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Combat"),

                Spell.WaitForCastOrChannel(),

                new Decorator( 
                    ret => !Spell.IsGlobalCooldown() && Me.GotTarget,
                    new PrioritySelector(
                        // aoe count
                        ActionAoeCount(),

                        CreateRetDiagnosticOutputBehavior(),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),

                        // Defensive
                        Spell.BuffSelf("Hand of Freedom",
                            ret => Me.HasAuraWithMechanic(WoWSpellMechanic.Dazed,
                                                                  WoWSpellMechanic.Disoriented,
                                                                  WoWSpellMechanic.Frozen,
                                                                  WoWSpellMechanic.Incapacitated,
                                                                  WoWSpellMechanic.Rooted,
                                                                  WoWSpellMechanic.Slowed,
                                                                  WoWSpellMechanic.Snared)),

                        Spell.BuffSelf("Divine Shield", ret => Me.HealthPercent <= 20 && !Me.HasAura("Forbearance") && (!Me.HasAura("Horde Flag") || !Me.HasAura("Alliance Flag"))),
                        Spell.BuffSelf("Divine Protection", ret => Me.HealthPercent <= PaladinSettings.DivineProtectionHealthProt),

                        Common.CreatePaladinSealBehavior(),

                        Spell.Cast( "Hammer of Justice", ret => PaladinSettings.StunMobsWhileSolo && SingularRoutine.CurrentWoWContext == WoWContext.Normal ),

                        //7	Blow buffs seperatly.  No reason for stacking while grinding.
                        Spell.Cast("Guardian of Ancient Kings", ret => PaladinSettings.RetAvengAndGoatK && (_mobCount >= 4 || Me.GotTarget && Me.CurrentTarget.TimeToDeath() > 30)),
                        Spell.Cast("Holy Avenger", ret => PaladinSettings.RetAvengAndGoatK && _mobCount < 4),
                        Spell.BuffSelf("Avenging Wrath", 
                            ret => PaladinSettings.RetAvengAndGoatK
                                && (_mobCount >= 4 || Me.GotTarget && Me.CurrentTarget.TimeToDeath() > 30 || (!Me.HasAura("Holy Avenger") && Spell.GetSpellCooldown("Holy Avenger").TotalSeconds > 10))),

                        Spell.Cast("Execution Sentence", ret => Me.CurrentTarget.TimeToDeath() > 15),
                        Spell.Cast("Holy Prism", on => Group.Tanks.FirstOrDefault(t => t.IsAlive && t.Distance < 40)),

                        new Decorator(
                            ret => _mobCount >= 2 && Spell.UseAOE,
                            new PrioritySelector(
                                //Spell.CastOnGround("Light's Hammer", loc => Me.CurrentTarget.Location, ret => 2 <= Clusters.GetClusterCount(Me.CurrentTarget, Unit.NearbyUnfriendlyUnits, ClusterType.Radius, 10f)),

                                // EJ: Inq > 5HP TV > ES > HoW > Exo > CS > Judge > 3-4HP TV (> SS)
                                Spell.BuffSelf("Inquisition", ret => Me.CurrentHolyPower > 0 && Me.GetAuraTimeLeft("Inquisition", true).TotalSeconds < 4),
                                Spell.Cast( ret => SpellManager.HasSpell("Divine Storm") ? "Divine Storm" : "Templar's Verdict", ret => Me.CurrentHolyPower == 5),
                                Spell.Cast("Execution Sentence" ),
                                Spell.Cast("Hammer of Wrath"),
                                Spell.Cast("Exorcism"),
                                Spell.Cast(ret => SpellManager.HasSpell("Divine Storm") ? "Divine Storm" : "Templar's Verdict", ret => Me.CurrentHolyPower >= 3),
                                Spell.Cast(ret => SpellManager.HasSpell("Hammer of the Righteous") ? "Hammer of the Righteous" : "Crusader Strike"),
                                Spell.Cast("Judgment"),                            
                                Spell.BuffSelf("Sacred Shield"),
                                Movement.CreateMoveToMeleeBehavior(true)
                                )
                            ),

                        // EJ: Inq > 5HP TV > ES > HoW > Exo > CS > Judge > 3-4HP TV (> SS)
                        Spell.BuffSelf("Inquisition", ret => Me.CurrentHolyPower > 0 && Me.GetAuraTimeLeft("Inquisition", true).TotalSeconds < 4),
                        Spell.Cast( "Templar's Verdict", ret => Me.CurrentHolyPower == 5),
                        Spell.Cast("Execution Sentence" ),
                        Spell.Cast("Hammer of Wrath"),
                        Spell.Cast("Exorcism"),
                        Spell.Cast("Crusader Strike"),
                        Spell.Cast("Judgment"),
                        Spell.Cast("Templar's Verdict", ret => Me.CurrentHolyPower >= 3),
                        Spell.BuffSelf("Sacred Shield")
                        )
                    ),

                // Move to melee is LAST. Period.
                Movement.CreateMoveToMeleeBehavior(true)
                );
        }

        private static Action ActionAoeCount()
        {
            return new Action(ret =>
            {
                _mobCount = Unit.NearbyUnfriendlyUnits.Count(u => u.Distance < (u.MeleeDistance() + 3));
                return RunStatus.Failure;
            });
        }

        #endregion


        #region Instance Rotation

        [Behavior(BehaviorType.Heal | BehaviorType.Pull | BehaviorType.Combat, WoWClass.Paladin, WoWSpec.PaladinRetribution, WoWContext.Instances | WoWContext.Normal)]
        public static Composite CreatePaladinRetributionInstancePullAndCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),

                Spell.WaitForCastOrChannel(),

                new Decorator( 
                    ret => !Spell.IsGlobalCooldown() && Me.GotTarget,
                    new PrioritySelector(
                        // aoe count
                        new Action(ret =>
                        {
                            _mobCount = Unit.NearbyUnfriendlyUnits.Count(u => u.Distance < (u.MeleeDistance() + 3));
                            return RunStatus.Failure;
                        }),

                       // CreateRetDiagnosticOutputBehavior(),

                        Helpers.Common.CreateAutoAttack(true),

                        Helpers.Common.CreateInterruptBehavior(),

                        // Defensive
                        Spell.BuffSelf("Hand of Freedom",
                                       ret =>
                                       !Me.Auras.Values.Any(
                                           a => a.Name.Contains("Hand of") && a.CreatorGuid == Me.Guid) &&
                                       Me.HasAuraWithMechanic(WoWSpellMechanic.Dazed,
                                                                      WoWSpellMechanic.Disoriented,
                                                                      WoWSpellMechanic.Frozen,
                                                                      WoWSpellMechanic.Incapacitated,
                                                                      WoWSpellMechanic.Rooted,
                                                                      WoWSpellMechanic.Slowed,
                                                                      WoWSpellMechanic.Snared)),

                        Spell.BuffSelf("Divine Shield", 
                            ret => Me.HealthPercent <= 20  && !Me.HasAura("Forbearance")),
                        Spell.BuffSelf("Divine Protection",
                            ret => Me.HealthPercent <= PaladinSettings.DivineProtectionHealthProt),

                        //Common.CreatePaladinSealBehavior(),

                        Spell.Cast( "Execution Sentence", ret => Me.CurrentTarget.TimeToDeath() > 12 && Me.CurrentTarget.IsBoss),
                        Spell.Cast( "Holy Prism", on => Group.Tanks.FirstOrDefault( t => t.IsAlive && t.Distance < 40)),

                        //Use Synapse Springs Engineering thingy if inquisition is up

                        new Decorator(
                            ret => _mobCount >= 2 && Spell.UseAOE,
                            new PrioritySelector(
                                Spell.CastOnGround("Light's Hammer", loc => Me.CurrentTarget.Location, ret => true),

                                // EJ Multi Rotation: Inq > 5HP TV > ES > HoW > Exo > CS > Judge > 3-4HP TV (> SS)
                                Spell.BuffSelf("Inquisition", ret => Me.CurrentHolyPower > 0 && Me.GetAuraTimeLeft("Inquisition", true).TotalSeconds < 3),
                                Spell.Cast(ret => SpellManager.HasSpell("Divine Storm") ? "Divine Storm" : "Templar's Verdict", ret => Me.CurrentHolyPower == 5),
                                Spell.Cast("Execution Sentence", ret => Me.CurrentTarget.IsBoss),
                                Spell.Cast("Hammer of Wrath"),
                                Spell.Cast("Exorcism", ret => Me.CurrentTarget.IsWithinMeleeRange),
                                Spell.Cast("Hammer of the Righteous"),
                                Spell.Cast(ret => SpellManager.HasSpell("Hammer of the Righteous") ? "Hammer of the Righteous" : "Crusader Strike"),
                                Spell.Cast("Judgment", ret => Unit.NearbyUnfriendlyUnits.FirstOrDefault(u => u.Distance.Between(0, 20) && Me.IsSafelyFacing(u)), ret => Me.HasAura("Glyph of Double Jeopardy")),
                                Spell.Cast("Judgment"),
                                Spell.Cast(ret => SpellManager.HasSpell("Divine Storm") ? "Divine Storm" : "Templar's Verdict", ret => Me.CurrentHolyPower >= 3),
                                Spell.BuffSelf("Sacred Shield"),
                                Movement.CreateMoveToMeleeBehavior(true)
                                )
                            ),

                        // Single Target Priority - EJ: Inq > 5HP TV > ES > HoW > Exo > CS > Judge > 3-4HP TV (> SS)
                        Spell.Cast("Guardian of Ancient Kings", ret => Me.CurrentTarget.IsBoss() && Me.ActiveAuras.ContainsKey("Inquisition")),
                        Spell.BuffSelf("Avenging Wrath", ret => Me.CurrentTarget.IsBoss() && Me.ActiveAuras.ContainsKey("Inquisition") && (Common.HasTalent(PaladinTalents.SanctifiedWrath) || Me.CurrentTarget.IsBoss() && Spell.GetSpellCooldown("Guardian of Ancient Kings").TotalSeconds <= 290)),
                        Spell.BuffSelf("Inquisition", ret => Me.CurrentHolyPower > 0 && Me.GetAuraTimeLeft("Inquisition", true).TotalSeconds <= 2),
                        Spell.Cast("Exorcism", ret => Me.CurrentTarget.IsWithinMeleeRange),
                        Spell.Cast("Templar's Verdict", ret => Me.CurrentHolyPower >= 3),
                        Spell.Cast("Execution Sentence", ret => Me.CurrentTarget.IsBoss),
                        Spell.Cast("Hammer of Wrath"),                       
                        Spell.Cast("Crusader Strike"),
                        Spell.Cast("Judgment"),
                        Spell.BuffSelf("Sacred Shield")
                        )
                    ),


                    // Move to melee is LAST. Period.
                    Movement.CreateMoveToMeleeBehavior(true)
                );
        }

        #endregion

        private static Composite CreateRetDiagnosticOutputBehavior()
        {
            if (!SingularSettings.Debug)
                return new Action( ret => { return RunStatus.Failure; } );

            return new Sequence(
                new Action( r => SingularRoutine.UpdateDiagnosticCastingState() ),
                new Action(r => TMsg.ShowTraceMessages = false),
                new ThrottlePasses(1, 1, 
                    new Action(ret =>
                    {
                        TMsg.ShowTraceMessages = true;

                        string sMsg;
                        sMsg = string.Format(".... h={0:F1}%, m={1:F1}%, moving={2}, mobs={3}",
                            Me.HealthPercent,
                            Me.ManaPercent,
                            Me.IsMoving.ToYN(),
                            _mobCount
                            );

                        WoWUnit target = Me.CurrentTarget;
                        if (target != null)
                        {
                            sMsg += string.Format(
                                ", {0}, {1:F1}%, {2:F1} yds, face={3}, loss={4}",
                                target.SafeName(),
                                target.HealthPercent,
                                target.Distance,
                                Me.IsSafelyFacing(target).ToYN(),
                                target.InLineOfSpellSight.ToYN()
                                );
                        }

                        Logger.WriteDebug(Color.LightYellow, sMsg);
                        return RunStatus.Failure;
                    })
                    )
                );
        }

    }

    public class TMsg : Decorator
    {
        public static bool ShowTraceMessages { get; set; }

        public TMsg(SimpleStringDelegate str)
            : base(ret => ShowTraceMessages, new Action(r => { Logger.WriteDebug(str(r)); return RunStatus.Failure; }))
        {
        }
    }

}
