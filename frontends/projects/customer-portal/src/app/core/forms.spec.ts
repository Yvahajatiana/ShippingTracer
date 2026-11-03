import { FormControl, FormGroup } from '@angular/forms';
import { LIMITS, volumetricWeightKg, volumetricWeightValidator } from './forms';

describe('parcel limits', () => {
  const parcel = (lengthCm: number, widthCm: number, heightCm: number) =>
    new FormGroup(
      {
        lengthCm: new FormControl(lengthCm),
        widthCm: new FormControl(widthCm),
        heightCm: new FormControl(heightCm),
      },
      { validators: volumetricWeightValidator },
    );

  it('mirrors the Shipment dimension limit', () => {
    expect(LIMITS.maxDimensionCm).toBe(200);
  });

  it('computes the volumetric weight like the Shipment service', () => {
    expect(volumetricWeightKg(180, 10, 10)).toBe(3.6);
  });

  it('accepts a long but light parcel within the volumetric limit', () => {
    expect(parcel(180, 10, 10).valid).toBe(true);
  });

  it('accepts a parcel exactly at the volumetric limit', () => {
    // 50 × 50 × 60 / 5000 = 30 kg.
    expect(parcel(50, 50, 60).valid).toBe(true);
  });

  it('rejects a parcel above the volumetric limit', () => {
    const group = parcel(100, 50, 40);

    expect(group.hasError('volumetricWeight')).toBe(true);
    expect(group.getError('volumetricWeight')).toEqual({ max: 30 });
  });

  it('ignores incomplete dimensions, left to the field validators', () => {
    expect(parcel(200, 200, 0).valid).toBe(true);
  });
});
