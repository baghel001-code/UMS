// Tracks user activity in the browser and reports it (throttled) to the IdleTimeout component.
// Loaded as an ES module via IJSRuntime "import". Activity in one tab is shared with the other tabs
// through localStorage, so working in one tab does not sign the user out of the others.

const STORAGE_KEY = 'vmm.lastActivity';
const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'scroll', 'touchstart'];

export function start(dotNetRef, options) {
    const throttleMs = options.reportThrottleMs;
    let disposed = false;
    let lastReport = 0;
    let lastBroadcast = 0;

    const report = (now) => {
        if (now - lastReport < throttleMs) return;
        lastReport = now;
        dotNetRef.invokeMethodAsync('ReportActivity').catch(() => { /* circuit gone */ });
    };

    const onActivity = () => {
        if (disposed) return;
        const now = Date.now();
        report(now);
        if (now - lastBroadcast >= throttleMs) {
            lastBroadcast = now;
            try { localStorage.setItem(STORAGE_KEY, String(now)); } catch { /* storage blocked */ }
        }
    };

    const onStorage = (e) => {
        if (!disposed && e.key === STORAGE_KEY) report(Date.now());
    };

    ACTIVITY_EVENTS.forEach(evt => document.addEventListener(evt, onActivity, { passive: true, capture: true }));
    window.addEventListener('storage', onStorage);

    return {
        dispose() {
            disposed = true;
            ACTIVITY_EVENTS.forEach(evt => document.removeEventListener(evt, onActivity, { capture: true }));
            window.removeEventListener('storage', onStorage);
        }
    };
}
