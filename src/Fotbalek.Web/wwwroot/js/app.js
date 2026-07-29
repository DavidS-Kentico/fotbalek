// ===== PWA service worker =====
// Registers the installable-shell worker (see /service-worker.js). Deferred to window load so
// it never competes with the Blazor circuit for startup bandwidth. The worker deliberately does
// not enable offline app use — Blazor Server needs a live connection — it only makes the app
// installable and caches the static shell + offline notice.
if ('serviceWorker' in navigator) {
    window.addEventListener('load', function () {
        navigator.serviceWorker.register('/service-worker.js').catch(function (err) {
            console.warn('Service worker registration failed:', err);
        });
    });
}

// Convert UTC dates to local time
function formatLocalDates() {
    document.querySelectorAll('.match-date').forEach(function(el) {
        const utc = el.getAttribute('data-utc');
        if (utc) {
            const date = new Date(utc);
            el.textContent = date.toLocaleString();
        }
    });
}

// Run on page load and after Blazor updates
document.addEventListener('DOMContentLoaded', formatLocalDates);

// The user's IANA timezone — resolved once per circuit by TimeZoneService.
window.getBrowserTimeZone = function () {
    return Intl.DateTimeFormat().resolvedOptions().timeZone;
};

// ===== Colour theme (light / dark / system) =====
// Preference is stored per-browser in the `fotbalek-theme` cookie and applied
// as data-theme on <html>. The pre-paint version of this lives inline in
// App.razor; this module is the runtime the account page talks to.
window.fotbalekTheme = (function () {
    var COOKIE = 'fotbalek-theme';
    var media = window.matchMedia('(prefers-color-scheme: dark)');

    function read() {
        var m = document.cookie.match(/(?:^|;\s*)fotbalek-theme=(light|dark|system)/);
        return m ? m[1] : 'system';
    }
    function resolve(pref) {
        if (pref === 'light' || pref === 'dark') return pref;
        return media.matches ? 'dark' : 'light';
    }
    function apply(pref) {
        document.documentElement.setAttribute('data-theme', resolve(pref));
    }
    // Keep "system" tracking the OS setting while the app is open.
    media.addEventListener('change', function () {
        if (read() === 'system') apply('system');
    });
    return {
        get: read,
        set: function (pref) {
            document.cookie = COOKIE + '=' + pref + ';path=/;max-age=31536000;samesite=lax';
            apply(pref);
        }
    };
})();

// ===== Toast handoff across a full page load =====
// ToastService is circuit-scoped, so a toast raised just before
// NavigateTo(forceLoad: true) — which the onboarding flows need in order to rebuild the auth
// and team context — would die with the circuit. Park it in sessionStorage instead (per-tab,
// survives the reload) and let ToastHost drain it on the next circuit.
//
// Wire shape must match ToastService.HandoffToast: { message, variant }.
window.fotbalekToast = (function () {
    var KEY = 'fotbalek-toast-handoff';
    var MAX = 4;

    function read() {
        try {
            var raw = sessionStorage.getItem(KEY);
            var parsed = raw ? JSON.parse(raw) : [];
            return Array.isArray(parsed) ? parsed : [];
        } catch (e) {
            return [];
        }
    }

    return {
        stash: function (message, variant) {
            var queue = read();
            queue.push({ message: message, variant: variant });
            // Only the last few can possibly be worth reading after a navigation.
            if (queue.length > MAX) queue = queue.slice(-MAX);
            try {
                sessionStorage.setItem(KEY, JSON.stringify(queue));
            } catch (e) {
                // Private-mode quota or storage disabled — losing a toast is acceptable.
            }
        },
        // Read-and-clear: a parked toast must show exactly once, not on every later reload.
        drain: function () {
            var queue = read();
            try { sessionStorage.removeItem(KEY); } catch (e) { }
            return queue;
        }
    };
})();

// ===== Tab-title unread badge =====
// The single "(N) " prefix on document.title, fed by SEVERAL sources — chat's unread messages and
// the bell's unseen notifications — and summed, because a tab title has room for exactly one number
// and a reader takes it as "N things want me".
//
// It lives here rather than in chat.js because it is no longer chat's alone: app.js is where every
// window.* interop helper lives, and it is what a component can reach by name without importing a
// module it has no other business with.
window.fotbalekTitleBadge = (function () {
    var counts = {};
    var observer = null;

    function total() {
        var sum = 0;
        for (var source in counts) sum += counts[source];
        return sum;
    }

    function desiredTitle() {
        var n = total();
        var base = document.title.replace(/^\(\d+\)\s/, '');
        // Team pages render no <PageTitle> during prerender (TeamLayout gates @Body until after its
        // first render), so the title can be empty — don't show a bare "(3) ".
        if (n > 0 && !base) base = 'Fotbalek';
        return n > 0 ? '(' + n + ') ' + base : base;
    }

    // Idempotent, so the MutationObserver below cannot loop: the write it triggers re-enters with an
    // already-correct title and does nothing.
    function apply() {
        var want = desiredTitle();
        if (document.title !== want) document.title = want;
    }

    return {
        // Replaces one source's count. Sources are independent, so chat and notifications can be set
        // in any order and either can drop to zero without disturbing the other.
        set: function (source, count) {
            counts[source] = count > 0 ? count : 0;
            // Blazor's <PageTitle> overwrites document.title on navigation, so re-apply the prefix
            // whenever <head> changes (a page may start with no <title> element at all — see
            // desiredTitle). Installed on first use and then left in place: it is one cheap observer,
            // and tearing it down would need the sources to agree on who owns it.
            if (!observer) {
                observer = new MutationObserver(apply);
                observer.observe(document.head, { childList: true, characterData: true, subtree: true });
            }
            apply();
        }
    };
})();

// Scroll an element into view by id. The Account page renders its panels only after its data loads,
// so a plain "/account#notifications" fragment cannot work — the element does not exist when the
// browser handles the fragment. The page uses "?section=..." and calls this once loaded.
//
// This lives here rather than in ui.js on purpose: app.js is where every window.* interop helper
// lives and is the file IJSRuntime.InvokeVoidAsync reaches by name, while ui.js is a closed IIFE with
// no export surface at all (AI/notifications.md §8.5).
window.fotbalekScrollToId = function (id) {
    var el = document.getElementById(id);
    if (el) el.scrollIntoView({ behavior: 'smooth', block: 'start' });
};

// Theme-aware colours for Chart.js (grid lines, axis ticks, pie borders) read
// at render time from the active data-theme.
function chartThemeColors() {
    var dark = document.documentElement.getAttribute('data-theme') === 'dark';
    return {
        grid: dark ? 'rgba(255, 255, 255, 0.12)' : 'rgba(0, 0, 0, 0.1)',
        tick: dark ? '#adb5bd' : '#666',
        border: dark ? '#2b3035' : '#fff'
    };
}

// Chart.js rendering functions
window.renderEloChart = function(canvasId, labels, data) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    // Destroy existing chart if any
    if (ctx.chart) {
        ctx.chart.destroy();
    }

    // Ticks, legend and titles follow the active colour theme.
    Chart.defaults.color = chartThemeColors().tick;

    ctx.chart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: 'ELO',
                data: data,
                borderColor: '#198754',
                backgroundColor: 'rgba(25, 135, 84, 0.1)',
                fill: true,
                tension: 0.3,
                pointRadius: 4,
                pointHoverRadius: 6
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: false,
                    grid: {
                        color: chartThemeColors().grid
                    }
                },
                x: {
                    grid: {
                        display: false
                    }
                }
            }
        }
    });
};

window.renderBarChart = function(canvasId, labels, data, label, color) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    // Destroy existing chart if any
    if (ctx.chart) {
        ctx.chart.destroy();
    }

    // Ticks, legend and titles follow the active colour theme.
    Chart.defaults.color = chartThemeColors().tick;

    ctx.chart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: label,
                data: data,
                backgroundColor: color,
                borderColor: color,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: chartThemeColors().grid
                    }
                },
                x: {
                    grid: {
                        display: false
                    }
                }
            }
        }
    });
};

window.renderPieChart = function(canvasId, labels, data, colors) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    // Destroy existing chart if any
    if (ctx.chart) {
        ctx.chart.destroy();
    }

    // Ticks, legend and titles follow the active colour theme.
    Chart.defaults.color = chartThemeColors().tick;

    ctx.chart = new Chart(ctx, {
        type: 'pie',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors,
                borderWidth: 2,
                borderColor: chartThemeColors().border
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'bottom'
                }
            }
        }
    });
};

window.renderHorizontalBarChart = function(canvasId, labels, data, label, color) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    // Destroy existing chart if any
    if (ctx.chart) {
        ctx.chart.destroy();
    }

    // Ticks, legend and titles follow the active colour theme.
    Chart.defaults.color = chartThemeColors().tick;

    ctx.chart = new Chart(ctx, {
        type: 'bar',
        data: {
            labels: labels,
            datasets: [{
                label: label,
                data: data,
                backgroundColor: color,
                borderColor: color,
                borderWidth: 1
            }]
        },
        options: {
            indexAxis: 'y',
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                x: {
                    beginAtZero: true,
                    grid: {
                        color: chartThemeColors().grid
                    }
                },
                y: {
                    grid: {
                        display: false
                    }
                }
            }
        }
    });
};

window.renderMultiLineChart = function(canvasId, labels, datasets) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    if (ctx.chart) {
        ctx.chart.destroy();
    }

    // Ticks, legend and titles follow the active colour theme.
    Chart.defaults.color = chartThemeColors().tick;

    ctx.chart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: datasets.map(function(d) {
                return {
                    label: d.label,
                    data: d.data,
                    borderColor: d.color,
                    backgroundColor: d.color,
                    fill: false,
                    tension: 0.2,
                    pointRadius: 1,
                    pointHoverRadius: 5,
                    spanGaps: true,
                    borderWidth: 2
                };
            })
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            interaction: {
                mode: 'nearest',
                axis: 'x',
                intersect: false
            },
            plugins: {
                legend: {
                    position: 'bottom',
                    labels: {
                        boxWidth: 12,
                        padding: 8,
                        font: { size: 11 }
                    }
                },
                tooltip: {
                    mode: 'index',
                    intersect: false,
                    itemSort: function(a, b) { return b.parsed.y - a.parsed.y; }
                }
            },
            scales: {
                y: {
                    beginAtZero: false,
                    title: { display: true, text: 'ELO' },
                    grid: { color: chartThemeColors().grid }
                },
                x: {
                    grid: { display: false },
                    ticks: { autoSkip: true, maxTicksLimit: 12 }
                }
            }
        }
    });
};

window.renderLineChart = function(canvasId, labels, data, label, color) {
    const ctx = document.getElementById(canvasId);
    if (!ctx) return;

    // Destroy existing chart if any
    if (ctx.chart) {
        ctx.chart.destroy();
    }

    // Ticks, legend and titles follow the active colour theme.
    Chart.defaults.color = chartThemeColors().tick;

    ctx.chart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [{
                label: label,
                data: data,
                borderColor: color,
                backgroundColor: color + '20',
                fill: true,
                tension: 0.3,
                pointRadius: 4,
                pointHoverRadius: 6
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    display: false
                }
            },
            scales: {
                y: {
                    beginAtZero: true,
                    grid: {
                        color: chartThemeColors().grid
                    }
                },
                x: {
                    grid: {
                        display: false
                    }
                }
            }
        }
    });
};
