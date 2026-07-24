/*
 * RidePass embeddable widgets host script.
 *
 * A track drops this on their own website, once per page:
 *
 *   <div data-ridepass="events" data-tenant="your-subdomain"></div>
 *   <div data-ridepass="event"  data-tenant="your-subdomain" data-event="<event-id>"></div>
 *   <div data-ridepass="seasonpasses" data-tenant="your-subdomain"></div>
 *   <div data-ridepass="seasonpass"   data-tenant="your-subdomain" data-pass="<pass-id>"></div>
 *   <script src="https://ridepass.io/embed.js" async></script>
 *
 * Legacy shorthand still works:
 *   <div data-ridepass-events data-tenant="your-subdomain"></div>
 *
 * Each tag becomes an iframe pointing at the chromeless widget on the track's
 * RidePass subdomain; the iframe auto-sizes from height messages the widget
 * posts back. Multiple widgets per page are supported.
 */
(function () {
    'use strict';

    // Resolve this script's own origin so the apex host works in any environment
    // (ridepass.io in prod, ridepass.local in dev). document.currentScript is null
    // for async scripts, so fall back to scanning for the embed.js tag.
    var me = document.currentScript || (function () {
        var scripts = document.querySelectorAll('script[src]');
        for (var i = scripts.length - 1; i >= 0; i--) {
            if (scripts[i].src.indexOf('/embed.js') !== -1) return scripts[i];
        }
        return null;
    })();
    if (!me) return;

    var base = new URL(me.src);
    var apexHost = base.host;
    var proto = base.protocol;
    var seq = 0;

    // Map a widget type + its element's data-* config to the chromeless route.
    // Keep in sync with src/embed/widgets.ts. Returns { path } or null.
    function resolveWidget(type, el) {
        switch (type) {
            case 'event': {
                var id = el.getAttribute('data-event');
                if (!id) {
                    console.warn('[ridepass] embed: data-event is required for the "event" widget');
                    return null;
                }
                return { path: '/embed/event/' + encodeURIComponent(id) };
            }
            case 'seasonpasses':
                return { path: '/embed/seasonpasses' };
            case 'seasonpass': {
                var pid = el.getAttribute('data-pass');
                if (!pid) {
                    console.warn('[ridepass] embed: data-pass is required for the "seasonpass" widget');
                    return null;
                }
                return { path: '/embed/seasonpass/' + encodeURIComponent(pid) };
            }
            case 'order': {
                return { path: '/embed/order' };
            }
            case 'status':
                return { path: '/embed/status' };
            case 'shop':
                return { path: '/embed/shop' };
            case 'rentals':
                return { path: '/embed/rentals' };
            case 'giftcard':
                return { path: '/embed/giftcard' };
            case 'membership':
                return { path: '/embed/membership' };
            case 'feedback':
                return { path: '/embed/feedback' };
            case 'blog': {
                var blimit = el.getAttribute('data-limit');
                return { path: '/embed/blog' + (blimit ? '?limit=' + encodeURIComponent(blimit) : '') };
            }
            case 'calendar': {
                var cqs = [];
                var climit = el.getAttribute('data-limit');
                var cetype = el.getAttribute('data-event-type');
                if (climit) cqs.push('limit=' + encodeURIComponent(climit));
                if (cetype) cqs.push('type=' + encodeURIComponent(cetype));
                return { path: '/embed/calendar' + (cqs.length ? '?' + cqs.join('&') : '') };
            }
            case 'events':
            case '':
            case null:
            case undefined: {
                var qs = [];
                var limit = el.getAttribute('data-limit');
                var etype = el.getAttribute('data-event-type');
                if (limit) qs.push('limit=' + encodeURIComponent(limit));
                if (etype) qs.push('type=' + encodeURIComponent(etype));
                return { path: '/embed/events' + (qs.length ? '?' + qs.join('&') : '') };
            }
            default:
                console.warn('[ridepass] embed: unknown widget type "' + type + '"');
                return null;
        }
    }

    function mount(el, type) {
        if (el.getAttribute('data-ridepass-mounted')) return;
        var tenant = el.getAttribute('data-tenant');
        if (!tenant) {
            console.warn('[ridepass] embed: data-tenant attribute is required');
            return;
        }
        var widget = resolveWidget(type, el);
        if (!widget) return;
        el.setAttribute('data-ridepass-mounted', '1');

        // Unique id so resize messages from this iframe only resize this iframe,
        // even when several widgets for the same tenant (same origin) share a page.
        var fid = 'rp' + (++seq);
        var sep = widget.path.indexOf('?') !== -1 ? '&' : '?';
        var src = proto + '//' + tenant + '.' + apexHost + widget.path + sep + 'rpfid=' + fid;

        var iframe = document.createElement('iframe');
        iframe.src = src;
        iframe.title = 'RidePass';
        iframe.loading = 'lazy';
        iframe.setAttribute('allow', 'payment');
        iframe.style.width = '100%';
        iframe.style.border = '0';
        iframe.style.display = 'block';
        // Pre-load placeholder only, replaced by the first resize message. Not a min-height:
        // widgets report their true content height, so a floor would strand dead space under a
        // short one. The compact strip widgets start small so they don't flash a tall box and
        // collapse; everything else starts at a roomy default.
        iframe.style.height = (type === 'status') ? '60px' : '400px';
        // No inner scrollbar: the iframe auto-resizes to its content (below), so a
        // scrollbar would be both redundant and a giveaway that the widget is framed.
        // scrolling="no" is the only cross-browser way to suppress it from OUT here;
        // the embedded app also hides it from the inside (App.vue rp-embed styles) so
        // the two don't have to agree on which one wins.
        iframe.setAttribute('scrolling', 'no');
        iframe.style.overflow = 'hidden';
        el.appendChild(iframe);

        // Only trust resize messages from this iframe's own origin AND frame id.
        var iframeOrigin = new URL(src).origin;
        window.addEventListener('message', function (ev) {
            if (ev.origin !== iframeOrigin) return;
            var d = ev.data;
            if (!d || d.type !== 'ridepass:resize' || typeof d.height !== 'number') return;
            // Strict frame-id match. A message carrying no frameId used to be treated as
            // "meant for me", which made EVERY widget on the page adopt it: one hand-rolled
            // iframe (no rpfid in its src) broadcasting its own height resized all the
            // managed widgets to that height, clipping the taller ones. Every iframe this
            // script creates is given an rpfid, so a message without a matching id is never
            // ours.
            if (d.frameId !== fid) return;
            // Floor low enough for a compact strip (the current-conditions widget is ~50px);
            // it only exists to stop a transient 0-height message collapsing the frame mid-load.
            iframe.style.height = Math.max(40, Math.ceil(d.height)) + 'px';
        });
    }

    function mountAll() {
        // New scheme: data-ridepass="<type>".
        var typed = document.querySelectorAll('[data-ridepass]');
        Array.prototype.forEach.call(typed, function (el) {
            mount(el, el.getAttribute('data-ridepass'));
        });
        // Legacy shorthand: data-ridepass-events (treated as the events widget).
        var legacy = document.querySelectorAll('[data-ridepass-events]');
        Array.prototype.forEach.call(legacy, function (el) {
            mount(el, 'events');
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', mountAll);
    } else {
        mountAll();
    }
})();
