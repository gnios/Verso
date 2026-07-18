// Interop mínimo para o editor (T64): índice do cursor em textarea/contenteditable
// e scroll suave até o segmento ativo durante playback (com suporte a lista virtualizada).
window.versoEditor = {
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
        if (el) {
            window.versoEditor.scrollIntoView(el);
        }
    },

    // Quando o item está fora do DOM (Virtualize), posiciona o scrollport pelo índice.
    scrollToSegmentIndex: function (index, itemHeight) {
        var el = document.querySelector('[data-testid="transcript-segments"]');
        if (!el || index < 0) {
            return;
        }

        var h = itemHeight > 0 ? itemHeight : 110;
        var top = Math.max(0, (index * h) - (el.clientHeight / 3));
        el.scrollTo({ top: top, behavior: 'smooth' });
    }
};
