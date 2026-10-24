// Remembered open/closed state of collapsible sections, per "<layer>:<group>" key, in localStorage.
// Storage can be missing or throw (Gameface profiles, private windows): then sections simply use their
// defaults for this session.
import { useCallback, useEffect, useState } from "react";

const KEY = "citymaker.sections.v1";
let cache: Record<string, boolean> | null = null;
const listeners = new Set<() => void>();

function load(): Record<string, boolean> {
    if (cache) return cache;
    cache = {};
    try {
        const raw = window.localStorage?.getItem(KEY);
        const v = raw ? JSON.parse(raw) : null;
        if (v && typeof v === "object") for (const k of Object.keys(v)) if (typeof v[k] === "boolean") cache[k] = v[k];
    } catch {
        /* defaults only */
    }
    return cache;
}

export function isOpen(key: string, def: boolean): boolean {
    const v = load()[key];
    return v === undefined ? def : v;
}

export function setOpen(key: string, open: boolean) {
    load()[key] = open;
    try {
        window.localStorage?.setItem(KEY, JSON.stringify(cache));
    } catch {
        /* not remembered */
    }
    listeners.forEach((l) => l());
}

/** Forget everything (tests, "reset layout"). */
export function resetSections() {
    cache = {};
    try {
        window.localStorage?.removeItem(KEY);
    } catch {
        /* ignore */
    }
    listeners.forEach((l) => l());
}

export function useOpen(key: string, def: boolean): [boolean, (open: boolean) => void] {
    const [, bump] = useState(0);
    useEffect(() => {
        const l = () => bump((n) => n + 1);
        listeners.add(l);
        return () => {
            listeners.delete(l);
        };
    }, []);
    const set = useCallback((open: boolean) => setOpen(key, open), [key]);
    return [isOpen(key, def), set];
}

/** Open/closed lookups that re-render the caller whenever any section folds (layouts that pack folded sections). */
export function useSections(): (key: string, def: boolean) => boolean {
    const [, bump] = useState(0);
    useEffect(() => {
        const l = () => bump((n) => n + 1);
        listeners.add(l);
        return () => {
            listeners.delete(l);
        };
    }, []);
    return isOpen;
}
