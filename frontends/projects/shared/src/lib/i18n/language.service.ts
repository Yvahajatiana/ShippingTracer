import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { BrnCalendarI18n, MonthLabels, injectBrnCalendarI18n } from '@spartan-ng/brain/calendar';
import { firstValueFrom } from 'rxjs';

export const LANGUAGES = ['fr', 'en'] as const;
export type Language = (typeof LANGUAGES)[number];

const STORAGE_KEY = 'pf.lang';
const INTL_LOCALES: Record<Language, string> = { fr: 'fr-FR', en: 'en-GB' };
const NAVIGATION_LABELS: Record<Language, { previous: string; next: string }> = {
  fr: { previous: 'Mois précédent', next: 'Mois suivant' },
  en: { previous: 'Previous month', next: 'Next month' },
};

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value);
}

function initialLanguage(): Language {
  try {
    const stored = localStorage.getItem(STORAGE_KEY);
    if (isLanguage(stored)) {
      return stored;
    }
  } catch {
    // Storage unavailable: fall back to the browser language.
  }
  return typeof navigator !== 'undefined' && navigator.language?.toLowerCase().startsWith('en')
    ? 'en'
    : 'fr';
}

/** Calendar texts built from Intl so the date pickers follow the UI language. */
export function calendarI18n(language: Language): Partial<BrnCalendarI18n> {
  const locale = INTL_LOCALES[language];
  // 2024-01-01 is a Monday; index 0 is Sunday as in Date#getDay.
  const weekday = (index: number, style: 'short' | 'long') =>
    new Intl.DateTimeFormat(locale, { weekday: style, timeZone: 'UTC' }).format(
      Date.UTC(2023, 11, 31 + index),
    );
  const monthName = (month: number) =>
    new Intl.DateTimeFormat(locale, { month: 'long', timeZone: 'UTC' }).format(
      Date.UTC(2024, month, 1),
    );
  return {
    formatWeekdayName: (index) => weekday(index, 'short'),
    labelWeekday: (index) => weekday(index, 'long'),
    formatHeader: (month, year) =>
      new Intl.DateTimeFormat(locale, { month: 'long', year: 'numeric', timeZone: 'UTC' }).format(
        Date.UTC(year, month, 1),
      ),
    formatMonth: monthName,
    formatYear: (year) => String(year),
    months: () => Array.from({ length: 12 }, (_, month) => monthName(month)) as MonthLabels,
    labelPrevious: () => NAVIGATION_LABELS[language].previous,
    labelNext: () => NAVIGATION_LABELS[language].next,
    // Both supported locales (fr-FR, en-GB) start the week on Monday.
    firstDayOfWeek: () => 1,
  };
}

/** Single source of truth for the UI language: Transloco, calendar texts, `<html lang>` and Intl formatting. */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly transloco = inject(TranslocoService);
  private readonly calendar = injectBrnCalendarI18n();
  private readonly document = inject(DOCUMENT);
  private readonly current = signal<Language>(initialLanguage());

  readonly language = this.current.asReadonly();
  readonly locale = computed(() => INTL_LOCALES[this.current()]);

  /** Loads the initial language before the first render. */
  async init(): Promise<void> {
    await this.apply(this.current());
  }

  async use(language: Language): Promise<void> {
    if (language === this.current()) {
      return;
    }
    await this.apply(language);
    this.current.set(language);
    try {
      localStorage.setItem(STORAGE_KEY, language);
    } catch {
      // Preference is kept for the session only.
    }
  }

  private async apply(language: Language): Promise<void> {
    await firstValueFrom(this.transloco.load(language));
    this.transloco.setActiveLang(language);
    this.calendar.use(calendarI18n(language));
    this.document.documentElement.lang = language;
  }
}
