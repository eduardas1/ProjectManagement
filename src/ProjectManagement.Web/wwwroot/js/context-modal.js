(function ($) {
    'use strict';
    var modal = $('#context-modal');
    if (!modal.length) return;
    var body = modal.find('[data-modal-body]');
    var error = modal.find('[data-modal-error]');
    var trigger = null, pending = false, load = null, generation = 0;
    var scopes = [], baseline = null, allowClose = false, verticalPosition = 0, accessLost = false;

    function localUrl(value) {
        var url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin) throw new Error('Use a local form address.');
        return url.href;
    }
    function scopeFor(element) {
        var root = $(element).closest('[data-modal-region]');
        var explicit = root.length > 0;
        if (!explicit) root = $(element).closest('main');
        if (!root.length) root = $('main[data-workspace-project]').first();
        var target = explicit ? root.find('[data-modal-list-url], [data-modal-read-url]').first() : root.find('#work-board[data-modal-list-url]').first();
        if (!explicit && !target.length) target = root.find('[data-workspace-content][data-modal-read-url]').first();
        if (!target.length) target = root.find('[data-modal-list-url], [data-modal-read-url]').first();
        if (!target.length) return null;
        var kind = target.attr('data-modal-read-view') || 'list';
        var notice = kind === 'workspace' ? root.children('[data-workspace-notice]') : root.find('[data-modal-notice]').first();
        if (!notice.length) notice = root.children('[data-workspace-notice]');
        var url = localUrl(target.attr('data-modal-list-url') || target.attr('data-modal-read-url'));
        var context = root.closest('[data-work-panel]').find('[data-preview-body]').attr('data-modal-read-url') || root.closest('main').find('[data-workspace-content]').attr('data-modal-read-url');
        return {root: root, target: target, notice: notice, kind: kind, url: url, accessUrl: root.attr('data-modal-access-url'), recoveryUrl: context ? localUrl(context) : url};
    }
    function snapshot() {
        var fields = body.find('form').serializeArray().filter(function (field) { return field.name !== '__RequestVerificationToken'; });
        body.find('input[type=file]').each(function () {
            fields.push({name: this.name, files: Array.from(this.files).map(function (file) { return [file.name, file.size, file.lastModified]; })});
        });
        return JSON.stringify(fields);
    }
    function dirty() { return baseline !== null && body.find('form[data-modal-dirty]').length && baseline !== snapshot(); }
    function focusForm() {
        var invalid = body.find('.validation-summary-errors').first();
        var field = body.find('.input-validation-error').filter(':visible:not(:disabled)').first();
        if (invalid.length) invalid.trigger('focus');
        else if (field.length) field.trigger('focus');
        else body.find('input:not([type=hidden]),select,textarea,button[type=submit],a').filter(':visible').first().trigger('focus');
    }
    function focusSource() {
        var accessLink = accessLost && scopes.length ? scopes[0].notice.find('a').first() : $();
        if (accessLink.length) { accessLink[0].focus({preventScroll: true}); return; }
        if (trigger && document.contains(trigger)) { trigger.focus({preventScroll: true}); return; }
        var root = scopes.length ? scopes[0].root : $('main');
        var target = scopes.length ? scopes[0].target : root;
        var heading = target.find('[data-modal-focus]').first();
        if (!heading.length) heading = target.find('h1').first();
        if (!heading.length) heading = root.find('[data-modal-focus],h1,h2').first();
        if (!heading.length) heading = root.prevAll('h1,h2,h3').first();
        if (heading.length) heading.attr('tabindex', '-1')[0].focus({preventScroll: true});
        else { var recovery = root.find('[data-modal-notice] a').first(); if (recovery.length) recovery[0].focus({preventScroll: true}); }
    }
    function showError(message) { error.text(message).removeClass('hidden'); }
    function failureMessage(xhr) {
        if (xhr.status === 401) return 'Your session has ended. Sign in again before trying this change.';
        if (xhr.status === 403 || xhr.status === 404) return 'This item or project is no longer available to your account.';
        if (xhr.status === 413) return 'The upload request is too large. Select one file no larger than 5 MiB.';
        if (xhr.status === 0) return 'The connection was interrupted. Check whether the change was saved before trying again.';
        return 'The form could not be loaded or saved. Your input has been kept. Please try again.';
    }
    function syncType() {
        var type = body.find('[name=Type]').val();
        if (type === undefined) return;
        function group(selector, visible) {
            var target = body.find(selector);
            visible = visible || target.find('.input-validation-error,.field-validation-error').length > 0;
            target.toggleClass('hidden', !visible).find('select').prop('disabled', !visible);
        }
        group('[data-work-parent]', type === '4');
        group('[data-work-epic]', ['0', '1', '2'].indexOf(type) !== -1);
    }
    function insertPartial(html, retainFiles) {
        var content = $($.parseHTML(html, document, false));
        var oldForm = body.find('form')[0], nextForm = content.filter('form').add(content.find('form'))[0];
        if (retainFiles && oldForm && nextForm && oldForm.action === nextForm.action) {
            $(oldForm).find('input[type=file]').each(function () {
                var original = this;
                if (!original.files.length) return;
                $(nextForm).find('input[type=file]').filter(function () { return this.name === original.name; }).first().replaceWith($(original).detach());
            });
        }
        body.empty().append(content); syncType();
    }
    function captureScopes() {
        verticalPosition = window.scrollY;
        scopes.forEach(function (scope) {
            var capture = scope.target.data('capture-scroll');
            scope.position = typeof capture === 'function' ? capture() : null;
            scope.scroll = scope.root.scrollTop();
            scope.hasBoard = scope.target.is('#work-board') || scope.target.find('#work-board').length > 0;
        });
    }
    function boardBusy(value) {
        var setter = $('#work-board').data('set-busy');
        if (typeof setter === 'function') setter(value);
    }
    function finish(lockBoard) {
        pending = false; modal.data('mutation-pending', false);
        body.find('[type=submit]').prop('disabled', accessLost);
        if (lockBoard) $('#work-board').data('requires-refresh', true);
        boardBusy(lockBoard);
        $(document).trigger('workboard:mutation-finished');
    }
    function refreshScopes(message, failed, item) {
        var remaining = scopes.length;
        var boardFresh = !scopes.some(function (scope) { return scope.hasBoard; });
        var vertical = verticalPosition;
        if (!remaining) { finish(false); return; }
        scopes.forEach(function (scope) {
            if (!document.contains(scope.target[0])) { complete(); return; }
            scope.notice.removeClass('hidden alert-info alert-success alert-danger').addClass(failed ? 'alert-danger' : 'alert-success').text(message);
            scope.target.attr('aria-busy', 'true');
            var headers = scope.kind === 'list' ? {} : {'X-Workboard-View': scope.kind};
            $.ajax({url: scope.url, method: 'GET', headers: headers, timeout: 20000}).done(function (html, status, xhr) {
                if (xhr.getResponseHeader('X-Workboard-Partial') !== scope.kind) { refreshFailed(); return; }
                apply(html);
            }).fail(function (xhr) {
                if (scope.kind === 'workspace' && xhr.status === 400 && xhr.getResponseHeader('X-Workboard-Partial') === 'workspace') apply(xhr.responseText);
                else refreshFailed(xhr);
            }).always(function () { scope.target.attr('aria-busy', 'false'); complete(); });
            function apply(html) {
                if (!document.contains(scope.target[0])) return;
                scope.target.empty().append($.parseHTML(html, document, false));
                if (scope.kind === 'workspace') $(document).trigger('workboard:workspace-updated');
                else if (scope.kind === 'list') $(document).trigger('workboard:list-updated', [scope.target, scope.position]);
                else $(document).trigger('workboard:detail-updated', [scope.target]);
                var restore = scope.target.data('restore-scroll');
                if (typeof restore === 'function') restore(scope.position);
                scope.root.scrollTop(scope.scroll);
                if (scope.hasBoard) boardFresh = true;
                if (item && item.workItemId && item.workItemUrl && !(scope.kind === 'preview' && scope.url === localUrl(item.workItemUrl)) && !scope.target.find('#work-' + item.workItemId + ', [data-work-id="' + item.workItemId + '"]').length) {
                    $('<a>').attr('href', localUrl(item.workItemUrl)).attr('data-work-preview', '').addClass('alert-link').text(' Open work item').appendTo(scope.notice);
                }
            }
            function refreshFailed(xhr) {
                if (scope.accessUrl && xhr && [401, 403, 404].indexOf(xhr.status) !== -1) {
                    accessLost = true; scope.target.empty();
                    scope.notice.removeClass('hidden alert-info alert-success').addClass('alert-danger').text('Your access to this view has changed. ');
                    $('<a>').attr('href', localUrl(scope.accessUrl)).addClass('alert-link').text('Open project').appendTo(scope.notice);
                    return;
                }
                var legacy = scope.kind === 'list' && !scope.hasBoard;
                var text = failed ? 'The current view could not be refreshed. Check its saved state before another change.' : legacy ? 'Your change was saved, but the list could not be refreshed. Reload this page to see it.' : 'Your change was saved, but the view could not be refreshed.';
                scope.notice.removeClass('hidden alert-info alert-success').addClass('alert-danger').empty().append($('<span>').text(text));
                $('<a>').attr('href', scope.recoveryUrl).addClass('alert-link').text(' Reload').appendTo(scope.notice);
            }
        });
        function complete() {
            remaining--;
            if (remaining) return;
            window.scrollTo(window.scrollX, vertical);
            finish(!boardFresh);
            if (!modal.hasClass('in')) focusSource();
        }
    }
    function loadForm(request) {
        var current = ++generation; accessLost = false;
        if (load) load.abort();
        body.text('Loading form…'); error.addClass('hidden').text(''); baseline = null;
        request.timeout = 20000;
        load = $.ajax(request).done(function (html, status, xhr) {
            if (current !== generation) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'modal') { body.empty(); showError('The server did not return the expected form. Close this dialog and try again.'); return; }
            insertPartial(html); baseline = snapshot(); focusForm();
        }).fail(function (xhr, status) {
            if (status === 'abort' || current !== generation) return;
            body.empty(); showError(failureMessage(xhr));
        }).always(function () { if (current === generation) load = null; });
    }
    function begin(element, title, request) {
        if (modal.hasClass('in') || pending || modal.data('mutation-pending') === true || $(element).closest('[data-context-region]').find('[data-modal-list-url][aria-busy=true]').length) return;
        var primary = scopeFor(element);
        if (!primary) return;
        trigger = element; scopes = [primary];
        if ($(element).closest('[data-work-panel]').length) {
            var background = scopeFor($('main[data-workspace-project]')[0]);
            if (background && background.target[0] !== primary.target[0]) scopes.push(background);
        }
        modal.find('#context-modal-title').text(title);
        modal.find('.modal-dialog').toggleClass('modal-lg', $(element).attr('data-modal-size') === 'large');
        modal.modal('show'); loadForm(request);
    }
    $(document).on('click', 'a[data-modal-form]', function (event) {
        if (!scopeFor(this)) return;
        event.preventDefault();
        if ($(this).attr('aria-disabled') === 'true') return;
        begin(this, $(this).attr('data-modal-title'), {url: localUrl(this.href), method: 'GET'});
    });
    $(document).on('submit', 'form[data-modal-open]', function (event) {
        if (!scopeFor(this)) return;
        event.preventDefault();
        begin($(this).find('[type=submit]')[0], $(this).attr('data-modal-title'), {url: localUrl(this.action), method: 'POST', data: $(this).serialize()});
    });
    modal.on('keydown', function (event) {
        if (event.which !== 9) return;
        var controls = modal.find('a[href],button,input,select,textarea,[tabindex]').filter(':visible:not(:disabled)').filter(function () { return this.tabIndex >= 0; });
        var first = controls[0], last = controls[controls.length - 1];
        if (!first) { event.preventDefault(); modal.trigger('focus'); return; }
        if (event.shiftKey && (document.activeElement === first || document.activeElement === modal[0])) { event.preventDefault(); $(last).trigger('focus'); }
        else if (!event.shiftKey && (document.activeElement === last || document.activeElement === modal[0])) { event.preventDefault(); $(first).trigger('focus'); }
    });
    modal.on('shown.bs.modal', focusForm);
    modal.on('change', '[name=Type]', syncType);
    modal.on('hide.bs.modal', function (event) {
        if (pending && !allowClose) { event.preventDefault(); return; }
        if (window.projectConfirmation.active) { event.preventDefault(); return; }
        if (!allowClose && dirty()) {
            event.preventDefault();
            window.projectConfirmation.ask('Discard unsaved changes?', modal).then(function (discard) {
                if (!discard || pending) return;
                allowClose = true; modal.modal('hide'); allowClose = false;
            });
            return;
        }
        generation++; if (load) load.abort(); load = null;
    });
    modal.on('hidden.bs.modal', function () { body.empty(); error.addClass('hidden').text(''); baseline = null; window.requestAnimationFrame(focusSource); });
    modal.on('click', '[data-modal-cancel]', function (event) { event.preventDefault(); modal.modal('hide'); });
    modal.on('click', '[data-modal-reload]', function (event) {
        event.preventDefault(); if (pending || window.projectConfirmation.active) return;
        var url = localUrl(this.href);
        var answer = dirty() ? window.projectConfirmation.ask('Discard unsaved changes?', modal) : Promise.resolve(true);
        answer.then(function (discard) { if (discard && !pending) loadForm({url: url, method: 'GET'}); });
    });
    modal.on('submit', 'form[data-modal-submit]', function (event) {
        event.preventDefault(); if (pending || accessLost) return;
        var form = $(this), multipart = form.attr('enctype') === 'multipart/form-data';
        var submitted = multipart ? new FormData(this) : form.serialize();
        captureScopes(); pending = true; modal.data('mutation-pending', true); boardBusy(true);
        error.addClass('hidden').text(''); form.find('[type=submit]').prop('disabled', true);
        $.ajax({url: localUrl(this.action), method: 'POST', data: submitted, processData: !multipart, contentType: multipart ? false : 'application/x-www-form-urlencoded; charset=UTF-8', timeout: multipart ? 60000 : 20000}).done(function (data) {
            if (!data || data.saved !== true) { showError('The server did not confirm this change. Check the saved list before trying again.'); refreshScopes('Check the current saved state.', true); return; }
            allowClose = true; modal.modal('hide'); allowClose = false;
            if (data.redirectUrl) { window.location.assign(localUrl(data.redirectUrl)); return; }
            refreshScopes(data.message || 'Change saved.', false, data);
        }).fail(function (xhr) {
            if ([400, 409, 503].indexOf(xhr.status) !== -1 && xhr.getResponseHeader('X-Workboard-Partial') === 'modal') { insertPartial(xhr.responseText, multipart); focusForm(); finish(false); }
            else if (xhr.status === 413 || (multipart && xhr.status === 400)) {
                var oversized = multipart && form.find('input[type=file]').toArray().some(function (input) { return Array.from(input.files).some(function (file) { return file.size > 5 * 1024 * 1024; }); });
                showError(oversized ? 'The upload request is too large. Select one file no larger than 5 MiB.' : failureMessage(xhr)); finish(false);
            }
            else { showError(failureMessage(xhr)); refreshScopes('Check the current saved state.', true); }
        });
    });
})(jQuery);
