import { ChangeDetectionStrategy, Component } from '@angular/core';
import { EmptyState } from '../../shared/ui';

@Component({
  selector: 'bb-not-found',
  imports: [EmptyState],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<div class="container page"><bb-empty icon="search" title="This page went out for delivery and never came back" text="Let's get you something to eat instead." actionLabel="Go to the menu" actionLink="/menu" /></div>`,
})
export class NotFound {}
