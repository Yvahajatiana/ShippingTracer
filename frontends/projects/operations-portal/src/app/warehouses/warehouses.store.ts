import { computed, inject } from '@angular/core';
import {
  patchState,
  signalStore,
  withComputed,
  withHooks,
  withMethods,
  withState,
} from '@ngrx/signals';
import { rxMethod } from '@ngrx/signals/rxjs-interop';
import {
  Guid,
  IsoDateTime,
  ScanDirection,
  StockParcel,
  Warehouse,
  WarehouseApi,
  newOperationId,
  toApiError,
} from '@parcelflow/shared';
import { EMPTY, catchError, firstValueFrom, pipe, switchMap, tap } from 'rxjs';
import { fromPage, withPagedList } from '../core/paged-list.feature';

export const WAREHOUSE_PAGE_SIZE = 20;
export const STOCK_PAGE_SIZE = 20;
/** Number of scans kept in the session history. */
export const SCAN_HISTORY_SIZE = 30;
/** Tracking numbers accepted by the tracking service. */
export const TRACKING_NUMBER_PATTERN = /^[A-Z0-9]{6,32}$/;

export const WarehouseListStore = signalStore(
  withPagedList<Warehouse>(() => {
    const api = inject(WarehouseApi);
    return (page, pageSize) => fromPage(api.list(page, pageSize));
  }, WAREHOUSE_PAGE_SIZE),
  withHooks({
    onInit(store) {
      store.loadPage(1);
    },
  }),
);

export type ScanOutcome = 'pending' | 'success' | 'error';

export interface ScanRecord {
  scanId: Guid;
  trackingNumber: string;
  direction: ScanDirection;
  outcome: ScanOutcome;
  at: IsoDateTime;
  /** Translation key describing the failure. */
  errorKey: string | null;
  /** Whether resending the same scan (same scanId) makes sense. */
  retryable: boolean;
}

interface WarehouseDetailState {
  warehouse: Warehouse | null;
  warehouseLoading: boolean;
  warehouseError: unknown;
  direction: ScanDirection;
  scans: ScanRecord[];
}

/** Maps a scan failure to an operator-friendly message. */
export function scanErrorKey(status: number, direction: ScanDirection, fallback: string): string {
  if (status === 404) {
    return 'warehouses.scan.errors.notFound';
  }
  if (status === 409) {
    return direction === 'Entry'
      ? 'warehouses.scan.errors.alreadyStored'
      : 'warehouses.scan.errors.notInWarehouse';
  }
  if (status === 400) {
    return 'warehouses.scan.errors.invalid';
  }
  return fallback;
}

/** Warehouse details, its paged stock and the barcode scan session. */
export const WarehouseDetailStore = signalStore(
  withPagedList<StockParcel>(() => {
    const api = inject(WarehouseApi);
    return (page, pageSize, scope) => fromPage(api.stock(scope ?? '', page, pageSize));
  }, STOCK_PAGE_SIZE),
  withState<WarehouseDetailState>({
    warehouse: null,
    warehouseLoading: false,
    warehouseError: null,
    direction: 'Entry',
    scans: [],
  }),
  withComputed(({ warehouse, scans }) => ({
    canScan: computed(() => warehouse()?.isActive === true),
    pendingScans: computed(() => scans().filter((scan) => scan.outcome === 'pending').length),
  })),
  withMethods((store, api = inject(WarehouseApi)) => {
    const loadWarehouse = rxMethod<Guid>(
      pipe(
        tap(() => patchState(store, { warehouseLoading: true, warehouseError: null })),
        switchMap((id) =>
          api.get(id).pipe(
            tap((warehouse) => patchState(store, { warehouse, warehouseLoading: false })),
            catchError((warehouseError: unknown) => {
              patchState(store, { warehouseLoading: false, warehouseError, warehouse: null });
              return EMPTY;
            }),
          ),
        ),
      ),
    );

    const updateScan = (scanId: Guid, changes: Partial<ScanRecord>) =>
      patchState(store, {
        scans: store
          .scans()
          .map((scan) => (scan.scanId === scanId ? { ...scan, ...changes } : scan)),
      });

    const send = async (record: ScanRecord): Promise<ScanOutcome> => {
      const warehouseId = store.scope();
      if (!warehouseId) {
        return 'error';
      }
      try {
        const scan = await firstValueFrom(
          api.scan({
            scanId: record.scanId,
            warehouseId,
            trackingNumber: record.trackingNumber,
            direction: record.direction,
          }),
        );
        updateScan(record.scanId, {
          outcome: 'success',
          at: scan.occurredAtUtc,
          errorKey: null,
          retryable: false,
        });
        store.reload();
        return 'success';
      } catch (error) {
        const apiError = toApiError(error);
        updateScan(record.scanId, {
          outcome: 'error',
          errorKey: scanErrorKey(apiError.status, record.direction, apiError.messageKey),
          // Network and server failures may be resent with the same scanId (idempotent).
          retryable: apiError.status === 0 || apiError.status >= 500,
        });
        return 'error';
      }
    };

    return {
      load(id: Guid): void {
        loadWarehouse(id);
        store.loadScope(id);
      },
      reloadWarehouse(): void {
        const id = store.scope();
        if (id) {
          loadWarehouse(id);
        }
      },
      setDirection(direction: ScanDirection): void {
        patchState(store, { direction });
      },
      /** Sends one scan with a fresh scanId; the tracking number is normalized first. */
      scan(rawTrackingNumber: string): Promise<ScanOutcome> {
        const record: ScanRecord = {
          scanId: newOperationId(),
          trackingNumber: rawTrackingNumber.trim().toUpperCase(),
          direction: store.direction(),
          outcome: 'pending',
          at: new Date().toISOString(),
          errorKey: null,
          retryable: false,
        };
        patchState(store, { scans: [record, ...store.scans()].slice(0, SCAN_HISTORY_SIZE) });
        return send(record);
      },
      /** Resends a failed scan with its original scanId so the server can deduplicate it. */
      retryScan(scanId: Guid): Promise<ScanOutcome> {
        const record = store.scans().find((scan) => scan.scanId === scanId);
        if (!record || record.outcome !== 'error') {
          return Promise.resolve('error');
        }
        updateScan(scanId, { outcome: 'pending', errorKey: null, retryable: false });
        return send(record);
      },
      clearScans(): void {
        patchState(store, { scans: [] });
      },
    };
  }),
);
