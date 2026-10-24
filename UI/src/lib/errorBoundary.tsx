// Crash guard: a render error in OUR tree must never take down the rest of the game UI.
// Adapted from Grid Road Generator (Ti4goc). In-game experience there: a React error #130 from a
// cs2/ui component missing at runtime (despite the typings) blanked the whole cohtml tree.
// React only offers render-error capture to class components, hence the class.
import React from "react";

interface Props {
    /** Short region name for the log ("panel", "review"…). */
    name: string;
    children?: React.ReactNode;
    /** Rendered instead of the children after a crash. Default: a small inline notice. */
    fallback?: React.ReactNode | ((error: unknown, reset: () => void) => React.ReactNode);
}

interface State {
    error: unknown;
    hasError: boolean;
}

export class ErrorBoundary extends React.Component<Props, State> {
    state: State = { error: null, hasError: false };

    static getDerivedStateFromError(error: unknown): State {
        return { error, hasError: true };
    }

    componentDidCatch(error: unknown, info: unknown) {
        console.error(`[Citymaker] UI region "${this.props.name}" crashed; showing fallback.`, error, info);
    }

    reset = () => this.setState({ error: null, hasError: false });

    render() {
        if (!this.state.hasError) return this.props.children ?? null;
        const { fallback } = this.props;
        if (typeof fallback === "function") return fallback(this.state.error, this.reset);
        if (fallback !== undefined) return fallback;
        const message = this.state.error instanceof Error ? this.state.error.message : String(this.state.error);
        return (
            <div
                style={{ padding: "6rem 10rem", color: "#ffb4a8", fontSize: "12rem", pointerEvents: "auto" }}
                onClick={this.reset}
                data-pm-crash={this.props.name}
            >
                Citymaker: this part of the panel failed ({message}). Click to retry.
            </div>
        );
    }
}
