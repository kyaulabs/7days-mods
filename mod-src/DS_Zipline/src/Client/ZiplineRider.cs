using System.Collections.Generic;
using UnityEngine;

namespace DSZipline
{
    /// <summary>
    /// Deliberately small Phase-0 rail controller. It proves camera/controller/network behavior;
    /// collision, acceleration and server ride authorization belong to later phases.
    /// </summary>
    public static class ZiplineRider
    {
        // Keep the hands at the cable while raising the torso enough for the
        // two-bone arm rig to bend naturally instead of locking both elbows.
        private const float HangOffset = 1.85f;
        private const float EndpointClearance = 1.5f;
        private const float DetachInputDelay = 0.35f;

        private static EntityPlayerLocal player;
        private static Vector3i upperBlock;
        private static Vector3i lowerBlock;
        private static Vector3 start;
        private static Vector3 end;
        private static float cableLength;
        private static float rideSpeed;
        private static float progress;
        private static float stopProgress;
        private static float elapsed;
        private static bool controllerWasEnabled;
        private static IKController rideIk;
        private static List<IKController.Target> previousIkTargets;
        private static Transform trolleyVisual;

        public static bool IsRiding => player != null;

        public static bool Controls(EntityPlayerLocal candidate)
        {
            return player != null && player == candidate;
        }

        public static bool TryStart(EntityPlayerLocal candidate, WorldBase world, Vector3i upper)
        {
            if (candidate == null || IsRiding || candidate.IsDead() ||
                candidate.AttachedToEntity != null || candidate.IsSwimming() || candidate.IsOnLadder())
            {
                return false;
            }

            if (!ZiplineLink.TryGetLowerEndpoint(world, upper, out Vector3i lower) ||
                !(world.GetBlock(upper).Block is BlockZiplineAnchor anchor))
                return false;

            Vector3 startPoint = ZiplineLink.AnchorPoint(upper);
            Vector3 endPoint = ZiplineLink.AnchorPoint(lower);
            float length = ZiplineLink.ApproximateLength(startPoint, endPoint);
            if (length < 4f) return false;

            player = candidate;
            upperBlock = upper;
            lowerBlock = lower;
            start = startPoint;
            end = endPoint;
            cableLength = length;
            rideSpeed = anchor.RideSpeed;
            progress = Mathf.Clamp(EndpointClearance / cableLength, 0.01f, 0.2f);
            stopProgress = Mathf.Clamp01(1f - EndpointClearance / cableLength);
            elapsed = 0f;
            trolleyVisual = ZiplineArt.CreateTrolley();

            vp_FPController controller = player.vp_FPController;
            controllerWasEnabled = controller != null && controller.enabled;
            if (controller != null)
            {
                // Leave the component enabled so vp_FPCamera continues receiving
                // look input. Harmony suppresses only its movement ticks.
                controller.Stop();
            }

            ResetFallState(player, true);
            AlignToTravel(player, progress);
            SetRidePose(player, true);
            ApplyPosition(player, progress);
            Log.Out("[DSZipline] Ride spike started: " + upper + " -> " + lower +
                    " (" + cableLength.ToString("0.0") + " m at " +
                    rideSpeed.ToString("0.0") + " m/s)");
            GameManager.ShowTooltip(player, Localization.Get("DSZiplineStarted"), false, false, 0f);
            return true;
        }

        public static void Tick(EntityPlayerLocal updatedPlayer)
        {
            if (!IsRiding || updatedPlayer != player) return;

            elapsed += Time.deltaTime;
            if (player.IsDead() || player.AttachedToEntity != null || player.IsSwimming() ||
                !ZiplineLink.AreLinked(player.world, upperBlock, lowerBlock))
            {
                Stop("invalid");
                return;
            }

            if (elapsed >= DetachInputDelay && player.playerInput != null &&
                (player.playerInput.Jump.WasPressed || player.playerInput.Activate.WasPressed))
            {
                Stop("released");
                return;
            }

            SetRidePose(player, true);
            progress += rideSpeed * Time.deltaTime / cableLength;
            if (progress >= stopProgress)
            {
                progress = stopProgress;
                ApplyPosition(player, progress);
                Stop("arrived");
                return;
            }

            ApplyPosition(player, progress);
        }

        public static void Stop(string reason)
        {
            if (!IsRiding) return;

            EntityPlayerLocal stoppedPlayer = player;
            player = null;

            vp_FPController controller = stoppedPlayer.vp_FPController;
            if (controller != null)
            {
                controller.enabled = controllerWasEnabled;
                controller.Stop();
            }

            SetRidePose(stoppedPlayer, false);
            if (trolleyVisual != null)
            {
                Object.Destroy(trolleyVisual.gameObject);
                trolleyVisual = null;
            }
            stoppedPlayer.motion = Vector3.zero;
            stoppedPlayer.SetVelocity(Vector3.zero);
            ResetFallState(stoppedPlayer, false);
            stoppedPlayer.SetPosition(stoppedPlayer.position, true);
            Log.Out("[DSZipline] Ride spike ended: " + reason);
        }

        private static void SetRidePose(EntityPlayerLocal targetPlayer, bool enabled)
        {
            AvatarController avatar = targetPlayer?.emodel?.avatarController;
            if (avatar != null)
            {
                if (enabled)
                {
                    // EntityHuman otherwise selects its falling state because the
                    // rail is airborne. Hold a grounded idle base pose and let IK
                    // position the hands on the overhead cable.
                    avatar.SetInAir(false);
                    avatar.SetFallAndGround(false, true);
                }
                avatar.UpdateBool(AvatarController.isClimbingHash, false, true);
            }

            if (enabled)
            {
                if (rideIk == null && targetPlayer?.emodel != null)
                {
                    rideIk = targetPlayer.emodel.AddIKController();
                    if (rideIk != null)
                    {
                        previousIkTargets = rideIk.targets;
                        rideIk.SetTargets(new List<IKController.Target>
                        {
                            new IKController.Target
                            {
                                avatarGoal = AvatarIKGoal.LeftHand,
                                // Targets remain just below the cable, but are lower,
                                // closer together, and farther forward relative to the
                                // raised torso. This gives the elbow solver room to bend.
                                position = new Vector3(-0.13f, 1.82f, 0.18f),
                                // Keep the mirrored -90° Z that aligns the left wrist
                                // with its forearm, then roll 180° around the hand's
                                // longitudinal local X axis to turn the palm over.
                                // In Unity's Euler composition that is (0,180,-90).
                                rotation = new Vector3(0f, 180f, -90f)
                            },
                            new IKController.Target
                            {
                                avatarGoal = AvatarIKGoal.RightHand,
                                position = new Vector3(0.13f, 1.82f, 0.18f),
                                rotation = new Vector3(0f, 0f, 90f)
                            }
                        });

                        // AddIKController reuses the avatar's existing component after
                        // the first ride. SetTargets only changes its list; unlike the
                        // component's one-time Start, it does not rebuild the rig. That
                        // made whichever tier was ridden second lose the hand grip (most
                        // visibly wood -> Sonic). Rebuild immediately when Start has
                        // already initialized this controller. A newly added controller
                        // has no rig yet and will apply these targets from Start normally.
                        if (rideIk.animator != null)
                            rideIk.ModifyRig();
                    }
                }
            }
            else if (rideIk != null)
            {
                // SetTargets only swaps the list; it does not restore the rig's
                // TwoBoneIKConstraint targets/weights. Cleanup + ModifyRig does.
                rideIk.Cleanup();
                if (previousIkTargets != null)
                {
                    rideIk.SetTargets(previousIkTargets);
                    rideIk.ModifyRig();
                }
                rideIk = null;
                previousIkTargets = null;
            }
        }

        private static void AlignToTravel(EntityPlayerLocal targetPlayer, float t)
        {
            Vector3 tangent = ZiplineLink.Tangent(start, end, t);
            Vector3 angles = Quaternion.LookRotation(tangent, Vector3.up).eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, angles.x);
            targetPlayer.SetRotation(new Vector3(pitch, angles.y, 0f));
        }

        private static void ApplyPosition(EntityPlayerLocal targetPlayer, float t)
        {
            Vector3 cablePoint = ZiplineLink.Point(start, end, t);
            Vector3 tangent = ZiplineLink.Tangent(start, end, t);
            if (trolleyVisual != null)
            {
                trolleyVisual.SetPositionAndRotation(
                    cablePoint,
                    Quaternion.LookRotation(tangent, Vector3.up));
            }

            Vector3 target = cablePoint + Vector3.down * HangOffset;
            ResetFallState(targetPlayer, true);
            targetPlayer.SetPosition(target, true);
        }

        private static void ResetFallState(EntityPlayerLocal targetPlayer, bool riding)
        {
            targetPlayer.motion = Vector3.zero;
            targetPlayer.fallDistance = 0f;
            targetPlayer.fallVelY = 0f;
            targetPlayer.fallLastMotion = Vector3.zero;
            targetPlayer.fallLastY = targetPlayer.position.y;
            targetPlayer.onGround = riding;
        }
    }
}
