import { EMPTY, Observable, defer, expand, map, reduce } from 'rxjs';
import { Page } from '../api/models';

/** Largest page size accepted by the paginated ParcelFlow endpoints (validators cap it at 100). */
export const MAX_PAGE_SIZE = 100;
/** Safety bound on the number of pages `fetchAllPages` requests (5,000 items at the max size). */
export const MAX_FETCH_PAGES = 50;

/** Items gathered across pages; `complete` is false when the page bound was hit first. */
export interface AllPages<T> {
  items: T[];
  total: number;
  complete: boolean;
}

export interface FetchAllPagesOptions {
  pageSize?: number;
  maxPages?: number;
}

/**
 * Requests pages sequentially (1, 2, ...) until `total` items are loaded, a short or empty
 * page arrives, or `maxPages` is reached. Use it when a lookup, a dropdown or a count must
 * cover the whole collection and the endpoint offers no dedicated filter.
 */
export function fetchAllPages<T>(
  fetchPage: (page: number, pageSize: number) => Observable<Page<T>>,
  { pageSize = MAX_PAGE_SIZE, maxPages = MAX_FETCH_PAGES }: FetchAllPagesOptions = {},
): Observable<AllPages<T>> {
  return defer(() => {
    let page = 1;
    let loaded = 0;
    return fetchPage(page, pageSize).pipe(
      expand((current) => {
        loaded += current.items.length;
        const more = loaded < current.total && current.items.length === pageSize;
        if (!more || page >= maxPages) {
          return EMPTY;
        }
        page += 1;
        return fetchPage(page, pageSize);
      }),
      reduce<Page<T>, { items: T[]; total: number }>(
        (all, current) => ({ items: all.items.concat(current.items), total: current.total }),
        { items: [], total: 0 },
      ),
      map((all) => ({ ...all, complete: all.items.length >= all.total })),
    );
  });
}
