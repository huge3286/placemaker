// Confirmation dialog: the game's own ConfirmationDialog (cs2/ui) when it exists at runtime, otherwise
// (or if it throws) a look-alike modal. Either way the player confirms destructive actions explicitly.
import React, { ReactNode } from "react";
import * as ui from "cs2/ui";
import { ErrorBoundary } from "../lib/errorBoundary";
import { useT } from "../lib/l10n";
import { Btn } from "./common";
import s from "./dialog.module.scss";

const Vanilla: any = (ui as any)?.ConfirmationDialog;

type Props = {
    title: ReactNode;
    message: ReactNode;
    confirm: string;
    danger?: boolean;
    onConfirm: () => void;
    onCancel: () => void;
};

export const OwnDialog = ({ title, message, confirm, danger, onConfirm, onCancel }: Props) => {
    const { t } = useT();
    return (
        <div className={s.backdrop} data-testid="confirm-dialog" onClick={onCancel}>
            <div className={s.dialog} onClick={(e) => e.stopPropagation()}>
                <div className={s.title}>{title}</div>
                <div className={s.message}>{message}</div>
                <div className={s.buttons}>
                    <Btn variant="solid" label={t("Citymaker.UI.Cancel", "Cancel")} onClick={onCancel} testId="confirm-cancel" />
                    <div className={s.spacer} />
                    <Btn variant={danger ? "danger" : "accent"} label={confirm} onClick={onConfirm} testId="confirm-ok" />
                </div>
            </div>
        </div>
    );
};

export const Confirm = (p: Props) => {
    if (!Vanilla) return <OwnDialog {...p} />;
    return (
        <ErrorBoundary name="dialog" fallback={<OwnDialog {...p} />}>
            <Vanilla title={p.title} message={p.message} confirm={p.confirm} onConfirm={() => p.onConfirm()} onCancel={p.onCancel} />
        </ErrorBoundary>
    );
};
