(function () {
    'use strict';
    var key = 'projectmanagement.theme';
    var system = window.matchMedia('(prefers-color-scheme: dark)');
    var selected = null;
    try { selected = localStorage.getItem(key); } catch (_) { }
    if (selected !== 'light' && selected !== 'dark') selected = null;

    function apply() {
        var dark = selected ? selected === 'dark' : system.matches;
        document.documentElement.setAttribute('data-theme', dark ? 'dark' : 'light');
        document.querySelectorAll('[data-theme-toggle]').forEach(function (button) {
            button.hidden = false;
            button.setAttribute('aria-label', 'Switch to ' + (dark ? 'light' : 'dark') + ' mode');
            button.setAttribute('aria-pressed', String(dark));
            button.textContent = (dark ? 'Light' : 'Dark') + (button.hasAttribute('data-compact') ? '' : ' mode');
        });
    }
    apply();
    system.addEventListener('change', function () { if (!selected) apply(); });
    document.addEventListener('DOMContentLoaded', function () {
        apply();
        document.querySelectorAll('[data-theme-toggle]').forEach(function (button) {
            button.addEventListener('click', function () {
                selected = document.documentElement.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
                try { localStorage.setItem(key, selected); } catch (_) { }
                apply();
            });
        });
    });
})();
