(function ($) {
    'use strict';
    var reads = [];
    function localUrl(value) {
        var url = new URL(value, window.location.href);
        return url.origin === window.location.origin ? url.href : null;
    }
    function read(target, url, focus) {
        var current = (target.data('read-generation') || 0) + 1;
        var previous = target.data('read-request');
        target.data('read-generation', current);
        if (previous) previous.abort();
        var region = target.closest('[data-context-region]'), notice = region.children('[data-modal-notice]');
        target.attr('aria-busy', 'true');
        notice.removeClass('hidden alert-danger alert-success').addClass('alert-info').text('Loading…');
        var request = $.ajax({url: url, method: 'GET', headers: {'X-Workboard-View': 'list'}, timeout: 20000});
        target.data('read-request', request); reads.push({target: target, request: request});
        request.done(function (html, status, xhr) {
            if (!live()) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'list') { failed('The server did not return the expected list.'); return; }
            target.empty().append($.parseHTML(html, document, false)).attr('data-modal-list-url', url);
            notice.addClass('hidden').text('');
            if (focus) {
                var heading = region.prev('h2');
                if (!heading.length) heading = region.closest('main').find('h1').first();
                if (heading.length) {
                    heading.attr('tabindex', '-1')[0].focus({preventScroll: true});
                    var panel = region.closest('[data-work-panel]');
                    if (panel.length) panel.scrollTop(panel.scrollTop() + heading[0].getBoundingClientRect().top - panel[0].getBoundingClientRect().top - panel.find('.preview-toolbar').outerHeight() - 12);
                    else heading[0].scrollIntoView({block: 'start'});
                }
            }
        }).fail(function (xhr, status) {
            if (!live() || status === 'abort') return;
            failed(xhr.status === 401 ? 'Your session has ended. Sign in again to load this list.' : xhr.status === 404 ? 'This list is no longer available to your account.' : 'The list could not be loaded.');
        }).always(function () {
            reads = reads.filter(function (read) { return read.request !== request; });
            if (live()) { target.attr('aria-busy', 'false'); target.removeData('read-request'); }
        });
        function live() { return document.contains(target[0]) && target.data('read-generation') === current; }
        function failed(text) {
            notice.removeClass('hidden alert-info alert-success').addClass('alert-danger').text(text + ' ');
            $('<a>').attr('href', url).attr('data-context-link', '').addClass('alert-link').text('Retry').appendTo(notice);
            $('<a>').attr('href', url).addClass('alert-link').text(' Open full page').appendTo(notice);
        }
    }
    function initialize() {
        reads.slice().forEach(function (read) { if (!document.contains(read.target[0])) read.request.abort(); });
        $('[data-context-load]').each(function () {
            var target = $(this), url = localUrl(target.attr('data-modal-list-url'));
            target.removeAttr('data-context-load');
            if (url) read(target, url, false);
        });
    }
    $(document).on('click', 'a[data-context-link]', function (event) {
        if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        var target = $(this).closest('[data-context-region]').find('[data-modal-list-url]').first(), url = localUrl(this.href);
        if (!target.length || !url) return;
        event.preventDefault();
        if ($('#context-modal').hasClass('in') || $('#context-modal').data('mutation-pending') === true) return;
        read(target, url, true);
    });
    $(document).on('workboard:workspace-updated workboard:detail-updated', initialize);
    initialize();
})(jQuery);
