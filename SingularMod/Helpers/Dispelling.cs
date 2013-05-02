﻿
using System;
using System.Linq;
using Singular.Managers;
using Styx;

using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Styx.CommonBot;
using Styx.TreeSharp;
using Singular.Settings;

using Action = Styx.TreeSharp.Action;
using Rest = Singular.Helpers.Rest;
using CommonBehaviors.Actions;

namespace Singular.Helpers
{
    /// <summary>Bitfield of flags for specifying DispelCapabilities.</summary>
    /// <remarks>Created 5/3/2011.</remarks>
    [Flags]
    public enum DispelCapabilities
    {
        None = 0,
        Curse = 1,
        Disease = 2,
        Poison = 4,
        Magic = 8,
        All = Curse | Disease | Poison | Magic
    }

    internal static class Dispelling
    {
        private static DispelCapabilities _cachedCapabilities = DispelCapabilities.None;

        public static void Init()
        {
            SingularRoutine.OnWoWContextChanged += (orig, ne) =>
            {
                _cachedCapabilities = Capabilities;
            };
        }

        /// <summary>Gets the dispel capabilities of the current player.</summary>
        /// <value>The capabilities.</value>
        public static DispelCapabilities Capabilities
        {
            get
            {
                DispelCapabilities ret = DispelCapabilities.None;
                if (CanDispelCurse)
                {
                    ret |= DispelCapabilities.Curse;
                }
                if (CanDispelMagic)
                {
                    ret |= DispelCapabilities.Magic;
                }
                if (CanDispelPoison)
                {
                    ret |= DispelCapabilities.Poison;
                }
                if (CanDispelDisease)
                {
                    ret |= DispelCapabilities.Disease;
                }

                return ret;
            }
        }

        /// <summary>Gets a value indicating whether we can dispel diseases.</summary>
        /// <value>true if we can dispel diseases, false if not.</value>
        public static bool CanDispelDisease
        {
            get
            {
                switch (StyxWoW.Me.Class)
                {
                    case WoWClass.Paladin:
                        return true;
					case WoWClass.Monk:
                        return true;
                    case WoWClass.Priest:
                        return true;
                }
                return false;
            }
        }

        /// <summary>Gets a value indicating whether we can dispel poison.</summary>
        /// <value>true if we can dispel poison, false if not.</value>
        public static bool CanDispelPoison
        {
            get
            {
                switch (StyxWoW.Me.Class)
                {
                    case WoWClass.Druid:
						return true;
                    case WoWClass.Paladin:
                        return true;
                    case WoWClass.Monk:
                        return true;
                }
                return false;
            }
        }

        /// <summary>Gets a value indicating whether we can dispel curses.</summary>
        /// <value>true if we can dispel curses, false if not.</value>
        public static bool CanDispelCurse
        {
            get
            {
                switch (StyxWoW.Me.Class)
                {
                    case WoWClass.Druid:
                        return true;
                    case WoWClass.Shaman:
                        return true;
                    case WoWClass.Mage:
                        return true;
                }
                return false;
            }
        }

        /// <summary>Gets a value indicating whether we can dispel magic.</summary>
        /// <value>true if we can dispel magic, false if not.</value>
        public static bool CanDispelMagic
        {
            get
            {
                switch (StyxWoW.Me.Class)
                {
                    case WoWClass.Druid:
                        return StyxWoW.Me.Specialization == WoWSpec.DruidRestoration;
                    case WoWClass.Paladin:
                        return StyxWoW.Me.Specialization == WoWSpec.PaladinHoly;
                    case WoWClass.Shaman:
                        return true;
                    case WoWClass.Priest:
                        return true;
                    case WoWClass.Monk: 
                        return StyxWoW.Me.Specialization == WoWSpec.MonkMistweaver;
                }
                return false;
            }
        }

        /// <summary>Gets a dispellable types on unit. </summary>
        /// <remarks>Created 5/3/2011.</remarks>
        /// <param name="unit">The unit.</param>
        /// <returns>The dispellable types on unit.</returns>
        public static DispelCapabilities GetDispellableTypesOnUnit(WoWUnit unit)
        {
            DispelCapabilities ret = DispelCapabilities.None;
            foreach(var debuff in unit.Debuffs.Values)
            {
                if (SingularSettings.CleanseBlacklist.ContainsKey(debuff.SpellId))
                    return DispelCapabilities.None;

                switch (debuff.Spell.DispelType)
                {
                    case WoWDispelType.Magic:
                        ret |= DispelCapabilities.Magic;
                        break;
                    case WoWDispelType.Curse:
                        ret |= DispelCapabilities.Curse;
                        break;
                    case WoWDispelType.Disease:
                        ret |= DispelCapabilities.Disease;
                        break;
                    case WoWDispelType.Poison:
                        ret |= DispelCapabilities.Poison;
                        break;
                }
            }
            return ret;
        }

        /// <summary>Queries if we can dispel unit 'unit'. </summary>
        /// <remarks>Created 5/3/2011.</remarks>
        /// <param name="unit">The unit.</param>
        /// <returns>true if it succeeds, false if it fails.</returns>
        public static bool CanDispel(WoWUnit unit)
        {
            return CanDispel(unit, _cachedCapabilities);
        }

        public static bool CanDispel(WoWUnit unit, DispelCapabilities chk)
        {
            return (chk & GetDispellableTypesOnUnit(unit)) != 0;
        }


        private static WoWUnit _unitDispel;




        public static Composite CreateDispelBehavior()
        {
            if (SingularSettings.Instance.DispelDebuffs == DispelStyle.None)
                return new ActionAlwaysFail();

            PrioritySelector prio = new PrioritySelector();
            switch ( StyxWoW.Me.Class)
            {
                case WoWClass.Paladin:
                    prio.AddChild( Spell.Cast( "Cleanse", on => _unitDispel));
                    break;
				case WoWClass.Monk:
                    prio.AddChild( Spell.Cast( "Detox", on => _unitDispel));
                    break;
                case WoWClass.Priest:
                    if ( StyxWoW.Me.Specialization == WoWSpec.PriestHoly || StyxWoW.Me.Specialization == WoWSpec.PriestDiscipline )
                        prio.AddChild( Spell.Cast( "Purify", on => _unitDispel));
                    break;
                case WoWClass.Druid:
                    if ( StyxWoW.Me.Specialization == WoWSpec.DruidRestoration )
                        prio.AddChild( Spell.Cast( "Nature's Cure", on => _unitDispel));
                    else 
                        prio.AddChild( Spell.Cast( "Remove Corruption", on => _unitDispel));
                    break;
                case WoWClass.Shaman:
                    if ( StyxWoW.Me.Specialization == WoWSpec.ShamanRestoration )
                        prio.AddChild(Spell.Cast("Purify Spirit", on => _unitDispel));
                    else
                        prio.AddChild(Spell.Cast("Cleanse Spirit", on => _unitDispel));
                    break;
                case WoWClass.Mage:
                    prio.AddChild(Spell.Cast("Remove Curse", on => _unitDispel));
                    break;
            }

            return new Sequence(
                new Action(r => _unitDispel = HealerManager.Instance.HealList.FirstOrDefault(u => u.IsAlive && CanDispel(u))),
                prio
                );
        }
    }
}