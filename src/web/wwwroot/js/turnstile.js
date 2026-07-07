// Cloudflare Turnstile glue (signup only). The challenge script legitimately
// comes from Cloudflare — it cannot be self-hosted (BRAND.md's no-CDN rule is
// about fonts). Loaded on demand and only when a site key is configured, so
// dev/CI never touch the network.
window.bbTurnstile = {
  async render(siteKey) {
    if (!siteKey) return;
    if (!window.turnstile) {
      await new Promise((resolve, reject) => {
        const s = document.createElement('script');
        s.src = 'https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit';
        s.async = true;
        s.onload = resolve;
        s.onerror = reject;
        document.head.appendChild(s);
      });
    }
    window.__bbTurnstileToken = '';
    window.turnstile.render('#bb-turnstile', {
      sitekey: siteKey,
      theme: 'dark',
      callback: (token) => { window.__bbTurnstileToken = token; },
      'expired-callback': () => { window.__bbTurnstileToken = ''; },
    });
  },
  token() { return window.__bbTurnstileToken || null; },
};
