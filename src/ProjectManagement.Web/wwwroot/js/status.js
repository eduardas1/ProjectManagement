(function ($) {
    'use strict';
    function ready() { $('[data-status-save]').addClass('hidden'); }
    function busy(value, lock) {
        $('#context-modal').data('mutation-pending', value);
        var board = $('#work-board');
        if (lock) board.data('requires-refresh', true);
        var setter = board.data('set-busy');
        if (typeof setter === 'function') setter(value || lock);
        if (!value) $(document).trigger('workboard:mutation-finished');
    }
    function scopes(form) {
        var targets = [form.closest('[data-modal-read-view=preview]')];
        if (form.closest('[data-work-panel]').length) targets.push($('main [data-workspace-content][data-modal-read-url]').first());
        return targets.filter(function (target) { return target.length; }).map(function (target) {
            var capture = target.find('#work-board').data('capture-scroll');
            return {target: target, url: target.attr('data-modal-read-url'), kind: target.attr('data-modal-read-view'),
                board: typeof capture === 'function' ? capture() : null, scroll: target.closest('[data-work-panel]').scrollTop()};
        });
    }
    function notice(form, message, reload) {
        var target = form.find('[data-status-notice]').empty().text(message + ' ');
        if (reload) $('<a>').attr('href', window.location.href).text('Reload current state').appendTo(target);
    }
    function refresh(reads, form, message) {
        var remaining = reads.length, failed = false, vertical = window.scrollY;
        if (!remaining) { notice(form, message, true); busy(false, true); return; }
        reads.forEach(function (scope) {
            $.ajax({url: scope.url, headers: {'X-Workboard-View': scope.kind}, timeout: 20000}).done(function (html, _, xhr) {
                if (!document.contains(scope.target[0])) return;
                if (xhr.getResponseHeader('X-Workboard-Partial') !== scope.kind) { failed = true; return; }
                scope.target.empty().append($.parseHTML(html, document, false));
                if (scope.kind === 'workspace') $(document).trigger('workboard:workspace-updated');
                else $(document).trigger('workboard:detail-updated', [scope.target]);
                var restore = scope.target.find('#work-board').data('restore-scroll');
                if (typeof restore === 'function') restore(scope.board);
                scope.target.closest('[data-work-panel]').scrollTop(scope.scroll);
                scope.target.find('[data-status-notice]').first().text(message);
            }).fail(function (xhr) {
                failed = true;
                if ([401,403,404].indexOf(xhr.status) !== -1 && document.contains(scope.target[0])) {
                    scope.target.empty().append($('<p>').attr('role', 'alert').text('Your access to this view has changed.'));
                }
            }).always(function () {
                if (--remaining) return;
                if (failed) {
                    var error = $('[data-workspace-notice]').first().removeClass('hidden').text(message + ' The view could not be refreshed. ');
                    $('<a>').attr('href', window.location.href).text('Reload current state').appendTo(error);
                    notice(form, message + ' Refresh failed.', true);
                } else {
                    var control = reads[0].target.find('[data-inline-status] select').first();
                    if (control.length) control[0].focus({preventScroll: true});
                }
                window.scrollTo(window.scrollX, vertical); ready(); busy(false, failed);
            });
        });
    }
    $(document).on('change', '[data-inline-status] select', function () { $(this).closest('form').trigger('submit'); });
    $(document).on('submit', '[data-inline-status]', function (event) {
        event.preventDefault();
        if ($('#context-modal').data('mutation-pending') === true || $('#context-modal').hasClass('in') || $('#work-board').data('mutation-pending') === true || $('#work-board').data('requires-refresh') === true) return;
        var form = $(this), reads = scopes(form), fields = form.serialize();
        form.find('select,button').prop('disabled', true); notice(form, 'Saving status…', false); busy(true, false);
        $.ajax({url: this.action, method: 'POST', data: fields, timeout: 20000}).done(function (result) {
            refresh(reads, form, result && result.saved === true ? 'Status saved.' : 'Check the current saved status.');
        }).fail(function (xhr) {
            if ([400,409,503].indexOf(xhr.status) !== -1 && xhr.getResponseHeader('X-Workboard-Partial') === 'modal') {
                var fragment = $($.parseHTML(xhr.responseText, document, false));
                var error = fragment.find('.validation-summary-errors').addBack('.validation-summary-errors').text().trim();
                notice(form, error || 'The status could not be changed.', xhr.status === 409);
                if (xhr.status !== 409) form.find('select,button').prop('disabled', false);
                busy(false, xhr.status === 409);
            } else refresh(reads, form, 'The response was interrupted. Check the current saved status.');
        });
    });
    $(document).on('workboard:detail-updated workboard:workspace-updated', ready);
    ready();
})(jQuery);
