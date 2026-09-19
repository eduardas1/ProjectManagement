(function () {
    'use strict';
    var panel = document.querySelector('[data-work-panel]');
    if (!panel) return;
    var handle = panel.querySelector('[data-preview-resize]');
    var key = 'projectmanagement.preview-width';
    var width = 560, drag = null;
    try { var stored = Number(localStorage.getItem(key)); if (stored >= 360 && stored <= 960) width = stored; } catch (_) { }
    function bounds() { return {min: 360, max: Math.max(360, Math.min(960, window.innerWidth - 48))}; }
    function apply() {
        var limit = bounds();
        var rendered = Math.max(limit.min, Math.min(limit.max, width));
        panel.style.setProperty('--preview-width', rendered + 'px');
        handle.setAttribute('aria-valuemin', limit.min);
        handle.setAttribute('aria-valuemax', limit.max);
        handle.setAttribute('aria-valuenow', Math.round(rendered));
        handle.setAttribute('aria-valuetext', Math.round(rendered) + ' pixels wide');
    }
    function save() { try { localStorage.setItem(key, Math.round(width)); } catch (_) { } }
    handle.addEventListener('pointerdown', function (event) {
        if (event.button !== 0 || window.innerWidth < 768) return;
        event.preventDefault();
        drag = {id: event.pointerId, x: event.clientX, width: panel.getBoundingClientRect().width};
        handle.setPointerCapture(event.pointerId); handle.focus();
        panel.classList.add('is-resizing');
    });
    handle.addEventListener('pointermove', function (event) {
        if (!drag || drag.id !== event.pointerId) return;
        var limit = bounds();
        width = Math.max(limit.min, Math.min(limit.max, drag.width + drag.x - event.clientX)); apply();
    });
    function finish() { if (!drag) return; drag = null; panel.classList.remove('is-resizing'); save(); }
    handle.addEventListener('pointerup', finish);
    handle.addEventListener('pointercancel', finish);
    handle.addEventListener('lostpointercapture', finish);
    handle.addEventListener('keydown', function (event) {
        var limit = bounds(), step = event.shiftKey ? 64 : 16;
        width = Number(handle.getAttribute('aria-valuenow'));
        if (event.key === 'ArrowLeft') width += step;
        else if (event.key === 'ArrowRight') width -= step;
        else if (event.key === 'Home') width = limit.min;
        else if (event.key === 'End') width = limit.max;
        else return;
        width = Math.max(limit.min, Math.min(limit.max, width));
        event.preventDefault(); apply(); save();
    });
    window.addEventListener('resize', apply);
    apply();
})();
