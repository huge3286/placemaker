// Mock "game" shell: the real Citymaker UI module (src/index.tsx) registered into a stand-in module
// registry, a 2D map, a fake vanilla toolbar and selected-info panel, and a small dev bar.
import React, { useEffect, useReducer, useState } from "react";
import { createRoot } from "react-dom/client";
import register from "../src/index";
import { INFO_SECTION_TYPE } from "../src/components/InfoSection";
import { icon } from "../src/lib/icons";
import { publish, trigger, triggerLog } from "./cs2/api";
import { setShowKeys, showKeys } from "./cs2/l10n";
import { createRegistry, SELECTED_INFO_SECTIONS } from "./cs2/modding";
import { MockBackend } from "./backend/backend";
import { MapView } from "./map";

const backend = new MockBackend();
const registry = createRegistry();
register(registry as any);
(window as any).pm = { backend, registry, trigger, triggerLog };
(window as any).pmPublish = publish;

function useBackend() {
    const [, bump] = useReducer((x: number) => x + 1, 0);
    useEffect(() => {
        const off = backend.onChange(bump);
        return () => {
            off();
        };
    }, []);
    return backend;
}

function useRemScale(uiScale: number) {
    useEffect(() => {
        const apply = () => {
            document.documentElement.style.fontSize = (window.innerHeight / 1080) * uiScale + "px";
        };
        apply();
        window.addEventListener("resize", apply);
        return () => window.removeEventListener("resize", apply);
    }, [uiScale]);
}

const VanillaToolbar = () => (
    <div className="vanilla-toolbar" aria-hidden="true">
        {["roads", "zone", "water", "building", "park", "parking", "tree", "people", "cost"].map((n) => (
            <div key={n} className="vt-btn">
                <img src={icon(n)} />
            </div>
        ))}
    </div>
);

const InfoPanel = () => {
    const b = useBackend();
    const g = b.groups.find((x) => x.id === b.selectedGroup);
    if (!g || b.open) return null;
    const Section = registry.get(SELECTED_INFO_SECTIONS, "selectedInfoSectionComponents")?.[INFO_SECTION_TYPE];
    return (
        <div className="info-panel" data-testid="vanilla-info-panel">
            <div className="ip-title">{g.customName || "Area"}</div>
            <div className="ip-general">
                <div className="ip-row">
                    <span>Upkeep</span>
                    <span>¢{Math.round(g.upkeep).toLocaleString("en-US")}/month</span>
                </div>
            </div>
            {Section ? (
                <Section
                    groupId={g.id}
                    name={g.customName}
                    recipeNameKey={g.recipeName.key === "Citymaker.UI.Literal" ? String(g.recipeName.args?.[0]) : g.recipeName.key}
                    stats={g.stats}
                    upkeep={g.upkeep}
                    memberCount={g.plan.layers.reduce((n, l) => n + l.objects.length + l.nets.length, 0)}
                    missingAssets={false}
                    modified={false}
                    removing={false}
                />
            ) : null}
        </div>
    );
};

const DevBar = ({ uiScale, setUiScale }: { uiScale: number; setUiScale: (v: number) => void }) => {
    const b = useBackend();
    const real = b.layers.filter((l) => (l as any).real).length;
    return (
        <div className="devbar" data-testid="devbar">
            <strong>Citymaker mock</strong>
            <span data-testid="host-status" className={b.hostUp ? "ok" : "warn"}>
                {b.hostUp ? `Core host · ${real} real layer${real === 1 ? "" : "s"}` : "No host · fake planner"}
            </span>
            <span>plan: {b.plan ? `${b.plan.source} ${b.plan.ms.toFixed(0)} ms` : "—"}</span>
            <label>
                money ¢
                <input type="number" value={b.money} onChange={(e) => { b.money = Number(e.target.value); b.publishAll(); }} data-testid="money" />
            </label>
            <label>
                UI scale
                <select value={uiScale} onChange={(e) => setUiScale(Number(e.target.value))}>
                    {[0.75, 1, 1.25, 1.5].map((v) => (
                        <option key={v} value={v}>{v * 100}%</option>
                    ))}
                </select>
            </label>
            <label>
                <input type="checkbox" checked={showKeys} onChange={(e) => setShowKeys(e.target.checked)} /> keys
            </label>
            <button onClick={() => trigger("citymaker", "toggleTool")}>toggle tool</button>
        </div>
    );
};

const Toast = () => {
    const b = useBackend();
    const [msg, setMsg] = useState<string | null>(null);
    useEffect(() => {
        if (!b.toast) return;
        setMsg(b.toast);
        b.toast = null;
        const t = window.setTimeout(() => setMsg(null), 1600);
        return () => window.clearTimeout(t);
    });
    return msg ? <div className="toast">{msg}</div> : null;
};

const App = () => {
    const [uiScale, setUiScale] = useState(1);
    useRemScale(uiScale);
    useEffect(() => {
        const onKey = (e: KeyboardEvent) => {
            const tag = (e.target as HTMLElement)?.tagName;
            if (tag === "INPUT" || tag === "SELECT" || tag === "TEXTAREA") return;
            if (e.key === "Enter") trigger("citymaker", "primary");
            else if (e.ctrlKey && (e.key === "z" || e.key === "Z") && !e.shiftKey) trigger("citymaker", "undo");
            else if (e.ctrlKey && (e.key === "y" || ((e.key === "z" || e.key === "Z") && e.shiftKey))) trigger("citymaker", "redo");
            else if (e.key === "Escape") trigger("citymaker", backend.markerMode ? "endMarkers" : "close");
            else if (e.ctrlKey && e.shiftKey && (e.key === "a" || e.key === "A")) trigger("citymaker", "toggleTool");
            else return;
            e.preventDefault();
        };
        window.addEventListener("keydown", onKey);
        return () => window.removeEventListener("keydown", onKey);
    }, []);
    const top = registry.appended["GameTopLeft"] ?? [];
    const game = registry.appended["Game"] ?? [];
    return (
        <div className="game">
            <MapView backend={backend} />
            <div className="top-left">
                <div className="vanilla-round" />
                <div className="vanilla-round" />
                {top.map((C, i) => (
                    <C key={i} />
                ))}
            </div>
            <VanillaToolbar />
            <div className="game-layer">
                {game.map((C, i) => (
                    <C key={i} />
                ))}
            </div>
            <InfoPanel />
            <DevBar uiScale={uiScale} setUiScale={setUiScale} />
            <Toast />
        </div>
    );
};

createRoot(document.getElementById("root")!).render(<App />);
// ?fake=1 → deterministic fake catalog/planner only (the Playwright suite's default).
if (!new URLSearchParams(location.search).has("fake")) void backend.connectHost();
