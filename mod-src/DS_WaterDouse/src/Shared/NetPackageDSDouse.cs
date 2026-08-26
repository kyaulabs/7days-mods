using System;
using UnityEngine.Scripting;

namespace DSWaterDouse
{
    /// <summary>
    /// Client -&gt; server report of a self-douse with a water item.
    ///
    /// The local player's scent state is computed client-side (PlayerStealth.
    /// SmellTickClient) and the server only receives the radius TARGET via
    /// NetPackageEntityStealth — it never learns about an instant radius cut, and the
    /// vanilla wet-clear (-1 radius) only supports a FULL clear. This package carries
    /// the douse so the server can cut its own authoritative smellRadius immediately
    /// (zombie AI + the "N M" smell display), and to validate that the player actually
    /// carries a douseable water item (forged packages are ignored).
    ///
    /// This class MUST exist in BOTH the server and client assemblies:
    /// NetPackageManager syncs package ids to clients by class name, and a client
    /// missing this class name gets disconnected.
    /// </summary>
    [Preserve]
    public class NetPackageDSDouse : NetPackage
    {
        /// <summary>Meters of scent radius to remove (ignored when FullClear).</summary>
        public float Meters;

        /// <summary>True = remove ALL scent (pure water), same as the vanilla wet clear.</summary>
        public bool FullClear;

        /// <summary>Server-side handler, wired by the server ModInit (not set on clients).</summary>
        public static Action<EntityPlayer, float, bool> Handler;

        public NetPackageDSDouse Setup(float meters, bool fullClear)
        {
            Meters = meters;
            FullClear = fullClear;
            return this;
        }

        public override void read(PooledBinaryReader _reader)
        {
            Meters = _reader.ReadSingle();
            FullClear = _reader.ReadBoolean();
        }

        public override void write(PooledBinaryWriter _writer)
        {
            base.write(_writer);
            // cast to BinaryWriter: PooledBinaryWriter declares ReadOnlySpan overloads
            // (needs System.Memory) which this mod's csproj does not reference
            var bw = (System.IO.BinaryWriter)_writer;
            bw.Write(Meters);
            bw.Write(FullClear);
        }

        public override void ProcessPackage(World _world, GameManager _callbacks)
        {
            try
            {
                if (_world == null || _world.IsRemote()) return; // server side only
                if (Sender == null || Sender.entityId == -1) return;
                var player = _world.GetEntity(Sender.entityId) as EntityPlayer;
                if (player == null) return;
                Handler?.Invoke(player, Meters, FullClear);
            }
            catch (Exception e)
            {
                Log.Error("[DSDouse] NetPackageDSDouse error: " + e);
            }
        }

        public override int GetLength()
        {
            return 8;
        }
    }
}
