(function ($) {
    'use strict';
    var active = false;
    window.projectConfirmation = {
        get active() { return active; },
        ask: function (message, container) {
            if (active) return Promise.resolve(false);
            active = true;
            return new Promise(function (resolve) {
                var nested = container && container.hasClass('in');
                var modal = nested ? container : $('#confirm-dialog');
                var body = nested ? modal.find('.modal-body').first() : modal.find('[data-confirm-body]');
                var original = document.activeElement;
                var scroll = body.scrollTop();
                var children = body.children().toArray().map(function (element) {
                    var hidden = element.hidden;
                    element.hidden = true;
                    return {element: element, hidden: hidden};
                });
                var panel = $('<div>').attr('data-confirm-panel', '');
                $('<p>').text(message).appendTo(panel);
                var actions = $('<div>').addClass('form-actions').appendTo(panel);
                var cancel = $('<button type="button">').addClass('btn btn-default').text('Keep editing').appendTo(actions);
                var discard = $('<button type="button">').addClass('btn btn-danger').text('Discard changes').appendTo(actions);
                panel.appendTo(body);
                var settled = false;
                function finish(answer) {
                    if (settled) return;
                    settled = true;
                    modal.off('.confirmation');
                    if (nested) complete();
                    else modal.one('hidden.bs.modal.confirmation', complete).modal('hide');
                    function complete() {
                        panel.remove();
                        children.forEach(function (entry) { entry.element.hidden = entry.hidden; });
                        body.scrollTop(scroll);
                        active = false;
                        if (original && document.contains(original)) original.focus({preventScroll: true});
                        resolve(answer);
                    }
                }
                cancel.on('click', function () { finish(false); });
                discard.on('click', function () { finish(true); });
                modal.on('hide.bs.modal.confirmation', function (event) { event.preventDefault(); finish(false); });
                modal.on('keydown.confirmation', function (event) {
                    if (event.key === 'Escape') { event.preventDefault(); event.stopImmediatePropagation(); finish(false); }
                    if (event.key === 'Tab') {
                        event.preventDefault(); event.stopImmediatePropagation();
                        (document.activeElement === cancel[0] ? discard : cancel).trigger('focus');
                    }
                });
                if (nested) cancel.trigger('focus');
                else modal.one('shown.bs.modal.confirmation', function () { cancel.trigger('focus'); }).modal({backdrop: 'static', keyboard: false});
            });
        }
    };
})(jQuery);
