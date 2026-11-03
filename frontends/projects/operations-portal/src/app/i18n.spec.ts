import en from '../../public/i18n/en.json';
import fr from '../../public/i18n/fr.json';

function keys(tree: Record<string, unknown>, prefix = ''): string[] {
  return Object.entries(tree).flatMap(([key, value]) =>
    value && typeof value === 'object'
      ? keys(value as Record<string, unknown>, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );
}

function values(tree: Record<string, unknown>): string[] {
  return Object.values(tree).flatMap((value) =>
    value && typeof value === 'object' ? values(value as Record<string, unknown>) : [String(value)],
  );
}

describe('operations portal translations', () => {
  it('define the same keys in French and English', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('use the same interpolation parameters in both languages', () => {
    const params = (tree: Record<string, unknown>) =>
      Object.fromEntries(
        keys(tree).map((key) => {
          const text = key
            .split('.')
            .reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], tree);
          return [key, [...String(text).matchAll(/\{\{\s*(\w+)\s*\}\}/g)].map((m) => m[1]).sort()];
        }),
      );
    expect(params(en)).toEqual(params(fr));
  });

  it('have no empty text', () => {
    expect(values(fr).filter((text) => text.trim() === '')).toEqual([]);
    expect(values(en).filter((text) => text.trim() === '')).toEqual([]);
  });
});
