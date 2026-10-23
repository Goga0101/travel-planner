import { formatNumber } from '@angular/common';
import { Pipe, PipeTransform, inject } from '@angular/core';
import { formatDistance, formatLocalDate, formatMinutes, formatMoney, formatTimeSpan } from '../core/format';
import { I18nService } from '../core/i18n/i18n.service';

// All pipes are impure and read the language signal, so they re-render when the language changes.

@Pipe({ name: 'money', pure: false })
export class MoneyPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(amount: number | null | undefined, currency: string | null | undefined): string {
    return formatMoney(amount, currency, this.i18n.lang());
  }
}

@Pipe({ name: 'minutes', pure: false })
export class MinutesPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(minutes: number | null | undefined): string {
    return formatMinutes(minutes, this.i18n.lang());
  }
}

@Pipe({ name: 'timespan', pure: false })
export class TimeSpanPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(value: string | null | undefined): string {
    return formatTimeSpan(value, this.i18n.lang());
  }
}

@Pipe({ name: 'distance', pure: false })
export class DistancePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(meters: number | null | undefined): string {
    return formatDistance(meters, this.i18n.lang());
  }
}

@Pipe({ name: 'stops', pure: false })
export class StopsPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(stops: number | null | undefined): string {
    if (stops === null || stops === undefined) return '';
    return stops === 0 ? this.i18n.t('stops.direct') : this.i18n.t('stops.count', { count: stops });
  }
}

/** Locale-aware replacement for DatePipe: `{{ value | ldate: 'd MMM' }}`. */
@Pipe({ name: 'ldate', pure: false })
export class LocalDatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(value: string | number | Date | null | undefined, format = 'mediumDate'): string {
    return formatLocalDate(value, format, this.i18n.lang());
  }
}

/** Locale-aware replacement for DecimalPipe: `{{ value | lnumber: '1.2-2' }}`. */
@Pipe({ name: 'lnumber', pure: false })
export class LocalNumberPipe implements PipeTransform {
  private readonly i18n = inject(I18nService);
  transform(value: number | null | undefined, digitsInfo?: string): string {
    if (value === null || value === undefined || !Number.isFinite(value)) return '';
    return formatNumber(value, this.i18n.lang(), digitsInfo);
  }
}
