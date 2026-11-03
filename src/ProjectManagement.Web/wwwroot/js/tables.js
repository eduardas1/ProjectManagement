(function ($) {
    'use strict';
    var collator = new Intl.Collator('en', {numeric: true, sensitivity: 'base'});
    function navigate(url, context, source) {
        var link = $('<a>').attr('href', url).attr(context ? 'data-context-link' : 'data-workspace-link', '').hide().insertAfter(source);
        link[0].click(); link.remove();
    }
    $(document).on('change', '[data-table-page], [data-table-size]', function () {
        navigate(this.value, this.getAttribute('data-table-context') === 'true', this);
    });
    function initialize() {
        $('table.table:not([data-server-table])').each(function () {
            var table = $(this);
            if (table.data('table-ready') || !table.find('thead th').length) return;
            table.data('table-ready', true);
            var rows = table.find('tbody > tr').get(), headers = table.find('thead th');
            var scope = table.closest('[data-context-region]'), source = scope.find('[data-modal-list-url]').attr('data-modal-list-url') || window.location.pathname;
            var id = 'grid-' + source.replace(/[^a-z0-9]/gi, '-') + '-' + $('table.table').index(this);
            var current = new URL(window.location.href), settings;
            try { settings = JSON.parse(current.searchParams.get(id) || '{}'); } catch (_) { settings = {}; }
            var size = [10,20,50,100].indexOf(settings.size) >= 0 ? settings.size : 20;
            var page = Number.isInteger(settings.page) ? settings.page : 1;
            var column = Number.isInteger(settings.column) ? settings.column : -1, descending = settings.descending === true;
            var controls = $('<div class="table-controls">').insertAfter(table.closest('.table-responsive').length ? table.closest('.table-responsive') : table);
            var status = $('<span>').attr('role', 'status').appendTo(controls);
            var previous = $('<button type="button" class="btn btn-default btn-sm">').text('Previous').appendTo(controls);
            var next = $('<button type="button" class="btn btn-default btn-sm">').text('Next').appendTo(controls);
            var pageSelect = $('<select class="form-control input-sm">').appendTo($('<label>').text('Page ').appendTo(controls));
            var sizeSelect = $('<select class="form-control input-sm">').appendTo($('<label>').text('Rows ').appendTo(controls));
            [10,20,50,100].forEach(function (n) { $('<option>').val(n).text(n).appendTo(sizeSelect); }); sizeSelect.val(size);
            function cell(row, index) {
                var td = $(row.cells[index]), person = td.find('.person-name');
                return td.attr('data-sort-value') || (person.length ? person.text() : td.text()).trim();
            }
            function render(save) {
                if (column >= 0 && column < headers.length) rows.sort(function (a, b) {
                    var comparison = collator.compare(cell(a, column), cell(b, column));
                    return descending ? -comparison : comparison;
                });
                var pages = Math.max(1, Math.ceil(rows.length / size)); page = Math.max(1, Math.min(page, pages));
                var body = table.children('tbody');
                rows.forEach(function (row, index) { body.append(row); $(row).toggleClass('hidden', index < (page - 1) * size || index >= page * size); });
                status.text('Page ' + page + ' of ' + pages + ' (' + rows.length + ' items)');
                previous.prop('disabled', page === 1).toggle(pages > 1); next.prop('disabled', page === pages).toggle(pages > 1);
                pageSelect.parent().toggle(pages > 1);
                pageSelect.empty(); for (var n = 1; n <= pages; n++) $('<option>').val(n).text(n).appendTo(pageSelect); pageSelect.val(page);
                headers.each(function (index) {
                    if (!$(this).find('[data-local-sort]').length) return;
                    $(this).attr('aria-sort', index === column ? descending ? 'descending' : 'ascending' : 'none');
                    $(this).find('.table-arrow').text(index === column ? descending ? ' ▼' : ' ▲' : '');
                });
                if (save) {
                    var url = new URL(window.location.href); url.searchParams.set(id, JSON.stringify({size: size, page: page, column: column, descending: descending}));
                    window.history.replaceState(window.history.state, '', url.href);
                }
            }
            headers.each(function (index) {
                var th = $(this), text = th.text().trim();
                if (!text || text === 'Actions') return;
                var button = $('<button type="button" class="table-sort" data-local-sort>'); button.text(text).append($('<span class="table-arrow" aria-hidden="true">'));
                th.empty().append(button);
                button.on('click', function () { descending = column === index ? !descending : false; column = index; page = 1; render(true); });
            });
            previous.on('click', function () { page--; render(true); }); next.on('click', function () { page++; render(true); });
            pageSelect.on('change', function () { page = Number(this.value); render(true); });
            sizeSelect.on('change', function () { size = Number(this.value); page = 1; render(true); });
            render(false);
        });
    }
    $(document).on('workboard:workspace-updated workboard:detail-updated workboard:list-updated', initialize);
    initialize();
})(jQuery);
