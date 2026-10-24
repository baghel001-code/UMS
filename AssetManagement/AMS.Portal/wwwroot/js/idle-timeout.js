// Tracks user activity and reports it (throttled) to IdleTimeoutMonitor.razor. While the user is active it also calls the keep-alive
// endpoint, which renews the auth cookie and tells UMS the session is in use. Activity is shared between tabs through localStorage.
const STORAGE_KEY = 'ams.lastActivity';
const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'scroll', 'touchstart'];

export function start(dotNetRef, options) {
    const throttleMs = options.reportThrottleMs;
    let disposed = false, lastReport = 0, lastBroadcast = 0, lastKeepAlive = Date.now();

    const report = (now) => {
        if (now - lastReport < throttleMs) return;
        lastReport = now;
        dotNetRef.invokeMethodAsync('ReportActivity').catch(() => { /* circuit gone */ });
    };

    const keepAlive = (now) => {
        if (!options.keepAliveUrl || now - lastKeepAlive < options.keepAliveIntervalMs) return;
        lastKeepAlive = now;
        fetch(options.keepAliveUrl, { method: 'POST', credentials: 'same-origin', redirect: 'manual', cache: 'no-store' })
            .then(r => { if (r.status === 401) r.json().then(b => dotNetRef.invokeMethodAsync('SessionEnded', (b && b.ended) || 'expired')).catch(() => {}); })
            .catch(() => { /* offline: the next activity retries */ });
    };

    const onActivity = () => {
        if (disposed) return;
        const now = Date.now();
        report(now);
        keepAlive(now);
        if (now - lastBroadcast >= throttleMs) {
            lastBroadcast = now;
            try { localStorage.setItem(STORAGE_KEY, String(now)); } catch { /* storage blocked */ }
        }
    };
    const onStorage = (e) => { if (!disposed && e.key === STORAGE_KEY) report(Date.now()); };

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
