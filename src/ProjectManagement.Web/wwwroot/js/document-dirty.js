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
        confirmLeave: function () { return !dirty() || window.confirm('Discard unsaved document changes?'); }
    };
    window.addEventListener('beforeunload', function (event) {
        if (!submitting && dirty()) { event.preventDefault(); event.returnValue = ''; }
    });
    document.addEventListener('submit', function (event) {
        if (event.target === form && !event.defaultPrevented && form.checkValidity()) submitting = true;
    });
    window.addEventListener('pageshow', function () { submitting = false; });
})(jQuery);
