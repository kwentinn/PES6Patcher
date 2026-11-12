// Fills in the latest version and the exe link from the GitHub release; the static link works without it.
(async () => {
  try {
    const response = await fetch('https://api.github.com/repos/kwentinn/PES6Patcher/releases/latest', { headers: { Accept: 'application/vnd.github+json' } });
    if (!response.ok) return;
    const release = await response.json();
    const version = String(release.tag_name || '').replace(/^v/, '');
    if (version) document.querySelectorAll('[data-version]').forEach(el => { el.textContent = version; });
    const exe = (release.assets || []).find(a => a.name === 'PES6Patcher.exe');
    if (exe) document.querySelectorAll('[data-download]').forEach(el => { el.href = exe.browser_download_url; });
  } catch { /* offline or rate limited: keep the static values */ }
})();
