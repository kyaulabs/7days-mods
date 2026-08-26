using System;
using UnityEngine.Scripting;

namespace DSWeaponMastery
{
    /// <summary>
    /// Client -> server report of a successful block interaction (melee/tool swing that
    /// dealt damage, or a repair/upgrade action). v1.0 dedicated servers never run
    /// ItemActionAttack.Hit / ItemActionRepair for remote players (melee and repair are
    /// simulated client-side and only the resulting block change is synced, without an
    /// attacker id), so the client reports these events. The server re-validates the
    /// skill against the player's held item, rolls the chance, applies cooldowns and
    /// grants XP (UseXp) — clients can't farm skills by spamming this package.
    ///
    /// This class MUST exist in BOTH the server and client assemblies: NetPackageManager
    /// discovers packages by reflection and syncs ids to clients by class name.
    /// </summary>
    [Preserve]
    public class NetPackageDSWMUseXp : NetPackage
    {
        /// <summary>Skill name the client derived from its held item (e.g. craftingHarvestingTools).</summary>
        public string Skill;

        /// <summary>True = this hit destroyed the block (guaranteed tool destroy grant).</summary>
        public bool Destroyed;

        /// <summary>Server-side grant callback, wired by the server ModInit (not set on clients).</summary>
        public static Action<EntityPlayer, string, bool> Handler;

        public NetPackageDSWMUseXp Setup(string skill, bool destroyed)
        {
            Skill = skill;
            Destroyed = destroyed;
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            Skill = _reader.ReadString();
            Destroyed = _reader.ReadBoolean();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            // cast to BinaryWriter: PooledBinaryWriter also declares ReadOnlySpan overloads
            // (needs System.Memory), which this mod's csproj doesn't reference
            var bw = (System.IO.BinaryWriter)_writer;
            bw.Write(Skill ?? "");
            bw.Write(Destroyed);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                if (_world == null || _world.IsRemote()) return; // server side only
                if (Sender == null || Sender.entityId == -1) return;
                var player = _world.GetEntity(Sender.entityId) as EntityPlayer;
                if (player == null) return;
                Handler?.Invoke(player, Skill, Destroyed);
            }
            catch (Exception e)
            {
                Log.Error("[DSWM] NetPackageDSWMUseXp error: " + e);
            }
        }

        public override int GetLength()
        {
            return 16 + (Skill != null ? Skill.Length : 0);
        }
    }
}
