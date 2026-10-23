import { Pipe, PipeTransform, inject } from '@angular/core';
import { LanguageService } from './language.service';

export type DateStyle = 'date' | 'dateTime' | 'time';

const DATE_OPTIONS: Record<DateStyle, Intl.DateTimeFormatOptions> = {
  date: { dateStyle: 'medium' },
  dateTime: { dateStyle: 'medium', timeStyle: 'short' },
  time: { timeStyle: 'short' },
};

/** `YYYY-MM-DD` values are calendar dates and must not shift with the viewer's time zone. */
const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

export function formatDate(
  value: string | Date | null | undefined,
  locale: string,
  style: DateStyle = 'dateTime',
) {
  if (value === null || value === undefined || value === '') {
    return '';
  }
  const dateOnly = typeof value === 'string' && DATE_ONLY.test(value);
  const date = dateOnly ? new Date(`${value}T00:00:00Z`) : new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(locale, {
    ...DATE_OPTIONS[style],
    ...(dateOnly ? { timeZone: 'UTC' } : {}),
  }).format(date);
}

export function formatMoney(
  amount: number | null | undefined,
  currency: string,
  locale: string,
): string {
  if (amount === null || amount === undefined) {
    return '';
  }
  return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount);
}

export function formatNumber(
  value: number | null | undefined,
  locale: string,
  maximumFractionDigits = 2,
): string {
  if (value === null || value === undefined) {
    return '';
  }
  return new Intl.NumberFormat(locale, { maximumFractionDigits }).format(value);
}

// The pipes are impure so they re-render when the language signal changes; Intl formatting is cheap.

@Pipe({ name: 'pfDate', pure: false })
export class PfDatePipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  transform(value: string | Date | null | undefined, style: DateStyle = 'dateTime'): string {
    return formatDate(value, this.language.locale(), style);
  }
}

@Pipe({ name: 'pfMoney', pure: false })
export class PfMoneyPipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  transform(amount: number | null | undefined, currency = 'EUR'): string {
    return formatMoney(amount, currency, this.language.locale());
  }
}

@Pipe({ name: 'pfNumber', pure: false })
export class PfNumberPipe implements PipeTransform {
  private readonly language = inject(LanguageService);
  transform(value: number | null | undefined, maximumFractionDigits = 2): string {
    return formatNumber(value, this.language.locale(), maximumFractionDigits);
  }
}
