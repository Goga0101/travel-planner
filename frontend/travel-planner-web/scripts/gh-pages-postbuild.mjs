// Prepares the production build for GitHub Pages (project site under /travel-planner/):
//  - 404.html = index.html so deep links such as /travel-planner/ka/plans/<id> load the SPA
//  - .nojekyll so GitHub serves files as-is
//  - config.json with the API address: API_BASE_URL env var, else gh-pages-api-url.txt, else the local API
import { copyFileSync, existsSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const dist = join('dist', 'travel-planner-web', 'browser');
const urlFile = 'gh-pages-api-url.txt';
const fromFile = existsSync(urlFile) ? readFileSync(urlFile, 'utf8').trim() : '';
const apiBaseUrl = process.env.API_BASE_URL || fromFile || 'http://localhost:5080';

copyFileSync(join(dist, 'index.html'), join(dist, '404.html'));
writeFileSync(join(dist, '.nojekyll'), '');
writeFileSync(join(dist, 'config.json'), JSON.stringify({ apiBaseUrl }, null, 2) + '\n');
console.log(`GitHub Pages build ready in ${dist} (apiBaseUrl: ${apiBaseUrl})`);
