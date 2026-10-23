import { Pipe, PipeTransform, inject } from '@angular/core';
import { categoryLabel } from './categories';
import { I18nService } from './i18n.service';

/** `{{ item.category | category }}` and `{{ list | category }}` (joined with ", "). */
@Pipe({ name: 'category', pure: false })
export class CategoryPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(value: string | readonly string[] | null | undefined): string {
    if (value === null || value === undefined) return '';
    const t = (k: Parameters<I18nService['t']>[0]) => this.i18n.t(k);
    return typeof value === 'string' ? categoryLabel(value, t) : value.map((c) => categoryLabel(c, t)).join(', ');
  }
}
