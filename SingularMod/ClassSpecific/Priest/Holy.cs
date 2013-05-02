﻿#define FIND_LOWEST_AT_THE_MOMENT

using System;
using System.Linq;
using Singular.Dynamics;
using Singular.Helpers;
using Singular.Managers;
using Singular.Settings;
using Styx;

using Styx.CommonBot;
using Styx.Helpers;
using Styx.WoWInternals.WoWObjects;
using Styx.TreeSharp;
using Styx.WoWInternals.World;
using Action = Styx.TreeSharp.Action;
using Rest = Singular.Helpers.Rest;
using Styx.WoWInternals;
using CommonBehaviors.Actions;
using System.Collections.Generic;
using System.Drawing;

namespace Singular.ClassSpecific.Priest
{
    public class Holy
    {
        private static LocalPlayer Me { get { return StyxWoW.Me; } }
        private static PriestSettings PriestSettings { get { return SingularSettings.Instance.Priest(); } }
        public static bool HasTalent(PriestTalents tal) { return TalentManager.IsSelected((int)tal); }

        
        [Behavior(BehaviorType.Rest, WoWClass.Priest, WoWSpec.PriestHoly)]
        public static Composite CreateHolyRest()
        {
            return new PrioritySelector(
                CreateHolyDiagnosticOutputBehavior("REST"),

                // Heal self before resting. There is no need to eat while we have 100% mana
                CreateHolyHealOnlyBehavior(true, false),
                // Rest up damnit! Do this first, so we make sure we're fully rested.
                Rest.CreateDefaultRestBehaviour( SingularRoutine.CurrentWoWContext == WoWContext.Normal ? "Flash Heal" : null, "Resurrection"),
                // Make sure we're healing OOC too!
                CreateHolyHealOnlyBehavior(false, false),
                // now buff our movement if possible
                Common.CreatePriestMovementBuff("Rest")
                );
        }

        private static WoWUnit _lightwellTarget = null;

        public static Composite CreateHolyHealOnlyBehavior(bool selfOnly, bool moveInRange)
        {
            return CreateRestoPriestHealingOnlyBehavior(selfOnly, moveInRange);
/*
            HealerManager.NeedHealTargeting = true;
            WoWUnit healTarget = null;
            return new
                PrioritySelector(
                ret => healTarget = selfOnly ? StyxWoW.Me : HealerManager.Instance.FirstUnit,
                    new Decorator(
                        ret => healTarget != null && (moveInRange || healTarget.InLineOfSpellSight && healTarget.DistanceSqr < 40 * 40),
                        new PrioritySelector(
                        Spell.WaitForCast(),
                        new Decorator(
                            ret => moveInRange,
                            Movement.CreateMoveToLosBehavior(ret => healTarget)),
                // use fade to drop aggro.
                        Spell.Cast("Fade", ret => (StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid) && StyxWoW.Me.CurrentMap.IsInstance && Targeting.GetAggroOnMeWithin(StyxWoW.Me.Location, 30) > 0),

                        Spell.Cast("Mindbender", ret => StyxWoW.Me.ManaPercent <= 80 && StyxWoW.Me.GotTarget),
                        Spell.Cast("Shadowfiend", ret => StyxWoW.Me.ManaPercent <= 80 && StyxWoW.Me.GotTarget),

                        Spell.BuffSelf("Desperate Prayer", ret => StyxWoW.Me.HealthPercent <= 50),
                        Spell.BuffSelf("Hymn of Hope", ret => StyxWoW.Me.ManaPercent <= 15 && (!SpellManager.HasSpell("Shadowfiend") || SpellManager.Spells["Shadowfiend"].Cooldown)),
                        Spell.BuffSelf("Divine Hymn", ret => Unit.NearbyFriendlyPlayers.Count(p => p.HealthPercent <= SingularSettings.Instance.Priest().DivineHymnHealth) >= SingularSettings.Instance.Priest().DivineHymnCount),

                        Spell.Cast(
                            "Prayer of Mending",
                            ret => healTarget,
                            ret => ret is WoWPlayer && Group.Tanks.Contains((WoWPlayer)ret) && !((WoWUnit)ret).HasMyAura("Prayer of Mending", 3) &&
                                   Group.Tanks.Where(t => t != healTarget).All(p => !p.HasMyAura("Prayer of Mending"))),
                        Spell.Cast(
                            "Renew",
                            ret => healTarget,
                            ret => healTarget is WoWPlayer && Group.Tanks.Contains(healTarget) && !healTarget.HasMyAura("Renew")),
                        Spell.Cast("Prayer of Healing",
                            ret => healTarget,
                            ret => StyxWoW.Me.HasAura("Serendipity", 2) && Unit.NearbyFriendlyPlayers.Count(p => p.HealthPercent <= SingularSettings.Instance.Priest().PrayerOfHealingSerendipityHealth) >= SingularSettings.Instance.Priest().PrayerOfHealingSerendipityCount),
                        Spell.Cast("Circle of Healing",
                            ret => healTarget,
                            ret => Unit.NearbyFriendlyPlayers.Count(p => p.HealthPercent <= SingularSettings.Instance.Priest().CircleOfHealingHealth) >= SingularSettings.Instance.Priest().CircleOfHealingCount),
                        Spell.CastOnGround(
                            "Holy Word: Sanctuary",
                            ret => Clusters.GetBestUnitForCluster(Unit.NearbyFriendlyPlayers.Select(p => p.ToUnit()), ClusterType.Radius, 10f).Location,
                            ret => Clusters.GetClusterCount(healTarget,
                                                            Unit.NearbyFriendlyPlayers.Select(p => p.ToUnit()),
                                                            ClusterType.Radius, 10f) >= 4 ),
                        Spell.Cast(
                            "Holy Word: Serenity",
                            ret => healTarget,
                            ret => ret is WoWPlayer && Group.Tanks.Contains(healTarget)),

                        Spell.Buff("Guardian Spirit",
                            ret => healTarget,
                            ret => healTarget.HealthPercent <= 10),

                        Spell.CastOnGround("Lightwell",ret => WoWMathHelper.CalculatePointFrom(StyxWoW.Me.Location, healTarget.Location, 5f)),

                        Spell.Cast("Power Infusion", ret => healTarget.HealthPercent < 40 || StyxWoW.Me.ManaPercent <= 20),
                        Spell.Cast(
                            "Flash Heal",
                            ret => healTarget,
                            ret => StyxWoW.Me.HasAura("Surge of Light") && healTarget.HealthPercent <= 90),
                        Spell.Cast(
                            "Flash Heal",
                            ret => healTarget,
                            ret => healTarget.HealthPercent < SingularSettings.Instance.Priest().HolyFlashHeal),
                        Spell.Cast(
                            "Greater Heal",
                            ret => healTarget,
                            ret => healTarget.HealthPercent < SingularSettings.Instance.Priest().HolyGreaterHeal),
                        Spell.Cast(
                            "Heal",
                            ret => healTarget,
                            ret => healTarget.HealthPercent < SingularSettings.Instance.Priest().HolyHealPercent ),
                        new Decorator(
                            ret => moveInRange,
                            Movement.CreateMoveToTargetBehavior(true, 35f, ret => healTarget))

                        // Divine Hymn
                // Desperate Prayer
                // Prayer of Mending
                // Prayer of Healing
                // Power Word: Barrier
                // TODO: Add smite healing. Only if Atonement is talented. (Its useless otherwise)
                        )));
 */
        }

        private static WoWUnit _moveToHealTarget = null;
        private static WoWUnit _lastMoveToTarget = null;

        // temporary lol name ... will revise after testing
        public static Composite CreateRestoPriestHealingOnlyBehavior(bool selfOnly, bool moveInRange)
        {
            if (SingularRoutine.CurrentWoWContext == WoWContext.Normal)
                return new ActionAlwaysFail();

            HealerManager.NeedHealTargeting = true;
            PrioritizedBehaviorList behavs = new PrioritizedBehaviorList();
            int cancelHeal = (int)Math.Max(SingularSettings.Instance.IgnoreHealTargetsAboveHealth, Math.Max(PriestSettings.HolyHeal.Heal, Math.Max(PriestSettings.HolyHeal.GreaterHeal, PriestSettings.HolyHeal.Renew )));

            Logger.WriteFile("Priest Healing: will cancel cast of direct heal if health reaches {0:F1}%", cancelHeal);

            if (SingularSettings.Instance.DispelDebuffs != DispelStyle.None)
            {
                int dispelPriority = (SingularSettings.Instance.DispelDebuffs == DispelStyle.HighPriority) ? 999 : -999;
                behavs.AddBehavior(dispelPriority, "Dispel", null, Common.CreatePriestDispelBehavior());
            }

            #region Save the Group

            if (PriestSettings.HolyHeal.DivineHymn != 0)
                behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.DivineHymn) + 300, "Divine Hymn @ " + PriestSettings.HolyHeal.DivineHymn + "% MinCount: " + PriestSettings.HolyHeal.CountDivineHymn, "Divine Hymn",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Divine Hymn", PriestSettings.HolyHeal.DivineHymn, 0, 40, PriestSettings.HolyHeal.CountDivineHymn),
                        new Decorator(
                            ret => ret != null,
                            Spell.Cast("Divine Hymn", mov => false, on => (WoWUnit)on, req => true, cancel => false)
                            )
                        )
                    )
                );

            if (PriestSettings.HolyHeal.VoidShift  != 0 )
            behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.VoidShift) + 300, "Void Shift @ " + PriestSettings.HolyHeal.VoidShift + "%", "Void Shift",
                Spell.Cast("Void Shift",
                    mov => false,
                    on => (WoWUnit)on,
                    req => ((WoWUnit)req).IsPlayer && ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.VoidShift
                    )
                );

            if (PriestSettings.HolyHeal.GuardianSpirit  != 0 )
            behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.GuardianSpirit) + 300, "Guardian Spirit @ " + PriestSettings.HolyHeal.GuardianSpirit + "%", "Guardian Spirit",
                Spell.Cast("Guardian Spirit",
                    mov => false,
                    on => (WoWUnit)on,
                    req => ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.GuardianSpirit
                    )
                );


            #endregion

            #region Tank Buffing

            if (PriestSettings.HolyHeal.PowerWordShield  != 0 )
            behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.PowerWordShield) + 200, "Tank - Power Word: Shield @ " + PriestSettings.HolyHeal.PowerWordShield + "%", "Power Word: Shield",
                Spell.Cast("Power Word: Shield", on =>
                    {
                        WoWUnit unit = HealerManager.GetBestTankTargetForPWS(PriestSettings.HolyHeal.PowerWordShield);
                        if (unit != null && Spell.CanCastHack("Power Word: Shield", unit))
                        {
                            Logger.WriteDebug("Buffing Power Word: Shield ON TANK: {0}", unit.SafeName());
                            return unit;
                        }
                        return null;
                    })
                );

            #endregion

            #region AoE Heals

            int maxDirectHeal = Math.Max(PriestSettings.HolyHeal.FlashHeal, Math.Max(PriestSettings.HolyHeal.Heal, PriestSettings.HolyHeal.GreaterHeal));

            if (maxDirectHeal  != 0 )
            behavs.AddBehavior(399, "Divine Insight - Prayer of Mending @ " + maxDirectHeal.ToString() + "%", "Prayer of Mending",
                new Decorator(
                    ret => (StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid) && Me.HasAura("Divine Insight"),
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Prayer of Mending", maxDirectHeal, 40, 20, 2),
                        new Decorator(
                            ret => ret != null,
                            Spell.Cast("Prayer of Mending", on => (WoWUnit)on, req => true)
                            )
                        )
                    )
                );

            // instant, so cast this first ALWAYS
            if (PriestSettings.HolyHeal.HolyWordSanctuary  != 0 )
            behavs.AddBehavior(398, "Holy Word: Sanctuary @ " + PriestSettings.HolyHeal.HolyWordSanctuary + "% MinCount: " + PriestSettings.HolyHeal.CountHolyWordSanctuary, "Chakra: Sanctuary",
                new Decorator(
                    ret => Me.HasAura("Chakra: Sanctuary"),
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Holy Word: Sanctuary", PriestSettings.HolyHeal.HolyWordSanctuary, 40, 8, PriestSettings.HolyHeal.CountHolyWordSanctuary),
                        new Decorator(
                            ret => ret != null,
                            Spell.CastOnGround("Holy Word: Sanctuary", on => (WoWUnit)on, req => true, false)
                            )
                        )
                    )
                );
/*
            behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.HolyLevel90Talent) + 200, "Divine Star", "Divine Star",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new Decorator(
                        ret => Clusters.GetClusterCount( Me, HealerManager.Instance.HealList.Where(u => u.HealthPercent < PriestSettings.HolyHeal.CountLevel90Talent).ToList(), ClusterType.Path, 4 ) >= PriestSettings.HolyHeal.CountLevel90Talent,
                        Spell.Cast("Divine Star", on => (WoWUnit)on, req => true)
                        )
                    )
                );
*/
            if (PriestSettings.HolyHeal.HolyLevel90Talent  != 0 )
            behavs.AddBehavior(397, "Halo @ " + PriestSettings.HolyHeal.HolyLevel90Talent + "% MinCount: " + PriestSettings.HolyHeal.CountLevel90Talent, "Halo",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new Decorator(
                        ret => ret != null
                            && HealerManager.Instance.HealList.Count(u => u.IsAlive && u.HealthPercent < PriestSettings.HolyHeal.HolyLevel90Talent && u.Distance < 30) >= PriestSettings.HolyHeal.CountLevel90Talent,
                        Spell.CastOnGround("Halo", on => (WoWUnit)on, req => true)
                        )
                    )
                );

            if (PriestSettings.HolyHeal.HolyLevel90Talent  != 0 )
            behavs.AddBehavior(397, "Cascade @ " + PriestSettings.HolyHeal.HolyLevel90Talent + "% MinCount: " + PriestSettings.HolyHeal.CountLevel90Talent, "Cascade",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Cascade", PriestSettings.HolyHeal.HolyLevel90Talent, 40, 30, PriestSettings.HolyHeal.CountLevel90Talent),
                        new Decorator(
                            ret => ret != null,
                            Spell.Cast("Cascade", on => (WoWUnit)on, req => true)
                            )
                        )
                    )
                );

            if (PriestSettings.HolyHeal.PrayerOfMending  != 0 )
            behavs.AddBehavior(397, "Prayer of Mending @ " + PriestSettings.HolyHeal.PrayerOfMending + "% MinCount: " + PriestSettings.HolyHeal.CountPrayerOfMending, "Cascade",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Prayer of Mending", PriestSettings.HolyHeal.PrayerOfMending, 40, 20, PriestSettings.HolyHeal.CountPrayerOfMending),
                        new Decorator(
                            ret => ret != null,
                            Spell.Cast("Prayer of Mending", on => (WoWUnit)on, req => true)
                            )
                        )
                    )
                );

            if ( PriestSettings.HolyHeal.BindingHeal != 0 )
            {
                if (!TalentManager.HasGlyph("Binding Heal"))
                {
                    behavs.AddBehavior(396, "Binding Heal @ " + PriestSettings.HolyHeal.BindingHeal + "% MinCount: 2", "Binding Heal",
                        Spell.Cast("Binding Heal",
                            mov => true,
                            on => (WoWUnit)on,
                            req => !((WoWUnit)req).IsMe && ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.BindingHeal && Me.HealthPercent < PriestSettings.HolyHeal.BindingHeal,
                            cancel => ((WoWUnit)cancel).HealthPercent > cancelHeal
                            )
                        );
                }
                else
                {
                    behavs.AddBehavior(396, "Binding Heal (glyphed) @ " + PriestSettings.HolyHeal.BindingHeal + "% MinCount: 3", "Binding Heal",
                        Spell.Cast("Binding Heal",
                            mov => true,
                            on => (WoWUnit)on,
                            req => !((WoWUnit)req).IsMe 
                                && ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.BindingHeal 
                                && Me.HealthPercent < PriestSettings.HolyHeal.BindingHeal
                                && HealerManager.Instance.HealList.Any( h => h.IsAlive && !h.IsMe && h.Guid != ((WoWUnit)req).Guid && h.HealthPercent < PriestSettings.HolyHeal.BindingHeal && h.SpellDistance() < 20),
                            cancel => ((WoWUnit)cancel).HealthPercent > cancelHeal
                            )
                        );
                }
            }

            if (PriestSettings.HolyHeal.CircleOfHealing  != 0 )
            behavs.AddBehavior(395, "Circle of Healing @ " + PriestSettings.HolyHeal.CircleOfHealing + "% MinCount: " + PriestSettings.HolyHeal.CountCircleOfHealing, "Circle of Healing",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Circle of Healing", PriestSettings.HolyHeal.CircleOfHealing, 40, 30, PriestSettings.HolyHeal.CountCircleOfHealing),
                        Spell.Cast("Circle of Healing", on => (WoWUnit)on)
                        )
                    )
                );

            if ( PriestSettings.HolyHeal.PrayerOfHealing != 0 )
            behavs.AddBehavior(394, "Prayer of Healing @ " + PriestSettings.HolyHeal.PrayerOfHealing + "% MinCount: " + PriestSettings.HolyHeal.CountPrayerOfHealing, "Prayer of Healing",
                new Decorator(
                    ret => StyxWoW.Me.GroupInfo.IsInParty || StyxWoW.Me.GroupInfo.IsInRaid,
                    new PrioritySelector(
                        context => GetBestCoverageTarget("Prayer of Healing", PriestSettings.HolyHeal.PrayerOfHealing, 40, 30, PriestSettings.HolyHeal.CountPrayerOfHealing),
                        Spell.Cast("Prayer of Healing", on => (WoWUnit)on)
                        )
                    )
                );

            #endregion

            /*          
HolyWordSanctuary       Holy Word: Sanctuary 
HolyWordSerenity        Holy Word: Serenity 
CircleOfHealing         Circle of Healing 
PrayerOfHealing         Prayer of Healing 
DivineHymn              Divine Hymn 
GuardianSpirit          Guardian Spirit 
VoidShift               Void Shift 
*/

#region Direct Heals

            if (PriestSettings.HolyHeal.HolyWordSerenity != 0)
            behavs.AddBehavior(HealthToPriority(1) + 4, "Holy Word: Serenity @ " + PriestSettings.HolyHeal.HolyWordSerenity + "%", "Chakra: Serenity",
                Spell.CastHack("Holy Word: Serenity",
                    on => (WoWUnit)on,
                    req => Me.HasAura("Chakra: Serenity") && ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.HolyWordSerenity
                    )
                );

            if (maxDirectHeal != 0)
                behavs.AddBehavior(HealthToPriority(1) + 3, "Surge of Light @ " + maxDirectHeal + "%", "Flash Heal",
                Spell.Cast("Flash Heal",
                    mov => true,
                    on => (WoWUnit)on,
                    req => Me.HasAura("Surge of Light") && ((WoWUnit)req).HealthPercent < maxDirectHeal
                    )
                );

            if (PriestSettings.HolyHeal.FlashHeal != 0)
                behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.FlashHeal), "Flash Heal @ " + PriestSettings.HolyHeal.FlashHeal + "%", "Flash Heal",
                Spell.Cast("Flash Heal",
                    mov => true,
                    on => (WoWUnit)on,
                    req => ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.FlashHeal,
                    cancel => ((WoWUnit)cancel).HealthPercent > cancelHeal
                    )
                );

            if (PriestSettings.HolyHeal.GreaterHeal != 0)
                behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.GreaterHeal), "Greater Heal @ " + PriestSettings.HolyHeal.GreaterHeal + "%", "Greater Heal",
                Spell.Cast( "Greater Heal",
                    mov => true,
                    on => (WoWUnit)on,
                    req => ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.GreaterHeal,
                    cancel => ((WoWUnit)cancel).HealthPercent > cancelHeal
                    )
                );

            if (PriestSettings.HolyHeal.Heal != 0)
                behavs.AddBehavior(HealthToPriority(PriestSettings.HolyHeal.Heal), "Heal @ " + PriestSettings.HolyHeal.Heal + "%", "Heal",
                Spell.Cast("Heal",
                    mov => true,
                    on => (WoWUnit)on,
                    req => ((WoWUnit)req).HealthPercent < PriestSettings.HolyHeal.Heal,
                    cancel => ((WoWUnit)cancel).HealthPercent > cancelHeal
                    )
                );

#endregion

#region Tank - Low Priority Buffs

            behavs.AddBehavior(HealthToPriority(101), "Tank - Refresh Renew w/ Serenity", "Chakra: Serenity",
                Spell.CastHack("Holy Word: Serenity", on =>
                    {
                        if (Me.HasAura("Chakra: Serenity"))
                        {
                            WoWUnit unit = Group.Tanks
                                .Where(u => u.IsAlive && u.Combat && u.DistanceSqr < 40 * 40 && u.GetAuraTimeLeft("Renew").TotalMilliseconds.Between(350, 2500) && u.InLineOfSpellSight)
                                .OrderBy(u => u.HealthPercent)
                                .FirstOrDefault();

                            if (unit != null && Spell.CanCastHack("Renew", unit))
                            {
                                Logger.WriteDebug("Refreshing RENEW ON TANK: {0}", unit.SafeName());
                                return unit;
                            }
                        }
                        return null;
                    },
                    req => true)
                );

            if (PriestSettings.HolyHeal.Renew != 0)
            behavs.AddBehavior(HealthToPriority(102), "Tank - Buff Renew @ " + PriestSettings.HolyHeal.Renew + "%", "Renew",
                // roll Renew on Tanks 
                Spell.Cast("Renew", on =>
                    {
                        WoWUnit unit = HealerManager.GetBestTankTargetForHOT("Renew", PriestSettings.HolyHeal.Renew);
                        if (unit != null && Spell.CanCastHack("Renew", unit))
                        {
                            Logger.WriteDebug("Buffing RENEW ON TANK: {0}", unit.SafeName());
                            return unit;
                        }
                        return null;
                    })
                );
#endregion


#region Lowest Priority Healer Tasks

            if (SingularRoutine.CurrentWoWContext == WoWContext.Instances)
            {
                behavs.AddBehavior(HealthToPriority(103), "Lightwell", "Lightwell",
                    new Throttle(TimeSpan.FromSeconds(20),
                        new Sequence(
                            new Action(r =>
                            {
                                _lightwellTarget = Group.Tanks.FirstOrDefault(t =>
                                {
                                    if (t.IsAlive && t.Combat && t.GotTarget && t.CurrentTarget.IsBoss())
                                    {
                                        if (t.Distance < 40 && t.SpellDistance(t.CurrentTarget) < 10)
                                        {
                                            return true;
                                        }
                                    }
                                    return false;
                                });
                            }),
                            Spell.CastOnGround("Lightwell",
                                location => WoWMathHelper.CalculatePointFrom(Me.Location, _lightwellTarget.Location, (float)Math.Min(10.0, _lightwellTarget.Distance / 2)),
                                req => _lightwellTarget != null,
                                false,
                                desc => string.Format("10 yds from {0}", _lightwellTarget.SafeName())
                                )
                            )
                        )
                    );
            }
#endregion

            behavs.OrderBehaviors();

            if ( selfOnly == false && Singular.Dynamics.CompositeBuilder.CurrentBehaviorType == BehaviorType.Combat)
                behavs.ListBehaviors();

            return new PrioritySelector(
                ctx => selfOnly ? StyxWoW.Me : FindLowestHealthTarget(), // HealerManager.Instance.FirstUnit,

                Spell.WaitForCastOrChannel(),

                new Decorator(
                    ret => moveInRange,
                    new PrioritySelector(
                        new Action(r =>
                        {
                            _moveToHealTarget = RaFHelper.Leader ?? Group.Tanks.Where(t => t.IsAlive).OrderBy(t => t.Distance).FirstOrDefault();
                            if (_moveToHealTarget == null)
                                _moveToHealTarget = (WoWUnit)r;

                            if (_lastMoveToTarget != _moveToHealTarget)
                            {
                                if ( _moveToHealTarget == null )
                                    Logger.WriteDebug(Color.White, "MoveToHealTarget:  no current heal movement target");
                                else
                                    Logger.WriteDebug(Color.White, "MoveToHealTarget:  in range of {0} @ {1:F1} yds", _moveToHealTarget.SafeName(), _moveToHealTarget.Distance);

                                _lastMoveToTarget = _moveToHealTarget;
                            }

                            return RunStatus.Failure;
                        }),

                        Movement.CreateMoveToLosBehavior(moveTo => _moveToHealTarget),
                        Movement.CreateEnsureMovementStoppedBehavior(30f, on => _moveToHealTarget)
                        )
                    ),

                new Decorator(
                    ret => !Spell.IsGlobalCooldown() && ret != null,
                    behavs.GenerateBehaviorTree()
                    ),

                new Decorator(
                    ret => moveInRange,
                    new Sequence(
                        new Action( r => {
                            if (_moveToHealTarget != null && _moveToHealTarget.IsValid && _moveToHealTarget.Distance > 35)
                                Logger.Write(Color.White, "heal target {0} is {1:F1} yds away", _moveToHealTarget.SafeName(), _moveToHealTarget.Distance);
                            }),
                        // Movement.CreateMoveToRangeAndStopBehavior(ret => _moveToHealTarget, ret => 35f)
                        Movement.CreateMoveToTargetBehavior( true, 35f, ret => _moveToHealTarget )
                        )
                    )
                );
        }

        private static int HealthToPriority(int nHealth)
        {
            return nHealth == 0 ? 0 : 200 - nHealth;
        }


        private static WoWUnit FindLowestHealthTarget()
        {
#if FIND_LOWEST_AT_THE_MOMENT
            double minHealth = 1000;
            WoWUnit minUnit = null;

            foreach (WoWUnit unit in HealerManager.Instance.HealList)
            {
                if (unit.HealthPercent < minHealth)
                {
                    minHealth = unit.HealthPercent;
                    minUnit = unit;
                }
            }

            return minUnit;
#else
            return HealerManager.Instance.FirstUnit;
#endif
        }

        [Behavior(BehaviorType.Heal, WoWClass.Priest, WoWSpec.PriestHoly)]
        public static Composite CreateHolyHeal()
        {
            return new Decorator(
                ret => !Unit.NearbyGroupMembers.Any(m => m.IsAlive && !m.IsMe),
                new PrioritySelector(
                    Spell.Cast("Desperate Prayer", ret => Me, ret => Me.Combat && Me.HealthPercent < PriestSettings.DesperatePrayerHealth),
                    Spell.BuffSelf("Power Word: Shield", ret => Me.Combat && Me.HealthPercent < PriestSettings.ShieldHealthPercent && !Me.HasAura("Weakened Soul")),

                    // keep heal buffs on if glyphed
                    Spell.BuffSelf("Prayer of Mending", ret => Me.Combat && Me.HealthPercent <= 90),
                    Spell.BuffSelf("Renew", ret => Me.Combat && Me.HealthPercent <= 90),

                    Spell.Cast("Psychic Scream", ret => Me.Combat
                        && PriestSettings.UsePsychicScream
                        && Me.HealthPercent <= PriestSettings.ShadowFlashHealHealth
                        && (Unit.NearbyUnfriendlyUnits.Count(u => u.DistanceSqr < 10 * 10) >= PriestSettings.PsychicScreamAddCount || (SingularRoutine.CurrentWoWContext == WoWContext.Battlegrounds && Unit.NearbyUnfriendlyUnits.Any(u => Me.SpellDistance(u) < 8 )))),

                    Spell.Cast("Flash Heal",
                        ctx => Me,
                        ret => Me.HealthPercent <= PriestSettings.ShadowFlashHealHealth),

                    Spell.Cast("Flash Heal",
                        ctx => Me,
                        ret => !Me.Combat && Me.GetPredictedHealthPercent(true) <= 85)
                    )
                );
        }

        [Behavior(BehaviorType.CombatBuffs, WoWClass.Priest, WoWSpec.PriestHoly)]
        public static Composite CreateHolyCombatBuffs()
        {
            return new PrioritySelector(
                new PrioritySelector(
                    ctx => {
                        if (SingularRoutine.CurrentWoWContext == WoWContext.Battlegrounds)
                            return "Chakra: Serenity";

                        int groupSize = Unit.NearbyGroupMembers.Count(m => m.IsAlive);
                        if (groupSize >= 8)
                            return "Chakra: Sanctuary";
                        if (groupSize <= 6)
                            return "Chakra: Serenity";
                        return "Chakra: Chastise";
                    },

                    Spell.Cast( spell=>(string)spell, on => Me, req => !Me.HasAura((string) req))
                    ),

                Spell.Cast("Fade", ret => SingularRoutine.CurrentWoWContext == WoWContext.Instances && Targeting.GetAggroOnMeWithin(StyxWoW.Me.Location, 30) > 0),

                Spell.BuffSelf("Desperate Prayer", ret => StyxWoW.Me.HealthPercent <= PriestSettings.DesperatePrayerHealth),

                Common.CreateShadowfiendBehavior(),

                Spell.Cast(
                    "Hymn of Hope", 
                    on => Me,
                    ret => StyxWoW.Me.ManaPercent <= PriestSettings.HymnofHopeMana && Spell.GetSpellCooldown("Shadowfiend").TotalMilliseconds > 0,
                    cancel => false),

                Spell.Cast("Power Infusion", ret => StyxWoW.Me.ManaPercent <= 75 || HealerManager.Instance.HealList.Any( h => h.HealthPercent < 40)),

                // Spell.Cast("Power Word: Solace", req => Me.GotTarget && Unit.ValidUnit(Me.CurrentTarget) && Me.IsSafelyFacing( Me.CurrentTarget) && Me.CurrentTarget.InLineOfSpellSight )
                // Spell.Cast(129250, req => Me.GotTarget && Unit.ValidUnit(Me.CurrentTarget) && Me.IsSafelyFacing(Me.CurrentTarget) && Me.CurrentTarget.InLineOfSpellSight),
                Spell.CastHack("Power Word: Solace", req => Me.GotTarget && Unit.ValidUnit(Me.CurrentTarget) && Me.IsSafelyFacing(Me.CurrentTarget) && Me.CurrentTarget.InLineOfSpellSight)
                );
        }

        // This behavior is used in combat/heal AND pull. Just so we're always healing our party.
        // Note: This will probably break shit if we're solo, but oh well!
        [Behavior(BehaviorType.Combat | BehaviorType.Pull, WoWClass.Priest, WoWSpec.PriestHoly)]
        public static Composite CreateHolyCombat()
        {
            return new PrioritySelector(

                CreateHolyDiagnosticOutputBehavior("COMBAT"),

                new Decorator(
                    ret => Unit.NearbyGroupMembers.Any(m => m.IsAlive && !m.IsMe),
                    CreateHolyHealOnlyBehavior(false, true)
                    ),

                new Decorator(
                    ret => Me.Combat && !Unit.NearbyGroupMembers.Any(m => m.IsAlive && !m.IsMe),
                    new PrioritySelector(
                        Safers.EnsureTarget(),
                        Movement.CreateMoveToLosBehavior(),
                        Movement.CreateFaceTargetBehavior(),
                        Movement.CreateEnsureMovementStoppedBehavior(33),
                        Spell.WaitForCastOrChannel(),

                        new Decorator( 
                            ret => !Spell.IsGlobalCooldown(),
                            new PrioritySelector(

                                Helpers.Common.CreateDismount("Pulling"),
                                Helpers.Common.CreateInterruptBehavior(),
                                Spell.Cast("Shadow Word: Death", ret => StyxWoW.Me.CurrentTarget.HealthPercent <= 20),
                                Spell.Buff("Shadow Word: Pain", true),
                                Spell.Buff("Holy Word: Chastise", ret => StyxWoW.Me.HasAura( "Chakra: Chastise")),
                                Spell.Cast("Holy Fire"),
                                // Spell.Cast("Power Word: Solace", ret => StyxWoW.Me.ManaPercent < 15),
                                Spell.Cast("Smite")
                                )
                            ),

                        Movement.CreateMoveToTargetBehavior(true, 38)
                        )
                    )
                );
        }

        private static WoWUnit GetBestCoverageTarget(string spell, int health, int range, int radius, int minCount)
        {
            if (!Me.IsInGroup() || !Me.Combat)
                return null;

            if (!Spell.CanCastHack(spell, Me))
            {
                if ( !SingularSettings.Instance.EnableDebugLoggingCanCast )
                    Logger.WriteDebug("GetBestCoverageTarget: CanCastHack says NO to [{0}]", spell);
                return null;
            }

            // build temp list of targets that could use heal and are in range + radius
            List<WoWUnit> coveredTargets = HealerManager.Instance.HealList
                .Where(u => u.IsAlive && u.Distance < (range+radius) && u.HealthPercent < health)
                .ToList();


            // create a iEnumerable of the possible heal targets wtihin range
            IEnumerable<WoWUnit> listOf;
            if (range == 0)
                listOf = new List<WoWUnit>() { Me };
            else
                listOf = Unit.NearbyGroupMembersAndPets.Where(p => p.Distance <= range && p.IsAlive);

            // now search list finding target with greatest number of heal targets in radius
            var t = listOf
                .Select(p => new
                {
                    Player = p,
                    Count = coveredTargets
                        .Where(pp => pp.IsAlive && pp.Location.Distance(p.Location) < radius)
                        .Count()
                })
                .OrderByDescending(v => v.Count)
                .DefaultIfEmpty(null)
                .FirstOrDefault();

            if (t != null)
            {
                if (t.Count >= minCount)
                {
                    Logger.WriteDebug("GetBestCoverageTarget('{0}'): found {1} with {2} nearby under {3}%", spell, t.Player.SafeName(), t.Count, health);
                    return t.Player;
                }

                if (SingularSettings.Instance.EnableDebugLoggingCanCast)
                {
                    Logger.WriteDebug("GetBestCoverageTarget('{0}'): not enough found - {1} with {2} nearby under {3}%", spell, t.Player.SafeName(), t.Count, health);
                }
            }

            return null;
        }

        #region Diagnostics

        private static DateTime _LastDiag = DateTime.MinValue;

        private static Composite CreateHolyDiagnosticOutputBehavior( string context)
        {
            return new Sequence(
                new Action( r => {
                    double sinceLast = _LastDiag == DateTime.MinValue ? -1 : (DateTime.Now - _LastDiag).TotalSeconds;
                    _LastDiag = DateTime.Now;

                    if (sinceLast > 3 && Me.IsInGroup())
                        Logger.Write(Color.Violet, "Warning: {0:F1} seconds since BotBase called Singular to check healing", sinceLast);
                }),
                new Decorator(
                    ret => SingularSettings.Debug,
                    new ThrottlePasses(1, 1,
                        new Action(ret =>
                        {
                            WoWAura chakra = Me.GetAllAuras().Where(a => a.Name.Contains("Chakra")).FirstOrDefault();

                            string line = string.Format(".... [{0}] h={1:F1}%/m={2:F1}%, combat={3}, {4}, surge={5}, serendip={6}",
                                context,
                                Me.HealthPercent,
                                Me.ManaPercent,
                                Me.Combat.ToYN(),
                                chakra == null ? "(null)" : chakra.Name,
                                (long)Me.GetAuraTimeLeft("Surge of Light").TotalMilliseconds,
                                (long)Me.GetAuraTimeLeft("Serendipity").TotalMilliseconds
                               );

                            if (HealerManager.Instance == null || HealerManager.Instance.FirstUnit == null || !HealerManager.Instance.FirstUnit.IsValid )
                                line += ", target=(null)";
                            else
                            {
                                WoWUnit healtarget = HealerManager.Instance.FirstUnit;
                                line += string.Format(", target={0} th={1:F1}%/{2:F1}%,  @ {3:F1} yds, combat={4}, tloss={5}, pw:s={6}, renew={7}",
                                    healtarget.SafeName(),
                                    healtarget.HealthPercent,
                                    healtarget.GetPredictedHealthPercent(true),
                                    healtarget.Distance,
                                    healtarget.Combat.ToYN(),
                                    healtarget.InLineOfSpellSight,
                                    (long)healtarget.GetAuraTimeLeft("Power Word: Shield").TotalMilliseconds,
                                    (long)healtarget.GetAuraTimeLeft("Renew").TotalMilliseconds
                                    );
                            }

                            Logger.WriteDebug(Color.LightGreen, line);
                            return RunStatus.Failure;
                        }))
                    )
                );
        }

        #endregion
    }
}