using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Which body a player has, so the two players never look the same: 0 = the prefab's own body (the male cube
    /// figure), 1 = the first of Variants (the female one).
    /// Each player picks it in the lobby (<see cref="ChooseLocal"/>); its player object asks the server for it. The
    /// server gives it, and a player that had it gets the other body, so the two never match. Without a choice (LAN,
    /// Multiplayer Play Mode) the server gives the first free body when the player spawns: the host is male.
    /// Every client puts the body on the cube figure: the part meshes, the joint positions, the rig's rest heights and
    /// grip, the back mount, and how it walks (PlayerCubeRig.Gait: body 0 walks as the rig is set in the prefab, a
    /// variant with its own gait walks its way; the female's comes from Actor_Player2's rig).
    /// Variants are filled by Tools/Coop/Player/Build Female Figure From Sentinel Cube Body Female.
    /// </summary>
    public class PlayerBody : NetworkBehaviour
    {
        [System.Serializable]
        public class Variant
        {
            public string name;
            [Tooltip("One mesh per figure part, in the order of Parts.")]
            public Mesh[] meshes;
            [Tooltip("Local position of each joint, in the order of Joints.")]
            public Vector3[] jointPositions;
            public Vector3 restHips, restUpperBody, gripOffset;
            public float hangLower;
            public Vector3 backMount;
            [Tooltip("This body walks its own way (else it keeps the rig's current walk).")]
            public bool ownGait;
            public PlayerCubeRig.Gait gait;
        }

        [SerializeField] PlayerCubeRig rig;
        [SerializeField] MeshFilter[] parts;
        [SerializeField] Transform[] joints;
        [SerializeField] Transform backMount;
        [Tooltip("Bodies besides the prefab's own one (body 0): body 1 is the first entry, and so on.")]
        [SerializeField] Variant[] variants;

        static readonly List<PlayerBody> s_Spawned = new();
        // The body this machine's player picked in the lobby (-1 = none picked).
        static int s_LocalChoice = -1;

        readonly NetworkVariable<byte> body = new(0);
        Variant original;
        int shown;

        /// <summary>0 = the prefab's own body (male), 1 = the first variant (female).</summary>
        public int BodyIndex => body.Value;
        public int BodyCount => 1 + (variants != null ? variants.Length : 0);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Spawned.Clear();
            s_LocalChoice = -1;
        }

        /// <summary>
        /// This machine's player picked a body (the lobby). Asked for right away if its player object is already there,
        /// else as soon as it spawns.
        /// </summary>
        public static void ChooseLocal(int index)
        {
            s_LocalChoice = index;
            foreach (var p in s_Spawned)
                if (p != null && p.IsSpawned && p.IsOwner) p.RequestBodyRpc((byte)index);
        }

        void Awake()
        {
            original ??= Capture();
        }

        public override void OnNetworkSpawn()
        {
            s_Spawned.Add(this);
            if (IsServer) body.Value = (byte)FreeBody();
            body.OnValueChanged += OnBodyChanged;
            Show(body.Value);
            if (IsOwner && s_LocalChoice >= 0) RequestBodyRpc((byte)s_LocalChoice);
        }

        // The player gets the body it picked; whoever had it gets a free one instead.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void RequestBodyRpc(byte index)
        {
            if (index >= BodyCount || body.Value == index) return;
            body.Value = index;
            foreach (var other in s_Spawned)
                if (other != this && other != null && other.IsSpawned && other.body.Value == index)
                    other.body.Value = (byte)other.FreeBody();
        }

        public override void OnNetworkDespawn()
        {
            body.OnValueChanged -= OnBodyChanged;
            s_Spawned.Remove(this);
        }

        void OnBodyChanged(byte previous, byte current) => Show(current);

        // The first body no other player has (the host spawns first, so it keeps body 0).
        int FreeBody()
        {
            for (int i = 0; i < BodyCount; i++)
            {
                bool taken = false;
                foreach (var other in s_Spawned)
                    if (other != this && other != null && other.IsSpawned && other.body.Value == i) taken = true;
                if (!taken) return i;
            }
            return 0;
        }

        /// <summary>Puts body <paramref name="index"/> on the figure (also usable in the editor, for previews).</summary>
        public void Show(int index)
        {
            original ??= Capture();
            var v = index >= 1 && variants != null && index <= variants.Length ? variants[index - 1] : original;
            if (index == shown && v == original) return;
            if (v.meshes != null)
                for (int i = 0; i < parts.Length && i < v.meshes.Length; i++)
                    if (parts[i] != null && v.meshes[i] != null) parts[i].sharedMesh = v.meshes[i];
            if (v.jointPositions != null)
                for (int i = 0; i < joints.Length && i < v.jointPositions.Length; i++)
                    if (joints[i] != null) joints[i].localPosition = v.jointPositions[i];
            if (backMount != null) backMount.localPosition = v.backMount;
            if (rig != null) rig.SetBody(v.restHips, v.restUpperBody, v.gripOffset, v.hangLower);
            if (rig != null && v.ownGait) rig.SetGait(v.gait);
            shown = index;
        }

        Variant Capture()
        {
            var v = new Variant { name = "prefab", meshes = new Mesh[parts != null ? parts.Length : 0], jointPositions = new Vector3[joints != null ? joints.Length : 0] };
            for (int i = 0; i < v.meshes.Length; i++) v.meshes[i] = parts[i] != null ? parts[i].sharedMesh : null;
            for (int i = 0; i < v.jointPositions.Length; i++) v.jointPositions[i] = joints[i] != null ? joints[i].localPosition : Vector3.zero;
            if (backMount != null) v.backMount = backMount.localPosition;
            if (rig != null)
            {
                rig.GetBody(out v.restHips, out v.restUpperBody, out v.gripOffset, out v.hangLower);
                v.gait = rig.GetGait(); v.ownGait = true;
            }
            return v;
        }

#if UNITY_EDITOR
        /// <summary>Editor builder: wires the figure and stores the extra bodies.</summary>
        public void EditorSetup(PlayerCubeRig rig, MeshFilter[] parts, Transform[] joints, Transform backMount, Variant[] variants)
        {
            this.rig = rig; this.parts = parts; this.joints = joints; this.backMount = backMount; this.variants = variants;
            original = null; shown = 0;
        }
#endif
    }
}
