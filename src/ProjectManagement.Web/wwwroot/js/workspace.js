(function ($) {
    'use strict';
    var panel = $('[data-work-panel]');
    if (!panel.length) return;
    var body = panel.find('[data-preview-body]');
    var error = panel.find('[data-preview-error]');
    var main = $('main[data-workspace-project]');
    var content = main.find('[data-workspace-content]');
    var notice = main.find('[data-workspace-notice]');
    var request = null, navigation = null;
    var generation = 0, navigationGeneration = 0;
    var trigger = null, source = null;
    var contentUrl = window.location.href;
    var waitingForHistory = false;
    var contextDialogOpen = false;
    var viewStates = Object.create(null);

    function mutationPending() {
        return contextDialogOpen || $('#context-modal').hasClass('in') || $('#context-modal').data('mutation-pending') === true || $('#work-board').data('mutation-pending') === true;
    }
    function captureView() {
        var capture = $('#work-board').data('capture-scroll');
        return {scroll: window.scrollY, boardScroll: typeof capture === 'function' ? capture() : null};
    }
    function restoreBoard(state) {
        var restore = $('#work-board').data('restore-scroll');
        if (typeof restore === 'function') restore(state);
    }
    var phone = window.matchMedia('(max-width: 767px)');

    function workspaceUrl(value) {
        var url = new URL(value, window.location.href);
        var supported = /^\/WorkItems(?:\/(?:Index|Backlog|Cancelled|Board))?$/.test(url.pathname)
            || /^\/(?:Search|Reports)(?:\/Index)?$/.test(url.pathname);
        return url.origin === window.location.origin && supported
            && url.searchParams.get('projectId') === main.attr('data-workspace-project') ? url : null;
    }
    function setActive(url) {
        $('.project-navigation li').removeClass('active').find('a').removeAttr('aria-current');
        $('.project-navigation a').each(function () {
            if (new URL(this.href).pathname === new URL(url).pathname) $(this).parent().addClass('active').find('a').attr('aria-current', 'page');
        });
    }
    function syncPhone() {
        var visible = !panel.hasClass('hidden');
        var modal = phone.matches && visible;
        panel.attr('aria-modal', String(modal && !contextDialogOpen)).prop('inert', visible && contextDialogOpen);
        $('.project-workspace').prop('inert', modal);
        $('body').toggleClass('preview-phone-open', modal);
    }
    function close() {
        generation++;
        if (request) request.abort();
        request = null;
        panel.addClass('hidden'); body.empty(); error.addClass('hidden').text('');
        $(document).trigger('workboard:detail-updated', [body]);
        syncPhone();
        if (trigger && document.contains(trigger)) trigger.focus({preventScroll: true});
        else { var heading = content.find('h1').first(); if (heading.length) heading.attr('tabindex', '-1')[0].focus({preventScroll: true}); }
        if (source) window.scrollTo(window.scrollX, source.scroll);
    }
    function cancelNavigation() {
        navigationGeneration++;
        if (navigation) navigation.abort();
        navigation = null; content.attr('aria-busy', 'false'); notice.addClass('hidden');
    }
    function rememberPosition() {
        var saved = captureView();
        viewStates[contentUrl] = saved;
        if (window.history.state && window.history.state.workboardPreview) {
            var preview = Object.assign({}, window.history.state, saved);
            window.history.replaceState(preview, '', window.location.href);
            if (source) { source.scroll = saved.scroll; source.boardScroll = saved.boardScroll; }
        } else if (workspaceUrl(window.location.href)) window.history.replaceState(Object.assign({workboardWorkspace: true}, saved), '', window.location.href);
    }
    function navigate(url, push, scroll, after, boardScroll) {
        var target = workspaceUrl(url);
        if (!target) { window.location.assign(url); return; }
        if (push) rememberPosition();
        cancelNavigation();
        var current = navigationGeneration;
        content.attr('aria-busy', 'true');
        notice.removeClass('hidden alert-danger').addClass('alert-info').text('Loading work…');
        navigation = $.ajax({url: target.href, method: 'GET', headers: {'X-Workboard-View': 'workspace'}}).done(function (html, status, xhr) {
            if (current !== navigationGeneration) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'workspace') { fail('The server did not return the expected project view.'); return; }
            apply(html);
        }).fail(function (xhr, status) {
            if (status === 'abort' || current !== navigationGeneration) return;
            if (xhr.status === 400 && xhr.getResponseHeader('X-Workboard-Partial') === 'workspace') { apply(xhr.responseText); return; }
            fail(xhr.status === 401 ? 'Your session has ended. Sign in again to continue.' : 'The project view could not be loaded. Your current view has been kept.');
        }).always(function () { if (current === navigationGeneration) { navigation = null; content.attr('aria-busy', 'false'); } });
        function apply(html) {
            var replacePreview = window.history.state && window.history.state.workboardPreview;
            close(); source = null; trigger = null;
            content.empty().append($.parseHTML(html, document, false));
            contentUrl = target.href;
            content.attr('data-modal-read-url', contentUrl).attr('data-modal-read-view', 'workspace');
            if (push) {
                if (replacePreview) window.history.replaceState({workboardWorkspace: true, scroll: 0, boardScroll: viewStates[target.href] ? viewStates[target.href].boardScroll : null}, '', target.href);
                else window.history.pushState({workboardWorkspace: true, scroll: 0, boardScroll: viewStates[target.href] ? viewStates[target.href].boardScroll : null}, '', target.href);
            }
            var heading = content.find('h1').first();
            if (heading.length) document.title = heading.text() + ' - Project Management System';
            setActive(target.href); notice.addClass('hidden');
            if (phone.matches) $('#project-navigation').collapse('hide');
            $(document).trigger('workboard:workspace-updated');
            restoreBoard(boardScroll || (viewStates[target.href] ? viewStates[target.href].boardScroll : null));
            window.scrollTo(window.scrollX, scroll || 0);
            if (heading.length && !after) heading.attr('tabindex', '-1')[0].focus({preventScroll: true});
            if (after) after();
        }
        function fail(text) {
            notice.removeClass('hidden alert-info').addClass('alert-danger').text(text + ' ');
            $('<a>').attr('href', target.href).addClass('alert-link').text('Open full page').appendTo(notice);
        }
    }
    function localItem(value) {
        var url = new URL(value, window.location.href);
        if (url.origin !== window.location.origin || !/^\/WorkItems\/Details\/\d+$/.test(url.pathname)) return null;
        return url;
    }
    function open(url, updateHistory) {
        cancelNavigation();
        var current = ++generation;
        if (request) request.abort();
        if (updateHistory) {
            source.boardScroll = captureView().boardScroll;
            var state = {workboardPreview: true, context: source.url, scroll: source.scroll, boardScroll: source.boardScroll};
            if (window.history.state && window.history.state.workboardPreview) window.history.replaceState(state, '', url);
            else { rememberPosition(); window.history.pushState(state, '', url); }
        }
        panel.removeClass('hidden'); panel.find('[data-modal-notice]').addClass('hidden').text(''); error.addClass('hidden').text(''); body.attr('data-modal-read-url', url).attr('data-modal-read-view', 'preview').text('Loading work item…');
        panel.find('[data-preview-full]').attr('href', url);
        panel.attr('aria-busy', 'true'); syncPhone(); panel.find('[data-preview-close]').trigger('focus');
        request = $.ajax({url: url, method: 'GET', headers: {'X-Workboard-View': 'preview'}}).done(function (html, status, xhr) {
            if (generation !== current) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'preview') { fail('The work item could not be displayed. Open its full page or close this panel.'); return; }
            body.empty().append($.parseHTML(html, document, false));
            $(document).trigger('workboard:detail-updated', [body]);
        }).fail(function (xhr, status) {
            if (status === 'abort' || generation !== current) return;
            fail(xhr.status === 401 ? 'Your session has ended. Sign in again to open this work item.' : 'This work item could not be loaded. Open its full page or close this panel.');
        }).always(function () { if (generation === current) { request = null; panel.attr('aria-busy', 'false'); } });
        function fail(text) { body.empty(); error.removeClass('hidden').text(text); }
    }
    function ordinary(event) { return !event.button && !event.ctrlKey && !event.metaKey && !event.shiftKey && !event.altKey; }
    $(document).on('click', 'a[data-work-preview]', function (event) {
        if (!ordinary(event)) return;
        var url = localItem(this.href);
        if (!url) return;
        event.preventDefault();
        if (mutationPending()) return;
        if (panel.hasClass('hidden')) { source = Object.assign({url: contentUrl}, captureView()); trigger = this; }
        open(url.href, true);
    });
    $(document).on('click', 'a[data-workspace-link]', function (event) {
        if (!ordinary(event) || !workspaceUrl(this.href)) return;
        event.preventDefault();
        if (mutationPending() || (window.workboardDirty && !window.workboardDirty.confirmLeave())) return;
        navigate(this.href, true, 0);
    });
    $(document).on('submit', 'form[data-workspace-get]', function (event) {
        var url = new URL(this.action, window.location.href);
        url.search = $(this).serialize();
        if (!workspaceUrl(url.href)) return;
        event.preventDefault();
        if (mutationPending() || (window.workboardDirty && !window.workboardDirty.confirmLeave())) return;
        navigate(url.href, true, 0);
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
    function restoreHistory(state) {
        cancelNavigation();
        if (state && state.workboardPreview) {
            var item = localItem(window.location.href);
            if (!item) { close(); return; }
            var context = viewStates[state.context] || state;
            function preview() { source = {url: state.context, scroll: context.scroll, boardScroll: context.boardScroll}; open(item.href, false); }
            if (contentUrl !== state.context) navigate(state.context, false, context.scroll, preview, context.boardScroll);
            else preview();
        } else if (contentUrl === window.location.href) {
            close(); var saved = viewStates[contentUrl] || state;
            if (saved) { restoreBoard(saved.boardScroll); window.scrollTo(window.scrollX, saved.scroll || 0); }
        } else if (workspaceUrl(window.location.href)) {
            var savedView = viewStates[window.location.href] || state;
            navigate(window.location.href, false, savedView ? savedView.scroll : 0, null, savedView ? savedView.boardScroll : null);
        }
        else window.location.reload();
    }
    window.addEventListener('popstate', function (event) {
        var previous = new URL(contentUrl), current = new URL(window.location.href);
        if (!workspaceUrl(contentUrl) && panel.hasClass('hidden') && !(event.state && event.state.workboardPreview)
            && previous.origin === current.origin && previous.pathname === current.pathname && previous.search === current.search) {
            contentUrl = window.location.href; return;
        }
        viewStates[contentUrl] = captureView();
        if (mutationPending()) { waitingForHistory = true; return; }
        restoreHistory(event.state);
    });
    function resumeHistory() {
        if (!waitingForHistory || contextDialogOpen || $('#context-modal').hasClass('in') || $('#context-modal').data('mutation-pending') === true) return;
        waitingForHistory = false; restoreHistory(window.history.state);
    }
    $(document).on('workboard:detail-updated', function (event, target) {
        var navigation = target && target.find('template[data-project-navigation]')[0];
        if (navigation && navigation.getAttribute('data-project-id') === $('main[data-workspace-project]').attr('data-workspace-project')) $('.project-sidebar').empty().append($(navigation.content).clone());
    });
    $(document).on('workboard:mutation-finished', resumeHistory);
    $('#context-modal').on('show.bs.modal', function () { contextDialogOpen = true; cancelNavigation(); syncPhone(); }).on('hidden.bs.modal', function () { contextDialogOpen = false; syncPhone(); resumeHistory(); });
    phone.addEventListener('change', syncPhone);
    window.addEventListener('pagehide', rememberPosition);
    if (workspaceUrl(window.location.href)) {
        content.attr('data-modal-read-url', contentUrl).attr('data-modal-read-view', 'workspace');
        var initial = window.history.state;
        if (initial && initial.workboardWorkspace) { restoreBoard(initial.boardScroll); window.scrollTo(window.scrollX, initial.scroll || 0); }
        rememberPosition();
    }
    setActive(window.location.href);
})(jQuery);
