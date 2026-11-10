// Staff dashboard behaviour. Plain JavaScript, no libraries except Bootstrap and Leaflet.
(function () {
    'use strict';

    // ---- Clickable table rows ----
    document.querySelectorAll('tr[data-href]').forEach(function (row) {
        row.addEventListener('click', function (e) {
            if (e.target.closest('a, button, input, select, form')) return;
            window.location.href = row.getAttribute('data-href');
        });
    });

    // ---- Confirm before risky buttons ----
    document.querySelectorAll('[data-confirm]').forEach(function (el) {
        el.addEventListener('click', function (e) {
            if (!window.confirm(el.getAttribute('data-confirm'))) e.preventDefault();
        });
    });


    // ---- Status form: a rejection needs a reason ----
    document.querySelectorAll('form[data-saveall]').forEach(function (form) {
        var select = form.querySelector('select[name="Status"]');
        var note = form.querySelector('textarea[name="Note"]');
        var hint = form.querySelector('[data-note-hint]');
        if (!select || !note) return;

        function sync() {
            var rejecting = select.value === 'rejected';
            note.required = rejecting;
            if (hint) hint.textContent = rejecting ? '(required: the citizen will see the reason)' : '(optional, internal history)';
        }
        select.addEventListener('change', sync);
        sync();
    });

    // ---- Save-all form: choose me, confirm a move, avoid double submit ----
    document.querySelectorAll('form[data-saveall]').forEach(function (form) {
        var me = form.querySelector('[data-assign-me]');
        var assignee = form.querySelector('select[name="AssigneeId"]');
        if (me && assignee) {
            me.addEventListener('click', function () { assignee.value = me.getAttribute('data-assign-me'); });
        }

        form.addEventListener('submit', function (e) {
            var moved = Array.prototype.some.call(form.querySelectorAll('select[data-move]'), function (sel) {
                var original = Array.prototype.find.call(sel.options, function (o) { return o.defaultSelected; });
                return original && original.value !== sel.value;
            });
            if (moved && !window.confirm('Move this report to another category or department? Its assignee is cleared if they belong to another department.')) {
                e.preventDefault();
                return;
            }
            var button = form.querySelector('[data-save-all]');
            if (button) { button.disabled = true; button.textContent = 'Saving...'; }
        });
    });

    // ---- Photo viewer ----
    var photoModalEl = document.getElementById('photoModal');
    if (photoModalEl && window.bootstrap) {
        var modal = new bootstrap.Modal(photoModalEl);
        var modalImg = document.getElementById('photoModalImg');
        document.querySelectorAll('[data-photo]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                modalImg.src = btn.getAttribute('data-photo');
                modal.show();
            });
        });
    }

    // ---- Maps ----
    if (!window.L) return;

    var SARAJEVO = [43.8563, 18.4131];
    var BOUNDS = L.latLngBounds([43.72, 18.12], [43.99, 18.56]);

    function baseMap(el, center, zoom) {
        var map = L.map(el, { center: center, zoom: zoom, minZoom: 11, maxBounds: BOUNDS.pad(0.2) });
        L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
            maxZoom: 19,
            attribution: '&copy; OpenStreetMap contributors'
        }).addTo(map);
        return map;
    }

    function pin(status) {
        return L.divIcon({
            className: '',
            html: '<div class="pin pin-' + status + '"></div>',
            iconSize: [24, 24],
            iconAnchor: [12, 24],
            popupAnchor: [0, -22]
        });
    }

    // Detail page: one marker
    var detail = document.getElementById('detail-map');
    if (detail) {
        var lat = parseFloat(detail.getAttribute('data-lat'));
        var lng = parseFloat(detail.getAttribute('data-lng'));
        if (!isNaN(lat) && !isNaN(lng)) {
            var m = baseMap(detail, [lat, lng], 16);
            L.marker([lat, lng], { icon: pin(detail.getAttribute('data-status') || 'submitted') }).addTo(m);
        }
    }

    // Map page: all matching issues
    var big = document.getElementById('issues-map');
    if (big) {
        var map = baseMap(big, SARAJEVO, 12);
        var count = document.getElementById('map-count');
        var url = big.getAttribute('data-url') + (big.getAttribute('data-query') || '');

        function esc(text) {
            var d = document.createElement('div');
            d.textContent = text == null ? '' : String(text);
            return d.innerHTML;
        }

        fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
            .then(function (r) { if (!r.ok) throw new Error('HTTP ' + r.status); return r.json(); })
            .then(function (points) {
                var group = [];
                points.forEach(function (p) {
                    var marker = L.marker([p.latitude, p.longitude], { icon: pin(p.status) }).addTo(map);
                    marker.bindPopup(
                        '<strong>#' + esc(p.id) + ' ' + esc(p.category) + '</strong><br>' +
                        esc(p.address || 'No address') + '<br>' +
                        '<a href="/Issues/Details/' + encodeURIComponent(p.id) + '">Open issue</a>'
                    );
                    group.push([p.latitude, p.longitude]);
                });
                if (count) count.textContent = points.length + (points.length === 1 ? ' issue' : ' issues');
                if (group.length > 0) map.fitBounds(L.latLngBounds(group).pad(0.15), { maxZoom: 15 });
            })
            .catch(function () {
                if (count) count.textContent = 'Could not load the map data';
            });
    }
})();