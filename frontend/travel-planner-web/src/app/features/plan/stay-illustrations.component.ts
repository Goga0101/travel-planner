import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslatePipe } from '../../core/i18n/translate.pipe';

/**
 * Decorative street of hotels between the hotel search and the hotel lists (design from Stitch).
 * Pointed at: the windows light up one by one, the taxi rolls on and the clouds drift.
 */
@Component({
  selector: 'app-hotel-scene',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="hs" aria-hidden="true">
      <svg class="hs-svg" viewBox="0 0 800 140" preserveAspectRatio="xMinYMid slice">
        <defs>
          <linearGradient id="hs-sky" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0%" stop-color="#bae6fd" stop-opacity="0.35" />
            <stop offset="100%" stop-color="#ccfbf1" stop-opacity="0.1" />
          </linearGradient>
        </defs>
        <rect width="800" height="140" fill="url(#hs-sky)" />

        <g class="hs-cloud" fill="#ffffff" opacity="0.85">
          <path d="M70,28 a10,10 0 0,1 18,-3 a14,14 0 0,1 24,4 a9,9 0 0,1 -2,17 l-38,0 a10,10 0 0,1 -2,-18 z" />
          <path d="M430,22 a8,8 0 0,1 14,-2 a11,11 0 0,1 20,3 a7,7 0 0,1 -2,13 l-30,0 a8,8 0 0,1 -2,-14 z" />
          <path d="M690,34 a8,8 0 0,1 14,-2 a11,11 0 0,1 20,3 a7,7 0 0,1 -2,13 l-30,0 a8,8 0 0,1 -2,-14 z" />
        </g>

        <rect x="0" y="122" width="800" height="18" fill="#e2e8f0" />
        <line x1="0" y1="122" x2="800" y2="122" stroke="#cbd5e1" stroke-width="1.5" />

        <circle cx="58" cy="100" r="16" fill="#14b8a6" opacity="0.8" />
        <rect x="56" y="108" width="4" height="14" fill="#0f766e" rx="1" />

        <!-- Boutique hotel with a sign and an awning -->
        <rect x="95" y="44" width="105" height="78" rx="2" fill="#0f172a" />
        <rect x="100" y="48" width="95" height="74" fill="#f8fafc" />
        <rect class="hs-sign" x="110" y="34" width="75" height="13" rx="3" fill="#0d9488" />
        <text x="147" y="43" fill="#ffffff" font-size="8" font-weight="800" text-anchor="middle" letter-spacing="1">HOTEL</text>
        <path d="M125,98 L170,98 L166,106 L129,106 Z" fill="#f97316" />
        <path d="M135,98 L144,98 L142,106 L133,106 Z" fill="#ffffff" />
        <path d="M152,98 L161,98 L159,106 L150,106 Z" fill="#ffffff" />
        <rect class="hs-win hs-w1" x="112" y="55" width="14" height="16" rx="2" />
        <rect class="hs-win hs-w2" x="140" y="55" width="14" height="16" rx="2" />
        <rect class="hs-win hs-w3" x="168" y="55" width="14" height="16" rx="2" />
        <rect class="hs-win hs-w4" x="112" y="78" width="14" height="16" rx="2" />
        <rect class="hs-win hs-w1" x="168" y="78" width="14" height="16" rx="2" />
        <rect x="139" y="104" width="16" height="18" fill="#115e59" rx="1" />

        <!-- Townhouse -->
        <rect x="220" y="60" width="74" height="62" rx="2" fill="#e6f6f4" />
        <rect x="220" y="60" width="74" height="6" fill="#115e59" />
        <rect class="hs-win hs-w2" x="232" y="74" width="12" height="14" rx="1" />
        <rect class="hs-win hs-w3" x="252" y="74" width="12" height="14" rx="1" />
        <rect class="hs-win hs-w4" x="272" y="74" width="12" height="14" rx="1" />
        <rect class="hs-win hs-w1" x="232" y="96" width="12" height="14" rx="1" />
        <rect class="hs-win hs-w2" x="272" y="96" width="12" height="14" rx="1" />

        <!-- Tower hotel -->
        <rect x="312" y="24" width="88" height="98" rx="2" fill="#0d9488" />
        <rect x="316" y="27" width="80" height="95" fill="#f1f5f9" />
        <rect class="hs-win hs-w1" x="326" y="36" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w2" x="349" y="36" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w3" x="372" y="36" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w4" x="326" y="54" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w1" x="349" y="54" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w2" x="372" y="54" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w3" x="326" y="72" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w4" x="349" y="72" width="13" height="11" rx="1" />
        <rect class="hs-win hs-w1" x="372" y="72" width="13" height="11" rx="1" />
        <rect x="344" y="102" width="24" height="20" fill="#0f172a" rx="1" />

        <!-- Street lamp -->
        <line x1="422" y1="78" x2="422" y2="122" stroke="#64748b" stroke-width="2" />
        <path d="M417,78 Q422,70 427,78 Z" fill="#475569" />
        <circle class="hs-lamp" cx="422" cy="80" r="3" fill="#fde047" />

        <g class="hs-cab">
          <rect x="450" y="112" width="38" height="9" rx="2" fill="#f97316" />
          <path d="M457,112 L462,106 L476,106 L481,112 Z" fill="#ea580c" />
          <rect x="466" y="103" width="7" height="3" fill="#0f172a" rx="0.5" />
          <circle cx="458" cy="122" r="3.5" fill="#0f172a" />
          <circle cx="478" cy="122" r="3.5" fill="#0f172a" />
          <circle cx="458" cy="122" r="1.5" fill="#f8fafc" />
          <circle cx="478" cy="122" r="1.5" fill="#f8fafc" />
        </g>

        <!-- Traveller with a suitcase -->
        <circle cx="560" cy="104" r="3.5" fill="#0f172a" />
        <rect x="558" y="108" width="4.5" height="10" rx="1" fill="#0d9488" />
        <rect x="568" y="112" width="7" height="10" rx="1.5" fill="#f97316" />
        <line x1="562" y1="113" x2="569" y2="112" stroke="#0f172a" stroke-width="1" />
        <circle cx="569" cy="122" r="1.2" fill="#0f172a" />
        <circle cx="574" cy="122" r="1.2" fill="#0f172a" />

        <circle cx="630" cy="98" r="18" fill="#0d9488" opacity="0.6" />
        <circle cx="648" cy="92" r="14" fill="#14b8a6" opacity="0.9" />
        <rect x="637" y="108" width="4" height="14" fill="#0f766e" rx="1" />
        <circle cx="742" cy="102" r="14" fill="#14b8a6" opacity="0.7" />
        <rect x="740" y="110" width="4" height="12" fill="#0f766e" rx="1" />
      </svg>
    </div>
  `,
})
export class HotelSceneComponent {}

/**
 * "Pick a hotel and Tripora shows you the way": a plane, a dashed route and the hotel with a pin (design from Stitch).
 * Pointed at: the plane moves along the route and the dashes run.
 */
@Component({
  selector: 'app-route-hint',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe],
  template: `
    <div class="rh">
      <div class="rh-illo" aria-hidden="true">
        <svg viewBox="0 0 160 64">
          <path class="rh-route" d="M 32 40 C 60 10, 100 12, 134 36" fill="none" stroke="#0d9488" stroke-width="2" stroke-linecap="round" stroke-dasharray="4 4" />
          <g transform="translate(132, 22)">
            <rect x="-8" y="14" width="16" height="14" rx="2" fill="#115e59" />
            <rect class="rh-win" x="-5" y="17" width="4" height="4" rx="0.5" />
            <rect class="rh-win" x="1" y="17" width="4" height="4" rx="0.5" />
            <rect x="-2" y="23" width="4" height="5" fill="#ffffff" rx="0.5" />
            <path class="rh-pin" d="M0,0 C-4.5,0 -8,3.5 -8,8 C-8,13.5 0,20 0,20 C0,20 8,13.5 8,8 C8,3.5 4.5,0 0,0 Z" fill="#f97316" />
            <circle cx="0" cy="7.5" r="2.8" fill="#ffffff" />
          </g>
          <!-- Position in the outer group, motion in the inner one: a CSS transform would replace the SVG attribute. -->
          <g transform="translate(24, 38) rotate(-18)">
            <g class="rh-plane">
              <path d="M-10,0 L6,-7 L12,-7 L2,0 L11,4 L8,6 L0,2 L-6,7 L-9,6 L-6,1 Z" fill="#f97316" />
              <circle cx="3" cy="-1" r="1" fill="#ffffff" />
            </g>
          </g>
        </svg>
      </div>
      <p class="rh-text">{{ 'nearby.guideHint' | t }}</p>
    </div>
  `,
})
export class RouteHintComponent {}
