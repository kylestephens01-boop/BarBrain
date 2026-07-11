// PWA glue (Sprint 6): install prompt capture + online/offline signal.
// Self-hosted, no external requests (CLAUDE.md hard rules).
let deferredPrompt = null;

window.addEventListener('beforeinstallprompt', (e) => {
    e.preventDefault(); // we show our own UI, flag-gated
    deferredPrompt = e;
    window.dispatchEvent(new CustomEvent('bb-installable'));
});

window.bbPwa = {
    isInstallable: () => deferredPrompt !== null,
    isStandalone: () =>
        window.matchMedia('(display-mode: standalone)').matches || navigator.standalone === true,
    isIos: () => /iphone|ipad|ipod/i.test(navigator.userAgent),
    promptInstall: async () => {
        if (!deferredPrompt) return false;
        deferredPrompt.prompt();
        const choice = await deferredPrompt.userChoice;
        deferredPrompt = null;
        return choice.outcome === 'accepted';
    },
    onInstallable: (dotnetRef) => {
        window.addEventListener('bb-installable', () =>
            dotnetRef.invokeMethodAsync('OnInstallable'));
    },
    // Returns current status and wires change events.
    watchOnline: (dotnetRef) => {
        window.addEventListener('online', () => dotnetRef.invokeMethodAsync('OnOnlineChanged', true));
        window.addEventListener('offline', () => dotnetRef.invokeMethodAsync('OnOnlineChanged', false));
        return navigator.onLine;
    },
    installDismissed: () => localStorage.getItem('bb-install-dismissed') === '1',
    dismissInstall: () => localStorage.setItem('bb-install-dismissed', '1'),
};
