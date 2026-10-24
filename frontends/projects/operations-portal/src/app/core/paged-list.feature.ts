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

/** One page of results, normalized for both page/pageSize and offset/limit endpoints. */
export interface PageResult<T> {
  items: T[];
  /** Total item count when the endpoint reports it, otherwise null. */
  total: number | null;
  hasMore: boolean;
}

/** Loads a 1-based page; `scope` carries an optional parent id (e.g. a warehouse). */
export type PageFetcher<T> = (
  page: number,
  pageSize: number,
  scope: string | null,
) => Observable<PageResult<T>>;

export interface PagedListState<T> {
  items: T[];
  page: number;
  pageSize: number;
  total: number | null;
  hasMore: boolean;
  scope: string | null;
  loading: boolean;
  loaded: boolean;
  error: unknown;
}

/** Adapts a page/pageSize endpoint returning `Page<T>`. */
export function fromPage<T>(source: Observable<Page<T>>): Observable<PageResult<T>> {
  return source.pipe(
    map((page) => ({
      items: page.items,
      total: page.total,
      hasMore: page.page * page.pageSize < page.total,
    })),
  );
}

/**
 * Adapts an offset/limit endpoint without total: one extra item is requested
 * to know whether a next page exists.
 */
export function sliceFetcher<T>(
  fetch: (offset: number, limit: number) => Observable<T[]>,
): PageFetcher<T> {
  return (page, pageSize) =>
    fetch((page - 1) * pageSize, pageSize + 1).pipe(
      map((items) => ({
        items: items.slice(0, pageSize),
        total: null,
        hasMore: items.length > pageSize,
      })),
    );
}

export function initialPagedListState<T>(pageSize: number): PagedListState<T> {
  return {
    items: [],
    page: 1,
    pageSize,
    total: null,
    hasMore: false,
    scope: null,
    loading: false,
    loaded: false,
    error: null,
  };
}

/**
 * Signal store feature for a paginated, read-only list. The fetcher factory runs in an
 * injection context, so it can `inject()` the API client it needs.
 */
export function withPagedList<T>(fetcherFactory: () => PageFetcher<T>, pageSize = 20) {
  return signalStoreFeature(
    withState<PagedListState<T>>(initialPagedListState<T>(pageSize)),
    withComputed(({ page, pageSize: size, total, items, loaded, loading, error }) => ({
      pageCount: computed(() => {
        const count = total();
        return count === null ? null : Math.max(1, Math.ceil(count / size()));
      }),
      hasPrevious: computed(() => page() > 1),
      isEmpty: computed(() => loaded() && !loading() && !error() && items().length === 0),
    })),
    withMethods((store) => {
      const fetcher = fetcherFactory();
      const loadPage = rxMethod<number>(
        pipe(
          tap((page) => patchState(store, { page, loading: true, error: null })),
          switchMap((page) =>
            fetcher(page, store.pageSize(), store.scope()).pipe(
              tap((result) =>
                patchState(store, {
                  items: result.items,
                  total: result.total,
                  hasMore: result.hasMore,
                  loading: false,
                  loaded: true,
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
        loadPage,
        reload(): void {
          loadPage(store.page());
        },
        next(): void {
          if (store.hasMore() && !store.loading()) {
            loadPage(store.page() + 1);
          }
        },
        previous(): void {
          if (store.page() > 1 && !store.loading()) {
            loadPage(store.page() - 1);
          }
        },
        /** Switches the parent scope (e.g. another warehouse) and reloads the first page. */
        loadScope(scope: string): void {
          patchState(store, { scope, items: [], total: null, hasMore: false, loaded: false });
          loadPage(1);
        },
      };
    }),
  );
}
