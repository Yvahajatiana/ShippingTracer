import { FormControl } from '@angular/forms';
import { notBeforeToday } from './create-route-form.component';

describe('notBeforeToday', () => {
  const validator = notBeforeToday(() => '2026-10-23');

  it('accepts today and future dates', () => {
    expect(validator(new FormControl('2026-10-23'))).toBeNull();
    expect(validator(new FormControl('2026-12-01'))).toBeNull();
    expect(validator(new FormControl(''))).toBeNull();
  });

  it('rejects past dates like the delivery service', () => {
    expect(validator(new FormControl('2026-10-22'))).toEqual({ pastDate: true });
  });
});
