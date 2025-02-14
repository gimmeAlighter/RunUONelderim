using System;
using System.Collections.Generic;
using System.Linq;
using Nelderim.Engines.ChaosChest;
using Server.Regions;
using Server.Targeting;
using Server.Network;
using Server.Multis;
using Server.Spells;
using Server.Misc;
using Server.Items;
using Server.ContextMenus;
using Server.Engines.Quests;
using Server.Factions;
using Server.Spells.Bushido;
using Server.Spells.Spellweaving;
using Server.Nelderim;

namespace Server.Mobiles
{
    #region Enums

    /// <summary>
    /// Summary description for MobileAI.
    /// </summary>
    /// 
    public enum FightMode
    {
        None,                // Never focus on others
        Aggressor,    // Only attack aggressors
        Strongest,    // Attack the strongest
        Weakest,        // Attack the weakest
        Closest,         // Attack the closest
        Evil,                // Only attack aggressor -or- negative karma
        Criminal         // Attack the closest ciminal
    }

    public enum OrderType
    {
        None,            //When no order, let's roam
        Come,            //"(All/Name) come"  Summons all or one pet to your location.  
        Drop,            //"(Name) drop"  Drops its loot to the ground (if it carries any).  
        Follow,            //"(Name) follow"  Follows targeted being.  
                        //"(All/Name) follow me"  Makes all or one pet follow you.  
        Friend,            //"(Name) friend"  Allows targeted player to confirm resurrection. 
        Unfriend,        // Remove a friend
        Guard,            //"(Name) guard"  Makes the specified pet guard you. Pets can only guard their owner. 
                        //"(All/Name) guard me"  Makes all or one pet guard you.  
        Attack,            //"(All/Name) kill", 
                        //"(All/Name) attack"  All or the specified pet(s) currently under your control attack the target. 
        Patrol,            //"(Name) patrol"  Roves between two or more guarded targets.  
        Release,        //"(Name) release"  Releases pet back into the wild (removes "tame" status). 
        Stay,            //"(All/Name) stay" All or the specified pet(s) will stop and stay in current spot. 
        Stop,            //"(All/Name) stop Cancels any current orders to attack, guard or follow.  
        Transfer        //"(Name) transfer" Transfers complete ownership to targeted player. 
    }

    [Flags]
    public enum FoodType
    {
        None            = 0x0000,
        Meat            = 0x0001,
        FruitsAndVegies = 0x0002,
        GrainsAndHay    = 0x0004,
        Fish            = 0x0008,
        Eggs            = 0x0010,
        Gold            = 0x0020,
        Amethyst        = 0x0040,
        Sapphire        = 0x0080,
        StarSapphire    = 0x0100,
        Emerald         = 0x0200,
        Ruby            = 0x0400,
        Diamond         = 0x0800
    }

    [Flags]
    public enum PackInstinct
    {
        None            = 0x0000,
        Canine            = 0x0001,
        Ostard            = 0x0002,
        Feline            = 0x0004,
        Arachnid        = 0x0008,
        Daemon            = 0x0010,
        Bear            = 0x0020,
        Equine            = 0x0040,
        Bull            = 0x0080
    }

    public enum ScaleType
    {
        Red,
        Yellow,
        Black,
        Green,
        White,
        Blue,
        All
    }

    public enum MeatType
    {
        Ribs,
        Bird,
        LambLeg
    }

    public enum HideType
    {
        Regular,
        Spined,
        Horned,
        Barbed
    }

    #endregion

    public class DamageStore : IComparable
    {
        public Mobile m_Mobile;
        public int m_Damage;
        public bool m_HasRight;

        public DamageStore( Mobile m, int damage )
        {
            m_Mobile = m;
            m_Damage = damage;
        }

        public int CompareTo( object obj )
        {
            DamageStore ds = (DamageStore)obj;

            return ds.m_Damage - m_Damage;
        }
    }

    [AttributeUsage( AttributeTargets.Class )]
    public class FriendlyNameAttribute : Attribute
    {
        //future use: Talisman 'Protection/Bonus vs. Specific Creature
        private TextDefinition m_FriendlyName;

        public TextDefinition FriendlyName
        {
            get
            {
                return m_FriendlyName;
            }
        }

        public FriendlyNameAttribute( TextDefinition friendlyName )
        {
            m_FriendlyName = friendlyName;
        }

        public static TextDefinition GetFriendlyNameFor( Type t )
        {
            if( t.IsDefined( typeof( FriendlyNameAttribute ), false ) )
            {
                object[] objs = t.GetCustomAttributes( typeof( FriendlyNameAttribute ), false );

                if( objs != null && objs.Length > 0 )
                {
                    FriendlyNameAttribute friendly = objs[0] as FriendlyNameAttribute;

                    return friendly.FriendlyName;
                }
            }

            return t.Name;
        }
    }

    public class BaseCreature : Mobile, IHonorTarget
    {
        public const int MaxLoyalty = 100;

        #region Var declarations
        private BaseAI    m_AI;                    // THE AI
        
        private AIType    m_CurrentAI;            // The current AI
        private AIType    m_DefaultAI;            // The default AI

        private Mobile    m_FocusMob;                // Use focus mob instead of combatant, maybe we don't whan to fight
        private FightMode m_FightMode;            // The style the mob uses

        private int        m_iRangePerception;        // The view area
        private int        m_iRangeFight;            // The fight distance
       
        private bool    m_bDebugAI;                // Show debug AI messages

        private int        m_iTeam;                // Monster Team

        private double    m_dActiveSpeed;            // Timer speed when active
        private double    m_dPassiveSpeed;        // Timer speed when not active
        private double    m_dCurrentSpeed;        // The current speed, lets say it could be changed by something;

        private Point3D m_pHome;                // The home position of the creature, used by some AI
        private int        m_iRangeHome = 10;        // The home range of the creature

        List<Type>        m_arSpellAttack;        // List of attack spell/power
        List<Type>        m_arSpellDefense;        // List of defensive spell/power

        private bool        m_bControlled;        // Is controlled
        private Mobile        m_ControlMaster;    // My master
        private Mobile        m_ControlTarget;    // My target mobile
        private Point3D        m_ControlDest;        // My target destination (patrol)
        private OrderType    m_ControlOrder;        // My order

        private int            m_Loyalty;

        private double    m_dMinTameSkill;
        private bool    m_bTamable;

        private bool        m_bSummoned = false;
        private DateTime    m_SummonEnd;
        private int            m_iControlSlots = 1;

        private bool        m_bBardProvoked = false;
        private bool        m_bBardPacified = false;
        private Mobile        m_bBardMaster = null;
        private Mobile        m_bBardTarget = null;
        private DateTime    m_timeBardEnd;
        private WayPoint    m_CurrentWayPoint = null;
        private Point2D        m_TargetLocation = Point2D.Zero;

        private Mobile        m_SummonMaster;

        private int            m_HitsMax = -1;
        private    int            m_StamMax = -1;
        private int            m_ManaMax = -1;
        private int            m_DamageMin = -1;
        private int            m_DamageMax = -1;

        private int            m_PhysicalResistance, m_PhysicalDamage = 100;
        private int            m_FireResistance, m_FireDamage;
        private int            m_ColdResistance, m_ColdDamage;
        private int            m_PoisonResistance, m_PoisonDamage;
        private int            m_EnergyResistance, m_EnergyDamage;

        private List<Mobile> m_Owners;
        private List<Mobile> m_Friends;

        private bool        m_IsStabled;

        private bool        m_HasGeneratedLoot; // have we generated our loot yet?

        private bool        m_Paragon;

        // dla zlecen mysliwskich
        private bool m_IsChampionSpawn;

        // 09.10.2012 :: zombie
        private double m_Difficulty;
        // zombie

        // 20.08.2012 :: zombie
        private bool m_DeleteCorpseOnDeath;
        // zombie

        // 18.07.2012 :: zombie
        private Dictionary<WeaponAbility, double> m_WeaponAbilities;
        // zombie

        private DateTime m_MutedUntil;
        #endregion

        // 25.06.2012 :: zombie :: szansa na zmiane celu
        #region Target Switching
        [CommandProperty( AccessLevel.Counselor )]
        public virtual double SwitchTargetChance { get { return 0.05; } }
        #endregion
        // zombie 

        // 25.06.2012 :: zombie :: szansa na zaatakowanie controlmastera obecnego przeciwnika
        #region Attack Master
        [CommandProperty( AccessLevel.Counselor )]
        public virtual double AttackMasterChance { get { return 0.05; } }
        #endregion
        // zombie

        #region Shrink
        public virtual bool OnShrink( Mobile from )
        {
            return true;
        }
        
        public virtual bool OnUnshrink( Mobile from )
        {
            return true;
        }
        
        #endregion

        // 25.06.2012 :: zombie :: system plotek
        #region Rumors
        public virtual void AnnounceRandomRumor( PriorityLevel level )
        {
            // Console.WriteLine( "public virtual void AnnounceRandomRumor( PriorityLevel level )" );

            try
            {
                List<RumorRecord> RumorsList = RumorsSystem.GetRumors( this, level );

                if ( RumorsList == null || RumorsList.Count == 0 )
                    return;

                // Console.WriteLine( "RumorsList.Count = {0} | PriorityLevel = {1}", RumorsList.Count, (int) level );

                int sum = 0;

                foreach ( RumorRecord r in RumorsList )
                    sum += (int)r.Priority;

                int index = Utility.Random( sum );
                double chance = ( (double)sum ) / ( 4.0 * ( (int)level ) );

                // Console.WriteLine( "sum = {0} | index = {1} | chance = {2}", sum, index, chance );

                sum = 0;
                RumorRecord rumor = null;

                foreach ( RumorRecord r in RumorsList )
                {
                    sum += (int)r.Priority;

                    if ( sum > index )
                    {
                        rumor = r;
                        break;
                    }
                }

                // Console.WriteLine( rumor.Coppice );

                if ( Utility.RandomDouble() < chance )
                    Say( rumor.Coppice );
            }
            catch ( Exception exc )
            {
                Console.WriteLine( exc.ToString() );
            }

        }

        public virtual double GetRumorsActionPropability()
        {
            // Console.WriteLine( "public virtual double GetRumorsActionPropability()" );

            try
            {
                double chance = 0.00;
                List<RumorRecord> RumorsList = RumorsSystem.GetRumors( this, PriorityLevel.Low );

                // Console.WriteLine( "RumorsList.Count = {0}", RumorsList.Count );

                if ( RumorsList == null || RumorsList.Count == 0 )
                    return 0.00;

                int sum = 0;

                foreach ( RumorRecord r in RumorsList )
                    sum += (int)r.Priority;

                chance = ( (double)sum ) / 320.0;

                // Console.WriteLine( "sum = {0} | chance = {1}", sum, chance );

                return ( chance > 1 ) ? 1.00 : chance;

            }
            catch ( Exception exc )
            {
                Console.WriteLine( exc.ToString() );
            }

            return 0.00;
        }

        public bool Activation( Mobile target )
        {
            return ( Utility.RandomDouble() < Math.Pow( this.GetDistanceToSqrt( target ), -2 ) );
        }

        #endregion
        // zombie

        public virtual bool IgnoreHonor { get { return false; } }

        private bool m_IsPrisoner;

        public virtual InhumanSpeech SpeechType{ get{ return null; } }

        public bool IsStabled
        {
            get{ return m_IsStabled; }
            set
            { 
                m_IsStabled = value;

                StopDeleteTimer();
            }
        }

        [CommandProperty(AccessLevel.GameMaster)]
        public bool IsMuted
        {
            get { return DateTime.Now < m_MutedUntil; }
            set
            {
                if (value)
                    m_MutedUntil = DateTime.Now.AddHours(3); // ensure silence for the next default amount of time
                else
                    m_MutedUntil = DateTime.Now;
            }
        }

		[CommandProperty(AccessLevel.GameMaster)]
		public DateTime IsMutedUntil
        {
            get { return m_MutedUntil; }
        }

[CommandProperty( AccessLevel.GameMaster )]
        public bool IsPrisoner
        {
            get { return m_IsPrisoner; }
            set { m_IsPrisoner = value; }
        }

        protected DateTime SummonEnd
        {
            get { return m_SummonEnd; }
            set { m_SummonEnd = value; }
        }

        public virtual Faction FactionAllegiance{ get{ return null; } }
        public virtual int FactionSilverWorth{ get{ return 30; } }

        #region Bonding
        public const bool BondingEnabled = true;

        public virtual bool IsNecromancer { get { return ( Skills[ SkillName.Necromancy ].Value > 50 ); } }

        public virtual bool IsBondable{ get{ return ( BondingEnabled && !Summoned && !m_Allured); } }
        public virtual TimeSpan BondingDelay{ get{ return TimeSpan.FromDays( 7.0 ); } }
        public virtual TimeSpan BondingAbandonDelay{ get{ return TimeSpan.FromDays( 1.0 ); } }

        public override bool CanRegenHits{ get{ return !m_IsDeadPet && base.CanRegenHits; } }
        public override bool CanRegenStam{ get{ return !m_IsDeadPet && base.CanRegenStam; } }
        public override bool CanRegenMana{ get{ return !m_IsDeadPet && base.CanRegenMana; } }

        public override bool IsDeadBondedPet{ get{ return m_IsDeadPet; } }

        private bool m_IsBonded;
        private bool m_IsDeadPet;
        private DateTime m_BondingBegin;
        private DateTime m_OwnerAbandonTime;

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile LastOwner
        {
            get
            {
                if ( m_Owners == null || m_Owners.Count == 0 )
                    return null;

                return m_Owners[m_Owners.Count - 1];
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public bool IsBonded
        {
            get{ return m_IsBonded; }
            set{ m_IsBonded = value; InvalidateProperties(); }
        }

        public bool IsDeadPet
        {
            get{ return m_IsDeadPet; }
            set{ m_IsDeadPet = value; }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public DateTime BondingBegin
        {
            get{ return m_BondingBegin; }
            set{ m_BondingBegin = value; }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public DateTime OwnerAbandonTime
        {
            get{ return m_OwnerAbandonTime; }
            set{ m_OwnerAbandonTime = value; }
        }
        #endregion

        #region Delete Previously Tamed Timer
        private DeleteTimer        m_DeleteTimer;

        [CommandProperty( AccessLevel.GameMaster )]
        public TimeSpan DeleteTimeLeft
        {
            get
            {
                if ( m_DeleteTimer != null && m_DeleteTimer.Running )
                    return m_DeleteTimer.Next - DateTime.Now;

                return TimeSpan.Zero;
            }
        }

        private class DeleteTimer : Timer
        {
            private Mobile m;

            public DeleteTimer( Mobile creature, TimeSpan delay ) : base( delay )
            {
                m = creature;
                Priority = TimerPriority.OneMinute;
            }

            protected override void OnTick()
            {
                if (m is BaseCreature && ((BaseCreature)m).Controlled)
                    return;

                Console.WriteLine("USUWAM PETA (BaseCreature.m_DeleteTimer.OnTick), Serial({0}), Name({1})", m.Serial.ToString(), m.Name);
                m.Delete();
            }
        }

        public void BeginDeleteTimer()
        {
            if ( !(this is BaseEscortable) && !Summoned && !Deleted && !IsStabled )
            {
                StopDeleteTimer();
                m_DeleteTimer = new DeleteTimer( this, TimeSpan.FromDays( 3.0 ) );
                m_DeleteTimer.Start();
            }
        }

        public void StopDeleteTimer()
        {
            if ( m_DeleteTimer != null )
            {
                m_DeleteTimer.Stop();
                m_DeleteTimer = null;
            }
        }

        #endregion
  
        // 18.07.2012 :: zombie
        public virtual void AddWeaponAbilities()
        {
        }

        public virtual Dictionary<WeaponAbility, double> WeaponAbilities
        {
            get { return m_WeaponAbilities; }
        }

        // Funckja tworzy kopie dictionary o losowej kolejnosci wzgledem oryginalu
        IEnumerable<KeyValuePair<TKey, TValue>> RandomValues<TKey, TValue>(IDictionary<TKey, TValue> dict)
        {
            int size = dict.Count;
            if (size == 0)
                yield break;

            Random rand = new Random();
            List<TKey> keys = Enumerable.ToList(dict.Keys);
            List<TValue> values = Enumerable.ToList(dict.Values);
            
            int randIt;
            while (true)
            {
                randIt = rand.Next(size);
                yield return (new KeyValuePair<TKey,TValue>(keys[randIt], values[randIt]));
            }
        }

        public WeaponAbility GetWeaponAbility()
        {
            try
            {
                double rand = Utility.RandomDouble();

                foreach (KeyValuePair<WeaponAbility, double> kvp in RandomValues(WeaponAbilities))
                {
                    if ( rand < kvp.Value )
                        return kvp.Key;
                    else
                        rand -= kvp.Value;
                }
            }
            catch( Exception e )
            {
                Console.WriteLine( "GetWeaponAbility: {0}", e.ToString() );
            }

            return null;
        }
        // zombie

        #region Elemental Resistance/Damage

        public override int BasePhysicalResistance{ get{ return m_PhysicalResistance; } }
        public override int BaseFireResistance{ get{ return m_FireResistance; } }
        public override int BaseColdResistance{ get{ return m_ColdResistance; } }
        public override int BasePoisonResistance{ get{ return m_PoisonResistance; } }
        public override int BaseEnergyResistance{ get{ return m_EnergyResistance; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int PhysicalResistanceSeed{ get{ return m_PhysicalResistance; } set{ m_PhysicalResistance = value; UpdateResistances(); } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int FireResistSeed{ get{ return m_FireResistance; } set{ m_FireResistance = value; UpdateResistances(); } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int ColdResistSeed{ get{ return m_ColdResistance; } set{ m_ColdResistance = value; UpdateResistances(); } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int PoisonResistSeed{ get{ return m_PoisonResistance; } set{ m_PoisonResistance = value; UpdateResistances(); } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int EnergyResistSeed{ get{ return m_EnergyResistance; } set{ m_EnergyResistance = value; UpdateResistances(); } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int PhysicalDamage{ get{ return m_PhysicalDamage; } set{ m_PhysicalDamage = value; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int FireDamage{ get{ return m_FireDamage; } set{ m_FireDamage = value; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int ColdDamage{ get{ return m_ColdDamage; } set{ m_ColdDamage = value; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int PoisonDamage{ get{ return m_PoisonDamage; } set{ m_PoisonDamage = value; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public int EnergyDamage{ get{ return m_EnergyDamage; } set{ m_EnergyDamage = value; } }

        #endregion

        [CommandProperty( AccessLevel.GameMaster )]
        public bool IsParagon
        {
            get{ return m_Paragon; }
            set
            {
                if ( m_Paragon == value )
                    return;
                else if ( value )
                    Paragon.Convert( this );
                else
                    Paragon.UnConvert( this );

                m_Paragon = value;

                InvalidateProperties();
            }
        }

        public bool IsChampionSpawn
        {
            get { return m_IsChampionSpawn; }
            set { m_IsChampionSpawn = value; }
        }

        // 20.08.2012 :: zombie
        [CommandProperty( AccessLevel.GameMaster )]
        public virtual bool DeleteCorpseOnDeath
        { 
            get { return m_DeleteCorpseOnDeath; }
            set { m_DeleteCorpseOnDeath = value; } 
        }
        // zombie

        public virtual FoodType FavoriteFood{ get{ return FoodType.Meat; } }
        public virtual PackInstinct PackInstinct{ get{ return PackInstinct.None; } }

        public List<Mobile> Owners { get { return m_Owners; } }

        public virtual bool AllowMaleTamer{ get{ return true; } }
        public virtual bool AllowFemaleTamer{ get{ return true; } }
        public virtual bool SubdueBeforeTame{ get{ return false; } }
        public virtual bool StatLossAfterTame{ get{ return SubdueBeforeTame; } }

        public virtual bool Commandable{ get{ return true; } }

        public virtual Poison HitPoison{ get{ return null; } }
        public virtual double HitPoisonChance{ get{ return 0.5; } }
        public virtual Poison PoisonImmune{ get{ return null; } }

        public virtual bool BardImmune{ get{ return false; } }
        public virtual bool Unprovokable{ get{ return BardImmune || m_IsDeadPet; } }
        public virtual bool Uncalmable{ get{ return BardImmune || m_IsDeadPet; } }
        public virtual bool AreaPeaceImmune { get { return BardImmune || m_IsDeadPet; } }

        public virtual bool BleedImmune{ get{ return false; } }
        public virtual double BonusPetDamageScalar{ get{ return 1.0; } }
        public virtual bool AllureImmune { get { return false; } }

        public virtual bool DeathAdderCharmable{ get{ return false; } }

        //TODO: Find the pub 31 tweaks to the DispelDifficulty and apply them of course.
        public virtual double DispelDifficulty{ get{ return 0.0; } } // at this skill level we dispel 50% chance
        public virtual double DispelFocus{ get{ return 20.0; } } // at difficulty - focus we have 0%, at difficulty + focus we have 100%

        #region Breath ability, like dragon fire breath
        private DateTime m_NextBreathTime;

        // Must be overriden in subclass to enable
        public virtual bool HasBreath{ get{ return false; } }

        // Base damage given is: CurrentHitPoints * BreathDamageScalar
        public virtual double BreathDamageScalar{ get{ return (Core.AOS ? 0.16 : 0.05); } }

        // Min/max seconds until next breath
        public virtual double BreathMinDelay{ get{ return 10.0; } }
        public virtual double BreathMaxDelay{ get{ return 15.0; } }

        // Creature stops moving for 1.0 seconds while breathing
        public virtual double BreathStallTime{ get{ return 1.0; } }

        // Effect is sent 1.3 seconds after BreathAngerSound and BreathAngerAnimation is played
        public virtual double BreathEffectDelay{ get{ return 1.3; } }

        // Damage is given 1.0 seconds after effect is sent
        public virtual double BreathDamageDelay{ get{ return 1.0; } }

        public virtual int BreathRange{ get{ return RangePerception; } }

        // Damage types
        public virtual int BreathPhysicalDamage{ get{ return 0; } }
        public virtual int BreathFireDamage{ get{ return 100; } }
        public virtual int BreathColdDamage{ get{ return 0; } }
        public virtual int BreathPoisonDamage{ get{ return 0; } }
        public virtual int BreathEnergyDamage{ get{ return 0; } }

        // Effect details and sound
        public virtual int BreathEffectItemID{ get{ return 0x36D4; } }
        public virtual int BreathEffectSpeed{ get{ return 5; } }
        public virtual int BreathEffectDuration{ get{ return 0; } }
        public virtual bool BreathEffectExplodes{ get{ return false; } }
        public virtual bool BreathEffectFixedDir{ get{ return false; } }
        public virtual int BreathEffectHue{ get{ return 0; } }
        public virtual int BreathEffectRenderMode{ get{ return 0; } }

        public virtual int BreathEffectSound{ get{ return 0x227; } }

        // Anger sound/animations
        public virtual int BreathAngerSound{ get{ return GetAngerSound(); } }
        public virtual int BreathAngerAnimation{ get{ return 12; } }

        public virtual void BreathStart( Mobile target )
        {
            BreathStallMovement();
            BreathPlayAngerSound();
            BreathPlayAngerAnimation();

            this.Direction = this.GetDirectionTo( target );

            Timer.DelayCall( TimeSpan.FromSeconds( BreathEffectDelay ), new TimerStateCallback( BreathEffect_Callback ), target );
        }

        public virtual void BreathStallMovement()
        {
            if ( m_AI != null )
                m_AI.NextMove = DateTime.Now + TimeSpan.FromSeconds( BreathStallTime );
        }

        public virtual void BreathPlayAngerSound()
        {
            PlaySound( BreathAngerSound );
        }

        public virtual void BreathPlayAngerAnimation()
        {
            Animate( BreathAngerAnimation, 5, 1, true, false, 0 );
        }

        public virtual void BreathEffect_Callback( object state )
        {
            Mobile target = (Mobile)state;

            if ( !target.Alive || !CanBeHarmful( target ) )
                return;

            BreathPlayEffectSound();
            BreathPlayEffect( target );

            Timer.DelayCall( TimeSpan.FromSeconds( BreathDamageDelay ), new TimerStateCallback( BreathDamage_Callback ), target );
        }

        public virtual void BreathPlayEffectSound()
        {
            PlaySound( BreathEffectSound );
        }

        public virtual void BreathPlayEffect( Mobile target )
        {
            Effects.SendMovingEffect( this, target, BreathEffectItemID,
                BreathEffectSpeed, BreathEffectDuration, BreathEffectFixedDir,
                BreathEffectExplodes, BreathEffectHue, BreathEffectRenderMode );
        }

        public virtual void BreathDamage_Callback( object state )
        {
            Mobile target = (Mobile)state;

            if ( CanBeHarmful( target ) )
            {
                DoHarmful( target );
                BreathDealDamage( target );
            }
        }

        public virtual void BreathDealDamage( Mobile target )
        {
			// 23.09.2013 :: mortuus - Evasion chroni przed oddechem smoka
			if( Evasion.CheckSpellEvasion(target) )
				return;

            int physDamage = BreathPhysicalDamage;
            int fireDamage = BreathFireDamage;
            int coldDamage = BreathColdDamage;
            int poisDamage = BreathPoisonDamage;
            int nrgyDamage = BreathEnergyDamage;

            if ( physDamage == 0 && fireDamage == 0 && coldDamage == 0 && poisDamage == 0 && nrgyDamage == 0 )
            { // Unresistable damage even in AOS
                target.Damage( BreathComputeDamage(), this );
            }
            else
            {
                AOS.Damage( target, this, BreathComputeDamage(), physDamage, fireDamage, coldDamage, poisDamage, nrgyDamage );
            }
        }

        public virtual int BreathComputeDamage()
        {
            int damage = (int)(Hits * BreathDamageScalar);

            if (damage > 100)
                damage = 100;
            
        //    if ( IsParagon )
        //        damage = (int)(damage / Paragon.HitsBuff);

            return damage;
        }
        #endregion

        #region Spill Acid
		public static int SolenAcidDmg = 20;
		public static string SolenAcidMsg = "* The solen's acid sac is burst open! *";

        public void SpillAcid( TimeSpan duration, int minDamage, int maxDamage )
        {
            SpillAcid( duration, minDamage, maxDamage, null, 1, 1 );
        }

		public void SpillAcid( TimeSpan duration, Mobile target )
		{
			SpillAcid( duration, SolenAcidDmg, SolenAcidDmg, target );
		}
        public void SpillAcid( TimeSpan duration, int minDamage, int maxDamage, Mobile target )
        {
            SpillAcid( duration, minDamage, maxDamage, target, 1, 1 );
        }

        public void SpillAcid( TimeSpan duration, int minDamage, int maxDamage, int count )
        {
            SpillAcid( duration, minDamage, maxDamage, null, count, count );
        }

        public void SpillAcid( TimeSpan duration, int minDamage, int maxDamage, int minAmount, int maxAmount )
        {
            SpillAcid( duration, minDamage, maxDamage, null, minAmount, maxAmount );
        }

        public void SpillAcid( TimeSpan duration, int minDamage, int maxDamage, Mobile target, int count )
        {
            SpillAcid( duration, minDamage, maxDamage, target, count, count );
        }

        public void SpillAcid( TimeSpan duration, int minDamage, int maxDamage, Mobile target, int minAmount, int maxAmount )
        {
            if ( (target != null && target.Map == null) || this.Map == null )
                return;

            int pools = Utility.RandomMinMax( minAmount, maxAmount );

            for ( int i = 0; i < pools; ++i )
            {
                PoolOfAcid acid = new PoolOfAcid( duration, minDamage, maxDamage );

                if ( target != null && target.Map != null )
                {
                    acid.MoveToWorld( target.Location, target.Map );
                    continue;
                }

                bool validLocation = false;
                Point3D loc = this.Location;
                Map map = this.Map;

                for ( int j = 0; !validLocation && j < 10; ++j )
                {
                    int x = X + Utility.Random( 3 ) - 1;
                    int y = Y + Utility.Random( 3 ) - 1;
                    int z = map.GetAverageZ( x, y );

                    if ( validLocation = map.CanFit( x, y, this.Z, 16, false, false ) )
                        loc = new Point3D( x, y, Z );
                    else if ( validLocation = map.CanFit( x, y, z, 16, false, false ) )
                        loc = new Point3D( x, y, z );
                }

                acid.MoveToWorld( loc, map );
            }
        }
        #endregion

        #region Flee!!!
        private DateTime m_EndFlee;

        public DateTime EndFleeTime
        {
            get{ return m_EndFlee; }
            set{ m_EndFlee = value; }
        }

        public virtual void StopFlee()
        {
            m_EndFlee = DateTime.MinValue;
        }

        public virtual bool CheckFlee()
        {
            if ( m_EndFlee == DateTime.MinValue )
                return false;

            if ( DateTime.Now >= m_EndFlee )
            {
                StopFlee();
                return false;
            }

            return true;
        }

        public virtual void BeginFlee( TimeSpan maxDuration )
        {
            m_EndFlee = DateTime.Now + maxDuration;
        }
        #endregion

        public BaseAI AIObject{ get{ return m_AI; } }

        public const int MaxOwners = 3;

        public virtual OppositionGroup OppositionGroup
        {
            get{ return null; }
        }

        #region Friends
        public List<Mobile> Friends { get { return m_Friends; } }

        public virtual bool AllowNewPetFriend
        {
            get{ return ( m_Friends == null || m_Friends.Count < 5 ); }
        }

        public virtual bool IsPetFriend( Mobile m )
        {
            return ( m_Friends != null && m_Friends.Contains( m ) );
        }

        public virtual void AddPetFriend( Mobile m )
        {
            if ( m_Friends == null )
                m_Friends = new List<Mobile>();

            m_Friends.Add( m );
        }

        public virtual void RemovePetFriend( Mobile m )
        {
            if ( m_Friends != null )
                m_Friends.Remove( m );
        }

        public virtual bool IsFriend( Mobile m )
        {
            OppositionGroup g = this.OppositionGroup;

            if ( g != null && g.IsEnemy( this, m ) )
                return false;

            if ( !(m is BaseCreature) )
                return false;

            BaseCreature c = (BaseCreature)m;

            return ( m_iTeam == c.m_iTeam && ( (m_bSummoned || m_bControlled) == (c.m_bSummoned || c.m_bControlled) )/* && c.Combatant != this */);
        }
        #endregion

        #region Allegiance
        public virtual Ethics.Ethic EthicAllegiance { get { return null; } }

        public enum Allegiance
        {
            None,
            Ally,
            Enemy
        }

        public virtual Allegiance GetFactionAllegiance( Mobile mob )
        {
            if ( mob == null || mob.Map != Faction.Facet || FactionAllegiance == null )
                return Allegiance.None;

            Faction fac = Faction.Find( mob, true );

            if ( fac == null )
                return Allegiance.None;

            return ( fac == FactionAllegiance ? Allegiance.Ally : Allegiance.Enemy );
        }

        public virtual Allegiance GetEthicAllegiance( Mobile mob )
        {
            if ( mob == null || mob.Map != Faction.Facet || EthicAllegiance == null )
                return Allegiance.None;

            Ethics.Ethic ethic = Ethics.Ethic.Find( mob, true );

            if ( ethic == null )
                return Allegiance.None;

            return ( ethic == EthicAllegiance ? Allegiance.Ally : Allegiance.Enemy );
        }
        #endregion

        public virtual bool IsEnemy( Mobile m )
        {
            if ( m == null )
                return false;

            OppositionGroup g = this.OppositionGroup;

            if ( g != null && g.IsEnemy( this, m ) )
                return true;

            // 26.06.2012 :: zombie :: moby atakuja guardow
            if ( m is BaseNelderimGuard && !Controlled && FightMode == FightMode.Closest )
                return true;
            // zombie

            if ( GetFactionAllegiance( m ) == Allegiance.Ally )
                return false;

            Ethics.Ethic ourEthic = EthicAllegiance;
            Ethics.Player pl = Ethics.Player.Find( m, true );

            if ( pl != null && pl.IsShielded && ( ourEthic == null || ourEthic == pl.Ethic ) )
                return false;

            if ( !(m is BaseCreature) || m is Server.Engines.Quests.Haven.MilitiaFighter )
                return true;

            if( TransformationSpellHelper.UnderTransformation( m, typeof( EtherealVoyageSpell ) ) )
                return false;

            BaseCreature c = (BaseCreature)m;

            return ( m_iTeam != c.m_iTeam || ( (m_bSummoned || m_bControlled) != (c.m_bSummoned || c.m_bControlled) )/* || c.Combatant == this*/ );
        }

        public override string ApplyNameSuffix( string suffix )
        {
            if ( IsParagon )
            {
                if ( suffix.Length == 0 )
                    suffix = "(Paragon)";
                else
                    suffix = String.Concat( suffix, " (Paragon)" );
            }

            return base.ApplyNameSuffix( suffix );
        }

        public virtual bool CheckControlChance( Mobile m )
        {
            if ( GetControlChance( m ) > Utility.RandomDouble() )
            {
                Loyalty += 1;
                return true;
            }

            PlaySound( GetAngerSound() );

            if ( Body.IsAnimal )
                Animate( 10, 5, 1, true, false, 0 );
            else if ( Body.IsMonster )
                Animate( 18, 5, 1, true, false, 0 );

            Loyalty -= 3;
            return false;
        }

        public virtual bool CanBeControlledBy( Mobile m )
        {
            return ( GetControlChance( m ) > 0.0 );
        }

        public double GetControlChance( Mobile m )
        {
            return GetControlChance( m, false );
        }

        public virtual double GetControlChance( Mobile m, bool useBaseSkill )
        {
            if ( m_dMinTameSkill <= 29.1 || m_bSummoned || m.AccessLevel >= AccessLevel.GameMaster )
                return 1.0;

            double dMinTameSkill = m_dMinTameSkill;

            if ( dMinTameSkill > -24.9 && Server.SkillHandlers.AnimalTaming.CheckMastery( m, this ) )
                dMinTameSkill = -24.9;

            int taming = (int)((useBaseSkill ? m.Skills[SkillName.AnimalTaming].Base : m.Skills[SkillName.AnimalTaming].Value ) * 10);
            int lore = (int)((useBaseSkill ? m.Skills[SkillName.AnimalLore].Base : m.Skills[SkillName.AnimalLore].Value )* 10);
            int bonus = 0, chance = 700;

            if ( Core.ML )
            {
                int SkillBonus = taming - (int)(dMinTameSkill * 10);
                int LoreBonus = lore - (int)(dMinTameSkill * 10);

                int SkillMod = 6, LoreMod = 6;

                if ( SkillBonus < 0 )
                    SkillMod = 28;
                if ( LoreBonus < 0 )
                    LoreMod = 14;

                SkillBonus *= SkillMod;
                LoreBonus *= LoreMod;

                bonus = (SkillBonus + LoreBonus) / 2;
            }
            else
            {

                int difficulty = (int)(dMinTameSkill * 10);
                int weighted = ((taming * 4) + lore) / 5;
                bonus = weighted - difficulty;

                if ( bonus <= 0 )
                    bonus *= 14;
                else
                    bonus *= 6;
            }

            chance += bonus;

            if ( chance >= 0 && chance < 200 )
                chance = 200;
            else if ( chance > 990 )
                chance = 990;

            chance -= (MaxLoyalty - m_Loyalty) * 10;

            return ( (double)chance / 1000 );
        }

        public virtual bool CanTransfer(Mobile m) {
            return !Allured;
        }

        private static Type[] m_AnimateDeadTypes = new Type[]
            {
                typeof( MoundOfMaggots ), typeof( HellSteed ), typeof( SkeletalMount ),
                typeof( WailingBanshee ), typeof( Wraith ), typeof( SkeletalDragon ),
                typeof( LichLord ), typeof( FleshGolem ), typeof( Lich ),
                typeof( BoneKnight ), typeof( Mummy ),
                typeof( BoneMagi ), typeof( PatchworkSkeleton )
            };

        public virtual bool IsAnimatedDead
        {
            get
            {
                if ( !Summoned )
                    return false;

                Type type = this.GetType();

                bool contains = false;

                for ( int i = 0; !contains && i < m_AnimateDeadTypes.Length; ++i )
                    contains = ( type == m_AnimateDeadTypes[i] );

                return contains;
            }
        }

        public override void Damage( int amount, Mobile from )
        {
            int oldHits = this.Hits;

            if ( Core.AOS && !this.Summoned && this.Controlled && 0.2 > Utility.RandomDouble() )
                amount = (int)(amount * BonusPetDamageScalar);

            if ( Spells.Necromancy.EvilOmenSpell.CheckEffect( this ) )
                amount = (int)(amount * 1.25);

            Mobile oath = Spells.Necromancy.BloodOathSpell.GetBloodOath( from );

            if ( oath == this )
            {
                amount = (int)(amount * 1.1);
                from.Damage( amount, from );
            }

            base.Damage( amount, from );

            if ( SubdueBeforeTame && !Controlled )
            {
                if ( (oldHits > (this.HitsMax / 10)) && (this.Hits <= (this.HitsMax / 10)) )
                    PublicOverheadMessage( MessageType.Regular, 0x3B2, false, "* The creature has been beaten into subjugation! *" );
            }
        }

        public override void SetLocation( Point3D newLocation, bool isTeleport )
        {
            base.SetLocation( newLocation, isTeleport );

            if ( isTeleport && m_AI != null )
                m_AI.OnTeleported();
        }

        public override void OnBeforeSpawn( Point3D location, Map m )
        {
            if ( Paragon.CheckConvert( this, location, m ) )
                IsParagon = true;

            base.OnBeforeSpawn( location, m );
        }

        public override ApplyPoisonResult ApplyPoison( Mobile from, Poison poison )
        {
            if ( !Alive || IsDeadPet )
                return ApplyPoisonResult.Immune;

            if ( Spells.Necromancy.EvilOmenSpell.CheckEffect( this ) )
                poison = PoisonImpl.IncreaseLevel( poison );

            ApplyPoisonResult result = base.ApplyPoison( from, poison );

            if ( from != null && result == ApplyPoisonResult.Poisoned && PoisonTimer is PoisonImpl.PoisonTimer )
                (PoisonTimer as PoisonImpl.PoisonTimer).From = from;

            return result;
        }

        public override bool CheckPoisonImmunity( Mobile from, Poison poison )
        {
            if ( base.CheckPoisonImmunity( from, poison ) )
                return true;

            Poison p = this.PoisonImmune;

            return ( p != null && p.Level >= poison.Level );
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int Loyalty
        {
            get
            {
                return m_Loyalty;
            }
            set
            {
                m_Loyalty = Math.Min( Math.Max( value, 0 ), MaxLoyalty );
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public WayPoint CurrentWayPoint 
        {
            get
            {
                return m_CurrentWayPoint;
            }
            set
            {
                m_CurrentWayPoint = value;
                if(m_CurrentWayPoint != null && AIObject != null)
                    AIObject.Activate();
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Point2D TargetLocation
        {
            get
            {
                return m_TargetLocation;
            }
            set
            {
                m_TargetLocation = value;
            }
        }

        public virtual Mobile ConstantFocus{ get{ return null; } }

        public virtual bool DisallowAllMoves
        {
            get
            {
                return false;
            }
        }

        public virtual bool InitialInnocent
        {
            get
            {
                return false;
            }
        }

        public virtual bool AlwaysMurderer
        {
            get
            {
                return false;
            }
        }

        public virtual bool AlwaysAttackable
        {
            get
            {
                return false;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public virtual int DamageMin{ get{ return m_DamageMin; } set{ m_DamageMin = value; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public virtual int DamageMax{ get{ return m_DamageMax; } set{ m_DamageMax = value; } }

        [CommandProperty( AccessLevel.GameMaster )]
        public override int HitsMax
        {
            get
            {
                if ( m_HitsMax >= 0 )
                    return m_HitsMax;

                return Str;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int HitsMaxSeed
        {
            get{ return m_HitsMax; }
            set{ m_HitsMax = value; }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public override int StamMax
        {
            get
            {
                if ( m_StamMax >= 0 )
                    return m_StamMax;

                return Dex;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int StamMaxSeed
        {
            get{ return m_StamMax; }
            set{ m_StamMax = value; }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public override int ManaMax
        {
            get
            {
                if ( m_ManaMax >= 0 )
                    return m_ManaMax;

                return Int;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int ManaMaxSeed
        {
            get{ return m_ManaMax; }
            set{ m_ManaMax = value; }
        }

        public virtual bool CanOpenDoors
        {
            get
            {
                return !this.Body.IsAnimal && !this.Body.IsSea;
            }
        }

        public virtual bool CanMoveOverObstacles
        {
            get
            {
                return Core.AOS || this.Body.IsMonster;
            }
        }

        public virtual bool CanDestroyObstacles
        {
            get
            {
                // to enable breaking of furniture, 'return CanMoveOverObstacles;'
                return false;
            }
        }

        public void Unpacify()
        {
            BardEndTime = DateTime.Now;
            BardPacified = false;
        }

        private HonorContext m_ReceivedHonorContext;

        public HonorContext ReceivedHonorContext{ get{ return m_ReceivedHonorContext; } set{ m_ReceivedHonorContext = value; } }

        public override void OnDamage( int amount, Mobile from, bool willKill )
        {
            // 20.08.2012 :: zombie :: kasujemy cialo moba, jezeli guard zadal mu jakikolwiek damage
            if ( from is BaseNelderimGuard && !Controlled && !IsDeadPet )
                m_DeleteCorpseOnDeath = true;
            // zombie

            if ( BardPacified && (HitsMax - Hits) * 0.001 > Utility.RandomDouble() )
                Unpacify();

            int disruptThreshold;
            //NPCs can use bandages too!
            if( !Core.AOS )
                disruptThreshold = 0;
            else if( from != null && from.Player )
                disruptThreshold = 18;
            else
                disruptThreshold = 25;

            if( amount > disruptThreshold )
            {
                BandageContext c = BandageContext.GetContext( this );

                if( c != null )
                    c.Slip();
            }

            if( Confidence.IsRegenerating( this ) )
                Confidence.StopRegenerating( this );

            WeightOverloading.FatigueOnDamage( this, amount );

            InhumanSpeech speechType = this.SpeechType;

            if ( speechType != null && !willKill )
                speechType.OnDamage( this, amount );

            if ( m_ReceivedHonorContext != null )
                m_ReceivedHonorContext.OnTargetDamaged( from, amount );

            base.OnDamage( amount, from, willKill );
        }

        public virtual void OnDamagedBySpell( Mobile from )
        {
        }

        #region Alter[...]Damage From/To

        public virtual void AlterDamageScalarFrom( Mobile caster, ref double scalar )
        {
        }

        public virtual void AlterDamageScalarTo( Mobile target, ref double scalar )
        {
        }

        public virtual void AlterSpellDamageFrom( Mobile from, ref int damage )
        {
        }

        public virtual void AlterSpellDamageTo( Mobile to, ref int damage )
        {
        }

        public virtual void AlterMeleeDamageFrom( Mobile from, ref int damage )
        {
        }

        public virtual void AlterMeleeDamageTo( Mobile to, ref int damage )
        {
        }
        #endregion


        public virtual void CheckReflect( Mobile caster, ref bool reflect )
        {
        }
        
        // 05.07.2012 mortuus: funkcja obliczajaca bonus (procentowy) ilosci skor/miesa/pior/lusek z wycinanych zwlok w zaleznosci od skilla Camping (Myslistwo):
        public static double campingCarveBonus( double skill )
        {
            double bonus = 0.0;    // (pamietac o mozliwosci przeslania ujemnego 'skill')
            
            // bonus rosnie od 25% do 100% skilla:
            if( skill >= 25.0 )
                bonus = (skill-25.0)/75.0;    // 0 bonusa przy 25% skilla, 100% bonusa przy 100% skilla (liniowo)
            
            // dodatkowo: za kazdy 1% powyzej 90% skilla dostajemy 0.01 bonusa (promowanie postaci 90%+ ):
            if( skill >= 90.0 )
                bonus += (skill-90.0)*0.01;    // 90% bonusa przy 90% skilla, ale 110% bonusa przy 100% skilla (liniowo)
            
            // bonus *= 0.75;    //(chec ogolnej redukcji dzialania skilla Camping moze byc wyrazona tutaj - nie ruszy progu minimum, nie ruszy bonusa dla postaci 90+ )
            
            return bonus;                            
        }

        public virtual void OnCarve( Mobile from, Corpse corpse, Item with )
        {
            int feathers = Feathers;
            int wool = Wool;
            int meat = Meat;
            int hides = Hides;
            int scales = Scales;
            int bones = Bones;
            int guts = Guts;

            if ( (feathers == 0 && wool == 0 && meat == 0 && hides == 0 && scales == 0 && bones == 0 && guts == 0) || Summoned || IsBonded )
            {
                from.SendLocalizedMessage( 500485 ); // You see nothing useful to carve from the corpse.
            }
            else
            {
                from.CheckTargetSkill(SkillName.Camping, corpse, 0, 100);

                // 05.07.2012 mortuus: nowa wersja bonusa ilosci wycinanych ze zwlok rzeczy od skilla Myslistwo:                
                double factor = campingCarveBonus( from.Skills[SkillName.Camping].Value );    // bonusowa ilosc materialu dla Campingu (procentowo)
                double factor0 = campingCarveBonus( corpse.CampingCarved );    // bonusowa ilosc materialu dla Campingu (procentowo) - dla postaci poprzednio tnacej zwloki
                
                if( corpse.CampingCarved < 0.0 )
                {
                    corpse.CampingCarved = 0.0;
                    factor += 1.0;    // Pierwsze ciecie:   ilosc bazowa + bonusowa
                }
                
                // Oblicz o ile postac moze wyciac wiecej, niz poprzednia (gorsza) postac:
                feathers = (int)Math.Round( (double)feathers * factor , MidpointRounding.AwayFromZero) - (int)Math.Round( (double)feathers * factor0 , MidpointRounding.AwayFromZero);
                wool = (int)Math.Round((double)wool * factor, MidpointRounding.AwayFromZero) - (int)Math.Round((double)wool * factor0, MidpointRounding.AwayFromZero);
                meat = (int)Math.Round( (double)meat * factor , MidpointRounding.AwayFromZero) - (int)Math.Round( (double)meat * factor0 , MidpointRounding.AwayFromZero);
                hides = (int)Math.Round( (double)hides * factor , MidpointRounding.AwayFromZero) - (int)Math.Round( (double)hides * factor0 , MidpointRounding.AwayFromZero);
                scales = (int)Math.Round( (double)scales * factor , MidpointRounding.AwayFromZero) - (int)Math.Round( (double)scales * factor0 , MidpointRounding.AwayFromZero);
                bones = (int)Math.Round( (double)bones * factor , MidpointRounding.AwayFromZero) - (int)Math.Round( (double)bones * factor0 , MidpointRounding.AwayFromZero);    // 07.07.2012: kosci
                guts = (int)Math.Round( (double)guts * factor , MidpointRounding.AwayFromZero) - (int)Math.Round( (double)guts * factor0 , MidpointRounding.AwayFromZero);
                
                corpse.CampingCarved = (double)from.Skills[SkillName.Camping].Value;    // zapamietaj "jak duzo wyciela TA postac" ze zwlok
                // (koniec liczenia bonusow)
                
                // jest efekt ciecia, czy nie ma:
                if( feathers>0 || wool>0 || meat>0 || hides>0 || scales>0 || bones>0 || guts>0 )
                {
                    new Blood( 0x122D ).MoveToWorld( corpse.Location, corpse.Map );
                }

                if ( feathers > 0 )
                {
                    corpse.AddCarvedItem( new Feather( feathers ), from );
                    from.SendLocalizedMessage( 500479 ); // You pluck the bird. The feathers are now on the corpse.
                }

                if ( guts > 0 )
                {
                    corpse.AddCarvedItem( new Gut( guts ), from );
                    from.SendMessage( "Wyciales nieco jelita ze zwlok." );
                }

                if ( bones > 0 )
                {
                    corpse.AddCarvedItem( new Bone( bones ), from );
                    from.SendMessage( "Wyciales troche kosci ze zwlok." );
                }
                
                if ( wool > 0 )
                {
                    corpse.AddCarvedItem( new TaintedWool( wool ), from );
                    from.SendLocalizedMessage( 500483 ); // You shear it, and the wool is now on the corpse.
                }

                if ( meat > 0 )
                {
                    if ( MeatType == MeatType.Ribs )
                        corpse.AddCarvedItem( new RawRibs( meat ), from );
                    else if ( MeatType == MeatType.Bird )
                        corpse.AddCarvedItem( new RawBird( meat ), from );
                    else if ( MeatType == MeatType.LambLeg )
                        corpse.AddCarvedItem( new RawLambLeg( meat ), from );

                    from.SendLocalizedMessage( 500467 ); // You carve some meat, which remains on the corpse.
                }

                if ( hides > 0 )
                {
                    if ( HideType == HideType.Regular )
                        corpse.DropItem( new Hides( hides ) );
                    else if ( HideType == HideType.Spined )
                        corpse.DropItem( new SpinedHides( hides ) );
                    else if ( HideType == HideType.Horned )
                        corpse.DropItem( new HornedHides( hides ) );
                    else if ( HideType == HideType.Barbed )
                        corpse.DropItem( new BarbedHides( hides ) );

                    from.SendLocalizedMessage( 500471 ); // You skin it, and the hides are now in the corpse.
                }

                if ( scales > 0 )
                {
                    ScaleType sc = this.ScaleType;

                    switch ( sc )
                    {
                        case ScaleType.Red:        corpse.DropItem( new RedScales( scales ) ); break;
                        case ScaleType.Yellow:    corpse.DropItem( new YellowScales( scales ) ); break;
                        case ScaleType.Black:    corpse.DropItem( new BlackScales( scales ) ); break;
                        case ScaleType.Green:    corpse.DropItem( new GreenScales( scales ) ); break;
                        case ScaleType.White:    corpse.DropItem( new WhiteScales( scales ) ); break;
                        case ScaleType.Blue:    corpse.DropItem( new BlueScales( scales ) ); break;
                        case ScaleType.All:
                        {
                            corpse.DropItem( new RedScales( scales ) );
                            corpse.DropItem( new YellowScales( scales ) );
                            corpse.DropItem( new BlackScales( scales ) );
                            corpse.DropItem( new GreenScales( scales ) );
                            corpse.DropItem( new WhiteScales( scales ) );
                            corpse.DropItem( new BlueScales( scales ) );
                            break;
                        }
                    }

                    from.SendMessage( "You cut away some scales, but they remain on the corpse." );
                }

                //corpse.Carved = true;

                if ( corpse.IsCriminalAction( from ) )
                    from.CriminalAction( true );
            }

            corpse.Carved = true;
        }

        public const int DefaultRangePerception = 16;
        public const int OldRangePerception = 10;

        public BaseCreature(AIType ai,
            FightMode mode,
            int iRangePerception,
            int iRangeFight,
            double dActiveSpeed, 
            double dPassiveSpeed)
        {
            if ( iRangePerception == OldRangePerception )
                iRangePerception = DefaultRangePerception;

            m_Loyalty = MaxLoyalty; // Wonderfully Happy

            m_CurrentAI = ai;
            m_DefaultAI = ai;

            m_iRangePerception = iRangePerception;
            m_iRangeFight = iRangeFight;
            
            m_FightMode = mode;

            m_iTeam = 0;

            SpeedInfo.GetSpeeds( this, ref dActiveSpeed, ref dPassiveSpeed );

            m_dActiveSpeed = dActiveSpeed;
            m_dPassiveSpeed = dPassiveSpeed;
            m_dCurrentSpeed = dPassiveSpeed;

            m_bDebugAI = false;

            m_arSpellAttack = new List<Type>();
            m_arSpellDefense = new List<Type>();

            m_bControlled = false;
            m_ControlMaster = null;
            m_ControlTarget = null;
            m_ControlOrder = OrderType.None;

            m_bTamable = false;

            m_Owners = new List<Mobile>();

            m_NextReacquireTime = DateTime.Now + ReacquireDelay;

            m_IsChampionSpawn = false;

            // 18.06.2012 :: zombie
            m_WeaponAbilities = new Dictionary<WeaponAbility, double>();
            AddWeaponAbilities();
            // zombie

            ChangeAIType(AI);

            InhumanSpeech speechType = this.SpeechType;

            if ( speechType != null )
                speechType.OnConstruct( this );
        }

        public BaseCreature( Serial serial ) : base( serial )
        {
            m_arSpellAttack = new List<Type>();
            m_arSpellDefense = new List<Type>();

            m_bDebugAI = false;
        }

        protected override void Init()
        {
            GenerateDifficulty();
            GenerateLoot( true );
        }

        public override void Serialize( GenericWriter writer )
        {
            base.Serialize( writer );

            writer.Write( (int) 19 ); // version

            writer.Write( (int)m_CurrentAI );
            writer.Write( (int)m_DefaultAI );

            writer.Write( (int)m_iRangePerception );
            writer.Write( (int)m_iRangeFight );

            writer.Write( (int)m_iTeam );

            writer.Write( (double)m_dActiveSpeed );
            writer.Write( (double)m_dPassiveSpeed );
            writer.Write( (double)m_dCurrentSpeed );

            writer.Write( (int) m_pHome.X );
            writer.Write( (int) m_pHome.Y );
            writer.Write( (int) m_pHome.Z );

            // Version 1
            writer.Write( (int) m_iRangeHome );

            int i=0;

            writer.Write( (int) m_arSpellAttack.Count );
            for ( i=0; i< m_arSpellAttack.Count; i++ )
            {
                writer.Write( m_arSpellAttack[i].ToString() );
            }

            writer.Write( (int) m_arSpellDefense.Count );
            for ( i=0; i< m_arSpellDefense.Count; i++ )
            {
                writer.Write( m_arSpellDefense[i].ToString() );
            }

            // Version 2
            writer.Write( (int) m_FightMode );

            writer.Write( (bool) m_bControlled );
            writer.Write( (Mobile) m_ControlMaster );
            writer.Write( (Mobile) m_ControlTarget );
            writer.Write( (Point3D) m_ControlDest );
            writer.Write( (int) m_ControlOrder );
            writer.Write( (double) m_dMinTameSkill );
            // Removed in version 9
            //writer.Write( (double) m_dMaxTameSkill );
            writer.Write( (bool) m_bTamable );
            writer.Write( (bool) m_bSummoned );

            if ( m_bSummoned )
                writer.WriteDeltaTime( m_SummonEnd );

            writer.Write( (int) m_iControlSlots );

            // Version 3
            writer.Write( (int)m_Loyalty );

            // Version 4 
            writer.Write( m_CurrentWayPoint );

            // Verison 5
            writer.Write( m_SummonMaster );

            // Version 6
            writer.Write( (int) m_HitsMax );
            writer.Write( (int) m_StamMax );
            writer.Write( (int) m_ManaMax );
            writer.Write( (int) m_DamageMin );
            writer.Write( (int) m_DamageMax );

            // Version 7
            writer.Write( (int) m_PhysicalResistance );
            writer.Write( (int) m_PhysicalDamage );

            writer.Write( (int) m_FireResistance );
            writer.Write( (int) m_FireDamage );

            writer.Write( (int) m_ColdResistance );
            writer.Write( (int) m_ColdDamage );

            writer.Write( (int) m_PoisonResistance );
            writer.Write( (int) m_PoisonDamage );

            writer.Write( (int) m_EnergyResistance );
            writer.Write( (int) m_EnergyDamage );

            // Version 8
            writer.Write( m_Owners, true );

            // Version 10
            writer.Write( (bool) m_IsDeadPet );
            writer.Write( (bool) m_IsBonded );
            writer.Write( (DateTime) m_BondingBegin );
            writer.Write( (DateTime) m_OwnerAbandonTime );

            // Version 11
            writer.Write( (bool) m_HasGeneratedLoot );

            // Version 12
            writer.Write( (bool) m_Paragon );

            // Version 13
            writer.Write( (bool) ( m_Friends != null && m_Friends.Count > 0 ) );

            if ( m_Friends != null && m_Friends.Count > 0 )
                writer.Write( m_Friends, true );

            // Version 14
            writer.Write( (bool)m_RemoveIfUntamed );
            writer.Write( (int)m_RemoveStep );

            // 13.10.2012 :: zombie :: 17
            // writer.Write( (double)m_Difficulty );
            // zombie

            // Version 18   (dla zlecen mysliwskich)
            //writer.Write((bool)m_IsQuestMonster);

            // Version 19
            if (IsStabled || (Controlled && ControlMaster != null))
                writer.Write(TimeSpan.Zero);
            else
                writer.Write(DeleteTimeLeft);

            // Version 23
            writer.Write( (string)null ); //CorpseNameOverride

            // Version 20
            writer.Write(m_Allured);

            // Version 21: Removed m_Difficulty 
        }

        // 12.11.2012 :: zombie
        private static double[] m_StandardActiveSpeeds = new double[]
            {
                0.075, 0.15, 0.225, 0.2625, 0.3, 0.375, 0.45, 0.6, 0.75, 0.9, 1.2
            };

        private static double[] m_StandardPassiveSpeeds = new double[]
            {
                0.1, 0.2, 0.350, 0.4, 0.5, 0.6, 0.8, 1.0, 1.2, 1.6, 2.0
            };
        // zombie

        public override void Deserialize( GenericReader reader )
        {
            base.Deserialize( reader );

            int version = reader.ReadInt();

            m_CurrentAI = (AIType)reader.ReadInt();
            m_DefaultAI = (AIType)reader.ReadInt();

            m_iRangePerception = reader.ReadInt();
            m_iRangeFight = reader.ReadInt();

            m_iTeam = reader.ReadInt();

            m_dActiveSpeed = reader.ReadDouble();
            m_dPassiveSpeed = reader.ReadDouble();
            m_dCurrentSpeed = reader.ReadDouble();

            if ( m_iRangePerception == OldRangePerception )
                m_iRangePerception = DefaultRangePerception;

            m_pHome.X = reader.ReadInt();
            m_pHome.Y = reader.ReadInt();
            m_pHome.Z = reader.ReadInt();

            if ( version >= 1 )
            {
                m_iRangeHome = reader.ReadInt();

                int i, iCount;
                
                iCount = reader.ReadInt();
                for ( i=0; i< iCount; i++ )
                {
                    string str = reader.ReadString();
                    Type type = Type.GetType( str );

                    if ( type != null )
                    {
                        m_arSpellAttack.Add( type );
                    }
                }

                iCount = reader.ReadInt();
                for ( i=0; i< iCount; i++ )
                {
                    string str = reader.ReadString();
                    Type type = Type.GetType( str );

                    if ( type != null )
                    {
                        m_arSpellDefense.Add( type );
                    }            
                }
            }
            else
            {
                m_iRangeHome = 0;
            }

            if ( version >= 2 )
            {
                m_FightMode = ( FightMode )reader.ReadInt();

                m_bControlled = reader.ReadBool();

                if ( version < 16 )
                {
                    switch ( ( int ) m_FightMode )
                    {
                        case 1: m_FightMode = FightMode.Aggressor; break;
                        case 2: m_FightMode = FightMode.Strongest; break;
                        case 3: m_FightMode = FightMode.Weakest; break;
                        case 4: m_FightMode = FightMode.Closest; break;
                        case 5: m_FightMode = FightMode.Evil; break;
                        case 6: m_FightMode = FightMode.Criminal; break;
                    }
                }
                m_ControlMaster = reader.ReadMobile();
                m_ControlTarget = reader.ReadMobile();
                m_ControlDest = reader.ReadPoint3D();
                m_ControlOrder = (OrderType) reader.ReadInt();

                m_dMinTameSkill = reader.ReadDouble();

                if ( version < 9 )
                    reader.ReadDouble();

                m_bTamable = reader.ReadBool();
                m_bSummoned = reader.ReadBool();

                if ( m_bSummoned )
                {
                    m_SummonEnd = reader.ReadDeltaTime();
                    new UnsummonTimer( m_ControlMaster, this, m_SummonEnd - DateTime.Now ).Start();
                }

                m_iControlSlots = reader.ReadInt();
            }
            else
            {
                m_FightMode = FightMode.Closest;

                m_bControlled = false;
                m_ControlMaster = null;
                m_ControlTarget = null;
                m_ControlOrder = OrderType.None;
            }

            if ( version >= 3 )
                m_Loyalty = reader.ReadInt();
            else
                m_Loyalty = MaxLoyalty; // Wonderfully Happy

            if ( version >= 4 )
                m_CurrentWayPoint = reader.ReadItem() as WayPoint;

            if ( version >= 5 )
                m_SummonMaster = reader.ReadMobile();

            if ( version >= 6 )
            {
                m_HitsMax = reader.ReadInt();
                m_StamMax = reader.ReadInt();
                m_ManaMax = reader.ReadInt();
                m_DamageMin = reader.ReadInt();
                m_DamageMax = reader.ReadInt();
            }

            if ( version >= 7 )
            {
                m_PhysicalResistance = reader.ReadInt();
                m_PhysicalDamage = reader.ReadInt();

                m_FireResistance = reader.ReadInt();
                m_FireDamage = reader.ReadInt();

                m_ColdResistance = reader.ReadInt();
                m_ColdDamage = reader.ReadInt();
                
                m_PoisonResistance = reader.ReadInt();
                m_PoisonDamage = reader.ReadInt();

                m_EnergyResistance = reader.ReadInt();
                m_EnergyDamage = reader.ReadInt();
            }

            if ( version >= 8 )
                m_Owners = reader.ReadStrongMobileList();
            else
                m_Owners = new List<Mobile>();

            if ( version >= 10 )
            {
                m_IsDeadPet = reader.ReadBool();
                m_IsBonded = reader.ReadBool();
                m_BondingBegin = reader.ReadDateTime();
                m_OwnerAbandonTime = reader.ReadDateTime();
            }

            if ( version >= 11 )
                m_HasGeneratedLoot = reader.ReadBool();
            else
                m_HasGeneratedLoot = true;

            if ( version >= 12 )
                m_Paragon = reader.ReadBool();
            else
                m_Paragon = false;

            if ( version >= 13 && reader.ReadBool() )
                m_Friends = reader.ReadStrongMobileList();
            else if ( version < 13 && m_ControlOrder >= OrderType.Unfriend )
                ++m_ControlOrder;

            if ( version < 16 )
                Loyalty *= 10;

            double activeSpeed = m_dActiveSpeed;
            double passiveSpeed = m_dPassiveSpeed;

            SpeedInfo.GetSpeeds( this, ref activeSpeed, ref passiveSpeed );

            bool isStandardActive = false;
            for ( int i = 0; !isStandardActive && i < m_StandardActiveSpeeds.Length; ++i )
                isStandardActive = ( m_dActiveSpeed == m_StandardActiveSpeeds[i] );

            bool isStandardPassive = false;
            for ( int i = 0; !isStandardPassive && i < m_StandardPassiveSpeeds.Length; ++i )
                isStandardPassive = ( m_dPassiveSpeed == m_StandardPassiveSpeeds[i] );

            if ( isStandardActive && m_dCurrentSpeed == m_dActiveSpeed )
                m_dCurrentSpeed = activeSpeed;
            else if ( isStandardPassive && m_dCurrentSpeed == m_dPassiveSpeed )
                m_dCurrentSpeed = passiveSpeed;

            if ( isStandardActive && !m_Paragon )
                m_dActiveSpeed = activeSpeed;

            if ( isStandardPassive && !m_Paragon )
                m_dPassiveSpeed = passiveSpeed;

            if ( version >= 14 )
            {
                m_RemoveIfUntamed = reader.ReadBool();
                m_RemoveStep = reader.ReadInt();
            }

            if ( version > 19 && version < 21 )
            {
                // 14.10.2012 :: zombie :: 17
                /*m_Difficulty = */reader.ReadDouble();
                // zombie
            }

            if ( version > 19 && version < 22)
            {
                // dla zlecen mysliwskich
                /*m_IsQuestMonster = */reader.ReadBool();
            }

            TimeSpan deleteTime = TimeSpan.Zero;

            if (version >= 17)
                deleteTime = reader.ReadTimeSpan();

            if (deleteTime > TimeSpan.Zero || LastOwner != null && !Controlled && !IsStabled && !Blessed) {
                if (deleteTime == TimeSpan.Zero)
                    deleteTime = TimeSpan.FromDays(3.0);

                m_DeleteTimer = new DeleteTimer(this, deleteTime);
                m_DeleteTimer.Start();
            }

            if ( version >= 19 )
            {
                reader.ReadString(); //Corpse Name Override
            }

            if ( version >= 19)
                m_Allured = reader.ReadBool();

            if ( version <= 14 && m_Paragon && Hue == 0x31 )
            {
                Hue = Paragon.Hue; //Paragon hue fixed, should now be 0x501.
            }

            CheckStatTimers();

            ChangeAIType(m_CurrentAI);

            AddFollowers();

            if ( IsAnimatedDead )
                Spells.Necromancy.AnimateDeadSpell.Register( m_SummonMaster, this );

            // 18.07.2012 :: zombie
            if ( m_WeaponAbilities == null )
                m_WeaponAbilities = new Dictionary<WeaponAbility, double>();

            AddWeaponAbilities();
            // zombie
        }

        public virtual bool IsHumanInTown()
        {
            return ( Body.IsHuman && Region.IsPartOf( typeof( Regions.GuardedRegion ) ) );
        }

        public virtual bool CheckGold( Mobile from, Item dropped )
        {
            if ( dropped is Gold )
                return OnGoldGiven( from, (Gold)dropped );

            return false;
        }

        public virtual bool OnGoldGiven( Mobile from, Gold dropped )
        {
            if ( CheckTeachingMatch( from ) )
            {
                // 07.01.2013 :: szczaw :: vendor "wydaje" reszt� 
                int goldTaken = Teach( m_Teaching, from, dropped.Amount, true );

                if ( goldTaken > 0)
                {
                    if(goldTaken == dropped.Amount)
                    {
                        // Usuwam z�oto...
                        dropped.Delete();
                        return true;
                    }
                    else
                    {
                        // Modyfikuj� dane z�oto...
                        dropped.Amount -= goldTaken;
                        return false;
                    }
                }
            }
            else if ( IsHumanInTown() )
            {
                Direction = GetDirectionTo( from );

                int oldSpeechHue = this.SpeechHue;

                this.SpeechHue = 0x23F;
                SayTo( from, "Thou art giving me gold?" );

                if ( dropped.Amount >= 400 )
                    SayTo( from, "'Tis a noble gift." );
                else
                    SayTo( from, "Money is always welcome." );

                this.SpeechHue = 0x3B2;
                SayTo( from, 501548 ); // I thank thee.

                this.SpeechHue = oldSpeechHue;

                dropped.Delete();
                return true;
            }

            return false;
        }

        public override bool ShouldCheckStatTimers{ get{ return false; } }

        #region Food
        private static Type[] m_Eggs = new Type[]
            {
                typeof( FriedEggs ), typeof( Eggs )
            };

        private static Type[] m_Fish = new Type[]
            {
                typeof( FishSteak ), typeof( RawFishSteak )
            };

        private static Type[] m_GrainsAndHay = new Type[]
            {
                typeof( BreadLoaf ), typeof( FrenchBread ), typeof( SheafOfHay )
            };

        private static Type[] m_Meat = new Type[]
            {
                /* Cooked */
                typeof( Bacon ), typeof( CookedBird ), typeof( Sausage ),
                typeof( Ham ), typeof( Ribs ), typeof( LambLeg ),
                typeof( ChickenLeg ),

                /* Uncooked */
                typeof( RawBird ), typeof( RawRibs ), typeof( RawLambLeg ),
                typeof( RawChickenLeg ),

                /* Body Parts */
                typeof( Head ), typeof( LeftArm ), typeof( LeftLeg ),
                typeof( Torso ), typeof( RightArm ), typeof( RightLeg )
            };

        private static Type[] m_FruitsAndVegies = new Type[]
            {
                typeof( HoneydewMelon ), typeof( YellowGourd ), typeof( GreenGourd ),
                typeof( Banana ), typeof( Bananas ), typeof( Lemon ), typeof( Lime ),
                typeof( Dates ), typeof( Grapes ), typeof( Peach ), typeof( Pear ),
                typeof( Apple ), typeof( Watermelon ), typeof( Squash ),
                typeof( Cantaloupe ), typeof( Carrot ), typeof( Cabbage ),
                typeof( Onion ), typeof( Lettuce ), typeof( Pumpkin )
            };

        private static Type[] m_Gold = new Type[]
            {
                // white wyrms eat gold..
                typeof( Gold )
            };

        private static Type[] m_Amethyst = new Type[]
            {
                // amethyst dragon eat amethyst.
                typeof( Amethyst )
            };

        private static Type[] m_Sapphire = new Type[]
            {
                // Sapphire dragon eat sapphire
                typeof( Sapphire )
            };

        private static Type[] m_StarSapphire = new Type[]
            {
                // Sapphire dragon eat star sapphire
                typeof( StarSapphire )
            };

        private static Type[] m_Emerald = new Type[]
            {
                // Emerald Dragons eat emerald
                typeof( Emerald )
            };

        private static Type[] m_Ruby = new Type[]
            {
                // Ruby dragon eat ruby
                typeof( Ruby )
            };

        private static Type[] m_Diamond = new Type[]
            {
                // Diamond dragon eat diamond
                typeof( Diamond )
            };

        public virtual bool CheckFoodPreference( Item f )
        {
            if ( CheckFoodPreference( f, FoodType.Eggs, m_Eggs ) )
                return true;

            if ( CheckFoodPreference( f, FoodType.Fish, m_Fish ) )
                return true;

            if ( CheckFoodPreference( f, FoodType.GrainsAndHay, m_GrainsAndHay ) )
                return true;

            if ( CheckFoodPreference( f, FoodType.Meat, m_Meat ) )
                return true;

            if ( CheckFoodPreference( f, FoodType.FruitsAndVegies, m_FruitsAndVegies ) )
                return true;

            if ( CheckFoodPreference( f, FoodType.Gold, m_Gold ) )
                return true;

            if ( CheckFoodPreference( f, FoodType.Amethyst, m_Amethyst ) )
            return true;

            if ( CheckFoodPreference( f, FoodType.Sapphire, m_Sapphire ) )
            return true;

            if ( CheckFoodPreference( f, FoodType.StarSapphire, m_StarSapphire ) )
            return true;

            if ( CheckFoodPreference( f, FoodType.Emerald, m_Emerald ) )
            return true;

            if ( CheckFoodPreference( f, FoodType.Ruby, m_Ruby ) )
            return true;

            if ( CheckFoodPreference( f, FoodType.Diamond, m_Diamond ) )
            return true;

            return false;
        }

        public virtual bool CheckFoodPreference( Item fed, FoodType type, Type[] types )
        {
            if ( (FavoriteFood & type) == 0 )
                return false;

            Type fedType = fed.GetType();
            bool contains = false;

            for ( int i = 0; !contains && i < types.Length; ++i )
                contains = ( fedType == types[i] );

            return contains;
        }
        
        public virtual bool OverrideBondingReqs()
        {
            return false;
        }

        public virtual bool CheckFeed( Mobile from, Item dropped )
        {
            if ( !IsDeadPet && Controlled && (ControlMaster == from || IsPetFriend( from )) && (dropped is Food || dropped is Gold || dropped is CookableFood || dropped is Head || dropped is LeftArm || dropped is LeftLeg || dropped is Torso || dropped is RightArm || dropped is RightLeg || dropped is Amethyst || dropped is Sapphire || dropped is StarSapphire || dropped is Emerald || dropped is Ruby || dropped is Diamond ) )
            {
                if (CheckFoodPreference(dropped))
                {
                    if (dropped.Amount > 0)
                    {
                        bool happier = false;

                        int stamGain = 0;
                        int reqStam = StamMax - Stam;                        
                        int toEat = 0;

                        if (reqStam > 0)
                        {
                            double stamPerFood;
                            if (dropped is Gold)
                                stamPerFood = 1.0;
                            else
                                stamPerFood = 15.0;

                            stamGain = (int)Math.Round(stamPerFood * dropped.Amount - 50);
                            if (stamGain > reqStam)
                            {
                                stamGain = reqStam;
                                toEat = (int)Math.Round((stamGain + 50) / stamPerFood);
                            }
                            else
                            {
                                toEat = dropped.Amount;
                            }
                        }

                        if ( stamGain > 0 )
                            Stam += stamGain;

                        int i ;
                        for (i = 0; i < dropped.Amount; ++i)
                        {
                            if ( m_Loyalty >= MaxLoyalty )
                                break;

                            if ( 0.5 >= Utility.RandomDouble() )
                            {
                                m_Loyalty += 10;
                                happier = true;
                            }
                        }

                        if (i > toEat)
                            toEat = i;

                        dropped.Consume(toEat);

                        if ( happier )
                            SayTo( from, 502060 ); // Your pet looks happier.

                        if ( Body.IsAnimal )
                            Animate( 3, 5, 1, true, false, 0 );
                        else if ( Body.IsMonster )
                            Animate( 17, 5, 1, true, false, 0 );

                        if ( IsBondable && !IsBonded )
                        {
                            Mobile master = m_ControlMaster;

                            if ( master != null && master == from )    //So friends can't start the bonding process
                            {
                                if ( m_dMinTameSkill <= 29.1 || master.Skills[SkillName.AnimalTaming].Base >= m_dMinTameSkill || GetControlChance( master, true ) >= 1.0 || OverrideBondingReqs() )
                                {
                                    // Fix: zmiana daty systemowej serwera spododowala, ze czas uwiernienia byl o kilka miesiecy
                                    // w przyszlosci. W takim przypadku zaczynamy uwiernianie od nowa:
                                    bool fixBonding = BondingBegin != DateTime.MinValue && BondingBegin > DateTime.Now;
                                    
                                    if ( BondingBegin == DateTime.MinValue || fixBonding ) // fix
                                    {
                                        BondingBegin = DateTime.Now;
                                        from.SendLocalizedMessage( 1005008 ); // Zwierze zaczyna sie do Ciebie przywiazywac.
                                    }
                                    else if ( (BondingBegin + BondingDelay) <= DateTime.Now )
                                    {
                                        IsBonded = true;
                                        BondingBegin = DateTime.MinValue;
                                        from.SendLocalizedMessage( 1049666 ); // Your pet has bonded with you!
                                    }
                                }
                                else if( Core.ML )
                                {
                                    from.SendLocalizedMessage( 1075268 ); // Your pet cannot form a bond with you until your animal taming ability has risen.
                                }
                            }
                        }

                        return false;
                    }
                }
            }

            return false;
        }

        #endregion

        public virtual bool CanAngerOnTame{ get{ return false; } }

        #region OnAction[...]
        public virtual void OnActionWander()
        {
        }

        public virtual void OnActionCombat()
        {
        }

        public virtual void OnActionGuard()
        {
        }

        public virtual void OnActionFlee()
        {
        }

        public virtual void OnActionInteract()
        {
        }

        public virtual void OnActionBackoff()
        {
        }
        #endregion

        public override bool OnDragDrop( Mobile from, Item dropped )
        {
            if ( CheckFeed( from, dropped ) )
                return true;
            else if ( CheckGold( from, dropped ) )
                return true;

            return base.OnDragDrop( from, dropped );
        }

        protected virtual BaseAI ForcedAI { get { return null; } }

        public  void ChangeAIType( AIType NewAI )
        {
            if ( m_AI != null )
                m_AI.m_Timer.Stop();

            if( ForcedAI != null )
            {
                m_AI = ForcedAI;
                return;
            }

            m_AI = null;

            switch ( NewAI )
            {
                case AIType.AI_Melee:
                    m_AI = new MeleeAI(this);
                    break;
                case AIType.AI_Animal:
                    m_AI = new AnimalAI(this);
                    break;
                case AIType.AI_Berserk:
                    m_AI = new BerserkAI(this);
                    break;
                case AIType.AI_Archer:
                    m_AI = new ArcherAI(this);
                    break;
                case AIType.AI_Healer:
                    m_AI = new HealerAI(this);
                    break;
                case AIType.AI_Vendor:
                    m_AI = new VendorAI(this);
                    break;
                case AIType.AI_Mage:
                    m_AI = new MageAI(this);
                    break;
                case AIType.AI_NecroMage:
                    m_AI = new NecroMageAI( this );
                    break;
                    case AIType.AI_BattleMage:
                    m_AI = new BattleMageAI( this );
                    break;
                case AIType.AI_Predator:
                    //m_AI = new PredatorAI(this);
                    m_AI = new MeleeAI(this);
                    break;
                case AIType.AI_Thief:
                    m_AI = new ThiefAI(this);
                    break;
                // 23.06.2012 :: zombie
                case AIType.AI_Boss:
                    m_AI = new BossAI( this );
                    break;
                case AIType.AI_Mounted:
                    m_AI = new MountedAI( this );
                    break;
                // zombie
                case AIType.AI_RangedMelee:
                    m_AI = new RangedMeleeAI( this );
                    break;
            }
        }

        public void ChangeAIToDefault()
        {
            ChangeAIType(m_DefaultAI);
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public AIType AI
        {
            get
            {
                return m_CurrentAI;
            }
            set
            {
                m_CurrentAI = value;

                if (m_CurrentAI == AIType.AI_Use_Default)
                {
                    m_CurrentAI = m_DefaultAI;
                }
                
                ChangeAIType(m_CurrentAI);
            }
        }

        [CommandProperty( AccessLevel.Administrator )]
        public bool Debug
        {
            get
            {
                return m_bDebugAI;
            }
            set
            {
                m_bDebugAI = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int Team
        {
            get
            {
                return m_iTeam;
            }
            set
            {
                m_iTeam = value;
                
                OnTeamChange();
            }
        }

        public virtual void OnTeamChange()
        {
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile FocusMob
        {
            get
            {
                return m_FocusMob;
            }
            set
            {
                m_FocusMob = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public FightMode FightMode
        {
            get
            {
                return m_FightMode;
            }
            set
            {
                m_FightMode = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int RangePerception
        {
            get
            {
                return m_iRangePerception;
            }
            set
            {
                m_iRangePerception = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int RangeFight
        {
            get
            {
                return m_iRangeFight;
            }
            set
            {
                m_iRangeFight = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public int RangeHome
        {
            get
            {
                return m_iRangeHome;
            }
            set
            {
                m_iRangeHome = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public double ActiveSpeed
        {
            get
            {
                return m_dActiveSpeed;
            }
            set
            {
                m_dActiveSpeed = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public double PassiveSpeed
        {
            get
            {
                return m_dPassiveSpeed;
            }
            set
            {
                m_dPassiveSpeed = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public double CurrentSpeed
        {
            get
            {
                return m_dCurrentSpeed;
            }
            set
            {
                if ( m_dCurrentSpeed != value )
                {
                    m_dCurrentSpeed = value;

                    if (m_AI != null)
                        m_AI.OnCurrentSpeedChanged();
                }
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Point3D Home
        {
            get
            {
                return m_pHome;
            }
            set
            {
                m_pHome = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public bool Controlled
        {
            get
            {
                return m_bControlled;
            }
            set
            {
                if ( m_bControlled == value )
                    return;

                m_bControlled = value;
                Delta( MobileDelta.Noto );

                InvalidateProperties();

                StopDeleteTimer();
            }
        }

        public override void RevealingAction()
        {
            Spells.Sixth.InvisibilitySpell.RemoveTimer( this );

            base.RevealingAction();
        }

        public void RemoveFollowers()
        {
            if ( m_ControlMaster != null )
            {
                m_ControlMaster.Followers -= ControlSlots;
                if ( m_ControlMaster is PlayerMobile )
                {
                    ((PlayerMobile)m_ControlMaster).AllFollowers.Remove( this );
                }
            }
            else if ( m_SummonMaster != null )
            {
                m_SummonMaster.Followers -= ControlSlots;
                if ( m_SummonMaster is PlayerMobile )
                {
                    ((PlayerMobile)m_SummonMaster).AllFollowers.Remove( this );
                }
            }

            if ( m_ControlMaster != null && m_ControlMaster.Followers < 0 )
                m_ControlMaster.Followers = 0;

            if ( m_SummonMaster != null && m_SummonMaster.Followers < 0 )
                m_SummonMaster.Followers = 0;
        }

        public void AddFollowers()
        {
            if ( m_ControlMaster != null )
            {
                m_ControlMaster.Followers += ControlSlots;
                if ( m_ControlMaster is PlayerMobile )
                {
                    ((PlayerMobile)m_ControlMaster).AllFollowers.Add( this );
                }
            }
            else if ( m_SummonMaster != null )
            {
                m_SummonMaster.Followers += ControlSlots;
                if ( m_SummonMaster is PlayerMobile )
                {
                    ((PlayerMobile)m_SummonMaster).AllFollowers.Add( this );
                }
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile ControlMaster
        {
            get
            {
                return m_ControlMaster;
            }
            set
            {
                if ( m_ControlMaster == value || this.Equals(value) )
                    return;

                RemoveFollowers();
                m_ControlMaster = value;
                AddFollowers();

                Delta( MobileDelta.Noto );
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile SummonMaster
        {
            get
            {
                return m_SummonMaster;
            }
            set
            {
                if ( m_SummonMaster == value || this.Equals(value) )
                    return;

                RemoveFollowers();
                m_SummonMaster = value;
                AddFollowers();

                Delta( MobileDelta.Noto );
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile ControlTarget
        {
            get
            {
                return m_ControlTarget;
            }
            set
            {
                m_ControlTarget = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Point3D ControlDest
        {
            get
            {
                return m_ControlDest;
            }
            set
            {
                m_ControlDest = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public OrderType ControlOrder
        {
            get
            {
                return m_ControlOrder;
            }
            set
            {
                m_ControlOrder = value;

                if (m_Allured) {
                    if (m_ControlOrder == OrderType.Release) {
                        Say(502003); // Sorry, but no.
                    } else if (m_ControlOrder != OrderType.None) {
                        Say(502002); // Very well.
                    }
                }

                if ( m_AI != null )
                    m_AI.OnCurrentOrderChanged();
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public bool BardProvoked
        {
            get
            {
                return m_bBardProvoked;
            }
            set
            {
                m_bBardProvoked = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public bool BardPacified
        {
            get
            {
                return m_bBardPacified;
            }
            set
            {
                m_bBardPacified = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile BardMaster
        {
            get
            {
                return m_bBardMaster;
            }
            set
            {
                m_bBardMaster = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public Mobile BardTarget
        {
            get
            {
                return m_bBardTarget;
            }
            set
            {
                m_bBardTarget = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public DateTime BardEndTime
        {
            get
            {
                return m_timeBardEnd;
            }
            set
            {
                m_timeBardEnd = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public double MinTameSkill
        {
            get
            {
                return m_dMinTameSkill;
            }
            set
            {
                m_dMinTameSkill = value;
            }
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public bool Tamable
        {
            get
            {
                return m_bTamable && !m_Paragon;
            }
            set
            {
                m_bTamable = value;
            }
        }

        [CommandProperty( AccessLevel.Administrator )]
        public bool Summoned
        {
            get
            {
                return m_bSummoned;
            }
            set
            {
                if ( m_bSummoned == value )
                    return;

                m_NextReacquireTime = DateTime.Now;

                m_bSummoned = value;
                Delta( MobileDelta.Noto );

                InvalidateProperties();
            }
        }

        [CommandProperty( AccessLevel.Administrator )]
        public int ControlSlots
        {
            get
            {
                return m_iControlSlots;
            }
            set
            {
                m_iControlSlots = value;
            }
        }

        public virtual bool NoHouseRestrictions{ get{ return false; } }
        public virtual bool IsHouseSummonable{ get{ return false; } }

        #region Corpse Resources
        public virtual int Feathers{ get{ return 0; } }
        public virtual int Wool{ get{ return 0; } }
        public virtual int Bones
        {
            get
            {
                // Be careful with any random functions here.
                // Bones may be called few times when characters with different Camping
                // skill level are carving the corpse. In such case it should return
                // the same value each time (see OnCarve() method below).
                return (int)Math.Round((double)Meat * 0.33, MidpointRounding.AwayFromZero);
            }
        }
        public virtual int Guts
        {
            get
            {
                // Be careful with any random functions here.
                // Guts may be called few times when characters with different Camping
                // skill level are carving the corpse. In such case it should return
                // the same value each time (see OnCarve() method below).
                return (int)Math.Round((double)Meat * 0.33, MidpointRounding.AwayFromZero);
            }
        }

        public virtual MeatType MeatType{ get{ return MeatType.Ribs; } }
        public virtual int Meat{ get{ return 0; } }

        public virtual int Hides{ get{ return 0; } }
        public virtual HideType HideType{ get{ return HideType.Regular; } }

        public virtual int Scales{ get{ return 0; } }
        public virtual ScaleType ScaleType{ get{ return ScaleType.Red; } }
        #endregion

        public virtual bool AutoDispel{ get{ return false; } }
        public virtual double AutoDispelChance{ get { return ((Core.SE) ? .10 : 1.0); } }

        public virtual bool IsScaryToPets{ get{ return false; } }
        public virtual bool IsScaredOfScaryThings{ get{ return true; } }

        public virtual bool CanRummageCorpses{ get{ return false; } }

        public virtual void OnGotMeleeAttack( Mobile attacker )
        {
            if ( AutoDispel && attacker is BaseCreature && ((BaseCreature)attacker).IsDispellable && AutoDispelChance > Utility.RandomDouble() )
                Dispel( attacker );
        }

        public virtual void Dispel( Mobile m )
        {
            Effects.SendLocationParticles( EffectItem.Create( m.Location, m.Map, EffectItem.DefaultDuration ), 0x3728, 8, 20, 5042 );
            Effects.PlaySound( m, m.Map, 0x201 );

            m.Delete();
        }

        public virtual bool DeleteOnRelease{ get{ return m_bSummoned; } }

        public virtual void OnGaveMeleeAttack( Mobile defender )
        {
            Poison p = HitPoison;
            
            if ( m_Paragon )
                p = PoisonImpl.IncreaseLevel( p );

            if ( p != null && HitPoisonChance >= Utility.RandomDouble() )
                defender.ApplyPoison( this, p );

            if( AutoDispel && defender is BaseCreature && ((BaseCreature)defender).IsDispellable && AutoDispelChance > Utility.RandomDouble() )
                Dispel( defender );
        }

        public override void OnAfterDelete()
        {
            if ( m_AI != null )
            {
                if ( m_AI.m_Timer != null )
                    m_AI.m_Timer.Stop();

                m_AI = null;
            }

            FocusMob = null;

            if ( IsAnimatedDead )
                Spells.Necromancy.AnimateDeadSpell.Unregister( m_SummonMaster, this );

            base.OnAfterDelete();
        }

        public void DebugSay( string text )
        {
            if ( m_bDebugAI )
                this.PublicOverheadMessage( MessageType.Regular, 41, false, text );
        }

        public void DebugSay( string format, params object[] args )
        {
            if ( m_bDebugAI )
                this.PublicOverheadMessage( MessageType.Regular, 41, false, String.Format( format, args ) );
        }

        /* 
         * This function can be overriden.. so a "Strongest" mobile, can have a different definition depending
         * on who check for value
         * -Could add a FightMode.Prefered
         * 
         */
        public virtual double GetFightModeRanking( Mobile m, FightMode acqType, bool bPlayerOnly )
        {
            if ( acqType >= FightMode.Criminal )
                acqType = ( FightMode ) ( ( int ) acqType - ( int ) FightMode.Closest );

            if ( ( bPlayerOnly && m.Player ) ||  !bPlayerOnly )
            {
                switch( acqType )
                {
                    case FightMode.Strongest : 
                        return (m.Skills[SkillName.Tactics].Value + m.Str); //returns strongest mobile

                    case FightMode.Weakest : 
                        return -m.Hits; // returns weakest mobile

                    default : 
                        return -GetDistanceToSqrt( m ); // returns closest mobile
                }
            }
            else
            {
                return double.MinValue;
            }
        }

        // Turn, - for left, + for right
        // Basic for now, needs work
        public virtual void Turn(int iTurnSteps)
        {
            int v = (int)Direction;

            Direction = (Direction)((((v & 0x7) + iTurnSteps) & 0x7) | (v & 0x80));
        }

        public virtual void TurnInternal(int iTurnSteps)
        {
            int v = (int)Direction;

            SetDirection( (Direction)((((v & 0x7) + iTurnSteps) & 0x7) | (v & 0x80)) );
        }

        public bool IsHurt()
        {
            return ( Hits != HitsMax );
        }

        public double GetHomeDistance()
        {
            return GetDistanceToSqrt( m_pHome );
        }

        public virtual int GetTeamSize(int iRange)
        {
            int iCount = 0;

            IPooledEnumerable eable = this.GetMobilesInRange( iRange );
            foreach ( Mobile m in eable )
            {
                if (m is BaseCreature)
                {
                    if ( ((BaseCreature)m).Team == Team )
                    {
                        if ( !m.Deleted )
                        {
                            if ( m != this )
                            {
                                if ( CanSee( m ) )
                                {
                                    iCount++;
                                }
                            }
                        }
                    }
                }
            }
            eable.Free();
            
            return iCount;
        }

        private class TameEntry : ContextMenuEntry
        {
            private BaseCreature m_Mobile;

            public TameEntry( Mobile from, BaseCreature creature ) : base( 6130, 6 )
            {
                m_Mobile = creature;

                Enabled = Enabled && ( from.Female ? creature.AllowFemaleTamer : creature.AllowMaleTamer );
            }

            public override void OnClick()
            {
                if ( !Owner.From.CheckAlive() )
                    return;

                Owner.From.TargetLocked = true;
                SkillHandlers.AnimalTaming.DisableMessage = true;

                if ( Owner.From.UseSkill( SkillName.AnimalTaming ) )
                    Owner.From.Target.Invoke( Owner.From, m_Mobile );

                SkillHandlers.AnimalTaming.DisableMessage = false;
                Owner.From.TargetLocked = false;
            }
        }

        #region Teaching
        public virtual bool CanTeach{ get{ return false; } }

        public virtual bool CheckTeach( SkillName skill, Mobile from )
        {
            if ( !CanTeach )
                return false;

            if( skill == SkillName.Stealth && from.Skills[SkillName.Hiding].Base < ((Core.SE) ? 50.0 : 80.0) )
                return false;

            if ( skill == SkillName.RemoveTrap && (from.Skills[SkillName.Lockpicking].Base < 50.0 || from.Skills[SkillName.DetectHidden].Base < 50.0) )
                return false;

            if ( !Core.AOS && (skill == SkillName.Focus || skill == SkillName.Chivalry || skill == SkillName.Necromancy) )
                return false;

            return true;
        }

        public enum TeachResult
        {
            Success,
            Failure,
            KnowsMoreThanMe,
            KnowsWhatIKnow,
            SkillNotRaisable,
            NotEnoughFreePoints
        }

        public virtual TeachResult CheckTeachSkills( SkillName skill, Mobile m, int maxPointsToLearn, ref int pointsToLearn, bool doTeach )
        {
            if ( !CheckTeach( skill, m ) || !m.CheckAlive() )
                return TeachResult.Failure;

            Skill ourSkill = Skills[skill];
            Skill theirSkill = m.Skills[skill];

            if ( ourSkill == null || theirSkill == null )
                return TeachResult.Failure;

            // 18.11.2012 mortuus - NPC beda uczyc rowne 30 skilla:
			int baseToSet = 300;
			/*
			int baseToSet = ourSkill.BaseFixedPoint / 3;

            if ( baseToSet > 420 )	
                baseToSet = 420;
            else if ( baseToSet < 200 )
                return TeachResult.Failure;
			*/

            if ( baseToSet > theirSkill.CapFixedPoint )
                baseToSet = theirSkill.CapFixedPoint;

            pointsToLearn = baseToSet - theirSkill.BaseFixedPoint;

            if ( maxPointsToLearn > 0 && pointsToLearn > maxPointsToLearn )
            {
                pointsToLearn = maxPointsToLearn;
                baseToSet = theirSkill.BaseFixedPoint + pointsToLearn;
            }

            if ( pointsToLearn < 0 )
                return TeachResult.KnowsMoreThanMe;

            if ( pointsToLearn == 0 )
                return TeachResult.KnowsWhatIKnow;

            if ( theirSkill.Lock != SkillLock.Up )
                return TeachResult.SkillNotRaisable;

            int freePoints = m.Skills.Cap - m.Skills.Total;
            int freeablePoints = 0;

            if ( freePoints < 0 )
                freePoints = 0;

            for ( int i = 0; (freePoints + freeablePoints) < pointsToLearn && i < m.Skills.Length; ++i )
            {
                Skill sk = m.Skills[i];

                if ( sk == theirSkill || sk.Lock != SkillLock.Down )
                    continue;

                freeablePoints += sk.BaseFixedPoint;
            }

            if ( (freePoints + freeablePoints) == 0 )
                return TeachResult.NotEnoughFreePoints;

            if ( (freePoints + freeablePoints) < pointsToLearn )
            {
                pointsToLearn = freePoints + freeablePoints;
                baseToSet = theirSkill.BaseFixedPoint + pointsToLearn;
            }

            if ( doTeach )
            {
                int need = pointsToLearn - freePoints;

                for ( int i = 0; need > 0 && i < m.Skills.Length; ++i )
                {
                    Skill sk = m.Skills[i];

                    if ( sk == theirSkill || sk.Lock != SkillLock.Down )
                        continue;

                    if ( sk.BaseFixedPoint < need )
                    {
                        need -= sk.BaseFixedPoint;
                        sk.BaseFixedPoint = 0;
                    }
                    else
                    {
                        sk.BaseFixedPoint -= need;
                        need = 0;
                    }
                }

                /* Sanity check */
                if ( baseToSet > theirSkill.CapFixedPoint || (m.Skills.Total - theirSkill.BaseFixedPoint + baseToSet) > m.Skills.Cap )
                    return TeachResult.NotEnoughFreePoints;

                theirSkill.BaseFixedPoint = baseToSet;
            }

            return TeachResult.Success;
        }

        public virtual bool CheckTeachingMatch( Mobile m )
        {
            if ( m_Teaching == (SkillName)(-1) )
                return false;

            if ( m is PlayerMobile )
                return ( ((PlayerMobile)m).Learning == m_Teaching );

            return true;
        }

        private SkillName m_Teaching = (SkillName)(-1);

        // 07.01.2013 :: szczaw :: vendor "wydaje" reszt� 
        public virtual int Teach( SkillName skill, Mobile m, int maxPointsToLearn, bool doTeach )
        {
            int pointsToLearn = 0;
            TeachResult res = CheckTeachSkills( skill, m, maxPointsToLearn, ref pointsToLearn, doTeach );

            switch ( res )
            {
                case TeachResult.KnowsMoreThanMe:
                {
                    Say( 501508 ); // I cannot teach thee, for thou knowest more than I!
                    break;
                }
                case TeachResult.KnowsWhatIKnow:
                {
                    Say( 501509 ); // I cannot teach thee, for thou knowest all I can teach!
                    break;
                }
                case TeachResult.NotEnoughFreePoints:
                case TeachResult.SkillNotRaisable:
                {
                    // Make sure this skill is marked to raise. If you are near the skill cap (700 points) you may need to lose some points in another skill first.
                    m.SendLocalizedMessage( 501510, "", 0x22 );
                    break;
                }
                case TeachResult.Success:
                {
                    if ( doTeach )
                    {
                        Say( 501539 ); // Let me show thee something of how this is done.
                        m.SendLocalizedMessage( 501540 ); // Your skill level increases.

                        m_Teaching = (SkillName)(-1);

                        if ( m is PlayerMobile )
                            ((PlayerMobile)m).Learning = (SkillName)(-1);
                    }
                    else
                    {
                        // I will teach thee all I know, if paid the amount in full.  The price is:
                        Say( 1019077, AffixType.Append, String.Format( " {0}", pointsToLearn ), "" );
                        Say( 1043108 ); // For less I shall teach thee less.

                        m_Teaching = skill;

                        if ( m is PlayerMobile )
                            ((PlayerMobile)m).Learning = skill;
                    }

                    return pointsToLearn;
                }
            }

            return 0;
        }
        #endregion

        public override void AggressiveAction( Mobile aggressor, bool criminal )
        {
            base.AggressiveAction( aggressor, criminal );

            OrderType ct = m_ControlOrder;

            if ( m_AI != null )
            {
                if ( !Core.ML || (ct != OrderType.Follow && ct != OrderType.Stop) )
                {
                    m_AI.OnAggressiveAction( aggressor );
                }
                else
                {
                    DebugSay( "I'm being attacked but my master told me not to fight." );
                    Warmode = false;
                    return;
                }
            }

            StopFlee();

            ForceReacquire();

            if ( !IsEnemy( aggressor ) )
            {
                Ethics.Player pl = Ethics.Player.Find( aggressor, true );

                if ( pl != null && pl.IsShielded )
                    pl.FinishShield();
            }

            if ( aggressor.ChangingCombatant && (m_bControlled || m_bSummoned) && (ct == OrderType.Come || (!Core.ML && ct == OrderType.Stay) || ct == OrderType.Stop || ct == OrderType.None || ct == OrderType.Follow) )
            {
                ControlTarget = aggressor;
                ControlOrder = OrderType.Attack;
            }
            else if ( Combatant == null && !m_bBardPacified )
            {
                Warmode = true;
                Combatant = aggressor;
            }
        }

        public override bool OnMoveOver( Mobile m )
        {
            if ( m is BaseCreature && !((BaseCreature)m).Controlled )
                return false;

            return base.OnMoveOver( m );
        }

        public virtual void AddCustomContextEntries( Mobile from, List<ContextMenuEntry> list )
        {
        }

        public virtual bool CanDrop { get { return IsBonded; } }

        public override void GetContextMenuEntries( Mobile from, List<ContextMenuEntry> list )
        {
            base.GetContextMenuEntries( from, list );

            if ( m_AI != null && Commandable )
                m_AI.GetContextMenuEntries( from, list );

            if ( m_bTamable && !m_bControlled && from.Alive )
                list.Add( new TameEntry( from, this ) );

            AddCustomContextEntries( from, list );

            if ( CanTeach && from.Alive )
            {
                Skills ourSkills = this.Skills;
                Skills theirSkills = from.Skills;

                for ( int i = 0; i < ourSkills.Length && i < theirSkills.Length; ++i )
                {
                    Skill skill = ourSkills[i];
                    Skill theirSkill = theirSkills[i];

                    if ( skill != null && theirSkill != null && skill.Base >= 60.0 && CheckTeach( skill.SkillName, from ) )
                    {
						// 18.11.2012 mortuus - NPC beda uczyc 30 skilla:
						double toTeach = 30.0;
						/*
                        double toTeach = skill.Base / 3.0;

                        if ( toTeach > 42.0 )	
                            toTeach = 42.0;
						 */

                        list.Add( new TeachEntry( (SkillName)i, this, from, ( toTeach > theirSkill.Base ) ) );
                    }
                }
            }
        }

        public override bool HandlesOnSpeech( Mobile from )
        {
            InhumanSpeech speechType = this.SpeechType;

            if ( speechType != null && (speechType.Flags & IHSFlags.OnSpeech) != 0 && from.InRange( this, 3 ) )
                return true;

            return ( m_AI != null && m_AI.HandlesOnSpeech( from ) && from.InRange( this, m_iRangePerception ) );
        }

        public override void OnSpeech( SpeechEventArgs e )
        {
            InhumanSpeech speechType = this.SpeechType;

            if ( speechType != null && speechType.OnSpeech( this, e.Mobile, e.Speech ) )
                e.Handled = true;
            else if ( !e.Handled && m_AI != null && e.Mobile.InRange( this, m_iRangePerception ) )
                m_AI.OnSpeech( e );
        }

        public override bool IsHarmfulCriminal( Mobile target )
        {
            if ( (Controlled && target == m_ControlMaster) || (Summoned && target == m_SummonMaster) )
                return false;

            if ( target is BaseCreature && ((BaseCreature)target).InitialInnocent && !((BaseCreature)target).Controlled )
                return false;

            if (false) // Nieporzadane: tylko ofiara kradziezy moze bic zlodzieja.
            {
                // Wszyscy (i ich pety) moga bic przylapanego zlodzieja
                if (target is PlayerMobile && ((PlayerMobile)target).PermaFlags.Count > 0)
                    return false;
            }

            return base.IsHarmfulCriminal( target );
        }

        public override void CriminalAction( bool message )
        {
            base.CriminalAction( message );

            if ( Controlled || Summoned )
            {
                if ( m_ControlMaster != null && m_ControlMaster.Player )
                    m_ControlMaster.CriminalAction( false );
                else if ( m_SummonMaster != null && m_SummonMaster.Player )
                    m_SummonMaster.CriminalAction( false );
            }
        }

        public override void DoHarmful( Mobile target, bool indirect )
        {
            base.DoHarmful( target, indirect );

            if ( target == this || target == m_ControlMaster || target == m_SummonMaster || (!Controlled && !Summoned) )
                return;

            List<AggressorInfo> list = this.Aggressors;

            for ( int i = 0; i < list.Count; ++i )
            {
                AggressorInfo ai = list[i];

                if ( ai.Attacker == target )
                    return;
            }

            list = this.Aggressed;

            for ( int i = 0; i < list.Count; ++i )
            {
                AggressorInfo ai = list[i];

                if ( ai.Defender == target )
                {
                    if ( m_ControlMaster != null && m_ControlMaster.Player && m_ControlMaster.CanBeHarmful( target, false ) )
                        m_ControlMaster.DoHarmful( target, true );
                    else if ( m_SummonMaster != null && m_SummonMaster.Player && m_SummonMaster.CanBeHarmful( target, false ) )
                        m_SummonMaster.DoHarmful( target, true );

                    return;
                }
            }
        }

        private static Mobile m_NoDupeGuards;

        public void ReleaseGuardDupeLock()
        {
            m_NoDupeGuards = null;
        }

        public void ReleaseGuardLock()
        {
            EndAction( typeof( GuardedRegion ) );
        }

        private DateTime m_IdleReleaseTime;

        public virtual bool CheckIdle()
        {
            if ( Combatant != null )
                return false; // in combat.. not idling

            if ( m_IdleReleaseTime > DateTime.MinValue )
            {
                // idling...

                if ( DateTime.Now >= m_IdleReleaseTime )
                {
                    m_IdleReleaseTime = DateTime.MinValue;
                    return false; // idle is over
                }

                return true; // still idling
            }

            if ( 95 > Utility.Random( 100 ) )
                return false; // not idling, but don't want to enter idle state

            m_IdleReleaseTime = DateTime.Now + TimeSpan.FromSeconds( Utility.RandomMinMax( 15, 25 ) );

            if ( Body.IsHuman )
            {
                switch ( Utility.Random( 2 ) )
                {
                    case 0: Animate( 5, 5, 1, true,  true, 1 ); break;
                    case 1: Animate( 6, 5, 1, true, false, 1 ); break;
                }    
            }
            else if ( Body.IsAnimal )
            {
                switch ( Utility.Random( 3 ) )
                {
                    case 0: Animate(  3, 3, 1, true, false, 1 ); break;
                    case 1: Animate(  9, 5, 1, true, false, 1 ); break;
                    case 2: Animate( 10, 5, 1, true, false, 1 ); break;
                }
            }
            else if ( Body.IsMonster )
            {
                switch ( Utility.Random( 2 ) )
                {
                    case 0: Animate( 17, 5, 1, true, false, 1 ); break;
                    case 1: Animate( 18, 5, 1, true, false, 1 ); break;
                }
            }

            PlaySound( GetIdleSound() );
            return true; // entered idle state
        }

        protected override void OnLocationChange( Point3D oldLocation )
        {
            Map map = this.Map;
            
            if ( !PlayerRangeSensitive || m_AI != null && map != null && map.GetSector( this.Location ).Active )
                m_AI.Activate();
            
            base.OnLocationChange( oldLocation );
        }

        public override void OnMovement( Mobile m, Point3D oldLocation )
        {
            base.OnMovement( m, oldLocation );

            if ( ReacquireOnMovement || m_Paragon )
                ForceReacquire();

            InhumanSpeech speechType = this.SpeechType;

            if ( speechType != null )
                speechType.OnMovement( this, m, oldLocation );

            /* Begin notice sound */
            if ( (!m.Hidden || m.AccessLevel == AccessLevel.Player) && m.Player && m_FightMode != FightMode.Aggressor && m_FightMode != FightMode.None && Combatant == null && !Controlled && !Summoned )
            {
                // If this creature defends itself but doesn't actively attack (animal) or
                // doesn't fight at all (vendor) then no notice sounds are played..
                // So, players are only notified of aggressive monsters

                // Monsters that are currently fighting are ignored

                // Controlled or summoned creatures are ignored

                if ( InRange( m.Location, 18 ) && !InRange( oldLocation, 18 ) )
                {
                    if ( Body.IsMonster )
                        Animate( 11, 5, 1, true, false, 1 );

                    PlaySound( GetAngerSound() );
                }
            }
            /* End notice sound */

            if ( m_NoDupeGuards == m )
                return;

            if ( !Body.IsHuman || Kills >= 5 || AlwaysMurderer || AlwaysAttackable || m.Kills < 5 || !m.InRange( Location, 12 ) || !m.Alive )
                return;

            GuardedRegion guardedRegion = (GuardedRegion) this.Region.GetRegion( typeof( GuardedRegion ) );

            if ( guardedRegion != null )
            {
                if ( !guardedRegion.IsDisabled() && guardedRegion.IsGuardCandidate( m ) && BeginAction( typeof( GuardedRegion ) ) )
                {
                    Say( 1013037 + Utility.Random( 16 ) );
                    guardedRegion.CallGuards( this.Location );

                    Timer.DelayCall( TimeSpan.FromSeconds( 5.0 ), new TimerCallback( ReleaseGuardLock ) );

                    m_NoDupeGuards = m;
                    Timer.DelayCall( TimeSpan.Zero, new TimerCallback( ReleaseGuardDupeLock ) );
                }
            }
        }


        public void AddSpellAttack( Type type )
        {
            m_arSpellAttack.Add ( type );
        }

        public void AddSpellDefense( Type type )
        {
            m_arSpellDefense.Add ( type );
        }

        public Spell GetAttackSpellRandom()
        {
            if ( m_arSpellAttack.Count > 0 )
            {
                Type type = m_arSpellAttack[Utility.Random(m_arSpellAttack.Count)];

                object[] args = {this, null};
                return Activator.CreateInstance( type, args ) as Spell;
            }
            else
            {
                return null;
            }
        }

        public Spell GetDefenseSpellRandom()
        {
            if ( m_arSpellDefense.Count > 0 )
            {
                Type type = m_arSpellDefense[Utility.Random(m_arSpellDefense.Count)];

                object[] args = {this, null};
                return Activator.CreateInstance( type, args ) as Spell;
            }
            else
            {
                return null;
            }
        }

        public Spell GetSpellSpecific( Type type )
        {
            int i;

            for( i=0; i< m_arSpellAttack.Count; i++ )
            {
                if( m_arSpellAttack[i] == type )
                {
                    object[] args = { this, null };
                    return Activator.CreateInstance( type, args ) as Spell;
                }
            }

            for ( i=0; i< m_arSpellDefense.Count; i++ )
            {
                if ( m_arSpellDefense[i] == type )
                {
                    object[] args = {this, null};
                    return Activator.CreateInstance( type, args ) as Spell;
                }            
            }

            return null;
        }

        #region Set[...]

        public void SetDamage( int val )
        {
            m_DamageMin = val;
            m_DamageMax = val;
        }

        public void SetDamage( int min, int max )
        {
            m_DamageMin = min;
            m_DamageMax = max;
        }

        public void SetHits( int val )
        {
            if ( val < 1000 && !Core.AOS )
                val = (val * 100) / 60;

            m_HitsMax = val;
            Hits = HitsMax;
        }

        public void SetHits( int min, int max )
        {
            if ( min < 1000 && !Core.AOS )
            {
                min = (min * 100) / 60;
                max = (max * 100) / 60;
            }

            m_HitsMax = Utility.RandomMinMax( min, max );
            Hits = HitsMax;
        }

        public void SetStam( int val )
        {
            m_StamMax = val;
            Stam = StamMax;
        }

        public void SetStam( int min, int max )
        {
            m_StamMax = Utility.RandomMinMax( min, max );
            Stam = StamMax;
        }

        public void SetMana( int val )
        {
            m_ManaMax = val;
            Mana = ManaMax;
        }

        public void SetMana( int min, int max )
        {
            m_ManaMax = Utility.RandomMinMax( min, max );
            Mana = ManaMax;
        }

        public void SetStr( int val )
        {
            RawStr = val;
            Hits = HitsMax;
        }

        public void SetStr( int min, int max )
        {
            RawStr = Utility.RandomMinMax( min, max );
            Hits = HitsMax;
        }

        public void SetDex( int val )
        {
            RawDex = val;
            Stam = StamMax;
        }

        public void SetDex( int min, int max )
        {
            RawDex = Utility.RandomMinMax( min, max );
            Stam = StamMax;
        }

        public void SetInt( int val )
        {
            RawInt = val;
            Mana = ManaMax;
        }

        public void SetInt( int min, int max )
        {
            RawInt = Utility.RandomMinMax( min, max );
            Mana = ManaMax;
        }

        public void SetDamageType( ResistanceType type, int min, int max )
        {
            SetDamageType( type, Utility.RandomMinMax( min, max ) );
        }

        public void SetDamageType( ResistanceType type, int val )
        {
            switch ( type )
            {
                case ResistanceType.Physical: m_PhysicalDamage = val; break;
                case ResistanceType.Fire: m_FireDamage = val; break;
                case ResistanceType.Cold: m_ColdDamage = val; break;
                case ResistanceType.Poison: m_PoisonDamage = val; break;
                case ResistanceType.Energy: m_EnergyDamage = val; break;
            }
        }

        public void SetResistance( ResistanceType type, int min, int max )
        {
            SetResistance( type, Utility.RandomMinMax( min, max ) );
        }

        public void SetResistance( ResistanceType type, int val )
        {
            switch ( type )
            {
                case ResistanceType.Physical: m_PhysicalResistance = val; break;
                case ResistanceType.Fire: m_FireResistance = val; break;
                case ResistanceType.Cold: m_ColdResistance = val; break;
                case ResistanceType.Poison: m_PoisonResistance = val; break;
                case ResistanceType.Energy: m_EnergyResistance = val; break;
            }

            UpdateResistances();
        }

        public void SetSkill( SkillName name, double val )
        {
            Skills[name].BaseFixedPoint = (int)(val * 10);

            if ( Skills[name].Base > Skills[name].Cap )
            {
                if ( Core.SE )
                    this.SkillsCap += (Skills[name].BaseFixedPoint - Skills[name].CapFixedPoint);

                Skills[name].Cap = Skills[name].Base;
            }
        }

        public void SetSkill( SkillName name, double min, double max )
        {
            int minFixed = (int)(min * 10);
            int maxFixed = (int)(max * 10);

            Skills[name].BaseFixedPoint = Utility.RandomMinMax( minFixed, maxFixed );

            if ( Skills[name].Base > Skills[name].Cap )
            {
                if ( Core.SE )
                    this.SkillsCap += (Skills[name].BaseFixedPoint - Skills[name].CapFixedPoint);

                Skills[name].Cap = Skills[name].Base;
            }
        }

        public void SetFameLevel( int level )
        {
            switch ( level )
            {
                case 1: Fame = Utility.RandomMinMax(     0,  1249 ); break;
                case 2: Fame = Utility.RandomMinMax(  1250,  2499 ); break;
                case 3: Fame = Utility.RandomMinMax(  2500,  4999 ); break;
                case 4: Fame = Utility.RandomMinMax(  5000,  9999 ); break;
                case 5: Fame = Utility.RandomMinMax( 10000, 10000 ); break;
            }
        }

        public void SetKarmaLevel( int level )
        {
            switch ( level )
            {
                case 0: Karma = -Utility.RandomMinMax(     0,   624 ); break;
                case 1: Karma = -Utility.RandomMinMax(   625,  1249 ); break;
                case 2: Karma = -Utility.RandomMinMax(  1250,  2499 ); break;
                case 3: Karma = -Utility.RandomMinMax(  2500,  4999 ); break;
                case 4: Karma = -Utility.RandomMinMax(  5000,  9999 ); break;
                case 5: Karma = -Utility.RandomMinMax( 10000, 10000 ); break;
            }
        }

        #endregion

        public static void Cap( ref int val, int min, int max )
        {
            if ( val < min )
                val = min;
            else if ( val > max )
                val = max;
        }

        #region Pack & Loot
        public void PackPotion()
        {
            PackItem( Loot.RandomPotion() );
        }

        public void PackNecroScroll( int index )
        {
            if ( !Core.AOS || 0.05 <= Utility.RandomDouble() )
                return;

            PackItem( Loot.Construct( Loot.NecromancyScrollTypes, index ) );
        }

        public void PackScroll( int minCircle, int maxCircle )
        {
            PackScroll( Utility.RandomMinMax( minCircle, maxCircle ) );
        }

        public void PackScroll( int circle )
        {
            int min = (circle - 1) * 8;

            PackItem( Loot.RandomScroll( min, min + 7, SpellbookType.Regular ) );
        }

        public void PackMagicItems( int minLevel, int maxLevel )
        {
            PackMagicItems( minLevel, maxLevel, 0.30, 0.15 );
        }

        public void PackMagicItems( int minLevel, int maxLevel, double armorChance, double weaponChance )
        {
            if ( !PackArmor( minLevel, maxLevel, armorChance ) )
                PackWeapon( minLevel, maxLevel, weaponChance );
        }

        public virtual void DropBackpack()
        {
            if ( Backpack != null )
            {
                if( Backpack.Items.Count > 0 )
                {
                    Backpack b = new CreatureBackpack( Name );

                    List<Item> list = new List<Item>( Backpack.Items );
                    foreach ( Item item in list )
                    {
                        b.DropItem( item );
                    }

                    BaseHouse house = BaseHouse.FindHouseAt( this );
                    if ( house  != null )
                        b.MoveToWorld( house.BanLocation, house.Map );
                    else
                        b.MoveToWorld( Location, Map );
                }
            }
        }

        protected bool m_Spawning;
        protected int m_KillersLuck;

        // 09.10.2012 :: zombie           
        public Skill MaxMeleeSkill
        {
            get
            {
                SkillName[] meleeSkillNames = new SkillName[]
                { 
                    SkillName.Wrestling, 
                    SkillName.Macing, 
                    SkillName.Fencing, 
                    SkillName.Swords, 
                    SkillName.Archery,
                };

                Skill skillMax = Skills[ meleeSkillNames[ 0 ] ];

                foreach( SkillName msn in meleeSkillNames )
                {
                    Skill skill = Skills[ msn ];

                    if ( skill.Base > skillMax.Base )
                        skillMax = skill;
                }
                
                return skillMax;
            }
        }

        public double AvgRes
        {
            get { return ( PhysicalResistance + FireResistance + ColdResistance + PoisonResistance + EnergyResistance ) / 5; }
        }
        // zombie

        public virtual void GenerateLoot( bool spawning )
        {
            if (m_Allured) 
                return;

            m_Spawning = spawning;

            if ( !spawning )
                m_KillersLuck = LootPack.GetLuckChanceForKiller( this );

            LootPack lootPack = LootPack.GetLootPack( this, spawning );
            
            if( lootPack != null )
                AddLoot( lootPack );
            
            //GenerateLoot();

            /*
            if ( m_Paragon )
            {
                if ( Fame < 1250 )
                    AddLoot( LootPack.Meager );
                else if ( Fame < 2500 )
                    AddLoot( LootPack.Average );
                else if ( Fame < 5000 )
                    AddLoot( LootPack.Rich );
                else if ( Fame < 10000 )
                    AddLoot( LootPack.FilthyRich );
                else
                    AddLoot( LootPack.UltraRich );
            }
            */
            m_Spawning = false;
            //m_KillersLuck = 0;    // ta wartosc mi sie przyda przy definiowaniu levelu mapy skarbu w onBeforeDeath()
        }

        public virtual void GenerateLoot()
        {
        }

        public virtual void AddLoot( LootPack pack, int amount )
        {
            for ( int i = 0; i < amount; ++i )
                AddLoot( pack );
        }

        public virtual void AddLoot( LootPack pack )
        {
            if ( Summoned )
                return;

            Container backpack = Backpack;

            if ( backpack == null )
            {
                backpack = new Backpack();

                backpack.Movable = false;

                AddItem( backpack );
            }

            pack.Generate( this, backpack, m_Spawning, m_KillersLuck );
        }

        public bool PackArmor( int minLevel, int maxLevel )
        {
            return PackArmor( minLevel, maxLevel, 1.0 );
        }

        public bool PackArmor( int minLevel, int maxLevel, double chance )
        {
            if ( chance <= Utility.RandomDouble() )
                return false;

            Cap( ref minLevel, 0, 5 );
            Cap( ref maxLevel, 0, 5 );

            if ( Core.AOS )
            {
                Item item = Loot.RandomArmorOrShieldOrJewelry();

                if ( item == null )
                    return false;

                int attributeCount, min, max;
                GetRandomAOSStats( minLevel, maxLevel, out attributeCount, out min, out max );

                if ( item is BaseArmor )
                    BaseRunicTool.ApplyAttributesTo( (BaseArmor)item, attributeCount, min, max );
                else if ( item is BaseJewel )
                    BaseRunicTool.ApplyAttributesTo( (BaseJewel)item, attributeCount, min, max );

                PackItem( item );
            }
            else
            {
                BaseArmor armor = Loot.RandomArmorOrShield();

                if ( armor == null )
                    return false;

                armor.ProtectionLevel = (ArmorProtectionLevel)RandomMinMaxScaled( minLevel, maxLevel );
                armor.Durability = (ArmorDurabilityLevel)RandomMinMaxScaled( minLevel, maxLevel );

                PackItem( armor );
            }

            return true;
        }

        public static void GetRandomAOSStats( int minLevel, int maxLevel, out int attributeCount, out int min, out int max )
        {
            int v = RandomMinMaxScaled( minLevel, maxLevel );

            if ( v >= 5 )
            {
                attributeCount = Utility.RandomMinMax( 2, 6 );
                min = 20; max = 70;
            }
            else if ( v == 4 )
            {
                attributeCount = Utility.RandomMinMax( 2, 4 );
                min = 20; max = 50;
            }
            else if ( v == 3 )
            {
                attributeCount = Utility.RandomMinMax( 2, 3 );
                min = 20; max = 40;
            }
            else if ( v == 2 )
            {
                attributeCount = Utility.RandomMinMax( 1, 2 );
                min = 10; max = 30;
            }
            else
            {
                attributeCount = 1;
                min = 10; max = 20;
            }
        }

        public static int RandomMinMaxScaled( int min, int max )
        {
            if ( min == max )
                return min;

            if ( min > max )
            {
                int hold = min;
                min = max;
                max = hold;
            }

            /* Example:
             *    min: 1
             *    max: 5
             *  count: 5
             * 
             * total = (5*5) + (4*4) + (3*3) + (2*2) + (1*1) = 25 + 16 + 9 + 4 + 1 = 55
             * 
             * chance for min+0 : 25/55 : 45.45%
             * chance for min+1 : 16/55 : 29.09%
             * chance for min+2 :  9/55 : 16.36%
             * chance for min+3 :  4/55 :  7.27%
             * chance for min+4 :  1/55 :  1.81%
             */

            int count = max - min + 1;
            int total = 0, toAdd = count;

            for ( int i = 0; i < count; ++i, --toAdd )
                total += toAdd*toAdd;

            int rand = Utility.Random( total );
            toAdd = count;

            int val = min;

            for ( int i = 0; i < count; ++i, --toAdd, ++val )
            {
                rand -= toAdd*toAdd;

                if ( rand < 0 )
                    break;
            }

            return val;
        }

        public bool PackSlayer()
        {
            return PackSlayer( 0.05 );
        }

        public bool PackSlayer( double chance )
        {
            if ( chance <= Utility.RandomDouble() )
                return false;

            if ( Utility.RandomBool() )
            {
                BaseInstrument instrument = Loot.RandomInstrument();

                if ( instrument != null )
                {
                    instrument.Slayer = SlayerGroup.GetLootSlayerType( GetType() );
                    PackItem( instrument );
                }
            }
            else if ( !Core.AOS )
            {
                BaseWeapon weapon = Loot.RandomWeapon();

                if ( weapon != null )
                {
                    weapon.Slayer = SlayerGroup.GetLootSlayerType( GetType() );
                    PackItem( weapon );
                }
            }

            return true;
        }

        public bool PackWeapon( int minLevel, int maxLevel )
        {
            return PackWeapon( minLevel, maxLevel, 1.0 );
        }

        public bool PackWeapon( int minLevel, int maxLevel, double chance )
        {
            if ( chance <= Utility.RandomDouble() )
                return false;

            Cap( ref minLevel, 0, 5 );
            Cap( ref maxLevel, 0, 5 );

            if ( Core.AOS )
            {
                Item item = Loot.RandomWeaponOrJewelry();

                if ( item == null )
                    return false;

                int attributeCount, min, max;
                GetRandomAOSStats( minLevel, maxLevel, out attributeCount, out min, out max );

                if ( item is BaseWeapon )
                    BaseRunicTool.ApplyAttributesTo( (BaseWeapon)item, attributeCount, min, max );
                else if ( item is BaseJewel )
                    BaseRunicTool.ApplyAttributesTo( (BaseJewel)item, attributeCount, min, max );

                PackItem( item );
            }
            else
            {
                BaseWeapon weapon = Loot.RandomWeapon();

                if ( weapon == null )
                    return false;

                if ( 0.05 > Utility.RandomDouble() )
                    weapon.Slayer = SlayerName.Silver;

                weapon.DamageLevel = (WeaponDamageLevel)RandomMinMaxScaled( minLevel, maxLevel );
                weapon.AccuracyLevel = (WeaponAccuracyLevel)RandomMinMaxScaled( minLevel, maxLevel );
                weapon.DurabilityLevel = (WeaponDurabilityLevel)RandomMinMaxScaled( minLevel, maxLevel );

                PackItem( weapon );
            }

            return true;
        }

        public void PackGold( int amount )
        {
            if ( amount > 0 )
                PackItem( new Gold( amount ) );
        }

        public void PackGold( int min, int max )
        {
            PackGold( Utility.RandomMinMax( min, max ) );
        }

        public void PackStatue( int min, int max )
        {
            PackStatue( Utility.RandomMinMax( min, max ) );
        }

        public void PackStatue( int amount )
        {
            for ( int i = 0; i < amount; ++i )
                PackStatue();
        }

        public void PackStatue()
        {
            PackItem( Loot.RandomStatue() );
        }

        public void PackGem()
        {
            PackGem( 1 );
        }

        public void PackGem( int min, int max )
        {
            PackGem( Utility.RandomMinMax( min, max ) );
        }

        public void PackGem( int amount )
        {
            if ( amount <= 0 )
                return;

            Item gem = Loot.RandomGem();

            gem.Amount = amount;

            PackItem( gem );
        }

        public void PackNecroReg( int min, int max )
        {
            PackNecroReg( Utility.RandomMinMax( min, max ) );
        }

        public void PackNecroReg( int amount )
        {
            for ( int i = 0; i < amount; ++i )
                PackNecroReg();
        }

        public void PackNecroReg()
        {
            if ( !Core.AOS )
                return;

            PackItem( Loot.RandomNecromancyReagent() );
        }

        public void PackReg( int min, int max )
        {
            PackReg( Utility.RandomMinMax( min, max ) );
        }

        public void PackReg( int amount )
        {
            if ( amount <= 0 )
                return;

            Item reg = Loot.RandomReagent();

            reg.Amount = amount;

            PackItem( reg );
        }

        public void PackItem( Item item )
        {
            if ( Summoned || item == null )
            {
                if ( item != null )
                    item.Delete();

                return;
            }

            Container pack = Backpack;

            if ( pack == null )
            {
                pack = new Backpack();

                pack.Movable = false;

                AddItem( pack );
            }

            if ( !item.Stackable || !pack.TryDropItem( this, item, false ) ) // try stack
                pack.DropItem( item ); // failed, drop it anyway
        }
        #endregion

        public override void OnDoubleClick( Mobile from )
        {
            if ( from.AccessLevel >= AccessLevel.GameMaster && !Body.IsHuman )
            {
                Container pack = this.Backpack;

                if ( pack != null )
                    pack.DisplayTo( from );
            }

            if ( this.DeathAdderCharmable && from.CanBeHarmful( this, false ) )
            {
                DeathAdder da = Spells.Necromancy.SummonFamiliarSpell.Table[from] as DeathAdder;

                if ( da != null && !da.Deleted )
                {
                    from.SendAsciiMessage( "You charm the snake.  Select a target to attack." );
                    from.Target = new DeathAdderCharmTarget( this );
                }
            }

            base.OnDoubleClick( from );
        }

        private class DeathAdderCharmTarget : Target
        {
            private BaseCreature m_Charmed;

            public DeathAdderCharmTarget( BaseCreature charmed ) : base( -1, false, TargetFlags.Harmful )
            {
                m_Charmed = charmed;
            }

            protected override void OnTarget( Mobile from, object targeted )
            {
                if ( !m_Charmed.DeathAdderCharmable || m_Charmed.Combatant != null || !from.CanBeHarmful( m_Charmed, false ) )
                    return;

                DeathAdder da = Spells.Necromancy.SummonFamiliarSpell.Table[from] as DeathAdder;
                if ( da == null || da.Deleted )
                    return;

                Mobile targ = targeted as Mobile;
                if ( targ == null || !from.CanBeHarmful( targ, false ) )
                    return;

                from.RevealingAction();
                from.DoHarmful( targ, true );

                m_Charmed.Combatant = targ;

                if ( m_Charmed.AIObject != null )
                    m_Charmed.AIObject.Action = ActionType.Combat;
            }
        }

        public override void AddNameProperties( ObjectPropertyList list )
        {
            base.AddNameProperties( list );

            if ( Controlled && Commandable )
            {
                if ( Summoned )
                    list.Add( 1049646 ); // (summoned)
                else if ( IsBonded )    //Intentional difference (showing ONLY bonded when bonded instead of bonded & tame)
                    list.Add( 1049608 ); // (bonded)
                else
                    list.Add( 502006 ); // (tame)
            }
        }

        public override void OnSingleClick( Mobile from )
        {
            if ( Controlled && Commandable )
            {
                int number;

                if ( Summoned )
                    number = 1049646; // (summoned)
                else if ( IsBonded )
                    number = 1049608; // (bonded)
                else
                    number = 502006; // (tame)

                PrivateOverheadMessage( MessageType.Regular, 0x3B2, number, from.NetState );
            }

            base.OnSingleClick( from );
        }

        public virtual double TreasureMapChance{ get{ return TreasureMap.LootChance; } }
        public virtual int TreasureMapLevel{ get{ return -1; } }

        public virtual bool IgnoreYoungProtection { get { return false; } }

        public override bool OnBeforeDeath()
        {
            if (!Summoned && !NoKillAwards && !m_HasGeneratedLoot)
            {
                m_HasGeneratedLoot = true;
                GenerateLoot(false);    // ta funkcja inicjalizuje m_KillersLuck
                ChaosChestQuest.OnBeforeDeath(this);
                SoulLantern.OnBeforeDeath(this);
            }

            int treasureLevel = TreasureMapLevel;

            if (treasureLevel == 5 )
            {
                double ingeniouslyMapChance = 0.1;  // 10% szans na mapke 6 lvl
                ingeniouslyMapChance *= 1 + 0.4 * (m_KillersLuck / LootPack.MaxLuckChance); // chance *= 1.4 dla pelnego luck
                if (ingeniouslyMapChance >= Utility.RandomDouble())
                    treasureLevel = 6;
            }
            m_KillersLuck = 0;  // przywracamy do zera - na wszelki wypadek

            if ( treasureLevel == 1 && this.Map == Map.Trammel && TreasureMap.IsInHavenIsland( this ) )
            {
                Mobile killer = this.LastKiller;

                if ( killer is BaseCreature )
                    killer = ((BaseCreature)killer).GetMaster();

                // if ( killer is PlayerMobile && ((PlayerMobile)killer).Young )
                //     treasureLevel = 0;
            }

            if ( !Summoned && !NoKillAwards && !IsBonded && treasureLevel >= 0 )
            {
                if ( m_Paragon && Paragon.ChestChance > Utility.RandomDouble() )
                    PackItem( new ParagonChest( this.Name, treasureLevel ) );
                else if ( (Map == Map.Felucca || Map == Map.Trammel) && TreasureMap.LootChance >= Utility.RandomDouble() )
                    PackItem(new TreasureMap(treasureLevel, Map.Felucca));
                    //PackItem( new TreasureMap( treasureLevel, Map ) );
            }

            if ( !NoKillAwards && Region.IsPartOf( "Doom" ) )
            {
                int bones = Engines.Quests.Doom.TheSummoningQuest.GetDaemonBonesFor( this );

                if ( bones > 0 )
                    PackItem( new DaemonBone( bones ) );
            }

            if ( IsAnimatedDead )
                Effects.SendLocationEffect( Location, Map, 0x3728, 13, 1, 0x461, 4 );

            InhumanSpeech speechType = this.SpeechType;

            if ( speechType != null )
                speechType.OnDeath( this );

            if ( m_ReceivedHonorContext != null )
                m_ReceivedHonorContext.OnTargetKilled();
            
            return base.OnBeforeDeath();
        }

        private bool m_NoKillAwards;

        public bool NoKillAwards
        {
            get{ return m_NoKillAwards; }
            set{ m_NoKillAwards = value; }
        }

        public int ComputeBonusDamage( List<DamageEntry> list, Mobile m )
        {
            int bonus = 0;

            for ( int i = list.Count - 1; i >= 0; --i )
            {
                DamageEntry de = list[i];

                if ( de.Damager == m || !(de.Damager is BaseCreature) )
                    continue;

                BaseCreature bc = (BaseCreature)de.Damager;
                Mobile master = null;

                master = bc.GetMaster();

                if ( master == m )
                    bonus += de.DamageGiven;
            }

            return bonus;
        }

        public Mobile GetMaster()
        {
            if ( Controlled && ControlMaster != null )
                return ControlMaster;
            else if ( Summoned && SummonMaster != null )
                return SummonMaster;

            return null;
        }

        private class FKEntry
        {
            public Mobile m_Mobile;
            public int m_Damage;

            public FKEntry( Mobile m, int damage )
            {
                m_Mobile = m;
                m_Damage = damage;
            }
        }

		// 20.08.2014 mortuus: funkcja GetDamageList() generuje liste postaci, ktore zadaly dmg potworowi.
		//                     Standardowo lista generowana byla jedynie wewnatrz funkcji definiujacej looting rights.
		//                     Od teraz mozna bedzie utworzyc liste rowniez w innych momentach (np. przy definiowaniu
        //                     z ktorych postaci brany jest Luck wplywajacy na loot).
		public static List<DamageStore> GetDamageList( List<DamageEntry> damageEntries )
		{
            List<DamageStore> rights = new List<DamageStore>();

            for ( int i = damageEntries.Count - 1; i >= 0; --i )
            {
                if ( i >= damageEntries.Count )
                    continue;

                DamageEntry de = damageEntries[i];

                if ( de.HasExpired )
                {
                    damageEntries.RemoveAt( i );
                    continue;
                }

                int damage = de.DamageGiven;

                List<DamageEntry> respList = de.Responsible;

                if ( respList != null )
                {
                    for ( int j = 0; j < respList.Count; ++j )
                    {
                        DamageEntry subEntry = respList[j];
                        Mobile master = subEntry.Damager;

                        if ( master == null || master.Deleted || !master.Player )
                            continue;

                        bool needNewSubEntry = true;

                        for ( int k = 0; needNewSubEntry && k < rights.Count; ++k )
                        {
                            DamageStore ds = rights[k];

                            if ( ds.m_Mobile == master )
                            {
                                ds.m_Damage += subEntry.DamageGiven;
                                needNewSubEntry = false;
                            }
                        }

                        if ( needNewSubEntry )
                            rights.Add( new DamageStore( master, subEntry.DamageGiven ) );

                        damage -= subEntry.DamageGiven;
                    }
                }

                Mobile m = de.Damager;

                if ( m == null || m.Deleted || !m.Player )
                    continue;

                if ( damage <= 0 )
                    continue;

                bool needNewEntry = true;

                for ( int j = 0; needNewEntry && j < rights.Count; ++j )
                {
                    DamageStore ds = rights[j];

                    if ( ds.m_Mobile == m )
                    {
                        ds.m_Damage += damage;
                        needNewEntry = false;
                    }
                }

                if ( needNewEntry )
                    rights.Add( new DamageStore( m, damage ) );
            }

			return rights;
		}

        public static List<DamageStore> GetLootingRights( List<DamageEntry> damageEntries, int hitsMax )
        {
            return GetLootingRights( damageEntries, hitsMax, false );
        }

        public static List<DamageStore> GetLootingRights( List<DamageEntry> damageEntries, int hitsMax, bool partyAsIndividual )
        {
            // 20.08.2014 mortuus: tworzenie listy postaci zadajacych dmg mobowi przeniesione do osobnej funkcji GetDamageList()
            List<DamageStore> rights = GetDamageList( damageEntries );

            if ( rights.Count > 0 )
            {
                rights[0].m_Damage = (int)(rights[0].m_Damage * 1.25);    //This would be the first valid person attacking it.  Gets a 25% bonus.  Per 1/19/07 Five on Friday

                if ( rights.Count > 1 )
                    rights.Sort();            //Sort by damage

                int topDamage = rights[0].m_Damage;
                int minDamage;

                if ( hitsMax >= 3000 )
                    minDamage = topDamage / 16;
                else if ( hitsMax >= 1000 )
                    minDamage = topDamage / 8;
                else if ( hitsMax >= 200 )
                    minDamage = topDamage / 4;
                else
                    minDamage = topDamage / 2;

                for ( int i = 0; i < rights.Count; ++i )
                {
                    DamageStore ds = rights[i];

                    ds.m_HasRight = ( ds.m_Damage >= minDamage );
                }
            }

            return rights;
        }

        #region Mondain's Legacy
        private bool m_Allured;

        [CommandProperty(AccessLevel.GameMaster)]
        public bool Allured {
            get { return m_Allured; }
            set {
                m_Allured = value;

                if (value && Backpack != null) {
                    ColUtility.SafeDelete(Backpack.Items);
                }
            }
        }
        #endregion

        public virtual void OnKilledBy( Mobile mob )
        {
            if ( m_Paragon && Paragon.CheckArtifactChance( mob, this ) )
                Paragon.GiveArtifactTo( mob );
        }

        public override void OnDeath( Container c )
        {
            MeerMage.StopEffect( this, false );

            if ( IsBonded )
            {
                int sound = this.GetDeathSound();

                if ( sound >= 0 )
                    Effects.PlaySound( this, this.Map, sound );

                Warmode = false;

                Poison = null;
                Combatant = null;

                Hits = 0;
                Stam = 0;
                Mana = 0;

                IsDeadPet = true;
                ControlTarget = ControlMaster;
                ControlOrder = OrderType.Follow;

                ProcessDeltaQueue();
                SendIncomingPacket();
                SendIncomingPacket();

                List<AggressorInfo> aggressors = this.Aggressors;

                for ( int i = 0; i < aggressors.Count; ++i )
                {
                    AggressorInfo info = aggressors[i];

                    if ( info.Attacker.Combatant == this )
                        info.Attacker.Combatant = null;
                }

                List<AggressorInfo> aggressed = this.Aggressed;

                for ( int i = 0; i < aggressed.Count; ++i )
                {
                    AggressorInfo info = aggressed[i];

                    if ( info.Defender.Combatant == this )
                        info.Defender.Combatant = null;
                }

                Mobile owner = this.ControlMaster;

                if ( owner == null || owner.Deleted || owner.Map != this.Map || !owner.InRange( this, 12 ) || !this.CanSee( owner ) || !this.InLOS( owner ) )
                {
                    if ( this.OwnerAbandonTime == DateTime.MinValue )
                        this.OwnerAbandonTime = DateTime.Now;
                }
                else
                {
                    this.OwnerAbandonTime = DateTime.MinValue;
                }

                GiftOfLifeSpell.HandleDeath( this );

                CheckStatTimers();
            }
            else
            {
                if ( !Summoned && !m_NoKillAwards )
                {
                    int totalFame = Fame / 100;
                    int totalKarma = -Karma / 100;

                    List<DamageStore> list = GetLootingRights( this.DamageEntries, this.HitsMax );
                    
                    bool givenQuestKill = false;
                    bool givenFactionKill = false;
                    bool givenToTKill = false;

                    for ( int i = 0; i < list.Count; ++i )
                    {
                        DamageStore ds = list[i];
                        
                        if ( !ds.m_HasRight )
                            continue;

                        Titles.AwardFame( ds.m_Mobile, totalFame, true );
                        Titles.AwardKarma( ds.m_Mobile, totalKarma, true );
                        // modification to support XmlQuest Killtasks
                        XmlQuest.RegisterKill( this, ds.m_Mobile);

                        OnKilledBy( ds.m_Mobile );

                        if ( !givenFactionKill )
                        {
                            givenFactionKill = true;
                            Faction.HandleDeath( this, ds.m_Mobile );
                        }

                        if( !givenToTKill )
                        {
                            givenToTKill = true;
                            TreasuresOfTokuno.HandleKill( this, ds.m_Mobile );
                        }

                        if ( givenQuestKill )
                            continue;

                        PlayerMobile pm = ds.m_Mobile as PlayerMobile;

                        if ( pm != null )
                        {
                            QuestSystem qs = pm.Quest;

                            if ( qs != null )
                            {
                                qs.OnKill( this, c );
                                givenQuestKill = true;
                            }
                        }
                    }
                }

                base.OnDeath( c );

                if ( DeleteCorpseOnDeath )
                    c.Delete();
            }
        }

        /* To save on cpu usage, RunUO creatures only reacquire creatures under the following circumstances:
         *  - 10 seconds have elapsed since the last time it tried
         *  - The creature was attacked
         *  - Some creatures, like dragons, will reacquire when they see someone move
         * 
         * This functionality appears to be implemented on OSI as well
         */

        private DateTime m_NextReacquireTime;

        public DateTime NextReacquireTime{ get{ return m_NextReacquireTime; } set{ m_NextReacquireTime = value; } }

        // 23.06.2012 :: zombie :: czas reakcji mobow, 10 -> 0
        public virtual TimeSpan ReacquireDelay{ get{ return TimeSpan.FromSeconds( 0.0 ); } }
        // zombie
        public virtual bool ReacquireOnMovement{ get{ return false; } }

        public void ForceReacquire()
        {
            m_NextReacquireTime = DateTime.MinValue;
        }

        public override void OnDelete()
        {
            SetControlMaster( null );
            SummonMaster = null;

            if ( m_ReceivedHonorContext != null )
                m_ReceivedHonorContext.Cancel();

            base.OnDelete();
        }

        public override bool CanBeHarmful( Mobile target, bool message, bool ignoreOurBlessedness )
        {
            if ( target is BaseFactionGuard )
                return false;

            if ( (target is BaseVendor && ((BaseVendor)target).IsInvulnerable) || target is PlayerVendor || target is TownCrier )
            {
                if ( message )
                {
                    if ( target.Title == null )
                        SendMessage( "{0} the vendor cannot be harmed.", target.Name );
                    else
                        SendMessage( "{0} {1} cannot be harmed.", target.Name, target.Title );
                }

                return false;
            }

            return base.CanBeHarmful( target, message, ignoreOurBlessedness );
        }

        public override bool CanBeRenamedBy( Mobile from )
        {
            bool ret = base.CanBeRenamedBy( from );

            if ( Controlled && from == ControlMaster && !from.Region.IsPartOf( typeof( JailRegion ) ) )
                ret = true;

            return ret;
        }

        public bool SetControlMaster( Mobile m )
        {
            if ( m == null )
            {
                ControlMaster = null;
                Controlled = false;
                Allured = false;
                ControlTarget = null;
                ControlOrder = OrderType.None;
                Guild = null;

                Delta( MobileDelta.Noto );
            }
            else
            {
                SpawnEntry se = this.Spawner as SpawnEntry;
                if ( se != null && se.UnlinkOnTaming )
                {
                    this.Spawner.Remove( this );
                    this.Spawner = null;
                }

                if ( m.Followers + ControlSlots > m.FollowersMax )
                {
                    m.SendLocalizedMessage( 1049607 ); // You have too many followers to control that creature.
                    return false;
                }

                CurrentWayPoint = null;//so tamed animals don't try to go back
            
                ControlMaster = m;
                Controlled = true;
                ControlTarget = null;
                ControlOrder = OrderType.Come;
                Guild = null;

                Delta( MobileDelta.Noto );
            }

            return true;
        }

        public override void OnRegionChange( Region Old, Region New )
        {
            base.OnRegionChange( Old, New );

            if ( this.Controlled )
            {
                SpawnEntry se = this.Spawner as SpawnEntry;

                if ( se != null && !se.UnlinkOnTaming && ( New == null || !New.AcceptsSpawnsFrom( se.Region ) ) )
                {
                    this.Spawner.Remove( this );
                    this.Spawner = null;
                }
            }
        }

        public virtual double GetDispelDifficulty() {
            double dif = DispelDifficulty;
            if (SummonMaster != null)
                dif += ArcaneEmpowermentSpell.GetDispelBonus(SummonMaster);
            return dif;
        }

        private static bool m_Summoning;

        public static bool Summoning
        {
            get{ return m_Summoning; }
            set{ m_Summoning = value; }
        }

        public static bool Summon( BaseCreature creature, Mobile caster, Point3D p, int sound, TimeSpan duration )
        {
            return Summon( creature, true, caster, p, sound, duration );
        }

        public static bool Summon( BaseCreature creature, bool controlled, Mobile caster, Point3D p, int sound, TimeSpan duration )
        {
            if ( caster.Followers + creature.ControlSlots > caster.FollowersMax )
            {
                caster.SendLocalizedMessage( 1049645 ); // You have too many followers to summon that creature.
                creature.Delete();
                return false;
            }

            m_Summoning = true;

            if ( controlled )
                creature.SetControlMaster( caster );

            creature.RangeHome = 10;
            creature.Summoned = true;

            creature.SummonMaster = caster;

            Container pack = creature.Backpack;

            if ( pack != null )
            {
                for ( int i = pack.Items.Count - 1; i >= 0; --i )
                {
                    if ( i >= pack.Items.Count )
                        continue;

                    pack.Items[i].Delete();
                }
            }

            creature.SetHits(
                (int)Math.Floor(creature.HitsMax * (1 + ArcaneEmpowermentSpell.GetSpellBonus(caster, false) / 100.0)));

            if (duration != TimeSpan.Zero)
            {
                new UnsummonTimer(caster, creature, duration).Start();
                creature.m_SummonEnd = DateTime.Now + duration;
            }

            creature.MoveToWorld( p, caster.Map );

            Effects.PlaySound( p, creature.Map, sound );

            m_Summoning = false;

            return true;
        }

        private static bool EnableRummaging = true;

        private const double ChanceToRummage = 0.5; // 50%

        private const double MinutesToNextRummageMin = 1.0;
        private const double MinutesToNextRummageMax = 4.0;

        private const double MinutesToNextChanceMin = 0.25;
        private const double MinutesToNextChanceMax = 0.75;

        private DateTime m_NextRummageTime;

        public virtual bool CanBreath { get { return HasBreath && !Summoned; } }
        public virtual bool IsDispellable { get { return Summoned && !IsAnimatedDead; } }


        #region Animate Dead
        public virtual bool CanAnimateDead { get { return false; } }
        public virtual double AnimateChance { get { return 0.05; } }
        public virtual int AnimateScalar { get { return 50; } }
        public virtual TimeSpan AnimateDelay { get { return TimeSpan.FromSeconds(10); } }
        public virtual BaseCreature Animates { get { return null; } }

        private DateTime m_NextAnimateDead = DateTime.Now;

        public virtual void AnimateDead()
        {
            Corpse best = null;

            foreach (Item item in Map.GetItemsInRange(Location, 12))
            {
                Corpse c = null;

                if (item is Corpse)
                    c = (Corpse)item;
                else
                    continue;

                if (c.ItemID != 0x2006 || c.Channeled || c.Owner.GetType() == typeof(PlayerMobile) || c.Owner.GetType() == null || (c.Owner != null && c.Owner.Fame < 100) || ((c.Owner != null) && (c.Owner is BaseCreature) && (((BaseCreature)c.Owner).Summoned || ((BaseCreature)c.Owner).IsBonded)))
                    continue;

                best = c;
                break;
            }

            if (best != null)
            {
                BaseCreature animated = Animates;

                if (animated != null)
                {
                    animated.Tamable = false;
                    animated.MoveToWorld(best.Location, Map);
                    Scale(animated, AnimateScalar);
                    Effects.PlaySound(best.Location, Map, 0x1FB);
                    Effects.SendLocationParticles(EffectItem.Create(best.Location, Map, EffectItem.DefaultDuration), 0x3789, 1, 40, 0x3F, 3, 9907, 0);
                }

                best.ProcessDelta();
                best.SendRemovePacket();
                best.ItemID = Utility.Random(0xECA, 9); // bone graphic
                best.Hue = 0;
                best.ProcessDelta();
            }

            m_NextAnimateDead = DateTime.Now + AnimateDelay;
        }

        public static void Scale(BaseCreature bc, int scalar)
        {
            int toScale;

            toScale = bc.RawStr;
            bc.RawStr = AOS.Scale(toScale, scalar);

            toScale = bc.HitsMaxSeed;

            if (toScale > 0)
                bc.HitsMaxSeed = AOS.Scale(toScale, scalar);

            bc.Hits = bc.Hits; // refresh hits
        }
        #endregion
    
        #region Area Poison
        public virtual bool CanAreaPoison { get { return false; } }
        public virtual Poison HitAreaPoison { get { return Poison.Deadly; } }
        public virtual int AreaPoisonRange { get { return 10; } }
        public virtual double AreaPosionChance { get { return 0.4; } }
        public virtual TimeSpan AreaPoisonDelay { get { return TimeSpan.FromSeconds(8); } }

        private DateTime m_NextAreaPoison = DateTime.Now;

        public virtual void AreaPoison()
        {
            List<Mobile> targets = new List<Mobile>();

            if (Map != null)
            {
                IPooledEnumerable eable = GetMobilesInRange(AreaDamageRange);
                foreach (Mobile m in eable)
                {
                    if (this != m && SpellHelper.ValidIndirectTarget(this, m) && CanBeHarmful(m, false) && (!Core.AOS || InLOS(m)))
                    {
                        if (m is BaseCreature && ((BaseCreature)m).Controlled)
                            targets.Add(m);
                        else if (m.Player)
                            targets.Add(m);
                    }
                }
                eable.Free();
            }

            for (int i = 0; i < targets.Count; ++i)
            {
                Mobile m = targets[i];

                m.ApplyPoison(this, HitAreaPoison);

                Effects.SendLocationParticles(EffectItem.Create(m.Location, m.Map, EffectItem.DefaultDuration), 0x36B0, 1, 14, 63, 7, 9915, 0);
                Effects.PlaySound(m.Location, m.Map, 0x229);
            }

            m_NextAreaPoison = DateTime.Now + AreaPoisonDelay;
        }
        #endregion
        
        #region Area damage
        public virtual bool CanAreaDamage { get { return false; } }
        public virtual int AreaDamageRange { get { return 10; } }
        public virtual double AreaDamageScalar { get { return 1.0; } }
        public virtual double AreaDamageChance { get { return 0.4; } }
        public virtual TimeSpan AreaDamageDelay { get { return TimeSpan.FromSeconds(8); } }

        public virtual int AreaPhysicalDamage { get { return 0; } }
        public virtual int AreaFireDamage { get { return 100; } }
        public virtual int AreaColdDamage { get { return 0; } }
        public virtual int AreaPoisonDamage { get { return 0; } }
        public virtual int AreaEnergyDamage { get { return 0; } }

        private DateTime m_NextAreaDamage = DateTime.Now;

        public virtual void AreaDamage()
        {
            List<Mobile> targets = new List<Mobile>();

            if (Map != null)
            {
                IPooledEnumerable eable = GetMobilesInRange(AreaDamageRange);
                foreach (Mobile m in eable)
                {
                    if (this != m && SpellHelper.ValidIndirectTarget(this, m) && CanBeHarmful(m, false) && (!Core.AOS || InLOS(m)))
                    {
                        if (m is BaseCreature && ((BaseCreature)m).Controlled)
                            targets.Add(m);
                        else if (m.Player)
                            targets.Add(m);
                    }
                }
                eable.Free();
            }

            for (int i = 0; i < targets.Count; ++i)
            {
                Mobile m = targets[i];

                int damage;

                if (Core.AOS)
                {
                    damage = m.Hits / 2;

                    if (!m.Player)
                        damage = Math.Max(Math.Min(damage, 100), 15);

                    damage += Utility.RandomMinMax(0, 15);
                }
                else
                {
                    damage = (m.Hits * 6) / 10;

                    if (!m.Player && damage < 10)
                        damage = 10;
                    else if (damage > 75)
                        damage = 75;
                }

                damage = (int)(damage * AreaDamageScalar);

                DoHarmful(m);
                AreaDamageEffect(m);
                SpellHelper.Damage(TimeSpan.Zero, m, this, damage, AreaPhysicalDamage, AreaFireDamage, AreaColdDamage, AreaPoisonDamage, AreaEnergyDamage);
            }

            m_NextAreaDamage = DateTime.Now + AreaDamageDelay;
        }

        public virtual void AreaDamageEffect(Mobile m)
        {
            m.FixedParticles(0x3709, 10, 30, 5052, EffectLayer.LeftFoot); // flamestrike
            m.PlaySound(0x208);
        }
        #endregion

        #region Healing
        public virtual bool CanHeal { get { return false; } }
        public virtual bool CanHealOwner { get { return false; } }
        public virtual double HealScalar { get { return 1.0; } }

        public virtual int HealSound { get { return 0x57; } }
        public virtual int HealStartRange { get { return 2; } }
        public virtual int HealEndRange { get { return RangePerception; } }
        public virtual double HealTrigger { get { return 0.78; } }
        public virtual double HealDelay { get { return 6.5; } }
        public virtual double HealInterval { get { return 0.0; } }
        public virtual bool HealFully { get { return true; } }
        public virtual double HealOwnerTrigger { get { return 0.78; } }
        public virtual double HealOwnerDelay { get { return 6.5; } }
        public virtual double HealOwnerInterval { get { return 30.0; } }
        public virtual bool HealOwnerFully { get { return false; } }

        private DateTime m_NextHealTime = DateTime.Now;
        private DateTime m_NextHealOwnerTime = DateTime.Now;
        private Timer m_HealTimer = null;

        public bool IsHealing { get { return ( m_HealTimer != null ); } }

        public virtual void HealStart( Mobile patient )
        {
            bool onSelf = ( patient == this );

            //DoBeneficial( patient );

            RevealingAction();

            if ( !onSelf )
            {
                patient.RevealingAction();
                patient.SendLocalizedMessage( 1008078, false, Name ); //  : Attempting to heal you.
            }

            double seconds = ( onSelf ? HealDelay : HealOwnerDelay ) + ( patient.Alive ? 0.0 : 5.0 );

            m_HealTimer = Timer.DelayCall( TimeSpan.FromSeconds( seconds ), new TimerStateCallback( Heal_Callback ), patient );
        }

        private void Heal_Callback( object state )
        {
            if ( state is Mobile )
                Heal( (Mobile)state );
        }

        public virtual void Heal( Mobile patient )
        {
            if ( !Alive || this.Map == Map.Internal || !CanBeBeneficial( patient, true, true ) || patient.Map != this.Map || !InRange( patient, HealEndRange ) )
            {
                StopHeal();
                return;
            }

            bool onSelf = ( patient == this );

            if ( !patient.Alive )
            {
            }
            else if ( patient.Poisoned )
            {
                int poisonLevel = patient.Poison.Level;

                double healing = Skills.Healing.Value;
                double anatomy = Skills.Anatomy.Value;
                double chance = ( healing - 30.0 ) / 50.0 - poisonLevel * 0.1;

                if ( ( healing >= 60.0 && anatomy >= 60.0 ) && chance > Utility.RandomDouble() )
                {
                    if ( patient.CurePoison( this ) )
                    {
                        patient.SendLocalizedMessage( 1010059 ); // You have been cured of all poisons.

                        CheckSkill( SkillName.Healing, 0.0, 60.0 + poisonLevel * 10.0 ); // TODO: Verify formula
                        CheckSkill( SkillName.Anatomy, 0.0, 100.0 );
                    }
                }
            }
            else if ( BleedAttack.IsBleeding( patient ) )
            {
                patient.SendLocalizedMessage( 1060167 ); // The bleeding wounds have healed, you are no longer bleeding!
                BleedAttack.EndBleed( patient, false );
            }
            else
            {
                double healing = Skills.Healing.Value;
                double anatomy = Skills.Anatomy.Value;
                double chance = ( healing + 10.0 ) / 100.0;

                if ( chance > Utility.RandomDouble() )
                {
                    double min, max;

                    min = ( anatomy / 10.0 ) + ( healing / 6.0 ) + 4.0;
                    max = ( anatomy / 8.0 ) + ( healing / 3.0 ) + 4.0;

                    if ( onSelf )
                        max += 10;

                    double toHeal = min + ( Utility.RandomDouble() * ( max - min ) );

                    toHeal *= HealScalar;

                    patient.Heal( (int)toHeal );

                    CheckSkill( SkillName.Healing, 0.0, 90.0 );
                    CheckSkill( SkillName.Anatomy, 0.0, 100.0 );
                }
            }

            HealEffect( patient );

            StopHeal();

            if ( ( onSelf && HealFully && Hits >= HealTrigger * HitsMax && Hits < HitsMax ) || ( !onSelf && HealOwnerFully && patient.Hits >= HealOwnerTrigger * patient.HitsMax && patient.Hits < patient.HitsMax ) )
                HealStart( patient );
        }

        public virtual void StopHeal()
        {
            if ( m_HealTimer != null )
                m_HealTimer.Stop();

            m_HealTimer = null;
        }

        public virtual void HealEffect( Mobile patient )
        {
            patient.PlaySound( HealSound );
        }

        #endregion
        public virtual void OnThink()
        {
            if ( EnableRummaging && CanRummageCorpses && !Summoned && !Controlled && DateTime.Now >= m_NextRummageTime )
            {
                double min, max;

                if ( ChanceToRummage > Utility.RandomDouble() && Rummage() )
                {
                    min = MinutesToNextRummageMin;
                    max = MinutesToNextRummageMax;
                }
                else
                {
                    min = MinutesToNextChanceMin;
                    max = MinutesToNextChanceMax;
                }

                double delay = min + (Utility.RandomDouble() * (max - min));
                m_NextRummageTime = DateTime.Now + TimeSpan.FromMinutes( delay );
            }

            if ( CanBreath && DateTime.Now >= m_NextBreathTime ) // tested: controled dragons do breath fire, what about summoned skeletal dragons?
            {
                Mobile target = this.Combatant;

                if ( target != null && target.Alive && !target.IsDeadBondedPet && CanBeHarmful( target ) && target.Map == this.Map && !IsDeadBondedPet && target.InRange( this, BreathRange ) && InLOS( target ) && !BardPacified )
                    BreathStart( target );

                m_NextBreathTime = DateTime.Now + TimeSpan.FromSeconds( BreathMinDelay + (Utility.RandomDouble() * BreathMaxDelay) );
            }
        }

        public virtual bool Rummage()
        {
            if (IsChampionSpawn == true)
                return false;

            Corpse toRummage = null;
            IPooledEnumerable eable = GetItemsInRange( 2 );
            foreach ( Item item in eable )
            {
                if ( item is Corpse && item.Items.Count > 0 )
                {
                    toRummage = (Corpse)item;
                    break;
                }
            }
            eable.Free();

            if ( toRummage == null )
                return false;

            Container pack = this.Backpack;

            if ( pack == null )
                return false;

            List<Item> items = toRummage.Items;

            bool rejected;
            LRReason reason;

            for ( int i = 0; i < items.Count; ++i )
            {
                Item item = items[Utility.Random( items.Count )];

                Lift( item, item.Amount, out rejected, out reason );

                if ( !rejected && Drop( this, new Point3D( -1, -1, 0 ) ) )
                {
                    // *rummages through a corpse and takes an item*
                    PublicOverheadMessage( MessageType.Emote, 0x3B2, 1008086 );
                    return true;
                }
            }

            return false;
        }

        public void Pacify( Mobile master, DateTime endtime )
        {
            BardPacified = true;
            BardEndTime = endtime;
        }

        public override Mobile GetDamageMaster( Mobile damagee )
        {
            if ( m_bBardProvoked && damagee == m_bBardTarget )
                return m_bBardMaster;
            else if ( m_bControlled && m_ControlMaster != null )
                return m_ControlMaster;
            else if ( m_bSummoned && m_SummonMaster != null )
                return m_SummonMaster;

            return base.GetDamageMaster( damagee );
        }
 
        public void Provoke( Mobile master, Mobile target, bool bSuccess )
        {
            BardProvoked = true;

            this.PublicOverheadMessage( MessageType.Emote, EmoteHue, false, "*looks furious*" );
 
            if ( bSuccess )
            {
                PlaySound( GetIdleSound() );
 
                BardMaster = master;
                BardTarget = target;
                Combatant = target;
                BardEndTime = DateTime.Now + TimeSpan.FromSeconds( 30.0 );

                if ( target is BaseCreature )
                {
                    BaseCreature t = (BaseCreature)target;

                    if ( t.Unprovokable || (t.IsParagon && BaseInstrument.GetBaseDifficulty( t ) >= 160.0) )
                        return;

                    t.BardProvoked = true;

                    t.BardMaster = master;
                    t.BardTarget = this;
                    t.Combatant = this;
                    t.BardEndTime = DateTime.Now + TimeSpan.FromSeconds( 30.0 );
                }
            }
            else
            {
                PlaySound( GetAngerSound() );

                BardMaster = master;
                BardTarget = target;
            }
        }

        public bool FindMyName( string str, bool bWithAll )
        {
            int i, j;

            string name = this.Name;
 
            if( name == null || str.Length < name.Length )
                return false;
 
            string[] wordsString = str.Split(' ');
            string[] wordsName = name.Split(' ');
 
            for ( j=0 ; j < wordsName.Length; j++ )
            {
                string wordName = wordsName[j];
 
                bool bFound = false;
                for ( i=0 ; i < wordsString.Length; i++ )
                {
                    string word = wordsString[i];

                    if ( Insensitive.Equals( word, wordName ) )
                        bFound = true;
 
                    if ( bWithAll && Insensitive.Equals( word, "all" ) )
                        return true;
                }
 
                if ( !bFound )
                    return false;
            }
 
            return true;
        }

        // 15.08.2012 :: zombie :: dodanie
        public virtual bool CanBeTeleported { get { return true; } }
        // zombie

        public static void TeleportPets( Mobile master, Point3D loc, Map map )
        {
            TeleportPets( master, loc, map, false );
        }

        public static void TeleportPets( Mobile master, Point3D loc, Map map, bool onlyBonded )
        {
            List<Mobile> move = new List<Mobile>();

            IPooledEnumerable eable = master.GetMobilesInRange( 3 );
            foreach ( Mobile m in eable )
            {
                // 15.08.2012 :: zombie
                if ( m is BaseCreature && ((BaseCreature)m).CanBeTeleported )
                // zombie
                {
                    BaseCreature pet = (BaseCreature)m;

                    if ( pet.Controlled && pet.ControlMaster == master )
                    {
                        if ( !onlyBonded || pet.IsBonded )
                        {
                            if ( pet.ControlOrder == OrderType.Guard || pet.ControlOrder == OrderType.Follow || pet.ControlOrder == OrderType.Come )
                                move.Add( pet );
                        }
                    }
                }
            }
            eable.Free();

            foreach ( Mobile m in move )
                m.MoveToWorld( loc, map );
        }

        public virtual void ResurrectPet()
        {
            if ( !IsDeadPet )
                return;

            OnBeforeResurrect();

            Poison = null;

            Warmode = false;

            Hits = 10;
            Stam = StamMax;
            Mana = 0;

            ProcessDeltaQueue();

            IsDeadPet = false;

            Effects.SendPacket( Location, Map, new BondedStatus( 0, this.Serial, 0 ) );

            this.SendIncomingPacket();
            this.SendIncomingPacket();

            OnAfterResurrect();

            Mobile owner = this.ControlMaster;

            if ( owner == null || owner.Deleted || owner.Map != this.Map || !owner.InRange( this, 12 ) || !this.CanSee( owner ) || !this.InLOS( owner ) )
            {
                if ( this.OwnerAbandonTime == DateTime.MinValue )
                    this.OwnerAbandonTime = DateTime.Now;
            }
            else
            {
                this.OwnerAbandonTime = DateTime.MinValue;
            }

            CheckStatTimers();
        }

        public override bool CanBeDamaged()
        {
            if ( IsDeadPet )
                return false;

            return base.CanBeDamaged();
        }

        public virtual bool PlayerRangeSensitive { get { return (this.CurrentWayPoint == null); } } //If they are following a waypoint, they'll continue to follow it even if players aren't around

        public override void OnSectorDeactivate()
        {
            if ( PlayerRangeSensitive && m_AI != null )
                m_AI.Deactivate();

            base.OnSectorDeactivate();
        }

        public override void OnSectorActivate()
        {
            if ( PlayerRangeSensitive && m_AI != null )
                m_AI.Activate();

            base.OnSectorActivate();
        }

        private bool m_RemoveIfUntamed;

        // used for deleting untamed creatures [in houses]
        private int m_RemoveStep; 

        [CommandProperty( AccessLevel.GameMaster )] 
        public bool RemoveIfUntamed{ get{ return m_RemoveIfUntamed; } set{ m_RemoveIfUntamed = value; } }

        [CommandProperty( AccessLevel.GameMaster )] 
        public int RemoveStep { get { return m_RemoveStep; } set { m_RemoveStep = value; } }

        // 17.06.2012 :: zombie 
        #region Szacowanie poziomu trudnosci moba

        public double GetPoisonBonus( Poison p )
        {
            if ( p == Poison.Lethal )       return 1;
            else if ( p == Poison.Deadly )  return 0.92;
            else if ( p == Poison.Greater ) return 0.70;
            else if ( p == Poison.Regular ) return 0.40;
            else if ( p == Poison.Lesser )  return 0.30;
            else                            return 0;
        }

        public double MeleeDPS
        {
            get 
            { 
                BaseWeapon bw = (BaseWeapon)Weapon;      
                int min, max;
                
                bw.GetBaseDamageRange( this, out min, out max );
                int avgDamage = (int)( min + max ) / 2;
                double damage = (double)bw.ScaleDamageAOS( (Mobile)this, avgDamage, false );

                return ( damage / bw.GetDelay( (Mobile)this ).TotalSeconds ) * MeeleeSkillFactor;
            }
        }

        public double MagicDPS
        {
            get 
            {
                double spellDamage = 0;
                double castDelay = 0;

                if ( AIObject is SpellCasterAI )
                {
                    SpellCasterAI ai = (SpellCasterAI)AIObject;
                    int maxCircle = ai.GetMaxCircle();
                    Spell s = ai.GetRandomDamageSpell();

                    if ( s == null )
                        return 0;

                    int[] circleDmg = new int[] { 1, 8, 11, 11, 20, 30, 38 };
                    spellDamage = (double)s.GetNewAosDamage( circleDmg[maxCircle - 1], 1, 5, false );
                    castDelay = 0.25 + ( maxCircle * 0.25 );
                    return spellDamage / castDelay;
                }
                else
                    return 0;
            }
        }

        public double DPS
        {
            get 
            {
                double dps = Math.Max( MeleeDPS, MagicDPS );
                
                if( HitPoisonBonus > 0 )
                    dps += dps * HitPoisonBonus;

                if ( WeaponAbilitiesBonus > 0 )
                    dps += dps * WeaponAbilitiesBonus;

                if ( HasBreath )
                    dps += (double)BreathComputeDamage() / 12.5;

                return dps;
            }
        }

        public double Life
        {
            get { return ( (double)HitsMax * AvgResFactor * MeeleeSkillFactor ) / 100; }
        }

        public double MeeleeSkillFactor
        {
            get { return Math.Max( 0.5, MaxMeleeSkill.Value / 120 ) ; }
        }

        public double AvgResFactor
        {
            get { return AvgRes / 100; }
        }

        public double HitPoisonBonus
        {
            get { return GetPoisonBonus( HitPoison ) * HitPoisonChance * MeeleeSkillFactor; }
        }

        public double WeaponAbilitiesBonus
        {
            get
            {
                double sum = 0;
                Dictionary<WeaponAbility, double> abilities = new Dictionary<WeaponAbility, double>();

                abilities[ WeaponAbility.ArmorIgnore  ]     = 0.4;
                abilities[ WeaponAbility.BleedAttack ]      = 0.7;
                abilities[ WeaponAbility.ConcussionBlow ]   = 0.4;
                abilities[ WeaponAbility.CrushingBlow ]     = 0.3;
                abilities[ WeaponAbility.Disarm ]           = 0.3;
                abilities[ WeaponAbility.Dismount ]         = 0.3;
                abilities[ WeaponAbility.DoubleStrike ]     = 0.3;
                abilities[ WeaponAbility.InfectiousStrike ] = 0.8;
                abilities[ WeaponAbility.MortalStrike ]     = 0.7;
                abilities[ WeaponAbility.MovingShot ]       = 0.2;
                abilities[ WeaponAbility.ParalyzingBlow ]   = 0.3;
                abilities[ WeaponAbility.ShadowStrike ]     = 0.2;
                abilities[ WeaponAbility.WhirlwindAttack ]  = 0.3;
                abilities[ WeaponAbility.RidingSwipe ]      = 0.3;
                abilities[ WeaponAbility.FrenziedWhirlwind ]= 0.3;
                abilities[ WeaponAbility.Block ]            = 0.1;
                abilities[ WeaponAbility.DefenseMastery ]   = 0.1;
                abilities[ WeaponAbility.NerveStrike ]      = 0.3;
                abilities[ WeaponAbility.TalonStrike ]      = 0.2;
                abilities[ WeaponAbility.Feint ]            = 0.1;
                abilities[ WeaponAbility.DualWield ]        = 0.2;
                abilities[ WeaponAbility.DoubleShot ]       = 0.3;
                abilities[ WeaponAbility.ArmorPierce ]      = 0.4;

                foreach ( WeaponAbility ab in WeaponAbilities.Keys )
                {
                    if ( abilities.ContainsKey( ab ) )
                    {
                        double chance = WeaponAbilities[ ab ];
                        sum += chance * abilities[ ab ] ;
                    }
                }

                return sum * 0.5;
            }
        }

        public virtual double DifficultyScalar
        {
            get{ return 1.0; }
        }

        public double BaseDifficulty
        {
            get { return DPS * Math.Max( 0.01, Life ); }
        }

        public void GenerateDifficulty()
        {
            double difficulty = BaseDifficulty * DifficultyScalar;

            m_Difficulty = Math.Round( difficulty, 4 ) + 0.0001; // So it's never 0.0
        }

        [CommandProperty( AccessLevel.GameMaster )]
        public double Difficulty
        {
            get
            {
                if ( m_Difficulty == 0.0 )
                    GenerateDifficulty();
                return m_Difficulty; 
            }
        }
        #endregion
        // zombie
    }
    
    public class LoyaltyTimer : Timer
    {
        private static TimeSpan InternalDelay = TimeSpan.FromMinutes( 5.0 );

        public static void Initialize()
        {
            new LoyaltyTimer().Start();
        }

        public LoyaltyTimer() : base( InternalDelay, InternalDelay )
        {
            m_NextHourlyCheck = DateTime.Now + TimeSpan.FromHours( 1.0 );
            Priority = TimerPriority.FiveSeconds;
        }

        private DateTime m_NextHourlyCheck;

        protected override void OnTick() 
        {
            if ( DateTime.Now >= m_NextHourlyCheck )
                m_NextHourlyCheck = DateTime.Now + TimeSpan.FromHours( 1.0 );
            else
                return;

            List<BaseCreature> toRelease = new List<BaseCreature>();

            // added array for wild creatures in house regions to be removed
            List<BaseCreature> toRemove = new List<BaseCreature>();

            foreach ( Mobile m in World.Mobiles.Values )
            {
                if ( m is BaseMount && ((BaseMount)m).Rider != null )
                {
                    ((BaseCreature)m).OwnerAbandonTime = DateTime.MinValue;
                    continue;
                }

                if ( m is BaseCreature )
                {
                    BaseCreature c = (BaseCreature)m;

                    if ( c.IsDeadPet )
                    {
                        Mobile owner = c.ControlMaster;

                        if ( !c.IsStabled && ( owner == null || owner.Deleted || owner.Map != c.Map || !owner.InRange( c, 12 ) || !c.CanSee( owner ) || !c.InLOS( owner ) ) )
                        {
                            if ( c.OwnerAbandonTime == DateTime.MinValue )
                                c.OwnerAbandonTime = DateTime.Now;
                            else if ( (c.OwnerAbandonTime + c.BondingAbandonDelay) <= DateTime.Now )
                                toRemove.Add( c );
                        }
                        else
                        {
                            c.OwnerAbandonTime = DateTime.MinValue;
                        }
                    }
                    else if ( c.Controlled && c.Commandable )
                    {
                        c.OwnerAbandonTime = DateTime.MinValue;
                        
                        if ( c.Map != Map.Internal )
                        {
                            c.Loyalty -= (BaseCreature.MaxLoyalty / 20);

                            if( c.Loyalty < (BaseCreature.MaxLoyalty / 5) )
                            {
                                c.Say( 1043270, c.Name ); // * ~1_NAME~ looks around desperately *
                                c.PlaySound( c.GetIdleSound() );
                            }

                            if ( c.Loyalty <= 0 )
                                toRelease.Add( c );
                        }
                    }

                    // added lines to check if a wild creature in a house region has to be removed or not
                    if ( (!c.Controlled && !c.IsStabled && ( c.Region.IsPartOf( typeof( HouseRegion ) ) && c.CanBeDamaged()) || ( c.RemoveIfUntamed && c.Spawner == null )) )
                    {
                        c.RemoveStep++;

                        if ( c.RemoveStep >= 20 )
                            toRemove.Add( c );
                    }
                    else
                    {
                        c.RemoveStep = 0;
                    }
                }
            }

            foreach ( BaseCreature c in toRelease )
            {
				Console.WriteLine("WYPUSZCZAM PETA (LoyaltyTimer): OwnerSerial({0}) OwnerName({1}) Serial({2}) Name({3}) ", c.ControlMaster!=null?c.ControlMaster.Serial.ToString():"-", c.ControlMaster!=null?c.ControlMaster.Name:"-", c.Serial, c.Name);
                c.Say( 1043255, c.Name ); // ~1_NAME~ appears to have decided that is better off without a master!
                c.Loyalty = BaseCreature.MaxLoyalty; // Wonderfully Happy
                c.IsBonded = false;
                c.BondingBegin = DateTime.MinValue;
                c.OwnerAbandonTime = DateTime.MinValue;
                c.ControlTarget = null;
                //c.ControlOrder = OrderType.Release;
                c.AIObject.DoOrderRelease(); // this will prevent no release of creatures left alone with AI disabled (and consequent bug of Followers)
            }

            // added code to handle removing of wild creatures in house regions
            foreach ( BaseCreature c in toRemove )
            {
				Console.WriteLine("USUWAM PETA (LoyaltyTimer): OwnerSerial({0}) OwnerName({1}) Serial({2}) Name({3}) ", c.ControlMaster!=null?c.ControlMaster.Serial.ToString():"-", c.ControlMaster!=null?c.ControlMaster.Name:"-", c.Serial, c.Name);
                c.Delete();
            }
        }
    }
}
