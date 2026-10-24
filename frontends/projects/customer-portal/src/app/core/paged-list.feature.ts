import { computed } from '@angular/core';
import { ApiError, Page, toApiError } from '@parcelflow/shared';
import {
  patchState,
  signalStoreFeature,
  withComputed,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import { EMPTY, Observable, catchError, map, pipe, switchMap, tap } from 'rxjs';

/** A page of results, whatever the server paging model (page/pageSize or offset/limit). */
export interface PageSlice<T> {
  items: T[];
  hasNext: boolean;
  /** Total count when the server reports it. */
  total: number | null;
}

export interface PagedListState<T> {
  items: T[];
  /** 1-based page index. */
  page: number;
  pageSize: number;
  total: number | null;
  hasNext: boolean;
  loading: boolean;
  loaded: boolean;
  error: ApiError | null;
}

/** Loads one page; called in an injection context so it can `inject()` API clients. */
export type PageLoaderFactory<T> = () => (
  page: number,
  pageSize: number,
) => Observable<PageSlice<T>>;

/** Adapts a `Page<T>` (page/pageSize/total) response. */
export function fromPage<T>(page: Page<T>): PageSlice<T> {
  return {
    items: page.items,
    total: page.total,
    hasNext: page.page * page.pageSize < page.total,
  };
}

/**
 * Adapts an offset/limit endpoint without a total: one extra item is requested
 * so "next" is only offered when another page really exists.
 */
export function fromOffsetLimit<T>(
  fetch: (offset: number, limit: number) => Observable<T[]>,
): (page: number, pageSize: number) => Observable<PageSlice<T>> {
  return (page, pageSize) =>
    fetch((page - 1) * pageSize, pageSize + 1).pipe(
      map((items) => ({
        items: items.slice(0, pageSize),
        hasNext: items.length > pageSize,
        total: null,
      })),
    );
}

/**
 * Reusable signal store feature for paged lists: loading/error state,
 * previous/next navigation and cancellation of superseded requests.
 */
export function withPagedList<T>(pageSize: number, loaderFactory: PageLoaderFactory<T>) {
  return signalStoreFeature(
    withState<PagedListState<T>>({
      items: [],
      page: 1,
      pageSize,
      total: null,
      hasNext: false,
      loading: false,
      loaded: false,
      error: null,
    }),
    withComputed(({ page, items, loaded, error, loading }) => ({
      hasPrevious: computed(() => page() > 1),
      empty: computed(() => loaded() && !loading() && !error() && items().length === 0),
    })),
    withMethods((store) => {
      const fetchPage = loaderFactory();
      const loadPage = rxMethod<number>(
        pipe(
          tap((page) => patchState(store, { page, loading: true, error: null })),
          switchMap((page) =>
            fetchPage(page, store.pageSize()).pipe(
              tap((slice) =>
                patchState(store, {
                  items: slice.items,
                  hasNext: slice.hasNext,
                  total: slice.total,
                  loading: false,
                  loaded: true,
                }),
              ),
              catchError((error: unknown) => {
                patchState(store, {
                  items: [],
                  hasNext: false,
                  loading: false,
                  loaded: true,
                  error: toApiError(error),
                });
                return EMPTY;
              }),
            ),
          ),
        ),
      );
      return {
        loadPage,
        reload: () => loadPage(store.page()),
        next: () => {
          if (store.hasNext()) {
            loadPage(store.page() + 1);
          }
        },
        previous: () => {
          if (store.page() > 1) {
            loadPage(store.page() - 1);
          }
        },
      };
    }),
  );
}
