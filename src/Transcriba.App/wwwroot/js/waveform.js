// Interop pontual e isolado (T66) — desenha a forma de onda mockada da tela de Gravação em
// <canvas>, portando fielmente o visual de `.rec-wave-bar` do protótipo
// (transcriba-v2-icons-transcriptions.html). Nenhuma lógica de animação/randomização vive aqui:
// os valores de altura e estado "ativo" de cada barra já são calculados em C# pelo
// `RecordingViewModel` (timer + `AnimateWaveform`); este módulo só materializa esses valores em
// pixels a cada chamada de `render`. Única exceção documentada de JS residual do app — ver seção
// "Risks" de `.specs/features/transcriba-desktop/design.md` (AD-005).
(function () {
    'use strict';

    // WeakMap em vez de dataset/atributos no elemento: evita vazar estado (ResizeObserver, cache
    // das últimas barras desenhadas) para o DOM e permite que o canvas seja coletado normalmente
    // quando o componente Razor é destruído, mesmo que `dispose` não seja chamado a tempo.
    var canvasState = new WeakMap();

    function readCssVariable(name, fallback) {
        var value = getComputedStyle(document.documentElement).getPropertyValue(name);
        return value && value.trim() ? value.trim() : fallback;
    }

    function resizeToDisplaySize(canvas) {
        var rect = canvas.getBoundingClientRect();
        var dpr = window.devicePixelRatio || 1;
        var width = Math.max(1, Math.round(rect.width * dpr));
        var height = Math.max(1, Math.round(rect.height * dpr));

        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }

        return dpr;
    }

    // Replica `border-radius:2px 2px 0 0` do `.rec-wave-bar` (cantos arredondados só no topo).
    function traceRoundedTopRect(ctx, x, y, width, height, radius) {
        var r = Math.max(0, Math.min(radius, width / 2, height));
        ctx.beginPath();
        ctx.moveTo(x, y + height);
        ctx.lineTo(x, y + r);
        ctx.arcTo(x, y, x + r, y, r);
        ctx.lineTo(x + width - r, y);
        ctx.arcTo(x + width, y, x + width, y + r, r);
        ctx.lineTo(x + width, y + height);
        ctx.closePath();
    }

    function draw(canvas, bars) {
        var ctx = canvas.getContext('2d');
        if (!ctx) {
            return;
        }

        var dpr = resizeToDisplaySize(canvas);
        var cssWidth = canvas.width / dpr;
        var cssHeight = canvas.height / dpr;

        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        ctx.clearRect(0, 0, cssWidth, cssHeight);

        if (!bars || !bars.length) {
            return;
        }

        var gap = 2; // mesmo `gap:2px` do `.rec-waveform` no protótipo/app.css
        var count = bars.length;
        var barWidth = Math.max(1, (cssWidth - gap * (count - 1)) / count);
        var accent = readCssVariable('--accent', '#2eaadc');
        var radius = 2;

        for (var i = 0; i < count; i++) {
            var bar = bars[i] || {};
            var height = Math.max(1, Math.min(Number(bar.height) || 0, cssHeight));
            var x = i * (barWidth + gap);
            var y = cssHeight - height;

            // `.rec-wave-bar{opacity:.5}` / `.rec-wave-bar.active{opacity:1}`.
            ctx.globalAlpha = bar.isActive ? 1 : 0.5;
            ctx.fillStyle = accent;
            traceRoundedTopRect(ctx, x, y, barWidth, height, radius);
            ctx.fill();
        }

        ctx.globalAlpha = 1;
    }

    window.transcribaWaveform = {
        init: function (canvas) {
            if (!canvas || canvasState.has(canvas)) {
                return;
            }

            var entry = { lastBars: [] };

            if (typeof ResizeObserver !== 'undefined') {
                entry.observer = new ResizeObserver(function () {
                    draw(canvas, entry.lastBars);
                });
                entry.observer.observe(canvas);
            }

            canvasState.set(canvas, entry);
            draw(canvas, entry.lastBars);
        },

        render: function (canvas, bars) {
            if (!canvas) {
                return;
            }

            var entry = canvasState.get(canvas) || { lastBars: [] };
            entry.lastBars = bars || [];
            canvasState.set(canvas, entry);
            draw(canvas, entry.lastBars);
        },

        dispose: function (canvas) {
            if (!canvas) {
                return;
            }

            var entry = canvasState.get(canvas);
            if (entry && entry.observer) {
                entry.observer.disconnect();
            }

            canvasState.delete(canvas);
        }
    };
})();
