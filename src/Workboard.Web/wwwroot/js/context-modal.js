(function ($) {
    'use strict';
    var modal = $('#context-modal');
    if (!modal.length) return;
    var body = modal.find('[data-modal-body]');
    var error = modal.find('[data-modal-error]');
    var notice = $('[data-modal-notice]');
    var list = $('[data-modal-list-url]');
    $(document).on('workboard:workspace-updated', function () {
        list = $('[data-modal-list-url]'); notice = $('[data-modal-notice]');
    });
    function focusList() { var heading = $('[data-modal-focus]')[0]; if (heading) heading.focus({preventScroll: true}); }
    var trigger = null;
    var pending = false;
    var load = null;
    var generation = 0;

    function localUrl(value) {
        var url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin) throw new Error('Use a local form address.');
        return url.href;
    }
    function showError(message) { error.text(message).removeClass('hidden'); }
    function failureMessage(xhr) {
        if (xhr.status === 401) return 'Your session has ended. Sign in again before trying this change.';
        if (xhr.status === 403 || xhr.status === 404) return 'This item or project is no longer available to your account.';
        if (xhr.status === 0) return 'The connection was interrupted. Check whether the change was saved before trying again.';
        return 'The form could not be loaded or saved. Your input has been kept. Please try again.';
    }
    function insertPartial(data) { body.empty().append($.parseHTML(data, document, false)); }
    function focusForm() {
        var invalid = body.find('.validation-summary-errors').first();
        if (invalid.length) invalid.trigger('focus');
        else body.find('input:not([type=hidden]),select,textarea,button[type=submit],a').filter(':visible').first().trigger('focus');
    }
    function begin(element, title, request) {
        if (modal.hasClass('in') || pending) return;
        trigger = element; generation++;
        var current = generation;
        body.text('Loading form…'); error.addClass('hidden').text('');
        modal.find('#context-modal-title').text(title);
        modal.modal('show');
        load = $.ajax(request).done(function (data, status, xhr) {
            if (current !== generation) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'modal') { body.empty(); showError('The server did not return the expected form. Close this dialog and try again.'); return; }
            insertPartial(data); focusForm();
        }).fail(function (xhr, status) {
            if (status === 'abort' || current !== generation) return;
            body.empty(); showError(failureMessage(xhr));
        }).always(function () { if (current === generation) load = null; });
    }
    $(document).on('click', 'a[data-modal-form]', function (event) {
        event.preventDefault();
        if ($(this).attr('aria-disabled') === 'true') return;
        begin(this, $(this).attr('data-modal-title'), {url: localUrl(this.href), method: 'GET'});
    });
    $(document).on('submit', 'form[data-modal-open]', function (event) {
        event.preventDefault();
        begin($(this).find('[type=submit]')[0], $(this).attr('data-modal-title'), {url: localUrl(this.action), method: 'POST', data: $(this).serialize()});
    });
    modal.on('keydown', function (event) {
        if (event.which !== 9) return;
        var controls = modal.find('a[href],button,input,select,textarea,[tabindex]').filter(':visible:not(:disabled)').filter(function () { return this.tabIndex >= 0; });
        var first = controls[0], last = controls[controls.length - 1];
        if (!first) { event.preventDefault(); modal.trigger('focus'); return; }
        if (event.shiftKey && (document.activeElement === first || document.activeElement === modal[0])) {
            event.preventDefault(); $(last).trigger('focus');
        } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === modal[0])) {
            event.preventDefault(); $(first).trigger('focus');
        }
    });
    modal.on('shown.bs.modal', focusForm);
    modal.on('hide.bs.modal', function (event) {
        if (pending) { event.preventDefault(); return; }
        generation++; if (load) load.abort(); load = null;
    });
    modal.on('hidden.bs.modal', function () {
        body.empty(); error.addClass('hidden').text('');
        if (trigger && document.contains(trigger)) trigger.focus({preventScroll: true});
        else focusList();
    });
    modal.on('click', '[data-modal-cancel]', function (event) { event.preventDefault(); modal.modal('hide'); });
    modal.on('submit', 'form[data-modal-submit]', function (event) {
        event.preventDefault(); if (pending) return;
        var form = $(this); var submitted = form.serialize();
        var targetList = list, targetNotice = notice;
        pending = true; error.addClass('hidden').text(''); form.find('[type=submit]').prop('disabled', true);
        $.ajax({url: localUrl(this.action), method: 'POST', data: submitted}).done(function (data) {
            if (!data || data.saved !== true) { showError('The server did not confirm this change. Check the saved list before trying again.'); return; }
            var position = window.scrollY;
            pending = false; modal.modal('hide');
            targetNotice.removeClass('hidden alert-danger').addClass('alert-success').text(data.message || 'Change saved.');
            $.ajax({url: localUrl(targetList.attr('data-modal-list-url')), method: 'GET'}).done(function (html, status, xhr) {
                if (xhr.getResponseHeader('X-Workboard-Partial') !== 'list') { refreshFailed(); return; }
                if (!targetList.length || !document.contains(targetList[0])) return;
                targetList.empty().append($.parseHTML(html, document, false));
                $(document).trigger('workboard:list-updated');
                window.scrollTo(window.scrollX, position);
                if (!trigger || !document.contains(trigger)) focusList();
            }).fail(refreshFailed);
            function refreshFailed() {
                targetNotice.removeClass('alert-success').addClass('alert-danger').text('Your change was saved, but the list could not be refreshed. Reload this page to see it.');
            }
        }).fail(function (xhr) {
            if ([400, 409, 503].indexOf(xhr.status) !== -1 && xhr.getResponseHeader('X-Workboard-Partial') === 'modal') {
                insertPartial(xhr.responseText); focusForm();
            } else showError(failureMessage(xhr));
        }).always(function () { pending = false; body.find('[type=submit]').prop('disabled', false); });
    });
})(jQuery);
