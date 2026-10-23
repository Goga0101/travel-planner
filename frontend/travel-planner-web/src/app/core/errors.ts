import { HttpErrorResponse } from '@angular/common/http';
import { TranslationKey } from './i18n/en';
import { ProblemDetails } from './models';

/**
 * A user-safe error. Client-generated messages are translation keys (rendered in the current language);
 * server problem details are already localized by the API (Accept-Language) and shown as-is.
 */
export interface ApiError {
  status: number;
  title?: string;
  titleKey?: TranslationKey;
  detail?: string;
  detailKey?: TranslationKey;
  fieldErrors: Record<string, string[]>;
}

/** Converts any HTTP failure into a user-safe message (uses RFC 7807 problem details when present). */
export function toApiError(error: unknown): ApiError {
  if (error instanceof HttpErrorResponse) {
    const body = (typeof error.error === 'object' && error.error ? error.error : {}) as ProblemDetails;
    if (error.status === 0) {
      return { status: 0, titleKey: 'error.unreachable', detailKey: 'error.checkConnection', fieldErrors: {} };
    }
    if (error.status === 429) {
      return { status: 429, titleKey: 'error.tooMany', detailKey: 'error.waitMoment', fieldErrors: {} };
    }
    return {
      status: error.status,
      title: body.title,
      titleKey: body.title ? undefined : error.status >= 500 ? 'error.server' : 'error.request',
      detail: body.detail,
      fieldErrors: body.errors ?? {},
    };
  }
  return { status: -1, titleKey: 'error.unexpected', fieldErrors: {} };
}
