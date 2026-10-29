import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18nService } from '../../core/i18n/i18n.service';

/**
 * Privacy policy: what signing in with Google / Facebook shares, what is kept and how to delete it. Google and Facebook
 * require a public page like this before the sign-in apps can go live (Facebook also links it as data-deletion instructions).
 */
@Component({
  selector: 'app-privacy-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host { display: block; }
    .legal { max-width: 46rem; margin: 0 auto; padding: 2.5rem 0 3.5rem; }
    .legal h1 { margin: 0 0 0.3rem; }
    .legal .updated { color: var(--muted); font-size: 0.88rem; margin: 0 0 2rem; }
    .legal h2 { font-size: 1.15rem; margin: 2rem 0 0.5rem; color: var(--navy); }
    .legal p, .legal li { line-height: 1.65; color: var(--text-2); }
    .legal ul { padding-left: 1.2rem; }
    .legal .box { padding: 1rem 1.15rem; border-radius: var(--radius-md); background: var(--surface-tint); border: 1px solid var(--border); }
  `,
  template: `
    <article class="container legal" id="privacy">
      @if (i18n.lang() === 'ka') {
        <h1>კონფიდენციალურობის პოლიტიკა</h1>
        <p class="updated">განახლდა: 2026 წლის 29 ოქტომბერი</p>
        <p>Tripora მოგზაურობის დაგეგმვაში გეხმარებათ: ეძებს ფრენებს, სასტუმროებს და ტრანსპორტს და ოფიციალურ ჯავშნის საიტებზე გადაგამისამართებთ.
          აქ აღწერილია, რა მონაცემებს ვიღებთ და რას ვაკეთებთ მათთან.</p>

        <h2>რას ვიღებთ, როცა Google-ით ან Facebook-ით შედიხართ</h2>
        <ul>
          <li>თქვენს სახელს და პროფილის ფოტოს;</li>
          <li>ელფოსტას (თუ პროვაიდერი გაგვიზიარებს და ის დადასტურებულია);</li>
          <li>პროვაიდერის მიერ მოცემულ ანგარიშის იდენტიფიკატორს, რომ შემდეგ ჯერზე იგივე ანგარიში გიცნოთ.</li>
        </ul>
        <p>Google-ისა და Facebook-ის პაროლს ვერასდროს ვხედავთ. თქვენი სახელით არაფერს ვაქვეყნებთ, თქვენს მეგობრებს ან კონტაქტებს არ ვკითხულობთ.</p>

        <h2>რისთვის ვიყენებთ</h2>
        <p>მხოლოდ თქვენი ანგარიშისთვის: რომ შეხვიდეთ და საიტზე თქვენი სახელი და ფოტო გამოჩნდეს (და მომავალში — თქვენი შენახული მოგზაურობები
          ყველა მოწყობილობაზე). მონაცემებს არ ვყიდით, რეკლამისთვის არ ვიყენებთ და მესამე მხარეს არ გადავცემთ.</p>

        <h2>სად ინახება</h2>
        <p>ანგარიში ინახება ჩვენს სერვერზე, მონაცემთა ბაზაში. შესვლის შემდეგ თქვენი ბრაუზერი ინახავს ჩვენს ხელმოწერილ ბილეთს (token), რომელიც 30 დღე მოქმედებს
          და „გასვლით“ იშლება. შენახული მოგზაურობები და ბოლო ძიებები ახლა მხოლოდ თქვენს ბრაუზერშია.</p>

        <h2>ქუქიები და გარე სერვისები</h2>
        <p>ჩვენ რეკლამის ან თვალთვალის ქუქიებს არ ვიყენებთ. როცა შესვლის ღილაკს ხსნით, Google-ისა და Facebook-ის შესვლის სკრიპტები იტვირთება და მათ
          საკუთარი წესები აქვთ. ფრენებისა და სასტუმროების ბმულები გარე საიტებზე (მაგ. Aviasales, Booking.com) გადაგიყვანთ, სადაც მათი პოლიტიკა მოქმედებს.</p>

        <h2 id="delete">როგორ წავშალოთ მონაცემები</h2>
        <div class="box">
          <p>შედით საიტზე → ზედა მარჯვენა კუთხეში დააჭირეთ თქვენს ფოტოს → <strong>„ანგარიშის წაშლა“</strong> → დაადასტურეთ.
            ანგარიში და მასთან დაკავშირებული ყველა მონაცემი მაშინვე წაიშლება.</p>
          <p>შეგიძლიათ Tripora-ს წვდომა თავად პროვაიდერთანაც გააუქმოთ: Google-ის ანგარიშში („Third-party apps & services“) ან Facebook-ში
            („Settings → Apps and Websites“).</p>
        </div>
      } @else {
        <h1>Privacy policy</h1>
        <p class="updated">Updated: 29 October 2026</p>
        <p>Tripora helps you plan trips: it searches flights, stays and transport and sends you to the official booking sites.
          This page explains what data we receive and what we do with it.</p>

        <h2>What we receive when you sign in with Google or Facebook</h2>
        <ul>
          <li>your name and profile photo;</li>
          <li>your email address (when the provider shares it and it is verified);</li>
          <li>the account id the provider gives us, so we recognise the same account next time.</li>
        </ul>
        <p>We never see your Google or Facebook password. We never post anything on your behalf and do not read your friends or contacts.</p>

        <h2>What we use it for</h2>
        <p>Only for your account: to sign you in and show your name and photo (and, later, your saved trips on all your devices). We do not
          sell the data, use it for advertising or pass it to third parties.</p>

        <h2>Where it is kept</h2>
        <p>Your account is stored in our database. After signing in, your browser keeps our signed token, valid for 30 days and removed when you
          sign out. Saved trips and recent searches currently stay in your browser only.</p>

        <h2>Cookies and other services</h2>
        <p>We use no advertising or tracking cookies. When you open the sign-in panel, Google's and Facebook's sign-in scripts load, under
          their own policies. Flight and hotel links take you to other sites (e.g. Aviasales, Booking.com) whose policies apply there.</p>

        <h2 id="delete">How to delete your data</h2>
        <div class="box">
          <p>Sign in → click your photo at the top right → <strong>“Delete account”</strong> → confirm. Your account and everything linked to it
            is deleted immediately.</p>
          <p>You can also remove Tripora's access at the provider: in your Google Account (“Third-party apps &amp; services”) or on Facebook
            (“Settings → Apps and Websites”).</p>
        </div>
      }
    </article>
  `,
})
export class PrivacyPage {
  protected readonly i18n = inject(I18nService);
}
