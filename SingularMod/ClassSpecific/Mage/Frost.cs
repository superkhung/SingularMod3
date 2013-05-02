﻿using System.Linq;
using CommonBehaviors.Actions;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;

using Styx.CommonBot;
using Styx.Helpers;


using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.TreeSharp;
using Action = Styx.TreeSharp.Action;
using System;
using System.Drawing;

namespace Singular.ClassSpecific.Mage
{
    public class Frost
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static MageSettings MageSettings { get { return SingularSettings.Instance.Mage(); } }

        [Behavior(BehaviorType.PreCombatBuffs, WoWClass.Mage, WoWSpec.MageFrost, WoWContext.All, 1)]
        public static Composite CreateMageFrostPreCombatbuffs()
        {
            return new Decorator(
                ret => !Spell.IsCasting() && !Spell.IsGlobalCooldown(),
                new PrioritySelector(

                    CreateSummonWaterElemental()

                    )
                );
        }

        #region Normal Rotation
        [Behavior(BehaviorType.Pull, WoWClass.Mage, WoWSpec.MageFrost, WoWContext.Normal)]
        public static Composite CreateMageFrostNormalPull()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Common.CreateStayAwayFromFrozenTargetsBehavior(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),
                Helpers.Common.CreateAutoAttack(true),
                Movement.CreateEnsureMovementStoppedBehavior(35f),
                Spell.WaitForCast(true),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(
                        CreateSummonWaterElemental(),
                        Spell.Cast("Frostbolt", ret => !Me.CurrentTarget.IsImmune(WoWSpellSchool.Frost)),
                        Spell.Cast("Frostfire Bolt"),
                        Movement.CreateMoveToTargetBehavior(true, 38f)
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Combat, WoWClass.Mage, WoWSpec.MageFrost, WoWContext.Normal)]
        public static Composite CreateMageFrostNormalCombat()
        {
            return new PrioritySelector(
                 Safers.EnsureTarget(),
                 Common.CreateStayAwayFromFrozenTargetsBehavior(),
                 Movement.CreateMoveToLosBehavior(),
                 Movement.CreateFaceTargetBehavior(),
                 Movement.CreateEnsureMovementStoppedBehavior(35f),

                 Spell.WaitForCast(true),

                 new Decorator(
                     ret => !Spell.IsGlobalCooldown(),
                     new PrioritySelector(

                         CreateFrostDiagnosticOutputBehavior(), 

                        CreateSummonWaterElemental(),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),

                        new Decorator(
                            ret => !Unit.NearbyUnfriendlyUnits.Any(u => u.Distance <= 15 && !u.IsCrowdControlled()),
                            new PrioritySelector(
                                CastFreeze( on => Me.CurrentTarget ),
                                Spell.BuffSelf(
                                "Frost Nova",
                                    ret => Unit.NearbyUnfriendlyUnits.Any(u => u.Distance <= 11 && !u.IsFrozen())
                                    )
                                )
                            ),

                        Spell.Cast("Icy Veins"),

                        new Decorator(ret => Spell.UseAOE && Me.Level >= 25 && Unit.UnfriendlyUnitsNearTarget(10).Count() > 1,
                            new PrioritySelector(
                                Movement.CreateEnsureMovementStoppedBehavior(5f),
                                new Throttle(1,
                                    new Decorator(
                                        ret => !Me.HasAura("Fingers of Frost", 2),
                                        CastFreeze(on => Clusters.GetBestUnitForCluster(Unit.UnfriendlyUnitsNearTarget(8), ClusterType.Radius, 8))
                                        )
                                    ),
                                Spell.CastOnGround("Flamestrike", loc => Me.CurrentTarget.Location),
                                Spell.Cast("Frozen Orb"),
                                Spell.Cast("Fire Blast", ret => TalentManager.HasGlyph("Fire Blast") && Me.CurrentTarget.HasAnyAura("Frost Bomb", "Living Bomb", "Nether Tempest")),
                                Spell.Cast("Ice Lance", ret => Me.HasAura("Fingers of Frost") && Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) < 4),
                                Spell.Cast("Arcane Explosion", ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) >= 4),
                                new Decorator(
                                    ret => Unit.UnfriendlyUnitsNearTarget(10).Count() >= 4,
                                    Movement.CreateMoveToTargetBehavior(true, 10f)
                                    )
                                )
                            ),

                        Movement.CreateEnsureMovementStoppedBehavior(35f),

                        Common.CreateMagePolymorphOnAddBehavior(),

                        // nether tempest in CombatBuffs
                        Spell.Cast("Frozen Orb", ret => Spell.UseAOE ),

                        // on mobs that will live a long time, build up the debuff... otherwise react to procs more quickly
                        // this is the main element that departs from normal instance rotation
                        Spell.Cast("Frostbolt", 
                            ret => Me.CurrentTarget.TimeToDeath(20) >= 20
                                && (!Me.CurrentTarget.HasAura("Frostbolt", 3) || Me.CurrentTarget.HasAuraExpired("Frostbolt", 3)) 
                                && !Me.CurrentTarget.IsImmune(WoWSpellSchool.Frost)),

                        new Throttle( 1,
                            new Decorator( 
                                ret => !Me.HasAura("Fingers of Frost", 2),
                                CastFreeze( on => Clusters.GetBestUnitForCluster(Unit.UnfriendlyUnitsNearTarget(8), ClusterType.Radius, 8))
                                )
                            ),
                        Spell.Cast("Frostfire Bolt", ret => Me.HasAura("Brain Freeze")),
                        Spell.Cast("Ice Lance", ret => (Me.IsMoving || Me.ActiveAuras.ContainsKey("Fingers of Frost")) && !Me.CurrentTarget.IsImmune(WoWSpellSchool.Frost)),
                        Spell.Cast("Frostbolt", ret => !Me.CurrentTarget.IsImmune(WoWSpellSchool.Frost)),

                        new Decorator(
                            ret => Me.CurrentTarget.IsImmune(WoWSpellSchool.Frost),
                            new PrioritySelector(
                                Spell.Cast("Fire Blast"),
                                Spell.Cast("Frostfire Bolt")
                                )
                            )
                        )
                    ),

                Movement.CreateMoveToTargetBehavior(true, 38f)
                );
        }

        #endregion

        #region Battleground Rotation

        [Behavior(BehaviorType.Pull|BehaviorType.Combat, WoWClass.Mage, WoWSpec.MageFrost, WoWContext.Battlegrounds)]
        public static Composite CreateMageFrostPvPPullAndCombat()
        {
            return new PrioritySelector(
                 Safers.EnsureTarget(),
                 Common.CreateStayAwayFromFrozenTargetsBehavior(),
                 Movement.CreateMoveToLosBehavior(),
                 Movement.CreateFaceTargetBehavior(),
                 Helpers.Common.CreateDismount("Pulling"),
                 Movement.CreateEnsureMovementStoppedBehavior(35f),
                 Spell.WaitForCast(true),

                 new Decorator(
                     ret => !Spell.IsGlobalCooldown(),
                     new PrioritySelector(

                         CreateFrostDiagnosticOutputBehavior(),

                         CreateSummonWaterElemental(),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),

                         // Defensive stuff
                         // Spell.BuffSelf("Blink", ret => MovementManager.IsClassMovementAllowed && (Me.IsStunned() || Me.IsRooted())),
                         Spell.BuffSelf("Mana Shield", ret => Me.HealthPercent <= 75),



                         Spell.BuffSelf("Frost Nova", ret => Unit.NearbyUnfriendlyUnits.Any(u => u.DistanceSqr <= 8 * 8 && !u.IsFrozen() && !u.Stunned)),

                         Common.CreateUseManaGemBehavior(ret => Me.ManaPercent < 80),
                        // Cooldowns
                         Spell.BuffSelf("Evocation", ret => Me.ManaPercent < 30),
                         Spell.BuffSelf("Mirror Image"),
                         Spell.BuffSelf("Mage Ward", ret => Me.HealthPercent <= 75),
                         Spell.BuffSelf("Icy Veins"),

                         // Rotation
                         Spell.Cast("Frost Bomb", ret => Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 3),
                         Spell.Cast("Deep Freeze",
                             ret => Me.ActiveAuras.ContainsKey("Fingers of Frost") || Me.CurrentTarget.IsFrozen()),

                         new Decorator(
                             ret => Me.ActiveAuras.ContainsKey("Brain Freeze"),
                             new PrioritySelector(
                                 Spell.Cast("Frostfire Bolt"),
                                 Spell.Cast("Fireball")
                                 )),
                         Spell.Cast("Ice Lance",
                             ret => Me.ActiveAuras.ContainsKey("Fingers of Frost") || Me.CurrentTarget.IsFrozen() || Me.IsMoving),

                         Spell.Cast("Frostbolt")
                         )
                    ),

                 Movement.CreateMoveToTargetBehavior(true, 38f)
                 );
        }

        #endregion

        #region Instance Rotation
        [Behavior(BehaviorType.Pull | BehaviorType.Combat, WoWClass.Mage, WoWSpec.MageFrost, WoWContext.Instances)]
        public static Composite CreateMageFrostInstanceCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),
                Spell.WaitForCast(true),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                         CreateFrostDiagnosticOutputBehavior(),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),

                        Spell.Cast("Icy Veins"),

                        new Decorator(ret => Spell.UseAOE && Me.Level >= 25 && Unit.UnfriendlyUnitsNearTarget(10).Count() > 1,
                            new PrioritySelector(
                                new Throttle(1,
                                    new Decorator(
                                        ret => !Me.HasAura("Fingers of Frost", 2),
                                        CastFreeze(on => Clusters.GetBestUnitForCluster(Unit.UnfriendlyUnitsNearTarget(8), ClusterType.Radius, 8))
                                        )
                                    ),
                                Movement.CreateEnsureMovementStoppedBehavior(5f),
                                Spell.CastOnGround("Flamestrike", loc => Me.CurrentTarget.Location),
                                Spell.Cast("Frozen Orb"),
                                Spell.Cast("Fire Blast", ret => TalentManager.HasGlyph("Fire Blast") && Me.CurrentTarget.HasAnyAura("Frost Bomb", "Living Bomb", "Nether Tempest")),
                                Spell.Cast("Ice Lance", ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) < 4),
                                Spell.Cast("Arcane Explosion", ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) >= 4),
                                new Decorator(
                                    ret => Unit.UnfriendlyUnitsNearTarget(10).Count() >= 4,
                                    Movement.CreateMoveToTargetBehavior(true, 10f)
                                    )
                                )
                            ),

                        Movement.CreateEnsureMovementStoppedBehavior(25f),

                        // nether tempest in CombatBuffs
                        Spell.Cast("Frozen Orb", ret => Spell.UseAOE ),
                        Spell.Cast("Frostbolt", ret => !Me.CurrentTarget.HasAura("Frostbolt", 3) || Me.CurrentTarget.HasAuraExpired("Frostbolt", 3)),
                        new Throttle( 1,
                            new Decorator( 
                                ret => !Me.HasAura("Fingers of Frost", 2),
                                CastFreeze( on => Clusters.GetBestUnitForCluster(Unit.UnfriendlyUnitsNearTarget(8), ClusterType.Radius, 8))
                                )
                            ),
                        Spell.Cast("Frostfire Bolt", ret => Me.HasAura("Brain Freeze")),
                        Spell.Cast("Ice Lance", ret => Me.IsMoving || Me.ActiveAuras.ContainsKey("Fingers of Frost")),
                        Spell.Cast("Frostbolt")
                        )
                    ),

                Movement.CreateMoveToTargetBehavior(true, 30f)
                );
        }

        #endregion

        public static Composite CreateSummonWaterElementalOld()
        {
            return new PrioritySelector(
                new Decorator(
                    ret => SingularSettings.Instance.DisablePetUsage && Me.GotAlivePet,
                    new Action( ret => Lua.DoString("PetDismiss()"))
                    ),

                new Decorator(
                    ret => !SingularSettings.Instance.DisablePetUsage
                        && (!Me.GotAlivePet || Me.Pet.Distance > 40)
                        && PetManager.PetSummonAfterDismountTimer.IsFinished
                        && SpellManager.CanCast("Summon Water Elemental"),
                    new Sequence(
                        new Action(ret => PetManager.CallPet("Summon Water Elemental")),
                        Helpers.Common.CreateWaitForLagDuration()
                        )
                    )
                );
        }

        public static Composite CreateSummonWaterElemental()
        {
            return new Decorator(
                ret => (!Me.GotAlivePet || Me.Pet.Distance > 40)
                    && PetManager.PetSummonAfterDismountTimer.IsFinished
                    && SpellManager.CanCast("Summon Water Elemental"),

                new Sequence(
                    // wait for possible auto-spawn if supposed to have a pet and none present
                    new DecoratorContinue(
                        ret => !Me.GotAlivePet && !SingularSettings.Instance.DisablePetUsage,
                        new Sequence(
                            new Action(ret => Logger.WriteDebug("Summon Water Elemental:  waiting briefly for live pet to appear")),
                            new WaitContinue(
                                TimeSpan.FromMilliseconds(1000),
                                ret => Me.GotAlivePet,
                                new Sequence(
                                    new Action(ret => Logger.WriteDebug("Summon Water Elemental:  live pet detected")),
                                    new Action(r => { return RunStatus.Failure; })
                                    )
                                )
                            )
                        ),

                    // dismiss pet if not supposed to have one
                    new DecoratorContinue(
                        ret => Me.GotAlivePet && SingularSettings.Instance.DisablePetUsage,
                        new Sequence(
                            new Action(ret => Logger.WriteDebug("Summon Water Elemental:  dismissing pet")),
                            new Action(ctx => Lua.DoString("PetDismiss()")),
                            new WaitContinue(
                                TimeSpan.FromMilliseconds(1000),
                                ret => !Me.GotAlivePet,
                                new Action(ret => {
                                    Logger.WriteDebug("Summon Water Elemental:  dismiss complete");
                                    return RunStatus.Success;
                                    })
                                )
                            )
                        ),

                    // summon pet if we still need to
                    new DecoratorContinue(
                        ret => !Me.GotAlivePet && !SingularSettings.Instance.DisablePetUsage,
                        new Sequence(
                            new Action(ret => Logger.WriteDebug("Summon Water Elemental:  about to summon pet")),

                            // Heal() used intentionally here (has spell completion logic not present in Cast())
                            Spell.Cast(n => "Summon Water Elemental",
                                chkMov => true,
                                onUnit => Me,
                                req => true,
                                cncl => false),

                            // make sure we see pet alive before continuing
                            new Wait(1, ret => Me.GotAlivePet, new ActionAlwaysSucceed()),
                            new Action(ret => Logger.WriteDebug("Summon Water Elemental:  now have alive pet"))
                            )
                        )
                    )
                );
        }


        
        /// <summary>
        /// Cast "Freeze" pet ability on a target.  Uses a local store for location to
        /// avoid target position changing during cast preparation and being out of
        /// range after range check
        /// </summary>
        /// <param name="onUnit">target to cast on</param>
        /// <returns></returns>
        public static Composite CastFreeze( UnitSelectionDelegate onUnit )
        {
            return new Sequence(
                new Decorator( 
                    ret => onUnit != null && onUnit(ret) != null, 
                    new Action( ret => _locFreeze = onUnit(ret).Location)
                    ),
                Pet.CreateCastPetActionOnLocation(
                    "Freeze",
                    on => _locFreeze,
                    ret => Me.Pet.ManaPercent >= 12
                        && Me.Pet.Location.Distance(_locFreeze) < 45
                        && !Me.CurrentTarget.IsFrozen()
                    )
                );
        }

        static private WoWPoint _locFreeze;

        #region Diagnostics

        private static Composite CreateFrostDiagnosticOutputBehavior()
        {
            return new Decorator(
                ret => SingularSettings.Debug,
                new Throttle(1,
                    new Action(ret =>
                    {
                        string log;

                        log = string.Format(".... h={0:F1}%/m={1:F1}%, pet={2:F1}%, fof={3}, brnfrz={4}",
                            Me.HealthPercent,
                            Me.ManaPercent,
                            Me.GotAlivePet ? Me.Pet.HealthPercent : 0,
                            Me.GetAuraStacks("Fingers of Frost"),
                            (long)Me.GetAuraTimeLeft("Brain Freeze", true).TotalMilliseconds
                            );

                        WoWUnit target = Me.CurrentTarget;
                        if (target != null)
                        {
                            log += string.Format(", ttd={0}, th={1:F1}%, dist={2:F1}, face={3}, loss={4}, fboltstks={5}",
                                target.TimeToDeath(),
                                target.HealthPercent,
                                target.Distance,
                                Me.IsSafelyFacing(target),
                                target.InLineOfSpellSight,
                                target.GetAuraStacks("Frostbolt", true)
                                );

                            if (Common.HasTalent(MageTalents.NetherTempest))
                                log += string.Format(", nethtmp={0}", (long)target.GetAuraTimeLeft("Nether Tempest", true).TotalMilliseconds);
                            else if (Common.HasTalent(MageTalents.LivingBomb ))
                                log += string.Format( ", livbomb={0}", (long)target.GetAuraTimeLeft("Living Bomb", true).TotalMilliseconds);
                            else if (Common.HasTalent(MageTalents.FrostBomb))
                                log += string.Format( ", frstbmb={0}", (long)target.GetAuraTimeLeft("Frost Bomb", true).TotalMilliseconds);

                            if (target.HasAura("Freeze"))
                                log += string.Format(", freeze={0}", (long)target.GetAuraTimeLeft("Freeze", true).TotalMilliseconds);
                            else if (target.HasAura("Frost Nova"))
                                log += string.Format(", frostnova={0}", (long)target.GetAuraTimeLeft("Frost Nova", true).TotalMilliseconds);
                            else if (target.HasAura("Ring of Frost"))
                                log += string.Format(", ringfrost={0}", (long)target.GetAuraTimeLeft("Ring of Frost", true).TotalMilliseconds);
                            else if (target.HasAura("Frostjaw"))
                                log += string.Format(", frostjaw={0}", (long)target.GetAuraTimeLeft("Frostjaw", true).TotalMilliseconds);
                            else if (target.HasAura("Ice Ward"))
                                log += string.Format(", iceward={0}", (long)target.GetAuraTimeLeft("Ice Ward", true).TotalMilliseconds);
                            else if (target.IsFrozen())
                                log += string.Format(", isfrozen=Yes");
                        }

                        Logger.WriteDebug(Color.AntiqueWhite, log);
                    })
                    )
                );
        }

        #endregion
    }
}
