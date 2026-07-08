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
// as data-bs-theme on <html>. The no-flash bootstrap of this lives inline in
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
        document.documentElement.setAttribute('data-bs-theme', resolve(pref));
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

// Theme-aware colours for Chart.js (grid lines, axis ticks, pie borders) read
// at render time from the active data-bs-theme.
function chartThemeColors() {
    var dark = document.documentElement.getAttribute('data-bs-theme') === 'dark';
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
