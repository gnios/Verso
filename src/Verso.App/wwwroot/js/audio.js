// Interop do player HTML5 (substitui LibVLC): um <audio> oculto controlado pela
// PlayerBar Blazor via IJSRuntime → window.versoAudio.
window.versoAudio = (function () {
    var audio = document.createElement('audio');
    audio.style.display = 'none';
    audio.preload = 'metadata';
    document.documentElement.appendChild(audio);

    var dotNetRef = null;
    var loaded = false;

    function notifyPosition() {
        if (!dotNetRef) {
            return;
        }
        var seconds = isFinite(audio.currentTime) ? audio.currentTime : 0;
        dotNetRef.invokeMethodAsync('OnPositionChanged', seconds);
    }

    audio.addEventListener('timeupdate', notifyPosition);
    audio.addEventListener('ended', function () {
        notifyPosition();
        if (dotNetRef) {
            dotNetRef.invokeMethodAsync('OnEnded');
        }
    });
    audio.addEventListener('loadedmetadata', function () {
        if (dotNetRef) {
            var duration = isFinite(audio.duration) ? audio.duration : 0;
            dotNetRef.invokeMethodAsync('OnDurationChanged', duration);
        }
        notifyPosition();
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
            notifyPosition();
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
