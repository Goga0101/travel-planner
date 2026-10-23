import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslationKey } from './en';
import { I18nService, TranslateParams } from './i18n.service';

/**
 * `{{ 'search.submit' | t }}` / `{{ 'unit.nights' | t: { count: n } }}`.
 * Impure so it re-evaluates when the language signal changes (the read is tracked by the template).
 */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: TranslationKey, params?: TranslateParams): string {
    return this.i18n.t(key, params);
  }
}
