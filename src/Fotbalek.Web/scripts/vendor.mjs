// Copies third-party browser assets out of node_modules into wwwroot/lib.
//
// Everything the browser loads is self-hosted — no CDN. That keeps the app
// working offline-ish for the PWA shell, avoids a third-party request on every
// page load, and means a dependency can only change when package-lock.json does.
//
// Run `npm run vendor` after changing a pinned version in package.json, then
// commit the result: wwwroot/lib is checked in so a build needs no Node.
//
// Not covered here: emoji-picker-element and signalr, which were vendored by
// hand before this script existed. Fold them in when either is next bumped.

import { copyFileSync, cpSync, mkdirSync } from 'node:fs';

const LIB = 'wwwroot/lib';

/** @type {{name: string, dest: string, files: [string, string][], dirs?: [string, string][]}[]} */
const packages = [
    {
        name: 'bootstrap-icons',
        dest: `${LIB}/bootstrap-icons`,
        files: [
            ['node_modules/bootstrap-icons/font/bootstrap-icons.min.css', 'bootstrap-icons.min.css'],
            ['node_modules/bootstrap-icons/LICENSE', 'LICENSE'],
        ],
        dirs: [['node_modules/bootstrap-icons/font/fonts', 'fonts']],
    },
    {
        name: 'chart.js',
        dest: `${LIB}/chart.js`,
        files: [
            // The package ships no pre-minified UMD build; the CDN was minifying
            // on the fly. Serving the full build is fine — MapStaticAssets
            // pre-compresses static assets at build time.
            ['node_modules/chart.js/dist/chart.umd.js', 'chart.umd.js'],
            ['node_modules/chart.js/LICENSE.md', 'LICENSE.md'],
        ],
    },
];

for (const pkg of packages) {
    mkdirSync(pkg.dest, { recursive: true });
    for (const [from, to] of pkg.files) {
        copyFileSync(from, `${pkg.dest}/${to}`);
    }
    for (const [from, to] of pkg.dirs ?? []) {
        cpSync(from, `${pkg.dest}/${to}`, { recursive: true });
    }
    console.log(`vendored ${pkg.name} -> ${pkg.dest}`);
}
