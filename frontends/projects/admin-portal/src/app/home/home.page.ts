import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { PageHeaderComponent } from '@parcelflow/shared';

@Component({
  selector: 'pf-home-page',
  imports: [PageHeaderComponent, TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<pf-page-header
    [title]="'home.title' | transloco"
    [subtitle]="'home.subtitle' | transloco"
  />`,
})
export class HomePage {}
