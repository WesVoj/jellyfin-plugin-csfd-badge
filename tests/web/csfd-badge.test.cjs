const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const { JSDOM } = require('jsdom');

const script = fs.readFileSync(
    path.join(__dirname, '../../src/Jellyfin.Plugin.CsfdBadge/Web/csfd-badge.js'),
    'utf8');

const rating = {
    rating: 84,
    url: 'https://www.csfd.cz/film/123-test/',
    title: 'Test title',
    isStale: false
};

async function createPage() {
    const dom = new JSDOM(
        '<!doctype html><html><head></head><body>'
        + '<div class="itemDetailPage"><div class="itemMiscInfo-primary"></div></div>'
        + '</body></html>',
        {
            runScripts: 'outside-only',
            url: 'http://localhost/web/index.html#!/details?id=0123456789abcdef0123456789abcdef'
        });

    const { window } = dom;
    window.ApiClient = {
        accessToken: () => 'test-token',
        getUrl: value => `http://localhost/${value}`,
        ajax: () => Promise.resolve(rating)
    };
    window.eval(script);
    await new Promise(resolve => window.setTimeout(resolve, 250));
    return dom;
}

test('renders only one badge during repeated DOM mutations', async () => {
    const dom = await createPage();
    const { window } = dom;

    for (let index = 0; index < 10; index += 1) {
        window.document.body.appendChild(window.document.createElement('span'));
    }
    await new Promise(resolve => window.setTimeout(resolve, 250));

    assert.equal(window.document.querySelectorAll('.csfd-rating-badge').length, 1);
    assert.equal(window.document.querySelector('.csfd-rating-badge').textContent, 'ČSFD 84 %');
    dom.window.close();
});

test('hands badge links to the native mobile shell', async () => {
    const dom = await createPage();
    const { window } = dom;
    let openedUrl = null;
    window.NativeShell = { openUrl: url => { openedUrl = url; } };

    const badge = window.document.querySelector('.csfd-rating-badge');
    const click = new window.MouseEvent('click', { bubbles: true, cancelable: true });
    badge.dispatchEvent(click);

    assert.equal(openedUrl, rating.url);
    assert.equal(click.defaultPrevented, true);
    dom.window.close();
});
