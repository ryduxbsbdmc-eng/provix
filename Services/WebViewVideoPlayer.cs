using System.IO;
using System.Net;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FileExplorer.Services;

public static class WebViewVideoPlayer
{
    private const string VirtualHost = "provix-media.local";

    public static bool UsesWebViewPlayback(string filePath) =>
        Path.GetExtension(filePath).Equals(".webm", StringComparison.OrdinalIgnoreCase);

    public static async Task PlayLocalFileAsync(WebView2 webView, string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("Invalid media path.");

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Media file not found.", fullPath);

        var fileName = Path.GetFileName(fullPath);
        await webView.EnsureCoreWebView2Async().ConfigureAwait(true);

        if (webView.CoreWebView2 is null)
            throw new InvalidOperationException("WebView2 is not available.");

        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            directory,
            CoreWebView2HostResourceAccessKind.Allow);

        var videoUrl = BuildVideoUrl(fileName);
        webView.CoreWebView2.NavigateToString(BuildPlayerHtml(videoUrl));
    }

    public static void Stop(WebView2 webView)
    {
        if (webView.CoreWebView2 is null)
            return;

        try
        {
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost,
                null,
                CoreWebView2HostResourceAccessKind.Deny);
            webView.CoreWebView2.NavigateToString("about:blank");
        }
        catch
        {
            // Ignore teardown races.
        }
    }

    private static string BuildVideoUrl(string fileName)
    {
        var encodedSegments = fileName
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Uri.EscapeDataString(segment));

        return $"https://{VirtualHost}/{string.Join('/', encodedSegments)}";
    }

    private static string BuildPlayerHtml(string videoUrl)
    {
        var encodedUrl = WebUtility.HtmlEncode(videoUrl);
        return PlayerHtmlTemplate.Replace("%%VIDEO_URL%%", encodedUrl);
    }

    private const string PlayerHtmlTemplate = """
        <!DOCTYPE html>
        <html lang="ru">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <style>
            :root {
              --bg: #141414;
              --panel: #1a1a1a;
              --panel-2: #181818;
              --border: rgba(255,255,255,0.2);
              --text: #f0f0f0;
              --muted: #b0b0b0;
              --accent: #4da3ff;
              --accent-strong: #0078d7;
              --hover: rgba(255,255,255,0.09);
              --pressed: rgba(255,255,255,0.16);
              --track: rgba(255,255,255,0.18);
              --shadow: 0 8px 28px rgba(0,0,0,0.45);
              --radius: 8px;
              --font: "Segoe UI", system-ui, sans-serif;
            }
            * { box-sizing: border-box; }
            html, body {
              margin: 0;
              width: 100%;
              height: 100%;
              background: var(--bg);
              color: var(--text);
              font-family: var(--font);
              overflow: hidden;
              user-select: none;
            }
            .stage {
              display: flex;
              flex-direction: column;
              width: 100%;
              height: 100%;
              background: #000;
            }
            .viewport {
              flex: 1;
              display: flex;
              align-items: center;
              justify-content: center;
              min-height: 0;
              background: #000;
            }
            video {
              width: 100%;
              height: 100%;
              max-height: 100%;
              object-fit: contain;
              background: #000;
              outline: none;
            }
            .controls {
              background: linear-gradient(180deg, rgba(20,20,20,0.96), rgba(20,20,20,1));
              border-top: 1px solid var(--border);
              padding: 10px 12px 12px;
            }
            .progress-wrap { margin-bottom: 10px; }
            input[type=range] {
              -webkit-appearance: none;
              appearance: none;
              width: 100%;
              height: 5px;
              border-radius: 999px;
              background: var(--track);
              outline: none;
              cursor: pointer;
            }
            input[type=range]::-webkit-slider-thumb {
              -webkit-appearance: none;
              width: 14px;
              height: 14px;
              border-radius: 50%;
              background: var(--accent-strong);
              border: 2px solid #fff;
              box-shadow: 0 0 0 2px rgba(0,120,215,0.25);
            }
            input[type=range]::-moz-range-thumb {
              width: 14px;
              height: 14px;
              border-radius: 50%;
              background: var(--accent-strong);
              border: 2px solid #fff;
            }
            .row {
              display: flex;
              align-items: center;
              gap: 10px;
            }
            .spacer { flex: 1; }
            .btn {
              display: inline-flex;
              align-items: center;
              justify-content: center;
              min-width: 38px;
              height: 38px;
              padding: 0 12px;
              border: 1px solid var(--border);
              border-radius: 6px;
              background: rgba(255,255,255,0.06);
              color: var(--text);
              font: 600 13px var(--font);
              cursor: pointer;
              transition: background .15s, border-color .15s;
            }
            .btn:hover { background: var(--hover); border-color: rgba(255,255,255,0.28); }
            .btn:active { background: var(--pressed); }
            .btn.icon { padding: 0; width: 38px; }
            .btn svg { width: 20px; height: 20px; fill: currentColor; }
            .time {
              font-size: 11px;
              color: var(--muted);
              min-width: 92px;
              font-variant-numeric: tabular-nums;
            }
            .volume { display: flex; align-items: center; gap: 8px; min-width: 120px; }
            .volume input[type=range] { width: 80px; }
            .menu-wrap { position: relative; }
            .menu {
              position: absolute;
              right: 0;
              bottom: calc(100% + 8px);
              min-width: 168px;
              background: var(--panel-2);
              border: 1px solid var(--border);
              border-radius: var(--radius);
              box-shadow: var(--shadow);
              padding: 6px;
              display: none;
              z-index: 10;
            }
            .menu.open { display: block; }
            .menu-item {
              display: flex;
              align-items: center;
              gap: 10px;
              width: 100%;
              padding: 8px 10px;
              border: 0;
              border-radius: 6px;
              background: transparent;
              color: var(--text);
              font: 500 12px var(--font);
              text-align: left;
              cursor: pointer;
            }
            .menu-item:hover { background: var(--hover); }
            .menu-item.active { background: rgba(0,120,215,0.22); color: #fff; }
            .menu-item svg { width: 14px; height: 14px; fill: var(--muted); flex-shrink: 0; }
            .menu-item.active svg { fill: var(--accent); }
          </style>
        </head>
        <body>
          <div class="stage">
            <div class="viewport">
              <video id="player" playsinline preload="metadata" src="%%VIDEO_URL%%"></video>
            </div>
            <div class="controls">
              <div class="progress-wrap">
                <input id="progress" type="range" min="0" max="1000" value="0" />
              </div>
              <div class="row">
                <button id="playBtn" class="btn icon" title="Play/Pause" type="button">
                  <svg id="playIcon" viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>
                  <svg id="pauseIcon" viewBox="0 0 24 24" style="display:none"><path d="M6 5h4v14H6zm8 0h4v14h-4z"/></svg>
                </button>
                <div id="time" class="time">0:00 / 0:00</div>
                <div class="spacer"></div>
                <div class="volume">
                  <button id="muteBtn" class="btn icon" title="Mute" type="button">
                    <svg id="volIcon" viewBox="0 0 24 24"><path d="M3 10v4h4l5 5V5L7 10H3zm13.5 2a4.5 4.5 0 0 0-2.5-4.03v8.05a4.5 4.5 0 0 0 2.5-4.02z"/></svg>
                    <svg id="muteIcon" viewBox="0 0 24 24" style="display:none"><path d="M3.63 3.63a1 1 0 0 1 1.41 0L9 7.59V5l5 5-2.18 2.18 5.77 5.77a1 1 0 0 1-1.41 1.41L3.63 5.04a1 1 0 0 1 0-1.41zM16 12.41 19.59 16H16v-3.59z"/></svg>
                  </button>
                  <input id="volume" type="range" min="0" max="100" value="100" />
                </div>
                <div class="menu-wrap">
                  <button id="speedBtn" class="btn" type="button">1×</button>
                  <div id="speedMenu" class="menu">
                    <button class="menu-item" data-speed="0.5" type="button">0.5×</button>
                    <button class="menu-item" data-speed="0.75" type="button">0.75×</button>
                    <button class="menu-item active" data-speed="1" type="button">1×</button>
                    <button class="menu-item" data-speed="1.25" type="button">1.25×</button>
                    <button class="menu-item" data-speed="1.5" type="button">1.5×</button>
                    <button class="menu-item" data-speed="2" type="button">2×</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
          <script>
            const player = document.getElementById('player');
            const playBtn = document.getElementById('playBtn');
            const playIcon = document.getElementById('playIcon');
            const pauseIcon = document.getElementById('pauseIcon');
            const progress = document.getElementById('progress');
            const timeEl = document.getElementById('time');
            const volume = document.getElementById('volume');
            const muteBtn = document.getElementById('muteBtn');
            const volIcon = document.getElementById('volIcon');
            const muteIcon = document.getElementById('muteIcon');
            const speedBtn = document.getElementById('speedBtn');
            const speedMenu = document.getElementById('speedMenu');
            let dragging = false;

            document.addEventListener('contextmenu', e => e.preventDefault());

            function fmt(sec) {
              if (!isFinite(sec) || sec < 0) sec = 0;
              const h = Math.floor(sec / 3600);
              const m = Math.floor((sec % 3600) / 60);
              const s = Math.floor(sec % 60);
              if (h > 0) return `${h}:${String(m).padStart(2,'0')}:${String(s).padStart(2,'0')}`;
              return `${m}:${String(s).padStart(2,'0')}`;
            }

            function updateTime() {
              const cur = player.currentTime || 0;
              const dur = player.duration || 0;
              timeEl.textContent = `${fmt(cur)} / ${fmt(dur)}`;
              if (!dragging && dur > 0) progress.value = Math.round((cur / dur) * 1000);
            }

            function setPlayingUI(playing) {
              playIcon.style.display = playing ? 'none' : 'block';
              pauseIcon.style.display = playing ? 'block' : 'none';
            }

            function togglePlay() {
              if (player.paused || player.ended) { player.play(); setPlayingUI(true); }
              else { player.pause(); setPlayingUI(false); }
            }

            playBtn.addEventListener('click', togglePlay);
            player.addEventListener('click', togglePlay);
            player.addEventListener('play', () => setPlayingUI(true));
            player.addEventListener('pause', () => setPlayingUI(false));
            player.addEventListener('timeupdate', updateTime);
            player.addEventListener('loadedmetadata', () => {
              if (player.videoWidth > 0 && player.videoHeight > 0)
                window.chrome.webview.postMessage('size:' + player.videoWidth + 'x' + player.videoHeight);
              updateTime();
              player.play().catch(() => {});
            });
            player.addEventListener('ended', () => setPlayingUI(false));
            player.addEventListener('error', () => {
              const code = player.error ? player.error.code : 0;
              window.chrome.webview.postMessage('error:' + code);
            });

            progress.addEventListener('input', () => {
              dragging = true;
              if (player.duration) player.currentTime = (progress.value / 1000) * player.duration;
              updateTime();
            });
            progress.addEventListener('change', () => dragging = false);

            volume.addEventListener('input', () => {
              player.volume = volume.value / 100;
              player.muted = player.volume === 0;
              updateMuteUI();
            });

            function updateMuteUI() {
              const muted = player.muted || player.volume === 0;
              volIcon.style.display = muted ? 'none' : 'block';
              muteIcon.style.display = muted ? 'block' : 'none';
            }

            muteBtn.addEventListener('click', () => {
              player.muted = !player.muted;
              if (!player.muted && player.volume === 0) { player.volume = 0.8; volume.value = 80; }
              updateMuteUI();
            });

            speedBtn.addEventListener('click', e => {
              e.stopPropagation();
              speedMenu.classList.toggle('open');
            });

            speedMenu.querySelectorAll('.menu-item').forEach(item => {
              item.addEventListener('click', e => {
                e.stopPropagation();
                const rate = parseFloat(item.dataset.speed);
                player.playbackRate = rate;
                speedBtn.textContent = rate + '×';
                speedMenu.querySelectorAll('.menu-item').forEach(x => x.classList.remove('active'));
                item.classList.add('active');
                speedMenu.classList.remove('open');
              });
            });

            document.addEventListener('click', () => speedMenu.classList.remove('open'));
            document.addEventListener('keydown', e => {
              if (e.code === 'Space') { e.preventDefault(); togglePlay(); }
              if (e.code === 'ArrowRight') player.currentTime = Math.min((player.duration || 0), player.currentTime + 5);
              if (e.code === 'ArrowLeft') player.currentTime = Math.max(0, player.currentTime - 5);
            });
          </script>
        </body>
        </html>
        """;
}
