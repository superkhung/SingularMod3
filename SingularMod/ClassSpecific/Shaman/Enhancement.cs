﻿using System;
using System.Linq;
using System.Text;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;

using Styx;

using Styx.CommonBot;
using Styx.Helpers;


using Styx.WoWInternals;
using Styx.TreeSharp;
using Action = Styx.TreeSharp.Action;
using Singular.Lists;
using Styx.WoWInternals.WoWObjects;
using Rest = Singular.Helpers.Rest;
using System.Drawing;

namespace Singular.ClassSpecific.Shaman
{
    public class Enhancement
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static ShamanSettings ShamanSettings { get { return SingularSettings.Instance.Shaman(); } }

        #region Common

        [Behavior(BehaviorType.PreCombatBuffs|BehaviorType.CombatBuffs, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Instances | WoWContext.Normal)]
        public static Composite CreateShamanEnhancementPreCombatBuffs()
        {
            return new PrioritySelector(

                Spell.WaitForCastOrChannel(),

                Common.CreateShamanImbueMainHandBehavior(Imbue.Windfury, Imbue.Flametongue),
                Common.CreateShamanImbueOffHandBehavior( Imbue.Flametongue ),

                Common.CreateShamanDpsShieldBehavior(),

                Totems.CreateRecallTotems()
                );
        }

        [Behavior(BehaviorType.PreCombatBuffs|BehaviorType.CombatBuffs, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Battlegrounds)]
        public static Composite CreateShamanEnhancementPvpPreCombatBuffs()
        {
            return new PrioritySelector(

                Spell.WaitForCastOrChannel(),

                Common.CreateShamanImbueMainHandBehavior(Imbue.Windfury, Imbue.Flametongue),
                Common.CreateShamanImbueOffHandBehavior(Imbue.Frostbrand, Imbue.Flametongue),

                Common.CreateShamanDpsShieldBehavior(),

                Totems.CreateRecallTotems()
                );
        }

        [Behavior(BehaviorType.Rest, WoWClass.Shaman, WoWSpec.ShamanEnhancement)]
        public static Composite CreateShamanEnhancementRest()
        {
            return new PrioritySelector(
                Spell.WaitForCast(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                        Rest.CreateDefaultRestBehaviour("Healing Surge", "Ancestral Spirit"),

                        Common.CreateShamanMovementBuff()
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Normal)]
        public static Composite CreateShamanEnhancementHeal()
        {
            return new PrioritySelector(

                Spell.WaitForCastOrChannel(),

                Spell.Cast("Healing Surge", on => Me, 
                    ret => Me.GetPredictedHealthPercent(true) < 80 && StyxWoW.Me.HasAura("Maelstrom Weapon", 5)),

                Common.CreateShamanDpsHealBehavior()
                );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Instances)]
        public static Composite CreateShamanEnhancementHealInstances()
        {
            return Common.CreateShamanDpsHealBehavior( );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Battlegrounds )]
        public static Composite CreateShamanEnhancementHealPvp()
        {
            return new PrioritySelector(

                Spell.WaitForCastOrChannel(),

                new Decorator(ret => StyxWoW.Me.HasAura("Maelstrom Weapon", 5),
                    new PrioritySelector(
                        Spell.Cast("Healing Surge", ret => StyxWoW.Me, ret => StyxWoW.Me.GetPredictedHealthPercent() < 75),
                        Spell.Cast("Healing Surge", ret => (WoWPlayer)Unit.GroupMembers.Where(p => p.IsAlive && p.GetPredictedHealthPercent() < 50 && p.Distance < 40).FirstOrDefault())
                        )
                    ),

                new Decorator(
                    ret => !StyxWoW.Me.Combat || (!Me.IsMoving && !Unit.NearbyUnfriendlyUnits.Any()),
                    Common.CreateShamanDpsHealBehavior( )
                    )
                );
        }

        #endregion
/*
        #region Normal Rotation

        [Behavior(BehaviorType.Pull, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Normal)]
        public static Composite CreateShamanEnhancementNormalPull()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),
                Spell.WaitForCastOrChannel(),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                        CreateEnhanceDiagnosticOutputBehavior(),

                        Common.CreateShamanImbueMainHandBehavior(Imbue.Windfury, Imbue.Flametongue),
                        Common.CreateShamanImbueOffHandBehavior(Imbue.Flametongue),

                        Common.CreateShamanDpsShieldBehavior(),

                        new Decorator(
                            ret => StyxWoW.Me.Level < 20,
                            new PrioritySelector(
                                Spell.Cast("Lightning Bolt"),
                                Movement.CreateMoveToTargetBehavior(true, 35f)
                                )),

                        Helpers.Common.CreateAutoAttack(true),
                        Totems.CreateTotemsBehavior(),
                        Spell.Cast("Lightning Bolt", ret => StyxWoW.Me.HasAura("Maelstrom Weapon", 5)),
                        Spell.Cast("Unleash Weapon", 
                            ret => StyxWoW.Me.Inventory.Equipped.OffHand != null 
                                && StyxWoW.Me.Inventory.Equipped.OffHand.TemporaryEnchantment.Id == 5),
                        Spell.Cast("Earth Shock")
                        )
                    ),

                Movement.CreateMoveToMeleeBehavior(true)
                );
        }

        [Behavior(BehaviorType.Combat, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Normal)]
        public static Composite CreateShamanEnhancementNormalCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Spell.WaitForCastOrChannel(),
                Helpers.Common.CreateAutoAttack(true),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(

                        CreateEnhanceDiagnosticOutputBehavior(),

                        Helpers.Common.CreateInterruptBehavior(),

                        Common.CreateShamanImbueMainHandBehavior(Imbue.Windfury, Imbue.Flametongue),
                        Common.CreateShamanImbueOffHandBehavior(Imbue.Flametongue),

                        Common.CreateShamanDpsShieldBehavior(),
                        // Spell.BuffSelf("Spiritwalker's Grace", ret => StyxWoW.Me.IsMoving && StyxWoW.Me.Combat),
                        Spell.BuffSelf("Feral Spirit", ret => 
                            ShamanSettings.FeralSpiritCastOn == CastOn.All 
                            || (ShamanSettings.FeralSpiritCastOn == CastOn.Bosses && StyxWoW.Me.CurrentTarget.Elite)
                            || (ShamanSettings.FeralSpiritCastOn == CastOn.Players && Unit.NearbyUnfriendlyUnits.Any(u => u.IsPlayer && u.Combat && u.IsTargetingMeOrPet))),

                        Spell.Cast("Elemental Blast"),
                        Spell.Cast("Unleash Elements", ret => Common.HasTalent(ShamanTalents.UnleashedFury)),

                        Spell.Cast("Stormstrike"),
                        Spell.Buff("Flame Shock", true,
                            ret => (StyxWoW.Me.HasAura("Unleash Flame") || !SpellManager.HasSpell("Unleash Elements")) &&
                                   (StyxWoW.Me.CurrentTarget.Elite || (SpellManager.HasSpell("Fire Nova") && Unit.UnfriendlyUnitsNearTarget(10).Count(u => u.IsTargetingMeOrPet) >= 3))),
                        Spell.Cast("Earth Shock",
                            ret => StyxWoW.Me.CurrentTarget.GetAuraTimeLeft("Flame Shock", true).TotalSeconds > 3 
                                || !StyxWoW.Me.CurrentTarget.Elite
                                || !SpellManager.HasSpell("Flame Shock")),
                        Spell.Cast("Lava Lash",
                            ret => StyxWoW.Me.Inventory.Equipped.OffHand != null &&
                                   StyxWoW.Me.Inventory.Equipped.OffHand.ItemInfo.ItemClass == WoWItemClass.Weapon),
                        Spell.Buff("Fire Nova",
                            on => StyxWoW.Me.CurrentTarget,
                            ret => StyxWoW.Me.CurrentTarget.HasMyAura("Flame Shock") &&
                                   Unit.NearbyUnfriendlyUnits.Count(u => 
                                       u.IsTargetingMeOrPet &&
                                       u.Location.DistanceSqr(StyxWoW.Me.CurrentTarget.Location) < 10 * 10) >= 3),
                        Spell.Cast("Primal Strike", ret => !SpellManager.HasSpell("Stormstrike")),
                        Spell.Cast("Unleash Elements"),

                        new Decorator(ret => StyxWoW.Me.HasAura("Maelstrom Weapon", 5) && (StyxWoW.Me.GetAuraTimeLeft("Maelstom Weapon", true).TotalSeconds < 3000 || StyxWoW.Me.GetPredictedHealthPercent(true) > 90),
                            new PrioritySelector(
                                Spell.Cast("Chain Lightning", ret => Spell.UseAOE && Spell.UseAOE && Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 2 && !Unit.UnfriendlyUnitsNearTarget(10f).Any(u => u.IsCrowdControlled())),
                                Spell.Cast("Lightning Bolt")
                                )
                            )
                        )
                    ),

                Movement.CreateMoveToMeleeBehavior(true)
                );
        }

        #endregion
*/
        #region Battleground Rotation

        [Behavior(BehaviorType.Pull | BehaviorType.Combat, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Battlegrounds)]
        public static Composite CreateShamanEnhancementPvPPullAndCombat()
        {
            return new PrioritySelector(
                Safers.EnsureTarget(),
                Movement.CreateMoveToLosBehavior(),
                Movement.CreateFaceTargetBehavior(),
                Helpers.Common.CreateDismount("Pulling"),
                Spell.WaitForCastOrChannel(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(), 
                    new PrioritySelector(

                        CreateEnhanceDiagnosticOutputBehavior(),

                        Helpers.Common.CreateAutoAttack(true),
                        Helpers.Common.CreateInterruptBehavior(),
                        Totems.CreateTotemsBehavior(),
                        Common.CreateShamanImbueMainHandBehavior(Imbue.Windfury, Imbue.Flametongue),
                        Common.CreateShamanImbueOffHandBehavior(Imbue.Frostbrand, Imbue.Flametongue),

                        Common.CreateShamanDpsShieldBehavior(),

                        // Spell.BuffSelf("Spiritwalker's Grace", ret => StyxWoW.Me.IsMoving && StyxWoW.Me.Combat),
                        Spell.BuffSelf("Feral Spirit", ret =>
                            ShamanSettings.FeralSpiritCastOn == CastOn.All
                            || (ShamanSettings.FeralSpiritCastOn == CastOn.Bosses && StyxWoW.Me.CurrentTarget.Elite)
                            || (ShamanSettings.FeralSpiritCastOn == CastOn.Players && Unit.NearbyUnfriendlyUnits.Any(u => u.IsPlayer && u.Combat && u.IsTargetingMeOrPet))),

                        Spell.Cast("Elemental Blast"),
                        Spell.Cast("Unleash Elements", ret => Common.HasTalent(ShamanTalents.UnleashedFury)),

                        Spell.Cast("Stormstrike"),
                        Spell.Cast("Primal Strike", ret => !SpellManager.HasSpell("Stormstrike")),
                        Spell.Cast("Lava Lash", 
                            ret => StyxWoW.Me.Inventory.Equipped.OffHand != null && 
                                   StyxWoW.Me.Inventory.Equipped.OffHand.ItemInfo.ItemClass == WoWItemClass.Weapon),

                        new Decorator(ret => StyxWoW.Me.HasAura("Maelstrom Weapon", 5) && (StyxWoW.Me.GetAuraTimeLeft("Maelstom Weapon", true).TotalSeconds < 3000 || StyxWoW.Me.GetPredictedHealthPercent() > 90),
                            new PrioritySelector(
                                Spell.Cast("Chain Lightning", ret => Spell.UseAOE && Unit.UnfriendlyUnitsNearTarget(10f).Count() >= 2 && !Unit.UnfriendlyUnitsNearTarget(10f).Any(u => u.IsCrowdControlled())),
                                Spell.Cast("Lightning Bolt")
                                )
                            ),

                        Spell.Cast("Unleash Elements"),
                        Spell.Buff("Flame Shock", true, ret => StyxWoW.Me.HasAura("Unleash Flame") || !SpellManager.HasSpell("Unleash Elements")),
                        Spell.Cast("Earth Shock", ret => StyxWoW.Me.CurrentTarget.GetAuraTimeLeft("Flame Shock", true).TotalSeconds > 6)
                        )
                    ),

                Movement.CreateMoveToMeleeBehavior(true)
                );
        }

        #endregion

        #region Instance Rotation
        [Behavior(BehaviorType.Pull | BehaviorType.Combat, WoWClass.Shaman, WoWSpec.ShamanEnhancement, WoWContext.Instances | WoWContext.Normal)]
        public static Composite CreateShamanEnhancementInstancePullAndCombat()
        {
            return new PrioritySelector(
                //Safers.EnsureTarget(),
                //Movement.CreateMoveToLosBehavior(),
                //Movement.CreateFaceTargetBehavior(),
                //Helpers.Common.CreateDismount("Pulling"),
                Spell.WaitForCast(true),
                //Helpers.Common.CreateAutoAttack(true),
                
                new Decorator(
                    ret => !Spell.IsGlobalCooldown() && !Me.Mounted,
                    new PrioritySelector(

                        //CreateEnhanceDiagnosticOutputBehavior(),

                        Helpers.Common.CreateInterruptBehavior(),

                        Common.CreateShamanDpsShieldBehavior(),
                        Totems.CreateTotemsBehavior(),
                        // Spell.BuffSelf("Spiritwalker's Grace", ret => StyxWoW.Me.IsMoving && StyxWoW.Me.Combat),
                        Spell.BuffSelf("Astral Shift", ret => Me.HealthPercent <= 40),
                        Spell.BuffSelf("Healing Surge", ret => Me.HealthPercent <= 50 && Me.HasAura("Maelstrom Weapon", 5) && !Me.CurrentTarget.IsBoss),
                        Spell.Cast("Feral Spirit", ret => StyxWoW.Me.CurrentTarget.IsBoss || StyxWoW.Me.CurrentTarget.IsPlayer),
                        Spell.BuffSelf("Ascendance", ret => StyxWoW.Me.CurrentTarget.IsBoss || StyxWoW.Me.CurrentTarget.IsPlayer),                   
                        Spell.Cast("Fire Elemental Totem", ret => StyxWoW.Me.CurrentTarget.IsBoss || StyxWoW.Me.CurrentTarget.IsPlayer),
                        new Decorator(
                            ret => Spell.UseAOE && Unit.UnfriendlyUnitsNearTarget(10f).Count() >= SingularSettings.Instance.AOENumber,
                            new PrioritySelector(
                                Spell.BuffSelf("Magma Totem", ret => !Totems.Exist(WoWTotem.Magma)),
                                Spell.Cast("Unleash Elements"),
                                Spell.Cast("Flame Shock"),
                                Spell.Cast("Lava Lash", ret => StyxWoW.Me.CurrentTarget.HasMyAura("Flame Shock")),
                                Spell.Cast("Fire Nova"),
                                Spell.Cast("Chain Lightning", ret => StyxWoW.Me.HasAura("Maelstrom Weapon", 5)),
                                Spell.Cast("Chain Lightning", ret => Spell.GetSpellCooldown("Fire Nova").Seconds > 1 && Spell.GetSpellCooldown("Unleash Elements").Seconds > 1),
                                Spell.Cast("Stormstrike")
                                //Movement.CreateMoveToMeleeBehavior(true)
                                )),
                        Spell.BuffSelf("Searing Totem", ret => !Totems.Exist(WoWTotemType.Fire) && Me.CurrentTarget.Distance < Totems.GetTotemRange(WoWTotem.Searing) - 2f),
                        //Spell.Cast("Elemental Blast"),
                        Spell.Cast("Elemental Mastery", ret => StyxWoW.Me.CurrentTarget.IsBoss || StyxWoW.Me.CurrentTarget.IsPlayer),
                        Spell.Cast("Elemental Blast"),
                        Spell.Cast("Unleash Elements"),
                        Spell.Cast("Flame Shock", ret => !Me.CurrentTarget.HasMyAura("Flame Shock")),
                        Spell.Cast("Stormstrike"),                       
                        //Lua.DoString("RunMacroText(\"/Cast Stormblast\")"),
                        Spell.Cast("Lightning Bolt", ret => StyxWoW.Me.HasAura("Maelstrom Weapon", 5)),
                        Spell.Cast("Lava Lash", ret => StyxWoW.Me.HasAura("Searing Flames", 5)),
                        //Spell.Cast("Primal Strike", ret => !SpellManager.HasSpell("Stormstrike")),
                        RunMacroText("/cast Stormblast", ret => Me.HasAura(128201)),
                        //Spell.Cast("Unleash Elements"),
                        Spell.Cast("Flame Shock", ret => StyxWoW.Me.HasAura("Unleash Flame")),
                        Spell.Cast("Earth Shock", ret => Spell.GetSpellCooldown("Unleash Elements").Seconds > 4),
                        //Spell.Cast("Lava Lash"),
                        Spell.Cast("Lightning Bolt", ret => Spell.GetSpellCooldown("Stormstrike").Seconds > 1 && Spell.GetSpellCooldown("Unleash Elements").Seconds > 1)
                    ))

                //Movement.CreateMoveToMeleeBehavior(true)
                );
        }
        public static string RealLuaEscape(string luastring)
        {
            var bytes = Encoding.UTF8.GetBytes(luastring);
            return bytes.Aggregate(String.Empty, (current, b) => current + ("\\" + b));
        }

        public static Composite RunMacroText(string macro, CanRunDecoratorDelegate cond)
        {
            return new Decorator(
                       cond,

                //new PrioritySelector(
                       new Sequence(
                           new Action(a => Styx.WoWInternals.Lua.DoString("RunMacroText(\"" + RealLuaEscape(macro) + "\")"))
                           //new Action(a => Logger.DebugLog("Running Macro Text: {0}", macro))
                               )
                           );
        }
        #endregion

        #region Diagnostics

        private static Composite CreateEnhanceDiagnosticOutputBehavior()
        {
            return new ThrottlePasses(1,
                new Decorator(
                    ret => SingularSettings.Debug,
                    new Action(ret =>
                    {
                        uint lstks = 0;
                        WoWAura aura = Me.ActiveAuras.Where( a => a.Key == "Maelstrom Weapon").Select( d => d.Value ).FirstOrDefault();
                        if (aura != null)
                        {
                            lstks = aura.StackCount;
                            if (lstks == 0)
                                Logger.WriteDebug(Color.MediumVioletRed, "Inconsistancy Error:  Maelstrom Weapon buff exists with 0 stacks !!!!");
                            else if ( !Me.HasAura("Maelstrom Weapon", (int)lstks))
                                Logger.WriteDebug(Color.MediumVioletRed, "Inconsistancy Error:  Me.HasAura('Maelstrom Weapon', {0}) was False!!!!", lstks );
                        }

                        string line = string.Format(".... h={0:F1}%/m={1:F1}%, maelstrom={2}",
                            Me.HealthPercent,
                            Me.ManaPercent,
                            lstks
                            );

                        WoWUnit target = Me.CurrentTarget;
                        if (target == null)
                            line += ", target=(null)";
                        else
                            line += string.Format(", target={0} @ {1:F1} yds, th={2:F1}%, tmelee={3}, tloss={4}", 
                                target.SafeName(), 
                                target.Distance, 
                                target.HealthPercent,
                                target.IsWithinMeleeRange, 
                                target.InLineOfSpellSight );

                        Logger.WriteDebug(line);
                        return RunStatus.Failure;
                    }))
                );
        }

        #endregion
    }
}
