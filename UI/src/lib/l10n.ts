// Localisation: every visible string goes through translate(key, fallback).
// English fallbacks live next to the call (t("Citymaker.UI.X", "English")) and are extracted into
// Lang/en-US.ui.json by tools/extract-lang.js, so the game's locale files and the code never drift.
// Keys owned by layers ("Citymaker.Park.*") fall back to a humanised last segment so a missing
// locale entry still reads well ("PathWidth" → "Path width") instead of showing a raw key.
import { useCallback, useMemo } from "react";
import { useLocalization } from "cs2/l10n";
import { LocText } from "../contract";

export type TFunc = (key: string, fallback?: string, args?: ReadonlyArray<string | number>) => string;

export function humanize(key: string): string {
    if (!key) return "";
    const last = key.split(".").pop() ?? key;
    const spaced = last
        .replace(/[_-]+/g, " ")
        .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
        .replace(/([A-Z])([A-Z][a-z])/g, "$1 $2")
        .trim();
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
}

/** "{0} of {1}" / "{NAME}" substitution (game locale files use both styles). */
export function substitute(text: string, args?: ReadonlyArray<string | number>): string {
    if (!args || args.length === 0) return text;
    return text.replace(/\{(\w+)\}/g, (m, k) => {
        const i = Number(k);
        if (!Number.isNaN(i) && i < args.length) return String(args[i]);
        return m;
    });
}

export function useT() {
    let translate: ((id: string, fallback?: string | null) => string | null) | null = null;
    try {
        translate = useLocalization().translate;
    } catch {
        translate = null;
    }
    const t: TFunc = useCallback(
        (key, fallback, args) => {
            let s: string | null = null;
            if (key) {
                try {
                    s = translate ? translate(key, null) : null;
                } catch {
                    s = null;
                }
            }
            if (s == null || s === key) s = fallback ?? humanize(key);
            return substitute(s, args);
        },
        [translate],
    );
    const tl = useCallback((loc: LocText | undefined | null, fallback?: string) => (loc && loc.key ? t(loc.key, fallback, loc.args) : fallback ?? ""), [t]);
    return useMemo(() => ({ t, tl }), [t, tl]);
}
