// Citymaker UI entry. Registration pattern adapted from Grid Road Generator (Ti4goc) and
// CS2-NetworkTools (Luca Rager, MIT). Each hook-up is isolated in its own try/catch: one failing
// registration (e.g. a game update renaming a module) must not cost the others.
import { ModRegistrar } from "cs2/modding";
import { initializeVanilla } from "./lib/vanilla";
import { CitymakerRoot } from "./components/Panel";
import { Launcher } from "./components/Launcher";
import { INFO_SECTION_TYPE, CitymakerInfoSection } from "./components/InfoSection";

const SELECTED_INFO_SECTIONS = "game-ui/game/components/selected-info-panel/selected-info-sections/selected-info-sections.tsx";

const register: ModRegistrar = (moduleRegistry) => {
    try {
        initializeVanilla(moduleRegistry);
    } catch (e) {
        console.error("[Citymaker] vanilla component lookup failed; using built-in fallbacks.", e);
    }

    // Panel: our own full-screen layer in "Game", docked bottom-centre above the toolbar. Researched
    // alternative: extending MouseToolOptions renders inside the narrow vanilla tool-options column,
    // which has no room for a stage bar, layer tree and review card and ignores our spacing.
    try {
        moduleRegistry.append("Game", CitymakerRoot);
    } catch (e) {
        console.error("[Citymaker] could not add the panel.", e);
    }

    try {
        moduleRegistry.append("GameTopLeft", Launcher);
    } catch (e) {
        console.error("[Citymaker] could not add the toolbar button.", e);
    }

    // Selected-info section for built groups. The C# side adds the section (SelectedInfoUISystem);
    // here we only map its type name to our component.
    try {
        const sections = moduleRegistry.get(SELECTED_INFO_SECTIONS, "selectedInfoSectionComponents");
        if (sections && typeof sections === "object" && Object.keys(sections).length > 0) {
            moduleRegistry.override(SELECTED_INFO_SECTIONS, "selectedInfoSectionComponents", {
                ...sections,
                [INFO_SECTION_TYPE]: CitymakerInfoSection,
            });
        } else {
            console.warn("[Citymaker] selectedInfoSectionComponents not found; the info-panel section stays off.");
        }
    } catch (e) {
        console.error("[Citymaker] could not register the info-panel section.", e);
    }
};

export default register;
