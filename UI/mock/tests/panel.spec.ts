// Playtest 1 acceptance: right-docked tall readable panel, draggable, icons everywhere, tooltips beside
// the panel (never over the stage bar), namespaced icon files, and a full-HD screenshot of each stage.
import { expect, Page, test } from "@playwright/test";
import fs from "fs";
import path from "path";
import { drawPolygon, FIELD, open, shot, waitPlanned, pickType, unfoldSections } from "./helpers";

const UI = path.join(__dirname, "..", "..");

/** Every square icon button in the panel (and picker) shows a loaded icon. */
async function iconButtonsHaveIcons(page: Page) {
    const find = () => page.evaluate(() => {
        const out: string[] = [];
        document.querySelectorAll('[data-testid="citymaker-wrap"] [data-icon-button="true"]').forEach((b) => {
            const img = b.querySelector("img") as HTMLImageElement | null;
            if (!img || !img.getAttribute("src") || !img.complete || img.naturalWidth === 0) out.push((b.getAttribute("data-testid") || b.className) + " → " + (img?.getAttribute("src") ?? "no <img>"));
        });
        return out;
    });
    await expect.poll(find, { timeout: 4000 }).toEqual([]);
    const count = await page.locator('[data-testid="citymaker-wrap"] [data-icon-button="true"]').count();
    expect(count).toBeGreaterThan(2);
}

/** All <img> in the panel loaded (bundled glyphs never fail; a failed game icon falls back to ours). */
async function allImagesLoaded(page: Page) {
    await page.waitForTimeout(150);
    const bad = await page.evaluate(() =>
        Array.from(document.querySelectorAll('[data-testid="citymaker-wrap"] img'))
            .filter((i) => !(i as HTMLImageElement).complete || (i as HTMLImageElement).naturalWidth === 0)
            .map((i) => i.getAttribute("src")),
    );
    expect(bad).toEqual([]);
}

test("docked right, tall, near-opaque; header drag moves it, position is remembered, double-click re-docks", async ({ page }) => {
    await open(page);
    const wrap = page.getByTestId("citymaker-wrap");
    const vp = page.viewportSize()!;
    let b = (await wrap.boundingBox())!;
    expect(vp.width - (b.x + b.width)).toBeLessThan(20); // right edge
    expect(b.width).toBeGreaterThanOrEqual(380); // compact (round 3)
    expect(b.width).toBeLessThanOrEqual(580); // 560rem default on wide screens
    expect(b.y).toBeGreaterThan(40); // below the top bar
    expect(b.height).toBeGreaterThan(vp.height * 0.7); // tall
    expect(b.y + b.height).toBeLessThan(vp.height - 90); // above the bottom toolbar
    await expect(wrap).toHaveAttribute("data-docked", "true");

    // combined background alpha ≥ 0.92 (dark base under the vanilla panel colour)
    const alpha = await page.evaluate(() => {
        const a = (el: Element) => {
            const m = /rgba?\(([^)]+)\)/.exec(getComputedStyle(el).backgroundColor);
            if (!m) return 0;
            const p = m[1].split(",").map((x) => parseFloat(x));
            return p.length === 4 ? p[3] : 1;
        };
        const panel = document.querySelector('[data-testid="citymaker-panel"]')!;
        const tint = panel.firstElementChild!;
        return 1 - (1 - a(panel)) * (1 - a(tint));
    });
    expect(alpha).toBeGreaterThanOrEqual(0.92);

    // drag by the header (grip area)
    const grip = (await page.getByTestId("panel-grip").boundingBox())!;
    await page.mouse.move(grip.x + grip.width / 2, grip.y + grip.height / 2);
    await page.mouse.down();
    await page.mouse.move(grip.x - 300, grip.y + 60, { steps: 8 });
    await page.mouse.move(grip.x - 700, grip.y + 80, { steps: 8 });
    await page.mouse.up();
    b = (await wrap.boundingBox())!;
    await expect(wrap).toHaveAttribute("data-docked", "false");
    expect(Math.abs(b.x - (vp.width - 12 - b.width - 700))).toBeLessThan(30);
    // tips open to the side away from the screen edge; panel left of centre → right
    await shot(page, "panel-dragged");

    // remembered across reloads
    await page.reload();
    await expect(page.getByTestId("citymaker-panel")).toBeVisible();
    const b2 = (await wrap.boundingBox())!;
    expect(Math.abs(b2.x - b.x)).toBeLessThan(4);
    expect(Math.abs(b2.y - b.y)).toBeLessThan(4);

    // clamped to the screen
    const g2 = (await page.getByTestId("panel-grip").boundingBox())!;
    await page.mouse.move(g2.x + 5, g2.y + 5);
    await page.mouse.down();
    await page.mouse.move(-500, -400, { steps: 6 });
    await page.mouse.up();
    const b3 = (await wrap.boundingBox())!;
    expect(b3.x).toBeGreaterThanOrEqual(0);
    expect(b3.y).toBeGreaterThanOrEqual(0);

    // double-click re-docks
    await page.getByTestId("panel-header").dblclick({ position: { x: 260, y: 20 } });
    await expect(wrap).toHaveAttribute("data-docked", "true");
    b = (await wrap.boundingBox())!;
    expect(vp.width - (b.x + b.width)).toBeLessThan(20);
});

test("every icon button shows an icon, in every stage; icon files are namespaced; no coui://gameui", async ({ page }) => {
    await open(page);
    await iconButtonsHaveIcons(page);
    await allImagesLoaded(page);
    await drawPolygon(page, FIELD);
    await iconButtonsHaveIcons(page);
    await page.getByTestId("primary").click();
    await pickType(page, "parking");
    await waitPlanned(page);
    await iconButtonsHaveIcons(page);
    await allImagesLoaded(page);
    await unfoldSections(page);
    await iconButtonsHaveIcons(page);
    await page.getByTestId("asset-button-surface").click();
    await expect(page.getByTestId("asset-picker")).toBeVisible();
    await iconButtonsHaveIcons(page);
    await page.keyboard.press("Escape");
    await pickType(page, "roads");
    await page.getByTestId("feature-toggle-edgeZoning").click();
    await waitPlanned(page);
    await iconButtonsHaveIcons(page);
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await iconButtonsHaveIcons(page);
    await allImagesLoaded(page);

    // the icons are plain <img> of namespaced, baked-colour SVGs (no CSS masks)
    const srcs = await page.evaluate(() => Array.from(document.querySelectorAll('[data-testid="citymaker-wrap"] img')).map((i) => i.getAttribute("src") || ""));
    const ours = srcs.filter((x) => !x.startsWith("Media/"));
    expect(ours.length).toBeGreaterThan(10);
    for (const src of ours) expect(src).toMatch(/images\/citymaker\/[A-Za-z]+-(light|dark|dim|accent|warn|danger|ok)\.[0-9a-f]{6}\.svg$/);

    // game bundle: namespaced output, never the invalid lower-case GameUI host
    const build = path.join(UI, "build");
    if (fs.existsSync(path.join(build, "Citymaker.mjs"))) {
        const js = fs.readFileSync(path.join(build, "Citymaker.mjs"), "utf8");
        expect(js.toLowerCase()).not.toContain("coui://gameui");
        expect(fs.readdirSync(path.join(build, "images")).filter((n) => n !== "citymaker")).toEqual([]);
    }
});

test("tooltips open beside the panel, never over the stage bar", async ({ page }) => {
    await open(page);
    // body tips open to the left (the panel is docked right) …
    await page.getByTestId("area-mode-block").hover();
    const tip = page.getByTestId("tooltip");
    await expect(tip).toBeVisible();
    await expect(tip).toHaveAttribute("data-direction", "left");
    const tb = (await tip.boundingBox())!;
    const mb = (await page.getByTestId("area-mode-block").boundingBox())!;
    expect(tb.x + tb.width).toBeLessThanOrEqual(mb.x + 1);
    // … header tips upwards
    await page.mouse.move(5, 300);
    await page.getByTestId("undo").hover();
    await expect(tip).toHaveAttribute("data-direction", "up");
    // the playtest case: a tooltip of the first row under the stage bar covered it
    const sb = (await page.getByTestId("stage-bar").boundingBox())!;
    for (const id of ["area-mode-draw", "area-mode-block", "panel-grip", "close"]) {
        await page.mouse.move(5, 300);
        await page.getByTestId(id).hover();
        await expect(tip).toBeVisible();
        const t2 = (await tip.boundingBox())!;
        const overlaps = t2.x < sb.x + sb.width && t2.x + t2.width > sb.x && t2.y < sb.y + sb.height && t2.y + t2.height > sb.y;
        expect(overlaps, id).toBe(false);
    }
});

test("full-HD screenshots of each stage", async ({ page }) => {
    await open(page, { width: 1920, height: 1080 });
    await shot(page, "stage-1-area-empty");
    await drawPolygon(page, FIELD);
    await shot(page, "stage-1-area");
    await page.getByTestId("primary").click();
    await pickType(page, "parking");
    await waitPlanned(page);
    await shot(page, "stage-2-design-parking");
    await pickType(page, "park");
    await waitPlanned(page);
    await shot(page, "stage-2-design-park");
    await page.getByTestId("primary").click();
    await waitPlanned(page);
    await shot(page, "stage-3-review");
});
