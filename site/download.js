const packages = {
  win: {
    suffix: '-cpu-win-x64-setup.exe',
    label: 'Windows x64',
  },
  mac: {
    suffix: '-cpu-osx-arm64.zip',
    label: 'macOS Apple Silicon',
  },
  linux: {
    suffix: '-cpu-linux-x64.zip',
    label: 'Linux x64',
  },
};

async function detectPackage() {
  const { userAgent, userAgentData, maxTouchPoints } = navigator;
  if (userAgentData?.mobile || /Android|iPhone|iPad|iPod|CrOS|Mobile/i.test(userAgent)) return null;
  if (/Macintosh/i.test(userAgent) && maxTouchPoints > 1) return null;

  let hints;
  try {
    hints = await userAgentData?.getHighEntropyValues(['platform', 'architecture', 'bitness', 'wow64']);
  } catch {
    // Some browsers restrict access to architecture hints.
  }

  const platform = hints?.platform || userAgentData?.platform || userAgent;
  const architecture = hints?.architecture;
  const arm = architecture ? architecture === 'arm' : /\b(?:aarch64|arm64)\b/i.test(userAgent);
  const x64 = architecture
    ? architecture === 'x86' && (hints.bitness === '64' || hints.wow64)
    : /\b(?:Win64|WOW64|x64|x86_64|amd64)\b/i.test(userAgent) && !arm;

  if (/Windows/i.test(platform) && x64) return 'win';
  if (/Mac/i.test(platform) && arm) return 'mac';
  if (/Linux/i.test(platform) && x64) return 'linux';
  return null;
}

async function updateDownloadLinks() {
  const platform = detectPackage();
  const response = await fetch('https://api.github.com/repos/gnios/Verso/releases/latest', { cache: 'no-cache' });
  if (!response.ok) return;
  const release = await response.json();
  if (release.draft || release.prerelease || !Array.isArray(release.assets)) return;

  const visitorPackage = await platform;
  for (const button of document.querySelectorAll('.os-button')) {
    const os = button.dataset.os;
    const { suffix, label } = packages[os];
    const asset = release.assets.find((item) =>
      item.name?.endsWith(suffix) &&
      item.browser_download_url?.startsWith('https://github.com/gnios/Verso/releases/download/')
    );
    if (!asset) continue;

    button.href = asset.browser_download_url;
    const size = Number.isFinite(asset.size) && asset.size > 0
      ? Math.round(asset.size / 1048576) + ' MB'
      : '';
    button.querySelector('[data-size]').textContent = size ? ' · ' + size : '';
    if (os !== visitorPackage) continue;

    button.classList.add('os-recommended');
    for (const link of document.querySelectorAll('[data-recommended-download]')) {
      link.href = asset.browser_download_url;
      link.textContent = 'Baixar para ' + label;
    }
    document.querySelector('[data-recommended-version]').textContent =
      [release.tag_name, 'CPU', size].filter(Boolean).join(' · ');
  }
}

updateDownloadLinks().catch(() => {
  // Keep the release-page links available if GitHub cannot be reached.
});
