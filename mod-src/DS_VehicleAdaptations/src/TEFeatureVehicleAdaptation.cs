using System;
using System.IO;
using UnityEngine;
using UnityEngine.Scripting;

namespace VehicleAdaptations
{
    /// <summary>
    /// Persistent replacement for vanilla TEFeatureExplodable on static vehicles.
    /// It keeps the vehicle's original ExplosionData, adds a damage threshold and
    /// visible fuse, and retains enough map-generation state for safe regeneration.
    /// </summary>
    [Preserve]
    public sealed class TEFeatureVehicleAdaptation : TEFeatureAbs, IFeatureTriggerCapability
    {
        private const ushort DataVersion = 1;

        public ExplosionData ExplosionData;
        public float DamageTaken;
        public float DamageRequired;
        public bool Burning;
        public long DetonateAtUtcTicks;
        public string OriginalBlockName = string.Empty;
        public byte OriginalRotation;
        public byte OriginalMeta;
        public byte OriginalMeta2;
        public byte OriginalMeta3;
        public bool RespawnEligible;
        public bool Detonating;

        private bool clientFireSpawned;

        public IFeatureTriggerCapability.ETriggerRole TriggerRole =>
            IFeatureTriggerCapability.ETriggerRole.TriggeredBy;

        public override void Init(TileEntityComposite parent, TileEntityFeatureData featureData)
        {
            base.Init(parent, featureData);
            ExplosionData = new ExplosionData(featureData.Props);
            if (ExplosionData.ParticleIndex == 0)
            {
                Log.Error("[VehicleAdaptations] Vehicle " + parent.blockValue.Block.GetBlockName() +
                          " has no explosion ParticleIndex.");
            }
        }

        public override void CopyFromInternal(TileEntityComposite other)
        {
            CopyState(other?.GetFeature<TEFeatureVehicleAdaptation>());
        }

        public override void UpgradeDowngradeFrom(TileEntityComposite other)
        {
            base.UpgradeDowngradeFrom(other);
            CopyState(other?.GetFeature<TEFeatureVehicleAdaptation>());
        }

        public override void OnAdded(Vector3i blockPos, BlockValue blockValue)
        {
            base.OnAdded(blockPos, blockValue);
            InitializeOriginal(blockValue);
        }

        public override void OnLoad()
        {
            base.OnLoad();
            InitializeOriginal(Parent.blockValue);
        }

        public override void SetBlockEntityData(BlockEntityData blockEntityData)
        {
            base.SetBlockEntityData(blockEntityData);
            if (Burning) EnsureClientFire();
        }

        public override void OnRemove(World world)
        {
            RemoveClientFire();
            base.OnRemove(world);
        }

        public override void OnUnload(World world)
        {
            RemoveClientFire();
            base.OnUnload(world);
        }

        public override void OnDestroy()
        {
            RemoveClientFire();
            base.OnDestroy();
        }

        public override void UpdateTick(World world)
        {
            if (!Burning || Detonating) return;

            if (world.IsRemote())
            {
                EnsureClientFire();
                return;
            }

            if (DateTime.UtcNow.Ticks >= DetonateAtUtcTicks)
                Detonate(world);
        }

        public override void OnBlockStartsToFall(Vector3i blockPos, BlockValue blockValue)
        {
            Ignite(GameManager.Instance.World, "structural fall");
        }

        public override Block.DestroyedResult OnBlockDestroyedBy(
            Vector3i blockPos, BlockValue blockValue, int entityId, bool useHarvestTool)
        {
            // Preserve normal visual downgrade stages until the damage threshold is
            // reached. The final stage is held in place to burn instead of vanishing.
            if (!Burning && !blockValue.Block.DowngradeBlock.isair)
                return Block.DestroyedResult.Downgrade;

            Ignite(GameManager.Instance.World, useHarvestTool ? "salvage damage" : "terminal damage");
            return Block.DestroyedResult.Keep;
        }

        public override Block.DestroyedResult OnBlockDestroyedByExplosion(
            Vector3i blockPos, BlockValue blockValue, int playerThatStartedExplosion)
        {
            Ignite(GameManager.Instance.World, "nearby explosion");
            return Block.DestroyedResult.Keep;
        }

        public void OnBlockTriggered(
            EntityPlayer player, Vector3i blockPos, BlockValue blockValue,
            System.Collections.Generic.List<BlockChangeInfo> blockChanges, BlockTrigger triggeredBy)
        {
            Ignite(GameManager.Instance.World, "block trigger");
        }

        public bool RecordDamage(World world, int damage, string reason)
        {
            if (damage <= 0 || Detonating) return Burning;
            InitializeOriginal(Parent.blockValue);
            if (Burning) return true;

            DamageTaken = Math.Min(DamageRequired, DamageTaken + damage);
            if (VehicleAdaptationsConfig.Instance.DebugLogging)
            {
                Log.Out("[VehicleAdaptations] " + Parent.ToWorldPos() + " recorded " + damage +
                        " damage (" + DamageTaken.ToString("0") + "/" +
                        DamageRequired.ToString("0") + ").");
            }

            if (DamageTaken >= DamageRequired)
                Ignite(world, reason);
            else if (!world.IsRemote())
                Parent.SetModified();
            return Burning;
        }

        public void Ignite(World world, string reason)
        {
            if (Burning || Detonating || world == null) return;
            InitializeOriginal(Parent.blockValue);
            Burning = true;
            DetonateAtUtcTicks = DateTime.UtcNow.AddSeconds(
                VehicleAdaptationsConfig.Instance.FireFuseSeconds).Ticks;

            if (world.IsRemote())
            {
                EnsureClientFire();
            }
            else
            {
                Parent.SetModified();
                Log.Out("[VehicleAdaptations] Vehicle ignited at " + Parent.ToWorldPos() +
                        " (" + reason + "); exploding in " +
                        VehicleAdaptationsConfig.Instance.FireFuseSeconds.ToString("0.#") + " s.");
            }
        }

        public override void Read(PooledBinaryReader reader, TileEntity.StreamModeRead streamMode)
        {
            base.Read(reader, streamMode);
            ushort version = reader.ReadUInt16();
            DamageTaken = reader.ReadSingle();
            DamageRequired = reader.ReadSingle();
            Burning = reader.ReadBoolean();
            DetonateAtUtcTicks = reader.ReadInt64();
            OriginalBlockName = reader.ReadString();
            OriginalRotation = reader.ReadByte();
            OriginalMeta = reader.ReadByte();
            OriginalMeta2 = reader.ReadByte();
            OriginalMeta3 = reader.ReadByte();
            RespawnEligible = reader.ReadBoolean();
            Detonating = false;

            if (version > DataVersion)
                Log.Warning("[VehicleAdaptations] Read newer vehicle state version " + version + ".");
        }

        public override void Write(PooledBinaryWriter writer, TileEntity.StreamModeWrite streamMode)
        {
            base.Write(writer, streamMode);
            var binary = (BinaryWriter)writer;
            binary.Write(DataVersion);
            binary.Write(DamageTaken);
            binary.Write(DamageRequired);
            binary.Write(Burning);
            binary.Write(DetonateAtUtcTicks);
            binary.Write(OriginalBlockName ?? string.Empty);
            binary.Write(OriginalRotation);
            binary.Write(OriginalMeta);
            binary.Write(OriginalMeta2);
            binary.Write(OriginalMeta3);
            binary.Write(RespawnEligible);
        }

        private void InitializeOriginal(BlockValue blockValue)
        {
            if (string.IsNullOrEmpty(OriginalBlockName))
            {
                OriginalBlockName = blockValue.Block.GetBlockName();
                OriginalRotation = blockValue.rotation;
                OriginalMeta = blockValue.meta;
                OriginalMeta2 = blockValue.meta2;
                OriginalMeta3 = blockValue.meta3;
                RespawnEligible = !Parent.PlayerPlaced;
            }

            if (DamageRequired <= 0f)
            {
                int available = AvailableDurability(blockValue);
                DamageRequired = Math.Max(1f, available *
                    VehicleAdaptationsConfig.Instance.IgnitionDamagePercent / 100f);
            }
        }

        private static int AvailableDurability(BlockValue value)
        {
            int total = Math.Max(1, value.Block.MaxDamage - Math.Max(0, value.damage));
            BlockValue next = value.Block.DowngradeBlock;
            int guard = 0;
            while (!next.isair && guard++ < 16)
            {
                total += Math.Max(1, next.Block.MaxDamage);
                next = next.Block.DowngradeBlock;
            }
            return total;
        }

        private void CopyState(TEFeatureVehicleAdaptation source)
        {
            if (source == null) return;
            DamageTaken = source.DamageTaken;
            DamageRequired = source.DamageRequired;
            Burning = source.Burning;
            DetonateAtUtcTicks = source.DetonateAtUtcTicks;
            OriginalBlockName = source.OriginalBlockName;
            OriginalRotation = source.OriginalRotation;
            OriginalMeta = source.OriginalMeta;
            OriginalMeta2 = source.OriginalMeta2;
            OriginalMeta3 = source.OriginalMeta3;
            RespawnEligible = source.RespawnEligible;
            Detonating = source.Detonating;
        }

        private void Detonate(World world)
        {
            if (Detonating) return;
            Detonating = true;
            RemoveClientFire();
            VehicleRespawnManager.RecordDestroyed(this, world);

            Vector3i blockPos = Parent.ToWorldPos();
            Vector3 worldPos = blockPos.ToVector3Center();
            Quaternion rotation = Quaternion.identity;
            BlockEntityData blockEntity = world.ChunkCache.GetBlockEntity(blockPos);
            if (blockEntity != null && blockEntity.transform != null)
            {
                worldPos = blockEntity.transform.position + Origin.position;
                rotation = blockEntity.transform.rotation;
            }

            Log.Out("[VehicleAdaptations] Vehicle exploded at " + blockPos + ".");
            GameManager.Instance.ExplosionServer(
                worldPos, blockPos, rotation, ExplosionData, -1, 0.1f,
                _bRemoveBlockAtExplPosition: true);
        }

        private void EnsureClientFire()
        {
            if (GameManager.IsDedicatedServer || clientFireSpawned || !Burning) return;
            string particle = VehicleAdaptationsConfig.Instance.FireParticle;
            if (!ParticleEffect.IsAvailable(particle))
            {
                Log.Warning("[VehicleAdaptations] Fire particle is unavailable: " + particle);
                return;
            }

            Vector3i pos = Parent.ToWorldPos();
            var effect = new ParticleEffect(
                particle, Parent.ToWorldCenterPos(), Quaternion.identity, 1f, Color.white);
            GameManager.Instance.SpawnBlockParticleEffect(pos, effect);
            clientFireSpawned = true;
        }

        private void RemoveClientFire()
        {
            if (GameManager.IsDedicatedServer || !clientFireSpawned) return;
            GameManager.Instance.RemoveBlockParticleEffect(Parent.ToWorldPos());
            clientFireSpawned = false;
        }
    }
}
