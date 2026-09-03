using System;
using System.IO;
using UnityEngine.Scripting;

namespace VehicleAdaptations
{
    /// <summary>
    /// Reports client-simulated static-block damage to the authoritative server.
    /// This package class must exist in both assemblies because package IDs are
    /// synchronized by class name.
    /// </summary>
    [Preserve]
    public sealed class NetPackageVehicleAdaptationDamage : NetPackage
    {
        public Vector3i BlockPosition;
        public int Damage;

        public NetPackageVehicleAdaptationDamage Setup(Vector3i blockPosition, int damage)
        {
            BlockPosition = blockPosition;
            Damage = damage;
            return this;
        }

        public override void read(PooledBinaryReader reader)
        {
            BlockPosition = new Vector3i(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());
            Damage = reader.ReadInt32();
        }

        public override void write(PooledBinaryWriter writer)
        {
            base.write(writer);
            var binary = (BinaryWriter)writer;
            binary.Write(BlockPosition.x);
            binary.Write(BlockPosition.y);
            binary.Write(BlockPosition.z);
            binary.Write(Damage);
        }

        public override void ProcessPackage(World world, GameManager callbacks)
        {
            try
            {
                if (world == null || world.IsRemote() || Sender == null || Sender.entityId < 0)
                    return;

                EntityPlayer player = world.GetEntity(Sender.entityId) as EntityPlayer;
                if (player == null ||
                    (player.position - BlockPosition.ToVector3Center()).sqrMagnitude > 65536f)
                    return;

                int damage = Math.Max(0, Math.Min(Damage, 5000));
                TEFeatureVehicleAdaptation feature =
                    VehicleDamagePatch.GetFeature(world, BlockPosition);
                if (feature == null || damage == 0) return;
                feature.RecordDamage(world, damage, "player damage");
            }
            catch (Exception e)
            {
                Log.Error("[VehicleAdaptations] Damage package failed: " + e);
            }
        }

        public override int GetLength()
        {
            return 20;
        }
    }
}
