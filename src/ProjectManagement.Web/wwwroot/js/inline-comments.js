(function ($) {
    'use strict';
    var editor = null, pending = false, request = null, bypass = false;
    var memory = Object.create(null);
    var user = document.body.getAttribute('data-current-user-id');
    function local(value) {
        var url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin) throw new Error('Use a local comment address.');
        return url.href;
    }
    function key(url) { return 'projectmanagement.comment.' + user + '.' + new URL(url).pathname + new URL(url).search; }
    function loadDraft(url) {
        try { return JSON.parse(window.sessionStorage.getItem(key(url))) || memory[key(url)]; }
        catch (_) { return memory[key(url)]; }
    }
    function keepDraft(state) {
        var form = state.host.find('form');
        if (!form.length || !form.find('[name=Body]').length) return;
        var draft = {body: form.find('[name=Body]').val() || '', version: form.find('[name=ExpectedVersion]').val(), locked: state.locked};
        memory[key(state.url)] = draft;
        try { window.sessionStorage.setItem(key(state.url), JSON.stringify(draft)); }
        catch (_) { state.notice.text('Your draft is kept in this tab. Browser storage is unavailable.'); }
    }
    function erase(url) { delete memory[key(url)]; try { window.sessionStorage.removeItem(key(url)); } catch (_) {} }
    function dirty() {
        return editor && (editor.locked || (editor.host.find('[name=Body]').val() || '') !== editor.baseline);
    }
    function boardBusy(value) {
        var setter = $('#work-board').data('set-busy');
        if (typeof setter === 'function') setter(value);
    }
    function finishPending() {
        pending = false; $('#context-modal').data('mutation-pending', false);
        boardBusy(!!editor);
        $(document).trigger('workboard:mutation-finished');
    }
    function close(discard) {
        if (!editor || pending) return;
        var previous = editor;
        if (request) request.abort(); request = null;
        if (discard) erase(previous.url);
        editor = null; previous.host.remove(); boardBusy(false);
        if (document.contains(previous.trigger)) previous.trigger.focus({preventScroll: true});
        $(document).trigger('workboard:mutation-finished');
    }
    function confirmLeave() {
        if (pending) return Promise.resolve(false);
        if (!editor) return Promise.resolve(true);
        return (dirty() ? window.projectConfirmation.ask('Discard this comment draft?') : Promise.resolve(true)).then(function (discard) {
            if (discard && !pending) close(true);
            return discard && !pending;
        });
    }
    window.projectInlineComments = {
        get active() { return editor !== null; },
        get pending() { return pending; },
        contains: function (target) { return editor && target && target.length && target[0].contains(editor.host[0]); },
        confirmLeave: confirmLeave
    };
    function formLoaded(state, html) {
        if (editor !== state || !document.contains(state.host[0])) return;
        state.host.find('[data-inline-form]').empty().append($.parseHTML(html, document, false));
        var form = state.host.find('form'), saved = loadDraft(state.url);
        state.baseline = form.find('[name=Body]').val() || '';
        if (saved) {
            form.find('[name=Body]').val(saved.body);
            if (saved.version !== undefined) form.find('[name=ExpectedVersion]').val(saved.version);
            state.locked = saved.locked === true;
            state.notice.text(state.locked ? 'The last save needs checking. Your draft is retained; review the comments below before discarding it.' : 'Your previous draft has been restored.');
        }
        form.find('[type=submit]').prop('disabled', state.locked);
        form.find('textarea').first().trigger('focus');
    }
    function open(trigger) {
        if (pending || $('#context-modal').hasClass('in') || $('#context-modal').data('mutation-pending') === true) return;
        var url = local(trigger.href), list = $(trigger).closest('[data-comment-list]');
        if (!list.length) return;
        var state = {url: url, trigger: trigger, list: list, baseline: '', locked: false};
        state.host = $('<section>').attr('data-inline-comment-editor', '').addClass('inline-comment-editor');
        var heading = $('<div>').addClass('inline-comment-heading').appendTo(state.host);
        $('<h3>').text($(trigger).attr('data-inline-title')).appendTo(heading);
        $('<button type="button">').attr('data-inline-close', '').attr('aria-label', 'Close comment editor').addClass('btn btn-default').text('×').appendTo(heading);
        state.notice = $('<p>').attr('role', 'status').appendTo(state.host);
        $('<div>').attr('data-inline-form', '').text('Loading comment…').appendTo(state.host);
        var article = $(trigger).closest('article');
        if (article.length) state.host.appendTo(article.find('.panel-body').first());
        else state.host.insertAfter(list.children('p').first());
        editor = state; boardBusy(true);
        request = $.ajax({url: url, headers: {'X-Workboard-View': 'inline-comment'}, timeout: 20000});
        request.done(function (html, _, xhr) {
            if (editor !== state) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'inline-comment') { state.notice.text('The comment form could not be loaded.'); return; }
            formLoaded(state, html);
        }).fail(function (xhr, status) {
            if (editor !== state || status === 'abort') return;
            var saved = loadDraft(state.url);
            state.host.find('[data-inline-form]').empty();
            state.notice.text('This comment is unavailable.');
            if (saved) {
                $('<textarea readonly>').addClass('form-control').val(saved.body).attr('aria-label', 'Retained comment draft').appendTo(state.host);
                state.locked = true;
            }
            $('<button type="button">').attr('data-inline-close', '').addClass('btn btn-default').text('Close editor').appendTo(state.host);
        }).always(function () { if (editor === state) request = null; });
    }
    $(document).on('click', 'a[data-inline-comment]', function (event) {
        if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        event.preventDefault(); var trigger = this;
        confirmLeave().then(function (leave) { if (leave) open(trigger); });
    });
    $(document).on('input', '[data-inline-comment-editor] textarea', function () { if (editor && !pending) keepDraft(editor); });
    $(document).on('click', '[data-inline-comment-editor] [data-modal-cancel], [data-inline-close]', function (event) {
        event.preventDefault(); confirmLeave();
    });
    $(document).on('click', '[data-inline-comment-editor] [data-modal-reload]', function (event) {
        event.preventDefault(); var trigger = editor && editor.trigger;
        confirmLeave().then(function (leave) { if (leave && trigger && document.contains(trigger)) open(trigger); });
    });
    function refresh(state, message, retain) {
        var target = state.list.closest('[data-modal-list-url]');
        var url = target.attr('data-modal-list-url');
        if (!url || !document.contains(target[0])) { state.notice.text(message + ' Reload to check the saved comments.'); finishPending(); return; }
        $.ajax({url: local(url), headers: {'X-Workboard-View': 'list'}, timeout: 20000}).done(function (html, _, xhr) {
            if (editor !== state || !document.contains(target[0])) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'list') { state.notice.text(message + ' The comments could not be refreshed.'); return; }
            var form = retain ? state.host.detach() : null;
            target.empty().append($.parseHTML(html, document, false));
            if (retain) {
                state.list = target.find('[data-comment-list]');
                state.host.prependTo(state.list);
                state.notice.text(message);
            } else {
                erase(state.url); editor = null;
                target.find('a[data-inline-comment]').first().trigger('focus');
                target.closest('[data-context-region]').children('[data-modal-notice]').removeClass('hidden alert-danger alert-info').addClass('alert-success').text(message);
            }
        }).fail(function (xhr) {
            if (editor !== state) return;
            if ([401,403,404].indexOf(xhr.status) !== -1) {
                var saved = loadDraft(state.url);
                state.host.detach().empty();
                var root = target.closest('[data-modal-read-view=preview]');
                (root.length ? root : target).empty().append(state.host);
                state.notice = $('<p>').attr('role', 'status').text('Your access has changed. Your comment draft is retained here.').appendTo(state.host);
                $('<textarea readonly>').attr('aria-label', 'Retained comment draft').addClass('form-control').val(saved ? saved.body : '').appendTo(state.host);
                $('<button type="button">').attr('data-inline-close', '').addClass('btn btn-default').text('Close editor').appendTo(state.host);
            } else state.notice.text(message + ' Comments could not be refreshed. Reload to check before another save.');
        }).always(finishPending);
    }
    $(document).on('submit', '[data-inline-comment-editor] form', function (event) {
        event.preventDefault();
        if (!editor || pending || editor.locked) return;
        var state = editor, form = $(this), fields = form.serialize();
        keepDraft(state); pending = true; $('#context-modal').data('mutation-pending', true); boardBusy(true);
        state.notice.text('Saving comment…'); form.find('[type=submit]').prop('disabled', true); form.find('textarea').prop('readonly', true);
        $.ajax({url: local(this.action), method: 'POST', data: fields, headers: {'X-Workboard-View': 'inline-comment'}, timeout: 20000})
        .done(function (data) {
            state.locked = true; keepDraft(state);
            refresh(state, data && data.saved === true ? (data.message || 'Comment saved.') : 'Check the comments before another save. Your draft is retained.', !(data && data.saved === true));
        }).fail(function (xhr) {
            if ([400,409,503].indexOf(xhr.status) !== -1 && xhr.getResponseHeader('X-Workboard-Partial') === 'inline-comment') {
                var baseline = state.baseline;
                formLoaded(state, xhr.responseText); state.baseline = baseline;
                if (xhr.status === 409) {
                    state.notice.text('This comment changed elsewhere. Your draft and submitted version are retained. Reload only after copying or discarding your draft.');
                    state.locked = true; state.host.find('[type=submit]').prop('disabled', true); keepDraft(state);
                }
                finishPending();
            } else {
                state.locked = true; keepDraft(state);
                refresh(state, 'The save result needs checking. Your draft is retained and will not be sent again automatically.', true);
            }
        });
    });
    document.addEventListener('click', function (event) {
        if (bypass) { bypass = false; return; }
        if (!editor || editor.host[0].contains(event.target) || event.defaultPrevented || event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        var link = event.target.closest('a[href]');
        if (!link || link.target || link.hasAttribute('download')) return;
        event.preventDefault(); event.stopImmediatePropagation();
        confirmLeave().then(function (leave) { if (leave && document.contains(link)) { bypass = true; link.click(); } });
    }, true);
    document.addEventListener('submit', function (event) {
        if (bypass) { bypass = false; return; }
        if (!editor || editor.host[0].contains(event.target)) return;
        event.preventDefault(); event.stopImmediatePropagation();
        var form = event.target, submitter = event.submitter;
        confirmLeave().then(function (leave) { if (leave && document.contains(form)) { bypass = true; form.requestSubmit(submitter || undefined); } });
    }, true);
    window.addEventListener('beforeunload', function (event) {
        if (editor) keepDraft(editor);
        if (dirty() || pending) { event.preventDefault(); event.returnValue = ''; }
    });
})(jQuery);
