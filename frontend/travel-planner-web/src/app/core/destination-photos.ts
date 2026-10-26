/**
 * Self-hosted photography (public/images) from Wikimedia Commons, resized and recompressed.
 * Keys are catalog destination codes. Every photo must stay credited here: the footer lists these credits.
 */
export interface PhotoCredit {
  file: string;
  author: string;
  license: string;
  source: string;
}

export const PHOTOS: Record<string, PhotoCredit> = {
  hero: { file: 'images/hero.jpg', author: 'Nicola Cerroni', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Positano_panorama.jpg' },
  cta: { file: 'images/cta.jpg', author: 'kallerna', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Ponte_di_Rialto_Venice_1.jpg' },
  barcelona: { file: 'images/destinations/barcelona.jpg', author: 'Alexey Komarov', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Barcelona,_View_from_Bunkers_del_Carmel.jpg' },
  paris: { file: 'images/destinations/paris.jpg', author: 'Getfunky Paris', license: 'CC BY 2.0', source: 'https://commons.wikimedia.org/wiki/File:Eiffel_Tower_and_Pont_Alexandre_III_at_night.jpg' },
  london: { file: 'images/destinations/london.jpg', author: 'Colin', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Tower_Bridge_from_London_City_Hall_2015.jpg' },
  rome: { file: 'images/destinations/rome.jpg', author: 'Wilfredor', license: 'CC0', source: 'https://commons.wikimedia.org/wiki/File:Colosseum_of_Rome,_Italy.jpg' },
  amsterdam: { file: 'images/destinations/amsterdam.jpg', author: 'Basile Morin', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Colorful_windows_and_canal_houses_at_blue_hour_with_water_reflection_in_Damrak_Amsterdam_Netherlands.jpg' },
  prague: { file: 'images/destinations/prague.jpg', author: 'A.Savin', license: 'FAL', source: 'https://commons.wikimedia.org/wiki/File:Prague_07-2016_View_from_Petrinska_Tower_img2.jpg' },
  vienna: { file: 'images/destinations/vienna.jpg', author: 'Thomas Wolf, www.foto-tw.de', license: 'CC BY-SA 3.0 de', source: 'https://commons.wikimedia.org/wiki/File:Schloss_Sch%C3%B6nbrunn_Wien_2014_(Zuschnitt_1).jpg' },
  budapest: { file: 'images/destinations/budapest.jpg', author: 'Godot13', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:HUN-2015-Budapest-Hungarian_Parliament_(Budapest)_2015-02.jpg' },
  berlin: { file: 'images/destinations/berlin.jpg', author: 'Thomas Wolf, www.foto-tw.de', license: 'CC BY-SA 3.0', source: 'https://commons.wikimedia.org/wiki/File:Brandenburger_Tor_morgens.jpg' },
  lisbon: { file: 'images/destinations/lisbon.jpg', author: 'Matti Blume', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Alfama,_Lisbon_(DSC03373).jpg' },
  athens: { file: 'images/destinations/athens.jpg', author: 'A.Savin', license: 'CC BY-SA 3.0', source: 'https://commons.wikimedia.org/wiki/File:Attica_06-13_Athens_50_View_from_Philopappos_-_Acropolis_Hill.jpg' },
  milan: { file: 'images/destinations/milan.jpg', author: 'Steffen Schmitz', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Milano,_Duomo_with_Milan_Cathedral_and_Galleria_Vittorio_Emanuele_II,_2016.jpg' },
  madrid: { file: 'images/destinations/madrid.jpg', author: 'Nan Palmero', license: 'CC BY 2.0', source: 'https://commons.wikimedia.org/wiki/File:Madrid_Gran_Via_Metropolis_(28895530633).jpg' },
  munich: { file: 'images/destinations/munich.jpg', author: 'Srihari Thalla', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Ukrainian_flag_on_Neues_Rathaus,_Marienplatz,_Munich.jpg' },
  warsaw: { file: 'images/destinations/warsaw.jpg', author: 'Igor123121', license: 'CC BY 4.0', source: 'https://commons.wikimedia.org/wiki/File:Old_Town_Market_Square,_..2026,_Warsaw,_Poland.jpg' },
  krakow: { file: 'images/destinations/krakow.jpg', author: 'Zygmunt Put', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Krak%C3%B3w_Cloth_Hall,_Caf%C3%A9_Noworolski,_3_Main_Market_square,_Old_Town,_Krakow,_Poland.jpg' },
  istanbul: { file: 'images/destinations/istanbul.jpg', author: 'Hunanuk', license: 'CC0', source: 'https://commons.wikimedia.org/wiki/File:Historical_peninsula_and_modern_skyline_of_Istanbul.jpg' },
  copenhagen: { file: 'images/destinations/copenhagen.jpg', author: 'Jakub Hałun', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Nyhavn,_Copenhagen,_20220618_1728_7354.jpg' },
  venice: { file: 'images/destinations/venice.jpg', author: 'Martin Falbisoner', license: 'CC BY-SA 4.0', source: 'https://commons.wikimedia.org/wiki/File:Panorama_of_Canal_Grande_and_Ponte_di_Rialto,_Venice_-_September_2017.jpg' },
  larnaca: { file: 'images/destinations/larnaca.jpg', author: 'A.Savin', license: 'FAL', source: 'https://commons.wikimedia.org/wiki/File:Larnaca_01-2017_img26_Larnaca_Marina.jpg' },
};

export const HERO_PHOTO = PHOTOS['hero'].file;
export const HERO_SRCSET = 'images/hero-960.jpg 960w, images/hero.jpg 1920w';
export const CTA_PHOTO = PHOTOS['cta'].file;
export const CTA_SRCSET = 'images/cta-480.jpg 480w, images/cta.jpg 800w';

/** Full-size widths of the photos that are not 800 px wide. */
const FULL_WIDTH: Record<string, number> = { lisbon: 1280 };

/** Responsive candidates: a 480 px variant for phones and small cards, plus the full photo. */
export function destinationSrcset(code: string | null | undefined): string | null {
  const file = destinationPhoto(code);
  if (!file || !code) return null;
  return `${file.replace(/.jpg$/, '-480.jpg')} 480w, ${file} ${FULL_WIDTH[code] ?? 800}w`;
}

/** Relative image URL for a catalog destination code, or null when we have no photo for it. */
export function destinationPhoto(code: string | null | undefined): string | null {
  return (code && code !== 'hero' && code !== 'cta' && PHOTOS[code]?.file) || null;
}
