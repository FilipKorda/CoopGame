using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace CoopGame
{
    /// <summary>
    /// Two hanging scales and the weights (A2_Start, the pipe workshop). The server works out which items rest on each
    /// scale's plate (on the plate itself, or stacked on an item that does), adds up their weights and shows the total of
    /// both scales on the sum panels. Solved, for good, when one plate holds at least <see cref="minOnOne"/> items, the
    /// other at least <see cref="minOnOther"/>, and their weights differ by exactly <see cref="difference"/> (either plate
    /// may be the heavier one). Then the double sliding door opens on every machine.
    /// Items being carried by the beam don't count; the plates must stay solved for <see cref="settleTime"/>.
    /// </summary>
    public class ScaleBalancePuzzle : NetworkBehaviour
    {
        [Tooltip("The plates (hanging pans) of the two scales.")]
        [SerializeField] Collider[] plates = new Collider[2];
        [SerializeField] BalanceWeight[] items;
        [SerializeField] int minOnOne = 5;
        [SerializeField] int minOnOther = 4;
        [SerializeField] int difference = 17;
        [Tooltip("Off: the plates must hold exactly these counts, no extra items.")]
        [SerializeField] bool countsAreMinimums = true;
        [Tooltip("Off: the first plate must take Min On One and the second Min On Other.")]
        [SerializeField] bool eitherPlate = true;
        [Tooltip("Seconds the plates must stay solved before it counts (items still landing).")]
        [SerializeField] float settleTime = 0.6f;
        [Tooltip("An item counts as resting on what is at most this far below it (metres).")]
        [SerializeField] float restGap = 0.08f;

        [Header("Show / open")]
        [Tooltip("Sum panels: all show the total weight on both scales.")]
        [SerializeField] TMP_Text[] sumDisplays;
        [Tooltip("{0} = total weight on both scales.")]
        [SerializeField] string sumFormat = "SUMA CIĘŻARU\n<size=200%>{0}</size>";
        [SerializeField] SlideLeaves door;

        readonly NetworkVariable<int> total = new();
        readonly NetworkVariable<bool> solved = new();

        // Server: the last reading
        readonly int[] plateSum = new int[2];
        readonly int[] plateCount = new int[2];
        int[] itemPlate;
        float solvedSince = -1f;
        int shownTotal = int.MinValue;
        static readonly RaycastHit[] s_Hits = new RaycastHit[16];

        public bool IsSolved => solved.Value;
        /// <summary>Total weight on both scales (every machine).</summary>
        public int Total => total.Value;
        /// <summary>Server: weight on plate 0 / 1.</summary>
        public int PlateSum(int plate) => plateSum[plate];
        /// <summary>Server: items on plate 0 / 1.</summary>
        public int PlateCount(int plate) => plateCount[plate];
        /// <summary>Server: the plate item <paramref name="index"/> rests on (0 / 1), or -1.</summary>
        public int ItemPlate(int index) => itemPlate != null && index >= 0 && index < itemPlate.Length ? itemPlate[index] : -1;
        public BalanceWeight[] Items => items;

        public override void OnNetworkSpawn()
        {
            solved.OnValueChanged += OnSolvedChanged;
            if (solved.Value && door != null) door.Open();
        }

        public override void OnNetworkDespawn()
        {
            solved.OnValueChanged -= OnSolvedChanged;
        }

        void OnSolvedChanged(bool previous, bool current)
        {
            if (current && door != null) door.Open();
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (IsServer) ServerTick();

            if (shownTotal == total.Value || sumDisplays == null) return;
            shownTotal = total.Value;
            string text = string.Format(sumFormat, shownTotal);
            foreach (var display in sumDisplays)
                if (display != null) display.text = text;
        }

        // ---------- Server ----------

        void ServerTick()
        {
            Weigh();
            int sum = plateSum[0] + plateSum[1];
            if (total.Value != sum) total.Value = sum;
            if (solved.Value) return;

            if (!Balanced())
            {
                solvedSince = -1f;
                return;
            }
            if (solvedSince < 0f) solvedSince = Time.time;
            if (Time.time - solvedSince >= settleTime) solved.Value = true;
        }

        bool Balanced()
        {
            if (Mathf.Abs(plateSum[0] - plateSum[1]) != difference) return false;
            return CountsFit(plateCount[0], plateCount[1]) || (eitherPlate && CountsFit(plateCount[1], plateCount[0]));
        }

        bool CountsFit(int first, int second) => countsAreMinimums
            ? first >= minOnOne && second >= minOnOther
            : first == minOnOne && second == minOnOther;

        /// <summary>Which plate every item rests on, then the sums and counts per plate.</summary>
        void Weigh()
        {
            int n = items != null ? items.Length : 0;
            if (itemPlate == null || itemPlate.Length != n) itemPlate = new int[n];

            // What each item stands on: a plate (-1 - plate index, so -1 / -2) or another item (its index).
            var below = new System.Collections.Generic.List<int>[n];
            for (int i = 0; i < n; i++)
            {
                itemPlate[i] = -1;
                below[i] = Supports(i);
            }

            // Spread the plates up through stacks.
            for (bool changed = true; changed;)
            {
                changed = false;
                for (int i = 0; i < n; i++)
                {
                    if (itemPlate[i] >= 0) continue;
                    foreach (int s in below[i])
                    {
                        int plate = s < 0 ? -1 - s : itemPlate[s];
                        if (plate < 0) continue;
                        itemPlate[i] = plate;
                        changed = true;
                        break;
                    }
                }
            }

            plateSum[0] = plateSum[1] = plateCount[0] = plateCount[1] = 0;
            for (int i = 0; i < n; i++)
            {
                if (itemPlate[i] < 0) continue;
                plateSum[itemPlate[i]] += items[i].Weight;
                plateCount[itemPlate[i]]++;
            }
        }

        System.Collections.Generic.List<int> Supports(int index)
        {
            var result = new System.Collections.Generic.List<int>(2);
            var item = items[index];
            if (item == null || !item.isActiveAndEnabled || item.IsCarried || item.Body == null) return result;

            BoxCollider box = item.Body;
            Transform bt = box.transform;
            Vector3 half = Vector3.Scale(box.size, bt.lossyScale) * 0.5f;
            half = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z));
            Vector3 center = bt.TransformPoint(box.center);
            // A thin slab at the item's middle, slightly narrower than the item, swept down past its bottom.
            Vector3 slab = new Vector3(half.x * 0.9f, 0.01f, half.z * 0.9f);
            float reach = half.y + restGap;

            int count = Physics.BoxCastNonAlloc(center, slab, Vector3.down, s_Hits, bt.rotation, reach, ~0, QueryTriggerInteraction.Ignore);
            for (int h = 0; h < count; h++)
            {
                Collider hit = s_Hits[h].collider;
                // Distance 0 = already overlapping at the item's middle: a neighbour at its side, not under it.
                if (hit == null || s_Hits[h].distance <= 0f || hit.transform.IsChildOf(item.transform)) continue;
                for (int p = 0; p < plates.Length && p < 2; p++)
                    if (plates[p] == hit && !result.Contains(-1 - p)) result.Add(-1 - p);
                var other = hit.GetComponentInParent<BalanceWeight>();
                if (other == null || other == item) continue;
                int j = System.Array.IndexOf(items, other);
                if (j >= 0 && !result.Contains(j)) result.Add(j);
            }
            return result;
        }
    }
}
