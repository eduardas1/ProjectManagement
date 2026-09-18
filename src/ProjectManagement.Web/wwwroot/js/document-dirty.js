(function ($) {
    'use strict';
    var form = document.querySelector('form[data-document-dirty]');
    if (!form) return;
    var submitting = false;
    var unsaved = form.getAttribute('data-unsaved') === 'true';
    function snapshot() {
        return JSON.stringify($(form).serializeArray().filter(function (field) { return field.name !== '__RequestVerificationToken'; }));
    }
    var baseline = snapshot();
    function dirty() { return document.contains(form) && (unsaved || snapshot() !== baseline); }
    window.workboardDirty = {
        confirmLeave: function () {
            return dirty() ? window.projectConfirmation.ask('Discard unsaved document changes?') : Promise.resolve(true);
        }
    };
    document.addEventListener('click', function (event) {
        var link = event.target.closest('a[href]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
            || link.target || link.hasAttribute('download') || !dirty()) return;
        var url = new URL(link.href, window.location.href);
        if (url.href === window.location.href || (url.pathname === window.location.pathname && url.search === window.location.search && url.hash)) return;
        event.preventDefault(); event.stopImmediatePropagation();
        window.workboardDirty.confirmLeave().then(function (leave) {
            if (leave) { submitting = true; window.location.assign(url.href); }
        });
    }, true);
    document.addEventListener('submit', function (event) {
        if (event.target === form || submitting || !dirty()) return;
        event.preventDefault(); event.stopImmediatePropagation();
        var target = event.target, submitter = event.submitter;
        window.workboardDirty.confirmLeave().then(function (leave) {
            if (leave) { submitting = true; target.requestSubmit(submitter || undefined); }
        });
    }, true);
    window.addEventListener('beforeunload', function (event) {
        if (!submitting && dirty()) { event.preventDefault(); event.returnValue = ''; }
    });
    document.addEventListener('submit', function (event) {
        if (event.target === form && !event.defaultPrevented && form.checkValidity()) submitting = true;
    });
    window.addEventListener('pageshow', function () { submitting = false; });
})(jQuery);
