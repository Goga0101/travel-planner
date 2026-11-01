# World news: work plan

The world-news-editor agent's running plan. Every run: read this file, do the tasks under **Next run** (top to
bottom, as many as fit), tick them off, move what's done to **Done** with the date, write new tasks you discovered
under **Backlog**, then refill **Next run** with the 3-5 most useful tasks for the run after. Then stop.

## Next run

- [ ] **Spain strike day:** before 11 November, look on RTVE (read with curl, see Backlog) for minimum services
      (Renfe, Madrid/Barcelona metro, Aena) for the general strike and update `es-2026-10-22-general-strike-11-november`
      (same id). Also check whether Renfe resumes Madrid–Andalusia sales earlier than December.
- [ ] **Christmas markets, second try (mid-November):** opening dates for Prague (ČT24), Budapest (MTI), Kraków
      (Polskie Radio), Berlin and Munich (tagesschau, read with curl). Only dates the broadcaster itself reports.
- [ ] **Thin countries:** JP (NHK feed is general news; try NHK World EN, which timed out from here), AE (WAM
      articles are JavaScript-only; find a way to read an article's text or skip AE), CY, HU, MA, MX: one strong story
      each if there is one.
- [ ] **Refresh:** fetch feeds again (`--days 4`) and add new travel-relevant stories (updates reuse the id).

## Backlog

- Bernama (MY) feed: titles carry a "General :" prefix and items have no dates; consider another MY source.
- Polskie Radio "395" section mixes in republished foreign stories: check every PL item is about Poland.
- BBC, CNA, KBS and Rai articles can't be opened with WebFetch; look for article pages or alternative
  feeds of the same broadcaster that can be checked. **tagesschau** and **RTVE** article pages read fine with plain
  `fetch`/curl (bot user agent), so check them that way.
- Japan: GDACS earthquake alerts for Japan only when orange/red.
- ES: RTVE has no working feed (api2.rtve.es/rss is frozen in 2022); the www.rtve.es/noticias/ page and articles
  are readable with curl. Spanish general election on 29 November: follow transport/strike news around it.
- HU: hirado.hu suspended its news service on 7 July 2026 and blocks bots; switched to MTI (mti.hu), which shows only
  a few free headlines a day. Skip MTI's "Nemzeti Közleménytár" items: they are press releases, not MTI reporting.
- CY: CyBC news moved to news.rik.cy (web only, no feed); few travel stories so far.
- AE: WAM's headlines are readable at https://www.wam.ae/en/sitemap/news.xml, but article pages need JavaScript,
  so facts can't be checked; no AE story until that is solved.
- EG: Ahram Online blocks all requests (403); EG now uses the State Information Service (sis.gov.eg/en, articles open
  with WebFetch). MENA (mena.org.eg) is Arabic only.
- MA: MAP blocks (403); MA now uses SNRTnews (snrtnews.com/fr). WebFetch gets 403 but curl works; the homepage embeds
  article JSON. Most current coverage is political (Sahara); watch for holidays (Green March 6 November,
  Independence Day 18 November).
- CA: CBC English feed and site time out / 403 for bots; switched to ICI Radio-Canada (same public broadcaster, French).
- MX: Canal Once has a working feed (oncenoticias.digital/feed/), added; only ~10 items, mostly national politics.
- DE: "Gen Z regelt" youth protests (31 October, riots in Frankfurt in the evening); organisers plan more rallies
  "in three weeks" (around 21 November): add a story once a date and cities are confirmed.

## Done

- 2026-11-01: First edition: 21 stories from 16 countries. Fixed NHK domain (`web.nhk`), Canal Once moved to
  `oncenoticias.digital`, http feed links upgraded to https.
- 2026-11-01 (run 2): **Fixed blocked sources:** CA → ICI Radio-Canada feed; HU → MTI; CY → news.rik.cy; EG → SIS;
  MA → SNRTnews; MX feed added; ES kept RTVE (readable with curl); AE kept WAM (headlines only, via news sitemap).
- 2026-11-01 (run 2): **Skipped countries:** ES 2 (general strike 11 Nov; Madrid–Andalusia line closed, update of
  the Toledo crash story), CA 1 (BC time zone), EG 1 (Grand Egyptian Museum). Nothing travel-relevant for HU, CY,
  MA, MX, JP; AE not verifiable.
- 2026-11-01 (run 2): **Events sweep:** Vienna Christmas markets (6 and 13 Nov, ORF). Prague, Budapest, Kraków,
  Berlin, Munich: no 2026 dates reported by the broadcasters yet.
- 2026-11-01 (run 2): **Refresh:** ZA local elections, 4 Nov public holiday (SABC). 26 stories from 20 countries now.
