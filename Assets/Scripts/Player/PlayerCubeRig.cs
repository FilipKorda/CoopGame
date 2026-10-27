using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Poses the cube figure from how the player actually moves, so it looks the same on every client without any
    /// networking. Standing still it holds a light fighting stance (left foot forward, torso turned left side
    /// forward, knees and elbows bent, fists by the hips) and breathes; moving, it blends into the walk cycle:
    /// legs and arms swing in opposite pairs, knees bend as a leg swings through, the whole figure leans forward, the
    /// torso turns with the arms and sways over the standing leg, and the body dips between steps.
    /// Off the ground (a short ray under the feet finds nothing) it jumps: legs push off and arms swing up while it
    /// rises fast, knees tuck near the top, legs reach for the ground and arms go out while it falls, and it crouches
    /// on landing, deeper the harder it lands. Holding still in the air (hanging from a ledge) it moves close to the
    /// wall, puts its hands on the ledge edge and hangs with its knees bent.
    /// Lying down or swimming (figure tipped over) it drops the stance and the jump.
    /// </summary>
    public class PlayerCubeRig : MonoBehaviour
    {
        /// <summary>One full-body pose (degrees).</summary>
        [System.Serializable]
        public class BodyPose
        {
            [Tooltip("Left leg: hip (X negative = forward, Z negative = out), knee bend, ankle (positive = toes down).")]
            public Vector3 hipLeft; public float kneeLeft; public float ankleLeft;
            [Tooltip("Right leg: hip (X negative = forward, Z positive = out), knee bend, ankle.")]
            public Vector3 hipRight; public float kneeRight; public float ankleRight;
            [Tooltip("Left arm: shoulder (X negative = arm forward/up, Z negative = out), elbow bend.")]
            public Vector3 shoulderLeft; public float elbowLeft;
            [Tooltip("Right arm: shoulder (X negative = arm forward/up, Z positive = out), elbow bend.")]
            public Vector3 shoulderRight; public float elbowRight;
            [Tooltip("Torso leaning forward.")]
            public float torsoLean;

            public BodyPose() { }

            public BodyPose(Vector3 hipLeft, float kneeLeft, float ankleLeft, Vector3 hipRight, float kneeRight, float ankleRight,
                Vector3 shoulderLeft, float elbowLeft, Vector3 shoulderRight, float elbowRight, float torsoLean)
            {
                this.hipLeft = hipLeft; this.kneeLeft = kneeLeft; this.ankleLeft = ankleLeft;
                this.hipRight = hipRight; this.kneeRight = kneeRight; this.ankleRight = ankleRight;
                this.shoulderLeft = shoulderLeft; this.elbowLeft = elbowLeft;
                this.shoulderRight = shoulderRight; this.elbowRight = elbowRight; this.torsoLean = torsoLean;
            }
        }

        [Header("Joints")]
        [Tooltip("Pelvis and both legs: lowered a little in the stance.")]
        [SerializeField] Transform hips;
        [SerializeField] Transform hipLeft, kneeLeft, ankleLeft, hipRight, kneeRight, ankleRight;
        [SerializeField] Transform shoulderLeft, elbowLeft, shoulderRight, elbowRight;
        [Tooltip("Upper body (chest, head, arms): sways and bobs a little with the steps.")]
        [SerializeField] Transform upperBody;
        [Tooltip("Head and neck: turned back towards the front when the stance turns the torso.")]
        [SerializeField] Transform[] headParts;

        [Header("Walk")]
        [Tooltip("Distance covered by one step: longer = slower, calmer legs.")]
        [SerializeField] float stepLength = 2.4f;
        [Tooltip("Speed at which the swing is full.")]
        [SerializeField] float fullSwingSpeed = 8f;
        [Tooltip("How fast the leg swing builds up when starting off (per second): high, so the first step comes at once.")]
        [SerializeField] float swingBuildUp = 12f;
        [SerializeField] float hipSwing = 34f;
        [SerializeField] float kneeBend = 55f;
        [SerializeField] float shoulderSwing = 26f;
        [Tooltip("Elbow bend at the start of walking / extra while walking.")]
        [SerializeField] float elbowRest = 12f;
        [SerializeField] float elbowWalk = 30f;
        [Tooltip("Arms held a little away from the body (degrees), like the reference figure.")]
        [SerializeField] float armSpread = 8f;
        [Tooltip("Legs held a little apart (degrees), like the reference figure.")]
        [SerializeField] float legSpread = 3.5f;
        [Tooltip("How far the body dips (metres) when the legs are furthest apart.")]
        [SerializeField] float bob = 0.035f;

        [Header("Walk: body motion")]
        [Tooltip("Whole figure leaning forward from the feet while walking (degrees).")]
        [SerializeField] float walkLean = 6f;
        [Tooltip("Extra forward lean of the torso (degrees).")]
        [SerializeField] float torsoLean = 4f;
        [Tooltip("Hips turning towards the leg that steps forward (degrees).")]
        [SerializeField] float hipTwist = 0f;
        [Tooltip("Hip on the side of the swinging leg dropping a little (degrees).")]
        [SerializeField] float hipDrop = 0f;
        [Tooltip("Torso turning the other way than the hips (degrees).")]
        [SerializeField] float torsoTwist = 7f;
        [Tooltip("Torso leaning over the leg it stands on (degrees) and shifting over it (metres).")]
        [SerializeField] float torsoRoll = 3f;
        [SerializeField] float torsoShift = 0f;
        [Tooltip("How much the head turns back against the torso twist, so it keeps looking ahead (0..1).")]
        [SerializeField, Range(0f, 1f)] float headSteady = 0.8f;

        [Header("Idle stance (standing still)")]
        [SerializeField] bool useStance = true;
        [Tooltip("Seconds to settle back into the stance after stopping (leaving it for the walk takes 40% of this).")]
        [SerializeField] float stanceBlendTime = 0.25f;
        [Tooltip("How far the hips sink (metres) so the bent legs still reach the ground.")]
        [SerializeField] float stanceDrop = 0.035f;
        [Tooltip("Pelvis and legs turned (Y) so the left side comes forward.")]
        [SerializeField] Vector3 stanceHips = new Vector3(0f, 20f, 0f);
        [Tooltip("Torso: X = lean forward, Y = turn (left side forward).")]
        [SerializeField] Vector3 stanceUpperBody = new Vector3(6f, 20f, 0f);
        [Tooltip("Head turn against the torso, so it still looks ahead.")]
        [SerializeField] float stanceHeadTurn = -16f;
        [Tooltip("Hip angles: X negative = leg forward, Z = leg out to the side.")]
        [SerializeField] Vector3 stanceHipLeft = new Vector3(-20f, 0f, -9f);
        [SerializeField] Vector3 stanceHipRight = new Vector3(6f, 0f, 9f);
        [SerializeField] float stanceKneeLeft = 24f, stanceKneeRight = 14f;
        [Tooltip("Ankles: keep both feet flat on the ground.")]
        [SerializeField] Vector3 stanceAnkleLeft = new Vector3(-4f, 0f, 9f);
        [SerializeField] Vector3 stanceAnkleRight = new Vector3(-17.7f, 0f, -9f);
        [Tooltip("Shoulders: X negative = arm forward, Y = turns the bent forearm inwards, Z = arm out to the side.")]
        [SerializeField] Vector3 stanceShoulderLeft = new Vector3(-10f, 20f, -18f);
        [SerializeField] Vector3 stanceShoulderRight = new Vector3(2f, -20f, 18f);
        [SerializeField] float stanceElbowLeft = 38f, stanceElbowRight = 40f;
        [Tooltip("Breathing: how far the chest rises (metres) and how long one breath takes.")]
        [SerializeField] float breath = 0.01f;
        [SerializeField] float breathPeriod = 3.4f;

        [Header("Jump")]
        [SerializeField] bool useJump = true;
        [Tooltip("Ground this far under the feet (metres) still counts as standing on it.")]
        [SerializeField] float groundCheck = 0.25f;
        [SerializeField] LayerMask groundLayers = Physics.DefaultRaycastLayers;
        [Tooltip("Rising this fast (m/s) = push-off pose; around this speed = knees tucked; falling this fast = reaching for the ground.")]
        [SerializeField] float pushOffSpeed = 9f;
        [SerializeField] float tuckSpeed = 3f;
        [SerializeField] float reachSpeed = -6f;
        [Tooltip("Legs pushing off, right arm swinging forward and up, left arm back (right after leaving the ground).")]
        [SerializeField] BodyPose pushOff = new BodyPose(new Vector3(-25f, 0f, -5f), 30f, 15f, new Vector3(15f, 0f, 5f), 10f, 40f, new Vector3(25f, 0f, -12f), 20f, new Vector3(-95f, 0f, 12f), 45f, 6f);
        [Tooltip("Knees tucked (near the top of the jump).")]
        [SerializeField] BodyPose tucked = new BodyPose(new Vector3(-55f, 0f, -6f), 85f, 20f, new Vector3(-25f, 0f, 6f), 75f, 25f, new Vector3(-20f, 0f, -40f), 60f, new Vector3(-50f, 0f, 35f), 60f, 12f);
        [Tooltip("Falling: legs reaching for the ground, arms out for balance.")]
        [SerializeField] BodyPose falling = new BodyPose(new Vector3(-25f, 0f, -8f), 25f, 5f, new Vector3(0f, 0f, 8f), 30f, 10f, new Vector3(-15f, 0f, -40f), 30f, new Vector3(-25f, 0f, 40f), 30f, 2f);

        [Header("Hanging from a ledge")]
        [Tooltip("Hanging (holding still in the air): knees bent with the shins back, away from the wall. The arms are " +
                 "placed on the ledge by themselves; their angles here only matter while grabbing on.")]
        [SerializeField] BodyPose hanging = new BodyPose(new Vector3(-2f, 0f, -4f), 60f, 20f, new Vector3(6f, 0f, 4f), 45f, 25f, new Vector3(-150f, 0f, -8f), 10f, new Vector3(-150f, 0f, 8f), 10f, 0f);
        [Tooltip("Where the hands hold the ledge, from the feet (metres): height and distance in front. Taken from the " +
                 "player's movement settings (Hand Height, Hang Wall Offset) when it has them.")]
        [SerializeField] float hangHandHeight = 1.95f;
        [SerializeField] float hangHandReach = 0.55f;
        [Tooltip("Half the gap between the hands on the ledge (metres).")]
        [SerializeField] float hangHandSpacing = 0.22f;
        [Tooltip("While hanging the body moves this far towards the wall and down (metres), so it hangs close to the " +
                 "wall from nearly straight arms (only the figure moves, not the player's collider).")]
        [SerializeField] float hangCloser = 0.33f;
        [SerializeField] float hangLower = 0.15f;
        [Tooltip("The point of the hand that holds on, measured from the elbow joint (metres), and how far above the " +
                 "ledge edge it rests.")]
        [SerializeField] Vector3 gripOffset = new Vector3(0f, -0.36f, 0.006f);
        [SerializeField] float gripAbove = 0f;
        [Tooltip("Moving along the ledge, hand over hand: distance covered by one full cycle of both hands (metres).")]
        [SerializeField] float shimmyCycle = 0.8f;
        [Tooltip("Part of the cycle each hand holds still on the ledge (the rest it moves on to the next hold).")]
        [SerializeField, Range(0.5f, 0.9f)] float shimmyHold = 0.6f;
        [Tooltip("How high a hand lifts off the ledge while moving on (metres).")]
        [SerializeField] float shimmyLift = 0.07f;
        [Tooltip("Legs trailing behind the way it moves along the ledge, and swaying with the hands (degrees).")]
        [SerializeField] float shimmyLegTrail = 8f;
        [SerializeField] float shimmyLegSway = 3f;

        [Header("Zipline")]
        [Tooltip("Riding a zipline: hands this high above the feet (just under the cable; the line's Hang Depth) and this far apart.")]
        [SerializeField] float zipHandHeight = 2.2f;
        [SerializeField] float zipHandSpacing = 0.12f;

        [Header("Cutscene hands")]
        [Tooltip("Seconds for the hands to go to (and come back from) points a cutscene gives them (SetHandTargets).")]
        [SerializeField] float handTargetBlend = 0.3f;

        [Header("Double jump: front flip")]
        [SerializeField] bool useFlip = true;
        [Tooltip("How long one flip takes (seconds).")]
        [SerializeField] float flipTime = 0.5f;
        [Tooltip("Height above the feet the figure turns around (metres): about the waist.")]
        [SerializeField] float flipPivotHeight = 1.0f;
        [Tooltip("The figure keeps the way it faced when the flip started (turning the player mid-flip, e.g. letting go of aim, " +
                 "doesn't swing it round), then turns to the new way at this speed (degrees per second).")]
        [SerializeField] float flipTurnAfter = 540f;
        [Tooltip("Tight tuck during the flip: knees to the chest, arms around the shins.")]
        [SerializeField] BodyPose flipTuck = new BodyPose(new Vector3(-100f, 0f, -6f), 120f, 30f, new Vector3(-95f, 0f, 6f), 115f, 30f, new Vector3(-55f, 0f, -12f), 75f, new Vector3(-55f, 0f, 12f), 75f, 25f);
        [Tooltip("Double jump while aiming and holding A or D: a cartwheel that way instead of the flip.")]
        [SerializeField] bool useCartwheel = true;
        [Tooltip("How sideways the stick must push for a cartwheel (0..1; A or D alone = 1, with W or S as well = 0.7).")]
        [SerializeField, Range(0f, 1f)] float cartwheelSideways = 0.6f;
        [Tooltip("Star shape during the cartwheel: arms up and out, legs spread wide.")]
        [SerializeField] BodyPose cartwheel = new BodyPose(new Vector3(0f, 0f, -35f), 5f, 10f, new Vector3(0f, 0f, 35f), 5f, 10f, new Vector3(0f, 0f, -150f), 5f, new Vector3(0f, 0f, 150f), 5f, 0f);

        [Header("Landing")]
        [Tooltip("Crouch on landing (at full strength). Ankles keep the feet flat.")]
        [SerializeField] BodyPose landing = new BodyPose(new Vector3(-45f, 0f, -8f), 80f, -35f, new Vector3(-40f, 0f, 8f), 80f, -40f, new Vector3(-35f, 0f, -20f), 45f, new Vector3(-35f, 0f, 20f), 45f, 18f);
        [Tooltip("How far the body sinks (metres) in the full crouch, so the bent legs still reach the ground.")]
        [SerializeField] float landDrop = 0.2f;
        [Tooltip("How long the landing crouch lasts (seconds).")]
        [SerializeField] float landTime = 0.32f;
        [Tooltip("Landing speeds (m/s): slower than the first = no crouch, the second and faster = full crouch.")]
        [SerializeField] float landSoftSpeed = 5f;
        [SerializeField] float landHardSpeed = 16f;

        [Header("Smoothing")]
        [Tooltip("How quickly the pose follows (higher = snappier).")]
        [SerializeField] float smoothing = 10f;
        [Tooltip("The same while jumping and landing (snappier, those poses are short).")]
        [SerializeField] float jumpSmoothing = 16f;

        // Rest positions of the hips and upper body, saved when the figure is built.
        [SerializeField, HideInInspector] Vector3 restHips, restUpperBody;

        const int HipL = 0, KneeL = 1, AnkleL = 2, HipR = 3, KneeR = 4, AnkleR = 5, ShoulderL = 6, ElbowL = 7,
            ShoulderR = 8, ElbowR = 9, HipsJ = 10, Torso = 11, Head = 12, JointCount = 13;
        readonly Quaternion[] groundPose = new Quaternion[JointCount], airPose = new Quaternion[JointCount],
            landPose = new Quaternion[JointCount], poseA = new Quaternion[JointCount], poseB = new Quaternion[JointCount];
        Transform[] joints;

        Vector3 lastPosition;
        float phase, amount, moving, breathTime;
        Vector3 upperBodyBase, hipsBase;
        bool snap = true;
        // Direction of travel in the figure's own frame (smoothed), and how much the feet must tilt back against the lean.
        Vector3 travelDir = Vector3.forward;
        float footPitch, footRoll;
        // Jump state: vertical speed (smoothed), off the ground (weight), holding still in the air, landing crouch.
        float verticalSpeed, air, hang, land;
        bool wasAirborne;
        float airTime, airLowestSpeed, stillInAir, landClock = 10f, landStrength;
        // Moving along a ledge: cycle position (0..1), direction along the ledge (+1 = to the right), and weight.
        float shimmyProgress, shimmyDirection = 1f, shimmy;
        // Riding a zipline (weight).
        float zip;
        // Cutscene hand targets (e.g. the edge of a gate being lifted) and how far the hands are on them (weight).
        Transform handTargetLeft, handTargetRight;
        bool handTargetsOn;
        float handTargetWeight;
        // Front flip on double jump: time into the flip (negative = not flipping), double jumps already seen.
        float flipClock = -1f;
        // 0 = front flip, 1 / -1 = cartwheel to the right / left.
        float flipSide;
        // Heading held during a flip: the way the player faced when it started (world yaw), and the figure's turn away
        // from the player's current facing (eases back to 0 afterwards).
        float flipStartYaw, headingHold;
        int seenDoubleJumps = -1;
        // Whole-figure lean and offset (eased), the flip turns them around the waist.
        Quaternion leanRotation = Quaternion.identity;
        Vector3 rootOffset;
        readonly RaycastHit[] groundHits = new RaycastHit[8];
        // What the feet stood on last frame (cutscene stand-ins only), and where on it: when that moves (a boat), it
        // carries the figure, which isn't walking.
        Transform groundUnder, carriedBy;
        Vector3 carriedAt;
        CharacterController body;
        PlayerMovement movement;

        // Where the figure stands: its parent, since the rig moves the figure itself (hanging) and must not mistake
        // that for the player moving.
        Vector3 Origin => transform.parent != null ? transform.parent.position : transform.position;
        float ParentYaw => transform.parent != null ? transform.parent.eulerAngles.y : 0f;

        void Awake()
        {
            CaptureRest();
            body = GetComponentInParent<CharacterController>();
            // Hands go exactly where the movement holds the player on a ledge.
            movement = GetComponentInParent<PlayerMovement>();
            if (movement != null)
            {
                hangHandHeight = movement.HandHeight;
                hangHandReach = movement.HangWallOffset;
            }
        }

        void CaptureRest()
        {
            if (upperBody != null) upperBodyBase = restUpperBody != Vector3.zero ? restUpperBody : upperBody.localPosition;
            if (hips != null) hipsBase = restHips != Vector3.zero ? restHips : hips.localPosition;
            joints = new[] { hipLeft, kneeLeft, ankleLeft, hipRight, kneeRight, ankleRight, shoulderLeft, elbowLeft, shoulderRight, elbowRight, hips, upperBody };
        }

        /// <summary>Shows the standing stance right away (to see it in the editor without playing).</summary>
        [ContextMenu("Preview stance")]
        public void PreviewStance() => Preview(0f, 0f, 0f, 0f, 0f, 0f, 0f);

        [ContextMenu("Reset to rest pose")]
        public void PreviewRest() => Preview(1f, 0f, 0f, 0f, 0f, 0f, 0f);

        [ContextMenu("Preview jump (push-off)")]
        void PreviewPushOff() => PreviewJump(pushOffSpeed, 0f);

        [ContextMenu("Preview jump (top, knees tucked)")]
        void PreviewTucked() => PreviewJump(tuckSpeed, 0f);

        [ContextMenu("Preview jump (falling)")]
        void PreviewFalling() => PreviewJump(reachSpeed, 0f);

        [ContextMenu("Preview hanging")]
        void PreviewHanging() => PreviewJump(0f, 1f);

        [ContextMenu("Preview landing")]
        void PreviewLandingFull() => PreviewLanding(1f);

        /// <summary>Shows one moment of the walk cycle (cycle 0..1, strength 0..1) right away, for previews.</summary>
        public void PreviewWalk(float cycle, float strength) => Preview(1f, Mathf.Clamp01(strength), Mathf.Repeat(cycle, 1f) * Mathf.PI * 2f, 0f, 0f, 0f, 0f);

        /// <summary>Shows the jump pose for a vertical speed (m/s; hang 1 = hanging from a ledge) right away, for previews.</summary>
        public void PreviewJump(float verticalSpeed, float hanging) => Preview(1f, 0f, 0f, 1f, Mathf.Clamp01(hanging), 0f, verticalSpeed);

        /// <summary>Shows the cartwheel at a point (0..1) to the right (side 1) or left (-1) right away, for previews.</summary>
        public void PreviewCartwheel(float progress, float side)
        {
            flipClock = Mathf.Clamp01(progress) * flipTime;
            flipSide = Mathf.Sign(side);
            Preview(1f, 0f, 0f, 1f, 0f, 0f, 6f);
            flipClock = -1f;
            flipSide = 0f;
        }

        /// <summary>Shows the double-jump flip at a point (0..1) right away, for previews.</summary>
        public void PreviewFlip(float progress)
        {
            flipClock = Mathf.Clamp01(progress) * flipTime;
            flipSide = 0f;
            PreviewJump(6f, 0f);
            flipClock = -1f;
        }

        /// <summary>
        /// Cutscenes: both hands go to these points (they follow them if they move, e.g. the edge of a gate being
        /// lifted) until switched off; on and off blend over Hand Target Blend.
        /// </summary>
        public void SetHandTargets(Transform left, Transform right, bool on)
        {
            if (on)
            {
                handTargetLeft = left;
                handTargetRight = right;
            }
            handTargetsOn = on;
        }

        /// <summary>Shows the landing crouch (strength 0..1) right away, for previews.</summary>
        public void PreviewLanding(float strength) => Preview(1f, 0f, 0f, 0f, 0f, Mathf.Clamp01(strength), 0f);

        void Preview(float walkMix, float walkStrength, float cyclePhase, float airborne, float hanging, float landing, float vSpeed)
        {
            CaptureRest();
            moving = walkMix; amount = walkStrength; phase = cyclePhase;
            air = airborne; hang = hanging; land = landing; verticalSpeed = vSpeed;
            travelDir = Vector3.forward;
            Pose(1f, 0f);
        }

        void OnEnable()
        {
            lastPosition = Origin;
            carriedBy = null;
            snap = true;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            // Only the figure's own motion counts: on a moving boat, the boat's isn't a step.
            Vector3 delta = Origin - (carriedBy != null ? carriedBy.TransformPoint(carriedAt) : lastPosition);
            lastPosition = Origin;
            // A teleport (respawn, cutscene end) isn't a step or a jump.
            bool teleported = delta.magnitude > 2f;
            Vector3 travel = new Vector3(delta.x, 0f, delta.z);
            float speed = teleported ? 0f : Mathf.Min(travel.magnitude / dt, 12f);
            verticalSpeed = teleported ? 0f : Mathf.Lerp(verticalSpeed, delta.y / dt, 1f - Mathf.Exp(-20f * dt));

            float target = speed < 0.3f ? 0f : Mathf.Clamp01(speed / fullSwingSpeed);
            // Starting off from a standstill steps straight away: the cycle starts with the left leg in front (as in
            // the stance) and the right one swinging through.
            bool startingOff = target > 0f && amount < 0.05f;
            // The swing follows the speed at once when speeding up (the player reaches full speed in a blink), so the
            // figure never glides; it eases out when slowing down.
            amount = Mathf.MoveTowards(amount, target, (target > amount ? swingBuildUp : 4f) * dt);
            // Which way the figure travels compared with where it faces (aiming or holding something can make it walk
            // sideways or backwards).
            if (speed >= 0.3f)
            {
                Transform frame = transform.parent != null ? transform.parent : transform;
                Vector3 local = frame.InverseTransformDirection(travel);
                local.y = 0f;
                if (local.sqrMagnitude > 1e-8f) travelDir = Vector3.Slerp(travelDir, local.normalized, 1f - Mathf.Exp(-8f * dt));
            }
            else travelDir = Vector3.Slerp(travelDir, Vector3.forward, 1f - Mathf.Exp(-4f * dt));
            // Walking backwards steps backwards.
            float stepDirection = travelDir.z < -0.3f ? -1f : 1f;
            if (startingOff) phase = Mathf.PI * 1.5f;
            phase = Mathf.Repeat(phase + stepDirection * speed * dt / stepLength * Mathf.PI, Mathf.PI * 2f);
            if (amount <= 0.001f) phase = Mathf.MoveTowardsAngle(phase * Mathf.Rad2Deg, 0f, 180f * dt) * Mathf.Deg2Rad;

            // 0 = stance, 1 = walk. No stance when the figure is tipped over (downed, swimming) or it's switched off.
            // (Read from the parent: the rig tilts the figure itself when it leans or flips.)
            bool upright = Vector3.Dot((transform.parent != null ? transform.parent : transform).up, Vector3.up) > 0.8f;
            bool stand = useStance && upright && speed < 0.3f;
            // Leaving the stance is quick (the first step must show at once); settling back into it is calmer.
            moving = Mathf.MoveTowards(moving, stand ? 0f : 1f, dt / Mathf.Max(0.01f, stand ? stanceBlendTime : stanceBlendTime * 0.4f));
            if (snap) moving = stand ? 0f : 1f;
            breathTime += dt;
            float breathe = Mathf.Sin(breathTime * Mathf.PI * 2f / Mathf.Max(0.5f, breathPeriod));

            UpdateJump(dt, upright && useJump && !teleported && !snap);
            // Stand-ins (no player movement) remember what they stand on. Real players are left as they were.
            carriedBy = movement == null && !wasAirborne ? groundUnder : null;
            if (carriedBy != null) carriedAt = carriedBy.InverseTransformPoint(Origin);

            // Moving along a ledge while hanging drives the hand-over-hand cycle (by distance, like the steps).
            Transform ledgeFrame = transform.parent != null ? transform.parent : transform;
            float alongSpeed = teleported ? 0f : Vector3.Dot(travel, ledgeFrame.right) / dt;
            bool shimmying = hang > 0.5f && Mathf.Abs(alongSpeed) > 0.2f;
            if (shimmying)
            {
                shimmyDirection = Mathf.Sign(alongSpeed);
                shimmyProgress = Mathf.Repeat(shimmyProgress + Mathf.Abs(alongSpeed) * dt / Mathf.Max(0.1f, shimmyCycle), 1f);
            }
            shimmy = Mathf.MoveTowards(shimmy, shimmying ? 1f : 0f, dt / 0.12f);

            // A double jump starts a front flip. It finishes fast if the figure lands first and stops on a ledge.
            if (movement != null)
            {
                int doubleJumps = movement.DoubleJumpCountForVisuals;
                if (seenDoubleJumps < 0 || teleported) seenDoubleJumps = doubleJumps;
                else if (doubleJumps != seenDoubleJumps)
                {
                    seenDoubleJumps = doubleJumps;
                    if (useFlip && upright)
                    {
                        flipClock = 0f;
                        flipStartYaw = ParentYaw;
                        // Made while aiming with the stick pushed mostly sideways (A or D): cartwheel that way.
                        float sideways = movement.DoubleJumpSidewaysForVisuals;
                        flipSide = useCartwheel && movement.DoubleJumpAimedForVisuals && Mathf.Abs(sideways) >= cartwheelSideways ? Mathf.Sign(sideways) : 0f;
                    }
                }
            }
            if (flipClock >= 0f)
            {
                flipClock += dt * (air > 0.5f ? 1f : 4f);
                if (flipClock >= flipTime || hang > 0.5f || !upright) flipClock = -1f;
            }
            // During a flip the figure keeps its heading; afterwards it turns to the player's facing.
            if (flipClock >= 0f) headingHold = Mathf.DeltaAngle(ParentYaw, flipStartYaw);
            else headingHold = Mathf.MoveTowardsAngle(headingHold, 0f, flipTurnAfter * dt);

            bool handTargetsValid = handTargetsOn && handTargetLeft != null && handTargetRight != null;
            handTargetWeight = Mathf.MoveTowards(handTargetWeight, handTargetsValid ? 1f : 0f, dt / Mathf.Max(0.01f, handTargetBlend));

            float sharpness = Mathf.Lerp(smoothing, jumpSmoothing, Mathf.Max(air, land > 0.001f ? 1f : 0f));
            float k = snap ? 1f : 1f - Mathf.Exp(-sharpness * dt);
            snap = false;
            Pose(k, breathe);
        }

        // Off the ground, hanging still, landing: decided from the ground under the feet and the vertical speed.
        void UpdateJump(float dt, bool allowed)
        {
            float gap = GroundGap();
            // Just after a jump the ground is still close, but the figure is clearly rising off it.
            // The player's movement says when it hangs from a ledge (on every client); figures without one (cutscene
            // stand-ins) count holding still in the air as hanging.
            bool knownHang = allowed && movement != null && movement.IsHangingForVisuals;
            bool zipping = allowed && movement != null && movement.IsZipliningForVisuals;
            bool airborne = allowed && (knownHang || zipping || gap > groundCheck || (verticalSpeed > 1.5f && gap > 0.03f));
            if (airborne)
            {
                if (!wasAirborne) { airTime = 0f; airLowestSpeed = 0f; stillInAir = 0f; }
                airTime += dt;
                airLowestSpeed = Mathf.Min(airLowestSpeed, verticalSpeed);
                // A jump passes its top in a blink; holding still in the air for longer is hanging from a ledge.
                stillInAir = Mathf.Abs(verticalSpeed) < 0.6f ? stillInAir + dt : 0f;
            }
            else
            {
                if (wasAirborne && airTime > 0.08f)
                {
                    float impact = -airLowestSpeed;
                    landStrength = impact < landSoftSpeed ? 0f : Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(landSoftSpeed, landHardSpeed, impact));
                    landClock = 0f;
                }
                stillInAir = 0f;
            }
            wasAirborne = airborne;

            air = Mathf.MoveTowards(air, airborne ? 1f : 0f, dt / 0.08f);
            bool hanging = movement != null ? knownHang : stillInAir > 0.12f;
            hang = Mathf.MoveTowards(hang, hanging ? 1f : 0f, dt / 0.1f);
            zip = Mathf.MoveTowards(zip, zipping ? 1f : 0f, dt / 0.12f);
            landClock += dt;
            land = allowed ? landStrength * LandCurve(landClock / Mathf.Max(0.05f, landTime)) * (1f - air) : 0f;
        }

        // Quick dip into the crouch, slower rise out of it.
        static float LandCurve(float t)
        {
            if (t <= 0f || t >= 1f) return 0f;
            if (t < 0.18f) return t / 0.18f;
            return 1f - Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.82f);
        }

        // Distance from the feet down to the nearest ground (anything but the player's own body), or infinity.
        float GroundGap()
        {
            const float start = 0.5f;
            int count = Physics.RaycastNonAlloc(Origin + Vector3.up * start, Vector3.down, groundHits,
                start + groundCheck + 0.5f, groundLayers, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            groundUnder = null;
            for (int i = 0; i < count; i++)
            {
                var col = groundHits[i].collider;
                if (body != null ? col.transform.IsChildOf(body.transform) : col.transform.IsChildOf(transform)) continue;
                float gap = groundHits[i].distance - start;
                if (gap >= best) continue;
                best = gap;
                groundUnder = col.transform;
            }
            return best;
        }

        // Works out the stance / walk pose, the jump pose and the landing crouch, mixes them and eases every joint there
        // (k = 1 jumps straight there).
        void Pose(float k, float breathe)
        {
            if (joints == null) CaptureRest();
            float still = 1f - moving;
            // How far the figure leans into the direction it travels (only a little when walking backwards); the ankles
            // tilt the feet back by the same amount.
            float leanForward = travelDir.z >= 0f ? travelDir.z : travelDir.z * 0.3f;
            footPitch = walkLean * amount * leanForward;
            footRoll = walkLean * amount * travelDir.x;

            // On the ground: stance and walk.
            GroundLeg(HipL, KneeL, AnkleL, phase, -legSpread, stanceHipLeft, stanceKneeLeft, stanceAnkleLeft);
            GroundLeg(HipR, KneeR, AnkleR, phase + Mathf.PI, legSpread, stanceHipRight, stanceKneeRight, stanceAnkleRight);
            GroundArm(ShoulderL, ElbowL, phase + Mathf.PI, -armSpread, stanceShoulderLeft + new Vector3(0f, 0f, -1.5f * breathe), stanceElbowLeft);
            GroundArm(ShoulderR, ElbowR, phase, armSpread, stanceShoulderRight + new Vector3(0f, 0f, 1.5f * breathe), stanceElbowRight);
            // Phase 0 = left foot under the body (standing on it), PI = right foot; sin(phase) < 0 = left leg in front.
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float walk = amount * moving;
            // Hips turn towards the leg that steps forward; the side of the swinging leg drops a little.
            groundPose[HipsJ] = Mix(Quaternion.Euler(0f, -s * hipTwist * amount, -c * hipDrop * amount), Quaternion.Euler(stanceHips));
            // Torso leans a bit more forward, turns against the hips and leans over the standing leg.
            groundPose[Torso] = Mix(Quaternion.Euler(torsoLean * amount, s * torsoTwist * amount, c * torsoRoll * amount), Quaternion.Euler(stanceUpperBody));
            groundPose[Head] = Mix(Quaternion.Euler(0f, -s * torsoTwist * amount * headSteady, 0f), Quaternion.Euler(0f, stanceHeadTurn, 0f));
            // The body dips while the legs are furthest apart and is highest over the standing foot.
            float dip = -bob * amount * s * s * moving;
            Vector3 hipsGround = hipsBase + Vector3.down * (stanceDrop * still) + Vector3.up * dip;
            Vector3 upperGround = upperBodyBase + Vector3.up * dip + Vector3.left * (c * torsoShift * walk)
                + Vector3.up * ((breath * breathe - stanceDrop) * still);

            // In the air: push-off while rising fast, knees tucked near the top, reaching down while falling;
            // hanging from a ledge on top of that.
            if (air > 0.001f)
            {
                if (verticalSpeed >= tuckSpeed) { Fill(poseA, tucked); Fill(poseB, pushOff); }
                else { Fill(poseA, tucked); Fill(poseB, falling); }
                float t = verticalSpeed >= tuckSpeed ? Mathf.InverseLerp(tuckSpeed, pushOffSpeed, verticalSpeed) : Mathf.InverseLerp(tuckSpeed, reachSpeed, verticalSpeed);
                for (int i = 0; i < JointCount; i++) airPose[i] = Quaternion.Slerp(poseA[i], poseB[i], t);
                float hangPose = Mathf.Max(hang, zip);
                if (hangPose > 0.001f)
                {
                    Fill(poseB, hanging);
                    // Moving along the ledge: the legs trail behind and sway with the hands.
                    float legRoll = (-shimmyDirection * shimmyLegTrail + Mathf.Sin(shimmyProgress * Mathf.PI * 4f) * shimmyLegSway) * shimmy;
                    poseB[HipsJ] = Quaternion.Euler(0f, 0f, legRoll);
                    for (int i = 0; i < JointCount; i++) airPose[i] = Quaternion.Slerp(airPose[i], poseB[i], hangPose);
                }
            }
            // Flipping: tuck tight in the middle of the flip, open up at the start and the end.
            float flipT = flipClock >= 0f ? Mathf.Clamp01(flipClock / Mathf.Max(0.05f, flipTime)) : -1f;
            if (flipT >= 0f && air > 0.001f)
            {
                float tuck = Mathf.Clamp01(Mathf.Sin(flipT * Mathf.PI) * (flipSide != 0f ? 2.5f : 1.6f));
                Fill(poseB, flipSide != 0f ? cartwheel : flipTuck);
                for (int i = 0; i < JointCount; i++) airPose[i] = Quaternion.Slerp(airPose[i], poseB[i], tuck);
            }
            if (land > 0.001f) Fill(landPose, landing);

            for (int i = 0; i < JointCount; i++)
            {
                Quaternion q = groundPose[i];
                if (air > 0.001f) q = Quaternion.Slerp(q, airPose[i], air);
                if (land > 0.001f) q = Quaternion.Slerp(q, landPose[i], land);
                if (i == Head)
                {
                    if (headParts != null)
                        foreach (var part in headParts)
                            if (part != null) part.localRotation = Quaternion.Slerp(part.localRotation, q, k);
                }
                else if (joints[i] != null) joints[i].localRotation = Quaternion.Slerp(joints[i].localRotation, q, k);
            }

            // In the air the stance crouch and the step dip go away; the landing sinks the body into the crouch.
            if (hips != null)
                hips.localPosition = Vector3.Lerp(hips.localPosition, Settle(hipsGround, hipsBase), k);
            if (upperBody != null)
                upperBody.localPosition = Vector3.Lerp(upperBody.localPosition, Settle(upperGround, upperBodyBase), k);

            // Whole figure leans from the feet into the direction it travels (only a little when walking backwards,
            // not while hanging).
            float lean = moving * (1f - Mathf.Max(hang, zip));
            leanRotation = Quaternion.Slerp(leanRotation, Quaternion.Euler(footPitch * lean, 0f, -footRoll * lean), k);
            // Hanging: the figure moves towards the wall and down (and the hands go onto the ledge, below).
            rootOffset = Vector3.Lerp(rootOffset, new Vector3(0f, -hangLower, hangCloser) * hang, k);
            // Front flip: one full turn forward around the waist, fastest in the middle.
            float turn = flipT >= 0f ? 360f * flipT * flipT * (3f - 2f * flipT) : 0f;
            // Flip: forward around the waist; cartwheel: sideways, the head going the way it moves.
            Quaternion flip = flipT < 0f ? Quaternion.identity : flipSide != 0f ? Quaternion.Euler(0f, 0f, -flipSide * turn) : Quaternion.Euler(turn, 0f, 0f);
            Vector3 pivot = Vector3.up * flipPivotHeight;
            Quaternion spin = Quaternion.Euler(0f, headingHold, 0f) * flip;
            transform.localRotation = spin * leanRotation;
            transform.localPosition = rootOffset + pivot - spin * pivot;
            if (hang > 0.001f && transform.parent != null)
            {
                Transform frame = transform.parent;
                ReachFor(shoulderLeft, elbowLeft, frame.TransformPoint(HandHold(-1f)), frame.TransformDirection(new Vector3(-1f, 0f, -0.5f)), hang);
                ReachFor(shoulderRight, elbowRight, frame.TransformPoint(HandHold(1f)), frame.TransformDirection(new Vector3(1f, 0f, -0.5f)), hang);
            }
            // Zipline: both hands up on the cable above the head.
            if (zip > 0.001f && transform.parent != null)
            {
                Transform frame = transform.parent;
                ReachFor(shoulderLeft, elbowLeft, frame.TransformPoint(new Vector3(-zipHandSpacing, zipHandHeight, 0f)), frame.TransformDirection(new Vector3(-1f, 0f, -0.3f)), zip);
                ReachFor(shoulderRight, elbowRight, frame.TransformPoint(new Vector3(zipHandSpacing, zipHandHeight, 0f)), frame.TransformDirection(new Vector3(1f, 0f, -0.3f)), zip);
            }
            // Cutscene hand targets (the elbows out and down, as when gripping and lifting something).
            if (handTargetWeight > 0.001f && handTargetLeft != null && handTargetRight != null)
            {
                Transform frame = transform.parent != null ? transform.parent : transform;
                float w = Mathf.SmoothStep(0f, 1f, handTargetWeight);
                ReachFor(shoulderLeft, elbowLeft, handTargetLeft.position, frame.TransformDirection(new Vector3(-1f, -0.6f, -0.2f)), w);
                ReachFor(shoulderRight, elbowRight, handTargetRight.position, frame.TransformDirection(new Vector3(1f, -0.6f, -0.2f)), w);
            }
        }

        // Where a hand (side -1 = left, 1 = right) holds the ledge, in the player's frame. Moving along the ledge each
        // hand stays put for part of the cycle (sliding back relative to the body exactly as fast as the body moves on)
        // and then lifts and reaches ahead to its next hold; the hands take turns.
        Vector3 HandHold(float side)
        {
            float along = 0f, lift = 0f;
            if (shimmy > 0.001f)
            {
                float reach = shimmyHold * shimmyCycle * 0.5f;
                float u = Mathf.Repeat(shimmyProgress + (side * shimmyDirection > 0f ? 0f : 0.5f), 1f);
                if (u < shimmyHold) along = Mathf.Lerp(reach, -reach, u / shimmyHold);
                else
                {
                    float w = (u - shimmyHold) / (1f - shimmyHold);
                    along = Mathf.Lerp(-reach, reach, Mathf.SmoothStep(0f, 1f, w));
                    lift = Mathf.Sin(w * Mathf.PI) * shimmyLift;
                }
                along *= shimmyDirection * shimmy;
                lift *= shimmy;
            }
            return new Vector3(side * hangHandSpacing + along, hangHandHeight + gripAbove + lift, hangHandReach - lift * 0.5f);
        }

        // Turns the shoulder and bends the elbow so the hand's grip point lands on target (two-joint reach), the elbow
        // pointing towards hint; weight 1 = exactly there.
        void ReachFor(Transform shoulder, Transform elbow, Vector3 target, Vector3 hint, float weight)
        {
            if (shoulder == null || elbow == null) return;
            Quaternion oldShoulder = shoulder.localRotation, oldElbow = elbow.localRotation;
            float upperLength = shoulder.TransformVector(elbow.localPosition).magnitude;
            float lowerLength = elbow.TransformVector(gripOffset).magnitude;
            Vector3 start = shoulder.position;
            Vector3 toTarget = target - start;
            float reach = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
            // Elbow bend from the triangle upper arm / forearm / distance to the target.
            float cosInner = (upperLength * upperLength + lowerLength * lowerLength - reach * reach) / (2f * upperLength * lowerLength);
            float bend = 180f - Mathf.Acos(Mathf.Clamp(cosInner, -1f, 1f)) * Mathf.Rad2Deg;
            elbow.localRotation = Quaternion.Euler(-bend, 0f, 0f);
            // Aim the whole arm at the target, then turn it around that line so the elbow points towards the hint.
            Vector3 grip = elbow.TransformPoint(gripOffset);
            shoulder.rotation = Quaternion.FromToRotation(grip - start, toTarget) * shoulder.rotation;
            Vector3 axis = toTarget.normalized;
            Vector3 elbowSide = Vector3.ProjectOnPlane(elbow.position - start, axis);
            Vector3 hintSide = Vector3.ProjectOnPlane(hint, axis);
            if (elbowSide.sqrMagnitude > 1e-8f && hintSide.sqrMagnitude > 1e-8f)
                shoulder.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(elbowSide, hintSide, axis), axis) * shoulder.rotation;
            shoulder.localRotation = Quaternion.Slerp(oldShoulder, shoulder.localRotation, weight);
            elbow.localRotation = Quaternion.Slerp(oldElbow, elbow.localRotation, weight);
        }

        Vector3 Settle(Vector3 onGround, Vector3 rest)
        {
            Vector3 p = Vector3.Lerp(onGround, rest, air);
            return Vector3.Lerp(p, rest + Vector3.down * landDrop, land);
        }

        Quaternion Mix(Quaternion walk, Quaternion stance) => Quaternion.Slerp(stance, walk, moving);

        // Positive X turns a limb's lower end backwards. Swing forward = negative.
        void GroundLeg(int hip, int knee, int ankle, float p, float spread, Vector3 stanceHip, float stanceKnee, Vector3 stanceAnkle)
        {
            float swing = Mathf.Sin(p) * hipSwing * amount;                     // + = leg back
            // The knee bends while the leg comes forward through the air and is straight when it plants.
            float bend = Mathf.Max(0f, Mathf.Sin(p - Mathf.PI * 0.35f)) * kneeBend * amount;
            groundPose[hip] = Mix(Quaternion.Euler(swing - bend * 0.25f, 0f, spread), Quaternion.Euler(stanceHip));
            groundPose[knee] = Mix(Quaternion.Euler(bend, 0f, 0f), Quaternion.Euler(stanceKnee, 0f, 0f));
            // The lean would tip the feet over: the ankles take it back out.
            groundPose[ankle] = Mix(Quaternion.Euler(-footPitch, 0f, footRoll), Quaternion.Euler(stanceAnkle));
        }

        // spread: roll that holds the limb away from the body (sign per side).
        void GroundArm(int shoulder, int elbow, float p, float spread, Vector3 stanceShoulder, float stanceElbow)
        {
            float swing = Mathf.Sin(p) * shoulderSwing * amount;                 // + = arm back
            float forward = Mathf.Max(0f, -Mathf.Sin(p));
            groundPose[shoulder] = Mix(Quaternion.Euler(swing, 0f, spread), Quaternion.Euler(stanceShoulder));
            groundPose[elbow] = Mix(Quaternion.Euler(-(elbowRest + elbowWalk * amount * (0.4f + 0.6f * forward)), 0f, 0f), Quaternion.Euler(-stanceElbow, 0f, 0f));
        }

        // A body pose as joint rotations. The ankles also undo the hips' sideways tilt so the soles stay level.
        static void Fill(Quaternion[] pose, BodyPose p)
        {
            pose[HipL] = Quaternion.Euler(p.hipLeft);
            pose[KneeL] = Quaternion.Euler(p.kneeLeft, 0f, 0f);
            pose[AnkleL] = Quaternion.Euler(p.ankleLeft, 0f, -p.hipLeft.z);
            pose[HipR] = Quaternion.Euler(p.hipRight);
            pose[KneeR] = Quaternion.Euler(p.kneeRight, 0f, 0f);
            pose[AnkleR] = Quaternion.Euler(p.ankleRight, 0f, -p.hipRight.z);
            pose[ShoulderL] = Quaternion.Euler(p.shoulderLeft);
            pose[ElbowL] = Quaternion.Euler(-p.elbowLeft, 0f, 0f);
            pose[ShoulderR] = Quaternion.Euler(p.shoulderRight);
            pose[ElbowR] = Quaternion.Euler(-p.elbowRight, 0f, 0f);
            pose[HipsJ] = Quaternion.identity;
            pose[Torso] = Quaternion.Euler(p.torsoLean, 0f, 0f);
            pose[Head] = Quaternion.identity;
        }
    }
}
