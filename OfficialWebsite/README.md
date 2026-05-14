# DynamicWhere.ex — Official Documentation Site

Source for **doc.dynamicwhere.com**. Built with Next.js 15 (App Router), Tailwind v4, TypeScript, and Algolia DocSearch. Exports as a fully static site — no server required.

## Stack

- **Framework:** Next.js 15.1 (App Router, static export via `output: 'export'`)
- **Styling:** Tailwind CSS v4 (zero-config, inline `@theme`)
- **TypeScript:** 5.7
- **Search:** Algolia DocSearch (`@docsearch/react`)
- **Fonts:** Inter (sans), JetBrains Mono (mono) — loaded from Google Fonts
- **Theme:** Dark only. LTR. English only.

## Local development

```bash
cd "OfficialWebsite"
npm install
npm run dev
```

The dev server starts on http://localhost:3000.

## Production build (static export)

```bash
npm run build
```

Static output lands in `out/`. Deploy that folder to any static host:

- **Cloudflare Pages / Vercel / Netlify** — point at this folder and set the build command to `npm run build` with output directory `out`.
- **GitHub Pages** — push the `out/` folder to `gh-pages` branch.
- **Custom origin** — `aws s3 sync out/ s3://doc.dynamicwhere.com/ --delete`.

## Algolia DocSearch setup

1. Apply at [https://docsearch.algolia.com/apply](https://docsearch.algolia.com/apply) — free for open-source docs.
2. After approval you'll receive `appId`, `apiKey` (search-only), and `indexName`.
3. Add them as environment variables (or `.env.local`):

```
NEXT_PUBLIC_DOCSEARCH_APP_ID=YOUR_APP_ID
NEXT_PUBLIC_DOCSEARCH_API_KEY=YOUR_SEARCH_API_KEY
NEXT_PUBLIC_DOCSEARCH_INDEX_NAME=dynamicwhere
```

Until then the search button is rendered but clicks fail silently. Replace the defaults in `components/Search.tsx` if you prefer hardcoded values.

## Folder map

```
OfficialWebsite/
├── app/
│   ├── layout.tsx           Root layout + metadata + fonts
│   ├── page.tsx             Marketing landing page
│   ├── globals.css          Tailwind v4 + design tokens
│   ├── not-found.tsx        404 fallback
│   └── docs/                All documentation routes
│       ├── page.tsx                          /docs            Introduction
│       ├── installation/page.tsx             /docs/installation
│       ├── quick-start/page.tsx              /docs/quick-start
│       ├── enums/...                         /docs/enums/*
│       ├── classes/...                       /docs/classes/*
│       ├── extensions/...                    /docs/extensions/*
│       ├── validation/...                    /docs/validation/*
│       ├── examples/...                      /docs/examples/*
│       ├── cache/...                         /docs/cache/*
│       ├── errors/page.tsx                   /docs/errors
│       └── breaking-changes/page.tsx         /docs/breaking-changes
├── components/
│   ├── DocPage.tsx          Wrapper used by every doc route
│   ├── DocLayout.tsx        Sidebar + main grid
│   ├── Sidebar.tsx          Grouped nav (data-driven from lib/nav.ts)
│   ├── Header.tsx           Sticky header with search + repo link
│   ├── Footer.tsx           Site footer
│   ├── PageNav.tsx          Prev/Next nav cards
│   ├── Code.tsx             Code block with copy button
│   ├── Callout.tsx          info / note / warn / success / danger
│   └── Search.tsx           Algolia DocSearch
├── lib/
│   └── nav.ts               SITE metadata + flat nav tree
├── public/                  Static assets
├── PATTERN.md               Page authoring contract
├── next.config.mjs          Static export + trailing slash
├── tailwind.config           (not needed — Tailwind v4 picks up @theme from globals.css)
├── postcss.config.mjs
└── tsconfig.json
```

## Adding a new doc page

1. Pick the route, e.g. `/docs/extensions/new-thing`.
2. Create `app/docs/extensions/new-thing/page.tsx` following the template in `PATTERN.md`.
3. Register it in `lib/nav.ts` under the appropriate `NavGroup`.

That's it. Sidebar, prev/next nav, and search indexing pick it up automatically.

## Conventions

- **Strict dark mode.** No light theme. No theme toggle. All tokens live in `app/globals.css` under `@theme`.
- **LTR only.** `<html dir="ltr">`. No RTL helpers.
- **English only.** No i18n wiring.
- **Every page wraps content in `<DocPage pathname="...">`** — that powers prev/next nav.
- **Code blocks use `<Code lang="...">`** from `components/Code.tsx` — never raw `<pre>`.
- **JSX-safe generics:** write `IQueryable&lt;T&gt;` in JSX text. Inside `<Code>{\`...\`}</Code>` template literals you can write `IQueryable<T>` literally.

## Domain

Production target: **doc.dynamicwhere.com**. CNAME the apex of `dynamicwhere.com` (or `doc` subdomain) to your static host's edge.

## License

The library and these docs are released under "Free Forever" © Sajjad H. Al-Khafaji. See the [main repo](https://github.com/Sajadh92/DynamicWhere.ex).
