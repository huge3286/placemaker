// Plausible stand-in plans for layers the mock host doesn't have (yet). Mirrors Core.Composer's
// recursion (targets, per-region overrides, disabled steps, reserved space) so mix-and-match recipes
// behave like the real thing; the geometry itself is deliberately simple.
import { Issue, SettingValue, Stat } from "../../src/contract";
import {
    add, area, bbox, centroid, clipConvex, clipHalfPlane, closestOnRing, dist, distToRing, distToSeg, hash, inside, lerp, mul, perimeter, rng,
    rotate, shrink, sub, V2,
} from "./geometry";
import { ComposeResult, PlanLayer, PlanNet, PlanObject, PlanRegion, StepModel } from "./types";

interface Reserved {
    discs: { c: V2; r: number }[];
    corridors: { a: V2; b: V2; hw: number }[];
}

interface Input {
    ring: V2[];
    tags: string[];
    v: Record<string, SettingValue>;
    seed: number;
    markers: { kind: string; p: V2 }[];
    reserved: Reserved;
}

type Out = Omit<PlanLayer, "layer" | "stepPath">;

const num = (v: Record<string, SettingValue>, k: string, d: number) => (typeof v[k] === "number" ? (v[k] as number) : d);
const bool = (v: Record<string, SettingValue>, k: string, d: boolean) => (typeof v[k] === "boolean" ? (v[k] as boolean) : d);
const str = (v: Record<string, SettingValue>, k: string, d: string) => (typeof v[k] === "string" ? (v[k] as string) : d);
const list = (v: Record<string, SettingValue>, k: string, d: string[]) => (Array.isArray(v[k]) ? (v[k] as string[]) : d);

const empty = (): Out => ({ regions: [], surfaces: [], areas: [], nets: [], objects: [], zoning: [], terraform: [], issues: [], stats: [] });

function straight(a: V2, b: V2, role: string, width: number): PlanNet {
    return { role, pts: [a, lerp(a, b, 1 / 3), lerp(a, b, 2 / 3), b], width };
}

function curved(a: V2, b: V2, bend: number, role: string, width: number): PlanNet {
    const d = sub(b, a);
    const n: V2 = [-d[1], d[0]];
    const k = bend / (Math.hypot(n[0], n[1]) || 1);
    return { role, pts: [a, add(lerp(a, b, 1 / 3), mul(n, k)), add(lerp(a, b, 2 / 3), mul(n, -k * 0.6)), b], width };
}

function bez(n: PlanNet, t: number): V2 {
    const [a, b, c, d] = n.pts;
    const u = 1 - t;
    return [
        u * u * u * a[0] + 3 * u * u * t * b[0] + 3 * u * t * t * c[0] + t * t * t * d[0],
        u * u * u * a[1] + 3 * u * u * t * b[1] + 3 * u * t * t * c[1] + t * t * t * d[1],
    ];
}

export function netLength(n: PlanNet) {
    let l = 0;
    let prev = n.pts[0];
    for (let i = 1; i <= 8; i++) {
        const p = bez(n, i / 8);
        l += dist(prev, p);
        prev = p;
    }
    return l;
}

function blocked(p: V2, r: number, res: Reserved, nets: PlanNet[] = [], objs: PlanObject[] = []) {
    for (const d of res.discs) if (dist(p, d.c) < r + d.r) return true;
    for (const c of res.corridors) if (distToSeg(p, c.a, c.b) < r + c.hw) return true;
    for (const n of nets) for (let i = 0; i < 4; i++) if (distToSeg(p, bez(n, i / 4), bez(n, (i + 1) / 4)) < r + n.width / 2) return true;
    for (const o of objs) if (dist(p, o.p) < r + o.r) return true;
    return false;
}

function reserve(out: Out, res: Reserved) {
    for (const n of out.nets) for (let i = 0; i < 4; i++) res.corridors.push({ a: bez(n, i / 4), b: bez(n, (i + 1) / 4), hw: n.width / 2 });
    for (const o of out.objects) res.discs.push({ c: o.p, r: o.r });
}

/** Spiral search for a free spot of radius r, starting at the centre. */
function findSpot(ring: V2[], r: number, res: Reserved, out: Out, start?: V2): V2 | null {
    const c = start ?? centroid(ring);
    for (let i = 0; i < 400; i++) {
        const ang = i * 2.399963;
        const rad = Math.sqrt(i) * 4;
        const p: V2 = [c[0] + Math.cos(ang) * rad, c[1] + Math.sin(ang) * rad];
        if (inside(p, ring) && distToRing(p, ring) > r + 1 && !blocked(p, r, res, out.nets, out.objects)) return p;
    }
    return null;
}

function circle(c: V2, r: number, n = 20): V2[] {
    const pts: V2[] = [];
    for (let i = 0; i < n; i++) pts.push([c[0] + Math.cos((i / n) * Math.PI * 2) * r, c[1] + Math.sin((i / n) * Math.PI * 2) * r]);
    return pts;
}

/** Longest straight run of a line through `c` with direction `dir`, inside `ring`. */
function chord(ring: V2[], c: V2, dir: V2): [V2, V2] | null {
    const walk = (s: number) => {
        let t = 0;
        while (t < 2000 && inside(add(c, mul(dir, (t + 1) * s)), ring)) t += 1;
        return add(c, mul(dir, t * s));
    };
    if (!inside(c, ring)) return null;
    const a = walk(-1), b = walk(1);
    return dist(a, b) > 4 ? [a, b] : null;
}

function ringNets(ring: V2[], role: string, width: number): PlanNet[] {
    const nets: PlanNet[] = [];
    for (let i = 0; i < ring.length; i++) {
        const a = ring[i], b = ring[(i + 1) % ring.length];
        if (dist(a, b) > 1) nets.push(straight(a, b, role, width));
    }
    return nets;
}

function alongRing(ring: V2[], spacing: number, fn: (p: V2, dir: V2, i: number) => void) {
    const per = perimeter(ring);
    const n = Math.floor(per / spacing);
    let segStart = 0, seg = 0;
    for (let k = 0; k < n; k++) {
        const s = (k + 0.5) * spacing;
        while (seg < ring.length) {
            const a = ring[seg], b = ring[(seg + 1) % ring.length];
            const l = dist(a, b);
            if (s <= segStart + l) {
                const t = l > 0 ? (s - segStart) / l : 0;
                fn(lerp(a, b, t), mul(sub(b, a), 1 / (l || 1)), k);
                break;
            }
            segStart += l;
            seg++;
        }
    }
}

// ───────────── layers ─────────────

const LAYERS: Record<string, (i: Input) => Out> = {
    park(i) {
        const o = empty();
        const A = area(i.ring);
        const c = centroid(i.ring);
        const R = rng(i.seed);
        const pw = num(i.v, "pathWidth", 4);
        const style = str(i.v, "style", "natural");
        o.surfaces.push({ role: "Grass", ring: i.ring });
        const loop = shrink(i.ring, Math.max(6, pw + 3));
        if (area(loop) > 200) {
            if (style === "natural") {
                for (let k = 0; k < loop.length; k++) o.nets.push(curved(loop[k], loop[(k + 1) % loop.length], 0.08, "Pathway", pw));
            } else o.nets.push(...ringNets(loop, "Pathway", pw));
        }
        const ang = style === "formal" ? 0 : 0.35;
        const d1: V2 = [Math.cos(ang), Math.sin(ang)], d2: V2 = [-Math.sin(ang), Math.cos(ang)];
        const cross: [V2, V2][] = [];
        if (style !== "meadow") {
            for (const d of [d1, d2]) {
                const ch = chord(loop, c, d);
                if (ch) {
                    cross.push(ch);
                    o.nets.push(style === "natural" ? curved(ch[0], ch[1], 0.12, "Pathway", pw) : straight(ch[0], ch[1], "PathwayWide", pw + 1));
                }
            }
        }
        for (const m of i.markers.filter((m) => m.kind === "entrance")) {
            const q = closestOnRing(m.p, loop);
            o.nets.push(straight(m.p, q.q, "Pathway", pw));
        }
        if (bool(i.v, "fence", false)) o.nets.push(...ringNets(i.ring, "Fence", 0.6));
        const regionsCut = cross.length === 2;
        if (regionsCut) {
            let n = 0;
            for (const s1 of [1, -1])
                for (const s2 of [1, -1]) {
                    let q = clipHalfPlane(i.ring, c, add(c, mul(d1, s1)));
                    q = clipHalfPlane(q, c, add(c, mul(d2, s2)));
                    if (q.length >= 3 && area(q) > 50) o.regions.push({ key: "green:" + n++, tags: ["green"], ring: q, areaM2: area(q) });
                }
        } else o.regions.push({ key: "green:0", tags: ["green"], ring: i.ring, areaM2: A });
        o.regions.push({ key: "core", tags: ["core"], ring: circle(c, Math.min(14, Math.sqrt(A) / 6), 12), areaM2: Math.PI * 196 });
        let lake: V2[] | null = null;
        if (bool(i.v, "lake", false)) {
            const r = Math.sqrt((A * num(i.v, "lakeSize", 20)) / 100 / Math.PI);
            const lc = add(c, [r * 0.6, r * 0.4]);
            lake = circle(lc, r, 24).map((p, k) => add(lc, mul(sub(p, lc), 1 + 0.12 * Math.sin(k * 1.7))));
            o.surfaces.push({ role: "Water", ring: lake });
            o.terraform.push({ kind: "Lower", permanent: true, ring: lake });
        }
        if (bool(i.v, "furniture", true)) {
            alongRing(loop, 26, (p, dir, k) => {
                const n: V2 = [-dir[1], dir[0]];
                const q = add(p, mul(n, pw / 2 + 1.5));
                if (inside(q, i.ring)) o.objects.push({ role: k % 2 ? "Lamp" : "Bench", p: q, yaw: Math.atan2(dir[0], dir[1]), r: 0.8 });
            });
        }
        const want = Math.round((A / 140) * (num(i.v, "trees", 60) / 100));
        let skipped = 0;
        for (let k = 0; k < want * 3 && o.objects.filter((x) => x.role === "Tree").length < want; k++) {
            const b = bbox(i.ring);
            const p: V2 = [b.x0 + R() * b.w, b.y0 + R() * b.h];
            if (!inside(p, i.ring) || distToRing(p, i.ring) < 2.5) continue;
            if (lake && inside(p, lake)) continue;
            if (blocked(p, 2.5, i.reserved, o.nets, o.objects)) {
                skipped++;
                continue;
            }
            o.objects.push({ role: "Tree", p, yaw: R() * 6.28, r: 2.5 });
        }
        const trees = o.objects.filter((x) => x.role === "Tree").length;
        if (skipped > 8) o.issues.push({ severity: "info", text: { key: "Citymaker.Issue.TreesSkipped", args: [skipped] } });
        o.stats = [
            { key: "Citymaker.Stat.Trees", value: trees, unit: "count" },
            { key: "Citymaker.Stat.Benches", value: o.objects.filter((x) => x.role === "Bench").length, unit: "count" },
            { key: "Citymaker.Stat.PathLength", value: Math.round(o.nets.filter((n) => n.role.startsWith("Pathway")).reduce((s, n) => s + netLength(n), 0)), unit: "m" },
        ];
        return o;
    },

    plaza(i) {
        const o = empty();
        const A = area(i.ring);
        const c = centroid(i.ring);
        o.surfaces.push({ role: "Plaza", ring: i.ring });
        const pattern = str(i.v, "pattern", "radial");
        if (pattern === "radial") o.surfaces.push({ role: "Decorative", ring: circle(c, Math.min(18, Math.sqrt(A) / 4), 28) });
        if (pattern === "grid") o.surfaces.push({ role: "Decorative", ring: shrink(i.ring, Math.sqrt(A) / 5) });
        if (pattern !== "open") o.objects.push({ role: "Centerpiece", p: c, yaw: 0, r: 6 });
        const seq = list(i.v, "arrangement", ["bench-wood", "lamp-classic"]);
        const dens = num(i.v, "furniture", 50);
        if (dens > 0 && seq.length) {
            const inner = shrink(i.ring, 4);
            alongRing(inner, 4 + (100 - dens) / 6, (p, dir, k) => {
                const id = seq[k % seq.length];
                const role = id.startsWith("lamp") ? "Lamp" : id.startsWith("planter") ? "Planter" : id.startsWith("bin") ? "Furniture" : "Bench";
                o.objects.push({ role, p, yaw: Math.atan2(dir[0], dir[1]), r: role === "Planter" ? 1.2 : 0.8 });
            });
        }
        if (bool(i.v, "edgeTrees", true)) alongRing(shrink(i.ring, 1.8), 11, (p) => o.objects.push({ role: "Tree", p, yaw: 0, r: 2.5 }));
        o.regions.push({ key: "paved", tags: ["paved"], ring: i.ring, areaM2: A });
        o.regions.push({ key: "core", tags: ["core"], ring: circle(c, 16, 14), areaM2: Math.PI * 256 });
        o.regions.push({ key: "edge", tags: ["edge"], ring: i.ring, areaM2: A });
        if (A > 30000) o.issues.push({ severity: "warning", text: { key: "Citymaker.Issue.PlazaLarge" }, at: [c[0], 0, c[1]] });
        o.stats = [
            { key: "Citymaker.Stat.Benches", value: o.objects.filter((x) => x.role === "Bench").length, unit: "count" },
            { key: "Citymaker.Stat.Lamps", value: o.objects.filter((x) => x.role === "Lamp").length, unit: "count" },
            { key: "Citymaker.Stat.PavedArea", value: Math.round(A), unit: "m2" },
        ];
        return o;
    },

    parking(i) {
        const o = empty();
        const A = area(i.ring);
        if (A < 600) {
            o.issues.push({ severity: "error", text: { key: "Citymaker.Parking.TooSmall" } });
            return o;
        }
        o.surfaces.push({ role: "Asphalt", ring: i.ring });
        let best = 0, theta = 0;
        for (let k = 0; k < i.ring.length; k++) {
            const a = i.ring[k], b = i.ring[(k + 1) % i.ring.length];
            if (dist(a, b) > best) {
                best = dist(a, b);
                theta = Math.atan2(b[1] - a[1], b[0] - a[0]);
            }
        }
        const c = centroid(i.ring);
        const aisle = num(i.v, "aisle", 6);
        const spacing = { "90": 2.6, "60": 3.0, "45": 3.7 }[str(i.v, "angle", "90")] ?? 2.6;
        const inner = shrink(i.ring, 2.5);
        const dir: V2 = [Math.cos(theta), Math.sin(theta)], nrm: V2 = [-dir[1], dir[0]];
        const pitch = aisle + 10.5;
        const b = bbox(i.ring);
        const reach = Math.hypot(b.w, b.h);
        let stalls = 0;
        for (let k = -Math.ceil(reach / pitch); k <= Math.ceil(reach / pitch); k++) {
            const lc = add(c, mul(nrm, k * pitch));
            const ch = chord(inner, lc, dir);
            if (!ch || dist(ch[0], ch[1]) < 12) continue;
            o.nets.push(straight(ch[0], ch[1], "DriveAisle", aisle));
            const L = dist(ch[0], ch[1]);
            for (let s = 3; s < L - 3; s += spacing)
                for (const side of [1, -1]) {
                    const p = add(add(ch[0], mul(dir, s)), mul(nrm, side * (aisle / 2 + 2.5)));
                    if (inside(p, i.ring) && distToRing(p, i.ring) > 1.2) {
                        o.objects.push({ role: "ParkingBay", p, yaw: theta, r: 1.2 });
                        stalls++;
                    }
                }
            if (bool(i.v, "lanterns", true)) for (let s = 10; s < L; s += 30) o.objects.push({ role: "Lamp", p: add(ch[0], mul(dir, s)), yaw: 0, r: 0.5 });
        }
        for (const m of i.markers) {
            let bestP: V2 | null = null, bd = Infinity;
            for (const n of o.nets) {
                for (let t = 0; t <= 1; t += 0.05) {
                    const p = bez(n, t);
                    if (dist(p, m.p) < bd) { bd = dist(p, m.p); bestP = p; }
                }
            }
            if (bestP) o.nets.push(straight(m.p, bestP, "DriveConnector", aisle));
        }
        if (bool(i.v, "greenery", true)) alongRing(shrink(i.ring, 1.2), 14, (p) => o.objects.push({ role: "Tree", p, yaw: 0, r: 2 }));
        o.regions.push({ key: "lot", tags: ["lot"], ring: i.ring, areaM2: A });
        o.regions.push({ key: "edge", tags: ["edge"], ring: i.ring, areaM2: A });
        o.stats = [
            { key: "Citymaker.Stat.Stalls", value: stalls, unit: "count" },
            { key: "Citymaker.Stat.Chargers", value: num(i.v, "chargers", 0), unit: "count" },
        ];
        return o;
    },

    roads(i) {
        const o = empty();
        const c = centroid(i.ring);
        const B = num(i.v, "blockSize", 90);
        const theta = (num(i.v, "angle", 0) * Math.PI) / 180;
        const per = bool(i.v, "perimeter", true);
        const street = 12;
        if (per) o.nets.push(...ringNets(i.ring, "PerimeterRoad", street));
        const inner = shrink(i.ring, per ? street / 2 : 0);
        const b = bbox(i.ring);
        const reach = Math.hypot(b.w, b.h) / 2 + B;
        const n = Math.ceil(reach / B);
        const dx: V2 = [Math.cos(theta), Math.sin(theta)], dz: V2 = [-dx[1], dx[0]];
        for (let k = -n; k <= n; k++) {
            for (const [d, nn] of [[dx, dz], [dz, dx]] as [V2, V2][]) {
                const lc = add(c, mul(nn, k * B + B / 2));
                const ch = chord(inner, lc, d);
                if (ch && dist(ch[0], ch[1]) > B * 0.4) o.nets.push(straight(ch[0], ch[1], "Street", street));
            }
        }
        let idx = 0, tiny = 0;
        const half = street / 2 + 1;
        for (let x = -n; x < n; x++)
            for (let z = -n; z < n; z++) {
                const corner = (u: number, v: number) => rotate([c[0] + u, c[1] + v], c, theta);
                const x0 = x * B + B / 2 + half, x1 = (x + 1) * B + B / 2 - half;
                const z0 = z * B + B / 2 + half, z1 = (z + 1) * B + B / 2 - half;
                const cell = [corner(x0, z0), corner(x1, z0), corner(x1, z1), corner(x0, z1)];
                const q = clipConvex(shrink(i.ring, per ? street / 2 + 1 : 1), cell);
                if (q.length < 3) continue;
                const A = area(q);
                if (A < 350) {
                    if (A > 60) tiny++;
                    continue;
                }
                o.regions.push({ key: "block:" + idx++, tags: ["block"], ring: q, areaM2: A });
            }
        if (tiny) o.issues.push({ severity: "info", text: { key: "Citymaker.Issue.BlockTooSmall", args: [tiny] } });
        o.regions.push({ key: "edge", tags: ["edge"], ring: i.ring, areaM2: area(i.ring) });
        o.stats = [
            { key: "Citymaker.Stat.RoadLength", value: Math.round(o.nets.reduce((s, x) => s + netLength(x), 0)), unit: "m" },
            { key: "Citymaker.Stat.Blocks", value: idx, unit: "count" },
        ];
        return o;
    },

    split(i) {
        const o = empty();
        const b = bbox(i.ring);
        const r = num(i.v, "ratio", 50) / 100;
        const vertical = str(i.v, "direction", "vertical") === "vertical";
        const gap = bool(i.v, "divider", true) ? 2 : 0;
        let p0: V2[], p1: V2[], a: V2, e: V2;
        if (vertical) {
            const X = b.x0 + b.w * r;
            p0 = clipHalfPlane(i.ring, [X - gap, 0], [X - gap, 1]);
            p1 = clipHalfPlane(i.ring, [X + gap, 1], [X + gap, 0]);
            a = [X, b.y0 - 1];
            e = [X, b.y1 + 1];
        } else {
            const Z = b.y0 + b.h * r;
            p0 = clipHalfPlane(i.ring, [1, Z - gap], [0, Z - gap]);
            p1 = clipHalfPlane(i.ring, [0, Z + gap], [1, Z + gap]);
            a = [b.x0 - 1, Z];
            e = [b.x1 + 1, Z];
        }
        if (p0.length >= 3) o.regions.push({ key: "part:0", tags: ["part"], ring: p0, areaM2: area(p0) });
        if (p1.length >= 3) o.regions.push({ key: "part:1", tags: ["part"], ring: p1, areaM2: area(p1) });
        if (gap) {
            const ch = chord(i.ring, lerp(a, e, 0.5), vertical ? [0, 1] : [1, 0]);
            if (ch) o.nets.push(straight(ch[0], ch[1], "Pathway", 3));
        }
        return o;
    },

    campground(i) {
        const o = empty();
        o.surfaces.push({ role: "Grass", ring: i.ring });
        const loop = shrink(i.ring, 14);
        if (area(loop) > 100) o.nets.push(...ringNets(loop, "Pathway", 3));
        const want = num(i.v, "pitches", 14);
        let tents = 0;
        alongRing(loop, Math.max(8, perimeter(loop) / want), (p, dir, k) => {
            const n: V2 = [-dir[1], dir[0]];
            const q = add(p, mul(n, k % 2 ? 6 : -6));
            if (inside(q, i.ring) && tents < want) {
                o.objects.push({ role: "Furniture", p: q, yaw: 0, r: 3 });
                tents++;
                if (bool(i.v, "campfires", true) && k % 3 === 0) o.objects.push({ role: "Marker", p: add(q, mul(dir, 4)), yaw: 0, r: 0.8 });
            }
        });
        o.regions.push({ key: "green:0", tags: ["green"], ring: i.ring, areaM2: area(i.ring) });
        o.stats = [{ key: "Citymaker.Stat.Tents", value: tents, unit: "count" }];
        return o;
    },

    playground(i) {
        const o = empty();
        const r = { s: 6, m: 9, l: 12 }[str(i.v, "size", "m")] ?? 9;
        const p = findSpot(i.ring, r, i.reserved, o);
        if (!p) {
            o.issues.push({ severity: "warning", text: { key: "Citymaker.Issue.NoRoom", args: ["Playground"] } });
            return o;
        }
        o.surfaces.push({ role: "Decorative", ring: circle(p, r, 16) });
        o.objects.push({ role: "Furniture", p, yaw: 0, r });
        o.stats = [{ key: "Citymaker.Stat.Playgrounds", value: 1, unit: "count" }];
        return o;
    },

    fountain(i) {
        const o = empty();
        const p = findSpot(i.ring, 5, i.reserved, o);
        if (!p) {
            o.issues.push({ severity: "warning", text: { key: "Citymaker.Issue.NoRoom", args: ["Fountain"] } });
            return o;
        }
        o.objects.push({ role: "Centerpiece", p, yaw: 0, r: 5 });
        o.stats = [{ key: "Citymaker.Stat.Fountains", value: 1, unit: "count" }];
        return o;
    },

    restrooms(i) {
        const o = empty();
        const n = num(i.v, "count", 1);
        const b = bbox(i.ring);
        for (let k = 0; k < n; k++) {
            const start: V2 = [b.x0 + (b.w * (k + 1)) / (n + 1), b.y0 + b.h * 0.3];
            const p = findSpot(i.ring, 4, i.reserved, o, start);
            if (p) o.objects.push({ role: "Furniture", p, yaw: 0, r: 4 });
        }
        o.stats = [{ key: "Citymaker.Stat.Restrooms", value: o.objects.length, unit: "count" }];
        return o;
    },

    bigTrees(i) {
        const o = empty();
        const R = rng(i.seed);
        const want = num(i.v, "count", 8);
        const b = bbox(i.ring);
        for (let k = 0; k < want * 20 && o.objects.length < want; k++) {
            const p: V2 = [b.x0 + R() * b.w, b.y0 + R() * b.h];
            if (inside(p, i.ring) && distToRing(p, i.ring) > 4 && !blocked(p, 4.5, i.reserved, o.nets, o.objects)) o.objects.push({ role: "Tree", p, yaw: 0, r: 4.5 });
        }
        o.stats = [{ key: "Citymaker.Stat.Trees", value: o.objects.length, unit: "count" }];
        return o;
    },

    edgeZoning(i) {
        const o = empty();
        const depth = num(i.v, "depth", 4) * 8;
        let cells = 0;
        const ccw = area(i.ring) === 0 ? i.ring : i.ring;
        const sign = signed(ccw) > 0 ? 1 : -1;
        for (let k = 0; k < ccw.length; k++) {
            const a = ccw[k], b = ccw[(k + 1) % ccw.length];
            const L = dist(a, b);
            if (L < 16) continue;
            const d = mul(sub(b, a), 1 / L);
            const n: V2 = mul([-d[1], d[0]], sign);
            const a2 = add(a, mul(d, 4)), b2 = add(b, mul(d, -4));
            o.zoning.push({ zone: str(i.v, "zone", "mixed"), quad: [a2, b2, add(b2, mul(n, depth)), add(a2, mul(n, depth))] });
            cells += Math.floor((L - 8) / 8) * (depth / 8);
        }
        o.stats = [{ key: "Citymaker.Stat.ZoneCells", value: cells, unit: "count" }];
        return o;
    },

    pond(i) {
        const o = empty();
        const A = area(i.ring);
        const r = Math.sqrt((A * num(i.v, "size", 15)) / 100 / Math.PI);
        const p = findSpot(i.ring, r, i.reserved, o);
        if (!p) {
            o.issues.push({ severity: "warning", text: { key: "Citymaker.Issue.NoRoom", args: ["Pond"] } });
            return o;
        }
        const lake = circle(p, r, 24);
        o.surfaces.push({ role: "Water", ring: lake });
        o.terraform.push({ kind: "Lower", permanent: true, ring: lake });
        return o;
    },

    kiosk(i) {
        const o = empty();
        const n = num(i.v, "count", 1);
        for (let k = 0; k < n; k++) {
            const p = findSpot(i.ring, 3, i.reserved, o);
            if (p) o.objects.push({ role: "Furniture", p, yaw: 0, r: 3 });
        }
        return o;
    },

    picnic(i) {
        const o = empty();
        const want = num(i.v, "tables", 6);
        for (let k = 0; k < want; k++) {
            const p = findSpot(i.ring, 2, i.reserved, o);
            if (p) o.objects.push({ role: "Bench", p, yaw: 0, r: 2 });
        }
        o.stats = [{ key: "Citymaker.Stat.Benches", value: o.objects.length, unit: "count" }];
        return o;
    },
};

function signed(ring: V2[]) {
    let s = 0;
    for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) s += ring[j][0] * ring[i][1] - ring[i][0] * ring[j][1];
    return s;
}

// ───────────── pricing (same stand-in as the mock host) ─────────────

const OBJ_COST: Record<string, number> = { Tree: 150, Bush: 40, Centerpiece: 4500, Bench: 90, Lamp: 120, Furniture: 600, Planter: 70 };
const NET_COST: Record<string, number> = { Avenue: 220, Street: 120, PerimeterRoad: 120, Roundabout: 120, CulDeSac: 120, Pathway: 18, PathwayWide: 30, Fence: 12 };

export function fakePrice(l: Out) {
    let c = 0;
    for (const o of l.objects) c += OBJ_COST[o.role] ?? 60;
    for (const n of l.nets) c += netLength(n) * (NET_COST[n.role] ?? 60);
    for (const s of l.surfaces) c += area(s.ring) * (s.role === "Grass" ? 0.5 : 3);
    return { cost: c, upkeep: c * 0.012 };
}

// ───────────── composer ─────────────

export function composeFake(root: StepModel, ring: V2[]): ComposeResult {
    const t0 = performance.now();
    const result: ComposeResult = { layers: [], issues: [], stats: [], perLayer: [], cost: 0, upkeep: 0, permanent: [], ms: 0, source: "fake" };
    const reserved: Reserved = { discs: [], corridors: [] };
    let budget = 512;

    const run = (step: StepModel, shape: V2[], tags: string[], regionKey: string, path: string, depth: number) => {
        if (!step.enabled) return;
        if (depth > 6 || --budget < 0) {
            result.issues.push({ severity: "error", text: { key: "Citymaker.Issue.RecipeTooDeep" } });
            return;
        }
        const fn = LAYERS[step.layer];
        if (!fn) {
            result.issues.push({ severity: "error", text: { key: "Citymaker.Issue.UnknownLayer", args: [step.layer] } });
            return;
        }
        const markers = step.markers.filter((m) => closestOnRing(m.p, shape).d <= 1.5);
        const out = fn({
            ring: shape, tags, v: step.values, seed: step.seed ^ hash(path + "|" + regionKey), markers,
            reserved: { discs: reserved.discs.slice(), corridors: reserved.corridors.slice() },
        });
        const layer: PlanLayer = { ...out, layer: step.layer, stepPath: path + "@" + regionKey, issues: out.issues.map((x) => ({ ...x, stepPath: path })) };
        result.layers.push(layer);
        reserve(out, reserved);
        const price = fakePrice(out);
        result.cost += price.cost;
        result.upkeep += price.upkeep;
        result.perLayer.push({ stepPath: path, layer: step.layer, regionKey, stats: out.stats ?? [], cost: Math.round(price.cost), upkeep: Math.round(price.upkeep) });
        step.children.forEach((child, ci) => {
            const childPath = path + "/" + ci;
            if (!child.target) {
                run(child, shape, tags, regionKey, childPath, depth + 1);
                return;
            }
            for (const region of out.regions) {
                if (child.target !== region.key && !region.tags.includes(child.target)) continue;
                const eff = step.overrides[region.key] ?? child;
                run(eff, region.ring, region.tags, region.key, childPath, depth + 1);
            }
        });
    };
    run(root, ring, ["root"], "root", "0", 0);

    const order: string[] = [];
    const sums = new Map<string, Stat>();
    for (const l of result.layers)
        for (const s of l.stats ?? []) {
            const k = s.key + "|" + s.unit;
            if (!sums.has(k)) {
                sums.set(k, { ...s, value: 0 });
                order.push(k);
            }
            sums.get(k)!.value += s.value;
        }
    result.stats = order.map((k) => sums.get(k)!);
    for (const l of result.layers) for (const t of l.terraform) if (t.permanent) result.permanent.push({ key: "Citymaker.Permanent.Terraform" });
    result.cost = Math.round(result.cost);
    result.upkeep = Math.round(result.upkeep);
    result.ms = performance.now() - t0;
    return result;
}

export const FAKE_LAYER_IDS = Object.keys(LAYERS);
export type { PlanRegion, Issue };
