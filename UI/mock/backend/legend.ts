// s22 mock: preview layers of a plan (mirrors C# Core/Build/PreviewLegend for the fake planner; with the mock host
// every element carries its real layer in `lg` and the host sends the legend computed by Core).
import { LegendEntry } from "../../src/contract";
import { ComposeResult, PlanLayer } from "./types";
import { V2 } from "./geometry";

export const ORDER = [
    "paths", "road:street", "road:avenue", "road:local", "road:roundabout", "road:perimeter", "road:carFree",
    "features", "trees", "planting", "water", "surfaces", "objects", "parking", "entrances", "terrain", "cutouts", "blocked", "decals",
];

const COLORS: Record<string, string> = {
    paths: "#ffd173", trees: "#45b84f", planting: "#8fd65e", water: "#4a8cf2", surfaces: "#d9cdb4", objects: "#b8a6ff",
    features: "#ff9a40", parking: "#6699ff", terrain: "#ff8c33", cutouts: "#4d8cff", blocked: "#d9443a", decals: "#bfa680",
    entrances: "#ffd633", "road:street": "#f2f2f2", "road:avenue": "#59d9ff", "road:local": "#8fe07a", "road:roundabout": "#c58cff",
    "road:perimeter": "#ffa64d", "road:carFree": "#ffd173",
};

export const defaultVisible = (id: string) => id !== "blocked" && id !== "decals";

export function legendColor(id: string) {
    return COLORS[id] ?? "#cccccc";
}

const ROAD_ROLE: Record<string, string> = { Street: "street", Avenue: "avenue", CulDeSac: "local", Roundabout: "roundabout", PerimeterRoad: "perimeter" };

export function netLayer(role: string, layer: string): string {
    if (role === "Pathway" || role === "PathwayWide") return layer === "roads" ? "road:carFree" : "paths";
    if (role === "DriveAisle" || role === "DriveConnector" || role === "Alley") return "parking";
    if (role === "Fence") return "objects";
    return ROAD_ROLE[role] ? "road:" + ROAD_ROLE[role] : "paths";
}

export function objectLayer(role: string): string {
    switch (role) {
        case "Tree": return "trees";
        case "Bush": case "Planter": return "planting";
        case "Decal": return "decals";
        case "Centerpiece": return "features";
        case "ParkingBay": case "Charger": case "Arrow": return "parking";
        case "Marker": case "AccessPoint": case "AnimalSpawner": return "entrances";
        case "Furniture": return "features";
        default: return "objects";
    }
}

export const surfaceLayer = (role: string) => (role === "Water" ? "water" : "surfaces");

function area(r: V2[]) {
    let a = 0;
    for (let i = 0; i < r.length; i++) {
        const p = r[i], q = r[(i + 1) % r.length];
        a += p[0] * q[1] - q[0] * p[1];
    }
    return Math.abs(a) / 2;
}

function bezierLength(pts: V2[]) {
    let len = 0;
    let prev = pts[0];
    for (let i = 1; i <= 12; i++) {
        const t = i / 12, u = 1 - t;
        const p: V2 = [
            u * u * u * pts[0][0] + 3 * u * u * t * pts[1][0] + 3 * u * t * t * pts[2][0] + t * t * t * pts[3][0],
            u * u * u * pts[0][1] + 3 * u * u * t * pts[1][1] + 3 * u * t * t * pts[2][1] + t * t * t * pts[3][1],
        ];
        len += Math.hypot(p[0] - prev[0], p[1] - prev[1]);
        prev = p;
    }
    return len;
}

/** Legend of a fake plan (the host sends Core's). */
export function fakeLegend(plan: ComposeResult, entrances: number): LegendEntry[] {
    const by = new Map<string, LegendEntry>();
    const e = (id: string) => {
        let x = by.get(id);
        if (!x) {
            x = {
                id, label: id.startsWith("road:") ? "Citymaker.Roads.Role." + id.slice(5) : "Citymaker.Legend." + id, color: legendColor(id), count: 0,
                defaultOff: defaultVisible(id) ? undefined : true, group: id.startsWith("road:") ? "roads" : undefined,
                color2: id === "terrain" ? "#4da6ff" : undefined,
            };
            by.set(id, x);
        }
        return x;
    };
    for (const l of plan.layers) {
        for (const n of l.nets) {
            const x = e(n.lg ?? netLayer(n.role, l.layer));
            x.count++;
            x.lengthM = (x.lengthM ?? 0) + bezierLength(n.pts);
        }
        for (const o of l.objects) e(o.lg ?? objectLayer(o.role)).count++;
        for (const s of l.surfaces) {
            const x = e(s.lg ?? surfaceLayer(s.role));
            x.count++;
            x.areaM2 = (x.areaM2 ?? 0) + area(s.ring);
        }
        for (const z of l.zoning ?? []) {
            const x = e("cutouts");
            x.count++;
            x.areaM2 = (x.areaM2 ?? 0) + area(z.quad);
        }
        for (const t of l.terraform ?? []) e("terrain").count++;
    }
    if (entrances > 0) e("entrances").count = entrances;
    const out: LegendEntry[] = [];
    for (const id of ORDER) {
        const x = by.get(id);
        if (!x) continue;
        if (x.lengthM) x.lengthM = Math.round(x.lengthM);
        if (x.areaM2) x.areaM2 = Math.round(x.areaM2);
        out.push(x);
    }
    return out;
}

/** Layer of a plan element as the mock map draws it. */
export const layerOfNet = (n: PlanLayer["nets"][number], l: PlanLayer) => n.lg ?? netLayer(n.role, l.layer);
export const layerOfObject = (o: PlanLayer["objects"][number]) => o.lg ?? objectLayer(o.role);
export const layerOfSurface = (s: PlanLayer["surfaces"][number]) => s.lg ?? surfaceLayer(s.role);
