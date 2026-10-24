// Bundled line icons (src/assets/icons/*.svg, white strokes; colour variants baked at build time by
// tools/svg-tint-loader.js) plus resolution of icon strings coming from C# (layer/recipe/option icons):
// a coui:// or Media/ path is used as is, a short name ("park", "fountain") maps to a bundled icon,
// anything unknown gets a neutral glyph. Everything renders as a plain <img>: Gameface showed nothing
// for our previous CSS mask-image + currentColor icons (playtest 1).

declare const require: any;

/** Baked colour variants (tools/icon-tints.json). `light`, `dark` and `dim` exist for every icon. */
export type Tint = "light" | "dark" | "dim" | "accent" | "warn" | "danger" | "ok";

function load(ctx: any): Record<string, string> {
    const out: Record<string, string> = {};
    ctx.keys().forEach((k: string) => {
        const name = k.replace(/^\.\//, "").replace(/\.svg(\?.*)?$/, "");
        const mod = ctx(k);
        out[name] = typeof mod === "string" ? mod : mod?.default ?? "";
    });
    return out;
}

// One context per tint: webpack appends the query to every file, the tint loader bakes the colour in
// and files land in images/citymaker/<name>-<tint>.<hash>.svg (namespaced on the shared ui-mods host).
const TINTED: Record<Tint, Record<string, string>> = {
    light: load(require.context("../assets/icons?tint=light", false, /\.svg$/)),
    dark: load(require.context("../assets/icons?tint=dark", false, /\.svg$/)),
    dim: load(require.context("../assets/icons?tint=dim", false, /\.svg$/)),
    accent: load(require.context("../assets/icons?tint=accent", false, /(info|check|area|logo|marker|building|undo|edit|eye|entrance|twoWay|entry|exit|alley|footpath|plus|search|auto|grip)\.svg$/)),
    warn: load(require.context("../assets/icons?tint=warn", false, /(warning|info|marker|cost|star|entrance|twoWay|entry|exit|alley|footpath)\.svg$/)),
    danger: load(require.context("../assets/icons?tint=danger", false, /(error|warning|cost|trash|close)\.svg$/)),
    ok: load(require.context("../assets/icons?tint=ok", false, /(check|info)\.svg$/)),
};
const BUNDLED = TINTED.light;

/** Every bundled icon name. */
export const ICON_NAMES = Object.keys(BUNDLED);

const ALIASES: Record<string, string> = {
    "roads-with-parks": "roadsParks",
    roadswithparks: "roadsParks",
    "roads+parks": "roadsParks",
    "park-plaza": "parkPlaza",
    "park|plaza": "parkPlaza",
    parkplazasplit: "parkPlaza",
    camp: "campground",
    camping: "campground",
    toilets: "restrooms",
    restroom: "restrooms",
    trees: "tree",
    "big-trees": "bigTrees",
    bigtrees: "bigTrees",
    "edge-zoning": "edgeZoning",
    edgezoning: "edgeZoning",
    zoning: "edgeZoning",
    stalls: "car",
    entrances: "entrance",
    "two-way": "twoWay",
    twoway: "twoWay",
    both: "twoWay",
    "entry-only": "entry",
    entryonly: "entry",
    "exit-only": "exit",
    exitonly: "exit",
    junction: "alley",
    alleyway: "alley",
    pedestrian: "footpath",
    walkway: "footpath",
    benches: "bench",
    lamps: "lamp",
    paths: "path",
    pathways: "path",
    roadlength: "ruler",
    pathlength: "path",
    length: "ruler",
    centerpiece: "fountain",
    "park-with-playground": "playground",
    "split-park-plaza": "parkPlaza",
    flowerbeds: "flower",
    flowerbed: "flower",
    statue: "fountain",
};

/** Canonical bundled icon name for a short name / alias, or undefined. */
export function bundledName(name: string): string | undefined {
    if (!name) return undefined;
    if (BUNDLED[name]) return name;
    const lower = name.toLowerCase();
    const alias = ALIASES[lower];
    if (alias && BUNDLED[alias]) return alias;
    return Object.keys(BUNDLED).find((k) => k.toLowerCase() === lower);
}

export function bundledIcon(name: string, tint: Tint = "light"): string | undefined {
    const n = bundledName(name);
    if (!n) return undefined;
    return TINTED[tint]?.[n] ?? BUNDLED[n];
}

export interface ResolvedIcon {
    src: string;
    /** True for our own glyphs (colour baked per tint); false for game/asset icons (own colours). */
    bundled: boolean;
}

/**
 * Icon string → <img> src:
 *  - "coui://…", "http…", "data:", "assetdb:", "thumbnail:" → as is;
 *  - "Media/…" (vanilla game icons) → as is, RELATIVE, like vanilla does: the game UI resolves it against
 *    its own document. Never build "coui://GameUI/…" — hosts are lower-cased and coui://gameui is not a
 *    registered host ("Invalid host locations map" in Player.log, playtest 1);
 *  - a short name ("park", "entrance") → our bundled glyph in the requested tint.
 */
export function resolveIcon(name: string | undefined, fallback = "layers", tint: Tint = "light"): ResolvedIcon {
    if (name) {
        if (/^(coui:|https?:|data:|assetdb:|thumbnail:)/i.test(name)) return { src: name, bundled: false };
        if (/^\/?Media\//.test(name)) return { src: name.replace(/^\//, ""), bundled: false };
        const b = bundledIcon(name, tint);
        if (b) return { src: b, bundled: true };
    }
    return { src: bundledIcon(fallback, tint) ?? BUNDLED.layers ?? "", bundled: true };
}

/** URL of a bundled glyph (launcher, mock toolbar). */
export const icon = (name: string, tint: Tint = "light") => bundledIcon(name, tint) ?? "";

/** Nearest baked tint for a colour passed to <Icon color>. Unknown colours → light. */
export function tintFor(color?: string): Tint {
    if (!color) return "light";
    const map: Record<string, Tint> = {
        "#0b1d26": "dark", "#ffb547": "warn", "#ffa94d": "warn", "#ff6b5b": "danger", "#6fdc8c": "ok", "#8fd1ff": "accent", "#9fb0b9": "dim",
    };
    return map[color.toLowerCase()] ?? "light";
}

/**
 * Icon for a layer or recipe: our bundled role glyph when we have one for its id (keeps the type cards
 * consistent), otherwise whatever icon C# declared.
 */
export function entityIcon(id: string, declared?: string): string {
    if (bundledName(id)) return id;
    // variants ("parking-green") use their base type's glyph
    const base = (id || "").split(/[-_]/)[0];
    if (base && base !== id && bundledName(base)) return base;
    return declared || id;
}

/** Stat key → icon ("Citymaker.Stat.Trees" → tree). */
export function statIcon(key: string): string {
    const last = (key.split(".").pop() ?? "").toLowerCase();
    const map: Record<string, string> = {
        trees: "tree", tree: "tree", benches: "bench", bench: "bench", lamps: "lamp",
        stalls: "car", parking: "car", spaces: "car", roadlength: "ruler", roads: "roads", pathlength: "path",
        paths: "path", area: "area", greenarea: "leaf", pavedarea: "plaza", blocks: "block", entrances: "entrance",
        playgrounds: "playground", fountains: "fountain", restrooms: "restrooms", zoning: "zone", zonecells: "zone",
        capacity: "people", parts: "split", bins: "more", bushes: "leaf", flowerbeds: "flower", pitches: "campground", visitors: "people", tents: "campground", water: "water",
    };
    return map[last] ?? (bundledName(last) ? last : "info");
}

/** Role colour of a layer (matches the overlay: paths yellow, roads cyan, green spaces green, parking blue…). */
export function layerColor(id: string, kind?: string): string {
    const k = (id || "").toLowerCase();
    if (k.includes("parking")) return "#5f9dff";
    if (k.includes("plaza")) return "#e2b47e";
    if (k.includes("road") || k.includes("street")) return "#8fe3ff";
    if (k.includes("path")) return "#f5c542";
    if (k.includes("zon")) return "#b48cff";
    if (k.includes("water") || k.includes("lake") || k.includes("fountain")) return "#4fb8e8";
    if (k.includes("split")) return "#c9d3da";
    if (k.includes("park") || k.includes("tree") || k.includes("camp") || k.includes("green") || k.includes("garden")) return "#6cc56a";
    if (kind === "addOn") return "#ffa94d";
    if (kind === "split") return "#c9d3da";
    return "#8fd1ff";
}
