using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Interior dressing of the protagonist warship. Everything is found from the greybox itself with physics queries
    /// (ship frame), so it follows the level when the greybox changes: ribs on the inner faces of walls, deck beams under
    /// ceilings, pipe runs along the hold walls, brackets / chains under the floating jump platforms, ceiling lamps and
    /// wall lanterns (with point lights), and props standing against walls. Nothing is placed close to gameplay objects
    /// (bounds incl. their travel); props are visual only unless propColliders is on.
    /// </summary>
    public static partial class ProtagonistShipBuilder
    {
        static readonly List<Bounds> placedProps = new List<Bounds>();   // ship frame
        static readonly List<Vector3> placedLights = new List<Vector3>(); // ship frame
        static readonly List<Bounds> gameplayRaw = new List<Bounds>();    // world, without the clearance margin
        static HashSet<Transform> gameplaySet;
        const float PropGap = 0.3f; // props stand in front of the wall ribs
        const int MaxCeilingLamps = 40, MaxLanterns = 120, MaxProps = 160;

        struct Blk { public Transform t; public Vector3 mn, mx; public Vector3 S => mx - mn; public Vector3 C => (mn + mx) * 0.5f; }
        static List<Blk> blocks;

        static Vector3 W(Vector3 l) => style.TransformPoint(l);
        static bool InL(Vector3 l) => InInterior(W(l));
        static bool FreeL(Vector3 c, Vector3 size, Quaternion r) => !Physics.CheckBox(W(c), Abs(size) * 0.5f, style.rotation * r, ~0, QueryTriggerInteraction.Ignore);
        static bool FreeL(Vector3 c, Vector3 size) => FreeL(c, size, Quaternion.identity);
        static bool NearG(Vector3 l, float r) => NearGameplay(W(l), r);
        static bool NearRaw(Vector3 l, float r) { var w = W(l); float r2 = r * r; foreach (var b in gameplayRaw) if (b.SqrDistance(w) < r2) return true; return false; }

        static bool RayL(Vector3 o, Vector3 d, float dist, out RaycastHit hit, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            if (!Physics.Raycast(W(o), style.rotation * d, out hit, dist, ~0, QueryTriggerInteraction.Ignore)) return false;
            point = style.InverseTransformPoint(hit.point); normal = Quaternion.Inverse(style.rotation) * hit.normal;
            return true;
        }

        static Transform ShipChild(Transform t) { while (t != null && t.parent != ship) t = t.parent; return t; }
        static bool IsGameplay(Collider c) { var t = ShipChild(c.transform); return t != null && gameplaySet.Contains(t); }
        static bool IsHullFloor(Collider c) { var t = ShipChild(c.transform); return t != null && t.name.StartsWith("Protagonist Ship"); }
        static bool IsShipFloor(Collider c) { var t = ShipChild(c.transform); return t != null && (t.name.StartsWith("Protagonist Ship") || (t.name.StartsWith("Platform") && !IsWrapped(t.name))); }

        static Blk LocalBlock(Transform t)
        {
            var bc = t.GetComponent<BoxCollider>(); var h = bc.size * 0.5f;
            var mn = Vector3.one * 1e9f; var mx = -mn;
            for (int i = 0; i < 8; i++)
            {
                var c = bc.center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                var l = style.InverseTransformPoint(t.TransformPoint(c));
                mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
            }
            return new Blk { t = t, mn = mn, mx = mx };
        }

        /// <summary>Vertical face f of a block: 0 +x, 1 -x, 2 +z, 3 -z. alongZ = the face spans z.</summary>
        struct VF { public bool alongZ; public Vector3 n; public float plane, u0, u1, y0, y1; public float Len => u1 - u0; }
        static VF Face(Blk b, int f)
        {
            var v = new VF { alongZ = f < 2, y0 = b.mn.y, y1 = b.mx.y };
            v.n = f == 0 ? Vector3.right : f == 1 ? Vector3.left : f == 2 ? Vector3.forward : Vector3.back;
            v.plane = f == 0 ? b.mx.x : f == 1 ? b.mn.x : f == 2 ? b.mx.z : b.mn.z;
            v.u0 = v.alongZ ? b.mn.z : b.mn.x; v.u1 = v.alongZ ? b.mx.z : b.mx.x;
            return v;
        }
        static Vector3 Pt(VF f, float u, float y) => f.alongZ ? V(f.plane, y, u) : V(u, y, f.plane);
        static Vector3 Sz(VF f, float w, float h, float d) => f.alongZ ? V(d, h, w) : V(w, h, d);
        static Vector3 UAxis(VF f) => f.alongZ ? Vector3.forward : Vector3.right;

        static void BuildInterior()
        {
            var parent = Group("Interior");
            gameplaySet = new HashSet<Transform>(gameplayRoots);
            blocks = hullPieces.Where(t => !IsWrapped(t.name)).Select(LocalBlock).ToList();
            FindNoLampRooms();
            var frames = new MB();
            int ribs = Ribs(frames), stringers = Stringers(frames), beams = Beams(frames), pipes = Pipes(frames), sup = PlatformSupports(frames);
            SceneMesh(parent, "Interior Frames", frames, inner, true);
            var lampT = new GameObject("Lamps").transform; lampT.SetParent(parent, false);
            var propT = new GameObject("Props").transform; propT.SetParent(parent, false);
            int lamps = CeilingLamps(lampT), lanterns = WallLanterns(lampT), props = Props(propT);
            report.Add($"Interior: {ribs} rib runs, {stringers} stringers, {beams} deck beam runs, {pipes} pipe runs, {sup} platform brackets/chains, {lamps} ceiling lamps + {lanterns} wall lanterns (point lights, no shadows), {props} props (colliders: {(propColliders ? "yes" : "no")}).");
        }

        // ---------- Ribs / beams / pipes ----------

        static int Ribs(MB mb)
        {
            int n = 0; const float step = 3.6f, seg = 0.5f, depth = 0.26f, width = 0.34f;
            foreach (var b in blocks)
                for (int fi = 0; fi < 4; fi++)
                {
                    var f = Face(b, fi); if (f.Len < 3f || f.y1 - f.y0 < 3f) continue;
                    int k0 = Mathf.CeilToInt((f.u0 + 0.4f - step * 0.5f) / step), k1 = Mathf.FloorToInt((f.u1 - 0.4f - step * 0.5f) / step);
                    for (int k = k0; k <= k1; k++)
                    {
                        float u = k * step + step * 0.5f; float run = float.NaN;
                        for (float y = f.y0; y < f.y1 - 0.05f; y += seg)
                        {
                            float y1 = Mathf.Min(y + seg, f.y1);
                            var c = Pt(f, u, (y + y1) * 0.5f) + f.n * (0.012f + depth * 0.5f);
                            bool ok = InL(c) && FreeL(c, Sz(f, width, y1 - y - 0.02f, depth - 0.02f)) && !NearRaw(c, 0.3f);
                            if (ok && float.IsNaN(run)) run = y;
                            bool last = y1 >= f.y1 - 0.05f;
                            if ((!ok || last) && !float.IsNaN(run))
                            {
                                float e = ok ? y1 : y;
                                if (e - run >= 0.99f)
                                {
                                    var p = Pt(f, u, (run + e) * 0.5f);
                                    mb.Box(p + f.n * 0.13f, Sz(f, 0.12f, e - run, 0.24f), FrameD, Quaternion.identity);
                                    mb.Box(p + f.n * 0.24f, Sz(f, width, e - run, 0.05f), FrameC, Quaternion.identity);
                                    n++;
                                }
                                run = float.NaN;
                            }
                        }
                    }
                }
            return n;
        }

        /// <summary>Horizontal stringers along tall walls (they break up the 30 m high holds into "decks").</summary>
        static int Stringers(MB mb)
        {
            int n = 0;
            foreach (var b in blocks)
                for (int fi = 0; fi < 4; fi++)
                {
                    var f = Face(b, fi); if (f.Len < 4f || f.y1 - f.y0 < 7f) continue;
                    foreach (float hy in new[] { 8.2f, 16.0f, 23.4f })
                    {
                        if (hy < f.y0 + 2f || hy > f.y1 - 1.5f) continue;
                        var runs = new List<(Vector3, Vector3, float, Color, Vector3, float)>();
                        CollectRuns(runs, f.u0 + 0.2f, f.u1 - 0.2f, u => Pt(f, u, hy) + f.n * 0.26f, UAxis(f), 0.2f, FrameD, f.n, 0f);
                        foreach (var (a, e, _, _, _, _) in runs)
                        {
                            var mid = (a + e) * 0.5f; float len = (e - a).magnitude;
                            mb.Box(mid - f.n * 0.12f, Sz(f, len, 0.36f, 0.26f), FrameD, Quaternion.identity);
                            mb.Box(mid + f.n * 0.02f, Sz(f, len, 0.48f, 0.05f), FrameC, Quaternion.identity);
                            mb.Box(mid - f.n * 0.09f + Vector3.up * 0.2f, Sz(f, len, 0.05f, 0.24f), FrameC, Quaternion.identity);
                            n++;
                        }
                    }
                }
            return n;
        }

        static int Beams(MB mb)
        {
            int n = 0; const float step = 3f, seg = 0.5f;
            foreach (var b in blocks)
            {
                var S = b.S; if (S.x < 3f || S.z < 3f) continue;
                float yc = b.mn.y;
                int k0 = Mathf.CeilToInt((b.mn.x + 0.3f - 1.5f) / step), k1 = Mathf.FloorToInt((b.mx.x - 0.3f - 1.5f) / step);
                for (int k = k0; k <= k1; k++)
                {
                    float x = k * step + 1.5f; float run = float.NaN;
                    for (float z = b.mn.z; z < b.mx.z - 0.05f; z += seg)
                    {
                        float z1 = Mathf.Min(z + seg, b.mx.z);
                        var c = V(x, yc - 0.012f - 0.225f, (z + z1) * 0.5f);
                        bool ok = InL(c) && FreeL(c, V(0.32f, 0.43f, z1 - z - 0.02f)) && !NearRaw(c, 0.3f);
                        if (ok && float.IsNaN(run)) run = z;
                        bool last = z1 >= b.mx.z - 0.05f;
                        if ((!ok || last) && !float.IsNaN(run))
                        {
                            float e = ok ? z1 : z;
                            if (e - run >= 0.99f)
                            {
                                float zm = (run + e) * 0.5f;
                                mb.Box(V(x, yc - 0.21f, zm), V(0.12f, 0.4f, e - run), FrameD, Quaternion.identity);
                                mb.Box(V(x, yc - 0.43f, zm), V(0.34f, 0.05f, e - run), FrameC, Quaternion.identity);
                                n++;
                            }
                            run = float.NaN;
                        }
                    }
                }
            }
            return n;
        }

        /// <summary>Two pipe runs along the hold walls (y 4.5 / 5.0) and two under the main deck along the ship.</summary>
        static int Pipes(MB mb)
        {
            int n = 0;
            var runs = new List<(Vector3 a, Vector3 b, float r, Color col, Vector3 wallN, float wallD)>();
            foreach (var b in blocks)
                for (int fi = 0; fi < 4; fi++)
                {
                    var f = Face(b, fi); if (f.Len < 6f || f.y0 > 3f || f.y1 < 6f) continue;
                    foreach (var (h, off, r, col) in new[] { (4.5f, 0.5f, 0.15f, Copper), (5.05f, 0.82f, 0.1f, SteelL) })
                        CollectRuns(runs, f.u0 + 0.3f, f.u1 - 0.3f, u => Pt(f, u, h) + f.n * off, UAxis(f), r, col, f.n, off, true);
                }
            foreach (var b in blocks)
            {
                var S = b.S; if (S.x < 10f || S.z < 6f) continue;
                foreach (float zr in new[] { b.mn.z + 1.6f, b.mx.z - 1.6f })
                    CollectRuns(runs, b.mn.x + 0.3f, b.mx.x - 0.3f, x => V(x, b.mn.y - 0.78f, zr), Vector3.right, 0.17f, Steel2, Vector3.zero, 0f, true);
            }
            foreach (var (a, b, r, col, wn, wd) in runs)
            {
                var d = b - a; float len = d.magnitude; if (len < 1.5f) continue;
                var dir = d / len;
                mb.Cyl(a, b, r, 8, col, 22.5f);
                for (float t = 0.6f; t < len - 0.3f; t += 2.4f)
                {
                    var p = a + dir * t;
                    mb.Prism(p - dir * 0.05f, 8, r * 1.35f, r * 1.35f, 0.1f, Iron, Quaternion.FromToRotation(Vector3.up, dir), 22.5f);
                    if (wn != Vector3.zero) mb.Box(p - wn * (wd * 0.5f), new Vector3(0.06f, 0.06f, 0.06f) + Abs(wn) * (wd - 0.04f) + Vector3.up * 0.04f, Iron, Quaternion.identity);
                    else mb.Box(p + Vector3.up * 0.39f, V(0.06f, 0.6f, 0.06f), Iron, Quaternion.identity);
                }
                n++;
            }
            return n;
        }

        static void CollectRuns(List<(Vector3, Vector3, float, Color, Vector3, float)> runs, float u0, float u1, System.Func<float, Vector3> at, Vector3 axis, float r, Color col, Vector3 wn, float wd, bool pipe = false)
        {
            const float seg = 1f; float run = float.NaN, uu = u0;
            for (float u = u0; u < u1 - 0.05f; u += seg)
            {
                float e1 = Mathf.Min(u + seg, u1);
                var c = at((u + e1) * 0.5f);
                var size = axis * (e1 - u - 0.02f) + (Vector3.one - Abs(axis)) * (2f * r + 0.06f);
                bool ok = InL(c) && FreeL(c, size) && !NearRaw(c, 0.4f) && !(pipe && InNoLampRoom(c)); // no pipes in the wall spike-trap room
                if (ok && float.IsNaN(run)) run = u;
                bool last = e1 >= u1 - 0.05f;
                if ((!ok || last) && !float.IsNaN(run))
                {
                    float e = ok ? e1 : u;
                    if (e - run >= 1.5f) runs.Add((at(run), at(e), r, col, wn, wd));
                    run = float.NaN;
                }
            }
        }

        /// <summary>Jump platforms float in the air: brace the ones near a wall with a bracket, hang the others on chains.</summary>
        static int PlatformSupports(MB mb)
        {
            int n = 0;
            foreach (var t in platformPieces)
            {
                if (IsWrapped(t.name)) continue;
                var b = LocalBlock(t); var S = b.S;
                if (S.y > 1.2f || S.x > 7f || S.z > 7f || S.x < 1f || S.z < 1f) continue;
                var c = b.C; float yb = b.mn.y;
                float best = 4.0f; Vector3 bd = Vector3.zero, bp = Vector3.zero;
                foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                {
                    float half = Mathf.Abs(Vector3.Dot(S, d)) * 0.5f;
                    var o = V(c.x, yb - 0.2f, c.z) + d * (half - 0.05f);
                    if (RayL(o, d, best, out var hit, out var p, out var nn) && !IsGameplay(hit.collider) && Vector3.Dot(nn, -d) > 0.9f) { best = hit.distance; bd = d; bp = p; }
                }
                if (bd != Vector3.zero)
                {
                    float half = Mathf.Abs(Vector3.Dot(S, bd)) * 0.5f;
                    var wallP = V(bp.x, yb - 0.17f, bp.z);
                    var end = V(c.x, yb - 0.17f, c.z) - bd * half * 0.55f;
                    var mid = (wallP + end) * 0.5f; float len = (end - wallP).magnitude;
                    var r = Quaternion.LookRotation(-bd, Vector3.up);
                    if (FreeL(mid + Vector3.down * 0.02f, V(0.2f, 0.26f, len - 0.06f), r) && !NearRaw(mid, 0.2f))
                    {
                        mb.Box(mid, V(0.1f, 0.3f, len), SteelD, r);
                        mb.Box(mid + Vector3.down * 0.15f, V(0.26f, 0.05f, len), Steel2, r);
                        var low = wallP + Vector3.down * Mathf.Min(1.6f, len * 1.1f) - bd * 0.06f;
                        var tip = wallP - bd * len * 0.8f + Vector3.down * 0.15f;
                        if (FreeL((low + tip) * 0.5f, V(0.12f, 0.12f, 0.12f)))
                        {
                            mb.Rod(low, tip, 0.12f, SteelD);
                            mb.Box(low, V(0.3f, 0.3f, 0.3f) - Abs(bd) * 0.24f, Iron, Quaternion.identity);
                        }
                        n++; continue;
                    }
                }
                // chains from two opposite corners to the ceiling (or the next thing above)
                int chains = 0;
                foreach (var sgn in new[] { new Vector2(-1, -1), new Vector2(1, 1) })
                {
                    var p = V(c.x + sgn.x * (S.x * 0.5f - 0.18f), b.mx.y + 0.03f, c.z + sgn.y * (S.z * 0.5f - 0.18f));
                    if (!RayL(p, Vector3.up, 14f, out var hit, out var hp, out var hn) || IsGameplay(hit.collider) || hn.y > -0.9f || hit.distance < 1.5f) continue;
                    if (NearRaw(p + Vector3.up * 1f, 0.15f)) continue;
                    Chain(mb, p + Vector3.up * 0.1f, hp + Vector3.down * 0.06f, 0.36f, Iron);
                    mb.Box(p + Vector3.up * 0.04f, V(0.18f, 0.08f, 0.18f), Iron, Quaternion.identity);
                    mb.Box(hp + Vector3.down * 0.04f, V(0.22f, 0.08f, 0.22f), Iron, Quaternion.identity);
                    chains++;
                }
                if (chains > 0) n++;
            }
            return n;
        }

        // ---------- Lights ----------

        static bool LightSpot(Vector3 l, float spacing)
        {
            foreach (var p in placedLights) if ((p - l).sqrMagnitude < spacing * spacing) return false;
            return true;
        }

        static void AddLightInstance(Transform parent, GameObject prefab, Vector3 pos, Quaternion rot, Vector3 lightPos, string name)
        {
            Place(prefab, parent, pos, rot, name);
            placedLights.Add(lightPos); lightCount++;
        }

        // Rooms with wall spike traps or a Coop_LeverGate get no hanging lamps and no pipes. Each room is found by casting from
        // every SpikeTrap_Wall / every lever and gate of a Coop_LeverGate along the ship axes to the nearest greybox wall / floor / ceiling.
        static List<Bounds> noLampRooms = new List<Bounds>(), noLampOnlyRooms = new List<Bounds>(); // second list: lamps removed, pipes kept (Ship Panel room)
        static void FindNoLampRooms()
        {
            noLampRooms.Clear(); noLampOnlyRooms.Clear();
            var root = style.parent;
            bool Ignore(Collider c) { for (var t = c.transform; t != null && t != root; t = t.parent) if (t.name.StartsWith("SpikeTrap") || t.name.StartsWith("Platform") || t.name.StartsWith("Coop_LeverGate") || t.name == "Ship Panel" || t == style || IsWrapped(t.name) || (t.parent == root && gameplaySet.Contains(t))) return true; return false; }
            bool Free(Vector3 p) { foreach (var c in Physics.OverlapSphere(p, 0.15f, ~0, QueryTriggerInteraction.Ignore)) if (!Ignore(c)) return false; return true; }
            Bounds Room(Vector3 o)
            {
                // the object may sit half inside a wall: start from the nearest free point next to it
                if (!Free(o))
                    foreach (float s in new[] { 0.5f, 1f, 1.5f })
                    {
                        bool done = false;
                        foreach (var d in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up }) { var q = o + style.rotation * d * s; if (Free(q)) { o = q; done = true; break; } }
                        if (done) break;
                    }
                var lb = new Bounds(style.InverseTransformPoint(o), Vector3.zero);
                foreach (var d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back, Vector3.up, Vector3.down })
                {
                    var wd = style.rotation * d; float best = -1f;
                    foreach (var h in Physics.RaycastAll(o, wd, 120f, ~0, QueryTriggerInteraction.Ignore)) if (!Ignore(h.collider) && h.distance > 0.05f && (best < 0 || h.distance < best)) best = h.distance;
                    lb.Encapsulate(style.InverseTransformPoint(o + wd * (best < 0 ? 1.5f : best)));
                }
                lb.Expand(0.6f);
                return lb;
            }
            void Add(Bounds lb, string what) { noLampRooms.Add(lb); report.Add("No hanging lamps / pipes around " + what + ": ship frame " + lb.min.ToString("F1") + " .. " + lb.max.ToString("F1") + "."); }
            var levers = new List<Bounds>(); var floorTraps = new List<(Bounds b, string n)>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("SpikeTrap_Wall"))
                {
                    var wb = new Bounds(t.position, Vector3.zero); foreach (var r in t.GetComponentsInChildren<Renderer>()) wb.Encapsulate(r.bounds);
                    Add(Room(wb.center), t.name);
                }
                else if (t.name.StartsWith("Coop_LeverGate") && t.parent == root)
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                    {
                        if (r.name != "Stick") continue;   // the gate itself stands in a doorway, its levers are in the room
                        var lb = Room(r.bounds.center); levers.Add(lb); Add(lb, t.name + " lever");
                    }
                else if (t.name == "Ship Panel" && t.parent == root)
                {
                    var wb = new Bounds(t.position, Vector3.zero); foreach (var r in t.GetComponentsInChildren<Renderer>()) wb.Encapsulate(r.bounds);
                    var lb = Room(wb.center); noLampOnlyRooms.Add(lb); report.Add("No hanging lamps in the Ship Panel room: ship frame " + lb.min.ToString("F1") + " .. " + lb.max.ToString("F1") + ".");
                }
                else if (t.name.StartsWith("SpikeTrap_Floor"))
                {
                    var wb = new Bounds(t.position, Vector3.zero); foreach (var r in t.GetComponentsInChildren<Renderer>()) wb.Encapsulate(r.bounds);
                    floorTraps.Add((Room(wb.center + Vector3.up * 1.0f), t.name));
                }
            }
            // the hall under a lever gallery: floor-spike rooms that touch a lever room
            foreach (var (b, nm) in floorTraps) { bool touch = false; foreach (var l in levers) { var e = l; e.Expand(1f); if (e.Intersects(b)) touch = true; } if (touch) Add(b, nm + " (hall of a Coop_LeverGate)"); }
        }
        static bool InNoLampRoom(Vector3 l) { foreach (var b in noLampRooms) if (b.Contains(l)) return true; return false; }

        static int CeilingLamps(Transform parent)
        {
            const float step = 7f;
            var cands = new List<(Vector3 p, int v, Vector3 lp)>();
            foreach (var b in blocks)
            {
                var S = b.S; if (S.x < 3.5f || S.z < 3.5f) continue;
                float yc = b.mn.y;
                for (int k = Mathf.CeilToInt((b.mn.x + 1f - 3.5f) / step); k * step + 3.5f <= b.mx.x - 1f; k++)
                    for (int j = Mathf.CeilToInt((b.mn.z + 1f) / step); j * step <= b.mx.z - 1f; j++)
                    {
                        var p = V(k * step + 3.5f, yc, j * step);
                        if (!InL(p + Vector3.down * 0.6f)) continue;
                        if (InNoLampRoom(p + Vector3.down * 0.6f)) continue;
                        { bool skip = false; foreach (var nb in noLampOnlyRooms) if (nb.Contains(p + Vector3.down * 0.6f)) skip = true; if (skip) continue; }
                        if (!RayL(p + Vector3.down * 0.05f, Vector3.down, 40f, out var hit, out var hp, out var hn) || hit.distance < 3.2f) continue;
                        int v = hit.distance > 14f ? 2 : hit.distance > 7f ? 1 : 0;
                        float drop = LampDrop[v];
                        var lp = p + Vector3.down * (drop + 1.05f);
                        if (!FreeL(p + Vector3.down * ((drop + 0.5f) * 0.5f + 0.02f), V(1.2f, drop + 0.5f, 1.2f)) || !FreeL(lp, V(1.1f, 1.1f, 1.1f)) || NearG(lp, 1.0f)) continue;
                        cands.Add((p, v, lp));
                    }
            }
            Shuffle(cands);
            int n = 0;
            foreach (var (p, v, lp) in cands)
            {
                if (n >= MaxCeilingLamps) break;
                if (!LightSpot(lp, 6.5f)) continue;
                AddLightInstance(parent, LampPrefab(v), p, Quaternion.Euler(0, Hash(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.z)) * 90f, 0), lp, "Ceiling Lamp");
                n++;
            }
            return n;
        }

        static readonly float[] LampDrop = { 0.7f, 2.0f, 4.2f }, LampRange = { 11f, 15f, 22f }, LampIntensity = { 2.6f, 3.4f, 5f };
        static GameObject LampPrefab(int v) => Prefab("Interior", "CeilingLamp_" + v, pb => CeilingLamp(pb, LampDrop[v], LampRange[v], LampIntensity[v]));

        static int WallLanterns(Transform parent)
        {
            const float step = 6f;
            var cands = new List<(Vector3 p, Quaternion r, Vector3 lp)>();
            foreach (var b in blocks)
                for (int fi = 0; fi < 4; fi++)
                {
                    var f = Face(b, fi); if (f.Len < 2f || f.y1 - f.y0 < 3f) continue;
                    for (int k = Mathf.CeilToInt((f.u0 + 0.6f) / step); k * step <= f.u1 - 0.6f; k++)
                    {
                        float u = k * step;
                        for (float y = f.y0 + 2.6f; y < f.y1 - 0.6f; y += 0.5f)
                        {
                            var q = Pt(f, u, y) + f.n * 0.6f;
                            if (!InL(q)) continue;
                            if (!RayL(q, Vector3.down, 3.4f, out var hit, out var hp, out var hn) || Mathf.Abs(hit.distance - 2.8f) > 0.26f || hn.y < 0.9f || !IsShipFloor(hit.collider)) continue;
                            float ly = hp.y + 2.8f;
                            var wp = Pt(f, u, ly);
                            var lp = wp + f.n * 0.95f + Vector3.down * 0.15f;
                            var r = Quaternion.LookRotation(f.n, Vector3.up);
                            if (FreeL(wp + f.n * 0.36f + Vector3.down * 0.05f, Sz(f, 0.5f, 0.8f, 0.66f)) && FreeL(lp, V(1f, 1f, 1f)) && !NearG(lp, 1.0f))
                                cands.Add((wp + f.n * 0.017f, r, lp));
                            y += 2.9f;
                        }
                    }
                }
            Shuffle(cands);
            var pf = Prefab("Interior", "WallLantern", WallLantern);
            int n = 0;
            foreach (var (p, r, lp) in cands)
            {
                if (n >= MaxLanterns) break;
                if (!LightSpot(lp, 4.6f)) continue;
                AddLightInstance(parent, pf, p, r, lp, "Wall Lantern");
                n++;
            }
            return n;
        }

        static void Shuffle<T>(List<T> l) { for (int i = l.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); var x = l[i]; l[i] = l[j]; l[j] = x; } }

        static void CeilingLamp(PB pb, float drop, float range, float intensity)
        {
            var mb = pb.s; pb.interior = true;
            mb.Prism(V(0, -0.06f, 0), 8, 0.17f, 0.17f, 0.06f, Iron);
            Chain(mb, V(0, -0.06f, 0), V(0, -drop, 0), 0.2f, Iron);
            mb.Prism(V(0, -drop - 0.12f, 0), 8, 0.09f, 0.09f, 0.12f, Brass);
            int n = 12; float y0 = -drop - 0.44f, y1 = -drop - 0.12f;
            var lo = new Vector3[n]; var hi = new Vector3[n];
            for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2f / n; lo[i] = V(Mathf.Sin(a) * 0.55f, y0, Mathf.Cos(a) * 0.55f); hi[i] = V(Mathf.Sin(a) * 0.13f, y1, Mathf.Cos(a) * 0.13f); }
            mb.Loft(lo, hi, Green, V(0, (y0 + y1) * 0.5f, 0));
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; mb.Quad(lo[i], lo[j], hi[j], hi[i], Cream, -((lo[i] + lo[j]) * 0.5f - V(0, y0, 0))); }
            mb.Cap(hi, Green, Vector3.up);
            mb.Prism(V(0, y0 - 0.02f, 0), 12, 0.57f, 0.57f, 0.035f, Brass);
            pb.g.Prism(V(0, -drop - 0.42f, 0), 8, 0.12f, 0.12f, 0.26f, GLamp);
            pb.lights.Add((V(0, -drop - 1.05f, 0), range, intensity));
        }

        static void WallLantern(PB pb)
        {
            var mb = pb.s; pb.interior = true;
            mb.Box(V(0, 0.12f, 0.025f), V(0.2f, 0.42f, 0.05f), Iron, Quaternion.identity);
            mb.Box(V(0, 0.3f, 0.24f), V(0.05f, 0.05f, 0.42f), Iron, Quaternion.identity);
            mb.Rod(V(0, -0.02f, 0.05f), V(0, 0.28f, 0.32f), 0.035f, Iron);
            var c = V(0, 0f, 0.44f);
            mb.Box(c + V(0, 0.25f, 0), V(0.04f, 0.1f, 0.04f), Iron, Quaternion.identity);
            mb.Prism(c + V(0, 0.12f, 0), 6, 0.18f, 0.04f, 0.13f, Iron);
            pb.g.Prism(c + V(0, -0.24f, 0), 6, 0.12f, 0.12f, 0.36f, GLamp);
            mb.Prism(c + V(0, -0.31f, 0), 6, 0.16f, 0.16f, 0.07f, Iron);
            for (int i = 0; i < 6; i += 2)
            {
                float a = (i * 60f + 30f) * Mathf.Deg2Rad; var o = V(Mathf.Sin(a) * 0.14f, 0, Mathf.Cos(a) * 0.14f);
                mb.Box(c + o + V(0, -0.06f, 0), V(0.025f, 0.38f, 0.025f), Iron, Quaternion.identity);
            }
            pb.lights.Add((V(0, -0.15f, 0.95f), 12f, 3.4f));
        }

        // ---------- Props ----------

        struct PropDef { public string name; public float w, d, h; public System.Action<PB> build; }
        static PropDef P(string n, float w, float d, float h, System.Action<PB> b) => new PropDef { name = n, w = w, d = d, h = h, build = b };

        static readonly PropDef[] PropDefs =
        {
            P("Barrels", 1.9f, 1.0f, 1.1f, Barrels), P("CrateStack", 2.3f, 1.15f, 2.1f, CrateStackProp), P("Sacks", 1.7f, 0.95f, 0.9f, Sacks),
            P("ShellRack", 2.0f, 0.7f, 1.7f, ShellRack), P("Workbench", 2.2f, 0.85f, 1.95f, Workbench), P("SeaChest", 1.2f, 0.65f, 0.75f, SeaChest),
            P("CoalBin", 2.2f, 1.3f, 1.15f, CoalBin), P("Lockers", 2.1f, 0.6f, 2.15f, Lockers), P("ValveBank", 1.6f, 0.55f, 2.3f, ValveBank),
            P("PowderKegs", 1.6f, 0.85f, 0.95f, PowderKegs),
        };
        static int PI(string n) { for (int i = 0; i < PropDefs.Length; i++) if (PropDefs[i].name == n) return i; return 0; }

        static int[] PropSet(Vector3 l)
        {
            if (l.x > 33.6f) return new[] { PI("PowderKegs"), PI("ShellRack"), PI("CrateStack"), PI("Sacks"), PI("PowderKegs") };
            if (l.y > 20f && l.x > -22.5f && l.x < 4.5f) return new[] { PI("SeaChest"), PI("Lockers"), PI("Workbench"), PI("Barrels"), PI("SeaChest"), PI("Sacks") };
            if (l.x < -23f) return new[] { PI("CoalBin"), PI("ValveBank"), PI("Workbench"), PI("Barrels"), PI("CrateStack"), PI("ValveBank") };
            if (l.z < -11.3f) return new[] { PI("ValveBank"), PI("Barrels"), PI("Sacks"), PI("CrateStack"), PI("ShellRack") };
            if (l.x > 4f) return new[] { PI("ShellRack"), PI("CrateStack"), PI("Barrels"), PI("Workbench"), PI("Lockers"), PI("ValveBank") };
            return new[] { PI("CrateStack"), PI("Barrels"), PI("Sacks"), PI("SeaChest") };
        }

        static int Props(Transform parent)
        {
            const float step = 2.7f;
            int n = 0;
            var cands = new List<(VF f, float u, float fy)>();
            foreach (var b in blocks)
                for (int fi = 0; fi < 4; fi++)
                {
                    var f = Face(b, fi); if (f.Len < 2.5f || f.y1 - f.y0 < 1.5f) continue;
                    for (int k = Mathf.CeilToInt((f.u0 + 1.2f) / step); k * step <= f.u1 - 1.2f; k++)
                    {
                        float u = k * step + (Hash(k, fi) - 0.5f) * 0.5f;
                        for (float y = f.y0 + 0.5f; y < f.y1 - 1f; y += 0.5f)
                        {
                            var q = Pt(f, u, y) + f.n * 0.6f;
                            if (!InL(q)) continue;
                            if (!RayL(q, Vector3.down, 0.76f, out var hit, out var hp, out var hn) || hn.y < 0.9f || !IsHullFloor(hit.collider)) continue;
                            cands.Add((f, u, hp.y));
                            y += 1.5f;
                        }
                    }
                }
            Shuffle(cands);
            var made = new Dictionary<int, GameObject>();
            foreach (var (f, u, fy) in cands)
            {
                if (n >= MaxProps) break;
                var basePt = Pt(f, u, fy);
                if (Hash(Mathf.RoundToInt(basePt.x * 3), Mathf.RoundToInt(basePt.z * 3 + fy * 7)) > 0.62f) continue;
                var set = PropSet(basePt);
                int pi = set[rnd.Next(set.Length)];
                var pd = PropDefs[pi];
                if (!PropFits(f, u, fy, pd)) { pd = PropDefs[PI("SeaChest")]; pi = PI("SeaChest"); if (!PropFits(f, u, fy, pd)) continue; }
                if (!made.TryGetValue(pi, out var pf)) { var def = pd; pf = Prefab("Props", (propColliders ? "Prop_" : "Prop_NoCol_") + def.name, pb => { pb.interior = true; def.build(pb); if (propColliders) pb.boxes.Add((V(0, def.h * 0.5f, def.d * 0.5f), V(def.w, def.h, def.d), Quaternion.identity)); }); made[pi] = pf; }
                var go = Place(pf, parent, basePt + f.n * PropGap, Quaternion.LookRotation(f.n, Vector3.up), pd.name);
                var cen = basePt + f.n * (pd.d * 0.5f + PropGap) + Vector3.up * pd.h * 0.5f;
                placedProps.Add(new Bounds(cen, Sz(f, pd.w, pd.h, pd.d) + Vector3.one * 0.5f));
                n++;
            }
            return n;
        }

        static bool PropFits(VF f, float u, float fy, PropDef pd)
        {
            var ua = UAxis(f);
            var cen = Pt(f, u, fy + pd.h * 0.5f + 0.03f) + f.n * (pd.d * 0.5f + PropGap + 0.01f);
            if (!InL(cen) || !FreeL(cen, Sz(f, pd.w - 0.04f, pd.h - 0.06f, pd.d - 0.04f))) return false;
            var bb = new Bounds(cen, Sz(f, pd.w, pd.h, pd.d));
            foreach (var pb in placedProps) if (pb.Intersects(bb)) return false;
            foreach (var lp in placedLights) if (bb.SqrDistance(lp) < 0.36f) return false;
            float diag = new Vector3(pd.w, pd.h, pd.d).magnitude * 0.5f;
            if (NearG(cen, diag + 0.4f)) return false;
            // a continuous wall behind, and floor under every corner (no props over edges or across doorways)
            foreach (float s in new[] { -0.5f, 0.5f })
                foreach (float hy in new[] { 0.4f, Mathf.Max(0.5f, pd.h - 0.15f) })
                {
                    var o = Pt(f, u + s * (pd.w - 0.1f), fy + hy) + f.n * 0.5f;
                    if (!RayL(o, -f.n, 0.75f, out var hit, out _, out var hn) || Vector3.Dot(hn, f.n) < 0.9f || IsGameplay(hit.collider)) return false;
                }
            foreach (float s in new[] { -0.5f, 0.5f })
                foreach (float dd in new[] { PropGap + 0.12f, PropGap + pd.d })
                {
                    var o = Pt(f, u + s * (pd.w - 0.1f), fy + 0.3f) + f.n * dd;
                    if (!RayL(o, Vector3.down, 0.5f, out var hit, out _, out var hn) || hn.y < 0.9f || IsGameplay(hit.collider)) return false;
                }
            return true;
        }

        static void Barrel(MB mb, Vector3 b, float r, float h, Color col)
        {
            mb.Prism(b, 10, r * 0.9f, r, h * 0.5f, col);
            mb.Prism(b + V(0, h * 0.5f, 0), 10, r, r * 0.9f, h * 0.5f, col);
            foreach (float t in new[] { 0.12f, 0.36f, 0.64f, 0.88f }) { float rr = Mathf.Lerp(r * 0.9f, r, 1f - Mathf.Abs(t - 0.5f) * 2f) + 0.012f; mb.Prism(b + V(0, h * t - 0.025f, 0), 10, rr, rr, 0.05f, Iron); }
            mb.Prism(b + V(0, h - 0.005f, 0), 10, r * 0.8f, r * 0.8f, 0.02f, Shade(col, 0.7f));
        }

        static void Barrels(PB pb)
        {
            var mb = pb.s;
            Barrel(mb, V(-0.62f, 0, 0.5f), 0.31f, 1.05f, Wood);
            Barrel(mb, V(0.02f, 0, 0.42f), 0.31f, 1.05f, Shade(Wood, 0.9f));
            Barrel(mb, V(0.64f, 0, 0.55f), 0.3f, 0.95f, RedPaint);
            mb.Prism(V(0.02f, 1.05f, 0.42f), 10, 0.2f, 0.2f, 0.05f, Rope);
        }

        static void CrateStackProp(PB pb)
        {
            var mb = pb.s;
            Crate(mb, V(-0.58f, 0.55f, 0.58f), V(1.1f, 1.1f, 1.1f), Wood, true);
            Crate(mb, V(0.6f, 0.5f, 0.55f), V(1.05f, 1.0f, 1.0f), WoodL, true);
            Crate(mb, V(-0.35f, 1.58f, 0.55f), V(0.95f, 0.95f, 0.95f), Shade(Wood, 0.85f), false);
            Crate(mb, V(0.62f, 1.32f, 0.5f), V(0.62f, 0.62f, 0.62f), CanvasG, false);
            mb.Box(V(-0.58f, 0.75f, 1.135f), V(0.5f, 0.3f, 0.02f), Cream, Quaternion.identity);
        }

        static void Sacks(PB pb)
        {
            var mb = pb.s;
            var spots = new[] { (V(-0.5f, 0.2f, 0.45f), 8f), (V(0.3f, 0.2f, 0.5f), -6f), (V(-0.1f, 0.58f, 0.45f), 4f), (V(0.6f, 0.18f, 0.25f), 70f) };
            int i = 0;
            foreach (var (p, a) in spots)
            {
                var col = i % 2 == 0 ? Canvas : Shade(Canvas, 0.88f); var r = Quaternion.Euler(0, a, 0);
                mb.Box(p, V(0.8f, 0.36f, 0.52f), col, r);
                mb.Box(p + V(0, 0.16f, 0), V(0.66f, 0.1f, 0.42f), col, r);
                mb.Box(p + r * V(0.42f, 0.02f, 0), V(0.1f, 0.22f, 0.3f), RopeD, r);
                i++;
            }
        }

        static void ShellRack(PB pb)
        {
            var mb = pb.s;
            foreach (float x in new[] { -0.96f, 0.96f }) mb.Box(V(x, 0.85f, 0.32f), V(0.08f, 1.7f, 0.6f), SteelD, Quaternion.identity);
            foreach (float y in new[] { 0.08f, 0.8f, 1.55f }) mb.Box(V(0, y, 0.32f), V(1.9f, 0.06f, 0.6f), Steel2, Quaternion.identity);
            mb.Box(V(0, 0.85f, 0.03f), V(1.9f, 1.7f, 0.04f), SteelD, Quaternion.identity);
            foreach (float y in new[] { 0.11f, 0.83f })
                for (int i = 0; i < 6; i++)
                {
                    var b = V(-0.75f + i * 0.3f, y, 0.34f);
                    mb.Prism(b, 8, 0.11f, 0.11f, 0.42f, Brass);
                    mb.Prism(b + V(0, 0.42f, 0), 8, 0.11f, 0.03f, 0.2f, SteelL);
                    mb.Prism(b + V(0, 0.3f, 0), 8, 0.115f, 0.115f, 0.04f, Copper);
                }
            mb.Box(V(0, 1.2f, 0.62f), V(1.9f, 0.05f, 0.03f), Iron, Quaternion.identity);
        }

        static void Workbench(PB pb)
        {
            var mb = pb.s;
            mb.Box(V(0, 0.9f, 0.43f), V(2.2f, 0.08f, 0.82f), WoodL, Quaternion.identity);
            foreach (float x in new[] { -1.02f, 1.02f }) foreach (float z in new[] { 0.1f, 0.76f }) mb.Box(V(x, 0.43f, z), V(0.08f, 0.86f, 0.08f), WoodD, Quaternion.identity);
            mb.Box(V(0, 0.28f, 0.43f), V(2.1f, 0.05f, 0.75f), WoodD, Quaternion.identity);
            mb.Box(V(0, 1.45f, 0.03f), V(2.2f, 1.0f, 0.05f), Wood, Quaternion.identity);
            mb.Box(V(0, 1.95f, 0.15f), V(2.2f, 0.05f, 0.3f), WoodD, Quaternion.identity);
            for (int i = 0; i < 6; i++) mb.Box(V(-0.85f + i * 0.33f, 1.45f + (i % 2) * 0.1f, 0.07f), V(0.05f, 0.42f, 0.03f), i % 3 == 0 ? RedPaint : Iron, Quaternion.Euler(0, 0, (i % 2) * 12f));
            mb.Box(V(0.75f, 1.0f, 0.6f), V(0.22f, 0.14f, 0.18f), Iron, Quaternion.identity);
            mb.Box(V(0.75f, 1.1f, 0.72f), V(0.3f, 0.06f, 0.05f), Iron, Quaternion.identity);
            mb.Prism(V(-0.4f, 0.94f, 0.5f), 8, 0.18f, 0.18f, 0.06f, SteelL);
            mb.Box(V(-0.85f, 1.04f, 0.35f), V(0.4f, 0.2f, 0.25f), RedPaint, Quaternion.identity);
            mb.Box(V(0.1f, 0.4f, 0.45f), V(0.5f, 0.2f, 0.4f), CanvasG, Quaternion.identity);
            mb.Prism(V(-0.6f, 1.97f, 0.15f), 8, 0.1f, 0.1f, 0.22f, Copper);
        }

        static void SeaChest(PB pb)
        {
            var mb = pb.s;
            mb.Box(V(0, 0.27f, 0.32f), V(1.1f, 0.5f, 0.6f), WoodD, Quaternion.identity);
            mb.Box(V(0, 0.6f, 0.32f), V(1.14f, 0.18f, 0.64f), Wood, Quaternion.identity);
            foreach (float x in new[] { -0.4f, 0.4f }) mb.Box(V(x, 0.36f, 0.32f), V(0.07f, 0.72f, 0.66f), Iron, Quaternion.identity);
            mb.Box(V(0, 0.5f, 0.645f), V(0.14f, 0.16f, 0.03f), Brass, Quaternion.identity);
        }

        static void CoalBin(PB pb)
        {
            var mb = pb.s;
            mb.Box(V(0, 0.55f, 0.05f), V(2.2f, 1.1f, 0.1f), WoodD, Quaternion.identity);
            foreach (float x in new[] { -1.05f, 1.05f }) mb.Box(V(x, 0.45f, 0.65f), V(0.1f, 0.9f, 1.3f), WoodD, Quaternion.identity);
            mb.Box(V(0, 0.3f, 1.25f), V(2.2f, 0.6f, 0.1f), Wood, Quaternion.identity);
            mb.Box(V(0, 0.2f, 0.65f), V(2.0f, 0.4f, 1.1f), Coal, Quaternion.identity);
            var rr = new System.Random(7);
            for (int i = 0; i < 7; i++)
            {
                var p = V(-0.8f + (float)rr.NextDouble() * 1.6f, 0.38f, 0.25f + (float)rr.NextDouble() * 0.7f);
                mb.Prism(p, 5, 0.32f + (float)rr.NextDouble() * 0.2f, 0.05f, 0.35f + (float)rr.NextDouble() * 0.35f, Coal, (float)rr.NextDouble() * 70f);
            }
            mb.Rod(V(0.7f, 0.05f, 1.35f), V(0.95f, 1.25f, 1.15f), 0.05f, WoodL);
            mb.Box(V(0.68f, 0.12f, 1.42f), V(0.28f, 0.03f, 0.3f), Iron, Quaternion.Euler(-10f, 0, 0));
        }

        static void Lockers(PB pb)
        {
            var mb = pb.s;
            mb.Box(V(0, 1.07f, 0.3f), V(2.1f, 2.14f, 0.58f), Green, Quaternion.identity);
            for (int i = 0; i < 3; i++)
            {
                float x = -0.7f + i * 0.7f;
                mb.Box(V(x, 1.1f, 0.6f), V(0.64f, 1.95f, 0.03f), Shade(Green, 1.08f), Quaternion.identity);
                for (int k = 0; k < 4; k++) mb.Box(V(x, 1.75f + k * 0.07f, 0.62f), V(0.3f, 0.03f, 0.02f), Black, Quaternion.identity);
                mb.Box(V(x + 0.24f, 1.05f, 0.63f), V(0.04f, 0.18f, 0.04f), Brass, Quaternion.identity);
            }
            mb.Box(V(0, 2.16f, 0.3f), V(2.14f, 0.04f, 0.6f), Shade(Green, 0.8f), Quaternion.identity);
            mb.Box(V(-0.5f, 2.3f, 0.3f), V(0.5f, 0.24f, 0.36f), CanvasG, Quaternion.identity);
        }

        static void ValveBank(PB pb)
        {
            var mb = pb.s;
            mb.Box(V(0, 1.15f, 0.025f), V(1.6f, 2.3f, 0.05f), SteelD, Quaternion.identity);
            foreach (float x in new[] { -0.5f, 0f, 0.5f })
            {
                mb.Cyl(V(x, 0f, 0.22f), V(x, 2.3f, 0.22f), 0.08f, 8, Copper);
                mb.Prism(V(x, 0.95f, 0.22f), 8, 0.11f, 0.11f, 0.12f, Brass);
                ValveWheel(mb, V(x, 1.35f, 0.34f), Vector3.forward, 0.16f);
            }
            mb.Cyl(V(-0.7f, 0.7f, 0.22f), V(0.7f, 0.7f, 0.22f), 0.1f, 8, SteelL);
            foreach (float x in new[] { -0.25f, 0.25f }) Gauge(mb, V(x, 1.85f, 0.07f), Vector3.forward, 0.14f);
            mb.Box(V(0, 0.08f, 0.25f), V(1.5f, 0.16f, 0.5f), Iron, Quaternion.identity);
        }

        static void PowderKegs(PB pb)
        {
            var mb = pb.s;
            Barrel(mb, V(-0.45f, 0, 0.42f), 0.3f, 0.8f, WoodD);
            Barrel(mb, V(0.2f, 0, 0.42f), 0.28f, 0.75f, WoodD);
            var q = Quaternion.FromToRotation(Vector3.up, Vector3.right);
            mb.Prism(V(0.35f, 0.24f, 0.62f), 10, 0.22f, 0.22f, 0.45f, Wood, q);
            mb.Box(V(-0.45f, 0.45f, 0.73f), V(0.25f, 0.2f, 0.02f), RedPaint, Quaternion.identity);
            mb.Box(V(0.2f, 0.42f, 0.71f), V(0.25f, 0.2f, 0.02f), RedPaint, Quaternion.identity);
        }
    }
}
