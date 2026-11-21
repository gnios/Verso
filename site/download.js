const packages = {
  win: '-cpu-win-x64-setup.exe',
  mac: '-cpu-osx-arm64.zip',
  linux: '-cpu-linux-x64.zip',
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

const downloadStatus = document.querySelector('[data-download-status]');

async function updateDownloadLinks() {
  downloadStatus.textContent = 'Carregando os arquivos da versão estável…';
  const [visitorPackage, response] = await Promise.all([
    detectPackage(),
    fetch('https://api.github.com/repos/gnios/Verso/releases/latest', {
      cache: 'no-cache',
      signal: AbortSignal.timeout(8000),
    }),
  ]);
  if (!response.ok) throw new Error('Release request failed');
  const release = await response.json();
  if (release.draft || release.prerelease || !Array.isArray(release.assets)) {
    throw new Error('Stable release unavailable');
  }

  let available = 0;
  for (const button of document.querySelectorAll('.os-button')) {
    const os = button.dataset.os;
    const asset = release.assets.find((item) =>
      item.name?.endsWith(packages[os]) &&
      item.browser_download_url?.startsWith('https://github.com/gnios/Verso/releases/download/')
    );
    const details = button.querySelector('[data-size]');
    if (!asset) {
      details.textContent = ' · Indisponível';
      continue;
    }

    available++;
    button.href = asset.browser_download_url;
    button.removeAttribute('aria-disabled');
    if (Number.isFinite(asset.size) && asset.size > 0) {
      details.textContent = ' · ' + Math.round(asset.size / 1048576) + ' MB';
    }
    if (os === visitorPackage) {
      button.classList.add('os-recommended');
      button.parentElement.prepend(button);
    }
  }

  downloadStatus.textContent = available
    ? 'Versão estável ' + release.tag_name + ' · CPU'
    : 'Esta versão estável não tem pacotes disponíveis.';
}

updateDownloadLinks().catch(() => {
  downloadStatus.textContent = 'Os downloads estão indisponíveis no momento. Recarregue a página para tentar de novo.';
});
