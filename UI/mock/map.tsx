// 2D map for the mock: fake city, the area being selected (draw / pick block / paint / road loop),
// markers, and the REAL plan (from Core via the mock host, or the fake planner) drawn with the same
// role colours as the in-game overlay.
import { layerOfNet, layerOfObject, layerOfSurface } from "./backend/legend";
import React, { useEffect, useRef } from "react";
import { MockBackend } from "./backend/backend";
import { BLOCKS, blockAt, CELL, GRID, NODES, WORLD } from "./backend/city";
import { closestOnRing, dist, inside, V2 } from "./backend/geometry";
import { PlanLayer } from "./backend/types";

const SURFACE: Record<string, string> = {
    Grass: "rgba(92, 160, 84, 0.55)", Plaza: "rgba(214, 190, 150, 0.8)", Paving: "rgba(200, 180, 150, 0.8)", Asphalt: "rgba(58, 63, 68, 0.92)",
    Water: "rgba(63, 167, 214, 0.85)", Decorative: "rgba(232, 205, 160, 0.75)", Ground: "rgba(120, 100, 70, 0.5)",
};
const ZONE: Record<string, string> = {
    resLow: "rgba(108, 197, 106, 0.4)", resHigh: "rgba(70, 160, 70, 0.45)", commercial: "rgba(80, 150, 255, 0.4)", mixed: "rgba(80, 200, 190, 0.4)",
    office: "rgba(180, 140, 255, 0.4)", industry: "rgba(240, 200, 70, 0.4)",
};

function netStyle(role: string): { color: string; edge?: string; centre?: string } {
    if (role.startsWith("Pathway")) return { color: "#f5c542" };
    if (role === "DriveAisle" || role === "DriveConnector" || role === "Alley") return { color: "#4a7fd6", centre: "rgba(255,255,255,0.5)" };
    if (role === "Fence") return { color: "#d6d6d6" };
    return { color: "#5a6168", edge: "#8fe3ff", centre: "rgba(255,255,255,0.55)" };
}

export function MapView({ backend }: { backend: MockBackend }) {
    const ref = useRef<HTMLCanvasElement>(null);
    const mouse = useRef<{ p: V2 | null; shift: boolean; ctrl: boolean }>({ p: null, shift: false, ctrl: false });
    const drag = useRef<{ kind: "corner" | "paint" | "erase"; index?: number } | null>(null);
    const view = useRef({ s: 1, ox: 0, oy: 0 });
    const pulse = useRef(0);

    const toWorld = (x: number, y: number): V2 => [(x - view.current.ox) / view.current.s, (y - view.current.oy) / view.current.s];
    const toScreen = (p: V2): V2 => [p[0] * view.current.s + view.current.ox, p[1] * view.current.s + view.current.oy];

    useEffect(() => {
        (window as any).pm = { ...(window as any).pm, backend, toScreen, toWorld };
    });

    useEffect(() => {
        const c = ref.current!;
        let raf = 0;
        const draw = () => {
            raf = requestAnimationFrame(draw);
            const dpr = window.devicePixelRatio || 1;
            const w = c.clientWidth, h = c.clientHeight;
            if (c.width !== Math.round(w * dpr) || c.height !== Math.round(h * dpr)) {
                c.width = Math.round(w * dpr);
                c.height = Math.round(h * dpr);
            }
            const s = Math.max(w / WORLD.w, h / WORLD.h) * 0.92;
            view.current = { s, ox: (w - WORLD.w * s) / 2, oy: (h - WORLD.h * s) / 2 - h * 0.2 };
            const g = c.getContext("2d")!;
            g.setTransform(dpr, 0, 0, dpr, 0, 0);
            render(g, w, h);
        };
        raf = requestAnimationFrame(draw);
        return () => cancelAnimationFrame(raf);
    }, []);

    const render = (g: CanvasRenderingContext2D, w: number, h: number) => {
        const b = backend;
        const { s, ox, oy } = view.current;
        g.fillStyle = "#2e3a30";
        g.fillRect(0, 0, w, h);
        g.save();
        g.translate(ox, oy);
        g.scale(s, s);
        const px = 1 / s;

        // terrain + blocks
        for (const bl of BLOCKS) {
            g.fillStyle = bl.empty ? "#3c5040" : "#454b46";
            poly(g, bl.ring);
            g.fill();
            for (const bd of bl.buildings) {
                g.fillStyle = "#6a6e70";
                poly(g, bd);
                g.fill();
                g.strokeStyle = "#535759";
                g.lineWidth = px;
                g.stroke();
            }
        }
        // streets
        g.strokeStyle = "#24282b";
        g.lineWidth = GRID.street;
        g.lineCap = "butt";
        for (let k = 0; k < GRID.nx; k++) line(g, [GRID.x0 + k * GRID.dx, GRID.z0], [GRID.x0 + k * GRID.dx, GRID.z0 + (GRID.nz - 1) * GRID.dz]);
        for (let j = 0; j < GRID.nz; j++) line(g, [GRID.x0, GRID.z0 + j * GRID.dz], [GRID.x0 + (GRID.nx - 1) * GRID.dx, GRID.z0 + j * GRID.dz]);
        g.strokeStyle = "rgba(220, 220, 200, 0.35)";
        g.lineWidth = 0.6;
        g.setLineDash([6, 6]);
        for (let k = 0; k < GRID.nx; k++) line(g, [GRID.x0 + k * GRID.dx, GRID.z0], [GRID.x0 + k * GRID.dx, GRID.z0 + (GRID.nz - 1) * GRID.dz]);
        for (let j = 0; j < GRID.nz; j++) line(g, [GRID.x0, GRID.z0 + j * GRID.dz], [GRID.x0 + (GRID.nx - 1) * GRID.dx, GRID.z0 + j * GRID.dz]);
        g.setLineDash([]);

        // built groups
        for (const grp of b.groups) {
            for (const l of grp.plan.layers) drawLayer(g, l, 0.9, px);
            const hovered = mouse.current.p && inside(mouse.current.p, grp.ring);
            const sel = b.managedId === grp.id || b.selectedGroup === grp.id;
            if (hovered || sel) {
                g.strokeStyle = sel ? "#8fd1ff" : "rgba(143, 209, 255, 0.6)";
                g.lineWidth = 2.5 * px;
                poly(g, grp.ring);
                g.stroke();
            }
        }

        const toolArea = b.open && (b.stage === "area" || b.stage === "design" || b.stage === "review" || b.stage === "building");
        const mp = mouse.current.p;

        // block hover
        if (b.open && b.stage === "area" && b.mode === "block" && mp) {
            const bl = blockAt(mp);
            if (bl) {
                g.fillStyle = "rgba(143, 209, 255, 0.16)";
                poly(g, bl.ring);
                g.fill();
                g.strokeStyle = "rgba(143, 209, 255, 0.9)";
                g.lineWidth = 2 * px;
                g.stroke();
            }
        }

        // plan preview
        if (toolArea && b.plan && b.closed) {
            const ghost = b.stage !== "area" && b.previewMode() === "ghost";
            const alpha = b.stale ? 0.55 : ghost ? 0.7 : 1;
            if (ghost) {
                // Real preview stand-in: the chosen ground texture under translucent models.
                g.save();
                g.globalAlpha = b.ghostPending ? 0.35 : 0.6;
                g.fillStyle = b.ghostSurfaceColor();
                poly(g, b.ring);
                g.fill();
                g.restore();
            }
            // s22: only the layers the legend shows (hover isolates one); the drawn set is kept for tests.
            const drawn = new Set<string>();
            const vis = b.stage === "area" ? undefined : (id: string) => b.layerVisible(id);
            for (const l of b.plan.layers) drawLayer(g, l, alpha, px, vis, drawn);
            b.drawnLayers = Array.from(drawn).sort();
            if (b.stage !== "area") {
                g.font = `${12 * px}px Arial`;
                g.textAlign = "center";
                for (const l of b.plan.layers)
                    for (const r of l.regions) {
                        if (!/^(block|part):/.test(r.key)) continue;
                        g.strokeStyle = "rgba(255,255,255,0.35)";
                        g.setLineDash([4 * px, 4 * px]);
                        g.lineWidth = px;
                        poly(g, r.ring);
                        g.stroke();
                        g.setLineDash([]);
                        const cx = r.ring.reduce((a, p) => a + p[0], 0) / r.ring.length;
                        const cz = r.ring.reduce((a, p) => a + p[1], 0) / r.ring.length;
                        const label = r.key.split(":")[1];
                        g.fillStyle = "rgba(10, 20, 26, 0.75)";
                        g.beginPath();
                        g.arc(cx, cz, 9 * px, 0, Math.PI * 2);
                        g.fill();
                        g.fillStyle = "#ffffff";
                        g.fillText(String(Number(label) + 1), cx, cz + 4 * px);
                    }
            }
        }

        // paint cells
        if (toolArea && (b.mode === "paint" || b.mode === "blockPaint") && b.cells.size && b.stage === "area") {
            g.fillStyle = "rgba(143, 209, 255, 0.18)";
            b.cells.forEach((k) => {
                const [i, j] = k.split(",").map(Number);
                g.fillRect(i * CELL, j * CELL, CELL, CELL);
            });
        }

        // area outline
        if (toolArea && b.ring.length) {
            if (b.closed) {
                g.fillStyle = b.stage === "area" ? "rgba(143, 209, 255, 0.12)" : "rgba(143, 209, 255, 0.04)";
                poly(g, b.ring);
                g.fill();
            }
            g.strokeStyle = b.problem() ? "#ff6b5b" : "#e8f6ff";
            g.lineWidth = 2 * px;
            g.beginPath();
            b.ring.forEach((p, i) => (i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1])));
            if (b.closed) g.closePath();
            else if (mp && b.stage === "area" && (b.mode === "draw" || b.mode === "perimeter")) g.lineTo(mp[0], mp[1]);
            g.stroke();
            if (b.mode === "draw" || b.mode === "perimeter" || b.stage !== "area") {
                for (let i = 0; i < b.ring.length; i++) {
                    const p = b.ring[i];
                    const hot = mp && dist(mp, p) < 8 * px * 1.5;
                    g.fillStyle = i === 0 && !b.closed ? "#8fd1ff" : "#ffffff";
                    g.beginPath();
                    g.arc(p[0], p[1], (hot ? 6 : 4.5) * px, 0, Math.PI * 2);
                    g.fill();
                    g.strokeStyle = "#1b2a33";
                    g.lineWidth = 1.5 * px;
                    g.stroke();
                }
            }
        }

        // perimeter nodes
        if (b.open && b.stage === "area" && b.mode === "perimeter") {
            for (const n of NODES) {
                const hot = mp && dist(mp, n) < 14;
                g.fillStyle = hot ? "#8fd1ff" : "rgba(143, 209, 255, 0.45)";
                g.beginPath();
                g.arc(n[0], n[1], (hot ? 6 : 4) * px, 0, Math.PI * 2);
                g.fill();
            }
        }

        // markers
        if (toolArea) {
            for (const st of b.allSteps())
                for (const m of st.markers) {
                    const kindColor: Record<string, [string, string]> = {
                        entrance: ["#ff9b45", "⇅"], entry: ["#6fdc8c", "↓"], exit: ["#ff6b5b", "↑"], alley: ["#b48cff", "A"], footpath: ["#f5c542", "F"],
                    };
                    const [col, glyph] = kindColor[m.kind] ?? ["#ff9b45", ""];
                    g.fillStyle = col;
                    g.beginPath();
                    g.arc(m.p[0], m.p[1], 8 * px, 0, Math.PI * 2);
                    g.fill();
                    g.strokeStyle = "#ffffff";
                    g.lineWidth = 2 * px;
                    g.stroke();
                    if (glyph) {
                        g.fillStyle = "#0b1d26";
                        g.font = `bold ${11 * px}px sans-serif`;
                        g.textAlign = "center";
                        g.textBaseline = "middle";
                        g.fillText(glyph, m.p[0], m.p[1] + 0.5 * px);
                    }
                }
            if (b.markerMode && mp && b.ring.length > 2) {
                const q = closestOnRing(mp, b.ring);
                if (q.d < 14) {
                    g.strokeStyle = "#ff9b45";
                    g.lineWidth = 2 * px;
                    g.beginPath();
                    g.arc(q.q[0], q.q[1], 8 * px, 0, Math.PI * 2);
                    g.stroke();
                }
            }
        }

        // brush cursor
        if (b.open && b.stage === "area" && (b.mode === "paint" || b.mode === "blockPaint") && mp) {
            const r = b.brush.size / 2;
            g.strokeStyle = "rgba(255,255,255,0.85)";
            g.lineWidth = 1.5 * px;
            g.beginPath();
            if (b.brush.shape === "round") g.arc(mp[0], mp[1], r, 0, Math.PI * 2);
            else {
                g.save();
                g.translate(mp[0], mp[1]);
                g.rotate((b.brush.angle * Math.PI) / 180);
                g.rect(-r, -r, 2 * r, 2 * r);
                g.restore();
            }
            g.stroke();
        }

        // located issue
        if (b.locateAt) {
            pulse.current = (pulse.current + 0.05) % 1;
            g.strokeStyle = `rgba(255, 107, 91, ${1 - pulse.current})`;
            g.lineWidth = 3 * px;
            g.beginPath();
            g.arc(b.locateAt[0], b.locateAt[2], 10 + pulse.current * 30, 0, Math.PI * 2);
            g.stroke();
        }
        g.restore();
    };

    const onDown = (e: React.MouseEvent) => {
        const b = backend;
        const p = toWorld(e.nativeEvent.offsetX, e.nativeEvent.offsetY);
        const right = e.button === 2;
        const hitGroup = b.groups.find((grp) => inside(p, grp.ring));
        if (b.markerMode) {
            if (right) b.removeMarkerNear(p);
            else b.addMarker(p);
            return;
        }
        if (!b.open) {
            b.selectedGroup = hitGroup ? hitGroup.id : null;
            b.publishAll();
            return;
        }
        const tol = 9 / view.current.s;
        // corner editing on a closed outline works in every stage ("adjust the shape anytime")
        if (b.closed && (b.mode === "draw" || b.stage !== "area") && b.stage !== "building") {
            const vi = b.ring.findIndex((q) => dist(q, p) < tol * 1.4);
            if (right && vi >= 0) {
                b.removePoint(vi);
                return;
            }
            if (!right && vi >= 0) {
                b.beginGesture({ key: "Citymaker.UI.History.MoveCorner" });
                drag.current = { kind: "corner", index: vi };
                return;
            }
            if (!right && e.ctrlKey) {
                const q = closestOnRing(p, b.ring);
                if (q.d < tol) {
                    b.insertPoint(q.seg, q.q);
                    return;
                }
            }
        }
        if ((b.stage === "area" || b.stage === "manage") && hitGroup && !right && (b.ring.length === 0 || b.stage === "manage")) {
            b.clickGroup(hitGroup.id);
            return;
        }
        if (b.stage !== "area") return;
        switch (b.mode) {
            case "draw":
                if (right) {
                    if (!b.closed && b.ring.length) b.removePoint(b.ring.length - 1);
                    return;
                }
                if (!b.closed) b.addPoint(b.ring.length >= 3 && dist(p, b.ring[0]) < tol * 1.5 ? b.ring[0] : p);
                return;
            case "block": {
                const bl = blockAt(p);
                if (bl && !right) b.pickBlock(bl, e.shiftKey);
                return;
            }
            case "paint":
                b.beginGesture({ key: right ? "Citymaker.UI.History.Erase" : "Citymaker.UI.History.Paint" });
                drag.current = { kind: right ? "erase" : "paint" };
                b.paintAt(p, right);
                return;
            case "blockPaint": {
                // Mock: no road graph, so no fence; Shift+drag erases like the game.
                const erase = right || e.shiftKey;
                b.beginGesture({ key: erase ? "Citymaker.UI.History.Erase" : "Citymaker.UI.History.Paint" });
                drag.current = { kind: erase ? "erase" : "paint" };
                b.paintAt(p, erase);
                return;
            }
            case "perimeter": {
                if (right) {
                    if (!b.closed && b.ring.length) b.removePoint(b.ring.length - 1);
                    return;
                }
                const i = NODES.findIndex((n) => dist(n, p) < 14);
                if (i >= 0) b.pickNode(i);
                return;
            }
        }
    };

    const onMove = (e: React.MouseEvent) => {
        const p = toWorld(e.nativeEvent.offsetX, e.nativeEvent.offsetY);
        const prev = mouse.current.p;
        mouse.current = { p, shift: e.shiftKey, ctrl: e.ctrlKey };
        const b = backend;
        const d = drag.current;
        if (d?.kind === "corner" && d.index !== undefined) b.movePoint(d.index, p);
        else if (d?.kind === "paint" || d?.kind === "erase") b.paintAt(p, d.kind === "erase");
        else if (e.shiftKey && (b.mode === "paint" || b.mode === "blockPaint") && b.brush.shape === "square" && prev) {
            b.brush = { ...b.brush, angle: Math.round((b.brush.angle + (p[0] - prev[0]) * 2 + 360) % 90) };
            b.publishAll();
        }
    };

    const onUp = () => {
        if (drag.current) {
            drag.current = null;
            backend.endGesture();
        }
    };

    return (
        <canvas
            ref={ref}
            className="map"
            data-testid="map"
            onMouseDown={onDown}
            onMouseMove={onMove}
            onMouseUp={onUp}
            onMouseLeave={() => {
                mouse.current.p = null;
                onUp();
            }}
            onContextMenu={(e) => e.preventDefault()}
        />
    );
}

function poly(g: CanvasRenderingContext2D, ring: V2[]) {
    g.beginPath();
    ring.forEach((p, i) => (i ? g.lineTo(p[0], p[1]) : g.moveTo(p[0], p[1])));
    g.closePath();
}

function line(g: CanvasRenderingContext2D, a: V2, b: V2) {
    g.beginPath();
    g.moveTo(a[0], a[1]);
    g.lineTo(b[0], b[1]);
    g.stroke();
}

function drawLayer(g: CanvasRenderingContext2D, l: PlanLayer, alpha: number, px: number, vis?: (id: string) => boolean, drawn?: Set<string>) {
    const show = (id: string) => {
        if (vis && !vis(id)) return false;
        drawn?.add(id);
        return true;
    };
    g.save();
    g.globalAlpha = alpha;
    for (const s of l.surfaces) {
        if (!show(layerOfSurface(s))) continue;
        g.fillStyle = SURFACE[s.role] ?? "rgba(200,200,200,0.4)";
        poly(g, s.ring);
        g.fill();
    }
    for (const z of l.zoning ?? []) {
        if (!show("cutouts")) continue;
        g.fillStyle = ZONE[z.zone] ?? "rgba(180, 140, 255, 0.4)";
        poly(g, z.quad);
        g.fill();
        g.strokeStyle = "rgba(255,255,255,0.25)";
        g.lineWidth = px;
        g.stroke();
    }
    g.lineCap = "round";
    for (const n of l.nets) {
        if (!show(layerOfNet(n, l))) continue;
        // s16: roads of the roads layer in their role colour (white street, cyan avenue, green local, violet ring, orange perimeter).
        const st = n.roleColor ? { color: "#5a6168", edge: n.roleColor, centre: n.roleColor } : netStyle(n.role);
        const [a, b1, c1, d] = n.pts;
        const path = () => {
            g.beginPath();
            g.moveTo(a[0], a[1]);
            g.bezierCurveTo(b1[0], b1[1], c1[0], c1[1], d[0], d[1]);
        };
        if (st.edge) {
            g.strokeStyle = st.edge;
            g.lineWidth = n.width + 1.2;
            path();
            g.stroke();
        }
        g.strokeStyle = st.color;
        g.lineWidth = Math.max(n.width, 1.2 * px);
        path();
        g.stroke();
        if (st.centre) {
            g.strokeStyle = st.centre;
            g.lineWidth = n.roleColor ? Math.max(1.5, n.width * 0.25) : 0.5;
            g.setLineDash([3, 4]);
            path();
            g.stroke();
            g.setLineDash([]);
        }
    }
    for (const o of l.objects) {
        if (!show(layerOfObject(o))) continue;
        switch (o.role) {
            case "Tree":
                g.fillStyle = "#2f7d32";
                g.beginPath();
                g.arc(o.p[0], o.p[1], o.r, 0, Math.PI * 2);
                g.fill();
                g.strokeStyle = "#7fd17c";
                g.lineWidth = 0.5;
                g.stroke();
                break;
            case "ParkingBay":
                g.save();
                g.translate(o.p[0], o.p[1]);
                g.rotate(o.yaw);
                g.strokeStyle = "rgba(255,255,255,0.75)";
                g.lineWidth = 0.25;
                g.strokeRect(-1.2, -2.4, 2.4, 4.8);
                g.restore();
                break;
            case "Centerpiece":
                g.fillStyle = "#4fb8e8";
                g.beginPath();
                g.arc(o.p[0], o.p[1], o.r * 0.8, 0, Math.PI * 2);
                g.fill();
                g.strokeStyle = "#dff4ff";
                g.lineWidth = 0.8;
                g.stroke();
                break;
            case "Lamp":
                g.fillStyle = "#ffe08a";
                g.beginPath();
                g.arc(o.p[0], o.p[1], 0.9, 0, Math.PI * 2);
                g.fill();
                break;
            case "Bench":
                g.fillStyle = "#c08a55";
                g.fillRect(o.p[0] - 1, o.p[1] - 0.5, 2, 1);
                break;
            case "Planter":
                g.fillStyle = "#6cc56a";
                g.fillRect(o.p[0] - 1, o.p[1] - 1, 2, 2);
                break;
            default:
                g.fillStyle = "#ffa94d";
                g.beginPath();
                g.arc(o.p[0], o.p[1], Math.max(1, o.r * 0.7), 0, Math.PI * 2);
                g.fill();
        }
    }
    g.restore();
}
