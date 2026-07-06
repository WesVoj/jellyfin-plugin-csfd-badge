'use strict';

(() => {
    const badgeClass = 'csfd-rating-badge';
    const responseCache = new Map();
    let scheduled = false;

    const addStyles = () => {
        if (document.querySelector('#csfd-badge-styles')) return;
        const style = document.createElement('style');
        style.id = 'csfd-badge-styles';
        style.textContent = `
            .${badgeClass} {
                align-items: center;
                background: #ba1b1b;
                border-radius: .25em;
                color: #fff !important;
                display: inline-flex;
                font-size: 92%;
                font-weight: 700;
                gap: .32em;
                line-height: 1;
                padding: .34em .5em;
                text-decoration: none !important;
                white-space: nowrap;
            }
            .${badgeClass}:hover,
            .${badgeClass}:focus-visible {
                background: #d32222;
                box-shadow: 0 0 0 2px rgba(255,255,255,.7);
            }
            .${badgeClass}[data-stale="true"] { opacity: .82; }
        `;
        document.head.appendChild(style);
    };

    const getItemId = () => {
        const searchId = new URLSearchParams(window.location.search).get('id');
        if (searchId) return searchId;
        const questionMark = window.location.hash.indexOf('?');
        if (questionMark < 0) return null;
        return new URLSearchParams(window.location.hash.slice(questionMark + 1)).get('id');
    };

    const getActiveDetailPage = () => {
        const pages = [...document.querySelectorAll('.itemDetailPage')];
        return pages.find(page => !page.classList.contains('hide') && page.offsetParent !== null)
            || pages.at(-1)
            || null;
    };

    const findVisiblePrimaryInfo = page => {
        const targets = [...page.querySelectorAll('.itemMiscInfo-primary')];
        return targets.find(target => target.offsetParent !== null) || targets[0] || null;
    };

    const cleanAndFindBadge = (page, target, itemId) => {
        const badges = [...page.querySelectorAll(`.${badgeClass}`)];
        const current = badges.find(badge => badge.dataset.itemId === itemId && target.contains(badge));
        for (const badge of badges) {
            if (badge !== current) badge.remove();
        }
        return current || null;
    };

    const fetchBadge = itemId => {
        if (responseCache.has(itemId)) return responseCache.get(itemId);
        const promise = window.ApiClient.ajax({
            type: 'GET',
            url: window.ApiClient.getUrl(`CsfdBadge/Items/${encodeURIComponent(itemId)}`),
            dataType: 'json'
        }).catch(error => {
            console.debug('[ČSFD Badge] Rating unavailable', error);
            return null;
        });
        responseCache.set(itemId, promise);
        return promise;
    };

    const render = async () => {
        scheduled = false;
        if (!window.ApiClient || !window.ApiClient.accessToken?.()) return;

        const page = getActiveDetailPage();
        const itemId = getItemId();
        const target = page ? findVisiblePrimaryInfo(page) : null;
        if (!page || !itemId || !target) return;

        if (cleanAndFindBadge(page, target, itemId)) return;

        const data = await fetchBadge(itemId);
        const rating = data?.rating ?? data?.Rating;
        const url = data?.url ?? data?.Url;
        if (!Number.isFinite(rating) || typeof url !== 'string' || !url.startsWith('https://www.csfd.cz/')) return;
        if (!document.contains(target) || getItemId() !== itemId) return;

        // Several DOM mutations can enter render() before the first network
        // request completes. Re-check after await so only the first continuation
        // inserts a badge and all later continuations reuse it.
        if (cleanAndFindBadge(page, target, itemId)) return;

        const badge = document.createElement('a');
        badge.className = `mediaInfoItem ${badgeClass}`;
        badge.dataset.itemId = itemId;
        badge.dataset.stale = String(data?.isStale ?? data?.IsStale ?? false);
        badge.href = url;
        badge.target = '_blank';
        badge.rel = 'noopener noreferrer';
        badge.title = `Otevřít ${data?.title ?? data?.Title ?? 'titul'} na ČSFD`;
        badge.setAttribute('aria-label', `ČSFD hodnocení ${rating} procent`);
        badge.textContent = `ČSFD ${rating} %`;
        badge.addEventListener('click', event => {
            if (window.NativeShell?.openUrl) {
                event.preventDefault();
                event.stopPropagation();
                window.NativeShell.openUrl(url);
            }
        });
        target.appendChild(badge);
    };

    const scheduleRender = () => {
        if (scheduled) return;
        scheduled = true;
        window.setTimeout(render, 80);
    };

    addStyles();
    document.addEventListener('viewshow', scheduleRender, true);
    window.addEventListener('hashchange', scheduleRender);
    window.addEventListener('popstate', scheduleRender);
    new MutationObserver(scheduleRender).observe(document.body, { childList: true, subtree: true });
    scheduleRender();
})();
