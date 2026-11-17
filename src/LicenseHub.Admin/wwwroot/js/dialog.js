// Native <dialog> helpers for the Modal component: showModal() traps focus and closes on Esc.
// A module file, not inline: the CSP allows only same-origin scripts.
export function open(dialog) {
    if (dialog && !dialog.open) {
        dialog.showModal();
    }
}

export function close(dialog) {
    if (dialog && dialog.open) {
        dialog.close();
    }
}
