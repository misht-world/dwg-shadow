using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Clipper2Lib;
using DwgShadow.Core;

namespace DwgShadow.Cad
{
    /// <summary>Build scenario: read → compute → write. Runs in command context.</summary>
    public static class ShadowRunner
    {
        /// <summary>Series colours from morning to evening (ACI).</summary>
        static readonly short[] SeriesColors = { 5, 150, 4, 130, 3, 90, 2, 40, 30, 20, 1, 240, 6, 200 };

        class Input
        {
            public List<Contour> Contours;
            public List<Building> Buildings;
            public int Skipped;
            public int DefaultHeight;
        }

        /// <summary>Which buildings to build and which previous objects to remove.</summary>
        class Plan
        {
            public HashSet<ObjectId> Sources;
            /// <summary>Buildings the user asked for: they get the current height and sun.</summary>
            public HashSet<ObjectId> Picked;
            /// <summary>Parameters each building was last built with (from existing tags).</summary>
            public Dictionary<ObjectId, SourceRef> Stored = new Dictionary<ObjectId, SourceRef>();
            public int InitialCount;
            public HashSet<ObjectId> Delete = new HashSet<ObjectId>();
            public List<TaggedObject> Existing = new List<TaggedObject>();
            public bool Partial;
            public int Nested;
            public int Linked => Math.Max(0, Sources.Count - InitialCount);
            public int Kept;
        }

        static void Msg(Editor ed, string key, params object[] args) =>
            ed.WriteMessage("\n" + L.T("msg.prefix") + L.F(key, args));

        // ================================================================== single shadow

        public static void Build(Document doc, ShadowSettings s)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            string err = s.Validate(false);
            if (err != null) { ed.WriteMessage("\n" + L.T("msg.prefix") + err); return; }

            ShadowDirection dir = ShadowCalc.Direction(s);
            if (dir.BelowThreshold)
            {
                if (dir.AltitudeDeg <= 0) Msg(ed, "msg.belowh", dir.AltitudeDeg);
                else Msg(ed, "msg.low", dir.AltitudeDeg, s.MinAltitude);
                return;
            }

            Plan plan = PickSources(doc, s);
            if (plan == null) return;
            string target = s.OutputLayer.Trim();
            bool IsTarget(string l) => string.Equals(l, target, StringComparison.OrdinalIgnoreCase);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                PrepareRemoval(tr, db, s, plan, IsTarget);

                Input input = ReadInput(db, s, plan, true);
                if (input == null) { Msg(ed, "msg.noclosed"); return; }
                ShadowResult r = ShadowCalc.Compute(input.Buildings, dir, s.UnitsPerMeter, s.SubtractFootprints);

                // Picked buildings: a new shadow that runs into another building's shadow would leave
                // two overlapping objects. Pull those buildings in and rebuild them together.
                if (plan.Partial && s.ReplacePrevious)
                {
                    for (int iter = 0; iter < 5 && TakeOverlapping(plan, r.Union); iter++)
                    {
                        input = ReadInput(db, s, plan, true);
                        r = ShadowCalc.Compute(input.Buildings, dir, s.UnitsPerMeter, s.SubtractFootprints);
                    }
                }

                List<PolygonWithHoles> polys = ShadowGeometry.ToPolygons(r.Union, MinArea(s));
                List<List<int>> owners = ShadowCalc.Attribute(polys, r);

                var w = new CadWriter(db, tr);
                w.EnsureLayer(target, s.ColorIndex);
                int removed = w.Erase(plan.Delete);
                for (int i = 0; i < polys.Count; i++)
                    w.WritePolygon(polys[i], target, s.ColorIndex, s.DrawContour, s.DrawHatch, s, SourceRefs(input, owners[i], dir));
                if (s.HatchToBack) w.PlaceUnderBuildings(input.Contours.Select(c => c.Id));
                tr.Commit();

                Msg(ed, "msg.dir", dir.AltitudeDeg, dir.DirectionDeg, dir.LengthRatio);
                if (dir.SunAzimuthDeg.HasValue) ed.WriteMessage(" (" + L.F("msg.sunaz", dir.SunAzimuthDeg.Value).TrimEnd(',', ' ') + ")");
                Msg(ed, "msg.done", input.Buildings.Count, polys.Count, target);
                Report(ed, s, plan, input, r.Failed, w, removed);
            }
        }

        // ================================================================== series

        public static void BuildSeries(Document doc, ShadowSettings s)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            string err = s.Validate(true);
            if (err != null) { ed.WriteMessage("\n" + L.T("msg.prefix") + err); return; }

            List<TimeSpan> times = ShadowCalc.SeriesTimes(s);
            var steps = times.Select(t => (time: t, dir: ShadowCalc.Direction(s, t, forceAstronomical: true))).ToList();
            var usable = steps.Where(x => !x.dir.BelowThreshold).ToList();
            if (usable.Count == 0) { Msg(ed, "msg.s.none", s.MinAltitude); return; }

            Plan plan = PickSources(doc, s);
            if (plan == null) return;
            string prefix = s.SeriesLayerPrefix.Trim();
            string zoneLayer = prefix + "_" + L.T("def.zone");
            bool IsTarget(string l) => string.Equals(l, prefix, StringComparison.OrdinalIgnoreCase) ||
                                       l.StartsWith(prefix + "_", StringComparison.OrdinalIgnoreCase);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                PrepareRemoval(tr, db, s, plan, IsTarget);
                // In a series the sun moves, so neighbours keep only their previous height.
                Input input = ReadInput(db, s, plan, false);
                if (input == null) { Msg(ed, "msg.noclosed"); return; }

                var results = usable.Select(u => (u.time, u.dir, r: ShadowCalc.Compute(input.Buildings, u.dir, s.UnitsPerMeter, s.SubtractFootprints))).ToList();

                var w = new CadWriter(db, tr);
                int removed = w.Erase(plan.Delete);
                int total = 0;
                for (int i = 0; i < results.Count; i++)
                {
                    var (time, dir, r) = results[i];
                    short color = SeriesColors[i * SeriesColors.Length / Math.Max(1, results.Count) % SeriesColors.Length];
                    string layer = s.SeriesLayerPerTime ? $"{prefix}_{time:hh\\-mm}" : prefix;
                    w.EnsureLayer(layer, color);

                    List<PolygonWithHoles> polys = ShadowGeometry.ToPolygons(r.Union, MinArea(s));
                    List<List<int>> owners = ShadowCalc.Attribute(polys, r);
                    total += polys.Count;
                    for (int k = 0; k < polys.Count; k++)
                    {
                        List<SourceRef> src = SourceRefs(input, owners[k], dir);
                        w.WritePolygon(polys[k], layer, color, true, false, s, src);
                        if (s.SeriesLabels)
                        {
                            PointD tip = FarthestPoint(polys[k].Outer, dir.DirectionDeg);
                            w.AddText($"{time:hh\\:mm}", tip.x, tip.y, s.SeriesLabelHeight * s.UnitsPerMeter, layer, color, src);
                        }
                    }
                }

                if (s.SeriesEnvelope)
                {
                    w.EnsureLayer(zoneLayer, s.ColorIndex, s.OutputLayer);
                    var zone = new ShadowResult { Union = ShadowGeometry.UnionAll(results.Select(x => x.r.Union)) };
                    for (int b = 0; b < input.Buildings.Count; b++)
                        zone.PerBuilding.Add(ShadowGeometry.UnionAll(results.Select(x => x.r.PerBuilding[b])));
                    List<PolygonWithHoles> polys = ShadowGeometry.ToPolygons(zone.Union, MinArea(s));
                    List<List<int>> owners = ShadowCalc.Attribute(polys, zone);
                    for (int k = 0; k < polys.Count; k++)
                        w.WritePolygon(polys[k], zoneLayer, s.ColorIndex, true, s.DrawHatch, s, SourceRefs(input, owners[k], results[0].dir));
                }
                if (s.HatchToBack) w.PlaceUnderBuildings(input.Contours.Select(c => c.Id));
                tr.Commit();

                Msg(ed, "msg.s.done", s.Date, usable.Count, usable.First().time.ToString(@"hh\:mm"), usable.Last().time.ToString(@"hh\:mm"), total);
                int low = steps.Count - usable.Count;
                if (low > 0)
                    Msg(ed, "msg.s.low", s.MinAltitude, string.Join(", ", steps.Where(x => x.dir.BelowThreshold).Select(x => x.time.ToString(@"hh\:mm"))));
                Report(ed, s, plan, input, results.SelectMany(x => x.r.Failed).Distinct().ToList(), w, removed);
            }
        }

        // ================================================================== helpers

        /// <summary>Initial building contours: the whole layer or the user's selection (plus nested contours).</summary>
        static Plan PickSources(Document doc, ShadowSettings s)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            var plan = new Plan();
            if (s.SourceMode == SourceMode.Layer)
            {
                List<ObjectId> ids = CadReader.ContoursOnLayers(db, new[] { s.SourceLayer });
                if (ids.Count == 0) { Msg(ed, "msg.nolayer", s.SourceLayer); return null; }
                plan.Sources = new HashSet<ObjectId>(ids);
            }
            else
            {
                List<ObjectId> ids = CadReader.SelectContours(ed);
                // Shadows drawn by DwgShadow are closed polylines too — never treat them as buildings.
                if (ids != null) ids = CadReader.WithoutShadowObjects(db, ids);
                if (ids == null || ids.Count == 0) { Msg(ed, "msg.noselect"); return null; }
                List<Contour> picked = CadReader.ReadContours(db, ids, out _);
                if (picked.Count == 0) { Msg(ed, "msg.noclosed"); return null; }
                plan.Nested = CadReader.AddNested(db, picked);
                plan.Sources = new HashSet<ObjectId>(picked.Select(c => c.Id));
                plan.Partial = true;
            }
            plan.InitialCount = plan.Sources.Count;
            plan.Picked = new HashSet<ObjectId>(plan.Sources);
            return plan;
        }

        /// <summary>
        /// Decides what to remove. Whole layer: every object on the target layers. Picked buildings:
        /// only objects built from them; a merged shadow that also belongs to other buildings pulls
        /// those buildings into the rebuild so nothing is lost.
        /// </summary>
        static void PrepareRemoval(Transaction tr, Database db, ShadowSettings s, Plan plan, Func<string, bool> isTarget)
        {
            if (!s.ReplacePrevious) return;
            plan.Existing = ShadowTags.Scan(tr, db, isTarget);
            foreach (TaggedObject t in plan.Existing)
                if (t.Sources != null)
                    foreach (SourceRef r in t.Sources)
                        if (r.HasParams) plan.Stored[r.Id] = r;
            if (!plan.Partial)
            {
                foreach (TaggedObject t in plan.Existing)
                {
                    plan.Delete.Add(t.Id);
                    if (t.Sources != null) AddSources(plan, t.Sources);
                }
                return;
            }
            Close(plan);
        }

        /// <summary>Removes every tagged object that shares a source with the plan, until stable.</summary>
        static void Close(Plan plan)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (TaggedObject t in plan.Existing)
                {
                    if (t.Sources == null || plan.Delete.Contains(t.Id)) continue;
                    if (!t.Sources.Any(r => plan.Sources.Contains(r.Id))) continue;
                    plan.Delete.Add(t.Id);
                    if (AddSources(plan, t.Sources)) changed = true;
                }
            }
        }

        static bool AddSources(Plan plan, IEnumerable<SourceRef> refs)
        {
            bool added = false;
            foreach (SourceRef r in refs)
                if (!r.Id.IsErased && CadReader.IsContourClass(r.Id) && plan.Sources.Add(r.Id)) added = true;
            return added;
        }

        /// <summary>Takes in tagged shadows of other buildings that overlap the new shadow. True if anything was added.</summary>
        static bool TakeOverlapping(Plan plan, PathsD shadow)
        {
            if (shadow.Count == 0) return false;
            RectD box = Clipper.GetBounds(shadow);
            bool added = false;
            foreach (TaggedObject t in plan.Existing)
            {
                if (t.Sources == null || t.Outline == null || plan.Delete.Contains(t.Id)) continue;
                if (!ShadowCalc.Intersects(box, ShadowGeometry.Bounds(t.Outline))) continue;
                if (ShadowGeometry.OverlapArea(new PathsD { t.Outline }, shadow) <= 1e-9) continue;
                plan.Delete.Add(t.Id);
                if (AddSources(plan, t.Sources)) added = true;
            }
            if (added) Close(plan);
            return added;
        }

        /// <param name="keepSun">Neighbours pulled into the rebuild also keep their previous sun.</param>
        static Input ReadInput(Database db, ShadowSettings s, Plan plan, bool keepSun)
        {
            List<Contour> contours = CadReader.ReadContours(db, plan.Sources.ToList(), out int skipped);
            if (contours.Count == 0) return null;
            var paths = contours.Select(c => c.Path).ToList();

            Dictionary<int, double> labelValues = null;
            if (s.HeightMode == HeightMode.FromText)
                labelValues = CadReader.AssignLabels(paths, CadReader.ReadLabels(db, s.TextLayer));

            List<Building> buildings = BuildingGrouper.Group(paths, s.Courtyards,
                labelValues == null ? (Func<int, bool>)null : i => labelValues.ContainsKey(i));

            int defaults = 0;
            plan.Kept = 0;
            foreach (Building b in buildings)
            {
                // A neighbour that was not picked is rebuilt exactly as before — its own height and sun.
                ObjectId id = contours[b.SourceIndex].Id;
                if (!plan.Picked.Contains(id) && plan.Stored.TryGetValue(id, out SourceRef prev) && prev.HasParams)
                {
                    b.HeightM = prev.HeightM;
                    if (keepSun) b.DirectionOverride = new ShadowDirection { DirectionDeg = prev.DirectionDeg, AltitudeDeg = prev.AltitudeDeg };
                    plan.Kept++;
                    continue;
                }
                if (labelValues != null && labelValues.TryGetValue(b.SourceIndex, out double v))
                    b.HeightM = s.TextMeaning == TextMeaning.Floors ? v * s.FloorHeight : v;
                else
                {
                    b.HeightM = s.FixedHeightM;
                    if (labelValues != null) defaults++;
                }
            }
            return new Input { Contours = contours, Buildings = buildings, Skipped = skipped, DefaultHeight = defaults };
        }

        /// <summary>Source contours (outline + courtyards) of the given buildings with the parameters used.</summary>
        static List<SourceRef> SourceRefs(Input input, IEnumerable<int> buildingIndices, ShadowDirection dir)
        {
            var refs = new List<SourceRef>();
            foreach (int i in buildingIndices)
            {
                Building b = input.Buildings[i];
                ShadowDirection d = b.DirectionOverride ?? dir;
                refs.Add(SourceRef.Of(input.Contours[b.SourceIndex].Id, b.HeightM, d));
                refs.AddRange(b.HoleIndices.Select(h => SourceRef.Of(input.Contours[h].Id, b.HeightM, d)));
            }
            return refs;
        }

        static double MinArea(ShadowSettings s) => 1e-4 * s.UnitsPerMeter * s.UnitsPerMeter; // 1 cm²

        static PointD FarthestPoint(PathD path, double dirDeg)
        {
            double a = dirDeg * Math.PI / 180.0, cx = Math.Cos(a), cy = Math.Sin(a);
            PointD best = path[0];
            double bestV = double.MinValue;
            foreach (PointD p in path)
            {
                double v = p.x * cx + p.y * cy;
                if (v > bestV) { bestV = v; best = p; }
            }
            return best;
        }

        static void Report(Editor ed, ShadowSettings s, Plan plan, Input input, List<int> failed, CadWriter w, int removed)
        {
            if (plan.Nested > 0) Msg(ed, "msg.nested", plan.Nested);
            if (plan.Partial && plan.Linked > 0) Msg(ed, "msg.linked", plan.Linked);
            if (plan.Partial && plan.Kept > 0) Msg(ed, "msg.kept", plan.Kept);
            if (removed > 0) Msg(ed, "msg.removed", removed);
            if (input.Skipped > 0) Msg(ed, "msg.skipped", input.Skipped);
            if (input.DefaultHeight > 0) Msg(ed, "msg.default", input.DefaultHeight, s.FixedHeightM);
            if (failed.Count > 0) Msg(ed, "msg.failed", failed.Count);
            if (w.HatchFailures > 0) Msg(ed, "msg.hatchfail", w.HatchFailures);
            ed.WriteMessage("\n");
        }
    }
}
