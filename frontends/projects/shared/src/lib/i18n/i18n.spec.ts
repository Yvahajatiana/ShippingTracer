import fr from '../../../i18n/fr.json';
import en from '../../../i18n/en.json';
import { formatDate, formatMoney, formatNumber } from './format.pipes';
import { deepMerge } from './translation.loader';

function keys(tree: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([key, value]) =>
    value && typeof value === 'object'
      ? keys(value as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );
}

describe('shared translations', () => {
  it('define the same keys in French and English', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });
});

describe('deepMerge', () => {
  it('lets portal keys override shared keys without dropping siblings', () => {
    const merged = deepMerge(
      { common: { save: 'Save', cancel: 'Cancel' } },
      { common: { save: 'Store' }, app: { title: 'X' } },
    );
    expect(merged).toEqual({ common: { save: 'Store', cancel: 'Cancel' }, app: { title: 'X' } });
  });
});

describe('formatters', () => {
  it('format money per locale', () => {
    expect(formatMoney(12.5, 'EUR', 'fr-FR')).toMatch(/12,50\s€/);
    expect(formatMoney(12.5, 'EUR', 'en-GB')).toBe('€12.50');
    expect(formatMoney(null, 'EUR', 'fr-FR')).toBe('');
  });

  it('keep DateOnly values on their calendar day', () => {
    expect(formatDate('2026-03-01', 'en-GB', 'date')).toBe('1 Mar 2026');
  });

  it('return empty text for missing or invalid dates', () => {
    expect(formatDate(null, 'fr-FR')).toBe('');
    expect(formatDate('not a date', 'fr-FR')).toBe('');
  });

  it('format numbers', () => {
    expect(formatNumber(1234.567, 'en-GB')).toBe('1,234.57');
  });
});
