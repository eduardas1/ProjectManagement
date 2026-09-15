(function ($) {
    'use strict';
    var board = $('#work-board');
    var notice = $('[data-board-notice]');
    var busy = false;
    var sortables = [];
    var drag = null;
    var layoutObserver = null;
    var layoutFrame = 0;

    function captureScroll() {
        var state = {project: board.find('[data-board-project]').attr('data-board-project'), horizontal: board.find('.board-scroll').scrollLeft(), columns: {}};
        board.find('.kanban-items').each(function () { state.columns[this.getAttribute('data-status')] = this.scrollTop; });
        return state;
    }
    function restoreScroll(state) {
        if (!state || state.project !== board.find('[data-board-project]').attr('data-board-project')) return;
        if (Number.isFinite(state.horizontal)) board.find('.board-scroll').scrollLeft(Math.max(0, state.horizontal));
        board.find('.kanban-items').each(function () {
            var value = state.columns && state.columns[this.getAttribute('data-status')];
            if (Number.isFinite(value)) this.scrollTop = Math.max(0, value);
        });
    }
    function sizeBoard() {
        layoutFrame = 0;
        var scroll = board.find('.board-scroll')[0];
        if (!scroll) return;
        var top = scroll.getBoundingClientRect().top + window.scrollY;
        var main = board.closest('main')[0];
        var outer = window.getComputedStyle(scroll);
        var bottom = (main ? parseFloat(window.getComputedStyle(main).paddingBottom) : 0) + parseFloat(outer.paddingBottom) + (scroll.offsetHeight - scroll.clientHeight) + 8;
        var height = Math.max(260, Math.floor((window.visualViewport ? window.visualViewport.height : window.innerHeight) - top - bottom)) + 'px';
        if (scroll.style.getPropertyValue('--column-height') !== height) scroll.style.setProperty('--column-height', height);
    }
    function scheduleLayout() {
        if (!layoutFrame) layoutFrame = window.requestAnimationFrame(sizeBoard);
    }

    function message(text, failed) {
        notice.removeClass('hidden alert-success alert-danger').addClass(failed ? 'alert-danger' : 'alert-success').text(text);
    }
    function setBusy(value, notify) {
        busy = value;
        board.find('.kanban-board').toggleClass('is-saving', value).attr('aria-busy', String(value));
        sortables.forEach(function (sortable) { sortable.option('disabled', value); });
        board.find('[data-modal-form]').attr('aria-disabled', String(value));
        board.find('.card-drag').prop('disabled', value);
        if (!value && notify !== false) $(document).trigger('workboard:mutation-finished');
    }
    function refresh(text, failed, item) {
        var position = captureScroll();
        var vertical = window.scrollY;
        $.ajax({url: board.attr('data-modal-list-url'), method: 'GET'}).done(function (html, status, xhr) {
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'list') { refreshFailed(); return; }
            board.empty().append($.parseHTML(html, document, false));
            message(text, failed);
            if (item && !board.find('#work-' + item.id).length) $('<a>').attr('href', item.url).attr('data-work-preview', '').addClass('alert-link').text(' Open work item').appendTo(notice);
            initialize();
            restoreScroll(position);
            window.scrollTo(window.scrollX, vertical);
            board.data('mutation-pending', false);
            setBusy(false);
        }).fail(refreshFailed);
        function refreshFailed() {
            message(text + ' The board could not be refreshed. Reload before making another change.', true);
            $('<a>').attr('href', board.attr('data-modal-list-url')).addClass('alert-link').text(' Reload board').appendTo(notice);
            board.data('requires-refresh', true); setBusy(true); board.data('mutation-pending', false);
            $(document).trigger('workboard:mutation-finished');
        }
    }
    function move(card, status) {
        if (busy || $('#context-modal').hasClass('in') || $('#context-modal').data('mutation-pending') === true) return;
        var url = new URL(card.getAttribute('data-status-url'), window.location.href);
        if (url.origin !== window.location.origin) return;
        board.data('mutation-pending', true); setBusy(true);
        var item = {id: card.getAttribute('data-work-id'), url: $(card).find('[data-work-preview]').first().attr('href')};
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
            refresh('Work status saved.', false, item);
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
    function dropColumn(event) {
        var original = event.originalEvent;
        var point = original && ((original.changedTouches && original.changedTouches[0]) || original);
        if (!point || !Number.isFinite(point.clientX) || !Number.isFinite(point.clientY)) return null;
        var previous = event.item.style.pointerEvents;
        event.item.style.pointerEvents = 'none';
        var hit = document.elementFromPoint(point.clientX, point.clientY);
        event.item.style.pointerEvents = previous;
        var column = $(hit).closest('.kanban-column');
        return column.length && board[0].contains(column[0]) ? column.attr('data-column') : null;
    }
    function initialize() {
        if (layoutObserver) layoutObserver.disconnect();
        if (layoutFrame) window.cancelAnimationFrame(layoutFrame);
        layoutObserver = null; layoutFrame = 0;
        sortables.forEach(function (sortable) { sortable.destroy(); });
        sortables = [];
        board = $('#work-board'); notice = $('[data-board-notice]'); busy = false;
        if (!board.length) return;
        board.data('requires-refresh', false).data('capture-scroll', captureScroll).data('restore-scroll', restoreScroll).data('set-busy', function (value) { setBusy(value || board.data('requires-refresh') === true, false); });
        sizeBoard();
        if (window.ResizeObserver) {
            layoutObserver = new ResizeObserver(scheduleLayout);
            $('.workspace-heading, [data-board-notice], [data-workspace-notice], .project-sidebar, .navbar').each(function () { layoutObserver.observe(this); });
        }
        if (typeof Sortable === 'undefined' || board.find('[data-board-archived]').attr('data-board-archived') === 'true') return;
        board.find('.kanban-board').attr('data-drag-ready', 'true');
        board.find('.kanban-items').each(function () {
            sortables.push(new Sortable(this, {
                group: 'work-status', sort: false, handle: '.card-drag', draggable: '.work-card', fallbackOnBody: true, forceFallback: true,
                animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 0 : 120,
                delay: 180, delayOnTouchOnly: true, touchStartThreshold: 5,
                onStart: function (event) { drag = {parent: event.from, next: event.item.nextSibling, position: captureScroll()}; },
                onMove: function (event) {
                    var target = event.to.getAttribute('data-status');
                    var allowed = (event.dragged.getAttribute('data-allowed') || '').split(',');
                    board.find('.kanban-column').removeClass('is-drop-target');
                    if (allowed.indexOf(target) === -1 || busy || $('#context-modal').hasClass('in') || $('#context-modal').data('mutation-pending') === true) return false;
                    $(event.to).closest('.kanban-column').addClass('is-drop-target');
                },
                onEnd: function (event) {
                    board.find('.kanban-column').removeClass('is-drop-target');
                    var target = dropColumn(event);
                    var source = event.from.getAttribute('data-status');
                    if (drag) {
                        var position = captureScroll();
                        position.columns[source] = drag.position.columns[source];
                        drag.parent.insertBefore(event.item, drag.next && drag.next.parentNode === drag.parent ? drag.next : null);
                        restoreScroll(position);
                    }
                    drag = null;
                    if (target !== source && (event.item.getAttribute('data-allowed') || '').split(',').indexOf(target) !== -1) move(event.item, target);
                }
            }));
        });
        if ($('#context-modal').data('mutation-pending') === true) setBusy(true, false);
    }
    $(document).on('click', '#work-board [data-modal-form]', function (event) {
        if (busy) { event.preventDefault(); event.stopImmediatePropagation(); }
    });
    $(document).on('workboard:list-updated', function (event, target, position) {
        if (!target || !target.is('#work-board')) return;
        initialize(); restoreScroll(position);
    });
    $(document).on('workboard:workspace-updated', initialize);
    $(window).on('resize', scheduleLayout);
    if (window.visualViewport) window.visualViewport.addEventListener('resize', scheduleLayout);
    initialize();
})(jQuery);
