const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const { JSDOM } = require('jsdom');

const html = fs.readFileSync(
    path.join(__dirname, '../../src/Jellyfin.Plugin.CsfdBadge/Configuration/configPage.html'),
    'utf8');

test('administration page renders live backfill progress', async () => {
    const dom = new JSDOM(html, { runScripts: 'outside-only', url: 'http://localhost/web/' });
    const { window } = dom;
    const currentPage = window.document.querySelector('#CsfdBadgeConfigPage');
    const stalePage = currentPage.cloneNode(true);
    stalePage.querySelector('#BackfillLibraryItems').remove();
    currentPage.parentNode.insertBefore(stalePage, currentPage);
    const status = {
        state: 'Running',
        libraryItems: 100,
        total: 60,
        processed: 30,
        remaining: 30,
        succeeded: 25,
        notFound: 4,
        failed: 1,
        skipped: 40,
        progressPercent: 50,
        currentTitle: 'Test title',
        lastError: 'Test error',
        lazyQueueSize: 3,
        lazyQueueLimit: 50
    };
    window.Dashboard = {
        showLoadingMsg() {},
        hideLoadingMsg() {},
        alert() {},
        processPluginConfigurationUpdateResult() {}
    };
    window.ApiClient = {
        getUrl: value => `http://localhost/${value}`,
        getPluginConfiguration: () => Promise.resolve({
            ApiBaseUrl: 'http://localhost:3030',
            CacheHours: 168,
            NegativeCacheHours: 24,
            MinimumMatchScore: 70,
            RequestDelayMilliseconds: 1200,
            EnableWebBadge: true,
            EnableLibraryCardBadges: true,
            FetchCardRatingsWhileBrowsing: false,
            CardFetchQueueLimit: 50
        }),
        updatePluginConfiguration: () => Promise.resolve({}),
        ajax: () => Promise.resolve(status)
    };

    window.eval(window.document.querySelector('script').textContent);
    const pages = window.document.querySelectorAll('#CsfdBadgeConfigPage');
    const page = pages[pages.length - 1];
    page.dispatchEvent(new window.Event('pageshow'));
    await new Promise(resolve => window.setTimeout(resolve, 25));

    assert.equal(page.querySelector('#BackfillState').textContent, 'Probíhá');
    assert.equal(page.querySelector('#BackfillProgressLabel').textContent, '50 %');
    assert.equal(page.querySelector('#BackfillProgressBar').style.width, '50%');
    assert.equal(page.querySelector('#BackfillProcessed').textContent, '30');
    assert.equal(page.querySelector('#BackfillRemaining').textContent, '30');
    assert.equal(page.querySelector('#BackfillCurrentTitle').textContent, 'Test title');
    assert.equal(page.querySelector('#LazyQueueStatus').textContent, '3 / 50');
    assert.equal(page.querySelector('#PauseBackfill').disabled, false);
    assert.equal(page.querySelector('#ResumeBackfill').disabled, true);

    page.dispatchEvent(new window.Event('pagehide'));
    dom.window.close();
});
