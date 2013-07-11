﻿using System.Linq;
using System.Threading;
using CommonBehaviors.Actions;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Styx;

using Styx.CommonBot;
using Styx.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.TreeSharp;
using Action = Styx.TreeSharp.Action;
using Rest = Singular.Helpers.Rest;
using Singular.Settings;
using System;
using Styx.CommonBot.POI;
using System.Collections.Generic;

namespace Singular.ClassSpecific.Rogue
{
    public class Common
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static RogueSettings RogueSettings { get { return SingularSettings.Instance.Rogue(); } }
        private static bool HasTalent(RogueTalents tal) { return TalentManager.IsSelected((int)tal); } 

        public static bool IsStealthed { get { return Me.HasAnyAura("Stealth", "Shadow Dance", "Vanish"); } }

        [Behavior(BehaviorType.Rest, WoWClass.Rogue)]
        public static Composite CreateRogueRest()
        {
            return new PrioritySelector(
                Spell.WaitForCastOrChannel(),
                new Decorator(
                    ret => !Spell.IsGlobalCooldown(),
                    new PrioritySelector(
                        CreateStealthBehavior( ret => StyxWoW.Me.HasAura("Food")),
                        Rest.CreateDefaultRestBehaviour( ),

                        CreateRogueOpenBoxes(),
                        CreateRoguePreCombatBuffs(),
                        CreateRogueGeneralMovementBuff("Rest")
                        )
                    )
                );
        }

        [Behavior(BehaviorType.Heal, WoWClass.Rogue, (WoWSpec) int.MaxValue, WoWContext.Normal|WoWContext.Battlegrounds)]
        public static Composite CreateRogueHeal()
        {
            return new Decorator(
                ret => !Spell.IsCastingOrChannelling() & !Spell.IsGlobalCooldown() && !IsStealthed && !Group.AnyHealerNearby,
                new PrioritySelector(
                    Movement.CreateFaceTargetBehavior(),
                    new Decorator(
                        ret => SingularSettings.Instance.UseBandages
                            && StyxWoW.Me.HealthPercent < 20
                            && !Unit.NearbyUnfriendlyUnits.Any(u => u.Combat && u.Guid != StyxWoW.Me.CurrentTargetGuid && u.CurrentTargetGuid == StyxWoW.Me.Guid)
                            && Item.HasBandage(),
                        new Sequence(
                            new PrioritySelector(
                                new Decorator(
                                    ret => Me.IsMoving && !MovementManager.IsMovementDisabled,
                                    new Action(ret => { Navigator.PlayerMover.MoveStop(); return RunStatus.Failure; })
                                    ),
                                new Wait(TimeSpan.FromMilliseconds(500), ret => !Me.IsMoving, new ActionAlwaysFail()),
                                new Decorator(
                                    ret => !Unit.NearbyUnfriendlyUnits.Any(u => !u.IsCrowdControlled()),
                                    new ActionAlwaysSucceed()
                                    ),
                                Spell.Cast("Gouge"),
                                Spell.Cast("Blind")
                                ),
                            Helpers.Common.CreateWaitForLagDuration(),
                            new WaitContinue(TimeSpan.FromMilliseconds(250), ret => Spell.IsGlobalCooldown(), new ActionAlwaysSucceed()),
                            new WaitContinue(TimeSpan.FromMilliseconds(1500), ret => !Spell.IsGlobalCooldown(), new ActionAlwaysSucceed()),
                            Item.CreateUseBandageBehavior()
                            )
                        ),

                    new Decorator(
                        ret => RogueSettings.RecuperateHealth > 0 && Me.RawComboPoints > 0,
                        new PrioritySelector(
                // cast regardless of combo points if we are below health level
                            Spell.BuffSelf("Recuperate", ret => Me.HealthPercent < RogueSettings.RecuperateHealth),

                            // cast at higher health level based upon number of attackers
                            Spell.BuffSelf("Recuperate",
                                ret => AoeCount > 0
                                    && Me.RawComboPoints >= Math.Min(AoeCount, 3)
                                    && Me.HealthPercent < (100 * (AoeCount - 1) + RogueSettings.RecuperateHealth) / AoeCount),

                            // cast if partially need healing and mob about to die
                            Spell.BuffSelf("Recuperate",
                                ret => Me.GotTarget
                                    && AoeCount == 1
                                    && Me.CurrentTarget.TimeToDeath() < 2
                                    && Me.HealthPercent < (100 + RogueSettings.RecuperateHealth) / 2)
                            )
                        )
                    )
                );

        }

        [Behavior(BehaviorType.PreCombatBuffs, WoWClass.Rogue)]
        public static Composite CreateRoguePreCombatBuffs()
        {
            return new PrioritySelector(
                // new Action(r => { Logger.WriteDebug("PreCombatBuffs -- stealthed={0}", Stealthed); return RunStatus.Failure; }),
                CreateApplyPoisons(),

                // don't waste the combo points if we have them
                Spell.Cast("Recuperate", 
                    on => Me,
                    ret => StyxWoW.Me.RawComboPoints > 0 
                        && (!SpellManager.HasSpell( "Redirect") || Spell.IsSpellOnCooldown("Redirect"))
                        && Me.HasAuraExpired("Recuperate", 3 + Me.RawComboPoints * 6))
                );
        }

        [Behavior(BehaviorType.PullBuffs, WoWClass.Rogue, (WoWSpec)int.MaxValue, WoWContext.All)]
        public static Composite CreateRoguePullBuffs()
        {
            return new PrioritySelector(
                // new Action( r => { Logger.WriteDebug("PullBuffs -- stealthed={0}", Stealthed ); return RunStatus.Failure; } ),
                CreateStealthBehavior( ret => !IsStealthed && Me.GotTarget && Me.CurrentTarget.Distance < ( Me.CurrentTarget.IsNeutral ? 8 : 99 )),
                Spell.Cast("Redirect", on => Me.CurrentTarget, ret => StyxWoW.Me.RawComboPoints > 0 && Me.ComboPointsTarget != Me.CurrentTargetGuid ),
                Spell.BuffSelf("Recuperate", ret => StyxWoW.Me.RawComboPoints > 0 && (!SpellManager.HasSpell("Redirect") || !SpellManager.CanCast("Redirect"))),
                // Throttle Shadowstep because cast can fail with no message
                new Throttle( 2, Spell.Cast("Shadowstep", ret => MovementManager.IsClassMovementAllowed && StyxWoW.Me.GotTarget && StyxWoW.Me.CurrentTarget.Distance > 12)),
                Spell.BuffSelf("Sprint", ret => MovementManager.IsClassMovementAllowed && StyxWoW.Me.IsMoving && StyxWoW.Me.HasAura("Stealth") && StyxWoW.Me.GotTarget && StyxWoW.Me.CurrentTarget.Distance > 15 && (!SpellManager.HasSpell("Shadowstep") || !SpellManager.CanCast("Shadowstep", true)))
                );

        }

        [Behavior(BehaviorType.CombatBuffs, WoWClass.Rogue, (WoWSpec)int.MaxValue, WoWContext.All)]
        public static Composite CreateRogueCombatBuffs()
        {
            return new PrioritySelector(

                Movement.CreateFaceTargetBehavior(),

                CreateActionCalcAoeCount(),

                // Defensive
                // Spell.BuffSelf("Combat Readiness", ret => AoeCount > 2 && !Me.HasAura("Feint")),

                // Symbiosis
                new Throttle(179, Spell.BuffSelf("Growl", ret => Me.HealthPercent < 65 && SingularRoutine.CurrentWoWContext != WoWContext.Instances)),

                // Spell.BuffSelf("Feint", ret => AoeCount > 2 && !Me.HasAura("Combat Readiness") && HaveTalent(RogueTalents.Elusivenss)),
                Spell.BuffSelf("Evasion", ret => Unit.NearbyUnfriendlyUnits.Count(u => u.DistanceSqr < 6 * 6 && u.IsTargetingMeOrPet) >= 2),
                Spell.BuffSelf("Cloak of Shadows", ret => Unit.NearbyUnfriendlyUnits.Count(u => u.IsTargetingMeOrPet && u.IsCasting) >= 1),
                Spell.BuffSelf("Smoke Bomb", ret => StyxWoW.Me.HealthPercent < 40 && Unit.NearbyUnfriendlyUnits.Count(u => u.DistanceSqr > 4 * 4 && u.IsAlive && u.Combat && u.IsTargetingMeOrPet) >= 1),
                Spell.BuffSelf("Vanish", ret => StyxWoW.Me.HealthPercent < 20 && !SingularRoutine.IsQuestBotActive),

                Spell.BuffSelf("Preparation",
                    ret => Spell.GetSpellCooldown("Vanish").TotalSeconds > 10
                        && Spell.GetSpellCooldown("Evasion").TotalSeconds > 10),

                Spell.Cast("Shiv", ret => Me.CurrentTarget.HasAura("Enraged")),

                Common.CreateRogueBlindOnAddBehavior(),

                // Redirect if we have CP left
                Spell.Cast("Redirect", ret => StyxWoW.Me.RawComboPoints > 0 && StyxWoW.Me.ComboPoints < 1),

                Spell.Cast("Marked for Death", ret => StyxWoW.Me.RawComboPoints == 0),

                Spell.Cast("Deadly Throw",
                    ret => Me.ComboPoints >= 3
                        && Me.GotTarget
                        && Me.CurrentTarget.IsCasting
                        && Me.CurrentTarget.CanInterruptCurrentSpellCast),

                // Pursuit
                Spell.Cast("Shadowstep", ret => MovementManager.IsClassMovementAllowed && Me.CurrentTarget.Distance > 12 && Unit.CurrentTargetIsMovingAwayFromMe),
                Spell.Cast("Burst of Speed", ret => MovementManager.IsClassMovementAllowed && Me.IsMoving && Me.CurrentTarget.Distance > 10 && Unit.CurrentTargetIsMovingAwayFromMe),

                // Vanish to boost DPS if behind target, not stealthed, have slice/dice, and 0/1 combo pts
                new Sequence(
                    Spell.BuffSelf("Vanish",
                        ret => Me.GotTarget
                            && !SingularRoutine.IsQuestBotActive
                            && !IsStealthed
                            && !Me.HasAuraExpired("Slice and Dice", 4)
                            && Me.ComboPoints < 2
                            && Me.IsSafelyBehind(Me.CurrentTarget)),
                    new Wait(TimeSpan.FromMilliseconds(500), ret => IsStealthed, new ActionAlwaysSucceed()),
                    CreateRogueOpenerBehavior()
                    ),

                // DPS Boost               
                new Sequence(
                    new Throttle(TimeSpan.FromSeconds(2),
                        new Decorator(
                            req => UseLongCoolDownAbility,
                            Spell.BuffSelf("Shadow Blades", req =>
                            {
                                switch (Me.Specialization)
                                {
                                    default:
                                    case WoWSpec.RogueAssassination:
                                        return Me.ComboPoints <= 2;

                                    case WoWSpec.RogueCombat:
                                        return Me.HasAura("Adrenaline Rush");

                                    case WoWSpec.RogueSubtlety:
                                        return Me.ComboPoints <= 2 && !Me.HasAura("Find Weakness");
                                }
                            }))
                        ),

                    new ActionAlwaysFail()
                    )
                );

        }

        public static Composite CreateApplyPoisons()
        {
            return new Sequence(
                new PrioritySelector(
                    Spell.BuffSelf(ret => (int)Poisons.NeedLethalPosion(), req => Poisons.NeedLethalPosion() != LethalPoisonType.None ),
                    Spell.BuffSelf(ret => (int)Poisons.NeedNonLethalPosion(), req => Poisons.NeedNonLethalPosion() != NonLethalPoisonType.None )
                    ),
                new Wait(1, ret => Me.IsCasting, new ActionAlwaysSucceed()),
                new Wait(4, ret => !Me.IsCasting, new ActionAlwaysSucceed()),
                Helpers.Common.CreateWaitForLagDuration()
                );
        }

        public static Composite CreateRogueOpenerBehavior()
        {
            return new Decorator(
                ret => Common.IsStealthed,
                new PrioritySelector(
                    CreateRoguePickPocket(),
                    Spell.Cast("Ambush", ret => Me.IsSafelyBehind(Me.CurrentTarget) || Common.HasTalent( RogueTalents.MarkedForDeath ) || Common.HasTalent(RogueTalents.CloakAndDagger)),
                    Spell.Cast("Garrote", ret => !Me.IsMoving && !Me.IsSafelyBehind(Me.CurrentTarget) || Common.HasTalent(RogueTalents.MarkedForDeath)),
                    Spell.Cast("Cheap Shot", ret => !Me.IsMoving )
                    )
                );
        }

        public static Composite CreateRogueBlindOnAddBehavior()
        {
            return new PrioritySelector(
                    ctx => Unit.NearbyUnfriendlyUnits.FirstOrDefault(u =>
                            u.IsTargetingMeOrPet && u != StyxWoW.Me.CurrentTarget),
                    new Decorator(
                        ret => ret != null && !StyxWoW.Me.HasAura("Blade Flurry"),
                        Spell.Buff("Blind", ret => (WoWUnit)ret, ret => Unit.NearbyUnfriendlyUnits.Count(u => u.Aggro) > 1)));
        }

        public static WoWUnit BestTricksTarget
        {
            get
            {
                if (!StyxWoW.Me.GroupInfo.IsInParty && !StyxWoW.Me.GroupInfo.IsInRaid)
                    return null;

                // If the player has a focus target set, use it instead. TODO: Add Me.FocusedUnit to the HB API.
                if (StyxWoW.Me.FocusedUnitGuid != 0)
                    return StyxWoW.Me.FocusedUnit;

                if (StyxWoW.Me.IsInInstance)
                {
                    if (RaFHelper.Leader != null && RaFHelper.Leader.IsValid && !RaFHelper.Leader.IsMe)
                    {
                        // Leader first, always. Otherwise, pick a rogue/DK/War pref. Fall back to others just in case.
                        return RaFHelper.Leader;
                    }

                    if (StyxWoW.Me.GroupInfo.IsInParty)
                    {
                        var bestTank = Group.Tanks.OrderBy(t => t.DistanceSqr).FirstOrDefault(t => t.IsAlive);
                        if (bestTank != null)
                            return bestTank;
                    }

                    var bestPlayer = Group.GetPlayerByClassPrio(100f, false,
                        WoWClass.Rogue, WoWClass.DeathKnight, WoWClass.Warrior,WoWClass.Hunter, WoWClass.Mage, WoWClass.Warlock, WoWClass.Shaman, WoWClass.Druid,
                        WoWClass.Paladin, WoWClass.Priest);
                    return bestPlayer;
                }

                return null;
            }
        }

        public static Decorator CreateRogueGeneralMovementBuff(string mode, bool checkMoving = true)
        {
            return new Decorator(
                ret => RogueSettings.UseSpeedBuff 
                    && MovementManager.IsClassMovementAllowed
                    && StyxWoW.Me.IsAlive
                    && (!checkMoving || StyxWoW.Me.IsMoving)
                    && !StyxWoW.Me.Mounted
                    && !StyxWoW.Me.IsOnTransport
                    && !StyxWoW.Me.OnTaxi
                    && SpellManager.HasSpell("Burst of Speed")
                    && !StyxWoW.Me.HasAnyAura("Burst of Speed")
                    && (BotPoi.Current == null || BotPoi.Current.Type == PoiType.None || BotPoi.Current.Location.Distance(StyxWoW.Me.Location) > 15)
                    && !StyxWoW.Me.IsAboveTheGround(),

                new PrioritySelector(
                    Spell.WaitForCast(),
                    new Decorator(
                        ret => !Spell.IsGlobalCooldown(),
                        new PrioritySelector(
                            Spell.BuffSelf("Burst of Speed")
                            )
                        )
                    )
                );
        }


        public static int AoeCount { get; set; }

        public static Action CreateActionCalcAoeCount()
        {
            return new Action(ret =>
            {
                if (Battlegrounds.IsInsideBattleground || Unit.NearbyUnfriendlyUnits.Any(u => u.Guid != Me.CurrentTargetGuid && u.IsCrowdControlled()))
                    AoeCount = 1;
                else
                    AoeCount = Unit.NearbyUnfriendlyUnits.Count(u => u.Distance < (u.MeleeDistance() + 3));
                return RunStatus.Failure;
            });
        }

        public static bool HaveTalent(RogueTalents rogueTalents)
        {
            return TalentManager.IsSelected((int)rogueTalents);
        }


        internal static Composite CreateAttackFlyingMobs()
        {
            return new Decorator(
                // changed to only do on non-player targets
                ret => !Me.CurrentTarget.IsPlayer && (Me.CurrentTarget.IsFlying || Me.CurrentTarget.IsAboveTheGround() || Me.CurrentTarget.Distance2DSqr < 5 * 5 && Math.Abs(Me.Z - Me.CurrentTarget.Z) >= 5),
                new PrioritySelector(
                    Spell.Cast("Deadly Throw"),
                    Spell.Cast("Throw"),

                    // nothing else worked, so cancel stealth so we can proximity aggro
                    new Decorator(
                        ret => Me.HasAura("Stealth"),
                        new Sequence(
                            new Action(ret => Logger.Write("/cancel Stealth")),
                            new Action(ret => Me.CancelAura("Stealth")),
                            new Wait(TimeSpan.FromMilliseconds(500), ret => !Me.HasAura("Stealth"), new ActionAlwaysSucceed())
                            )
                        )
                    )
                );
        }

        internal static Composite CreateStealthBehavior( SimpleBooleanDelegate req = null)
        {
            return new PrioritySelector(
                new Sequence(
                    Spell.BuffSelf("Stealth", ret => req == null || req(ret)),
                    new Wait( TimeSpan.FromMilliseconds(500), ret => IsStealthed, new ActionAlwaysSucceed())
                    )                
                );
        }

        private static Helpers.Throttle CreateRoguePickPocket()
        {
            return new Throttle(5,
                new Decorator(
                    ret => RogueSettings.UsePickPocket
                        && IsStealthed
                        && Me.GotTarget
                        && Me.CurrentTarget.IsAlive
                        && !Me.CurrentTarget.IsPlayer
                        && (Me.CurrentTarget.IsWithinMeleeRange || (TalentManager.HasGlyph("Pick Pocket") && Me.CurrentTarget.SpellDistance() < 10))
                        && (Me.CurrentTarget.IsHumanoid || Me.CurrentTarget.IsUndead)
                        && AutoLootIsEnabled(),
                    new Sequence(
                        new Action( r => { Navigator.PlayerMover.MoveStop(); } ),
                        new Wait( TimeSpan.FromMilliseconds(500), until => !Me.IsMoving, new ActionAlwaysSucceed()),
                        Spell.Cast("Pick Pocket", on => Me.CurrentTarget),
                        new ActionAlwaysFail() // not on the GCD, so fail
                        )
                    )
                );
        }

        public static Composite CreateRogueFeintBehavior()
        {
            return Spell.Cast("Feint",
                                        ret => Me.CurrentTarget.ThreatInfo.RawPercent > 80
                                            && Me.IsInGroup()
                                            && Group.AnyTankNearby);
        }

        public static bool HasDaggerInMainHand
        {
            get
            {
                return IsDagger( Me.Inventory.Equipped.MainHand );
            }
        }

        public static bool HasDaggerInOffHand
        {
            get
            {
                return IsDagger(Me.Inventory.Equipped.OffHand);
            }
        }

        public static bool HasTwoDaggers
        {
            get
            {
                return IsDagger(Me.Inventory.Equipped.MainHand) && IsDagger(Me.Inventory.Equipped.OffHand);
            }
        }

        public static bool IsDagger( WoWItem hand)
        {
            return hand != null && hand.ItemInfo.IsWeapon && hand.ItemInfo.WeaponClass == WoWItemWeaponClass.Dagger;
        }

        private static WoWItem box;

        public static Composite CreateRogueOpenBoxes()
        {
            return new Decorator(
                ret => RogueSettings.UsePickLock,
                new PrioritySelector(
                    new Decorator( 
                        ret => SpellManager.HasSpell("Pick Lock") 
                            && SpellManager.CanCast("Pick Lock", Me, false, true)
                            && AutoLootIsEnabled(),
                        new Sequence(
                            new Action( r => { box = FindLockedBox();  return box == null ? RunStatus.Failure : RunStatus.Success; }),
                            new Action( r => Logger.Write( "/pick lock on {0} #{1}", box.Name, box.Entry)),
                            new Action( r => { return SpellManager.Cast( "Pick Lock", Me) ? RunStatus.Success : RunStatus.Failure; }),
                            new Action( r => Logger.WriteDebug( "picklock: wait for spell on cursor")),
                            new Wait( 1, ret => Me.CurrentPendingCursorSpell != null, new ActionAlwaysSucceed()),
                            new Action( r => Logger.WriteDebug( "picklock: use item")),
                            new Action( r => box.Use() ),
                            new Action( r => Logger.WriteDebug( "picklock: wait for spell in progress")),
                            new Wait( 1, ret => Spell.IsCastingOrChannelling(), new ActionAlwaysSucceed()),
                            new Action( r => Logger.WriteDebug( "picklock: wait for spell to complete")),
                            new Wait( 6, ret => !Spell.IsCastingOrChannelling(), new ActionAlwaysSucceed()),
                            Helpers.Common.CreateWaitForLagDuration()
                            )
                        ),

                    new Action( r => { box = FindUnlockedBox();  return RunStatus.Failure; }),
                    new Decorator(
                        ret => box != null && AutoLootIsEnabled(),
                        new Sequence(
                            new Action( r => Logger.WriteDebug( "open box - wait for openable")),
                            new Wait(2, ret => box.IsOpenable && !Spell.IsGlobalCooldown() && !Spell.IsCastingOrChannelling(), new Action(r => box.UseContainerItem())),
                            Helpers.Common.CreateWaitForLagDuration()
                            )
                        )
                    )
                );
        }

        private static bool AutoLootIsEnabled()
        {
            List<string> option = Lua.GetReturnValues("return GetCVar(\"AutoLootDefault\")");
            return option != null && !string.IsNullOrEmpty(option[0]) && option[0] == "1";
        }

        internal static bool UseLongCoolDownAbility
        {
            get
            {
                if (!Me.GotTarget || !Me.CurrentTarget.IsWithinMeleeRange )
                    return false;

                if (SingularRoutine.CurrentWoWContext == WoWContext.Instances)
                    return Me.CurrentTarget.IsBoss();

                if (Me.CurrentTarget.IsPlayer && Me.CurrentTarget.TimeToDeath() > 3)
                    return true;

                if (Me.CurrentTarget.TimeToDeath() > 20)
                    return true;

                return Unit.NearbyUnitsInCombatWithMe.Any(u => u.Guid != Me.CurrentTargetGuid && !u.IsPet && u.IsWithinMeleeRange );
            }
        }

        public static WoWItem FindLockedBox()
        {
            return Me.CarriedItems
                .Where(b => b.ItemInfo.ItemClass == WoWItemClass.Miscellaneous
                    && b.ItemInfo.ContainerClass == WoWItemContainerClass.Container
                    && b.ItemInfo.Level <= Me.Level
                    && !b.IsOpenable
                    && b.Usable
                    && b.Cooldown <= 0
                    && _boxes.Contains(b.Entry))
                .FirstOrDefault();
        }

        public static WoWItem FindUnlockedBox()
        {
            return Me.CarriedItems
                .Where(b => b.ItemInfo.ItemClass == WoWItemClass.Miscellaneous
                    && b.ItemInfo.ContainerClass == WoWItemContainerClass.Container
                    && b.IsOpenable
                    && b.Usable
                    && b.Cooldown <= 0
                    && _boxes.Contains(b.Entry))
                .FirstOrDefault();
        }

        private static HashSet<uint> _boxes = new HashSet<uint>()
        {
            4632,	// Ornate Bronze Lockbox
            6354,	// Small Locked Chest
            16882,	// Battered Junkbox
            4633,	// Heavy Bronze Lockbox
            4634,	// Iron Lockbox
            6355,	// Sturdy Locked Chest
            16883,	// Worn Junkbox
            4636,	// Strong Iron Lockbox
            4637,	// Steel Lockbox
            16884,	// Sturdy Junkbox
            4638,	// Reinforced Steel Lockbox
            13875,	// Ironbound Locked Chest
            5758,	// Mithril Lockbox
            5759,	// Thorium Lockbox
            13918,	// Reinforced Locked Chest
            5760,	// Eternium Lockbox
            12033,	// Thaurissan Family Jewels
            29569,	// Strong Junkbox
            31952,	// Khorium Lockbox
            43575,	// Reinforced Junkbox
            43622,	// Froststeel Lockbox
            43624,	// Titanium Lockbox
            45986,	// Tiny Titanium Lockbox
            63349,	// Flame-Scarred Junkbox
            68729,	// Elementium Lockbox
            88567,	// Ghost Iron Lockbox
            88165,	// Vine-Cracked Junkbox
        };

    }


    public enum RogueTalents
    {
        None = 0,
        Nightstalker,
        Subterfuge,
        ShadowFocus,
        DeadlyThrow,
        NerveStrike,
        CombatReadiness,
        CheatDeath,
        LeechingPoison,
        Elusivenss,
        CloakAndDagger,
        Shadowstep,
        BurstOfSpeed,
        PreyOnTheWeak,
        ParalyticPoison,
        DirtyTricks,
        ShurikenToss,
        MarkedForDeath,
        Anticipation
    }
}
