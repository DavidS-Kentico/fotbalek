// Team chat interop — loaded on demand as an ES module by ChatDock (mirrors game.js).
// In-app only: tab-title unread prefix, visibility/focus reporting, composer and scroll
// helpers, and the vendored emoji-picker-element wiring. No Notification API in v1.

// ── Dock: tab title + browser-active reporting ─────────────────────────────────────────

let dockRef = null;
let unreadCount = 0;
let titleObserver = null;

function isBrowserActive() {
    return document.visibilityState === 'visible' && document.hasFocus();
}

function desiredTitle() {
    let base = document.title.replace(/^\(\d+\)\s/, '');
    // Team pages render no <PageTitle> during prerender (pre-existing quirk: TeamLayout
    // gates @Body until after first render), so the title can be empty — don't show a
    // bare "(3) ".
    if (unreadCount > 0 && !base) {
        base = 'Fotbalek';
    }
    return unreadCount > 0 ? `(${unreadCount}) ${base}` : base;
}

// Idempotent, so the MutationObserver below can't loop: the write it triggers
// re-enters with an already-correct title and does nothing.
function applyTitle() {
    const want = desiredTitle();
    if (document.title !== want) {
        document.title = want;
    }
}

function reportActive() {
    dockRef?.invokeMethodAsync('OnBrowserActiveChanged', isBrowserActive()).catch(() => { });
}

// A MainLayout↔TeamLayout swap creates the new dock before the old one is disposed; the
// token lets a stale dispose no-op instead of tearing down the new dock's wiring.
let dockToken = 0;

export function initDock(ref) {
    dockToken++;
    dockRef = ref;
    document.addEventListener('visibilitychange', reportActive);
    window.addEventListener('focus', reportActive);
    window.addEventListener('blur', reportActive);

    // Blazor's <PageTitle> overwrites document.title on navigation; watch <head> (a page
    // may start with no <title> element at all — see desiredTitle) and re-apply the unread
    // prefix. applyTitle is idempotent, so this cannot loop.
    if (!titleObserver) {
        titleObserver = new MutationObserver(applyTitle);
        titleObserver.observe(document.head, { childList: true, characterData: true, subtree: true });
    }

    return {
        token: dockToken,
        active: isBrowserActive(),
        // Soft keyboards have no Shift+Enter: on coarse pointers Enter inserts a newline
        // and the send button is the send affordance.
        coarse: window.matchMedia('(pointer: coarse)').matches,
    };
}

export function setUnread(n) {
    unreadCount = n;
    applyTitle();
}

export function disposeDock(token) {
    if (token !== dockToken) return;
    document.removeEventListener('visibilitychange', reportActive);
    window.removeEventListener('focus', reportActive);
    window.removeEventListener('blur', reportActive);
    titleObserver?.disconnect();
    titleObserver = null;
    dockRef = null;
    unreadCount = 0;
    applyTitle();
}

// ── Composer ────────────────────────────────────────────────────────────────────────────

function autoResize(el) {
    el.style.height = 'auto';
    el.style.height = Math.min(el.scrollHeight, 120) + 'px';
}

export function initComposer(el, ref, sendOnEnter, initialValue) {
    if (initialValue) {
        el.value = initialValue;
    }
    autoResize(el);

    const onKeyDown = e => {
        if (e.key === 'Enter' && !e.shiftKey && sendOnEnter && !e.isComposing) {
            e.preventDefault();
            const text = el.value;
            el.value = '';
            autoResize(el);
            ref.invokeMethodAsync('OnComposerSend', text).catch(() => { });
        }
    };
    const onInput = () => {
        autoResize(el);
        ref.invokeMethodAsync('OnComposerInput', el.value, el.selectionStart ?? el.value.length)
            .catch(() => { });
    };
    el.addEventListener('keydown', onKeyDown);
    el.addEventListener('input', onInput);
    el._chatComposerDispose = () => {
        el.removeEventListener('keydown', onKeyDown);
        el.removeEventListener('input', onInput);
    };
}

export function disposeComposer(el) {
    el?._chatComposerDispose?.();
}

export function clearComposer(el) {
    if (!el) return;
    el.value = '';
    autoResize(el);
}

export function focusComposer(el) {
    el?.focus();
}

// Inserts at the caret (emoji picker) and syncs back to .NET via the input listener.
export function insertIntoComposer(el, text) {
    if (!el) return;
    const start = el.selectionStart ?? el.value.length;
    const end = el.selectionEnd ?? start;
    el.setRangeText(text, start, end, 'end');
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.focus();
}

// Replaces the current @mention token (mention autocomplete pick).
export function replaceComposerRange(el, start, end, text) {
    if (!el) return;
    el.setRangeText(text, start, end, 'end');
    el.dispatchEvent(new Event('input', { bubbles: true }));
    el.focus();
}

// ── Message list scrolling ──────────────────────────────────────────────────────────────

const NEAR_BOTTOM_PX = 60;

function nearBottom(el) {
    return el.scrollHeight - el.scrollTop - el.clientHeight < NEAR_BOTTOM_PX;
}

export function initScroll(el, ref) {
    el._chatNearBottom = true;
    const onScroll = () => {
        if (el.scrollTop < 40) {
            ref.invokeMethodAsync('OnScrolledNearTop').catch(() => { });
        }
        const near = nearBottom(el);
        if (near !== el._chatNearBottom) {
            el._chatNearBottom = near;
            ref.invokeMethodAsync('OnNearBottomChanged', near).catch(() => { });
        }
    };
    el.addEventListener('scroll', onScroll, { passive: true });
    el._chatScrollDispose = () => el.removeEventListener('scroll', onScroll);
}

export function disposeScroll(el) {
    el?._chatScrollDispose?.();
}

export function scrollToBottom(el) {
    if (el) {
        el.scrollTop = el.scrollHeight;
        el._chatNearBottom = true;
    }
}

export function measureScrollHeight(el) {
    return el?.scrollHeight ?? 0;
}

// Older page prepended above the viewport: keep what the user was looking at in place.
export function restoreScrollAfterPrepend(el, previousHeight) {
    if (el) {
        el.scrollTop += el.scrollHeight - previousHeight;
    }
}

// ── Emoji picker (vendored emoji-picker-element web component) ─────────────────────────

let pickerModule = null;

export async function initEmojiPicker(container, ref) {
    pickerModule ??= import('/lib/emoji-picker-element/index.js');
    await pickerModule;

    const picker = document.createElement('emoji-picker');
    // Self-hosted emoji data — the bundle's default dataSource points at a CDN.
    picker.dataSource = '/lib/emoji-picker-element/data.json';

    const syncTheme = () => {
        const dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
        picker.classList.toggle('dark', dark);
        picker.classList.toggle('light', !dark);
    };
    syncTheme();
    const themeObserver = new MutationObserver(syncTheme);
    themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });

    picker.addEventListener('emoji-click', e => {
        const unicode = e.detail?.unicode;
        if (unicode) {
            ref.invokeMethodAsync('OnEmojiPicked', unicode).catch(() => { });
        }
    });

    container.appendChild(picker);
    container._chatPickerDispose = () => {
        themeObserver.disconnect();
        picker.remove();
    };
}

export function disposeEmojiPicker(container) {
    container?._chatPickerDispose?.();
}
