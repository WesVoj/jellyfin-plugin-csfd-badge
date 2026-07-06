'use strict';

(() => {
    const badgeClass = 'csfd-rating-badge';
    const cardBadgeClass = 'csfd-card-rating-badge';
    const cardSelector = '.card';
    const responseCache = new Map();
    const pendingCardIds = new Set();
    const inFlightCardIds = new Set();
    const cardRetryCounts = new Map();
    const maxCardRetries = 6;
    const cardRetryDelayMs = 10000;
    let scheduled = false;
    let cardFlushTimer = null;
    let cardConfiguration = null;
    let cardConfigurationPromise = null;

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
            .csfd-card-badge-host { position: relative !important; }
            .${cardBadgeClass} {
                background: #ba1b1b;
                border-radius: .3em;
                bottom: .55em;
                color: #fff;
                font-size: .78em;
                font-weight: 700;
                left: .55em;
                line-height: 1;
                padding: .36em .5em;
                pointer-events: none;
                position: absolute;
                white-space: nowrap;
                z-index: 3;
            }
            .${cardBadgeClass}[data-stale="true"] { opacity: .82; }
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

    const normalizeItemId = value => {
        const match = String(value || '').match(/[a-f\d]{32}|[a-f\d]{8}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{4}-[a-f\d]{12}/i);
        return match ? match[0] : null;
    };

    const getCardItemId = card => {
        const typeElement = card.matches('[data-type]') ? card : card.querySelector('[data-type]');
        const itemType = typeElement?.dataset.type;
        if (itemType && itemType !== 'Movie' && itemType !== 'Series') return null;

        const idElement = card.matches('[data-id], [data-itemid]')
            ? card
            : card.querySelector('[data-id], [data-itemid]');
        const directId = idElement?.dataset.id || idElement?.dataset.itemid;
        const normalizedDirectId = normalizeItemId(directId);
        if (normalizedDirectId) return normalizedDirectId;

        for (const link of card.querySelectorAll('a[href]')) {
            const normalizedLinkId = normalizeItemId(link.getAttribute('href'));
            if (normalizedLinkId) return normalizedLinkId;
        }
        return null;
    };

    const getCardsForItem = itemId => [...document.querySelectorAll(cardSelector)]
        .filter(card => getCardItemId(card)?.toLowerCase() === itemId.toLowerCase());

    const renderCardBadge = (card, itemId, data) => {
        const rating = data?.rating ?? data?.Rating;
        if (!Number.isFinite(rating) || !document.contains(card)) return;

        const host = card.querySelector('.cardScalable, .cardImageContainer, .itemImageContainer') || card;
        host.classList.add('csfd-card-badge-host');
        let badge = host.querySelector(`.${cardBadgeClass}`);
        if (!badge) {
            badge = document.createElement('div');
            badge.className = cardBadgeClass;
            host.appendChild(badge);
        }
        badge.dataset.itemId = itemId;
        badge.dataset.stale = String(data?.isStale ?? data?.IsStale ?? false);
        badge.textContent = `ČSFD ${rating} %`;
    };

    const loadCardConfiguration = () => {
        if (cardConfigurationPromise) return cardConfigurationPromise;
        cardConfigurationPromise = window.ApiClient.ajax({
            type: 'GET',
            url: window.ApiClient.getUrl('CsfdBadge/ClientConfiguration'),
            dataType: 'json'
        }).then(data => {
            cardConfiguration = {
                enabled: data?.enableLibraryCardBadges ?? data?.EnableLibraryCardBadges ?? false,
                fetchMissing: data?.fetchCardRatingsWhileBrowsing ?? data?.FetchCardRatingsWhileBrowsing ?? false
            };
            return cardConfiguration;
        }).catch(error => {
            console.debug('[ČSFD Badge] Card configuration unavailable', error);
            cardConfiguration = { enabled: false, fetchMissing: false };
            return cardConfiguration;
        });
        return cardConfigurationPromise;
    };

    const scheduleCardRetry = itemId => {
        if (!cardConfiguration?.fetchMissing) return;
        const attempt = cardRetryCounts.get(itemId) || 0;
        if (attempt >= maxCardRetries) return;
        cardRetryCounts.set(itemId, attempt + 1);
        window.setTimeout(() => {
            if (getCardsForItem(itemId).length > 0) queueCardId(itemId);
        }, cardRetryDelayMs);
    };

    const fetchCardBatch = async itemIds => {
        try {
            const data = await window.ApiClient.ajax({
                type: 'POST',
                url: window.ApiClient.getUrl('CsfdBadge/Items/Batch'),
                data: JSON.stringify({ ItemIds: itemIds }),
                contentType: 'application/json',
                dataType: 'json'
            });
            const items = data?.items ?? data?.Items ?? {};
            const pending = new Set(data?.pendingItemIds ?? data?.PendingItemIds ?? []);
            for (const itemId of itemIds) {
                const rating = items[itemId]
                    || items[itemId.toLowerCase()]
                    || Object.entries(items).find(([key]) => key.toLowerCase() === itemId.toLowerCase())?.[1];
                if (rating) {
                    cardRetryCounts.delete(itemId);
                    for (const card of getCardsForItem(itemId)) renderCardBadge(card, itemId, rating);
                } else if (pending.has(itemId)) {
                    scheduleCardRetry(itemId);
                }
            }
        } catch (error) {
            console.debug('[ČSFD Badge] Card ratings unavailable', error);
        } finally {
            for (const itemId of itemIds) inFlightCardIds.delete(itemId);
        }
    };

    const flushCardIds = () => {
        cardFlushTimer = null;
        const itemIds = [...pendingCardIds].slice(0, 50);
        if (itemIds.length === 0) return;
        for (const itemId of itemIds) {
            pendingCardIds.delete(itemId);
            inFlightCardIds.add(itemId);
        }
        fetchCardBatch(itemIds);
        if (pendingCardIds.size > 0) {
            cardFlushTimer = window.setTimeout(flushCardIds, 150);
        }
    };

    function queueCardId(itemId) {
        if (!itemId || pendingCardIds.has(itemId) || inFlightCardIds.has(itemId)) return;
        pendingCardIds.add(itemId);
        if (!cardFlushTimer) cardFlushTimer = window.setTimeout(flushCardIds, 150);
    }

    const cardObserver = 'IntersectionObserver' in window
        ? new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                cardObserver.unobserve(entry.target);
                queueCardId(getCardItemId(entry.target));
            }
        }, { rootMargin: '200px' })
        : null;

    const prepareCard = card => {
        const itemId = getCardItemId(card);
        if (!itemId || card.dataset.csfdObservedItemId === itemId) return;
        card.querySelectorAll(`.${cardBadgeClass}`).forEach(badge => badge.remove());
        card.dataset.csfdObservedItemId = itemId;
        if (cardObserver) cardObserver.observe(card);
        else queueCardId(itemId);
    };

    const scanCards = root => {
        if (!cardConfiguration?.enabled || !(root instanceof Element || root instanceof Document)) return;
        if (root instanceof Element && root.matches(cardSelector)) prepareCard(root);
        root.querySelectorAll(cardSelector).forEach(prepareCard);
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
        window.setTimeout(() => {
            render();
            if (!cardConfigurationPromise) initializeCardBadges();
        }, 80);
    };

    const initializeCardBadges = async () => {
        if (!window.ApiClient || !window.ApiClient.accessToken?.()) return;
        const configuration = await loadCardConfiguration();
        if (configuration.enabled) scanCards(document);
    };

    addStyles();
    document.addEventListener('viewshow', scheduleRender, true);
    window.addEventListener('hashchange', scheduleRender);
    window.addEventListener('popstate', scheduleRender);
    new MutationObserver(records => {
        scheduleRender();
        if (!cardConfiguration?.enabled) return;
        for (const record of records) {
            for (const node of record.addedNodes) scanCards(node);
        }
    }).observe(document.body, { childList: true, subtree: true });
    scheduleRender();
    initializeCardBadges();
})();
