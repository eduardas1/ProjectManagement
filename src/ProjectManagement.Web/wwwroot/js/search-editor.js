(function ($) {
    'use strict';
    var instances = [], library = null;
    function vendor(address) {
        if (window.ProjectCodeMirror) return Promise.resolve(window.ProjectCodeMirror);
        if (library) return library;
        var url = new URL(address, window.location.href);
        if (url.origin !== window.location.origin) return Promise.reject(new Error('Use a local editor library.'));
        library = new Promise(function (resolve, reject) {
            var script = document.createElement('script'); script.src = url.href;
            script.onload = function () { window.ProjectCodeMirror ? resolve(window.ProjectCodeMirror) : reject(new Error('Editor unavailable.')); };
            script.onerror = function () { script.remove(); library = null; reject(new Error('Editor unavailable.')); };
            document.head.appendChild(script);
        });
        return library;
    }
    function initialize() {
        instances = instances.filter(function (entry) {
            if (document.contains(entry.form[0])) return true;
            if (entry.view) entry.view.destroy();
            entry.live = false; return false;
        });
        $('[data-search-form]').each(function () {
            var form = $(this); if (form.data('search-editor')) return;
            var textarea = form.find('[name="Input.Expression"]'), details = form.find('[data-advanced-search]');
            if (!textarea.length) return;
            var grammar;
            try { grammar = JSON.parse(form.find('[data-search-grammar]').text()); }
            catch (_) { return; }
            var entry = {form: form, textarea: textarea, view: null, live: true};
            form.data('search-editor', entry); instances.push(entry);
            var indicator = form.find('[data-expression-indicator]');
            function changed() { indicator.text(textarea.val().trim() ? 'An advanced expression is set.' : ''); }
            form.find('[data-search-basic],[data-search-advanced],[data-expression-clear]').removeClass('hidden');
            form.find('[data-search-basic]').on('click', function () {
                details.prop('open', false); form.find('[name="Input.Text"]')[0].focus(); changed();
            });
            form.find('[data-search-advanced]').on('click', function () {
                details.prop('open', true); enhance().then(function () { entry.view ? entry.view.focus() : textarea.trigger('focus'); });
            });
            form.find('[data-expression-clear]').on('click', function () {
                if (entry.view) entry.view.dispatch({changes: {from: 0, to: entry.view.state.doc.length, insert: ''}});
                else textarea.val('');
                changed();
            });
            textarea.on('input', changed); changed();
            var loading = false;
            function enhance() {
                if (entry.view || loading) return Promise.resolve();
                loading = true;
                return vendor(form.attr('data-editor-source')).then(function (cm) {
                    if (!entry.live || !document.contains(form[0]) || entry.view) return;
                    var holder = $('<div>').addClass('search-code-editor').insertAfter(textarea);
                    var hint = $('<span>').addClass('sr-only').text('Advanced search expression. Use Ctrl+Space for suggestions.').insertBefore(holder);
                    function complete(context) {
                        var found = window.projectSearchCompletions(context.state.doc.toString(), context.pos, grammar);
                        if (!found) return null;
                        return {from: found.from, to: found.to, filter: false, options: found.options.map(function (option) {
                            return {label: option.label, type: option.type, detail: option.detail, apply: function (view, completion, from, to) {
                                var nextLength = view.state.doc.length - (to - from) + option.insert.length;
                                if (nextLength > 2000) return;
                                view.dispatch({changes: {from: from, to: to, insert: option.insert}, selection: {anchor: from + option.insert.length}});
                                if (option.type === 'property' || option.type === 'operator') window.setTimeout(function () { if (entry.live) cm.startCompletion(view); }, 0);
                            }};
                        })};
                    }
                    var binding = [{key: 'Tab', run: function (view) { return cm.selectedCompletion(view.state) !== null && cm.acceptCompletion(view); }},
                        {key: 'Enter', run: function (view) { if (cm.acceptCompletion(view)) return true; form[0].requestSubmit(); return true; }}];
                    entry.view = new cm.EditorView({parent: holder[0], state: cm.EditorState.create({doc: textarea.val(), extensions: [
                        cm.EditorView.lineWrapping, cm.history(), cm.keymap.of(binding.concat(cm.completionKeymap, cm.defaultKeymap, cm.historyKeymap)),
                        cm.EditorView.contentAttributes.of({'aria-label': 'Advanced expression', 'aria-describedby': 'expression-help', 'spellcheck': 'false'}),
                        cm.autocompletion({override: [complete], activateOnTyping: true}),
                        cm.EditorState.transactionFilter.of(function (transaction) { return transaction.newDoc.length > 2000 && transaction.newDoc.length > transaction.startState.doc.length && transaction.docChanged ? [] : transaction; }),
                        cm.EditorView.updateListener.of(function (update) { if (update.docChanged) { textarea.val(update.state.doc.toString()); changed(); } })
                    ]})});
                    textarea.addClass('hidden'); hint.attr('aria-hidden', 'true');
                    form.find('#expression-label').on('click', function (event) { event.preventDefault(); entry.view.focus(); });
                }).catch(function () {
                    if (entry.live) indicator.text('Editor suggestions are unavailable. You can still use the expression field.');
                }).finally(function () { loading = false; });
            }
            details.on('toggle', function () { if (this.open) enhance(); });
            if (details.prop('open')) enhance();
            form.on('submit', function () { if (entry.view) textarea.val(entry.view.state.doc.toString()); });
        });
    }
    $(document).on('workboard:workspace-updated workboard:detail-updated', initialize);
    initialize();
})(jQuery);
