// Pure helpers over the catalog/design contract: no React, unit-testable in the mock.
import { Catalog, Control, LayerType, SettingValue, StepView } from "../contract";

/** Control with the optional default value (proposed contract addition, see docs/proposals/ui.md). */
export type ControlEx = Control & { default?: SettingValue };

export function layerOf(catalog: Catalog, id: string): LayerType {
    const l = catalog.layers.find((x) => x.id === id);
    if (l) return l;
    return { id, name: id, icon: "", kind: "fill", accepts: [], emits: [], controls: [], presets: [] };
}

/**
 * "fence=true", "mode!=auto", "style=a|b", several joined with "&" or ",".
 * Unknown ids evaluate against undefined (hidden for "=", shown for "!=").
 */
export function isVisible(visibleWhen: string | undefined, values: Record<string, SettingValue>): boolean {
    if (!visibleWhen) return true;
    const clauses = visibleWhen.split(/&&|&|,/).map((s) => s.trim()).filter(Boolean);
    return clauses.every((clause) => {
        const neg = clause.includes("!=");
        const [id, rhs = "true"] = clause.split(neg ? "!=" : "=").map((s) => s.trim());
        const v = values[id];
        const actual = Array.isArray(v) ? v.join("|") : v === undefined ? "" : String(v);
        const match = rhs.split("|").some((alt) => alt === actual || (alt === "true" && v === true) || (alt === "false" && v === false));
        return neg ? !match : match;
    });
}

/** The first clause of a condition that is false for these values, or null when it holds. */
export function failingClause(cond: string | undefined, values: Record<string, SettingValue>): { id: string; neg: boolean; values: string[] } | null {
    if (!cond) return null;
    for (const clause of cond.split(/&&|&|,/).map((s) => s.trim()).filter(Boolean)) {
        if (isVisible(clause, values)) continue;
        const neg = clause.includes("!=");
        const [id, rhs = "true"] = clause.split(neg ? "!=" : "=").map((s) => s.trim());
        return { id, neg, values: rhs.split("|") };
    }
    return null;
}

/** (s15) How a control shows now: hidden (the documented exceptions), greyed out with a reason, or live. */
export type ControlAvailability = { show: boolean; disabled: boolean; reason?: string };

type Translate = (key: string, fallback?: string, args?: ReadonlyArray<string | number>) => string;

/**
 * Disable-with-reason (s15): a control that does not apply is SHOWN greyed out with a short reason instead of
 * vanishing, so the player sees what exists and why it is off. Sources, first match wins:
 *   visibleWhen false  → hidden only with hideWhenOff (Roads' per-pattern essentials), else greyed;
 *   enabledWhen false  → greyed;
 *   areaReason         → greyed with the area reason from C# (step.disabled: "Needs a street edge"…).
 * The reason is disabledReason (a key) or worded from the condition: "Only when Fence is on",
 * "Only when Layout is Alley".
 */
export function controlAvailability(
    c: ControlEx,
    values: Record<string, SettingValue>,
    controls: ControlEx[],
    t: Translate,
    areaReason?: string,
): ControlAvailability {
    const applies = isVisible(c.visibleWhen, values);
    if (!applies && c.hideWhenOff) return { show: false, disabled: true };
    const cond = !applies ? c.visibleWhen : !isVisible(c.enabledWhen, values) ? c.enabledWhen : undefined;
    if (cond) {
        const worded = whyFrom(cond, values, controls, t);
        return { show: true, disabled: true, reason: c.disabledReason ? t(c.disabledReason, worded) : worded };
    }
    if (areaReason) return { show: true, disabled: true, reason: areaReason };
    return { show: true, disabled: false };
}

function whyFrom(cond: string, values: Record<string, SettingValue>, controls: ControlEx[], t: Translate): string {
    const f = failingClause(cond, values);
    const other = f ? controls.find((x) => x.id === f.id) : undefined;
    if (!f || !other) return t("Citymaker.UI.Why.NotNow", "Not available with the current settings");
    const name = t(other.label, humanizeId(other.label || other.id));
    const onOff = f.values.length === 1 && (f.values[0] === "true" || f.values[0] === "false");
    if (other.kind === "toggle" || onOff) {
        const on = (f.values[0] !== "false") !== f.neg;
        return on ? t("Citymaker.UI.Why.WhenOn", "Only when {0} is on", [name]) : t("Citymaker.UI.Why.WhenOff", "Only when {0} is off", [name]);
    }
    const opts = f.values.map((v) => {
        const o = other.options?.find((x) => x.value === v);
        return o ? t(o.label, humanizeId(o.label || o.value)) : v;
    });
    const list = opts.join(t("Citymaker.UI.Why.Or", " or "));
    return f.neg ? t("Citymaker.UI.Why.WhenNot", "Not when {0} is {1}", [name, list]) : t("Citymaker.UI.Why.WhenIs", "Only when {0} is {1}", [name, list]);
}

function humanizeId(key: string): string {
    const last = key.split(".").pop() || key;
    return last.replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (ch) => ch.toUpperCase());
}

export function valuesEqual(a: SettingValue | undefined, b: SettingValue | undefined): boolean {
    if (Array.isArray(a) || Array.isArray(b)) {
        if (!Array.isArray(a) || !Array.isArray(b) || a.length !== b.length) return false;
        return a.every((x, i) => x === b[i]);
    }
    if (typeof a === "number" && typeof b === "number") return Math.abs(a - b) < 1e-9;
    return a === b;
}

export function defaultFor(c: ControlEx): SettingValue {
    if (c.default !== undefined && c.default !== null) return c.default;
    switch (c.kind) {
        case "slider":
            return c.min ?? 0;
        case "toggle":
            return false;
        case "choice":
            return c.options?.[0]?.value ?? "";
        case "assetMulti":
        case "arrangement":
            return [];
        default:
            return "";
    }
}

export function valueOf(step: StepView, c: ControlEx): SettingValue {
    const v = step.values[c.id];
    return v === undefined || v === null ? defaultFor(c) : v;
}

/** Depth-first over children and region overrides. */
export function walk(step: StepView | null, fn: (s: StepView, depth: number, parent: StepView | null) => void, depth = 0, parent: StepView | null = null) {
    if (!step) return;
    fn(step, depth, parent);
    step.children.forEach((c) => walk(c, fn, depth + 1, step));
    Object.keys(step.overrides).forEach((k) => walk(step.overrides[k], fn, depth + 1, step));
}

export function findStep(root: StepView | null, path: string): StepView | null {
    let found: StepView | null = null;
    walk(root, (s) => {
        if (!found && s.path === path) found = s;
    });
    return found;
}

export function parentOf(root: StepView | null, path: string): StepView | null {
    let found: StepView | null = null;
    walk(root, (s, _d, p) => {
        if (!found && s.path === path) found = p;
    });
    return found;
}

export function isOverridePath(root: StepView | null, path: string): string | null {
    const p = parentOf(root, path);
    if (!p) return null;
    for (const k of Object.keys(p.overrides)) if (p.overrides[k].path === path) return k;
    return null;
}

export function regionLabelArgs(key: string): { tag: string; index: number | null } {
    const i = key.indexOf(":");
    if (i < 0) return { tag: key, index: null };
    const n = Number(key.slice(i + 1));
    return { tag: key.slice(0, i), index: Number.isNaN(n) ? null : n + 1 };
}
