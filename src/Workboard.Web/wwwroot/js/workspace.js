(function ($) {
    'use strict';
    var panel = $('[data-work-panel]');
    if (!panel.length) return;
    var body = panel.find('[data-preview-body]');
    var error = panel.find('[data-preview-error]');
    var request = null;
    var generation = 0;
    var trigger = null;
    var source = null;
    var phone = window.matchMedia('(max-width: 767px)');

    function syncPhone() {
        var modal = phone.matches && !panel.hasClass('hidden');
        panel.attr('aria-modal', String(modal));
        $('.project-workspace').prop('inert', modal);
        $('body').toggleClass('preview-phone-open', modal);
    }
    function close() {
        generation++;
        if (request) request.abort();
        request = null;
        panel.addClass('hidden'); body.empty(); error.addClass('hidden').text('');
        syncPhone();
        if (trigger && document.contains(trigger)) trigger.focus({preventScroll: true});
        if (source) window.scrollTo(window.scrollX, source.scroll);
    }
    function local(value) {
        var url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin || !/^\/WorkItems\/Details\/\d+$/.test(url.pathname)) return null;
        return url;
    }
    function open(url, updateHistory) {
        var current = ++generation;
        if (request) request.abort();
        if (updateHistory) {
            var state = {workboardPreview: true, context: source.url, scroll: source.scroll};
            if (window.history.state && window.history.state.workboardPreview) window.history.replaceState(state, '', url);
            else window.history.pushState(state, '', url);
        }
        panel.removeClass('hidden'); error.addClass('hidden').text(''); body.text('Loading work item…');
        panel.find('[data-preview-full]').attr('href', url);
        panel.attr('aria-busy', 'true'); syncPhone(); panel.find('[data-preview-close]').trigger('focus');
        request = $.ajax({url: url, method: 'GET', headers: {'X-Workboard-View': 'preview'}}).done(function (html, status, xhr) {
            if (generation !== current) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'preview') { fail('The work item could not be displayed. Open its full page or close this panel.'); return; }
            body.empty().append($.parseHTML(html, document, false));
        }).fail(function (xhr, status) {
            if (status === 'abort' || generation !== current) return;
            fail(xhr.status === 401 ? 'Your session has ended. Sign in again to open this work item.' : 'This work item could not be loaded. Open its full page or close this panel.');
        }).always(function () { if (generation === current) { request = null; panel.attr('aria-busy', 'false'); } });
        function fail(text) { body.empty(); error.removeClass('hidden').text(text); }
    }
    $(document).on('click', 'a[data-work-preview]', function (event) {
        if (event.button || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || $('#context-modal').hasClass('in')) return;
        var url = local(this.href);
        if (!url) return;
        event.preventDefault();
        if (panel.hasClass('hidden')) { source = {url: window.location.href, scroll: window.scrollY}; trigger = this; }
        open(url.href, true);
    });
    function dismiss() {
        if (window.history.state && window.history.state.workboardPreview) window.history.back();
        else close();
    }
    panel.on('click', '[data-preview-close]', dismiss);
    panel.on('keydown', function (event) {
        if (event.key === 'Escape' && !$('#context-modal').hasClass('in')) { event.preventDefault(); dismiss(); }
        if (event.key !== 'Tab' || !phone.matches) return;
        var controls = panel.find('a[href],button,input,select,textarea,[tabindex]').filter(':visible:not(:disabled)').filter(function () { return this.tabIndex >= 0; });
        var first = controls[0], last = controls[controls.length - 1];
        if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
        else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
    });
    window.addEventListener('popstate', function (event) {
        if (event.state && event.state.workboardPreview) {
            source = {url: event.state.context, scroll: event.state.scroll};
            var url = local(window.location.href);
            if (url) open(url.href, false); else close();
        } else close();
    });
    phone.addEventListener('change', syncPhone);
    $('.project-navigation a').each(function () {
        if (new URL(this.href).pathname === window.location.pathname) $(this).parent().addClass('active').find('a').attr('aria-current', 'page');
    });
})(jQuery);
