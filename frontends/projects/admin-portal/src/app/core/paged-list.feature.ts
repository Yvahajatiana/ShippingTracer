import { computed } from '@angular/core';
import {
  patchState,
  signalStoreFeature,
  withComputed,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { Page } from '@parcelflow/shared';
import { EMPTY, Observable, catchError, map, pipe, switchMap, tap } from 'rxjs';

/** Loads one page of items for a query; called with 1-based page numbers. */
export type PageLoader<T, Q> = (query: Q, page: number, pageSize: number) => Observable<Page<T>>;

export interface PagedListState<T, Q> {
  items: T[];
  query: Q;
  page: number;
  pageSize: number;
  total: number;
  /** False when the backend only pages by offset/limit and the total is a lower bound. */
  totalKnown: boolean;
  loading: boolean;
  error: unknown;
}

export interface PagedListOptions<Q> {
  pageSize?: number;
  query: Q;
  totalKnown?: boolean;
}

interface PageRequest<Q> {
  query: Q;
  page: number;
}

/**
 * Reusable signal-store feature for server-paged lists: loading flag, error, page navigation,
 * optional query (filters) and local item replacement after a mutation.
 * `loaderFactory` runs in an injection context so it can `inject()` API clients.
 */
export function withPagedList<T, Q>(
  loaderFactory: () => PageLoader<T, Q>,
  options: PagedListOptions<Q>,
) {
  const initial: PagedListState<T, Q> = {
    items: [],
    query: options.query,
    page: 1,
    pageSize: options.pageSize ?? 20,
    total: 0,
    totalKnown: options.totalKnown ?? true,
    loading: false,
    error: null,
  };
  return signalStoreFeature(
    withState<PagedListState<T, Q>>(initial),
    withComputed(({ page, pageSize, total }) => ({
      pageCount: computed(() => Math.max(1, Math.ceil(total() / pageSize()))),
      hasPrevious: computed(() => page() > 1),
      hasNext: computed(() => page() * pageSize() < total()),
    })),
    withMethods((store) => {
      const loader = loaderFactory();
      const fetch = rxMethod<PageRequest<Q>>(
        pipe(
          tap(({ query, page }) => patchState(store, { query, page, loading: true, error: null })),
          switchMap(({ query, page }) =>
            loader(query, page, store.pageSize()).pipe(
              tap((result) =>
                patchState(store, {
                  items: result.items,
                  total: result.total,
                  page: result.page,
                  loading: false,
                }),
              ),
              catchError((error: unknown) => {
                patchState(store, { loading: false, error });
                return EMPTY;
              }),
            ),
          ),
        ),
      );
      return {
        /** Loads the given page with the current query. */
        loadPage(page: number): void {
          fetch({ query: store.query(), page: Math.max(1, page) });
        },
        /** Applies a new query and goes back to the first page. */
        search(query: Q): void {
          fetch({ query, page: 1 });
        },
        reload(): void {
          fetch({ query: store.query(), page: store.page() });
        },
        nextPage(): void {
          fetch({ query: store.query(), page: store.page() + 1 });
        },
        previousPage(): void {
          fetch({ query: store.query(), page: Math.max(1, store.page() - 1) });
        },
        /** Replaces an item in the loaded page after a successful update. */
        replaceItem(matches: (item: T) => boolean, updated: T): void {
          patchState(store, {
            items: store.items().map((item) => (matches(item) ? updated : item)),
          });
        },
      };
    }),
  );
}

/** Adapts an offset/limit endpoint returning arrays to a Page by requesting one extra item. */
export function offsetPage<T>(
  fetch: (offset: number, limit: number) => Observable<T[]>,
  page: number,
  pageSize: number,
): Observable<Page<T>> {
  const offset = (page - 1) * pageSize;
  return fetch(offset, pageSize + 1).pipe(
    map((items) => ({
      page,
      pageSize,
      // Lower bound: one extra item means at least one more page exists.
      total: offset + items.length,
      items: items.slice(0, pageSize),
    })),
  );
}
