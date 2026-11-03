import { firstValueFrom, of } from 'rxjs';
import { Page } from '../api/models';
import { addDays, daysBetween } from './dates';
import { fetchAllPages } from './paging';

function source(total: number) {
  const calls: [number, number][] = [];
  const all = Array.from({ length: total }, (_, index) => index);
  const fetchPage = (page: number, pageSize: number) => {
    calls.push([page, pageSize]);
    const items = all.slice((page - 1) * pageSize, page * pageSize);
    return of<Page<number>>({ page, pageSize, total, items });
  };
  return { calls, fetchPage };
}

describe('fetchAllPages', () => {
  it('loads pages sequentially until the total is reached', async () => {
    const { calls, fetchPage } = source(250);
    const result = await firstValueFrom(fetchAllPages(fetchPage));
    expect(calls).toEqual([
      [1, 100],
      [2, 100],
      [3, 100],
    ]);
    expect(result.items).toHaveLength(250);
    expect(result).toMatchObject({ total: 250, complete: true });
  });

  it('stops after one request when everything fits in the first page', async () => {
    const { calls, fetchPage } = source(3);
    const result = await firstValueFrom(fetchAllPages(fetchPage));
    expect(calls).toHaveLength(1);
    expect(result).toEqual({ items: [0, 1, 2], total: 3, complete: true });
  });

  it('honours the page bound and reports a partial result', async () => {
    const { calls, fetchPage } = source(50);
    const result = await firstValueFrom(fetchAllPages(fetchPage, { pageSize: 10, maxPages: 2 }));
    expect(calls).toHaveLength(2);
    expect(result).toMatchObject({ total: 50, complete: false });
    expect(result.items).toHaveLength(20);
  });

  it('stops on a short page even if the total grew meanwhile', async () => {
    const fetchPage = (page: number, pageSize: number) =>
      of<Page<number>>({ page, pageSize, total: 500, items: [1, 2] });
    const result = await firstValueFrom(fetchAllPages(fetchPage));
    expect(result).toMatchObject({ items: [1, 2], complete: false });
  });
});

describe('date helpers', () => {
  it('shifts calendar dates across month and year boundaries', () => {
    expect(addDays('2026-03-31', 1)).toBe('2026-04-01');
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    // Unaffected by the DST change on 2026-03-29 in Europe.
    expect(addDays('2026-03-28', 2)).toBe('2026-03-30');
  });

  it('counts the days between two dates', () => {
    expect(daysBetween('2026-05-01', '2026-05-01')).toBe(0);
    expect(daysBetween('2024-01-01', '2025-01-01')).toBe(366);
    expect(daysBetween('2026-05-02', '2026-05-01')).toBe(-1);
  });
});
