(function ($) {
    'use strict';
    $('[data-document-preview]').removeClass('hidden');
    $(document).on('click', '[data-preview-url]', function () {
        var button = $(this), form = button.closest('form'), area = button.closest('[data-document-preview]');
        var content = area.find('[data-preview-content]'), status = area.find('[data-preview-status]');
        var url = new URL(button.attr('data-preview-url'), window.location.href);
        if (url.origin !== window.location.origin) return;
        var submittedFormat = form.find('[name="Format"]').val();
        var submittedBody = form.find('[name="Body"]').val();
        var previous = area.data('preview-request');
        if (previous) previous.abort();
        var generation = (area.data('preview-generation') || 0) + 1;
        area.data('preview-generation', generation);
        button.prop('disabled', true); status.text('Loading preview…');
        var request = $.ajax({url: url.href, method: 'POST', timeout: 20000,
            data: {ProjectId: form.find('[name="ProjectId"]').val(), Body: submittedBody,
                Format: submittedFormat, __RequestVerificationToken: form.find('[name="__RequestVerificationToken"]').val()}});
        area.data('preview-request', request);
        request.done(function (html, state, xhr) {
            if (!live()) return;
            if (xhr.getResponseHeader('X-Workboard-Partial') !== 'document-preview') { status.text('Preview unavailable. Your text is unchanged.'); return; }
            var format = submittedFormat;
            content.empty().append($('<div>').addClass(format === '1' ? 'document-body document-markdown' : 'document-body document-plain')
                .append($.parseHTML(html, document, false))).removeClass('hidden');
            status.text(form.find('[name=Body]').val() === submittedBody && form.find('[name=Format]').val() === submittedFormat
                ? 'Preview only. Save the document to keep your changes.' : 'Text changed after this preview. Preview again to see the current draft.');
        }).fail(function (xhr, state) {
            if (!live() || state === 'abort') return;
            status.text(xhr.status === 401 ? 'Sign in again to preview. Your text is unchanged.' : 'Preview could not be loaded. Your text is unchanged.');
        }).always(function () {
            if (live()) { button.prop('disabled', false); area.removeData('preview-request'); }
        });
        function live() { return document.contains(area[0]) && area.data('preview-generation') === generation; }
    });
})(jQuery);
