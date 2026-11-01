// Fills in the latest version, size, date and checksum from the project's GitHub release.
// Nothing here is needed for the page to work: without JavaScript the buttons point at the releases page.
// Everything from the network is validated, and written with textContent, never as HTML.
(() => {
  'use strict';
  const REPO = 'giovannimazza/haptic-bridge';
  const API = `https://api.github.com/repos/${REPO}/releases/latest`;
  const lang = (document.documentElement.lang || 'en').slice(0, 2);
  const $$ = (sel) => document.querySelectorAll(sel);
  const setText = (sel, value) => $$(sel).forEach((el) => { el.textContent = value; });
  const setHref = (sel, href) => $$(sel).forEach((el) => { el.href = href; });
  const show = (sel) => $$(sel).forEach((el) => { el.hidden = false; });

  function megabytes(bytes) {
    return (bytes / 1048576).toLocaleString(lang, { maximumFractionDigits: 0 }) + ' MB';
  }

  async function load() {
    try {
      const response = await fetch(API, { headers: { Accept: 'application/vnd.github+json' }, cache: 'no-cache' });
      if (!response.ok) return;
      const release = await response.json();
      if (!release || release.draft || release.prerelease) return;

      const tag = String(release.tag_name || '');
      const version = /^v(\d{1,4}\.\d{1,4}\.\d{1,4})$/.exec(tag);
      if (!version) return;
      const base = `https://github.com/${REPO}/releases/download/${tag}/`;
      const assets = Array.isArray(release.assets) ? release.assets : [];
      const find = (name) => assets.find((a) => a && a.name === name && a.browser_download_url === base + name);

      const installerName = `HapticGamingBridge-Setup-${version[1]}.exe`;
      const installer = find(installerName);
      if (!installer) return;

      setHref('[data-dl]', base + installerName);
      setText('[data-version]', 'v' + version[1]);
      setText('[data-installer-name]', installerName);
      if (Number.isFinite(installer.size) && installer.size > 0) setText('[data-size]', megabytes(installer.size));
      const date = new Date(release.published_at);
      if (!Number.isNaN(date.getTime())) setText('[data-date]', date.toLocaleDateString(lang, { year: 'numeric', month: 'long', day: 'numeric' }));

      const digest = /^sha256:([0-9a-f]{64})$/i.exec(String(installer.digest || ''));
      if (digest) { setText('[data-sha]', digest[1].toLowerCase()); show('[data-sha-block]'); }

      const plugin = find('HapticGamingBridge_1_0.lplug4');
      if (plugin) { setHref('[data-plugin]', base + plugin.name); show('[data-plugin-item]'); }
      const sums = find('SHA256SUMS.txt');
      if (sums) { setHref('[data-sums]', base + sums.name); show('[data-sums-item]'); }
      if (typeof release.html_url === 'string' && release.html_url.startsWith(`https://github.com/${REPO}/releases/`)) setHref('[data-notes]', release.html_url);
    } catch (e) { /* offline or rate limited: the static links keep working */ }
  }

  $$('[data-copy]').forEach((button) => {
    const original = button.textContent;
    button.addEventListener('click', async () => {
      const code = document.querySelector(button.dataset.copy);
      if (!code || !code.textContent.trim()) return;
      try { await navigator.clipboard.writeText(code.textContent.trim()); button.textContent = button.dataset.done || 'Copied'; }
      catch (e) { const range = document.createRange(); range.selectNodeContents(code); const s = getSelection(); s.removeAllRanges(); s.addRange(range); }
      setTimeout(() => { button.textContent = original; }, 1800);
    });
  });

  load();
})();
