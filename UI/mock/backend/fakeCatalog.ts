// Fallback catalog for the mock: plausible layers covering every control kind, used whenever the
// mock host has no real layer with that id (other workstreams write the real ones in parallel).
// Shapes follow contract.ts plus the proposed `default` on controls and preset `values`.
import { Control, LayerType, RecipeInfo, SettingValue } from "../../src/contract";

export type MockControl = Control & { default?: SettingValue };
export type MockLayer = Omit<LayerType, "controls" | "presets"> & {
    controls: MockControl[];
    presets: { id: string; name: string; icon?: string; values?: Record<string, SettingValue> }[];
    minArea?: number;
    /** Typed marker kinds (StepView.markerKinds) and how many markers Build needs (StepView.markersRequired). */
    markerKinds?: { kind: string; label: string; icon?: string; tooltip?: string; crosswalk?: boolean }[];
    markersRequired?: number;
    real?: boolean;
    /** Built-in features (the mock host sends Core's IHasFeatures; fake layers use FAKE_FEATURES). */
    features?: FeatureDef[];
};

const opt = (value: string, label: string, icon?: string, tooltip?: string) => ({ value, label, icon, tooltip });

export const FAKE_LAYERS: MockLayer[] = [
    {
        id: "park", name: "Citymaker.Park.Name", icon: "park", kind: "fill", accepts: [], emits: ["green", "core"],
        controls: [
            { id: "style", kind: "choice", tier: "essential", group: "", label: "Citymaker.Park.Style", tooltip: "Citymaker.Park.StyleTip",
              options: [opt("formal", "Citymaker.Park.Style.Formal", "plaza"), opt("natural", "Citymaker.Park.Style.Natural", "leaf"), opt("meadow", "Citymaker.Park.Style.Meadow", "tree")], default: "natural" },
            { id: "pathWidth", kind: "slider", tier: "essential", group: "", label: "Citymaker.Park.PathWidth", min: 2, max: 8, step: 0.5, unit: "m", default: 4, cheap: false },
            { id: "trees", kind: "slider", tier: "essential", group: "", label: "Citymaker.Park.TreeDensity", tooltip: "Citymaker.Park.TreeDensityTip", min: 0, max: 100, step: 5, unit: "%", default: 60, cheap: true },
            { id: "furniture", kind: "toggle", tier: "essential", group: "", label: "Citymaker.Park.Furniture", default: true, cheap: true },
            { id: "pathAsset", kind: "asset", tier: "more", group: "paths", label: "Citymaker.Park.PathAsset", assetCategory: "net.pathway", default: "" },
            { id: "treeSpecies", kind: "assetMulti", tier: "more", group: "planting", label: "Citymaker.Park.TreeSpecies", assetCategory: "tree", default: [] },
            { id: "entrances", kind: "markers", tier: "more", group: "paths", label: "Citymaker.Park.Entrances", markerKinds: ["entrance"] },
            { id: "lake", kind: "toggle", tier: "more", group: "water", label: "Citymaker.Park.Lake", tooltip: "Citymaker.Park.LakeTip", default: false },
            { id: "lakeSize", kind: "slider", tier: "more", group: "water", label: "Citymaker.Park.LakeSize", min: 10, max: 40, step: 5, unit: "%", default: 20, visibleWhen: "lake=true" },
            { id: "fence", kind: "toggle", tier: "more", group: "edge", label: "Citymaker.Park.Fence", default: false },
            { id: "plazaShape", kind: "choice", tier: "more", group: "plaza", label: "Citymaker.Park.PlazaShape",
              options: [opt("circle", "Citymaker.Shape.Circle"), opt("square", "Citymaker.Shape.Square"), opt("rectangle", "Citymaker.Shape.Rect")], default: "circle" },
            { id: "plazaAt", kind: "choice", tier: "more", group: "plaza", label: "Citymaker.Park.PlazaAt",
              options: [opt("centre", "Citymaker.At.Centre"), opt("corner", "Citymaker.At.Corner"), opt("edge", "Citymaker.At.Edge"), opt("entrance", "Citymaker.At.Entrance")], default: "centre" },
            { id: "plazaSize", kind: "slider", tier: "more", group: "plaza", label: "Citymaker.Park.PlazaSize", min: 5, max: 40, step: 1, unit: "%", default: 15 },
            { id: "fenceAsset", kind: "asset", tier: "more", group: "edge", label: "Citymaker.Park.FenceAsset", assetCategory: "fence", default: "", visibleWhen: "fence=true" },
            { id: "ground", kind: "asset", tier: "more", group: "ground", label: "Citymaker.Park.Ground", tooltip: "Citymaker.Park.GroundTip", assetCategory: "surface.grass", default: "" },
            { id: "decals", kind: "assetMulti", tier: "more", group: "ground", label: "Citymaker.Decals.Pick", tooltip: "Citymaker.Decals.PickTip", assetCategory: "decal", default: [] },
            { id: "decalDensity", kind: "slider", tier: "more", group: "ground", label: "Citymaker.Decals.Density", min: 0, max: 100, step: 5, unit: "%", default: 30 },
            { id: "decalClustering", kind: "slider", tier: "more", group: "ground", label: "Citymaker.Decals.Clustering", tooltip: "Citymaker.Decals.ClusteringTip", min: 0, max: 100, step: 5, unit: "%", default: 50 },
        ],
        presets: [
            { id: "formal", name: "Citymaker.Park.Preset.Formal", values: { style: "formal", pathWidth: 5, trees: 40, furniture: true } },
            { id: "wild", name: "Citymaker.Park.Preset.Wild", values: { style: "meadow", pathWidth: 2.5, trees: 85, furniture: false } },
            { id: "pond", name: "Citymaker.Park.Preset.Pond", values: { style: "natural", trees: 55, lake: true, lakeSize: 25 } },
        ],
        minArea: 400,
    },
    {
        id: "plaza", name: "Citymaker.Plaza.Name", icon: "plaza", kind: "fill", accepts: [], emits: ["paved", "core", "edge"],
        controls: [
            { id: "pattern", kind: "choice", tier: "essential", group: "", label: "Citymaker.Plaza.Pattern",
              options: [opt("grid", "Citymaker.Plaza.Pattern.Grid", "block"), opt("radial", "Citymaker.Plaza.Pattern.Radial", "fountain"), opt("open", "Citymaker.Plaza.Pattern.Open", "area")], default: "radial" },
            { id: "centerpiece", kind: "asset", tier: "essential", group: "", label: "Citymaker.Plaza.Centerpiece", assetCategory: "centerpiece", default: "" },
            { id: "furniture", kind: "slider", tier: "essential", group: "", label: "Citymaker.Plaza.Furniture", min: 0, max: 100, step: 10, unit: "%", default: 50 },
            { id: "surface", kind: "asset", tier: "more", group: "", label: "Citymaker.Plaza.Surface", assetCategory: "surface.paving", default: "" },
            { id: "arrangement", kind: "arrangement", tier: "more", group: "", label: "Citymaker.Plaza.Arrangement", assetCategory: "bench", default: ["bench-wood", "lamp-classic", "bench-wood", "planter-square"] },
            { id: "edgeTrees", kind: "toggle", tier: "more", group: "", label: "Citymaker.Plaza.EdgeTrees", default: true },
            { id: "decals", kind: "assetMulti", tier: "more", group: "decals", label: "Citymaker.Decals.Pick", tooltip: "Citymaker.Decals.PickTip", assetCategory: "decal", default: [] },
            { id: "decalDensity", kind: "slider", tier: "more", group: "decals", label: "Citymaker.Decals.Density", min: 0, max: 100, step: 5, unit: "%", default: 30 },
            { id: "decalClustering", kind: "slider", tier: "more", group: "decals", label: "Citymaker.Decals.Clustering", tooltip: "Citymaker.Decals.ClusteringTip", min: 0, max: 100, step: 5, unit: "%", default: 50 },
        ],
        presets: [
            { id: "market", name: "Citymaker.Plaza.Preset.Market", values: { pattern: "open", furniture: 30 } },
            { id: "civic", name: "Citymaker.Plaza.Preset.Civic", values: { pattern: "radial", furniture: 60, edgeTrees: true } },
        ],
        minArea: 200,
    },
    {
        id: "parking", name: "Citymaker.Parking.Name", icon: "parking", kind: "fill", accepts: [], emits: ["lot", "edge"],
        controls: [
            { id: "angle", kind: "choice", tier: "essential", group: "", label: "Citymaker.Parking.StallAngle",
              options: [opt("90", "Citymaker.Parking.Angle90"), opt("60", "Citymaker.Parking.Angle60"), opt("45", "Citymaker.Parking.Angle45")], default: "90" },
            { id: "entrances", kind: "markers", tier: "essential", group: "", label: "Citymaker.Parking.Entrances", markerKinds: ["entrance", "entry", "exit", "alley", "footpath"] },
            { id: "surface", kind: "asset", tier: "essential", group: "", label: "Citymaker.Parking.Surface", assetCategory: "surface.asphalt", default: "" },
            { id: "greenery", kind: "toggle", tier: "essential", group: "", label: "Citymaker.Parking.Greenery", default: true },
            { id: "aisle", kind: "slider", tier: "more", group: "layout", label: "Citymaker.Parking.AisleWidth", min: 5, max: 8, step: 0.5, unit: "m", default: 6 },
            { id: "medians", kind: "toggle", tier: "more", group: "layout", label: "Citymaker.Parking.Medians", default: false },
            { id: "lanterns", kind: "toggle", tier: "more", group: "extras", label: "Citymaker.Parking.Lanterns", default: true },
            { id: "perimeterRoad", kind: "toggle", tier: "more", group: "layout", label: "Citymaker.Parking.PerimeterRoad", default: false },
            { id: "zoningEdges", kind: "toggle", tier: "more", group: "zoning", label: "Citymaker.Parking.ZoningEdges", tooltip: "Citymaker.Parking.ZoningEdgesTip", default: false },
            { id: "zone", kind: "choice", tier: "more", group: "zoning", label: "Citymaker.EdgeZoning.Zone", visibleWhen: "zoningEdges=true",
              options: [opt("resLow", "Citymaker.Zone.ResidentialLow"), opt("commercial", "Citymaker.Zone.Commercial"), opt("mixed", "Citymaker.Zone.Mixed")], default: "commercial" },
            { id: "chargers", kind: "slider", tier: "more", group: "extras", label: "Citymaker.Parking.Chargers", min: 0, max: 20, step: 1, unit: "count", default: 0 },
            { id: "fee", kind: "slider", tier: "more", group: "extras", label: "Citymaker.Parking.Fee", min: 0, max: 20, step: 1, unit: "money", default: 5 },
        ],
        presets: [],
        minArea: 600,
        markerKinds: [
            { kind: "entrance", label: "Citymaker.Marker.TwoWay" },
            { kind: "entry", label: "Citymaker.Marker.Entry" },
            { kind: "exit", label: "Citymaker.Marker.Exit" },
            { kind: "alley", label: "Citymaker.Marker.Alley" },
            { kind: "footpath", label: "Citymaker.Marker.Footpath", crosswalk: true },
        ],
        markersRequired: 1,
    },
    {
        id: "roads", name: "Citymaker.Roads.Name", icon: "roads", kind: "fill", accepts: [], emits: ["block", "edge"],
        controls: [
            { id: "pattern", kind: "choice", tier: "essential", group: "", label: "Citymaker.Roads.Pattern",
              options: [opt("grid", "Citymaker.Roads.Pattern.Grid", "roads"), opt("loop", "Citymaker.Roads.Pattern.Loop", "perimeter"), opt("organic", "Citymaker.Roads.Pattern.Organic", "path")], default: "grid" },
            { id: "blockSize", kind: "slider", tier: "essential", group: "", label: "Citymaker.Roads.BlockSize", min: 50, max: 200, step: 5, unit: "m", default: 90 },
            { id: "street", kind: "asset", tier: "essential", group: "", label: "Citymaker.Roads.Street", assetCategory: "net.road", default: "" },
            { id: "angle", kind: "slider", tier: "more", group: "", label: "Citymaker.Roads.Angle", min: -45, max: 45, step: 1, unit: "deg", default: 0 },
            { id: "perimeter", kind: "toggle", tier: "more", group: "", label: "Citymaker.Roads.Perimeter", default: true },
            { id: "culDeSacs", kind: "toggle", tier: "more", group: "", label: "Citymaker.Roads.CulDeSacs", default: false },
        ],
        presets: [
            { id: "dense", name: "Citymaker.Roads.Preset.Dense", values: { blockSize: 70 } },
            { id: "suburb", name: "Citymaker.Roads.Preset.Suburb", values: { blockSize: 140, culDeSacs: true } },
        ],
        minArea: 4000,
    },
    {
        id: "split", hidden: true, name: "Citymaker.Split.Name", icon: "split", kind: "split", accepts: [], emits: ["part"],
        controls: [
            { id: "ratio", kind: "slider", tier: "essential", group: "", label: "Citymaker.Split.Ratio", min: 20, max: 80, step: 5, unit: "%", default: 50 },
            { id: "direction", kind: "choice", tier: "essential", group: "", label: "Citymaker.Split.Direction",
              options: [opt("vertical", "Citymaker.Split.Vertical"), opt("horizontal", "Citymaker.Split.Horizontal")], default: "vertical" },
            { id: "divider", kind: "toggle", tier: "more", group: "", label: "Citymaker.Split.Divider", default: true },
        ],
        presets: [],
    },
    {
        id: "campground", name: "Citymaker.Campground.Name", icon: "campground", kind: "fill", accepts: [], emits: ["green"],
        controls: [
            { id: "pitches", kind: "slider", tier: "essential", group: "", label: "Citymaker.Campground.Pitches", min: 4, max: 40, step: 1, unit: "count", default: 14 },
            { id: "campfires", kind: "toggle", tier: "essential", group: "", label: "Citymaker.Campground.Campfires", default: true },
        ],
        presets: [],
        minArea: 1500,
    },
    {
        id: "playground", name: "Citymaker.Playground.Name", icon: "playground", kind: "addOn", accepts: ["green", "core", "root", "block", "part", "paved"], emits: [],
        controls: [
            { id: "size", kind: "choice", tier: "essential", group: "", label: "Citymaker.Playground.Size",
              options: [opt("s", "Citymaker.Size.Small"), opt("m", "Citymaker.Size.Medium"), opt("l", "Citymaker.Size.Large")], default: "m" },
            { id: "equipment", kind: "assetMulti", tier: "more", group: "", label: "Citymaker.Playground.Equipment", assetCategory: "playground", default: [] },
        ],
        presets: [],
    },
    {
        id: "fountain", name: "Citymaker.Fountain.Name", icon: "fountain", kind: "addOn", accepts: ["paved", "core", "green"], emits: [],
        controls: [{ id: "asset", kind: "asset", tier: "essential", group: "", label: "Citymaker.Fountain.Asset", assetCategory: "centerpiece", default: "" }],
        presets: [],
    },
    {
        id: "restrooms", name: "Citymaker.Restrooms.Name", icon: "restrooms", kind: "addOn", accepts: ["green", "paved", "core"], emits: [],
        controls: [{ id: "count", kind: "slider", tier: "essential", group: "", label: "Citymaker.Restrooms.Count", min: 1, max: 4, step: 1, unit: "count", default: 1 }],
        presets: [],
    },
    {
        id: "bigTrees", name: "Citymaker.BigTrees.Name", icon: "bigTrees", kind: "addOn", accepts: ["green", "edge", "root", "block", "core"], emits: [],
        controls: [
            { id: "count", kind: "slider", tier: "essential", group: "", label: "Citymaker.BigTrees.Count", min: 1, max: 30, step: 1, unit: "count", default: 8 },
            { id: "species", kind: "assetMulti", tier: "essential", group: "", label: "Citymaker.BigTrees.Species", assetCategory: "tree", default: [] },
        ],
        presets: [],
    },
    {
        id: "edgeZoning", name: "Citymaker.EdgeZoning.Name", icon: "edgeZoning", kind: "addOn", accepts: ["edge", "block", "root", "lot", "paved"], emits: [],
        controls: [
            { id: "zone", kind: "choice", tier: "essential", group: "", label: "Citymaker.EdgeZoning.Zone",
              options: [opt("resLow", "Citymaker.Zone.ResidentialLow"), opt("resHigh", "Citymaker.Zone.ResidentialHigh"), opt("commercial", "Citymaker.Zone.Commercial"), opt("mixed", "Citymaker.Zone.Mixed"), opt("office", "Citymaker.Zone.Office"), opt("industry", "Citymaker.Zone.Industry")], default: "mixed" },
            { id: "depth", kind: "slider", tier: "essential", group: "", label: "Citymaker.EdgeZoning.Depth", min: 1, max: 6, step: 1, unit: "count", default: 4 },
        ],
        presets: [],
    },
    {
        id: "pond", hidden: true, name: "Citymaker.Pond.Name", icon: "water", kind: "addOn", accepts: ["green"], emits: [],
        controls: [
            { id: "size", kind: "slider", tier: "essential", group: "", label: "Citymaker.Pond.Size", tooltip: "Citymaker.Pond.SizeTip", min: 5, max: 40, step: 5, unit: "%", default: 15 },
            { id: "shore", kind: "choice", tier: "more", group: "", label: "Citymaker.Pond.Shore",
              options: [opt("grass", "Citymaker.Pond.Shore.Grass"), opt("stone", "Citymaker.Pond.Shore.Stone")], default: "grass" },
        ],
        presets: [],
    },
    {
        id: "kiosk", name: "Citymaker.Kiosk.Name", icon: "building", kind: "addOn", accepts: ["paved", "core"], emits: [],
        controls: [{ id: "count", kind: "slider", tier: "essential", group: "", label: "Citymaker.Kiosk.Count", min: 1, max: 4, step: 1, unit: "count", default: 1 }],
        presets: [],
    },
    {
        id: "picnic", name: "Citymaker.Picnic.Name", icon: "picnic", kind: "addOn", accepts: ["green"], emits: [],
        controls: [{ id: "tables", kind: "slider", tier: "essential", group: "", label: "Citymaker.Picnic.Tables", min: 2, max: 16, step: 1, unit: "count", default: 6 }],
        presets: [],
    },
];

export interface MockRecipe {
    info: RecipeInfo;
    root: RecipeTemplate;
}

export interface RecipeTemplate {
    layer: string;
    target?: string;
    values?: Record<string, SettingValue>;
    children?: RecipeTemplate[];
    overrides?: Record<string, RecipeTemplate>;
}

/** Built-in features of fill layers (mirrors Core.FeatureInfo): a toggle that adds a child step. */
export interface FeatureDef {
    layer: string;
    target?: string;
    defaultOn?: boolean;
    labelKey?: string;
    tooltip?: string;
    /** Ids of the PARENT's controls that belong to the feature (shown under its toggle). */
    options?: string[];
    preset?: Record<string, SettingValue>;
}

/** Playtest 2 decision: refine each type with features instead of free mixing. */
export const FAKE_FEATURES: Record<string, FeatureDef[]> = {
    park: [
        { layer: "plaza", target: "core", labelKey: "Citymaker.Feature.CentralPlaza", tooltip: "Citymaker.Feature.CentralPlazaTip", options: ["plazaShape", "plazaAt", "plazaSize"], preset: { pattern: "radial", furniture: 40 } },
        { layer: "playground" },
        { layer: "pond", labelKey: "Citymaker.Feature.Pond", tooltip: "Citymaker.Pond.SizeTip" },
        { layer: "picnic" },
        { layer: "restrooms" },
        { layer: "fountain" },
        { layer: "bigTrees" },
    ],
    plaza: [
        { layer: "bigTrees", labelKey: "Citymaker.Feature.Trees", defaultOn: true },
        { layer: "playground" },
        { layer: "fountain" },
        { layer: "kiosk" },
    ],
    roads: [{ layer: "edgeZoning", target: "edge", labelKey: "Citymaker.Feature.StreetZoning" }],
};

export const FAKE_RECIPES: MockRecipe[] = [
    { info: { id: "parking", name: "Citymaker.Parking.Name", icon: "parking", custom: false, description: "Citymaker.Recipe.ParkingTip", group: "parking" }, root: { layer: "parking" } },
    { info: { id: "plaza", name: "Citymaker.Plaza.Name", icon: "plaza", custom: false, description: "Citymaker.Recipe.PlazaTip", group: "squares" }, root: { layer: "plaza" } },
    { info: { id: "park", name: "Citymaker.Park.Name", icon: "park", custom: false, description: "Citymaker.Recipe.ParkTip", group: "parks" }, root: { layer: "park" } },
    { info: { id: "roads", name: "Citymaker.Roads.Name", icon: "roads", custom: false, description: "Citymaker.Recipe.RoadsTip", group: "roads" }, root: { layer: "roads" } },
    {
        info: { id: "roads-with-parks", name: "Citymaker.Recipe.RoadsParks", icon: "roadsParks", custom: false, description: "Citymaker.Recipe.RoadsParksTip" },
        root: { layer: "roads", children: [{ layer: "park", target: "block" }] },
    },
    {
        info: { id: "park-plaza", name: "Citymaker.Recipe.ParkPlaza", icon: "parkPlaza", custom: false, description: "Citymaker.Recipe.ParkPlazaTip" },
        root: { layer: "split", children: [{ layer: "park", target: "part:0" }, { layer: "plaza", target: "part:1" }] },
    },
    {
        info: { id: "neighbourhood-park", name: "Citymaker.Recipe.NeighbourhoodPark", icon: "playground", custom: false, description: "Citymaker.Recipe.NeighbourhoodParkTip" },
        root: { layer: "park", children: [{ layer: "playground" }, { layer: "restrooms" }] },
    },
    { info: { id: "campground", name: "Citymaker.Campground.Name", icon: "campground", custom: false, description: "Citymaker.Recipe.CampgroundTip", group: "nature" }, root: { layer: "campground" } },
];

/** English for the fake layers' keys (the real layers ship their own Lang files). */
export const FAKE_STRINGS: Record<string, string> = {
    "Citymaker.Park.Name": "Park", "Citymaker.Park.Style": "Style", "Citymaker.Park.StyleTip": "Overall look: formal lines, natural curves or an open meadow.",
    "Citymaker.Park.Style.Formal": "Formal", "Citymaker.Park.Style.Natural": "Natural", "Citymaker.Park.Style.Meadow": "Meadow",
    "Citymaker.Park.PathWidth": "Path width", "Citymaker.Park.TreeDensity": "Trees", "Citymaker.Park.TreeDensityTip": "How much of the lawn is planted with trees.",
    "Citymaker.Park.Furniture": "Benches & lamps", "Citymaker.Park.PathAsset": "Path type", "Citymaker.Park.TreeSpecies": "Tree species",
    "Citymaker.Park.Entrances": "Entrances", "Citymaker.Park.Lake": "Pond", "Citymaker.Park.LakeTip": "Digs a pond. Terrain changes are permanent.",
    "Citymaker.Park.LakeSize": "Pond size", "Citymaker.Park.Fence": "Fence", "Citymaker.Park.FenceAsset": "Fence type",
    "Citymaker.Park.Preset.Formal": "Formal garden", "Citymaker.Park.Preset.Wild": "Wild meadow", "Citymaker.Park.Preset.Pond": "Pond park",
    "Citymaker.Plaza.Name": "Plaza", "Citymaker.Plaza.Pattern": "Layout", "Citymaker.Plaza.Pattern.Grid": "Grid", "Citymaker.Plaza.Pattern.Radial": "Radial",
    "Citymaker.Plaza.Pattern.Open": "Open", "Citymaker.Plaza.Centerpiece": "Centerpiece", "Citymaker.Plaza.Furniture": "Furniture",
    "Citymaker.Plaza.Surface": "Paving", "Citymaker.Plaza.Arrangement": "Edge sequence", "Citymaker.Plaza.EdgeTrees": "Trees along the edge",
    "Citymaker.Plaza.Preset.Market": "Market square", "Citymaker.Plaza.Preset.Civic": "Civic square",
    "Citymaker.Parking.Name": "Parking", "Citymaker.Parking.StallAngle": "Stall angle", "Citymaker.Parking.Angle90": "90°", "Citymaker.Parking.Angle60": "60°",
    "Citymaker.Parking.Angle45": "45°", "Citymaker.Parking.Entrances": "Entrances", "Citymaker.Parking.Surface": "Surface", "Citymaker.Parking.Greenery": "Greenery",
    "Citymaker.Parking.AisleWidth": "Aisle width", "Citymaker.Parking.Medians": "Planted medians", "Citymaker.Parking.Lanterns": "Lanterns",
    "Citymaker.Parking.Chargers": "EV chargers", "Citymaker.Parking.Fee": "Parking fee", "Citymaker.Parking.NeedsEntrance": "Place at least one entrance on the outline, where cars come in.",
    "Citymaker.Parking.TooSmall": "Too small for a parking lot (at least 600 m²).",
    "Citymaker.Roads.Name": "Roads", "Citymaker.Roads.Pattern": "Pattern", "Citymaker.Roads.Pattern.Grid": "Grid", "Citymaker.Roads.Pattern.Loop": "Loops",
    "Citymaker.Roads.Pattern.Organic": "Organic", "Citymaker.Roads.BlockSize": "Block size", "Citymaker.Roads.Street": "Street type", "Citymaker.Roads.Angle": "Rotation",
    "Citymaker.Roads.Perimeter": "Road along the edge", "Citymaker.Roads.CulDeSacs": "Cul-de-sacs", "Citymaker.Roads.Preset.Dense": "Dense grid", "Citymaker.Roads.Preset.Suburb": "Suburb",
    "Citymaker.Split.Name": "Split", "Citymaker.Split.Ratio": "First part", "Citymaker.Split.Direction": "Direction", "Citymaker.Split.Vertical": "Side by side",
    "Citymaker.Split.Horizontal": "Stacked", "Citymaker.Split.Divider": "Path between parts",
    "Citymaker.Campground.Name": "Campground", "Citymaker.Campground.Pitches": "Pitches", "Citymaker.Campground.Campfires": "Campfires",
    "Citymaker.Playground.Name": "Playground", "Citymaker.Playground.Size": "Size", "Citymaker.Playground.Equipment": "Equipment",
    "Citymaker.Size.Small": "Small", "Citymaker.Size.Medium": "Medium", "Citymaker.Size.Large": "Large",
    "Citymaker.Fountain.Name": "Fountain", "Citymaker.Fountain.Asset": "Fountain",
    "Citymaker.Restrooms.Name": "Restrooms", "Citymaker.Restrooms.Count": "Buildings",
    "Citymaker.BigTrees.Name": "Big trees", "Citymaker.BigTrees.Count": "Trees", "Citymaker.BigTrees.Species": "Species",
    "Citymaker.EdgeZoning.Name": "Edge zoning", "Citymaker.EdgeZoning.Zone": "Zone", "Citymaker.EdgeZoning.Depth": "Depth (cells)",
    "Citymaker.Zone.ResidentialLow": "Low res.", "Citymaker.Zone.ResidentialHigh": "High res.", "Citymaker.Zone.Commercial": "Commercial",
    "Citymaker.Zone.Mixed": "Mixed", "Citymaker.Zone.Office": "Office", "Citymaker.Zone.Industry": "Industry",
    "Citymaker.Picnic.Name": "Picnic area", "Citymaker.Picnic.Tables": "Tables",
    "Citymaker.Pond.Name": "Pond", "Citymaker.Pond.Size": "Size", "Citymaker.Pond.SizeTip": "Share of the park. Digging is permanent.",
    "Citymaker.Pond.Shore": "Shore", "Citymaker.Pond.Shore.Grass": "Grass", "Citymaker.Pond.Shore.Stone": "Stone",
    "Citymaker.Kiosk.Name": "Kiosk", "Citymaker.Kiosk.Count": "Kiosks",
    "Citymaker.Feature.CentralPlaza": "Central plaza", "Citymaker.Feature.Pond": "Pond", "Citymaker.Feature.Trees": "Trees",
    "Citymaker.Feature.StreetZoning": "Zoning along streets",
    "Citymaker.Feature.CentralPlazaTip": "A paved plaza inside the park; pick its shape, place and size.",
    "Citymaker.Park.PlazaShape": "Shape", "Citymaker.Park.PlazaAt": "Position", "Citymaker.Park.PlazaSize": "Size",
    "Citymaker.Shape.Circle": "Circle", "Citymaker.Shape.Square": "Square", "Citymaker.Shape.Rect": "Rect",
    "Citymaker.At.Centre": "Centre", "Citymaker.At.Corner": "Corner", "Citymaker.At.Edge": "Edge", "Citymaker.At.Entrance": "Entrance",
    "Citymaker.Recipe.RoadsParks": "Roads + parks", "Citymaker.Recipe.ParkPlaza": "Park | plaza", "Citymaker.Recipe.NeighbourhoodPark": "Family park",
    "Citymaker.Marker.TwoWay": "Two-way", "Citymaker.Marker.Entry": "Entry only", "Citymaker.Marker.Exit": "Exit only",
    "Citymaker.Marker.Alley": "Alley", "Citymaker.Marker.Footpath": "Footpath", "Citymaker.Reason.NeedEntrance": "Place at least one entrance", "Citymaker.Reason.AddEntrance": "Add an entrance to build",
    "Citymaker.Parking.PerimeterRoad": "Perimeter road", "Citymaker.Parking.ZoningEdges": "Zone the edges", "Citymaker.Parking.ZoningEdgesTip": "Zones a strip along the lot so buildings can grow next to it.",
    "Citymaker.Park.Ground": "Lawn", "Citymaker.Park.GroundTip": "Ground texture under the lawn.",
    "Citymaker.Decals.Pick": "Decals", "Citymaker.Decals.PickTip": "Leaves, cracks, puddles, markings, scattered over the ground.",
    "Citymaker.Decals.Density": "Density", "Citymaker.Decals.Clustering": "Clustering", "Citymaker.Decals.ClusteringTip": "Low: spread evenly. High: in patches.",
    "Citymaker.UI.Group.ground": "Ground", "Citymaker.UI.Group.decals": "Decals",
    "Citymaker.UI.Group.zoning": "Zoning", "Citymaker.UI.Group.layout": "Layout", "Citymaker.UI.Group.extras": "Extras", "Citymaker.UI.Group.paths": "Paths",
    "Citymaker.UI.Group.planting": "Planting", "Citymaker.UI.Group.water": "Water", "Citymaker.UI.Group.edge": "Edge",
    "Citymaker.Recipe.ParkingTip": "A surface parking lot with stalls, aisles and entrances.",
    "Citymaker.Recipe.PlazaTip": "A paved square with a centrepiece, seating and lights.",
    "Citymaker.Recipe.ParkTip": "Paths, lawns, trees and benches.",
    "Citymaker.Recipe.RoadsTip": "A street pattern fitted to the area.",
    "Citymaker.Recipe.RoadsParksTip": "Streets, and every block between them becomes a park.",
    "Citymaker.Recipe.ParkPlazaTip": "Half park, half plaza.",
    "Citymaker.Recipe.NeighbourhoodParkTip": "A park with a playground and restrooms.",
    "Citymaker.Recipe.CampgroundTip": "Tent pitches around a loop path.",
    "Citymaker.Stat.Trees": "trees", "Citymaker.Stat.Benches": "benches", "Citymaker.Stat.Lamps": "lamps", "Citymaker.Stat.Stalls": "stalls",
    "Citymaker.Stat.PathLength": "of paths", "Citymaker.Stat.RoadLength": "of road", "Citymaker.Stat.Blocks": "blocks", "Citymaker.Stat.Playgrounds": "playgrounds",
    "Citymaker.Stat.Fountains": "fountains", "Citymaker.Stat.Restrooms": "restrooms", "Citymaker.Stat.ZoneCells": "zone cells", "Citymaker.Stat.Tents": "tents",
    "Citymaker.Stat.Chargers": "EV chargers", "Citymaker.Stat.PavedArea": "paved",
    "Citymaker.Issue.PlazaLarge": "Large plazas feel empty. Consider Park | plaza, or more furniture.",
    "Citymaker.Issue.TreesSkipped": "{0} trees were left out where they would block a path.",
    "Citymaker.Issue.BlockTooSmall": "Block {0} is too small to fill and stays empty.",
    "Citymaker.Issue.NoRoom": "{0}: no free spot left, skipped.",
    "Citymaker.Marker.entrance.Plural": "entrances",
};

// ───────────── fake assets (fakeAssets.ts) ─────────────

export { fakeAssets, surfaceColor } from "./fakeAssets";

// ───────────── more types (the Make row at scale) ─────────────

/** Types other workstreams are adding: backend.addMoreTypes() puts them in the catalog (Make-row tests). */
export const MORE_TYPES: [string, string, string, string][] = [
    ["botanicalGarden", "Botanical garden", "flower", "gardens"],
    ["sportsPark", "Sports park", "people", "sports"],
    ["dogPark", "Dog park", "park", "parks"],
    ["picnicGrounds", "Picnic grounds", "picnic", "parks"],
    ["natureReserve", "Nature reserve", "tree", "nature"],
    ["formalGarden", "Formal garden", "flower", "gardens"],
    ["communityGarden", "Community garden", "leaf", "gardens"],
    ["marketSquare", "Market square", "plaza", "squares"],
    ["parkingGarage", "Parking garage", "parking", "parking"],
    ["quarry", "Quarry", "area", "industrial"],
    ["restArea", "Rest area", "parking", "transport"],
];
