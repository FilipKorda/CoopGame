using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CoopGame.EditorTools
{
    /// <summary>
    /// Two bodies, both read from templates in the open Game scene (only read):
    /// - male, from "Sentinel Cube Body": the Cubes_* meshes, joints of Player.prefab (body 0) and Actor_Player1;
    /// - female, from "Sentinel Cube Body Female": the Female/Cubes_* meshes and joints of Actor_Player2, and the
    ///   player's second body (PlayerBody on Player.prefab, put on the second player at runtime).
    /// Makes the player's cube figure from "Sentinel Cube Body" in the open scene (the template; it is only read).
    /// Every template part is straightened (its cubes turned to face forward and lined up along its bone) and baked
    /// into the matching Cubes_* mesh on the PlayerCubeRig bones, so the figure keeps all its animation:
    /// torso -> Pelvis (below the waist), Abdomen + Chest, Neck; Head; arms; thighs; shins split into shin + foot.
    /// The joints of the Player and both cutscene actors are moved to the template's proportions (both sides alike,
    /// each part's cubes stretched a little along its bone to the average length of the two sides).
    /// Gaps between the cubes show a dark backing, and dark blocks fill the joints when they bend.
    /// </summary>
    public static class PlayerSentinelFigureBuilder
    {
        const string MeshDir = "Assets/Meshes/PlayerCubes";
        const string PlayerPrefab = "Assets/Prefabs/Player.prefab";
        const string Actor1Prefab = "Assets/Prefabs/Cutscenes/Actor_Player1.prefab";
        const string Actor2Prefab = "Assets/Prefabs/Cutscenes/Actor_Player2.prefab";

        /// <summary>What one build reads and where it goes.</summary>
        class Config
        {
            public string template;
            public string meshDir;
            // Heights in the template's torso frame: cubes below waistY are the pelvis (moves with the hips), above neckY the neck.
            public float waistY, neckY;
            // Thigh cubes turned like the torso (the female's hip sides, on the torso's grid) belong to the pelvis.
            public bool hipSidesToPelvis;
            // The arms also hang clear of the pelvis (wide hips), not only of the belly.
            public bool armsClearPelvis;
            // Shin cubes with their centre below this height (template world y) are the foot (NaN = found by shape).
            public float footBelowY = float.NaN;
            // The thighs start where they do in the template (overlapping the pelvis) and the shins overlap the thighs
            // as much as there, instead of each part below the one above it (keeps the template's height).
            // The shin's top also sits under the thigh's bottom, as there (a tapered thigh's bottom is off its axis).
            public bool legOverlapsFromTemplate;
            // The feet face straight ahead and stand level (in the template they are turned out and its shins lean).
            public bool straightFeet;
            // Prefabs whose joints are moved; the first one is measured for the sole height.
            public string[] prefabs;
            // Point those prefabs' figure parts at this build's meshes (they may still show another body's).
            public bool assignMeshes;
            // Also store the result as the player's second body (PlayerBody on Player.prefab).
            public bool playerVariant;
        }

        // The player (host) and the first cutscene stand-in: the male figure.
        static readonly Config Male = new Config
        {
            template = "Sentinel Cube Body", meshDir = MeshDir, waistY = 1.09f, neckY = 1.60f,
            prefabs = new[] { PlayerPrefab, Actor1Prefab },
        };

        // The second player and the second cutscene stand-in: the female figure.
        static readonly Config Female = new Config
        {
            template = "Sentinel Cube Body Female", meshDir = MeshDir + "/Female", waistY = 1.155f, neckY = 1.62f,
            hipSidesToPelvis = true, armsClearPelvis = true, footBelowY = 0.10f, legOverlapsFromTemplate = true, straightFeet = true,
            prefabs = new[] { Actor2Prefab }, assignMeshes = true, playerVariant = true,
        };

        // Figure parts and joints as PlayerBody lists them (paths under Visual/CubeFigure).
        static readonly string[] PartPaths =
        {
            "Hips/Pelvis",
            "Hips/Hip_L/Thigh_L", "Hips/Hip_L/Knee_L/Shin_L", "Hips/Hip_L/Knee_L/Ankle_L/Foot_L",
            "Hips/Hip_R/Thigh_R", "Hips/Hip_R/Knee_R/Shin_R", "Hips/Hip_R/Knee_R/Ankle_R/Foot_R",
            "UpperBody/Abdomen", "UpperBody/Chest", "UpperBody/Neck", "UpperBody/Head",
            "UpperBody/Shoulder_L/UpperArm_L", "UpperBody/Shoulder_L/Elbow_L/Forearm_L",
            "UpperBody/Shoulder_R/UpperArm_R", "UpperBody/Shoulder_R/Elbow_R/Forearm_R",
        };
        static readonly string[] JointPaths =
        {
            "Hips", "UpperBody",
            "Hips/Hip_L", "Hips/Hip_L/Knee_L", "Hips/Hip_L/Knee_L/Ankle_L",
            "Hips/Hip_R", "Hips/Hip_R/Knee_R", "Hips/Hip_R/Knee_R/Ankle_R",
            "UpperBody/Shoulder_L", "UpperBody/Shoulder_L/Elbow_L",
            "UpperBody/Shoulder_R", "UpperBody/Shoulder_R/Elbow_R",
        };
        // Gap between the template's cubes.
        const float Gap = 0.0056f;
        // Hip / shoulder pivot this far below the top of the thigh / upper arm.
        const float PivotDrop = 0.03f;
        // Where the hand holds on (ledges, cutscene targets): this share of the forearm's length below the elbow.
        const float GripFrac = 0.77f;
        // Soles in the rest pose. The CharacterController rests with the root at y -0.005, so the soles end up 2 cm over the
        // collision floor (kit floors in some acts are a few cm above it; was -0.05, which sank them into the floor).
        const float SoleY = 0.015f;
        // The arms hang at least this far clear of the belly (at the rig's arm spread).
        const float ArmClear = 0.01f;
        // Previous figure: hanging from a ledge keeps the shoulders where they were; the back mount follows the chest top.
        const float OldShoulderY = 1.678f, OldHangLower = 0.15f;
        const float OldChestTop = 1.693f, OldBackMountY = 1.5f, BackMountInset = 0.031f;

        struct Box { public Vector3 c; public Quaternion r; public Vector3 h; }

        // One mesh: its cubes (template world space) and how they map into the bone's space.
        class Piece
        {
            public string mesh;
            public List<Box> boxes = new List<Box>();
            public List<Box> group;          // cubes that count as neighbours (the same rigid part)
            public Quaternion inv = Quaternion.identity;
            public Vector3 a, b;             // anchor in the part's frame -> its place in the bone's space
            public float s = 1f;             // stretch along the bone
            public List<(Vector3 c, Vector3 h)> fillers = new List<(Vector3, Vector3)>(); // dark joint blocks (bone space)
            public Vector3 Map(Vector3 w)
            {
                var l = inv * w;
                return new Vector3(l.x - a.x + b.x, b.y + (l.y - a.y) * s, l.z - a.z + b.z);
            }
        }

        // A limb part in its straightened frame.
        class Limb
        {
            public Quaternion canon, inv;
            public List<Box> main = new List<Box>(), foot = new List<Box>();
            public float top, bottom, footTop, footBottom;
            public float ax, az, hx, hz;          // axis (middle of the part) and half its width / depth
            public Vector4 topRect, bottomRect;   // end layers around the axis: x0, x1, z0, z1
            public float Len => top - bottom;
            public float FootLen => foot.Count > 0 ? footTop - footBottom : 0f;
        }

        [MenuItem("Tools/Coop/Player/Build Figure From Sentinel Cube Body")]
        public static void BuildMenu() => Debug.Log(Build());

        [MenuItem("Tools/Coop/Player/Build Female Figure From Sentinel Cube Body Female")]
        public static void BuildFemaleMenu() => Debug.Log(BuildFemale());

        /// <summary>The male figure: Player.prefab (body 0) and Actor_Player1.</summary>
        public static string Build() => Build(Male);

        /// <summary>The female figure: Actor_Player2, and the player's second body (PlayerBody on Player.prefab).</summary>
        public static string BuildFemale() => Build(Female);

        static string Build(Config cfg)
        {
            var log = new System.Text.StringBuilder();
            var parts = ReadTemplate(cfg.template, out string err);
            if (err != null) return err;
            foreach (var n in new[] { "Head", "Torso", "UpperArm_L", "UpperArm_R", "Forearm_L", "Forearm_R", "Thigh_L", "Thigh_R", "Shin_L", "Shin_R" })
                if (!parts.ContainsKey(n) || parts[n].Count == 0) return "template part missing: " + n;
            if (cfg.hipSidesToPelvis)
            {
                var tm = Major(parts["Torso"]); int moved = 0;
                foreach (var sd in new[] { "Thigh_L", "Thigh_R" })
                {
                    var keep = new List<Box>();
                    foreach (var b in parts[sd]) if (Quaternion.Angle(b.r, tm) < 0.5f) { parts["Torso"].Add(b); moved++; } else keep.Add(b);
                    parts[sd] = keep;
                }
                log.AppendLine("hip side cubes moved from the thighs to the pelvis: " + moved);
            }

            // ---- torso: its frame decides where forward is ----
            var torso = parts["Torso"];
            var ct = Canon(Major(torso), Vector3.forward);
            var it = Quaternion.Inverse(ct);
            var fwd = ct * Vector3.forward; fwd.y = 0f; fwd.Normalize();
            var pel = new List<Box>(); var upper = new List<Box>(); var neck = new List<Box>();
            foreach (var b in torso) { float y = (it * b.c).y; (y < cfg.waistY ? pel : y < cfg.neckY ? upper : neck).Add(b); }
            if (pel.Count == 0 || upper.Count == 0) return "torso split failed (check waistY / neckY)";
            Range(pel, it, out var pLo, out var pHi);
            Range(upper, it, out var uLo, out var uHi);
            var tLo = Vector3.Min(pLo, uLo); var tHi = Vector3.Max(pHi, uHi);
            float xc = (tLo.x + tHi.x) * 0.5f, zc = (tLo.z + tHi.z) * 0.5f;
            float torsoBottom = pLo.y, ubY = pHi.y, chestTop = uHi.y;
            float hipsY = (torsoBottom + ubY) * 0.5f, chestSplit = (ubY + chestTop) * 0.5f;
            var abd = new List<Box>(); var chest = new List<Box>();
            foreach (var b in upper) ((it * b.c).y < chestSplit ? abd : chest).Add(b);
            // template world -> figure (straight pose, torso centred on the figure's axis)
            Vector3 Fig(Vector3 w) { var l = it * w; return new Vector3(l.x - xc, l.y, l.z - zc); }

            // ---- limbs ----
            var thL = Analyse(parts["Thigh_L"], fwd, false); var thR = Analyse(parts["Thigh_R"], fwd, false);
            var shL = Analyse(parts["Shin_L"], fwd, true, cfg.footBelowY); var shR = Analyse(parts["Shin_R"], fwd, true, cfg.footBelowY);
            var uaL = Analyse(parts["UpperArm_L"], fwd, false); var uaR = Analyse(parts["UpperArm_R"], fwd, false);
            var faL = Analyse(parts["Forearm_L"], fwd, false); var faR = Analyse(parts["Forearm_R"], fwd, false);
            float lenThigh = Avg(thL.Len, thR.Len), lenShin = Avg(shL.Len, shR.Len), lenFoot = Avg(shL.FootLen, shR.FootLen);
            float lenUpper = Avg(uaL.Len, uaR.Len), lenFore = Avg(faL.Len, faR.Len);
            Vector3 TopAt(Limb l) => Fig(l.canon * new Vector3(l.ax, l.top, l.az));
            Vector3 BottomAt(Limb l) => Fig(l.canon * new Vector3(l.ax, l.bottom, l.az));

            float spread = 10f;
            // The arms hang at this body's own arm spread (its walk; the first prefab's rig).
            var playerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(cfg.prefabs[0]);
            var rigAsset = playerAsset != null ? playerAsset.GetComponentInChildren<PlayerCubeRig>(true) : null;
            if (rigAsset != null) spread = new SerializedObject(rigAsset).FindProperty("armSpread").floatValue;

            // Joints (straight pose, figure space before the final height shift).
            float hipPivot = torsoBottom - Gap - PivotDrop;
            float kneeOverlap = 0f;
            if (cfg.legOverlapsFromTemplate)
            {
                float thighTop = (TopAt(thL).y + TopAt(thR).y) * 0.5f;
                hipPivot = Mathf.Max(hipPivot, thighTop - PivotDrop);
                kneeOverlap = Mathf.Max(0f, (TopAt(shL).y - BottomAt(thL).y + TopAt(shR).y - BottomAt(thR).y) * 0.5f);
                log.AppendLine("legs as in the template: thigh top " + thighTop.ToString("F3") + " (pelvis bottom " + torsoBottom.ToString("F3") + "), shin overlaps thigh " + kneeOverlap.ToString("F3"));
            }
            float hipX = (-TopAt(thL).x + TopAt(thR).x) * 0.5f, hipZ = (TopAt(thL).z + TopAt(thR).z) * 0.5f;
            float shPivot = chestTop - 0.01f - PivotDrop;
            float shZ = (TopAt(uaL).z + TopAt(uaR).z) * 0.5f;
            float shX = (-TopAt(uaL).x + TopAt(uaR).x) * 0.5f;
            // Arms far enough out to hang clear of the belly.
            float armHalf = Mathf.Max(Mathf.Max(uaL.hx, uaR.hx), Mathf.Max(faL.hx, faR.hx));
            float sinS = Mathf.Sin(spread * Mathf.Deg2Rad);
            var cs = new List<Vector3>();
            var clearOf = new List<Box>(upper);
            if (cfg.armsClearPelvis) clearOf.AddRange(pel);
            foreach (var b in clearOf)
            {
                Corners(b, cs);
                foreach (var w in cs)
                {
                    var f = Fig(w); if (f.y > shPivot - 0.15f) continue;
                    shX = Mathf.Max(shX, Mathf.Abs(f.x) + ArmClear + armHalf - (shPivot - f.y) * sinS);
                }
            }
            float kneeY = PivotDrop - lenThigh - Gap * 0.5f + kneeOverlap;
            float ankleY = -Gap - lenShin;
            float elbowY = PivotDrop - lenUpper - Gap * 0.5f;
            float gripY = -Gap * 0.5f - GripFrac * lenFore;

            // ---- pieces ----
            var pieces = new List<Piece>();
            Piece Torso(string mesh, List<Box> bx, List<Box> grp, float boneY)
            {
                var p = new Piece { mesh = mesh, boxes = bx, group = grp, inv = it, a = new Vector3(xc, 0f, zc), b = new Vector3(0f, -boneY, 0f) };
                pieces.Add(p); return p;
            }
            var upperGroup = new List<Box>(upper);
            var headNeck = new List<Box>(neck); headNeck.AddRange(parts["Head"]);
            Torso("Pelvis", pel, pel, hipsY);
            var abdP = Torso("Abdomen", abd, upperGroup, ubY);
            Torso("Chest", chest, upperGroup, ubY);
            Torso("Neck", neck, headNeck, ubY);
            {
                // Head: turned to face forward about its own centre.
                var hb = parts["Head"]; var chd = Canon(Major(hb), fwd); var ih = Quaternion.Inverse(chd);
                Range(hb, ih, out var hLo, out var hHi);
                var hc = (hLo + hHi) * 0.5f; var fc = Fig(chd * hc);
                pieces.Add(new Piece { mesh = "Head", boxes = hb, group = headNeck, inv = ih, a = hc, b = new Vector3(fc.x, fc.y - ubY, fc.z) });
            }
            // waist block: between the pelvis top and the belly, inside both
            {
                var pr = LayerRect(pel, it, pHi.y, true, xc, zc); var ur = LayerRect(upper, it, uLo.y, false, xc, zc);
                AddFiller(abdP, Shrink(Overlap(pr, ur)), -0.05f, 0.09f);
            }
            void LimbPieces(string sd, Limb th, Limb sh, Limb ua, Limb fa)
            {
                // Each part is centred on its own axis. Moves the shin (and its foot) so the shin's top layer is under
                // the thigh's bottom layer, as in the template.
                float kx = 0f, kz = 0f;
                if (cfg.legOverlapsFromTemplate && !float.IsInfinity(th.bottomRect.x) && !float.IsInfinity(sh.topRect.x))
                {
                    kx = (th.bottomRect.x + th.bottomRect.y - sh.topRect.x - sh.topRect.y) * 0.5f;
                    kz = (th.bottomRect.z + th.bottomRect.w - sh.topRect.z - sh.topRect.w) * 0.5f;
                }
                var t = new Piece { mesh = "Thigh" + sd, boxes = th.main, group = th.main, inv = th.inv, a = new Vector3(th.ax, th.top, th.az), b = new Vector3(0f, PivotDrop, 0f), s = lenThigh / th.Len };
                AddFiller(t, Shrink(th.topRect), -0.04f, PivotDrop + 0.085f);
                var k = new Piece { mesh = "Shin" + sd, boxes = sh.main, group = sh.main, inv = sh.inv, a = new Vector3(sh.ax, sh.top, sh.az), b = new Vector3(kx, -Gap * 0.5f, kz), s = lenShin / sh.Len };
                AddFiller(k, Shrink(Overlap(th.bottomRect, Moved(sh.topRect, kx, kz))), -0.045f, 0.09f);
                var f = new Piece { mesh = "Foot" + sd, boxes = sh.foot, group = sh.foot, inv = sh.inv, a = new Vector3(sh.ax, sh.footTop, sh.az), b = new Vector3(kx, -Gap * 0.5f, kz), s = sh.FootLen > 0f ? lenFoot / sh.FootLen : 1f };
                if (cfg.straightFeet && sh.foot.Count > 0)
                {
                    // In the foot's own frame (its cubes' frame), turned about the ankle point on the shin's axis.
                    var fc = Canon(Major(sh.foot), fwd);
                    f.inv = Quaternion.Inverse(fc);
                    f.a = f.inv * (sh.canon * f.a);
                    var toe = fc * Vector3.forward; toe.y = 0f;
                    log.AppendLine(sd + ": foot turned " + Vector3.SignedAngle(toe, fwd, Vector3.up).ToString("F1") + " deg to face ahead, level");
                }
                AddFiller(f, Shrink(Moved(sh.bottomRect, kx, kz)), -0.035f, 0.07f);
                if (kx != 0f || kz != 0f) log.AppendLine(sd + ": shin and foot moved under the thigh by x " + kx.ToString("F3") + " z " + kz.ToString("F3"));
                var u = new Piece { mesh = "UpperArm" + sd, boxes = ua.main, group = ua.main, inv = ua.inv, a = new Vector3(ua.ax, ua.top, ua.az), b = new Vector3(0f, PivotDrop, 0f), s = lenUpper / ua.Len };
                var e = new Piece { mesh = "Forearm" + sd, boxes = fa.main, group = fa.main, inv = fa.inv, a = new Vector3(fa.ax, fa.top, fa.az), b = new Vector3(0f, -Gap * 0.5f, 0f), s = lenFore / fa.Len };
                AddFiller(e, Shrink(Overlap(ua.bottomRect, fa.topRect)), -0.04f, 0.08f);
                pieces.Add(t); pieces.Add(k); pieces.Add(f); pieces.Add(u); pieces.Add(e);
                log.AppendLine(sd + ": stretch thigh " + t.s.ToString("F3") + " shin " + k.s.ToString("F3") + " foot " + f.s.ToString("F3") + " upper arm " + u.s.ToString("F3") + " forearm " + e.s.ToString("F3"));
            }
            LimbPieces("_L", thL, shL, uaL, faL);
            LimbPieces("_R", thR, shR, uaR, faR);

            // ---- meshes ----
            int total = 0;
            foreach (var p in pieces)
            {
                int n = WriteMesh(p, cfg.meshDir, out int verts, out Vector3 size);
                if (n < 0) { log.AppendLine("missing mesh " + p.mesh); continue; }
                total += n;
                log.AppendLine(p.mesh + " cubes=" + n + " verts=" + verts + " size=" + size.ToString("F3"));
            }
            AssetDatabase.SaveAssets();
            log.AppendLine("TOTAL cubes=" + total);

            // ---- joints: straight pose first, then the whole figure moved so the soles are where they were ----
            var j = new Joints
            {
                hipsY = hipsY, ubY = ubY, hipX = hipX, hipY = hipPivot - hipsY, hipZ = hipZ, kneeY = kneeY, ankleY = ankleY,
                shX = shX, shY = shPivot - ubY, shZ = shZ, elbowY = elbowY, gripY = gripY, shPivot = shPivot, chestTop = chestTop,
            };
            float backZ = 0f, backMin = float.PositiveInfinity;
            foreach (var b in chest) { Corners(b, cs); foreach (var w in cs) backMin = Mathf.Min(backMin, Fig(w).z); }
            backZ = backMin + BackMountInset;
            j.backZ = backZ;
            if (cfg.assignMeshes) foreach (var path in cfg.prefabs) log.AppendLine(AssignMeshes(path, cfg.meshDir));
            foreach (var path in cfg.prefabs) ApplyJoints(path, j, 0f);
            float sole = RestSole(cfg.prefabs[0]);
            float shift = SoleY - sole;
            foreach (var path in cfg.prefabs) log.AppendLine(ApplyJoints(path, j, shift));
            if (cfg.playerVariant) log.AppendLine(StorePlayerVariant(cfg, j, shift));
            log.AppendLine("forward " + fwd.ToString("F3") + " torso centre x " + xc.ToString("F3") + " z " + zc.ToString("F3") + " height shift " + shift.ToString("F3"));
            log.AppendLine("hips " + (hipsY + shift).ToString("F3") + " upper body " + (ubY + shift).ToString("F3") + " hip " + hipX.ToString("F3") + "/" + (hipPivot - hipsY).ToString("F3") + "/" + hipZ.ToString("F3")
                + " knee " + kneeY.ToString("F3") + " ankle " + ankleY.ToString("F3"));
            log.AppendLine("shoulder " + shX.ToString("F3") + "/" + (shPivot - ubY).ToString("F3") + "/" + shZ.ToString("F3") + " (template " + ((-TopAt(uaL).x + TopAt(uaR).x) * 0.5f).ToString("F3") + ") elbow " + elbowY.ToString("F3") + " grip " + gripY.ToString("F3"));
            log.AppendLine("lengths thigh " + thL.Len.ToString("F3") + "/" + thR.Len.ToString("F3") + " shin " + shL.Len.ToString("F3") + "/" + shR.Len.ToString("F3") + " foot " + shL.FootLen.ToString("F3") + "/" + shR.FootLen.ToString("F3")
                + " upper arm " + uaL.Len.ToString("F3") + "/" + uaR.Len.ToString("F3") + " forearm " + faL.Len.ToString("F3") + "/" + faR.Len.ToString("F3"));
            log.AppendLine("rest sole before shift " + sole.ToString("F3") + ", after " + RestSole(cfg.prefabs[0]).ToString("F3"));
            return log.ToString();
        }

        // ---------- template ----------

        static Dictionary<string, List<Box>> ReadTemplate(string templateName, out string err)
        {
            err = null;
            var root = GameObject.Find(templateName);
            if (root == null) { err = "'" + templateName + "' not found in the open scenes"; return null; }
            var res = new Dictionary<string, List<Box>>();
            foreach (Transform part in root.transform)
            {
                var list = new List<Box>();
                foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    var t = mf.transform; var bb = mf.sharedMesh.bounds;
                    list.Add(new Box { c = t.TransformPoint(bb.center), r = t.rotation, h = Vector3.Scale(bb.extents, t.lossyScale) });
                }
                res[part.name] = list;
            }
            return res;
        }

        // The rotation most of the part's cubes share.
        static Quaternion Major(List<Box> boxes)
        {
            Quaternion best = Quaternion.identity; int bestN = -1;
            foreach (var x in boxes)
            {
                int n = 0;
                foreach (var y in boxes) if (Quaternion.Angle(x.r, y.r) < 0.5f) n++;
                if (n > bestN) { bestN = n; best = x.r; }
            }
            return best;
        }

        // The cube frame turned by quarter turns so its y points up and its z as close to forward as it gets.
        static Quaternion Canon(Quaternion r, Vector3 fwd)
        {
            Quaternion res = r; float best = float.NegativeInfinity;
            for (int i = 0; i < 64; i++)
            {
                var c = r * Quaternion.Euler(90f * (i & 3), 90f * ((i >> 2) & 3), 90f * (i >> 4));
                float sc = 10f * Vector3.Dot(c * Vector3.up, Vector3.up) + Vector3.Dot(c * Vector3.forward, fwd);
                if (sc > best + 1e-4f) { best = sc; res = c; }
            }
            return res;
        }

        static Vector3 Sgn(int i) => new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);

        static void Corners(Box b, List<Vector3> into)
        {
            into.Clear();
            for (int i = 0; i < 8; i++) into.Add(b.c + b.r * Vector3.Scale(b.h, Sgn(i)));
        }

        static void Range(List<Box> boxes, Quaternion inv, out Vector3 lo, out Vector3 hi)
        {
            lo = Vector3.one * float.PositiveInfinity; hi = -lo;
            var cs = new List<Vector3>();
            foreach (var b in boxes) { Corners(b, cs); foreach (var w in cs) { var l = inv * w; lo = Vector3.Min(lo, l); hi = Vector3.Max(hi, l); } }
        }

        // Rectangle (x0, x1, z0, z1, around the given axis) of the end layer at height y (cubes whose top / bottom is there).
        static Vector4 LayerRect(List<Box> boxes, Quaternion inv, float y, bool isTop, float ax, float az)
        {
            var r = new Vector4(float.PositiveInfinity, float.NegativeInfinity, float.PositiveInfinity, float.NegativeInfinity);
            var cs = new List<Vector3>();
            foreach (var b in boxes)
            {
                Corners(b, cs);
                float e = isTop ? float.NegativeInfinity : float.PositiveInfinity;
                foreach (var w in cs) { float ly = (inv * w).y; e = isTop ? Mathf.Max(e, ly) : Mathf.Min(e, ly); }
                if (Mathf.Abs(e - y) > 0.012f) continue;
                foreach (var w in cs) { var l = inv * w; r.x = Mathf.Min(r.x, l.x - ax); r.y = Mathf.Max(r.y, l.x - ax); r.z = Mathf.Min(r.z, l.z - az); r.w = Mathf.Max(r.w, l.z - az); }
            }
            return r;
        }

        static Vector4 Overlap(Vector4 a, Vector4 b) => new Vector4(Mathf.Max(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.z, b.z), Mathf.Min(a.w, b.w));

        static Vector4 Moved(Vector4 r, float dx, float dz) => new Vector4(r.x + dx, r.y + dx, r.z + dz, r.w + dz);

        // Kept well inside the cubes around it (15% of the width off each side).
        static Vector4 Shrink(Vector4 r)
        {
            float dx = (r.y - r.x) * 0.15f, dz = (r.w - r.z) * 0.15f;
            return new Vector4(r.x + dx, r.y - dx, r.z + dz, r.w - dz);
        }

        static void AddFiller(Piece p, Vector4 r, float y0, float height)
        {
            if (!(r.y > r.x) || !(r.w > r.z) || float.IsInfinity(r.x)) return;
            p.fillers.Add((new Vector3((r.x + r.y) * 0.5f, y0 + height * 0.5f, (r.z + r.w) * 0.5f), new Vector3((r.y - r.x) * 0.5f, height * 0.5f, (r.w - r.z) * 0.5f)));
        }

        static float Avg(float a, float b) => a > 0f && b > 0f ? (a + b) * 0.5f : Mathf.Max(a, b);

        static Limb Analyse(List<Box> boxes, Vector3 fwd, bool splitFoot, float footBelowY = float.NaN)
        {
            var L = new Limb();
            var maj = Major(boxes);
            L.canon = Canon(maj, fwd); L.inv = Quaternion.Inverse(L.canon);
            Range(boxes, L.inv, out var lo, out var hi);
            // axis: middle of the cubes between 30% and 85% of the length (no hand, no foot, no odd cubes)
            float y0 = lo.y + 0.30f * (hi.y - lo.y), y1 = lo.y + 0.85f * (hi.y - lo.y);
            var alo = Vector3.one * float.PositiveInfinity; var ahi = -alo; var cs = new List<Vector3>();
            foreach (var b in boxes)
            {
                float cy = (L.inv * b.c).y;
                if (cy < y0 || cy > y1 || Quaternion.Angle(b.r, maj) > 0.5f) continue;
                Corners(b, cs); foreach (var w in cs) { var l = L.inv * w; alo = Vector3.Min(alo, l); ahi = Vector3.Max(ahi, l); }
            }
            if (float.IsInfinity(alo.x)) { alo = lo; ahi = hi; }
            L.ax = (alo.x + ahi.x) * 0.5f; L.az = (alo.z + ahi.z) * 0.5f; L.hx = (ahi.x - alo.x) * 0.5f; L.hz = (ahi.z - alo.z) * 0.5f;
            // foot: the bottom layers that reach much further heel to toe than the shin
            var footSet = new HashSet<int>();
            if (splitFoot && !float.IsNaN(footBelowY))
            {
                for (int i = 0; i < boxes.Count; i++) if (boxes[i].c.y < footBelowY) footSet.Add(i);
            }
            else if (splitFoot)
            {
                var layers = new SortedDictionary<int, List<int>>();
                for (int i = 0; i < boxes.Count; i++)
                {
                    int key = Mathf.RoundToInt((L.inv * boxes[i].c).y / 0.01f);
                    if (!layers.TryGetValue(key, out var li)) layers[key] = li = new List<int>();
                    li.Add(i);
                }
                foreach (var kv in layers)
                {
                    float z0 = float.PositiveInfinity, z1 = float.NegativeInfinity;
                    foreach (int i in kv.Value) { Corners(boxes[i], cs); foreach (var w in cs) { float lz = (L.inv * w).z; z0 = Mathf.Min(z0, lz); z1 = Mathf.Max(z1, lz); } }
                    if (z1 - z0 <= 1.3f * 2f * L.hz) break;
                    foreach (int i in kv.Value) footSet.Add(i);
                }
            }
            for (int i = 0; i < boxes.Count; i++) (footSet.Contains(i) ? L.foot : L.main).Add(boxes[i]);
            Range(L.main, L.inv, out var mlo, out var mhi);
            L.top = mhi.y; L.bottom = mlo.y;
            if (L.foot.Count > 0) { Range(L.foot, L.inv, out var flo, out var fhi); L.footTop = fhi.y; L.footBottom = flo.y; }
            L.topRect = LayerRect(L.main, L.inv, L.top, true, L.ax, L.az);
            L.bottomRect = LayerRect(L.main, L.inv, L.bottom, false, L.ax, L.az);
            return L;
        }

        // ---------- mesh ----------

        static readonly int[][] Faces =
        {
            new[] { 1, 3, 7, 5 }, new[] { 0, 4, 6, 2 },   // +x, -x
            new[] { 2, 6, 7, 3 }, new[] { 0, 1, 5, 4 },   // +y, -y
            new[] { 4, 5, 7, 6 }, new[] { 0, 2, 3, 1 },   // +z, -z
        };

        static Vector3 Axis(int a) => a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward;
        static float Comp(Vector3 v, int a) => a == 0 ? v.x : a == 1 ? v.y : v.z;

        // Another cube of the same part right across this face (within the gap)?
        static bool HasNeighbour(Box b, int face, List<Box> group)
        {
            int ax = face / 2; float sign = (face & 1) == 0 ? 1f : -1f;
            Vector3 p = b.c + b.r * (Axis(ax) * (sign * (Comp(b.h, ax) + Gap * 0.5f)));
            float m = Gap * 0.6f;
            foreach (var o in group)
            {
                if ((o.c - b.c).sqrMagnitude < 1e-10f) continue;
                var q = Quaternion.Inverse(o.r) * (p - o.c);
                if (Mathf.Abs(q.x) <= o.h.x + m && Mathf.Abs(q.y) <= o.h.y + m && Mathf.Abs(q.z) <= o.h.z + m) return true;
            }
            return false;
        }

        static int WriteMesh(Piece p, string meshDir, out int vertCount, out Vector3 size)
        {
            vertCount = 0; size = Vector3.zero;
            string path = meshDir + "/Cubes_" + p.mesh + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null && meshDir != MeshDir)
            {
                // a new body's meshes are made the first time (the male ones must already exist: prefabs use them)
                if (!AssetDatabase.IsValidFolder(meshDir)) AssetDatabase.CreateFolder(MeshDir, meshDir.Substring(MeshDir.Length + 1));
                mesh = new Mesh { name = "Cubes_" + p.mesh };
                AssetDatabase.CreateAsset(mesh, path);
            }
            if (mesh == null) return -1;
            bool head = p.mesh == "Head";
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var t0 = new List<int>(); var t1 = new List<int>();
            var P = new Vector3[8]; var nb = new bool[6];
            foreach (var b in p.boxes)
            {
                for (int i = 0; i < 8; i++) P[i] = p.Map(b.c + b.r * Vector3.Scale(b.h, Sgn(i)));
                Vector3 cen = p.Map(b.c);
                for (int f = 0; f < 6; f++) nb[f] = HasNeighbour(b, f, p.group);
                for (int f = 0; f < 6; f++)
                {
                    var q = Faces[f];
                    Vector3 n = Vector3.Cross(P[q[1]] - P[q[0]], P[q[2]] - P[q[0]]).normalized;
                    Vector3 fc = (P[q[0]] + P[q[1]] + P[q[2]] + P[q[3]]) * 0.25f;
                    if (Vector3.Dot(n, fc - cen) < 0f) n = -n;
                    Quad(P[q[0]], P[q[1]], P[q[2]], P[q[3]], n, verts, norms, t0);
                    if (nb[f]) continue;
                    // Dark backing through the middle of the cube, out into the gaps where there are neighbours:
                    // looking into a gap shows it instead of the hollow inside of the part.
                    int ax = f / 2, ua = (ax + 1) % 3, wa = (ax + 2) % 3;
                    float Ext(int a, int sideFace) => nb[sideFace] ? Comp(b.h, a) + Gap * 0.6f : Comp(b.h, a) * 0.98f;
                    float up = Ext(ua, ua * 2), un = Ext(ua, ua * 2 + 1), wp = Ext(wa, wa * 2), wn = Ext(wa, wa * 2 + 1);
                    Vector3 U = Axis(ua), W = Axis(wa);
                    Quad(p.Map(b.c + b.r * (-U * un - W * wn)), p.Map(b.c + b.r * (U * up - W * wn)), p.Map(b.c + b.r * (U * up + W * wp)), p.Map(b.c + b.r * (-U * un + W * wp)), n, verts, norms, t1);
                }
            }
            // Dark blocks in the joints (show when the joint bends and opens a gap).
            foreach (var (c, h) in p.fillers)
            {
                for (int i = 0; i < 8; i++) P[i] = c + Vector3.Scale(h, Sgn(i));
                for (int f = 0; f < 6; f++)
                {
                    var q = Faces[f];
                    Quad(P[q[0]], P[q[1]], P[q[2]], P[q[3]], Axis(f / 2) * ((f & 1) == 0 ? 1f : -1f), verts, norms, t1);
                }
            }
            mesh.Clear();
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetNormals(norms);
            // The head keeps its third (eye) submesh, empty: its renderer still has three materials.
            mesh.subMeshCount = head ? 3 : 2;
            mesh.SetTriangles(t0, 0); mesh.SetTriangles(t1, 1);
            if (head) mesh.SetTriangles(new List<int>(), 2);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            vertCount = verts.Count; size = mesh.bounds.size;
            return p.boxes.Count;
        }

        static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, List<Vector3> v, List<Vector3> nm, List<int> t)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) { var tmp = b; b = d; d = tmp; }
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            for (int k = 0; k < 4; k++) nm.Add(n);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
        }

        // ---------- prefabs ----------

        struct Joints
        {
            public float hipsY, ubY, hipX, hipY, hipZ, kneeY, ankleY, shX, shY, shZ, elbowY, gripY, shPivot, chestTop, backZ;
        }

        static string ApplyJoints(string path, Joints j, float shift)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fig = root.transform.Find("Visual/CubeFigure");
                if (fig == null) return path + ": no CubeFigure";
                void Pos(string p, Vector3 v) { var t = fig.Find(p); if (t != null) t.localPosition = v; }
                Pos("Hips", new Vector3(0f, j.hipsY + shift, 0f));
                Pos("UpperBody", new Vector3(0f, j.ubY + shift, 0f));
                foreach (var (sd, sx) in new[] { ("_L", -1f), ("_R", 1f) })
                {
                    Pos("Hips/Hip" + sd, new Vector3(sx * j.hipX, j.hipY, j.hipZ));
                    Pos("Hips/Hip" + sd + "/Knee" + sd, new Vector3(0f, j.kneeY, 0f));
                    Pos("Hips/Hip" + sd + "/Knee" + sd + "/Ankle" + sd, new Vector3(0f, j.ankleY, 0f));
                    Pos("UpperBody/Shoulder" + sd, new Vector3(sx * j.shX, j.shY, j.shZ));
                    Pos("UpperBody/Shoulder" + sd + "/Elbow" + sd, new Vector3(0f, j.elbowY, 0f));
                }
                float hangLower = Mathf.Max(0f, OldHangLower - (OldShoulderY - (j.shPivot + shift)));
                var rig = fig.GetComponent<PlayerCubeRig>();
                if (rig != null)
                {
                    var so = new SerializedObject(rig);
                    so.FindProperty("restHips").vector3Value = new Vector3(0f, j.hipsY + shift, 0f);
                    so.FindProperty("restUpperBody").vector3Value = new Vector3(0f, j.ubY + shift, 0f);
                    var gp = so.FindProperty("gripOffset"); var g = gp.vector3Value; gp.vector3Value = new Vector3(0f, j.gripY, g.z);
                    so.FindProperty("hangLower").floatValue = hangLower;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                string extra = "";
                var back = root.transform.Find("Visual/BackMount");
                if (back != null && path.EndsWith("/Player.prefab"))
                {
                    var lp = back.localPosition;
                    lp.y = OldBackMountY - (OldChestTop - (j.chestTop + shift)); lp.z = j.backZ;
                    back.localPosition = lp;
                    extra = " back mount " + lp.ToString("F3");
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path + ": joints set, hang lower " + hangLower.ToString("F3") + extra;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Points the prefab's figure parts at the meshes in meshDir (Cubes_<part name>).
        static string AssignMeshes(string path, string meshDir)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fig = root.transform.Find("Visual/CubeFigure");
                if (fig == null) return path + ": no CubeFigure";
                int n = 0;
                foreach (var pp in PartPaths)
                {
                    var t = fig.Find(pp); if (t == null) continue;
                    var mf = t.GetComponent<MeshFilter>();
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshDir + "/Cubes_" + t.name + ".asset");
                    if (mf != null && mesh != null) { mf.sharedMesh = mesh; n++; }
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path + ": " + n + " part meshes from " + meshDir;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The build (as it now stands on cfg.prefabs[0]) becomes the player's second body: PlayerBody on Player.prefab.
        static string StorePlayerVariant(Config cfg, Joints j, float shift)
        {
            var v = new PlayerBody.Variant { name = "Female", meshes = new Mesh[PartPaths.Length], jointPositions = new Vector3[JointPaths.Length] };
            var src = PrefabUtility.LoadPrefabContents(cfg.prefabs[0]);
            try
            {
                var fig = src.transform.Find("Visual/CubeFigure");
                for (int i = 0; i < JointPaths.Length; i++)
                {
                    var t = fig.Find(JointPaths[i]);
                    if (t == null) return cfg.prefabs[0] + ": joint missing " + JointPaths[i];
                    v.jointPositions[i] = t.localPosition;
                }
                var so = new SerializedObject(fig.GetComponent<PlayerCubeRig>());
                v.restHips = so.FindProperty("restHips").vector3Value;
                v.restUpperBody = so.FindProperty("restUpperBody").vector3Value;
                v.gripOffset = so.FindProperty("gripOffset").vector3Value;
                v.hangLower = so.FindProperty("hangLower").floatValue;
                // The body walks as its cutscene stand-in is set to (its rig's walk values).
                v.gait = fig.GetComponent<PlayerCubeRig>().GetGait(); v.ownGait = true;
            }
            finally { PrefabUtility.UnloadPrefabContents(src); }
            for (int i = 0; i < PartPaths.Length; i++)
            {
                string name = PartPaths[i].Substring(PartPaths[i].LastIndexOf('/') + 1);
                v.meshes[i] = AssetDatabase.LoadAssetAtPath<Mesh>(cfg.meshDir + "/Cubes_" + name + ".asset");
                if (v.meshes[i] == null) return "mesh missing: " + cfg.meshDir + "/Cubes_" + name + ".asset";
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
            try
            {
                var fig = root.transform.Find("Visual/CubeFigure");
                if (fig == null) return PlayerPrefab + ": no CubeFigure";
                var back = root.transform.Find("Visual/BackMount");
                if (back != null) v.backMount = new Vector3(back.localPosition.x, OldBackMountY - (OldChestTop - (j.chestTop + shift)), j.backZ);
                var parts = new MeshFilter[PartPaths.Length]; var joints = new Transform[JointPaths.Length];
                for (int i = 0; i < parts.Length; i++) { var t = fig.Find(PartPaths[i]); parts[i] = t != null ? t.GetComponent<MeshFilter>() : null; }
                for (int i = 0; i < joints.Length; i++) joints[i] = fig.Find(JointPaths[i]);
                var pb = root.GetComponent<PlayerBody>();
                bool added = pb == null;
                if (added) pb = root.AddComponent<PlayerBody>();
                pb.EditorSetup(fig.GetComponent<PlayerCubeRig>(), parts, joints, back, new[] { v });
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
                return PlayerPrefab + ": PlayerBody " + (added ? "added" : "updated") + ", body 1 = " + v.name + ", rest hips " + v.restHips.ToString("F3")
                    + " upper body " + v.restUpperBody.ToString("F3") + " grip " + v.gripOffset.ToString("F3") + " hang lower " + v.hangLower.ToString("F3") + " back mount " + v.backMount.ToString("F3");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Lowest point of the figure in the rest pose (figure space).
        static float RestSole(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var fig = root.transform.Find("Visual/CubeFigure");
                fig.GetComponent<PlayerCubeRig>().PreviewRest();
                float lo = float.PositiveInfinity;
                foreach (var mf in fig.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    foreach (var v in mf.sharedMesh.vertices) lo = Mathf.Min(lo, fig.InverseTransformPoint(mf.transform.TransformPoint(v)).y);
                }
                return lo;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
