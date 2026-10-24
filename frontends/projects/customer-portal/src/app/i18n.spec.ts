import en from '../../public/i18n/en.json';
import fr from '../../public/i18n/fr.json';
import { NAVIGATION } from './navigation';

function keys(tree: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([key, value]) =>
    value && typeof value === 'object'
      ? keys(value as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );
}

function lookup(tree: Record<string, unknown>, key: string): unknown {
  return key
    .split('.')
    .reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], tree);
}

function placeholders(text: unknown): string[] {
  return [...String(text).matchAll(/\{\{\s*(\w+)\s*\}\}/g)].map((match) => match[1]).sort();
}

describe('customer portal translations', () => {
  it('define the same keys in French and English', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('use the same interpolation parameters in both languages', () => {
    for (const key of keys(fr)) {
      expect(placeholders(lookup(en, key)), key).toEqual(placeholders(lookup(fr, key)));
    }
  });

  it('have no empty text', () => {
    for (const key of [...keys(fr), ...keys(en)]) {
      expect(String(lookup(fr, key) ?? lookup(en, key)).trim(), key).not.toBe('');
    }
  });

  it('translate every navigation entry', () => {
    for (const item of NAVIGATION) {
      expect(lookup(fr, item.labelKey), item.labelKey).toBeTypeOf('string');
    }
  });
});
