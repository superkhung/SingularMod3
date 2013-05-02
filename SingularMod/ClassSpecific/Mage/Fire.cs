﻿using System.Linq;
using CommonBehaviors.Actions;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Styx;

using Styx.CommonBot;
using Styx.Helpers;


using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.TreeSharp;
using Action = Styx.TreeSharp.Action;
using Singular.Settings;
using System.Drawing;

namespace Singular.ClassSpecific.Mage
{
    public class Fire
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static MageSettings MageSettings { get { return SingularSettings.Instance.Mage(); } }

        #region Normal Rotation


        [Behavior(BehaviorType.Pull, WoWClass.Mage, WoWSpec.MageFire, WoWContext.Normal)]
        public static Composite CreateMageFireNormalPull()
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
                        Spell.Cast("Combustion", ret => Me.CurrentTarget.HasMyAura("Ignite")),
                        Spell.Cast("Inferno Blast", ret => Me.ActiveAuras.ContainsKey("Heating Up")),
                        Spell.Cast("Pyroblast")
                       )
                    ),

                Movement.CreateMoveToTargetBehavior(true, 38f)
                );
        }

        [Behavior(BehaviorType.Combat, WoWClass.Mage, WoWSpec.MageFire, WoWContext.Normal)]
        public static Composite CreateMageFireNormalCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Common.CreateStayAwayFromFrozenTargetsBehavior(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Combat"),

/*
                new Throttle(8,
                    new Decorator(
                        ret => Me.CastingSpell != null && Me.CastingSpell.Name == "Fireball"
                            && Me.HasAura("Heating Up")
                            && SpellManager.HasSpell("Inferno Blast"),
                        new Action(r =>
                        {
                            Logger.Write("/cancel Fireball for Heating Up proc");
                            SpellManager.StopCasting();
                        })
                        )
                    ),
*/
                Spell.WaitForCast(true),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                        new Action( r => { Me.CurrentTarget.TimeToDeath(); return RunStatus.Failure; } ),

                        CreateFireDiagnosticOutputBehavior(),

                        // move to highest in priority to ensure this is cast
                        new Decorator( 
                            ret => !Me.CurrentTarget.IsImmune( WoWSpellSchool.Fire),
                            new PrioritySelector(
                                Spell.Cast("Pyroblast", ret => Me.ActiveAuras.ContainsKey("Pyroblast!")),
                                Spell.Cast("Inferno Blast", ret => Me.ActiveAuras.ContainsKey("Heating Up"))
                                )
                            ),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),

                        new Decorator(
                            ret => !Unit.NearbyUnfriendlyUnits.Any(u => u.SpellDistance() < 10 && u.IsCrowdControlled())
                                && (Unit.NearbyUnitsInCombatWithMe.Count() > 1 || Me.CurrentTarget.TimeToDeath() > 4),
                            Spell.BuffSelf("Frost Nova", ret => Unit.NearbyUnfriendlyUnits.Any(u => u.SpellDistance() < 8 && !u.IsFrozen()))
                            ),

                        // AoE comes first
                        new Decorator(
                            ret => Spell.UseAOE && Me.Level >= 25 && Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 3,
                            new PrioritySelector(
                                Movement.CreateEnsureMovementStoppedBehavior(5f),
                                Spell.Cast("Inferno Blast", ret => TalentManager.HasGlyph("Fire Blast") && Me.CurrentTarget.HasAnyAura("Frost Bomb", "Living Bomb", "Nether Tempest")),
                                Spell.Cast("Dragon's Breath", ret => Me.CurrentTarget.DistanceSqr <= 12 * 12),
                                Spell.CastOnGround("Flamestrike", loc => Me.CurrentTarget.Location),
                                Spell.Cast("Arcane Explosion", ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) >= 3),
                                new Decorator(
                                    ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) >= 3,
                                    Movement.CreateMoveToTargetBehavior(true, 10f)
                                    )
                                )
                            ),

                        Movement.CreateEnsureMovementStoppedBehavior(35f),

                        Spell.Cast("Dragon's Breath",
                            ret => Me.IsSafelyFacing(Me.CurrentTarget, 90) &&
                                   Me.CurrentTarget.DistanceSqr <= 12 * 12),

                        Common.CreateMagePolymorphOnAddBehavior(),

                        Spell.Cast("Deep Freeze",
                             ret => Me.CurrentTarget.IsFrozen()),

                        Spell.Cast("Counterspell", ret => Me.CurrentTarget.IsCasting && Me.CurrentTarget.CanInterruptCurrentSpellCast),

                        // Single Target
                        // living bomb in Common
                        new Decorator(
                            ret =>  !Me.CurrentTarget.IsImmune(WoWSpellSchool.Fire),
                            new PrioritySelector(
                                Spell.Cast("Combustion", ret => Me.CurrentTarget.HasMyAura("Ignite")),
                                Spell.Cast("Pyroblast", 
                                    ret => Me.ActiveAuras.ContainsKey("Pyroblast!") || (Me.CurrentTarget.IsFrozen() && !Unit.NearbyUnitsInCombatWithMe.Any(u => u.Guid != Me.CurrentTargetGuid))),
                                Spell.Cast("Inferno Blast", ret => Me.ActiveAuras.ContainsKey("Heating Up")),
                                Spell.Cast("Fire Blast", ret => !SpellManager.HasSpell("Inferno Blast")),
                                Spell.Cast("Scorch"),
                                Spell.Cast("Fireball")
                                )
                            ),

                        // 
                        Spell.Cast("Ice Lance", ret => (Me.IsMoving || Me.CurrentTarget.IsFrozen()) && Me.CurrentTarget.IsImmune(WoWSpellSchool.Fire)),
                        Spell.Cast("Frostfire Bolt", ret => !SpellManager.HasSpell("Fireball") || Me.CurrentTarget.IsImmune(WoWSpellSchool.Fire))
                        )
                    ),

                Movement.CreateMoveToTargetBehavior(true, 38f)
                );

        }

        #endregion

        #region Battleground Rotation
        [Behavior(BehaviorType.Pull|BehaviorType.Combat, WoWClass.Mage, WoWSpec.MageFire, WoWContext.Battlegrounds)]
        public static Composite CreateMageFirePvPPullAndCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Common.CreateStayAwayFromFrozenTargetsBehavior(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),
                Movement.CreateEnsureMovementStoppedBehavior(35f),
                Helpers.Common.CreateAutoAttack(true),
                Spell.WaitForCast(true),

                // Defensive stuff
                Spell.BuffSelf("Ice Block", ret => Me.HealthPercent < 10 && !Me.ActiveAuras.ContainsKey("Hypothermia")),
                // Spell.BuffSelf("Blink", ret => MovementManager.IsClassMovementAllowed && (Me.IsRooted() || Unit.NearbyUnitsInCombatWithMe.Any( u => u.IsWithinMeleeRange ))),
                Spell.BuffSelf("Mana Shield", ret => Me.HealthPercent <= 75),
                Spell.BuffSelf("Frost Nova", ret => Unit.NearbyUnfriendlyUnits.Any(u => u.DistanceSqr <= 8 * 8 && !u.IsFrozen() && !u.Stunned)),
                
                Common.CreateUseManaGemBehavior(ret => Me.ManaPercent < 80),

                // Cooldowns
                Spell.BuffSelf("Evocation", ret => Me.ManaPercent < 30),
                Spell.BuffSelf("Mirror Image"),
                Spell.BuffSelf("Mage Ward", ret => Me.HealthPercent <= 75),
                Spell.Cast("Deep Freeze", ret => Me.CurrentTarget.IsFrozen()),
                Spell.Cast("Counter Spell", ret => (Me.CurrentTarget.Class == WoWClass.Paladin ||Me.CurrentTarget.Class == WoWClass.Priest || Me.CurrentTarget.Class == WoWClass.Druid || Me.CurrentTarget.Class == WoWClass.Shaman) && Me.CurrentTarget.IsCasting && Me.CurrentTarget.HealthPercent >= 20),
                Spell.Cast("Dragon's Breath",
                    ret => Me.IsSafelyFacing(Me.CurrentTarget, 90) &&
                           Me.CurrentTarget.DistanceSqr <= 8 * 8),

                Spell.Cast("Fire Blast",
                    ret => Me.ActiveAuras.ContainsKey("Impact")),
                // Rotation
                
                Spell.Cast("Mage Bomb", ret => !Me.CurrentTarget.HasAura("Living Bomb") || (Me.CurrentTarget.HasAura("Living Bomb") && Me.CurrentTarget.GetAuraTimeLeft("Living Bomb", true).TotalSeconds <= 2)),
                 Spell.Cast("Inferno Blast", ret => Me.HasAura("Heating Up")),
                 Spell.Cast("Frost Bomb", ret => Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 1),
                Spell.Cast("Combustion", ret => Me.CurrentTarget.HasMyAura("Ignite") && Me.CurrentTarget.HasMyAura("Pyroblast")),

                Spell.Cast("Pyroblast", ret => Me.ActiveAuras.ContainsKey("Pyroblast!")),
                Spell.Cast("Fireball", ret => !SpellManager.HasSpell("Scorch")),
                Spell.Cast("Frostfire bolt", ret => !SpellManager.HasSpell("Fireball")),
                Spell.Cast("Scorch"),
                Movement.CreateMoveToTargetBehavior(true, 39f)
                );
        }

        #endregion

        #region Instance Rotation
        [Behavior(BehaviorType.Pull | BehaviorType.Combat, WoWClass.Mage, WoWSpec.MageFire, WoWContext.Instances)]
        public static Composite CreateMageFireInstancePullAndCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),
                /*
                                new Throttle( 8,
                                    new Decorator(
                                        ret => Me.CastingSpell != null && Me.CastingSpell.Name == "Fireball" 
                                            && Me.HasAura("Heating Up") 
                                            && SpellManager.HasSpell("Inferno Blast"),
                                        new Action(r => {
                                            Logger.Write("/cancel Fireball for Heating Up proc");
                                            SpellManager.StopCasting();
                                            })
                                        )
                                    ),
                */
                Spell.WaitForCast(true),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                        CreateFireDiagnosticOutputBehavior(),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),

                        // AoE comes first
                        new Decorator(
                            ret => Spell.UseAOE && Me.Level >= 25 && Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 3,
                            new PrioritySelector(
                                Movement.CreateEnsureMovementStoppedBehavior(5f),
                                Spell.Cast("Inferno Blast", ret => TalentManager.HasGlyph("Fire Blast") && Me.CurrentTarget.HasAnyAura("Frost Bomb", "Living Bomb", "Nether Tempest")),
                                Spell.Cast("Dragon's Breath", ret => Me.CurrentTarget.DistanceSqr <= 12 * 12),
                                Spell.CastOnGround("Flamestrike", loc => Me.CurrentTarget.Location),
                                Spell.Cast("Arcane Explosion", ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) >= 3),
                                new Decorator( 
                                    ret => Unit.NearbyUnfriendlyUnits.Count(t => t.Distance <= 10) >= 3,
                                    Movement.CreateMoveToTargetBehavior(true, 10f)
                                    )
                                )
                            ),

                        Movement.CreateEnsureMovementStoppedBehavior(35f),

                        // Single Target
                        // living bomb in Common
                        Spell.Cast("Combustion", ret => Me.CurrentTarget.HasMyAura("Ignite")),
                        Spell.Cast("Pyroblast", ret => Me.ActiveAuras.ContainsKey("Pyroblast!")),
                        Spell.Cast("Inferno Blast", ret => Me.ActiveAuras.ContainsKey("Heating Up")),
                        Spell.Cast("Fireball"),

                        Spell.Cast("Frostfire Bolt")
                        )
                    ),

                Movement.CreateMoveToTargetBehavior(true, 38f)
                );
        }

        #endregion

        #region Diagnostics

        private static Composite CreateFireDiagnosticOutputBehavior()
        {
            if (!SingularSettings.Debug)
                return new ActionAlwaysFail();

            return new Throttle(1,
                new Action(ret =>
                {
                    string line = string.Format(".... h={0:F1}%/m={1:F1}%, moving={2}, heatup={3} {4:F0} ms, pyroblst={5} {6:F0} ms",
                        Me.HealthPercent,
                        Me.ManaPercent,
                        Me.IsMoving,
                        Me.GetAuraStacks("Heating Up"),
                        Me.GetAuraTimeLeft("Heating Up").TotalMilliseconds,
                        Me.GetAuraStacks("Pyroblast!"),
                        Me.GetAuraTimeLeft("Pyroblast!").TotalMilliseconds
                        );

                    WoWUnit target = Me.CurrentTarget;
                    if (target == null)
                        line += ", target=(null)";
                    else
                    {
                        line += string.Format(", target={0} @ {1:F1} yds, h={2:F1}%, face={3}, loss={4}, frozen={5}",
                            target.SafeName(),
                            target.Distance,
                            target.HealthPercent,
                            Me.IsSafelyFacing(target),
                            target.InLineOfSpellSight,
                            target.GetAuraTimeLeft("Living Bomb").TotalMilliseconds,
                            target.IsFrozen()
                            );

                        if (Common.HasTalent(MageTalents.NetherTempest))
                            line += string.Format(", nethtmp={0}", (long)target.GetAuraTimeLeft("Nether Tempest", true).TotalMilliseconds);
                        else if (Common.HasTalent(MageTalents.LivingBomb))
                            line += string.Format(", livbomb={0}", (long)target.GetAuraTimeLeft("Living Bomb", true).TotalMilliseconds);
                        else if (Common.HasTalent(MageTalents.FrostBomb))
                            line += string.Format(", frstbmb={0}", (long)target.GetAuraTimeLeft("Frost Bomb", true).TotalMilliseconds);
                    }

                    Logger.WriteDebug(Color.Wheat, line);
                    return RunStatus.Success;
                })
                );
        }

        #endregion
    }
}
