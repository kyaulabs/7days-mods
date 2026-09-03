using System.Collections.Generic;
using UnityEngine;

namespace DSZipline
{
    /// <summary>Reads zipline links from vanilla TileEntityPowered parent/child data.</summary>
    public static class ZiplineLink
    {
        public const float MinimumDrop = 2f;
        public const float WoodenMaximumLength = 250f;
        public const float SonicMaximumLength = 500f;
        // The wire sits at raised-hand height. ZiplineRider uses the matching
        // hang offset, so raising the cable does not change the player's path.
        public static readonly Vector3 AnchorOffset = new Vector3(0.5f, 2.55f, 0.5f);

        public static Vector3 AnchorPoint(Vector3i blockPos)
        {
            return blockPos.ToVector3() + AnchorOffset;
        }

        public static float Sag(Vector3 start, Vector3 end)
        {
            float horizontal = Vector2.Distance(
                new Vector2(start.x, start.z),
                new Vector2(end.x, end.z));
            return Mathf.Clamp(horizontal * 0.02f, 0.25f, 1.5f);
        }

        public static Vector3 Point(Vector3 start, Vector3 end, float t)
        {
            float sag = Sag(start, end) * 4f * t * (1f - t);
            return Vector3.Lerp(start, end, t) + Vector3.down * sag;
        }

        public static Vector3 Tangent(Vector3 start, Vector3 end, float t)
        {
            t = Mathf.Clamp01(t);
            Vector3 tangent = end - start;
            tangent.y -= Sag(start, end) * 4f * (1f - 2f * t);
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
        }

        public static float ApproximateLength(Vector3 start, Vector3 end)
        {
            const int samples = 24;
            float length = 0f;
            Vector3 previous = Point(start, end, 0f);
            for (int i = 1; i <= samples; i++)
            {
                Vector3 current = Point(start, end, i / (float)samples);
                length += Vector3.Distance(previous, current);
                previous = current;
            }
            return length;
        }

        public static bool TryGetAnchor(WorldBase world, Vector3i pos, out BlockZiplineAnchor anchor)
        {
            anchor = world != null ? world.GetBlock(pos).Block as BlockZiplineAnchor : null;
            return anchor != null;
        }

        public static bool IsAnchor(WorldBase world, Vector3i pos)
        {
            return TryGetAnchor(world, pos, out _);
        }

        public static float MaximumLengthFor(BlockZiplineAnchor anchor)
        {
            return anchor != null && anchor.IsSonicTier
                ? SonicMaximumLength
                : WoodenMaximumLength;
        }

        public static bool AreSameTier(WorldBase world, Vector3i first, Vector3i second)
        {
            return TryGetAnchor(world, first, out BlockZiplineAnchor firstAnchor) &&
                   TryGetAnchor(world, second, out BlockZiplineAnchor secondAnchor) &&
                   firstAnchor.IsSonicTier == secondAnchor.IsSonicTier;
        }

        private static bool References(TileEntityPowered tile, Vector3i other)
        {
            if (tile == null) return false;
            if (tile.HasParent() && tile.GetParent() == other) return true;
            return tile.wireDataList != null && tile.wireDataList.Contains(other);
        }

        private static bool IsLoaded(WorldBase world, Vector3i position)
        {
            return world?.GetChunkFromWorldPos(position.x, position.y, position.z) is Chunk;
        }

        public static bool AreLinked(WorldBase world, Vector3i first, Vector3i second)
        {
            bool firstLoaded = IsLoaded(world, first);
            bool secondLoaded = IsLoaded(world, second);
            BlockZiplineAnchor firstAnchor = null;
            BlockZiplineAnchor secondAnchor = null;

            if (firstLoaded && !TryGetAnchor(world, first, out firstAnchor)) return false;
            if (secondLoaded && !TryGetAnchor(world, second, out secondAnchor)) return false;
            if (firstAnchor != null && secondAnchor != null &&
                firstAnchor.IsSonicTier != secondAnchor.IsSonicTier) return false;

            BlockZiplineAnchor knownAnchor = firstAnchor ?? secondAnchor;
            float maximum = knownAnchor != null ? MaximumLengthFor(knownAnchor) : SonicMaximumLength;
            if (Vector3.Distance(AnchorPoint(first), AnchorPoint(second)) > maximum) return false;

            if (firstLoaded && References(world.GetTileEntity(first) as TileEntityPowered, second))
                return true;
            if (secondLoaded && References(world.GetTileEntity(second) as TileEntityPowered, first))
                return true;

            // A 500 m ride spends time with both endpoint chunks outside the
            // loaded radius. The relationship was validated at launch; defer
            // revalidation until either endpoint streams back in.
            return !firstLoaded && !secondLoaded;
        }

        public static bool HasLinkedAnchor(WorldBase world, Vector3i anchor)
        {
            if (!IsAnchor(world, anchor)) return false;
            TileEntityPowered tile = world.GetTileEntity(anchor) as TileEntityPowered;
            if (tile == null) return false;

            BlockZiplineAnchor source = world.GetBlock(anchor).Block as BlockZiplineAnchor;
            if (source == null) return false;

            if (tile.HasParent() && IsCompatibleLinkedEndpoint(world, source, tile.GetParent())) return true;
            if (tile.wireDataList != null)
            {
                foreach (Vector3i candidate in tile.wireDataList)
                    if (IsCompatibleLinkedEndpoint(world, source, candidate)) return true;
            }
            return false;
        }

        public static bool TryGetLowerEndpoint(WorldBase world, Vector3i upper, out Vector3i lower)
        {
            lower = Vector3i.invalid;
            if (!IsAnchor(world, upper)) return false;

            TileEntityPowered tile = world.GetTileEntity(upper) as TileEntityPowered;
            if (tile == null) return false;

            var candidates = new List<Vector3i>();
            if (tile.HasParent()) candidates.Add(tile.GetParent());
            if (tile.wireDataList != null) candidates.AddRange(tile.wireDataList);

            BlockZiplineAnchor source = world.GetBlock(upper).Block as BlockZiplineAnchor;
            if (source == null) return false;

            float bestDrop = MinimumDrop;
            float maximumLength = MaximumLengthFor(source);
            foreach (Vector3i candidate in candidates)
            {
                if (!IsCompatibleLinkedEndpoint(world, source, candidate)) continue;
                float drop = upper.y - candidate.y;
                float distance = Vector3.Distance(AnchorPoint(upper), AnchorPoint(candidate));
                if (drop >= bestDrop && distance <= maximumLength)
                {
                    bestDrop = drop;
                    lower = candidate;
                }
            }

            return lower.IsValid;
        }

        private static bool IsCompatibleLinkedEndpoint(
            WorldBase world,
            BlockZiplineAnchor source,
            Vector3i candidate)
        {
            if (!IsLoaded(world, candidate))
                return true; // The server validated the persisted relationship when it was created.
            return TryGetAnchor(world, candidate, out BlockZiplineAnchor endpoint) &&
                   endpoint.IsSonicTier == source.IsSonicTier;
        }
    }
}
