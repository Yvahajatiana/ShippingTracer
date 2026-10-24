import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ShellComponent } from '@parcelflow/shared';
import { NAVIGATION } from './navigation';

@Component({
  selector: 'pf-root',
  imports: [ShellComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<pf-shell titleKey="app.title" [navigation]="navigation" />`,
})
export class App {
  protected readonly navigation = NAVIGATION;
}
