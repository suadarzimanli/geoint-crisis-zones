/* Theme: follows the system setting unless the visitor picks light or dark (remembered on this device).
   Loaded in <head> so the page never flashes in the wrong theme. Dispatches "cz-themechange" on change. */
(() => {
  'use strict';
  const KEY = 'cz-theme';
  const root = document.documentElement;
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const stored = () => { try { return localStorage.getItem(KEY); } catch { return null; } };
  const store = (v) => { try { localStorage.setItem(KEY, v); } catch { /* private mode: keep for this page only */ } };

  const saved = stored();
  if (saved === 'dark' || saved === 'light') root.dataset.theme = saved;

  const isDark = () => (root.dataset.theme ? root.dataset.theme === 'dark' : media.matches);
  window.CrisisZonesTheme = { isDark };

  const announce = () => document.dispatchEvent(new CustomEvent('cz-themechange', { detail: { dark: isDark() } }));

  function syncButton() {
    const b = document.getElementById('theme-toggle');
    if (!b) return;
    const dark = isDark();
    b.setAttribute('aria-pressed', String(dark));
    b.setAttribute('aria-label', dark ? 'Switch to light mode' : 'Switch to dark mode');
    b.title = b.getAttribute('aria-label');
  }

  document.addEventListener('DOMContentLoaded', () => {
    syncButton();
    document.getElementById('theme-toggle')?.addEventListener('click', () => {
      root.dataset.theme = isDark() ? 'light' : 'dark';
      store(root.dataset.theme);
      syncButton();
      announce();
    });
  });

  // Follow system changes while the visitor has not chosen a theme
  media.addEventListener('change', () => {
    if (!root.dataset.theme) { syncButton(); announce(); }
  });
})();
