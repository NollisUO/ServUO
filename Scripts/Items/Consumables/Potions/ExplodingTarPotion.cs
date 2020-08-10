using Server.Misc;
using Server.Network;
using Server.Spells;
using Server.Targeting;

using System;
using System.Collections.Generic;
using System.Linq;

namespace Server.Items
{
    [TypeAlias("Server.Items.ExplodingTarPotion")]
    public class ExplodingTarPotion : BasePotion
    {
        public virtual int Radius { get { return 20; } }

        public override int LabelNumber => 1095147; // Exploding Tar Potion
        public override bool RequireFreeHand => false;

        [Constructable]
        public ExplodingTarPotion()
            : base(0xF06, PotionEffect.ExplodingTarPotion)
        {
            Hue = 1109;
        }

        public ExplodingTarPotion(Serial serial) : base(serial)
        {
        }

        public override void Drink(Mobile from)
        {
            if (from.Paralyzed || from.Frozen || (from.Spell != null && from.Spell.IsCasting))
            {
                from.SendLocalizedMessage(1062725); // You can not use that potion while paralyzed.
                return;
            }

            int delay = GetDelay(from);

            if (delay > 0)
            {
                from.SendLocalizedMessage(1072529, String.Format("{0}\t{1}", delay, delay > 1 ? "seconds." : "second.")); // You cannot use that for another ~1_NUM~ ~2_TIMEUNITS~
                return;
            }

            ThrowTarget targ = from.Target as ThrowTarget;

            if (targ != null && targ.Potion == this)
                return;

            from.RevealingAction();

            from.Target = new ThrowTarget(this);
        }

        public override void Serialize(GenericWriter writer)
        {
            base.Serialize(writer);

            writer.Write(1); // version
        }

        public override void Deserialize(GenericReader reader)
        {
            base.Deserialize(reader);

            int version = reader.ReadInt();

            if (version == 0)
            {
                reader.ReadInt();
            }
        }

        public void Explode_Callback(object state)
        {
            object[] states = (object[])state;

            Explode((Mobile)states[0], (Point3D)states[1], (Map)states[2]);
        }

        public virtual void Explode(Mobile from, Point3D loc, Map map)
        {
            if (Deleted || map == null)
                return;

            Consume();

            // Effects
            Effects.PlaySound(loc, map, 0x207);
            Geometry.Circle2D(loc, map, Radius, TarEffect, 270, 90);

            Timer.DelayCall(TimeSpan.FromSeconds(1), new TimerStateCallback(CircleEffect2), new object[] { loc, map });

            foreach (Mobile m in SpellHelper.AcquireIndirectTargets(from, loc, map, Radius).OfType<Mobile>())
            {
                AddEffects(m);
            }
        }

        #region Effects
        public virtual void TarEffect(Point3D p, Map map)
        {
            if (map.CanFit(p, 12, true, false))
                Effects.SendLocationEffect(p, map, 0x376A, 4, 9);
        }

        public void CircleEffect2(object state)
        {
            object[] states = (object[])state;

            Geometry.Circle2D((Point3D)states[0], (Map)states[1], Radius, TarEffect, 90, 270);
        }
        #endregion

        #region Delay
        private static Dictionary<Mobile, Timer> _SlowEffects;
        private static Dictionary<Mobile, Timer> _Delay;

        public static void AddEffects(Mobile m)
        {
            if (_SlowEffects == null)
            {
                _SlowEffects = new Dictionary<Mobile, Timer>();
            }
            else if (_SlowEffects.ContainsKey(m))
            {
                _SlowEffects[m].Stop();
            }

            _SlowEffects[m] = Timer.DelayCall(TimeSpan.FromMinutes(1), mob => RemoveEffects(mob), m);

            m.SendLocalizedMessage(1095151);
            m.SendSpeedControl(SpeedControlType.WalkSpeed);
        }

        public static void RemoveEffects(Mobile m)
        {
            if (_SlowEffects != null && _SlowEffects.ContainsKey(m))
            {
                m.SendSpeedControl(SpeedControlType.Disable);

                _SlowEffects.Remove(m);

                if (_SlowEffects.Count == 0)
                {
                    _SlowEffects = null;
                }
            }
        }

        public static void AddDelay(Mobile m)
        {
            if (_Delay == null)
            {
                _Delay = new Dictionary<Mobile, Timer>();
            }

            if (_Delay.ContainsKey(m))
            {
                Timer timer = _Delay[m];

                if (timer != null)
                {
                    timer.Stop();
                }
            }

            _Delay[m] = Timer.DelayCall(TimeSpan.FromSeconds(60), EndDelay_Callback, m);
        }

        public static int GetDelay(Mobile m)
        {
            if (_Delay != null && _Delay.ContainsKey(m))
            {
                Timer timer = _Delay[m];

                if (timer != null && timer.Next > DateTime.UtcNow)
                {
                    return (int)(timer.Next - DateTime.UtcNow).TotalSeconds;
                }
            }

            return 0;
        }

        private static void EndDelay_Callback(Mobile m)
        {
            EndDelay(m);
        }

        public static void EndDelay(Mobile m)
        {
            if (_Delay != null && _Delay.ContainsKey(m))
            {
                Timer timer = _Delay[m];

                if (timer != null)
                {
                    timer.Stop();
                    _Delay.Remove(m);

                    if (_Delay.Count == 0)
                    {
                        _Delay = null;
                    }
                }
            }
        }
        #endregion

        private class ThrowTarget : Target
        {
            private readonly ExplodingTarPotion m_Potion;

            public ExplodingTarPotion Potion => m_Potion;

            public ThrowTarget(ExplodingTarPotion potion) : base(12, true, TargetFlags.None)
            {
                m_Potion = potion;
            }

            protected override void OnTarget(Mobile from, object targeted)
            {
                if (m_Potion.Deleted || m_Potion.Map == Map.Internal)
                    return;

                IPoint3D p = targeted as IPoint3D;

                if (p == null || from.Map == null)
                    return;

                // Add delay
                AddDelay(from);

                SpellHelper.GetSurfaceTop(ref p);

                from.RevealingAction();

                IEntity to;

                if (p is Mobile)
                    to = (Mobile)p;
                else
                    to = new Entity(Serial.Zero, new Point3D(p), from.Map);

                Effects.SendMovingEffect(from, to, 0xF0D, 7, 0, false, false, m_Potion.Hue, 0);
                Timer.DelayCall(TimeSpan.FromSeconds(1.0), new TimerStateCallback(m_Potion.Explode_Callback), new object[] { from, new Point3D(p), from.Map });
            }
        }
    }
}
