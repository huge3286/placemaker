// FEEDBACK-4b #7 / #8: enabled features fold (collapsed by default, remembered per type + feature,
// one-line summary when folded), and each enabled feature says in Design whether it fit the latest
// plan: green check, amber "won't fit — reduced", red "no room"; the type header counts the misfits.
import { expect, Page, test } from "@playwright/test";
import { drawPolygon, FIELD, noTextOverflow, open, panelShot, waitPlanned, pickType } from "./helpers";

async function toDesign(page: Page, recipe: string) {
    await drawPolygon(page, FIELD);
    await page.getByTestId("primary").click();
    await pickType(page, recipe);
    await waitPlanned(page);
}

async function forceFit(page: Page, layer: string, fit: unknown) {
    await page.evaluate(
        ([l, f]) => {
            const b = (window as any).pm.backend;
            b.fitOverride[l as string] = f;
            b.publishAll();
        },
        [layer, fit] as const,
    );
}

test("feature rows fold: collapsed after enabling, summary when folded, open state remembered per type + feature", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("citymaker.sections.v1"));
    await toDesign(page, "park");

    // off: no chevron, nothing to fold
    await expect(page.getByTestId("feature-fold-plaza")).toHaveCount(0);
    await page.getByTestId("feature-toggle-plaza").click();
    const plaza = page.getByTestId("feature-plaza");
    await expect(plaza).toHaveAttribute("data-open", "false");
    await expect(page.getByTestId("feature-options-plaza")).toHaveCount(0);
    // e.g. "Circle · Centre · 15%"
    const summary = page.getByTestId("feature-summary-plaza");
    await expect(summary).toBeVisible();
    expect((await summary.textContent())!.split("·").length).toBeGreaterThanOrEqual(2);

    await page.getByTestId("feature-fold-plaza").click();
    await expect(plaza).toHaveAttribute("data-open", "true");
    await expect(page.getByTestId("feature-options-plaza").getByTestId("control-plazaShape")).toBeVisible();
    await expect(summary).toHaveCount(0);

    // a second feature starts folded on its own
    await page.getByTestId("feature-toggle-playground").click();
    await expect(page.getByTestId("feature-playground")).toHaveAttribute("data-open", "false");

    // remembered across a type switch and a reload
    await pickType(page, "plaza");
    await pickType(page, "park");
    await page.getByTestId("feature-toggle-plaza").click();
    await expect(page.getByTestId("feature-plaza")).toHaveAttribute("data-open", "true");
    await page.getByTestId("feature-fold-plaza").click();
    await expect(page.getByTestId("feature-plaza")).toHaveAttribute("data-open", "false");
    expect(await page.evaluate(() => JSON.parse(localStorage.getItem("citymaker.sections.v1") || "{}")["feature:park>plaza"])).toBe(false);
    await page.getByTestId("feature-fold-plaza").click();
    await page.reload();
    await expect(page.getByTestId("citymaker-panel")).toBeVisible();
    await toDesign(page, "park");
    await page.getByTestId("feature-toggle-plaza").click();
    await expect(page.getByTestId("feature-plaza")).toHaveAttribute("data-open", "true");
    await page.evaluate(() => localStorage.removeItem("citymaker.sections.v1"));
    expect(errors).toEqual([]);
});

test("fit status: check when placed, amber reduced / red no room from the plan, count on the type header", async ({ page }) => {
    const errors = await open(page);
    await page.evaluate(() => localStorage.removeItem("citymaker.sections.v1"));
    await toDesign(page, "park");
    await expect(page.getByTestId("misfits-0")).toHaveCount(0);

    // computed from the plan: the playground fits in an open field
    await page.getByTestId("feature-toggle-playground").click();
    await waitPlanned(page);
    await expect(page.getByTestId("feature-fit-playground")).toHaveAttribute("data-kind", "ok");
    await expect(page.getByTestId("feature-fit-line-playground")).toHaveCount(0);
    // off features carry no status
    await expect(page.getByTestId("feature-fit-fountain")).toHaveCount(0);

    // the plan says: smaller playground, no room for the fountain
    await page.getByTestId("feature-toggle-fountain").click();
    await waitPlanned(page);
    await forceFit(page, "playground", { kind: "reduced", text: { key: "Citymaker.Issue.NoRoom", args: ["Playground"] } });
    await forceFit(page, "fountain", { kind: "failed", text: { key: "Citymaker.UI.Feature.NoRoom" } });
    await expect(page.getByTestId("feature-fit-playground")).toHaveAttribute("data-kind", "reduced");
    await expect(page.getByTestId("feature-fit-line-playground")).toContainText("Reduced");
    await expect(page.getByTestId("feature-fit-line-fountain")).toContainText("No room");
    await expect(page.getByTestId("feature-fit-line-fountain")).toHaveAttribute("data-kind", "failed");
    const chip = page.getByTestId("misfits-0");
    await expect(chip).toHaveAttribute("data-count", "2");
    await expect(chip).toContainText("2 didn't fit");
    // the verdict stays visible while the row is folded, and with it open
    await page.getByTestId("feature-fold-fountain").click();
    await expect(page.getByTestId("feature-fit-line-fountain")).toBeVisible();
    await noTextOverflow(page, "fit status");
    await panelShot(page, "fit-status-design");

    // stale (re-planning) statuses are dimmed, not hidden
    await forceFit(page, "fountain", { kind: "failed", text: { key: "Citymaker.UI.Feature.NoRoom" }, stale: true });
    await expect(page.getByTestId("feature-fit-fountain")).toHaveAttribute("data-stale", "true");

    // back to computed: both fit again → no count
    await forceFit(page, "playground", null);
    await forceFit(page, "fountain", null);
    await expect(page.getByTestId("misfits-0")).toHaveCount(0);
    // turning a feature off clears its status
    await page.getByTestId("feature-toggle-fountain").click();
    await expect(page.getByTestId("feature-fit-fountain")).toHaveCount(0);
    await page.evaluate(() => localStorage.removeItem("citymaker.sections.v1"));
    expect(errors).toEqual([]);
});
