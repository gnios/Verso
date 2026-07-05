// Interop mínimo para o editor (T64): índice do cursor em textarea/contenteditable
// e scroll suave até o segmento ativo durante playback.
window.transcribaEditor = {
    caretIndex: function (element) {
        if (!element) {
            return 0;
        }

        if (element.selectionStart != null) {
            return element.selectionStart;
        }

        var selection = window.getSelection();
        if (!selection || selection.rangeCount === 0) {
            return 0;
        }

        var range = selection.getRangeAt(0);
        if (!element.contains(range.startContainer)) {
            return 0;
        }

        var pre = range.cloneRange();
        pre.selectNodeContents(element);
        pre.setEnd(range.startContainer, range.startOffset);
        return pre.toString().length;
    },

    scrollIntoView: function (element) {
        if (element) {
            element.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    },

    scrollToSegmentById: function (segmentId) {
        var el = document.querySelector('[data-segment-id="' + segmentId + '"]');
        window.transcribaEditor.scrollIntoView(el);
    }
};
