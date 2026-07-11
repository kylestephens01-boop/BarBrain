// Geo-ASSIST only (ADR-015): sorts the nearby list and prefills a wiki add.
// Permission-optional by design — resolves null on denial/timeout/absence,
// never blocks a flow. There is NO GPS proximity gate anywhere in v1.
window.bbGetLocation = () => new Promise((resolve) => {
    if (!navigator.geolocation) { resolve(null); return; }
    navigator.geolocation.getCurrentPosition(
        (p) => resolve({ lat: p.coords.latitude, lng: p.coords.longitude }),
        () => resolve(null),
        { timeout: 5000, maximumAge: 300000 });
});
