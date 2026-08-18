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
    },

    shouldInterceptWordLikeKey: function (e, element) {
        if (!element || e.ctrlKey || e.metaKey || e.altKey) {
            return false;
        }

        var start = element.selectionStart;
        var end = element.selectionEnd;
        var len = element.value.length;
        var collapsed = start === end;
        var before = element.value.slice(0, start);
        var after = element.value.slice(end);
        var isFirstLine = before.indexOf('\n') === -1;
        var isLastLine = after.indexOf('\n') === -1;

        if (e.key === 'Enter' && !e.shiftKey) {
            return true;
        }
        if (e.key === 'Backspace' && collapsed && start === 0) {
            return true;
        }
        if (e.key === 'Delete' && collapsed && start === len) {
            return true;
        }
        if (e.key === 'ArrowLeft' && collapsed && start === 0) {
            return true;
        }
        if (e.key === 'ArrowRight' && collapsed && start === len) {
            return true;
        }
        if (e.key === 'ArrowUp' && isFirstLine) {
            return true;
        }
        if (e.key === 'ArrowDown' && isLastLine) {
            return true;
        }
        return false;
    },

    attachWordLikeKeys: function (element, dotNetRef) {
        if (!element || element._versoWordLikeAttached) {
            return;
        }

        var handler = function (e) {
            if (!window.versoEditor.shouldInterceptWordLikeKey(e, element)) {
                return;
            }

            e.preventDefault();
            var start = element.selectionStart;
            var end = element.selectionEnd;
            var before = element.value.slice(0, start);
            var after = element.value.slice(end);
            var lastNl = before.lastIndexOf('\n');
            dotNetRef.invokeMethodAsync('OnWordLikeKey', {
                key: e.key,
                shift: !!e.shiftKey,
                caretStart: start,
                caretEnd: end,
                textLength: element.value.length,
                isFirstLine: before.indexOf('\n') === -1,
                isLastLine: after.indexOf('\n') === -1,
                column: start - (lastNl + 1)
            });
        };

        element._versoWordLikeAttached = true;
        element._versoWordLikeHandler = handler;
        element.addEventListener('keydown', handler);
    },

    focusTextarea: function (element, caretIndex) {
        if (!element) {
            return false;
        }

        element.focus();
        var caret = Math.max(0, Math.min(caretIndex, element.value.length));
        element.setSelectionRange(caret, caret);
        return true;
    },

    focusSegment: function (segmentId, caretIndex) {
        var root = document.querySelector('[data-segment-id="' + segmentId + '"]');
        if (!root) {
            return false;
        }

        return window.versoEditor.focusTextarea(root.querySelector('textarea'), caretIndex);
    }
};
