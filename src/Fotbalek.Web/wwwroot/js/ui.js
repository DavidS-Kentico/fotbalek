// Dropdown menus and collapsible regions.
//
// Replaces the only two behaviours the app used Bootstrap's JS for. Driven by
// data attributes and delegated from the document, which matters for Blazor:
// nothing is "initialised", so menus keep working after any re-render, and menus
// whose contents update live (the presence list, the live-game viewers) do not
// close underneath the user.
//
//   <button data-toggle="dropdown">      toggles the nearest .dropdown ancestor
//   <button data-toggle="collapse" data-target="#id">   toggles #id
//
// Open state lives in the DOM as a `.show` class rather than in component state,
// for the same reason: a Blazor render must not be able to disturb it.
(function () {
    'use strict';

    var OPEN = 'show';

    function menuOf(dropdown) {
        return dropdown.querySelector('.dropdown-menu');
    }

    function close(dropdown) {
        if (!dropdown.classList.contains(OPEN)) return;
        dropdown.classList.remove(OPEN);
        var menu = menuOf(dropdown);
        if (menu) menu.classList.remove(OPEN);
        var trigger = dropdown.querySelector('[data-toggle="dropdown"]');
        if (trigger) trigger.setAttribute('aria-expanded', 'false');
    }

    function closeAll(except) {
        var open = document.querySelectorAll('.dropdown.' + OPEN);
        for (var i = 0; i < open.length; i++) {
            if (open[i] !== except) close(open[i]);
        }
    }

    function open(dropdown) {
        closeAll(dropdown);
        dropdown.classList.add(OPEN);
        var menu = menuOf(dropdown);
        if (menu) menu.classList.add(OPEN);
        var trigger = dropdown.querySelector('[data-toggle="dropdown"]');
        if (trigger) trigger.setAttribute('aria-expanded', 'true');
    }

    document.addEventListener('click', function (e) {
        var target = e.target instanceof Element ? e.target : null;
        if (!target) return;

        // --- dropdown trigger ---
        var trigger = target.closest('[data-toggle="dropdown"]');
        if (trigger) {
            e.preventDefault();
            var dropdown = trigger.closest('.dropdown, .btn-group, .nav-item');
            if (dropdown) {
                if (dropdown.classList.contains(OPEN)) close(dropdown);
                else open(dropdown);
            }
            return;
        }

        // --- collapse trigger ---
        var collapseBtn = target.closest('[data-toggle="collapse"]');
        if (collapseBtn) {
            e.preventDefault();
            var sel = collapseBtn.getAttribute('data-target');
            var region = sel && document.querySelector(sel);
            if (region) {
                var nowOpen = region.classList.toggle(OPEN);
                collapseBtn.setAttribute('aria-expanded', nowOpen ? 'true' : 'false');
            }
            return;
        }

        // --- click inside an open menu ---
        // Menu items that navigate or act should dismiss the menu; a click on
        // inert chrome (a heading, a divider) should not.
        var insideMenu = target.closest('.dropdown-menu');
        if (insideMenu) {
            if (target.closest('.dropdown-item, a[href], button:not([data-keep-open])')) {
                var owner = insideMenu.closest('.dropdown, .btn-group, .nav-item');
                if (owner) close(owner);
            }
            return;
        }

        // --- click anywhere else ---
        closeAll(null);
    });

    // Escape dismisses every open menu. Focus goes back to the trigger of the menu
    // the user was actually inside, so a keyboard user is not dropped at the top of
    // the document with no idea where they were.
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;

        var active = document.activeElement;
        var owner = active && active.closest
            ? active.closest('.dropdown.' + OPEN + ', .btn-group.' + OPEN + ', .nav-item.' + OPEN)
            : null;

        closeAll(null);

        if (owner) {
            var trigger = owner.querySelector('[data-toggle="dropdown"]');
            if (trigger) trigger.focus();
        }
    });
})();
