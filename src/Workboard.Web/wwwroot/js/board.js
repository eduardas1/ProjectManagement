(function ($) {
    'use strict';
    var board = $('#work-board');
    var notice = $('[data-board-notice]');
    var busy = false;
    var sortables = [];
    var drag = null;

    function message(text, failed) {
        notice.removeClass('hidden alert-success alert-danger').addClass(failed ? 'alert-danger' : 'alert-success').text(text);
    }
    function setBusy(value) {
        busy = value;
        board.find('.kanban-board').toggleClass('is-saving', value).attr('aria-busy', String(value));
        sortables.forEach(function (sortable) { sortable.option('disabled', value); });
        board.find('[data-modal-form]').attr('aria-disabled', String(value));
        if (!value) $(document).trigger('workboard:mutation-finished');
    }
    function refresh(text, failed) {
        var horizontal = board.find('.board-scroll').scrollLeft();
        var vertical = window.scrollY;
        $.ajax({url: board.attr('data-modal-list-url'), method: 'GET'}).done(function (html, status, xhr) {
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'list') { refreshFailed(); return; }
            board.empty().append($.parseHTML(html, document, false));
            initialize();
            board.find('.board-scroll').scrollLeft(horizontal);
            window.scrollTo(window.scrollX, vertical);
            message(text, failed);
            setBusy(false);
        }).fail(refreshFailed);
        function refreshFailed() {
            message(text + ' The board could not be refreshed. Reload before making another change.', true);
            $('<a>').attr('href', board.attr('data-modal-list-url')).addClass('alert-link').text(' Reload board').appendTo(notice);
            setBusy(true);
            $(document).trigger('workboard:mutation-finished');
        }
    }
    function move(card, status) {
        if (busy || $('#context-modal').hasClass('in')) return;
        var url = new URL(card.getAttribute('data-status-url'), window.location.href);
        if (url.origin !== window.location.origin) return;
        setBusy(true);
        $(card).addClass('is-pending');
        var input = {
            ProjectId: board.find('[data-board-project]').attr('data-board-project'),
            WorkItemId: card.getAttribute('data-work-id'),
            ExpectedVersion: card.getAttribute('data-version'),
            Status: status,
            __RequestVerificationToken: board.find('[name=__RequestVerificationToken]').val()
        };
        $.ajax({url: url.href, method: 'POST', data: input}).done(function (data) {
            if (!data || data.saved !== true) {
                refresh('The server did not confirm the change. Check the current item status before trying again.', true);
                return;
            }
            refresh('Work status saved.', false);
        }).fail(function (xhr) {
            var text = 'The change could not be confirmed. Check the current item status before trying again.';
            if ([400, 409, 503].indexOf(xhr.status) !== -1 && xhr.getResponseHeader('X-Workboard-Partial') === 'modal') {
                var errors = $('<div>').append($.parseHTML(xhr.responseText, document, false)).find('.validation-summary-errors').text().trim();
                text = errors || 'The status change was rejected. Check the current item before trying again.';
            } else if (xhr.status === 401) text = 'Your session has ended. Sign in again before changing work.';
            else if (xhr.status === 403 || xhr.status === 404) text = 'This item or project is no longer available to your account.';
            refresh(text, true);
        });
    }
    function initialize() {
        sortables.forEach(function (sortable) { sortable.destroy(); });
        sortables = [];
        board = $('#work-board'); notice = $('[data-board-notice]'); busy = false;
        if (!board.length) return;
        if (typeof Sortable === 'undefined' || board.find('[data-board-archived]').attr('data-board-archived') === 'true') return;
        board.find('.kanban-board').attr('data-drag-ready', 'true');
        board.find('.kanban-items').each(function () {
            sortables.push(new Sortable(this, {
                group: 'work-status', sort: false, handle: '.card-drag', draggable: '.work-card',
                animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 120,
                delay: 180, delayOnTouchOnly: true, touchStartThreshold: 5,
                onStart: function (event) { drag = {parent: event.from, next: event.item.nextSibling}; },
                onMove: function (event) {
                    var target = event.to.getAttribute('data-status');
                    var allowed = (event.dragged.getAttribute('data-allowed') || '').split(',');
                    board.find('.kanban-column').removeClass('is-drop-target');
                    if (allowed.indexOf(target) === -1 || busy || $('#context-modal').hasClass('in')) return false;
                    $(event.to).closest('.kanban-column').addClass('is-drop-target');
                },
                onEnd: function (event) {
                    board.find('.kanban-column').removeClass('is-drop-target');
                    var target = event.to.getAttribute('data-status');
                    var source = event.from.getAttribute('data-status');
                    if (drag) drag.parent.insertBefore(event.item, drag.next && drag.next.parentNode === drag.parent ? drag.next : null);
                    drag = null;
                    if (target !== source && (event.item.getAttribute('data-allowed') || '').split(',').indexOf(target) !== -1) move(event.item, target);
                }
            }));
        });
    }
    $(document).on('click', '#work-board [data-modal-form]', function (event) {
        if (busy) { event.preventDefault(); event.stopImmediatePropagation(); }
    });
    $(document).on('workboard:list-updated workboard:workspace-updated', function () { initialize(); });
    initialize();
})(jQuery);
