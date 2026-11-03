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

describe('admin portal translations', () => {
  it('define the same keys in French and English', () => {
    expect(keys(en).sort()).toEqual(keys(fr).sort());
  });

  it('use the same interpolation parameters in both languages', () => {
    const params = (tree: Record<string, unknown>) =>
      keys(tree).map((key) => {
        const text = key
          .split('.')
          .reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], tree) as string;
        return `${key}:${[...text.matchAll(/{{\s*(\w+)\s*}}/g)]
          .map((m) => m[1])
          .sort()
          .join(',')}`;
      });
    expect(params(en).sort()).toEqual(params(fr).sort());
  });

  it('have no empty texts', () => {
    expect([...values(fr), ...values(en)].every((text) => text.trim().length > 0)).toBe(true);
  });
});
