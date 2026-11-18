using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// A jump from one point to another that peaks <see cref="Apex"/> metres up and comes down with the player's
    /// heavier fall gravity, so it looks like an ordinary (big) jump. Shared by the player's flight
    /// (<see cref="PlayerMovement.LaunchTo"/>) and the pad's editor preview, so the drawn arc is the arc flown.
    /// </summary>
    public readonly struct JumpArc
    {
        public const float DefaultRiseGravity = 32f;
        public const float DefaultFallGravity = 48f;

        public readonly Vector3 Start, End;
        public readonly float Apex;
        public readonly float RiseGravity, FallGravity;
        public readonly float RiseTime, Duration;
        readonly Vector3 planarVelocity;

        /// <param name="arcHeight">How far above the higher of the two points the arc peaks.</param>
        public JumpArc(Vector3 start, Vector3 end, float arcHeight,
            float riseGravity = DefaultRiseGravity, float fallGravity = DefaultFallGravity)
        {
            Start = start;
            End = end;
            RiseGravity = riseGravity;
            FallGravity = fallGravity;
            Apex = Mathf.Max(start.y, end.y) + Mathf.Max(0.1f, arcHeight);
            RiseTime = Mathf.Sqrt(2f * (Apex - start.y) / riseGravity);
            Duration = RiseTime + Mathf.Sqrt(2f * (Apex - end.y) / fallGravity);
            planarVelocity = Vector3.ProjectOnPlane(end - start, Vector3.up) / Duration;
        }

        public Vector3 PositionAt(float time)
        {
            time = Mathf.Clamp(time, 0f, Duration);
            float y = time <= RiseTime
                ? Apex - 0.5f * RiseGravity * (RiseTime - time) * (RiseTime - time)
                : Apex - 0.5f * FallGravity * (time - RiseTime) * (time - RiseTime);
            Vector3 p = Start + planarVelocity * time;
            p.y = y;
            return p;
        }

        public float VerticalVelocityAt(float time)
            => time <= RiseTime ? RiseGravity * (RiseTime - time) : -FallGravity * (time - RiseTime);

        public Vector3 PlanarVelocity => planarVelocity;
    }
}
