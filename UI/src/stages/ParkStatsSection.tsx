// s17 — "Park stats": the last thing to configure before Build (docs/PARK-STATS.md). Each stat shows what
// Citymaker suggests from the design (calibrated to vanilla parks of this size), a one-line meaning, and lets the
// player override it. "Keep within vanilla range" (default on) clamps to what vanilla parks of a comparable size
// use; off allows anything, with a gentle warning. "How it's calculated" opens the formula and similar vanilla parks.
// Edits are per build (kept with the recipe, undoable, saved in the build's receipt) and never re-plan.
import React from "react";
import classNames from "classnames";
import { ParkStatView, ParkStatsView } from "../contract";
import { act } from "../lib/bindings";
import { area as fmtArea, formatValue } from "../lib/format";
import { useT } from "../lib/l10n";
import { Tip } from "../lib/tips";
import { Btn, Check, Collapsible, Icon } from "../components/common";
import { useOpen } from "../lib/collapse";
import { Slider } from "../controls/Slider";
import s from "./parkStats.module.scss";

/** [label key, English], [meaning key, English] per stat id (extract-lang reads these pairs). */
const STAT_TEXT: Record<string, { label: [string, string]; meaning: [string, string] }> = {
    attractiveness: {
        label: ["Citymaker.UI.ParkStats.Attractiveness", "Attractiveness"],
        meaning: ["Citymaker.UI.ParkStats.Attractiveness.Meaning", "Draws tourists: adds to your city's tourism attractiveness."],
    },
    leisure: {
        label: ["Citymaker.UI.ParkStats.Leisure", "Leisure"],
        meaning: ["Citymaker.UI.ParkStats.Leisure.Meaning", "How much fun one visit gives a citizen looking for something to do."],
    },
    coverageRange: {
        label: ["Citymaker.UI.ParkStats.Range", "Reach"],
        meaning: ["Citymaker.UI.ParkStats.Range.Meaning", "How far residents walk to enjoy the park (path cost, about metres)."],
    },
    coverageCapacity: {
        label: ["Citymaker.UI.ParkStats.Capacity", "Capacity"],
        meaning: ["Citymaker.UI.ParkStats.Capacity.Meaning", "How many nearby streets the park can serve before it gets crowded."],
    },
    entertainment: {
        label: ["Citymaker.UI.ParkStats.Entertainment", "Entertainment"],
        meaning: ["Citymaker.UI.ParkStats.Entertainment.Meaning", "Wellbeing boost for residents within reach."],
    },
    upkeep: {
        label: ["Citymaker.UI.ParkStats.Upkeep", "Upkeep"],
        meaning: ["Citymaker.UI.ParkStats.Upkeep.Meaning", "Monthly running cost a vanilla park this good would have."],
    },
};

const fmt = (st: ParkStatView, v: number) => formatValue(v, st.unit === "money" ? "money" : st.unit === "count" ? "count" : "", st.step);

/** The five tiles of the folded card (upkeep is already in the money card). */
const TILES: { id: string; label: [string, string] }[] = [
    { id: "attractiveness", label: ["Citymaker.UI.ParkStats.Tile.Tourism", "Tourism"] },
    { id: "leisure", label: ["Citymaker.UI.ParkStats.Tile.Leisure", "Leisure"] },
    { id: "coverageRange", label: ["Citymaker.UI.ParkStats.Tile.Reach", "Reach"] },
    { id: "coverageCapacity", label: ["Citymaker.UI.ParkStats.Tile.Capacity", "Capacity"] },
    { id: "entertainment", label: ["Citymaker.UI.ParkStats.Tile.Wellbeing", "Wellbeing"] },
];

/** One mini tile: name, value, a tiny bar of the value against the vanilla range (band marked). */
const Tile = ({ st, label }: { st: ParkStatView; label: string }) => {
    const { t } = useT();
    const lo = Math.min(st.min, st.bandMin);
    const hi = Math.max(st.max, st.bandMax, lo + 1);
    const at = (v: number) => Math.max(0, Math.min(100, ((v - lo) / (hi - lo)) * 100));
    const text = STAT_TEXT[st.id];
    const outside = st.value < st.bandMin || st.value > st.bandMax;
    return (
        <Tip title={label + " " + fmt(st, st.value)} description={(text ? t(text.meaning[0], text.meaning[1]) + " " : "") + t("Citymaker.UI.ParkStats.Band", "Vanilla parks this size: {0}–{1}", [fmt(st, st.bandMin), fmt(st, st.bandMax)])}>
            <div className={s.tile} data-testid={`park-tile-${st.id}`}>
                <div className={s.tileLabel}>{label}</div>
                <div className={classNames(s.tileValue, { [s.valueOut]: outside, [s.valueSet]: st.overridden })}>{fmt(st, st.value)}</div>
                <div className={s.tileBar}>
                    <div className={s.tileBand} style={{ left: at(st.bandMin) + "%", width: Math.max(2, at(st.bandMax) - at(st.bandMin)) + "%" }} />
                    <div className={s.tileMark} style={{ left: at(st.value) + "%" }} />
                </div>
            </div>
        </Tip>
    );
};

export const ParkStatsSection = ({ ps, pending }: { ps: ParkStatsView; pending: boolean }) => {
    const { t } = useT();
    const [open, setOpen] = useOpen("review:parkStats:edit", false);
    const anyOverride = ps.stats.some((x) => x.overridden);
    const outside = !ps.keepVanilla && ps.stats.some((x) => x.value < x.bandMin || x.value > x.bandMax);
    return (
        <div className={classNames(s.card, { [s.shimmer]: pending })} data-testid="park-stats" data-open={open ? "true" : "false"}>
            <div className={s.cardHead}>
                <Icon name="star" size={11} tint="dim" />
                <div className={s.cardTitle}>{t("Citymaker.UI.ParkStats.Title", "Park stats")}</div>
                <div className={s.flex} />
                {anyOverride ? (
                    <Btn icon="reset" iconSize={11} title={t("Citymaker.UI.ParkStats.ResetAll", "Reset all to suggested")} onClick={() => act.resetParkStat("")} testId="park-stats-reset-all" />
                ) : null}
                <Btn
                    icon={open ? "chevronUp" : "edit"}
                    iconSize={11}
                    label={open ? t("Citymaker.UI.ParkStats.Done", "Done") : t("Citymaker.UI.ParkStats.Edit", "Edit")}
                    className={s.editBtn}
                    onClick={() => setOpen(!open)}
                    testId="park-stats-toggle"
                />
            </div>
            <div className={s.tiles}>
                {TILES.map((x) => {
                    const st = ps.stats.find((y) => y.id === x.id);
                    return st ? <Tile key={x.id} st={st} label={t(x.label[0], x.label[1])} /> : null;
                })}
            </div>
            {open ? (
                <div className={s.edit}>
                    <div className={s.topRow}>
                        <Tip title={t("Citymaker.UI.ParkStats.KeepVanilla.Tip", "Keeps every value inside what vanilla parks of a similar size use.")}>
                            <div className={s.keep}>
                                <Check testId="park-stats-keep-vanilla" checked={ps.keepVanilla} onChange={(v) => act.setParkStatsKeepVanilla(v)} label={t("Citymaker.UI.ParkStats.KeepVanilla", "Vanilla range")} />
                            </div>
                        </Tip>
                        <div className={s.kind}>
                            {ps.leisureType === "beach" ? t("Citymaker.UI.ParkStats.Beach", "Beach leisure") : t("Citymaker.UI.ParkStats.Park", "Park leisure")}
                        </div>
                    </div>
                    {ps.companion === "noRoad" ? (
                        <Tip title={t("Citymaker.UI.ParkStats.NoRoad", "No road edge: only tourism applies.")}>
                            <div className={s.warn} data-testid="park-stats-no-road">
                                <Icon name="info" size={11} tint="warn" />
                                <div className={s.warnText}>{t("Citymaker.UI.ParkStats.NoRoadShort", "No road edge")}</div>
                            </div>
                        </Tip>
                    ) : null}
                    {outside ? (
                        <Tip title={t("Citymaker.UI.ParkStats.Outside", "Some values are beyond what vanilla parks this size have; the city may feel unbalanced.")}>
                            <div className={s.warn} data-testid="park-stats-outside">
                                <Icon name="warning" size={11} tint="warn" />
                                <div className={s.warnText}>{t("Citymaker.UI.ParkStats.OutsideShort", "Beyond vanilla")}</div>
                            </div>
                        </Tip>
                    ) : null}
                    {ps.stats.map((st) => (
                        <StatRow key={st.id} st={st} keepVanilla={ps.keepVanilla} />
                    ))}
                    <Tip
                        title={
                            ps.companion === "yes"
                                ? t("Citymaker.UI.ParkStats.NoteCompanion", "A small hidden park building at the road edge carries these stats, like a vanilla park.")
                                : t("Citymaker.UI.ParkStats.Note", "Only attractiveness reaches the game here. The others are saved with the build.")
                        }
                    >
                        <div className={s.note}>{ps.companion === "yes" ? t("Citymaker.UI.ParkStats.NoteCompanionShort", "All stats apply") : t("Citymaker.UI.ParkStats.NoteShort", "Only tourism applies")}</div>
                    </Tip>
                </div>
            ) : null}
        </div>
    );
};

const StatRow = ({ st, keepVanilla }: { st: ParkStatView; keepVanilla: boolean }) => {
    const { t, tl } = useT();
    const text = STAT_TEXT[st.id] ?? { label: [st.id, st.id], meaning: ["", ""] };
    const label = t(text.label[0], text.label[1]);
    const meaning = text.meaning[0] ? t(text.meaning[0], text.meaning[1]) : "";
    const lo = keepVanilla ? st.bandMin : st.min;
    const hi = keepVanilla ? Math.max(st.bandMax, st.bandMin + st.step) : st.max;
    const outside = st.value < st.bandMin || st.value > st.bandMax;
    const similar = st.similar
        .map((v) => t(v.nameKey, v.name) + " (" + fmtArea(v.areaM2) + ") " + fmt(st, v.value))
        .join(" · ");
    return (
        <div className={s.stat} data-testid={`park-stat-${st.id}`}>
            <div className={s.head}>
                <Tip title={meaning}>
                    <div className={s.label} data-ellipsis="tooltip">
                        {label}
                    </div>
                </Tip>
                <Tip
                    title={
                        st.applied
                            ? t("Citymaker.UI.ParkStats.Applied.Tip", "The game uses this value for the build.")
                            : t("Citymaker.UI.ParkStats.Shown.Tip", "Needs the companion park building (road edge and setting); saved with the build.")
                    }
                >
                    <div className={classNames(s.tag, { [s.tagOn]: st.applied })}>
                        {st.applied ? t("Citymaker.UI.ParkStats.Applied", "In game") : t("Citymaker.UI.ParkStats.Shown", "Shown only")}
                    </div>
                </Tip>
                <div className={s.flex} />
                <div className={classNames(s.value, { [s.valueOut]: outside, [s.valueSet]: st.overridden })} data-testid={`park-stat-${st.id}-value`}>
                    {fmt(st, st.value)}
                </div>
                {st.overridden ? (
                    <Btn
                        icon="reset"
                        iconSize={11}
                        className={s.reset}
                        title={t("Citymaker.UI.ParkStats.Reset", "Reset to suggested ({0})", [fmt(st, st.suggested)])}
                        onClick={() => act.resetParkStat(st.id)}
                        testId={`park-stat-${st.id}-reset`}
                    />
                ) : null}
            </div>
            <Slider
                value={Math.min(hi, Math.max(lo, st.value))}
                min={lo}
                max={hi}
                step={st.step}
                unit={st.unit === "money" ? "money" : st.unit === "count" ? "count" : ""}
                onPreview={() => undefined}
                onCommit={(v) => act.setParkStat(st.id, v)}
                testId={`park-stat-${st.id}-slider`}
            />
            <Collapsible
                id={`review:parkStats:${st.id}`}
                title={t("Citymaker.UI.ParkStats.How", "How it's calculated")}
                summary={t("Citymaker.UI.ParkStats.Suggested", "Suggested {0}", [fmt(st, st.suggested)])}
                nested
                testId={`park-stat-${st.id}-how`}
            >
                {st.formula.map((f, n) => (
                    <div key={n} className={s.formula}>
                        {tl(f)}
                    </div>
                ))}
                {similar ? (
                    <div className={s.similar}>
                        {t("Citymaker.UI.ParkStats.Similar", "Similar to: {0}", [similar])}
                    </div>
                ) : null}
            </Collapsible>
        </div>
    );
};
