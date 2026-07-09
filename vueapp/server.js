const express = require('express');
const fs = require('fs');
const path = require('path');
const app = express();
const port = process.env.PORT || 8080;

// Internal webapi origin (same box, behind nginx). Overridable for other topologies.
const WEBAPI_ORIGIN = process.env.WEBAPI_ORIGIN || 'http://127.0.0.1:7293';

const DIST = path.join(__dirname, 'dist');
const INDEX_HTML = fs.readFileSync(path.join(DIST, 'index.html'), 'utf8');

// Region between the OG_START/OG_END markers in index.html — replaced per-event below.
const OG_REGION = /<!--OG_START-->[\s\S]*?<!--OG_END-->/;
const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function escapeHtml(s) {
    return String(s)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// Uploaded event images are either an absolute URL (DO Spaces) or a root-relative
// path served by nginx (/uploads/...). Social scrapers need an absolute URL, so
// resolve relatives against the public host the visitor is on.
function absoluteUrl(maybeUrl, proto, host) {
    if (!maybeUrl) return null;
    if (/^https?:\/\//i.test(maybeUrl)) return maybeUrl;
    return `${proto}://${host}${maybeUrl.startsWith('/') ? '' : '/'}${maybeUrl}`;
}

function buildOgTags(fields) {
    const tags = [
        '<!--OG_START-->',
        '<meta property="og:type" content="website" />',
        '<meta property="og:site_name" content="RidePass" />',
        `<meta property="og:title" content="${escapeHtml(fields.title)}" />`,
        `<meta property="og:url" content="${escapeHtml(fields.url)}" />`,
    ];
    if (fields.description) {
        tags.push(`<meta property="og:description" content="${escapeHtml(fields.description)}" />`);
    }
    if (fields.image) {
        tags.push(`<meta property="og:image" content="${escapeHtml(fields.image)}" />`);
        tags.push('<meta name="twitter:card" content="summary_large_image" />');
    } else {
        tags.push('<meta name="twitter:card" content="summary" />');
    }
    tags.push('<!--OG_END-->');
    return tags.join('\n    ');
}

// Server-render Open Graph tags for a shared event link so the preview card shows the
// event image, title, and description. Falls back to serving the untouched SPA shell on
// any failure so the page itself never breaks.
app.get('/Event/:id', async (req, res, next) => {
    const { id } = req.params;
    if (!GUID.test(id)) return next();

    const host = req.headers['host'];
    const proto = (req.headers['x-forwarded-proto'] || 'https').split(',')[0].trim();
    if (!host) return next();

    try {
        const controller = new AbortController();
        const timeout = setTimeout(() => controller.abort(), 2500);
        let ev;
        try {
            // Forward the visitor's Host so the webapi resolves the right tenant.
            const r = await fetch(`${WEBAPI_ORIGIN}/api/Event/Public/${id}`, {
                headers: { Host: host },
                signal: controller.signal,
            });
            if (!r.ok) return next();
            const body = await r.json();
            ev = body && body.data;
        } finally {
            clearTimeout(timeout);
        }
        if (!ev || !ev.title) return next();

        const rawDescription = (ev.description || '').replace(/\s+/g, ' ').trim();
        const description = rawDescription.length > 200
            ? rawDescription.slice(0, 197) + '…'
            : rawDescription;

        const image = absoluteUrl(ev.imageUrl, proto, host)
            || absoluteUrl(ev.eventTypeImageUrl, proto, host);

        const og = buildOgTags({
            title: ev.title,
            description,
            image,
            url: `${proto}://${host}/Event/${id}`,
        });

        res.set('Content-Type', 'text/html; charset=utf-8');
        return res.send(INDEX_HTML.replace(OG_REGION, og));
    } catch (err) {
        console.warn('[og] event meta injection failed for', id, err && err.message);
        return next();
    }
});

app.use(express.static(DIST));

app.get('*', (req, res) => {
    res.sendFile(path.join(DIST, 'index.html'));
});

app.listen(port, () => {
    console.log(`Server running on port ${port}`);
});
