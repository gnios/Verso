// Interop do player HTML5 (substitui LibVLC): um <audio> oculto controlado pela
// PlayerBar Blazor via IJSRuntime → window.versoAudio.
window.versoAudio = (function () {
    var audio = document.createElement('audio');
    audio.style.display = 'none';
    audio.preload = 'metadata';
    document.documentElement.appendChild(audio);

    var dotNetRef = null;
    var loaded = false;
    var throttleTimer = null;
    var THROTTLE_MS = 100;

    function emitPosition() {
        if (!dotNetRef) {
            return;
        }
        var seconds = isFinite(audio.currentTime) ? audio.currentTime : 0;
        dotNetRef.invokeMethodAsync('OnPositionChanged', seconds);
    }

    // timeupdate: coalesce; seek/ended/metadata: force (UI precisa refletir na hora).
    function notifyPosition(force) {
        if (force) {
            if (throttleTimer !== null) {
                clearTimeout(throttleTimer);
                throttleTimer = null;
            }
            emitPosition();
            return;
        }

        if (throttleTimer !== null) {
            return;
        }

        throttleTimer = setTimeout(function () {
            throttleTimer = null;
            emitPosition();
        }, THROTTLE_MS);
    }

    audio.addEventListener('timeupdate', function () {
        notifyPosition(false);
    });
    audio.addEventListener('ended', function () {
        notifyPosition(true);
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnEnded');
        }
    });
    audio.addEventListener('loadedmetadata', function () {
        if (dotNetRef) {
            var duration = isFinite(audio.duration) ? audio.duration : 0;
            dotNetRef.invokeMethodAsync('OnDurationChanged', duration);
        }
        notifyPosition(true);
    });

    return {
        bindDotNet: function (ref) {
            dotNetRef = ref;
        },

        load: function (url) {
            audio.pause();
            audio.removeAttribute('src');
            audio.load();
            audio.src = url;
            loaded = true;
            // Dispara fetch de metadata; OnDurationChanged chega via loadedmetadata.
        },

        unload: function () {
            audio.pause();
            audio.removeAttribute('src');
            audio.load();
            loaded = false;
        },

        play: function () {
            if (!loaded) {
                return Promise.resolve();
            }
            return audio.play().catch(function () { /* ignore */ });
        },

        pause: function () {
            audio.pause();
        },

        seek: function (seconds) {
            if (!isFinite(seconds)) {
                return;
            }
            try {
                audio.currentTime = Math.max(0, seconds);
            } catch (e) {
                // currentTime pode falhar se metadata ainda não carregou
            }
            notifyPosition(true);
        },

        setVolume: function (volume01) {
            audio.volume = Math.min(1, Math.max(0, volume01));
        },

        setRate: function (rate) {
            audio.playbackRate = rate > 0 ? rate : 1;
        },

        getDuration: function () {
            return isFinite(audio.duration) ? audio.duration : 0;
        },

        isPlaying: function () {
            return !audio.paused && !audio.ended && loaded;
        }
    };
})();
