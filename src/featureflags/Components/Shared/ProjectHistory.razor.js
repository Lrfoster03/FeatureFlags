const dialogs = new WeakMap();

export function openDialog(dialog, callback) {
    if (!dialogs.has(dialog)) {
        const closed = () => callback.invokeMethodAsync('DialogClosed');
        // Wrap Tab within the dialog instead of moving focus to browser chrome at either end.
        const keydown = event => {
            if (event.key !== 'Tab') return;
            const controls = [...dialog.querySelectorAll('button, summary, input, select, textarea')]
                .filter(element => !element.disabled && element.getClientRects().length > 0);
            const first = controls[0], last = controls.at(-1);
            if (event.shiftKey && document.activeElement === first) {
                event.preventDefault(); last?.focus();
            } else if (!event.shiftKey && document.activeElement === last) {
                event.preventDefault(); first?.focus();
            }
        };
        dialog.addEventListener('close', closed);
        dialog.addEventListener('keydown', keydown);
        dialogs.set(dialog, { closed, keydown });
    }
    dialog.showModal();
}

export function closeDialog(dialog) {
    dialog.close();
}

export function disposeDialog(dialog) {
    if (!dialog) return;
    const handlers = dialogs.get(dialog);
    if (handlers) {
        dialog.removeEventListener('close', handlers.closed);
        dialog.removeEventListener('keydown', handlers.keydown);
    }
    dialogs.delete(dialog);
    if (dialog.open) dialog.close();
}

export function formatTimes(root) {
    const dateFormat = new Intl.DateTimeFormat(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
    const timeFormat = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit', second: '2-digit', timeZoneName: 'short' });
    root.querySelectorAll('time[data-audit-time]').forEach(el => {
        const instant = new Date(el.dateTime);
        el.querySelector('[data-audit-date]').textContent = dateFormat.format(instant);
        el.querySelector('[data-audit-clock]').textContent = timeFormat.format(instant);
    });
}

export function dateBounds(from, through) {
    const boundary = (value, nextDay) => {
        if (!value) return null;
        const [year, month, day] = value.split('-').map(Number);
        return new Date(year, month - 1, day + (nextDay ? 1 : 0)).toISOString();
    };
    return { fromUtc: boundary(from, false), untilUtc: boundary(through, true) };
}
